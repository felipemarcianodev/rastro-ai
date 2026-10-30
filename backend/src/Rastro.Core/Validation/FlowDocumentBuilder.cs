using System.Text;
using Rastro.Core.Graph;
using Rastro.Core.Prompts;

namespace Rastro.Core.Validation;

// Documentação do fluxo aprovado (Fase 9): regras na ordem do fluxo, diagrama Mermaid (sem as
// chamadas do .NET, até um limite de nós) e o histórico da mudança que foi validada.
public static class FlowDocumentBuilder
{
    public const int MaxDiagramNodes = 80;

    public static string Build(PromptContext context, FlowGraph flow, FlowGraph original, FlowGraph draft,
        ChangeValidationResult validation, string? newCommit, DateTimeOffset approvedAt,
        IReadOnlyDictionary<string, string>? notes = null)
    {
        var text = new StringBuilder();
        text.AppendLine($"# Fluxo `{context.EntryPoint}`");
        text.AppendLine();
        text.AppendLine($"Repositório `{context.Repository}`, solution `{context.SolutionPath}`" +
                        (newCommit is null ? "." : $", commit `{newCommit[..Math.Min(7, newCommit.Length)]}`.") +
                        $" Documentado pelo Rastro AI em {approvedAt:yyyy-MM-dd HH:mm} (UTC), depois da mudança validada.");
        text.AppendLine();

        text.AppendLine("## Regras do fluxo");
        var order = DraftDiff.FlowOrder(flow);
        var withRules = flow.Nodes.Where(n => n.Rule is not null).OrderBy(n => order.GetValueOrDefault(n.Id, int.MaxValue)).ToList();
        if (withRules.Count == 0) text.AppendLine("Nenhuma regra escrita para este fluxo ainda.");
        var index = 0;
        foreach (var node in withRules)
            text.AppendLine($"{++index}. **{node.Label}**{(node.Location is null ? "" : $" (`{node.Location.File}:{node.Location.Line}`)")}: {node.Rule!.Text}");
        text.AppendLine();

        // Anotações do usuário nos passos (Fase 12), na ordem do fluxo.
        var annotated = flow.Nodes.Where(n => notes?.ContainsKey(n.Id) == true).OrderBy(n => order.GetValueOrDefault(n.Id, int.MaxValue)).ToList();
        if (annotated.Count > 0)
        {
            text.AppendLine("## Anotações");
            foreach (var node in annotated)
                text.AppendLine($"- **{node.Label}**: {notes![node.Id]}");
            text.AppendLine();
        }

        text.AppendLine("## Diagrama");
        text.AppendLine("```mermaid");
        text.Append(Mermaid(flow, context.Repository));
        text.AppendLine("```");
        text.AppendLine();

        text.AppendLine("## Histórico desta mudança");
        foreach (var change in DraftDiff.Compute(original, draft))
            text.AppendLine($"- {Describe(change)}");
        text.AppendLine();
        text.AppendLine($"Validação: **{VerdictText(validation.Verdict)}**, {validation.Score}% dos critérios cumpridos; " +
                        $"pontos de decisão do fluxo {validation.DecisionPointsBefore} → {validation.DecisionPointsAfter}; " +
                        $"build: {StatusText(validation.Build.Status)}; testes: {StatusText(validation.Tests.Status)}.");
        foreach (var check in validation.Expectations)
            text.AppendLine($"- [{(check.Status == CheckStatus.Passed ? "x" : " ")}] {check.Description}");
        foreach (var line in validation.Unexpected)
            text.AppendLine($"- Atenção: {line}");
        return text.ToString();
    }

    // Resumo em texto da validação (MCP check_change e rodapé da documentação).
    public static string Summary(ChangeValidationResult validation)
    {
        var text = new StringBuilder();
        text.AppendLine($"Validação {VerdictText(validation.Verdict)}: nota {validation.Score} (critérios cumpridos).");
        text.AppendLine($"Build: {StatusText(validation.Build.Status)}{Suffix(validation.Build.Detail)}");
        text.AppendLine($"Testes: {StatusText(validation.Tests.Status)}{Suffix(validation.Tests.Detail)}");
        text.AppendLine($"Pontos de decisão do fluxo: {validation.DecisionPointsBefore} → {validation.DecisionPointsAfter}.");
        text.AppendLine("Critérios do rascunho:");
        if (validation.Expectations.Count == 0) text.AppendLine("- nenhum");
        foreach (var check in validation.Expectations)
            text.AppendLine($"- [{(check.Status == CheckStatus.Passed ? "ok" : "falhou")}] {check.Description}{Suffix(check.Detail)}");
        if (validation.Unexpected.Count > 0)
        {
            text.AppendLine("Mudanças fora do pedido:");
            foreach (var line in validation.Unexpected) text.AppendLine($"- {line}");
        }
        return text.ToString();
    }

    private static string Suffix(string? detail) => string.IsNullOrWhiteSpace(detail) ? "" : $" ({detail.ReplaceLineEndings(" ").Trim()})";

    // Com mais de um sistema (Fase 25), um subgraph por sistema; o primeiro leva o nome do repositório do levantamento.
    private static string Mermaid(FlowGraph flow, string firstSystem)
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
            var label = edge.Label ?? (edge.Http is null ? null : $"{edge.Http.Method} {edge.Http.Route ?? "?"}");
            text.AppendLine(label is null
                ? $"  {keys[edge.From]} --> {keys[edge.To]}"
                : $"  {keys[edge.From]} -->|\"{label.Replace("\"", "'")}\"| {keys[edge.To]}");
        }
        if (flow.Nodes.Count > visible.Count)
            text.AppendLine($"  %% {flow.Nodes.Count - visible.Count} nós fora do diagrama (chamadas do .NET ou acima do limite)");
        return text.ToString();
    }

    private static string Describe(DraftChange change) => change.Type switch
    {
        DraftChangeType.AddedStep => $"Passo novo `{change.Node.Label}`: {change.Node.Intent}",
        DraftChangeType.ModifiedStep => $"Regra mudou em `{change.Node.Label}`: {change.Node.Intent}",
        DraftChangeType.RemovedStep => $"`{change.Node.Label}` saiu do fluxo",
        DraftChangeType.MovedStep => $"`{change.Node.Label}` passou a ser chamado por `{change.CalledFrom[0].Label}` (antes por `{change.PreviousCaller!.Label}`)",
        DraftChangeType.AddedLink => $"`{change.Node.Label}` passou a chamar `{change.Other!.Label}`",
        _ => $"`{change.Node.Label}` deixou de chamar `{change.Other!.Label}`"
    };

    public static string VerdictText(ValidationVerdict verdict) => verdict switch
    {
        ValidationVerdict.Approved => "aprovada",
        ValidationVerdict.Partial => "parcial",
        _ => "reprovada"
    };

    public static string StatusText(CheckStatus status) => status switch
    {
        CheckStatus.Passed => "passou",
        CheckStatus.Failed => "falhou",
        _ => "não configurado"
    };
}
