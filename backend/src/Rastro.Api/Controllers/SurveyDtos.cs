using Rastro.Core.Graph;
using Rastro.Core.Surveys;

namespace Rastro.Api.Controllers;

public record CreateSurveyRequest(string? Repository, string? SolutionPath, string? EntryPoint, int? MaxDepth);

public record SurveySummary(
    Guid Id,
    string Repository,
    string SolutionPath,
    string EntryPoint,
    int MaxDepth,
    string? CommitHash,
    string? Branch,
    SurveyStatus Status,
    string? Error,
    int SchemaVersion,
    int NodeCount,
    int EdgeCount,
    int UnresolvedCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt)
{
    public static SurveySummary From(Survey s) => new(
        s.Id, s.Repository, s.SolutionPath, s.EntryPoint, s.MaxDepth, s.CommitHash, s.Branch, s.Status, s.Error,
        s.SchemaVersion, s.NodeCount, s.EdgeCount, s.UnresolvedCount, s.CreatedAt, s.StartedAt, s.CompletedAt);
}

public record SurveyDetail(SurveySummary Survey, FlowGraph? Graph);

public record SurveyStaleness(bool Stale, string? SurveyCommit, string? CurrentCommit);

// Path é a pasta cadastrada (ADR-003); os comandos de build e testes não saem, só se existem.
public record RepositoryItem(string Name, string Path, bool HasGit, bool HasBuildCommand, bool HasTestCommand);

public record AddRepositoryRequest(string? Name, string? Path);

public record ChangePrompt(string Prompt, int Changes);

public record RefactorPrompt(string Prompt);

public record C4Export(string Dsl, IReadOnlyList<Rastro.Core.Architecture.C4Person> People);

public record EdgeDecisionRequest(string? From, string? To, bool Confirm);

// Anotação do usuário num passo (Fase 12). Texto vazio no PUT apaga.
public record NodeNoteView(string NodeId, string Text, DateTimeOffset UpdatedAt);

public record NodeNoteInput(string? NodeId, string? Text);

// Regra cruzada entre fluxos (Fase 13): este fluxo grava a entidade sem a guarda que outros usam.
public record CrossFlowView(string NodeId, string Label, string Entity, string Guard, IReadOnlyList<string> Flows);

public record SourceSnippet(string File, int StartLine, int HighlightLine, IReadOnlyList<string> Lines);

// Códigos de erro estáveis (o front traduz pelo código, não pela mensagem).
public static class ErrorCodes
{
    public const string RepositoryNotAllowed = "REPOSITORY_NOT_ALLOWED";
    public const string SolutionPathInvalid = "SOLUTION_PATH_INVALID";
    public const string SolutionNotFound = "SOLUTION_NOT_FOUND";
    public const string EntryPointRequired = "ENTRY_POINT_REQUIRED";
    public const string DepthOutOfRange = "DEPTH_OUT_OF_RANGE";
    public const string SourcePathInvalid = "SOURCE_PATH_INVALID";
    public const string EdgeNotProposed = "EDGE_NOT_PROPOSED";
    public const string DraftInvalid = "DRAFT_INVALID";
    public const string DraftRequired = "DRAFT_REQUIRED";
    public const string C4PeopleInvalid = "C4_PEOPLE_INVALID";
    public const string ValidationRunning = "VALIDATION_RUNNING";
    public const string ValidationNotApprovable = "VALIDATION_NOT_APPROVABLE";
    public const string NoteInvalid = "NOTE_INVALID";
    public const string SurveyRunning = "SURVEY_RUNNING";
    public const string RepositoryNameInvalid = "REPOSITORY_NAME_INVALID";
    public const string RepositoryPathInvalid = "REPOSITORY_PATH_INVALID";
    public const string RepositoryFolderNotFound = "REPOSITORY_FOLDER_NOT_FOUND";
    public const string RepositoryExists = "REPOSITORY_EXISTS";
    public const string RepositoryNotFound = "REPOSITORY_NOT_FOUND";
    public const string OriginNotAllowed = "ORIGIN_NOT_ALLOWED";
}
