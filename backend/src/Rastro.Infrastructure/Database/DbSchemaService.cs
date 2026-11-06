using Microsoft.Extensions.Logging;
using Rastro.Core.Database;
using Rastro.Extractor.Database;
using Rastro.Infrastructure.Repositories;

namespace Rastro.Infrastructure.Database;

// Esquema do banco de um repositório cadastrado (Fase 37, ADR-006): lê os .cs e os .sql pela única
// porta de disco (RepositoryCatalog) e entrega ao extrator. Refeito a cada pedido, como o grafo: o
// código é a fonte da verdade. Usado pela API (GET api/repositories/{nome}/schema) e pelo MCP (get_db_schema).
// Com a conexão viva cadastrada (Fase 40b, só na API), o catálogo do banco entra como mais uma fonte.
public class DbSchemaService(RepositoryCatalog catalog, ILogger<DbSchemaService> logger, Live.ILiveSchemaSource? live = null)
{
    private static readonly string[] Extensions = [".cs", ".sql"];

    // Um repositório grande não pode travar a leitura: arquivos acima disso ficam de fora, com uma nota.
    public const int MaxFiles = 20_000;
    public const long MaxFileBytes = 2 * 1024 * 1024;

    // Nulo quando o repositório não está cadastrado (ou a pasta sumiu).
    public async Task<DbSchema?> ReadAsync(string repositoryName, CancellationToken ct)
    {
        if (await FilesAsync(repositoryName, ct) is not var (files, skipped)) return null;

        var liveFragment = live is null ? null : await live.ReadAsync(repositoryName, ct);
        var schema = DbSchemaExtractor.Extract(files, liveFragment is null ? null : [liveFragment]);
        logger.LogInformation("Esquema do banco do repositório {Repository} lido: {Tables} tabelas, {Divergences} divergências.",
            repositoryName, schema.Tables.Count, schema.Divergences.Count);
        return skipped.Count == 0 ? schema : schema with { Notes = [.. skipped, .. schema.Notes] };
    }

    // O dialeto do código do repositório (Fase 42b), sem o banco vivo: decide a string de conexão que
    // serve aos dois drivers. Nulo quando o repositório não está cadastrado.
    public async Task<SqlDialect?> DialectAsync(string repositoryName, CancellationToken ct) =>
        await FilesAsync(repositoryName, ct) is var (files, _) ? DbSchemaExtractor.DialectOf(files) : null;

    private async Task<(List<SchemaFile> Files, List<SchemaNote> Skipped)?> FilesAsync(string repositoryName, CancellationToken ct)
    {
        var root = catalog.RootOf(repositoryName);
        var paths = catalog.SourceFiles(repositoryName, Extensions, MaxFiles + 1);
        if (root is null || paths is null) return null;

        var files = new List<SchemaFile>(paths.Count);
        var skipped = new List<SchemaNote>();
        if (paths.Count > MaxFiles)
            skipped.Add(new SchemaNote($"O repositório tem mais de {MaxFiles} arquivos .cs e .sql: o esquema saiu dos primeiros, em ordem de caminho.", string.Empty, 0));

        foreach (var path in paths.Take(MaxFiles))
        {
            ct.ThrowIfCancellationRequested();
            var full = Path.Combine(root, path);
            try
            {
                if (new FileInfo(full).Length > MaxFileBytes)
                {
                    if (path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                        skipped.Add(new SchemaNote("Arquivo .sql grande demais (mais de 2 MB, provavelmente carga de dados): ficou de fora.", path, 1));
                    continue;
                }
                files.Add(new SchemaFile(path, await File.ReadAllTextAsync(full, ct)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Arquivo {File} do repositório {Repository} não pôde ser lido para o esquema do banco.", path, repositoryName);
                skipped.Add(new SchemaNote("O arquivo não pôde ser lido (em uso ou sem permissão).", path, 1));
            }
        }

        return (files, skipped);
    }
}
