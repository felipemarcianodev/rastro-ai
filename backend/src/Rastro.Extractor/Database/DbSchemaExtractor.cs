using Rastro.Core.Database;

namespace Rastro.Extractor.Database;

// Esquema do banco pelo código do repositório (Fase 37, ADR-006): modelo do EF Core e migrations pelo
// Roslyn, .sql e projeto de banco pelo leitor de SQL do dialeto, e a fusão com as divergências. Não
// abre conexão, não compila nem executa nada do cliente, e não usa IA (ADR-001).
public static class DbSchemaExtractor
{
    public static DbSchema Extract(IReadOnlyList<SchemaFile> files)
    {
        var ordered = files.OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToList();
        var code = new CodeIndex(ordered.Where(f => f.RelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)).ToList());
        var dialect = code.Calls("UseNpgsql") && !code.Calls("UseSqlServer") ? SqlDialect.PostgreSql : SqlDialect.SqlServer;

        var fragments = new List<SchemaFragment> { new EfModelReader(code, dialect).Read() };
        fragments.AddRange(new EfMigrationReader(code, dialect).Read());

        var scripts = ordered.Where(f => f.RelativePath.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)).ToList();
        if (SqlSchemaReaders.For(dialect) is { } reader)
            fragments.AddRange(scripts.Select(f => reader.Read(f.Text, f.RelativePath, 1, SchemaSourceKind.SqlScript)));
        else if (scripts.Count > 0)
            fragments.Add(SchemaFragment.Empty with
            {
                Notes = [new SchemaNote($"Os arquivos .sql do {dialect} ainda não são lidos ({scripts.Count} arquivo(s)); o leitor entra na Fase 42.", scripts[0].RelativePath, 1)]
            });

        return DbSchemaMerger.Merge(dialect, fragments);
    }
}
