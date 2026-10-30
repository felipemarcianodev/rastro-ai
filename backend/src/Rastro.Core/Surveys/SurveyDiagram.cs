namespace Rastro.Core.Surveys;

// Visão consolidada em Mermaid (Fase 27): o texto do diagrama como o usuário deixou, salvo junto com
// o levantamento. Um registro por levantamento. O commit guarda de qual versão do código o texto veio.
public class SurveyDiagram
{
    public const int MaxLength = 200_000;

    public Guid SurveyId { get; private set; }
    public string Mermaid { get; private set; } = string.Empty;
    public string? CommitHash { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private SurveyDiagram() { }

    public static SurveyDiagram Create(Guid surveyId, string mermaid, string? commitHash, DateTimeOffset now)
    {
        var diagram = new SurveyDiagram { SurveyId = surveyId };
        diagram.Update(mermaid, commitHash, now);
        return diagram;
    }

    public void Update(string mermaid, string? commitHash, DateTimeOffset now)
    {
        Mermaid = mermaid;
        CommitHash = commitHash;
        UpdatedAt = now;
    }

    // Vazio (ou só espaço) e acima do limite são recusados. Devolve o problema para o usuário.
    public static string? Validate(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "O diagrama está vazio.";
        if (text.Length > MaxLength) return $"O diagrama passa de {MaxLength} caracteres.";
        return null;
    }
}
