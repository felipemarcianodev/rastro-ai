namespace Rastro.Core.Business;

// Texto gravado de um passo da visão de negócio (Fase 59). Chave (levantamento, chave do nó). Quem escreveu e se
// já foi confirmado pelo usuário ficam gravados; o hash é do código do passo no momento da gravação, para o
// "o código mudou desde a confirmação". Só o usuário confirma (ADR-001).
public class BusinessLabel
{
    public Guid SurveyId { get; private set; }
    public string NodeId { get; private set; } = string.Empty;
    public string CodeHash { get; private set; } = string.Empty;
    public string Text { get; private set; } = string.Empty;
    public LabelOrigin Origin { get; private set; }
    public LabelState State { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private BusinessLabel() { }

    public static BusinessLabel Create(Guid surveyId, string nodeId, string codeHash, string text, LabelOrigin origin, LabelState state, DateTimeOffset now) =>
        new() { SurveyId = surveyId, NodeId = nodeId, CodeHash = codeHash, Text = text, Origin = origin, State = state, UpdatedAt = now };

    public void Set(string codeHash, string text, LabelOrigin origin, LabelState state, DateTimeOffset now)
    {
        CodeHash = codeHash;
        Text = text;
        Origin = origin;
        State = state;
        UpdatedAt = now;
    }
}

// Tipo de negócio trocado de um passo (Fase 59). Origem User (já confirmado) ou Agent (proposta, até o usuário
// confirmar). Chave (levantamento, chave do nó). Reason é o motivo curto do agente.
public class BusinessKindOverride
{
    public Guid SurveyId { get; private set; }
    public string NodeId { get; private set; } = string.Empty;
    public BusinessKind Kind { get; private set; }
    public LabelOrigin Origin { get; private set; }
    public LabelState State { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }

    private BusinessKindOverride() { }

    public static BusinessKindOverride Create(Guid surveyId, string nodeId, BusinessKind kind, LabelOrigin origin, LabelState state, string reason, DateTimeOffset now) =>
        new() { SurveyId = surveyId, NodeId = nodeId, Kind = kind, Origin = origin, State = state, Reason = reason, UpdatedAt = now };

    public void Set(BusinessKind kind, LabelOrigin origin, LabelState state, string reason, DateTimeOffset now)
    {
        Kind = kind;
        Origin = origin;
        State = state;
        Reason = reason;
        UpdatedAt = now;
    }
}

// Proposta de troca de tipo do agente, que viaja no passo até o usuário confirmar ou descartar.
public sealed record KindProposal(BusinessKind Kind, string Reason);

// O que está gravado e entra na derivação (a regra continua pura): tipos trocados e confirmados, por chave do nó,
// e propostas de tipo do agente, que só aparecem no passo.
public sealed record BusinessOverlay(
    IReadOnlyDictionary<string, BusinessKind> KindOverrides,
    IReadOnlyDictionary<string, KindProposal> KindProposals)
{
    public static BusinessOverlay Empty { get; } = new(
        new Dictionary<string, BusinessKind>(StringComparer.Ordinal),
        new Dictionary<string, KindProposal>(StringComparer.Ordinal));
}

// Limites do que o usuário e o agente escrevem.
public static class BusinessLabelLimits
{
    public const int MaxLabelLength = BusinessFlowBuilder.MaxLabelLength;
    public const int MaxReasonLength = 300;

    // Uma linha, sem quebra de linha nem caractere de controle. Devolve o texto aparado ou nulo se não vale.
    public static string? CleanLabel(string? text)
    {
        var clean = (text ?? string.Empty).Trim();
        if (clean.Length > MaxLabelLength || clean.Any(char.IsControl)) return null;
        return clean;
    }

    public static string? CleanReason(string? text)
    {
        var clean = (text ?? string.Empty).Trim();
        if (clean.Length == 0 || clean.Length > MaxReasonLength || clean.Any(char.IsControl)) return null;
        return clean;
    }
}
