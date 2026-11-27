using System.Globalization;
using System.Text;
using Rastro.Core.Graph;
using Rastro.Core.Localization;
using Rastro.Core.Prompts;

namespace Rastro.Core.Validation;

// Documentação do fluxo aprovado (Fase 9): regras na ordem do fluxo, diagrama Mermaid (sem as
// chamadas do .NET, até um limite de nós) e o histórico da mudança que foi validada. O idioma é
// parâmetro (ADR-009); o nome dos passos, o código e o conteúdo escrito pelo usuário não se traduzem.
public static class FlowDocumentBuilder
{
    public const int MaxDiagramNodes = 80;

    public static string Build(PromptContext context, UiLanguage language, FlowGraph flow, FlowGraph original, FlowGraph draft,
        ChangeValidationResult validation, string? newCommit, DateTimeOffset approvedAt,
        IReadOnlyDictionary<string, string>? notes = null)
    {
        string T(string key, params object[] args) => LocalizedText.Get(key, language, args);

        var text = new StringBuilder();
        text.AppendLine(T("FlowDoc.Title", context.EntryPoint));
        text.AppendLine();
        text.AppendLine((newCommit is null
                            ? T("FlowDoc.RepositoryLine", context.Repository, context.SolutionPath)
                            : T("FlowDoc.RepositoryLineCommit", context.Repository, context.SolutionPath, newCommit[..Math.Min(7, newCommit.Length)]))
                        + " " + T("FlowDoc.Documented", approvedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)));
        text.AppendLine();

        text.AppendLine(T("FlowDoc.Rules.Heading"));
        var order = DraftDiff.FlowOrder(flow);
        var withRules = flow.Nodes.Where(n => n.Rule is not null).OrderBy(n => order.GetValueOrDefault(n.Id, int.MaxValue)).ToList();
        if (withRules.Count == 0) text.AppendLine(T("FlowDoc.Rules.None"));
        var index = 0;
        foreach (var node in withRules)
            text.AppendLine($"{++index}. **{node.Label}**{(node.Location is null ? "" : $" (`{node.Location.File}:{node.Location.Line}`)")}: {node.Rule!.Text}");
        text.AppendLine();

        // Anotações do usuário nos passos (Fase 12), na ordem do fluxo.
        var annotated = flow.Nodes.Where(n => notes?.ContainsKey(n.Id) == true).OrderBy(n => order.GetValueOrDefault(n.Id, int.MaxValue)).ToList();
        if (annotated.Count > 0)
        {
            text.AppendLine(T("FlowDoc.Notes.Heading"));
            foreach (var node in annotated)
                text.AppendLine($"- **{node.Label}**: {notes![node.Id]}");
            text.AppendLine();
        }

        text.AppendLine(T("FlowDoc.Diagram.Heading"));
        text.AppendLine("```mermaid");
        text.Append(Mermaid(flow, context.Repository, language));
        text.AppendLine("```");
        text.AppendLine();

        text.AppendLine(T("FlowDoc.History.Heading"));
        foreach (var change in DraftDiff.Compute(original, draft))
            text.AppendLine($"- {Describe(change, language)}");
        text.AppendLine();
        text.AppendLine(T("FlowDoc.History.Validation", VerdictText(validation.Verdict, language), validation.Score,
            validation.DecisionPointsBefore, validation.DecisionPointsAfter,
            StatusText(validation.Build.Status, language), StatusText(validation.Tests.Status, language)));
        foreach (var check in validation.Expectations)
            text.AppendLine($"- [{(check.Status == CheckStatus.Passed ? "x" : " ")}] {check.Description}");
        foreach (var line in validation.Unexpected)
            text.AppendLine(T("FlowDoc.History.Warning", line));
        return text.ToString();
    }

    // Resumo em texto da validação (MCP check_change e rodapé da documentação).
    public static string Summary(ChangeValidationResult validation, UiLanguage language)
    {
        string T(string key, params object[] args) => LocalizedText.Get(key, language, args);

        var text = new StringBuilder();
        text.AppendLine(T("Validation.Summary.Verdict", VerdictText(validation.Verdict, language), validation.Score));
        text.AppendLine(T("Validation.Summary.Build", StatusText(validation.Build.Status, language), Suffix(validation.Build.Detail)));
        text.AppendLine(T("Validation.Summary.Tests", StatusText(validation.Tests.Status, language), Suffix(validation.Tests.Detail)));
        text.AppendLine(T("Validation.Summary.DecisionPoints", validation.DecisionPointsBefore, validation.DecisionPointsAfter));
        text.AppendLine(T("Validation.Summary.Criteria"));
        if (validation.Expectations.Count == 0) text.AppendLine(T("Validation.Summary.CriteriaNone"));
        foreach (var check in validation.Expectations)
            text.AppendLine(T("Validation.Summary.CriteriaLine", check.Status == CheckStatus.Passed ? T("Validation.Summary.Ok") : T("Validation.Summary.Failed"), check.Description, Suffix(check.Detail)));
        if (validation.Unexpected.Count > 0)
        {
            text.AppendLine(T("Validation.Summary.Unexpected"));
            foreach (var line in validation.Unexpected) text.AppendLine($"- {line}");
        }
        return text.ToString();
    }

    private static string Suffix(string? detail) => string.IsNullOrWhiteSpace(detail) ? "" : $" ({detail.ReplaceLineEndings(" ").Trim()})";

    // Com mais de um sistema (Fase 25), um subgraph por sistema; o primeiro leva o nome do repositório do levantamento.
    private static string Mermaid(FlowGraph flow, string firstSystem, UiLanguage language)
    {
        var visible = flow.Nodes
            .Where(n => !(n.Kind == NodeKind.External && (n.Symbol ?? "").StartsWith("System.", StringComparison.Ordinal)))
            .Where(n => !(n.Kind == NodeKind.External && (n.Symbol ?? "").StartsWith("Microsoft.", StringComparison.Ordinal)))
            .OrderBy(n => DraftDiff.FlowOrder(flow).GetValueOrDefault(n.Id, int.MaxValue))
            .Take(MaxDiagramNodes)
            .Select((n, i) => (Node: n, Key: $"n{i + 1}"))
            .ToList();
        var keys = visible.ToDictionary(v => v.Node.Id, v => v.Key);

        var text = new StringBuilder("flowchart LR\n");
        string NodeLine(FlowNode node, string key, string indent)
        {
            var label = node.Label.Replace("\"", "'");
            return node.Kind is NodeKind.Decision ? $"{indent}{key}{{\"{label}\"}}" : $"{indent}{key}[\"{label}\"]";
        }

        if (visible.Any(v => v.Node.System is not null))
        {
            var groups = visible.GroupBy(v => v.Node.System).OrderBy(g => g.Key is null ? 0 : 1).ToList();
            foreach (var (group, index) in groups.Select((g, i) => (g, i)))
            {
                text.AppendLine($"  subgraph sistema{index + 1}[\"{(group.Key ?? firstSystem).Replace("\"", "'")}\"]");
                foreach (var (node, key) in group)
                    text.AppendLine(NodeLine(node, key, "    "));
                text.AppendLine("  end");
            }
        }
        else
        {
            foreach (var (node, key) in visible)
                text.AppendLine(NodeLine(node, key, "  "));
        }
        foreach (var edge in flow.Edges.Where(e => keys.ContainsKey(e.From) && keys.ContainsKey(e.To)))
        {
            var label = (edge.Label is null ? null : BranchText.Plain(edge.Label, language)) ?? (edge.Http is null ? null : $"{edge.Http.Method} {edge.Http.Route ?? "?"}");
            text.AppendLine(label is null
                ? $"  {keys[edge.From]} --> {keys[edge.To]}"
                : $"  {keys[edge.From]} -->|\"{label.Replace("\"", "'")}\"| {keys[edge.To]}");
        }
        if (flow.Nodes.Count > visible.Count)
            text.AppendLine("  " + LocalizedText.Get("FlowDoc.Diagram.Omitted", language, flow.Nodes.Count - visible.Count));
        return text.ToString();
    }

    private static string Describe(DraftChange change, UiLanguage language)
    {
        string T(string key, params object[] args) => LocalizedText.Get(key, language, args);
        return change.Type switch
        {
            DraftChangeType.AddedStep => T("FlowDoc.Change.Added", change.Node.Label, change.Node.Intent ?? ""),
            DraftChangeType.ModifiedStep => T("FlowDoc.Change.Modified", change.Node.Label, change.Node.Intent ?? ""),
            DraftChangeType.RemovedStep => T("FlowDoc.Change.Removed", change.Node.Label),
            DraftChangeType.MovedStep => T("FlowDoc.Change.Moved", change.Node.Label, change.CalledFrom[0].Label, change.PreviousCaller!.Label),
            DraftChangeType.AddedLink => T("FlowDoc.Change.LinkAdded", change.Node.Label, change.Other!.Label),
            _ => T("FlowDoc.Change.LinkRemoved", change.Node.Label, change.Other!.Label)
        };
    }

    public static string VerdictText(ValidationVerdict verdict, UiLanguage language) => LocalizedText.Get(verdict switch
    {
        ValidationVerdict.Approved => "Validation.Verdict.Approved",
        ValidationVerdict.Partial => "Validation.Verdict.Partial",
        _ => "Validation.Verdict.Rejected"
    }, language);

    public static string StatusText(CheckStatus status, UiLanguage language) => LocalizedText.Get(status switch
    {
        CheckStatus.Passed => "Validation.Status.Passed",
        CheckStatus.Failed => "Validation.Status.Failed",
        _ => "Validation.Status.Skipped"
    }, language);
}
