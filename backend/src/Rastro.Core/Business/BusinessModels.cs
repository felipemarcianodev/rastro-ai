namespace Rastro.Core.Business;

// Tipo de negócio de um passo-chave. Valores explícitos: viajam como int no JSON.
public enum BusinessKind
{
    Start = 0,
    Decision = 1,
    Notification = 2,
    Rejection = 3,
    CrossSystem = 4,
    Step = 5,
    End = 6
}

// Quem escreveu o texto do passo. Na derivação por regra (Fase 55) é sempre Rule.
public enum LabelOrigin
{
    Rule = 0,
    Ai = 1,
    Agent = 2,
    User = 3
}

// Proposto até o usuário confirmar. A regra nunca marca como confirmado.
public enum LabelState
{
    Proposed = 0,
    Confirmed = 1
}

// Repository: o repositório cadastrado do passo (o do levantamento ou o do trecho).
// File é relativo ao repositório, com "/".
public sealed record CodeRef(string Repository, string File, int Line, string? CommitHash, string? Branch);

// Id: estável, os 16 primeiros caracteres hexadecimais (minúsculos) do SHA-256 de "{surveyId:N}|{nodeId}".
// SourceNodeIds: o nó do passo e os nós de código agrupados nele.
public sealed record BusinessStep(
    string Id, string NodeId, BusinessKind Kind, string Label,
    LabelOrigin LabelOrigin, LabelState LabelState, CodeRef? Ref,
    IReadOnlyList<string> SourceNodeIds, bool Inferred);

// From e To são Ids de BusinessStep (não ids de nó).
public sealed record BusinessEdge(string From, string To, string? Label, bool Inferred);

public sealed record BusinessFlow(
    Guid SurveyId, int SchemaVersion,
    IReadOnlyList<BusinessStep> Steps, IReadOnlyList<BusinessEdge> Edges,
    int CollapsedDecisions);

// De onde vem a referência de cada passo: o levantamento (primeiro trecho, FlowNode.System nulo) e os trechos.
public sealed record BusinessSource(Guid SurveyId, string Repository, string? CommitHash, string? Branch,
    IReadOnlyList<BusinessSegmentSource> Segments);

public sealed record BusinessSegmentSource(string Repository, string? CommitHash, string? Branch);

public sealed record BusinessViewOptions(
    IReadOnlyList<string> NotificationPatterns,
    IReadOnlyList<string> RejectionReturnTypes,
    int MaxStepsPerFlow)
{
    public const int DefaultMaxStepsPerFlow = 25;

    // Padrões da seção 6.2 da proposta: não citam produto nem cliente.
    public static BusinessViewOptions Default { get; } = new(
        [
            "*Email*Service.Send*",
            "*Mail*.Send*",
            "*Notification*.Register*",
            "*Notification*.Send*",
            "*Notifier*.Notify*",
            "*SmtpClient.Send*",
            "*IHubContext*.Send*"
        ],
        ["BadRequest", "UnprocessableEntity", "NotFound", "Forbid", "Conflict", "Problem"],
        DefaultMaxStepsPerFlow);
}
