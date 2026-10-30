namespace Rastro.Core.Surveys;

// Trecho de um levantamento que atravessa sistemas (Fase 25): o repositório de destino de uma chamada
// HTTP, a solution aberta nele, o método de entrada do trecho e o commit em que foi lido. O primeiro
// trecho é o próprio Survey; aqui ficam os demais (Order 1, 2, ...).
public class SurveySegment
{
    public Guid Id { get; private set; }
    public Guid SurveyId { get; private set; }
    public int Order { get; private set; }
    public string Repository { get; private set; } = string.Empty;

    // Caminho da solution relativo à raiz do repositório, com "/".
    public string SolutionPath { get; private set; } = string.Empty;

    public string EntryPoint { get; private set; } = string.Empty;
    public string? CommitHash { get; private set; }
    public string? Branch { get; private set; }

    private SurveySegment() { }

    public static SurveySegment Create(Guid surveyId, int order, string repository, string solutionPath, string entryPoint, string? commitHash, string? branch) => new()
    {
        Id = Guid.NewGuid(),
        SurveyId = surveyId,
        Order = order,
        Repository = repository,
        SolutionPath = solutionPath,
        EntryPoint = entryPoint,
        CommitHash = commitHash,
        Branch = branch
    };
}
