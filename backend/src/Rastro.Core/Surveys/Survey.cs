using Rastro.Core.Graph;

namespace Rastro.Core.Surveys;

public enum SurveyStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3
}

// Levantamento: o grafo de um fluxo num commit de um repositório.
// O grafo fica em JSON (FlowGraphJson) para o formato ser o mesmo da CLI e do MCP.
public class Survey
{
    public Guid Id { get; private set; }

    // Nome do repositório na lista de permitidos (nunca um caminho vindo da requisição).
    public string Repository { get; private set; } = string.Empty;

    // Caminho da solution relativo à raiz do repositório, com "/".
    public string SolutionPath { get; private set; } = string.Empty;

    public string EntryPoint { get; private set; } = string.Empty;
    public int MaxDepth { get; private set; }
    public string? CommitHash { get; private set; }
    public string? Branch { get; private set; }
    public SurveyStatus Status { get; private set; }
    public string? Error { get; private set; }
    public int SchemaVersion { get; private set; }
    public string? GraphJson { get; private set; }
    public int NodeCount { get; private set; }
    public int EdgeCount { get; private set; }
    public int UnresolvedCount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    private Survey() { }

    public static Survey Create(string repository, string solutionPath, string entryPoint, int maxDepth, string? commitHash, string? branch, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Repository = repository,
        SolutionPath = solutionPath,
        EntryPoint = entryPoint,
        MaxDepth = maxDepth,
        CommitHash = commitHash,
        Branch = branch,
        Status = SurveyStatus.Pending,
        SchemaVersion = RastroSchema.Version,
        CreatedAt = now
    };

    public void Start(DateTimeOffset now)
    {
        Status = SurveyStatus.Running;
        StartedAt = now;
        Error = null;
    }

    public void Complete(FlowGraph graph, DateTimeOffset now)
    {
        Status = SurveyStatus.Completed;
        GraphJson = FlowGraphJson.Serialize(graph);
        SchemaVersion = graph.SchemaVersion;
        NodeCount = graph.Nodes.Count;
        EdgeCount = graph.Edges.Count;
        UnresolvedCount = graph.Nodes.Count(n => n.Kind == NodeKind.Unresolved && graph.Edges.All(e => e.From != n.Id));
        CompletedAt = now;
    }

    // Grafo alterado depois da análise (proposta aceita, decisão do usuário, regra escrita).
    public void ReplaceGraph(FlowGraph graph)
    {
        if (Status != SurveyStatus.Completed)
            throw new InvalidOperationException("Só levantamento concluído tem grafo para alterar.");

        GraphJson = FlowGraphJson.Serialize(graph);
        NodeCount = graph.Nodes.Count;
        EdgeCount = graph.Edges.Count;
        UnresolvedCount = graph.Nodes.Count(n => n.Kind == NodeKind.Unresolved && graph.Edges.All(e => e.From != n.Id));
    }

    public void Fail(string error, DateTimeOffset now)
    {
        Status = SurveyStatus.Failed;
        Error = error;
        CompletedAt = now;
    }

    public FlowGraph? ReadGraph() => GraphJson is null ? null : FlowGraphJson.Deserialize(GraphJson);
}
