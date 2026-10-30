using System.Text.Json;
using Rastro.Core.Http;

namespace Rastro.Core.Surveys;

// Índice de rotas de um repositório num commit (Fase 25), guardado para não reler todos os .cs a
// cada levantamento. A chave é repositório + commit: mudou o commit, o índice é refeito.
public class RouteIndex
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Repository { get; private set; } = string.Empty;
    public string CommitHash { get; private set; } = string.Empty;
    public string EntriesJson { get; private set; } = "[]";
    public DateTimeOffset CreatedAt { get; private set; }

    private RouteIndex() { }

    public static RouteIndex Create(string repository, string commitHash, IReadOnlyList<RouteEntry> entries, DateTimeOffset now) => new()
    {
        Repository = repository,
        CommitHash = commitHash,
        EntriesJson = JsonSerializer.Serialize(entries, Json),
        CreatedAt = now
    };

    public IReadOnlyList<RouteEntry> ReadEntries() =>
        JsonSerializer.Deserialize<List<RouteEntry>>(EntriesJson, Json) ?? [];
}
