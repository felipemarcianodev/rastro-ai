using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rastro.Core.Graph;
using Rastro.Core.Surveys;
using Rastro.Extractor;
using Rastro.Infrastructure.Analysis;
using Rastro.Infrastructure.Processes;
using Rastro.Infrastructure.Repositories;

namespace Rastro.Infrastructure.Proposals;

public record ProposalOutcome(bool Accepted, string Message, Guid? ProposalId = null);

// Porta única para propostas de agentes (MCP) e provedores de IA (ADR-001): toda proposta
// passa pelas mesmas regras e pelo validador de prova antes de mexer no grafo, e fica
// registrada (aceita ou recusada) em survey_proposals.
public class ProposalService(
    RastroDbContext db,
    RepositoryCatalog catalog,
    IGitInfo git,
    ISolutionSource solutions,
    TimeProvider clock,
    ILogger<ProposalService> logger)
{
    public const int MinRuleLength = 10;
    public const int MaxRuleLength = 2000;
    public const int MaxReasonLength = 1000;
    public const int MaxWhenLength = 200;

    public async Task<ProposalOutcome> ProposeEdgeAsync(
        Guid surveyId, string nodeId, string target, string evidenceFile, int evidenceLine, string reason,
        ProposalOrigin origin, CancellationToken ct, string? when = null)
    {
        var survey = await db.Surveys.FirstOrDefaultAsync(s => s.Id == surveyId, ct);
        if (survey?.ReadGraph() is not { } graph)
            return new ProposalOutcome(false, "Levantamento não encontrado ou ainda sem grafo.");

        var proposal = SurveyProposal.ForEdge(surveyId, origin, nodeId, target, evidenceFile, evidenceLine,
            Truncate(reason, MaxReasonLength), clock.GetUtcNow(), string.IsNullOrWhiteSpace(when) ? null : Truncate(when, MaxWhenLength));
        db.SurveyProposals.Add(proposal);

        var outcome = await EvaluateEdgeAsync(survey, graph, proposal, ct);
        if (!outcome.Accepted) proposal.Reject(outcome.Message);

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Proposta de aresta {ProposalId} no levantamento {SurveyId}: {Status} (origem {Origin}).",
            proposal.Id, surveyId, proposal.Status, origin);
        return outcome with { ProposalId = proposal.Id };
    }

    public async Task<ProposalOutcome> ProposeRuleAsync(Guid surveyId, string nodeId, string text, ProposalOrigin origin, CancellationToken ct)
    {
        var survey = await db.Surveys.FirstOrDefaultAsync(s => s.Id == surveyId, ct);
        if (survey?.ReadGraph() is not { } graph)
            return new ProposalOutcome(false, "Levantamento não encontrado ou ainda sem grafo.");

        var clean = text.Trim();
        if (clean.Length < MinRuleLength || clean.Length > MaxRuleLength)
            return new ProposalOutcome(false, $"A regra precisa ter de {MinRuleLength} a {MaxRuleLength} caracteres.");
        if (graph.Nodes.All(n => n.Id != nodeId))
            return new ProposalOutcome(false, "Nó não encontrado neste levantamento.");

        survey.ReplaceGraph(GraphEditor.WithRule(graph, nodeId, new NodeRule(clean, origin)));
        var proposal = SurveyProposal.ForRule(surveyId, origin, nodeId, clean, clock.GetUtcNow());
        db.SurveyProposals.Add(proposal);
        await db.SaveChangesAsync(ct);
        return new ProposalOutcome(true, "Regra gravada no nó.", proposal.Id);
    }

    // Decisão do usuário sobre uma aresta proposta (confirmar ou descartar).
    public async Task<ProposalOutcome> DecideEdgeAsync(Guid surveyId, string from, string to, bool confirm, CancellationToken ct)
    {
        var survey = await db.Surveys.FirstOrDefaultAsync(s => s.Id == surveyId, ct);
        if (survey?.ReadGraph() is not { } graph)
            return new ProposalOutcome(false, "Levantamento não encontrado ou ainda sem grafo.");

        if (!graph.Edges.Any(e => e.From == from && e.To == to && e.Confidence != EdgeConfidence.Extracted))
            return new ProposalOutcome(false, "Não há aresta proposta entre esses nós.");

        survey.ReplaceGraph(GraphEditor.WithEdgeDecision(graph, from, to, confirm));

        var proposal = await db.SurveyProposals
            .Where(p => p.SurveyId == surveyId && p.Kind == ProposalKind.Edge && p.NodeId == from && p.TargetNodeId == to
                        && p.Status == ProposalStatus.Accepted)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(ct);
        proposal?.Decide(confirm, clock.GetUtcNow());

        await db.SaveChangesAsync(ct);
        return new ProposalOutcome(true, confirm ? "Aresta confirmada." : "Aresta descartada.", proposal?.Id);
    }

    private async Task<ProposalOutcome> EvaluateEdgeAsync(Survey survey, FlowGraph graph, SurveyProposal proposal, CancellationToken ct)
    {
        var node = graph.Nodes.FirstOrDefault(n => n.Id == proposal.NodeId);
        if (node is null) return new ProposalOutcome(false, "Nó não encontrado neste levantamento.");
        if (node.Kind != NodeKind.Unresolved)
            return new ProposalOutcome(false, "Só nós não resolvidos recebem proposta de destino; este já foi resolvido pelo compilador.");
        var resolved = catalog.Resolve(survey.Repository, survey.SolutionPath);
        if (resolved.Target is not { } target)
            return new ProposalOutcome(false, "O repositório ou a solution não estão mais disponíveis na configuração.");

        // Prova conferida contra código diferente do levantado não vale.
        if (survey.CommitHash is not null && await git.HeadAsync(target.RepositoryRoot, ct) is { } head && head.Commit != survey.CommitHash)
            return new ProposalOutcome(false, "O código mudou desde este levantamento. Levante o fluxo de novo antes de propor.");

        SolutionHandle handle;
        try
        {
            handle = await solutions.OpenAsync(target, fresh: false, ct);
        }
        catch (AnalysisException ex)
        {
            return new ProposalOutcome(false, ex.Message);
        }

        var verdict = await ProofValidator.ValidateAsync(handle.Solution, handle.BaseDirectory,
            new EdgeProof(proposal.Target!, proposal.EvidenceFile!, proposal.EvidenceLine!.Value), ct);
        if (!verdict.Accepted) return new ProposalOutcome(false, verdict.Message);

        // Vários destinos por buraco valem (despacho por tipo, por exemplo); o mesmo destino duas vezes, não.
        var branchEntryId = await FlowExtractor.ExtractAsync(handle.Solution, proposal.Target!, new ExtractionOptions { MaxDepth = 0, BaseDirectory = handle.BaseDirectory }, ct);
        if (graph.Edges.Any(e => e.From == node.Id && e.To == branchEntryId.EntryNodeId))
            return new ProposalOutcome(false, "Esse destino já foi proposto para este nó. Para trocar, descarte o atual na tela.");

        // O rastro continua a partir do destino, com a mesma profundidade do levantamento.
        var branch = await FlowExtractor.ExtractAsync(handle.Solution, proposal.Target!,
            new ExtractionOptions { MaxDepth = survey.MaxDepth, BaseDirectory = handle.BaseDirectory }, ct);
        survey.ReplaceGraph(GraphEditor.WithInferredBranch(graph, node.Id, branch, proposal.Origin, proposal.When));
        proposal.Accept(branch.EntryNodeId);

        return new ProposalOutcome(true, $"{verdict.Message} Aresta inferida acrescentada com {branch.Nodes.Count} nó(s) a partir do destino; aguarda a confirmação do usuário.");
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text.Trim() : text[..max].Trim();
}
