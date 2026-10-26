using Rastro.Core.Graph;

namespace Rastro.Core.Surveys;

public enum ProposalKind
{
    // Destino de uma chamada não resolvida, com prova (arquivo:linha).
    Edge = 0,

    // Regra do nó em linguagem humana.
    Rule = 1
}

public enum ProposalStatus
{
    // Passou pelo validador e entrou no grafo como inferida.
    Accepted = 0,

    // O validador recusou (prova não confere, método inexistente, levantamento desatualizado...).
    Rejected = 1,

    // O usuário confirmou.
    Confirmed = 2,

    // O usuário descartou.
    Dismissed = 3
}

// Histórico de tudo o que agentes e provedores propuseram, aceito ou não. Serve de auditoria
// e para ver quanto a IA acerta.
public class SurveyProposal
{
    public Guid Id { get; private set; }
    public Guid SurveyId { get; private set; }
    public ProposalKind Kind { get; private set; }
    public ProposalOrigin Origin { get; private set; }
    public string NodeId { get; private set; } = string.Empty;
    public string? Target { get; private set; }
    public string? TargetNodeId { get; private set; }
    public string? EvidenceFile { get; private set; }
    public string? When { get; private set; }
    public int? EvidenceLine { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public ProposalStatus Status { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }

    private SurveyProposal() { }

    public static SurveyProposal ForEdge(Guid surveyId, ProposalOrigin origin, string nodeId, string target, string evidenceFile, int evidenceLine, string reason, DateTimeOffset now, string? when = null) => new()
    {
        Id = Guid.NewGuid(),
        SurveyId = surveyId,
        Kind = ProposalKind.Edge,
        Origin = origin,
        NodeId = nodeId,
        Target = target,
        EvidenceFile = evidenceFile,
        EvidenceLine = evidenceLine,
        When = when,
        Text = reason,
        CreatedAt = now
    };

    public static SurveyProposal ForRule(Guid surveyId, ProposalOrigin origin, string nodeId, string text, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        SurveyId = surveyId,
        Kind = ProposalKind.Rule,
        Origin = origin,
        NodeId = nodeId,
        Text = text,
        Status = ProposalStatus.Accepted,
        CreatedAt = now
    };

    public void Accept(string targetNodeId) => (Status, TargetNodeId) = (ProposalStatus.Accepted, targetNodeId);

    public void Reject(string reason) => (Status, RejectionReason) = (ProposalStatus.Rejected, reason);

    public void Decide(bool confirm, DateTimeOffset now) =>
        (Status, DecidedAt) = (confirm ? ProposalStatus.Confirmed : ProposalStatus.Dismissed, now);
}
