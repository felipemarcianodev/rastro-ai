using Rastro.Core.Graph;
using Rastro.Core.Surveys;

namespace Rastro.Core.Database;

// "Fluxos que usam esta tabela" (Fase 39): cada passo de levantamento que lê ou grava a tabela (ou
// executa a procedure), com o levantamento, o método e a linha da chamada.
public record DbUse(
    string Target,
    DbOperation Operation,
    Guid SurveyId,
    string EntryPoint,
    string NodeId,
    string NodeLabel,
    SourceLocation? Location,
    DateTimeOffset SurveyCreatedAt);

// Achado sem IA de um fluxo (N+1, ToList antes do filtro, leitura sem AsNoTracking, procedure por SQL
// cru) sobre a tabela ou a procedure Target.
public record DbFlowFinding(string Target, FindingKind Kind, FindingSeverity Severity, string Message, Guid SurveyId, string EntryPoint, string NodeId, SourceLocation? Location);

public record DbUsageReport(IReadOnlyList<DbUse> Uses, IReadOnlyList<DbFlowFinding> Findings);

public static class DbUsage
{
    // Só o levantamento mais novo de cada método de entrada (e solution) entra, para o mesmo fluxo não
    // aparecer uma vez por levantamento antigo. Só os passos do próprio repositório (System nulo): o
    // trecho de outro sistema fala do banco dele.
    public static DbUsageReport From(IEnumerable<Survey> surveys)
    {
        var latest = surveys
            .Where(s => s.Status == SurveyStatus.Completed)
            .GroupBy(s => (s.SolutionPath, s.EntryPoint))
            .Select(g => g.OrderByDescending(s => s.CreatedAt).First());

        var uses = new List<DbUse>();
        var findings = new List<DbFlowFinding>();
        foreach (var survey in latest)
        {
            if (survey.ReadGraph() is not { } graph) continue;
            foreach (var node in graph.Nodes.Where(n => n.Db is { Count: > 0 } && n.System is null))
                foreach (var access in node.Db!)
                    uses.Add(new DbUse(access.Target, access.Operation, survey.Id, survey.EntryPoint, node.Id, node.Label,
                        node.Location is null ? null : node.Location with { Line = access.Line }, survey.CreatedAt));
            var own = graph.Nodes.Where(n => n.System is null).Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var f in (graph.Findings ?? []).Where(f => f.Target is not null && own.Contains(f.NodeId)))
                findings.Add(new DbFlowFinding(f.Target!, f.Kind, f.Severity, f.Message, survey.Id, survey.EntryPoint, f.NodeId, f.Location));
        }

        var orderedUses = uses
            .OrderBy(u => u.Target, StringComparer.OrdinalIgnoreCase)
            .ThenBy(u => u.EntryPoint, StringComparer.Ordinal)
            .ThenBy(u => u.Operation)
            .ThenBy(u => u.NodeLabel, StringComparer.Ordinal)
            .ToList();
        var orderedFindings = findings
            .OrderBy(f => f.Target, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(f => f.Severity)
            .ThenBy(f => f.EntryPoint, StringComparer.Ordinal)
            .ToList();
        return new DbUsageReport(orderedUses, orderedFindings);
    }
}
