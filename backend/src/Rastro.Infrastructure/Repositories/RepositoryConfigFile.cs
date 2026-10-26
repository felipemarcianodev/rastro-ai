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
    NotFound = 5
}

// Path é a pasta canonicalizada (preenchida em Added e FolderNotFound); HasGit só vale em Added.
public record RepositoryConfigResult(RepositoryConfigOutcome Outcome, string? Path = null, bool HasGit = false)
{
    public bool Succeeded => Outcome is RepositoryConfigOutcome.Added or RepositoryConfigOutcome.Removed;
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

    // Cadastro sem comandos: é o que a tela pode fazer (build e testes só pelo terminal).
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
        File.WriteAllText(configPath, root.ToJsonString(Indented));
    }
}
