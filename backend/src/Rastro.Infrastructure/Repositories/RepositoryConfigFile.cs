using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Rastro.Infrastructure.Repositories;

public enum RepositoryConfigOutcome
{
    Added = 0,
    Removed = 1,
    InvalidName = 2,
    FolderNotFound = 3,
    Exists = 4,
    NotFound = 5,
    Updated = 6
}

// Path é a pasta canonicalizada (preenchida em Added e FolderNotFound); HasGit só vale em Added.
public record RepositoryConfigResult(RepositoryConfigOutcome Outcome, string? Path = null, bool HasGit = false)
{
    public bool Succeeded => Outcome is RepositoryConfigOutcome.Added or RepositoryConfigOutcome.Removed or RepositoryConfigOutcome.Updated;
}

// Único lugar que lê e grava o config.json do Rastro (ADR-003): a CLI e a API usam o mesmo serviço.
// Mesmo formato do appsettings ({ "Rastro": { "Repositories": [...] } }).
public sealed partial class RepositoryConfigFile(string configPath)
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    // Um processo só grava por vez (a CLI e a API são processos diferentes, mas cada um tem o seu serviço).
    private readonly object _gate = new();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$")]
    private static partial Regex ValidName();

    public static bool IsValidName(string? name) => name is not null && ValidName().IsMatch(name);

    // Cadastro sem comandos; os comandos entram depois, pela edição (ADR-008) ou já no "rastro repo add".
    public RepositoryConfigResult Add(string name, string folder) => AddWithCommands(name, folder, null, null);

    public RepositoryConfigResult AddWithCommands(string name, string folder, string? buildCommand, string? testCommand)
    {
        if (!IsValidName(name)) return new(RepositoryConfigOutcome.InvalidName);

        var path = Path.GetFullPath(folder);
        if (!Directory.Exists(path)) return new(RepositoryConfigOutcome.FolderNotFound, path);

        lock (_gate)
        {
            var root = Load();
            var repositories = Repositories(root);
            if (Find(repositories, name) is not null) return new(RepositoryConfigOutcome.Exists, path);

            var entry = new JsonObject { ["Name"] = name, ["Path"] = path };
            if (buildCommand is not null) entry["BuildCommand"] = buildCommand;
            if (testCommand is not null) entry["TestCommand"] = testCommand;
            repositories.Add(entry);
            Save(root);

            return new(RepositoryConfigOutcome.Added, path, Directory.Exists(Path.Combine(path, ".git")));
        }
    }

    public RepositoryConfigResult Remove(string name)
    {
        lock (_gate)
        {
            var root = Load();
            var repositories = Repositories(root);
            if (Find(repositories, name) is not { } entry) return new(RepositoryConfigOutcome.NotFound);

            repositories.Remove(entry);
            Save(root);
            return new(RepositoryConfigOutcome.Removed);
        }
    }

    // Troca o comando de build e o de testes da validação (ADR-008). Nulo ou vazio tira o comando da entrada.
    // Quem chama confere o texto antes (IsValidCommand): o comando roda sem shell, pelo ProcessRunner.
    public RepositoryConfigResult SetCommands(string name, string? buildCommand, string? testCommand)
    {
        lock (_gate)
        {
            var root = Load();
            var repositories = Repositories(root);
            if (Find(repositories, name) is not JsonObject entry) return new(RepositoryConfigOutcome.NotFound);

            SetOrRemove(entry, "BuildCommand", buildCommand);
            SetOrRemove(entry, "TestCommand", testCommand);
            Save(root);
            return new(RepositoryConfigOutcome.Updated, (string?)entry["Path"]);
        }
    }

    public const int MaxCommandLength = 500;

    // Uma linha só, sem caractere de controle e com até 500 caracteres. Nulo ou vazio vale (tira o comando).
    public static bool IsValidCommand(string? command) =>
        string.IsNullOrWhiteSpace(command) || (command.Length <= MaxCommandLength && !command.Any(char.IsControl));

    private static void SetOrRemove(JsonObject entry, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) entry.Remove(key);
        else entry[key] = value.Trim();
    }

    // Nome e pasta de cada entrada gravada (para a "rastro repo list").
    public IReadOnlyList<(string Name, string Path)> List()
    {
        lock (_gate)
        {
            return Repositories(Load()).OfType<JsonObject>()
                .Select(e => ((string?)e["Name"] ?? string.Empty, (string?)e["Path"] ?? string.Empty))
                .ToList();
        }
    }

    private JsonObject Load()
    {
        if (!File.Exists(configPath)) return [];
        try
        {
            return JsonNode.Parse(File.ReadAllText(configPath)) as JsonObject ?? [];
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"O arquivo {configPath} não é um JSON válido: {ex.Message}", ex);
        }
    }

    private static JsonArray Repositories(JsonObject root)
    {
        if (root["Rastro"] is not JsonObject rastro) root["Rastro"] = rastro = [];
        if (rastro["Repositories"] is not JsonArray repositories) rastro["Repositories"] = repositories = [];
        return repositories;
    }

    private static JsonNode? Find(JsonArray repositories, string name) =>
        repositories.FirstOrDefault(r => string.Equals((string?)r?["Name"], name, StringComparison.OrdinalIgnoreCase));

    private void Save(JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(configPath))!);
        // Grava num arquivo ao lado e troca de uma vez: quem recarrega o config.json (file watcher da API,
        // outro processo) nunca pega o arquivo truncado ou pela metade, que esvaziaria a lista de repositórios.
        var temp = configPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, root.ToJsonString(Indented));
            File.Move(temp, configPath, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }
    }
}
