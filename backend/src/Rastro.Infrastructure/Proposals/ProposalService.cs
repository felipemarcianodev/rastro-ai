using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Globalization;
using Rastro.Core.Failures;
using Rastro.Core.Graph;
using Rastro.Core.Surveys;
using Rastro.Extractor;
using Rastro.Infrastructure.Analysis;
using Rastro.Infrastructure.Processes;
using Rastro.Infrastructure.Repositories;

namespace Rastro.Infrastructure.Proposals;

// Refusal e RefusalArgs: o motivo da recusa em código (a tela traduz); Message é o texto em português.
public record ProposalOutcome(bool Accepted, string Message, Guid? ProposalId = null,
    ProposalRefusal? Refusal = null, IReadOnlyList<string>? RefusalArgs = null)
{
    public static ProposalOutcome Refuse(ProposalRefusal refusal, params string[] args) =>
        new(false, FailureText.Of(refusal, args), null, refusal, args);

    public static ProposalOutcome Refuse(ProofVerdict verdict) =>
        new(false, verdict.Message, null, verdict.Refusal, verdict.RefusalArgs);

    // A solution não abriu: o motivo é o da análise.
    public static ProposalOutcome Refuse(AnalysisException ex) =>
        Refuse(ProposalRefusal.AnalysisFailed, [.. FailureText.Nest(ex.Failure, ex.Args)]);
}

// Porta única para propostas de agentes (MCP) e provedores de IA (ADR-001): toda proposta
// passa pelas mesmas regras e pelo validador de prova antes de mexer no grafo, e fica
// registrada (aceita ou recusada) em survey_proposals.
public class ProposalService(
    RastroDbContext db,
    RepositoryCatalog catalog,
    IGitInfo git,
    ISolutionSource solutions,
    RouteIndexStore routeIndexes,
    TimeProvider clock,
    ILogger<ProposalService> logger)
{
    public const int MinRuleLength = 10;
    public const int MaxRuleLength = 2000;
    public const int MaxReasonLength = 1000;
    public const int MaxWhenLength = 200;

    public async Task<ProposalOutcome> ProposeEdgeAsync(
        Guid surveyId, string nodeId, string target, string evidenceFile, int evidenceLine, string reason,
        ProposalOrigin origin, CancellationToken ct, string? when = null, string? system = null)
    {
        var survey = await db.Surveys.FirstOrDefaultAsync(s => s.Id == surveyId, ct);
        if (survey?.ReadGraph() is not { } graph)
            return ProposalOutcome.Refuse(ProposalRefusal.SurveyNotFound);

        var proposal = SurveyProposal.ForEdge(surveyId, origin, nodeId, target, evidenceFile, evidenceLine,
            Truncate(reason, MaxReasonLength), clock.GetUtcNow(), string.IsNullOrWhiteSpace(when) ? null : Truncate(when, MaxWhenLength));
        db.SurveyProposals.Add(proposal);

        var outcome = await EvaluateEdgeAsync(survey, graph, proposal, system, ct);
        if (!outcome.Accepted) proposal.Reject(outcome.Message, outcome.Refusal, outcome.RefusalArgs);

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Proposta de aresta {ProposalId} no levantamento {SurveyId}: {Status} (origem {Origin}).",
            proposal.Id, surveyId, proposal.Status, origin);
        return outcome with { ProposalId = proposal.Id };
    }

    public async Task<ProposalOutcome> ProposeRuleAsync(Guid surveyId, string nodeId, string text, ProposalOrigin origin, CancellationToken ct)
    {
        var survey = await db.Surveys.FirstOrDefaultAsync(s => s.Id == surveyId, ct);
        if (survey?.ReadGraph() is not { } graph)
            return ProposalOutcome.Refuse(ProposalRefusal.SurveyNotFound);

        var clean = text.Trim();
        if (clean.Length < MinRuleLength || clean.Length > MaxRuleLength)
            return ProposalOutcome.Refuse(ProposalRefusal.RuleLength, MinRuleLength.ToString(CultureInfo.InvariantCulture), MaxRuleLength.ToString(CultureInfo.InvariantCulture));
        if (graph.Nodes.All(n => n.Id != nodeId))
            return ProposalOutcome.Refuse(ProposalRefusal.NodeNotFound);

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
            return ProposalOutcome.Refuse(ProposalRefusal.SurveyNotFound);

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

    private async Task<ProposalOutcome> EvaluateEdgeAsync(Survey survey, FlowGraph graph, SurveyProposal proposal, string? system, CancellationToken ct)
    {
        var node = graph.Nodes.FirstOrDefault(n => n.Id == proposal.NodeId);
        if (node is null) return ProposalOutcome.Refuse(ProposalRefusal.NodeNotFound);
        if (node.Kind != NodeKind.Unresolved)
            return ProposalOutcome.Refuse(ProposalRefusal.NotUnresolved);
        if (node.Reason is UnresolvedReason.RouteNotResolved or UnresolvedReason.NoMatchingEndpoint or UnresolvedReason.MultipleEndpoints)
            return await EvaluateHttpEdgeAsync(survey, graph, proposal, node, system, ct);
        var resolved = catalog.Resolve(survey.Repository, survey.SolutionPath);
        if (resolved.Target is not { } target)
            return ProposalOutcome.Refuse(ProposalRefusal.TargetUnavailable);
        if (target.Language != Rastro.Core.Graph.SourceLanguage.DotNet)
            return ProposalOutcome.Refuse(ProposalRefusal.AngularUnsupported);

        // Prova conferida contra código diferente do levantado não vale.
        if (survey.CommitHash is not null && await git.HeadAsync(target.RepositoryRoot, ct) is { } head && head.Commit != survey.CommitHash)
            return ProposalOutcome.Refuse(ProposalRefusal.CodeChanged);

        SolutionHandle handle;
        try
        {
            handle = await solutions.OpenAsync(target, fresh: false, ct);
        }
        catch (AnalysisException ex)
        {
            return ProposalOutcome.Refuse(ex);
        }

        var verdict = await ProofValidator.ValidateAsync(handle.Solution, handle.BaseDirectory,
            new EdgeProof(proposal.Target!, proposal.EvidenceFile!, proposal.EvidenceLine!.Value), ct);
        if (!verdict.Accepted) return ProposalOutcome.Refuse(verdict);

        // Vários destinos por buraco valem (despacho por tipo, por exemplo); o mesmo destino duas vezes, não.
        var branchEntryId = await FlowExtractor.ExtractAsync(handle.Solution, proposal.Target!, new ExtractionOptions { MaxDepth = 0, BaseDirectory = handle.BaseDirectory }, ct);
        if (graph.Edges.Any(e => e.From == node.Id && e.To == branchEntryId.EntryNodeId))
            return ProposalOutcome.Refuse(ProposalRefusal.DuplicateTarget);

        // O rastro continua a partir do destino, com a mesma profundidade do levantamento.
        var branch = await FlowExtractor.ExtractAsync(handle.Solution, proposal.Target!,
            new ExtractionOptions { MaxDepth = survey.MaxDepth, BaseDirectory = handle.BaseDirectory }, ct);
        survey.ReplaceGraph(GraphEditor.WithInferredBranch(graph, node.Id, branch, proposal.Origin, proposal.When));
        proposal.Accept(branch.EntryNodeId);

        return new ProposalOutcome(true, $"{verdict.Message} Aresta inferida acrescentada com {branch.Nodes.Count} nó(s) a partir do destino; aguarda a confirmação do usuário.");
    }

    // Chamada HTTP sem destino (Fase 25): o destino está em outro repositório da costura do levantamento.
    // Além da prova normal, o método precisa atender o verbo e a rota da chamada, pelo índice de rotas.
    private async Task<ProposalOutcome> EvaluateHttpEdgeAsync(
        Survey survey, FlowGraph graph, SurveyProposal proposal, FlowNode node, string? system, CancellationToken ct)
    {
        var allowed = survey.ReadCrossRepositories();
        var destination = string.IsNullOrWhiteSpace(system) ? null : allowed.FirstOrDefault(r => string.Equals(r, system.Trim(), StringComparison.OrdinalIgnoreCase));
        if (destination is null)
        {
            return allowed.Count == 0
                ? ProposalOutcome.Refuse(ProposalRefusal.HttpNeedsSystemNoCross)
                : ProposalOutcome.Refuse(ProposalRefusal.HttpNeedsSystem, string.Join(", ", allowed));
        }

        var call = graph.Edges.FirstOrDefault(e => e.To == node.Id && e.Http is not null)?.Http;
        if (call is null)
            return ProposalOutcome.Refuse(ProposalRefusal.HttpCallMissing);

        var firstSolution = catalog.DotNetSolutions(destination)?.FirstOrDefault();
        var resolved = firstSolution is null ? null : catalog.Resolve(destination, firstSolution).Target;
        if (resolved is null)
            return ProposalOutcome.Refuse(ProposalRefusal.DestinationUnavailable, destination);

        // Prova conferida contra código diferente do levantado não vale, no repositório do levantamento nem no do destino.
        var ownTarget = catalog.Resolve(survey.Repository, survey.SolutionPath);
        if (ownTarget.Target is { } own && survey.CommitHash is not null && await git.HeadAsync(own.RepositoryRoot, ct) is { } ownHead && ownHead.Commit != survey.CommitHash)
            return ProposalOutcome.Refuse(ProposalRefusal.CodeChanged);

        var segment = await db.SurveySegments.AsNoTracking().FirstOrDefaultAsync(s => s.SurveyId == survey.Id && s.Repository == destination, ct);
        var basis = string.Equals(destination, survey.Repository, StringComparison.OrdinalIgnoreCase) ? survey.CommitHash : segment?.CommitHash;
        if (basis is not null && await git.HeadAsync(resolved.RepositoryRoot, ct) is { } destinationHead && destinationHead.Commit != basis)
            return ProposalOutcome.Refuse(ProposalRefusal.DestinationCodeChanged, destination);

        SolutionHandle handle;
        try
        {
            handle = await solutions.OpenAsync(resolved, fresh: false, ct);
        }
        catch (AnalysisException ex)
        {
            return ProposalOutcome.Refuse(ex);
        }

        var index = await routeIndexes.GetAsync(destination, ct);
        var verdict = await ProofValidator.ValidateHttpAsync(handle.Solution, handle.BaseDirectory,
            new EdgeProof(proposal.Target!, proposal.EvidenceFile!, proposal.EvidenceLine!.Value), call, index, ct);
        if (!verdict.Accepted) return ProposalOutcome.Refuse(verdict);

        var branchOptions = new ExtractionOptions { MaxDepth = survey.MaxDepth, BaseDirectory = handle.BaseDirectory, HttpCalls = true };
        var branch = SystemIds.InSystem(await FlowExtractor.ExtractAsync(handle.Solution, proposal.Target!, branchOptions, ct), destination);
        if (graph.Edges.Any(e => e.From == node.Id && e.To == branch.EntryNodeId))
            return ProposalOutcome.Refuse(ProposalRefusal.DuplicateTarget);

        var merged = GraphEditor.WithInferredBranch(graph, node.Id, branch, proposal.Origin, proposal.When);
        // A aresta inferida leva a chamada HTTP, como a extraída leva.
        var edges = merged.Edges.Select(e => e.From == node.Id && e.To == branch.EntryNodeId && e.Http is null ? e with { Http = call } : e).ToList();
        survey.ReplaceGraph(merged with { Edges = edges });
        proposal.Accept(branch.EntryNodeId);

        // O trecho novo passa a contar na conferência de "desatualizado" (/stale), como os da costura automática.
        if (segment is null && !string.Equals(destination, survey.Repository, StringComparison.OrdinalIgnoreCase))
        {
            var order = await db.SurveySegments.Where(s => s.SurveyId == survey.Id).Select(s => (int?)s.Order).MaxAsync(ct) ?? 0;
            var head = await git.HeadAsync(resolved.RepositoryRoot, ct);
            db.SurveySegments.Add(SurveySegment.Create(survey.Id, order + 1, destination, resolved.SolutionRelativePath, proposal.Target!, head?.Commit, head?.Branch));
        }

        return new ProposalOutcome(true, $"{verdict.Message} Aresta HTTP inferida acrescentada com {branch.Nodes.Count} nó(s) de {destination} a partir do destino; aguarda a confirmação do usuário.");
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text.Trim() : text[..max].Trim();
}
