using Rastro.Core.Graph;

namespace Rastro.Core.Surveys;

// Rascunho de edição de um levantamento (um por levantamento): o fluxo como o usuário quer que
// fique. Guardado à parte; o grafo extraído do levantamento não muda.
public class SurveyDraft
{
    public Guid SurveyId { get; private set; }
    public string GraphJson { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }

    private SurveyDraft() { }

    public static SurveyDraft Create(Guid surveyId, FlowGraph graph, DateTimeOffset now) =>
        new() { SurveyId = surveyId, GraphJson = FlowGraphJson.Serialize(graph), UpdatedAt = now };

    public void Update(FlowGraph graph, DateTimeOffset now)
    {
        GraphJson = FlowGraphJson.Serialize(graph);
        UpdatedAt = now;
    }

    public FlowGraph ReadGraph() => FlowGraphJson.Deserialize(GraphJson);
}
