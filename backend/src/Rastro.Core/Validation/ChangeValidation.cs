using Rastro.Core.Graph;

namespace Rastro.Core.Validation;

public enum CheckStatus
{
    Passed = 0,
    Failed = 1,
    // Não deu para conferir (ex.: build sem comando configurado). Nunca conta como "passou".
    Skipped = 2
}

public enum ValidationVerdict
{
    Approved = 0,
    Partial = 1,
    Rejected = 2
}

public record ValidationCheck(string Description, CheckStatus Status, string? Detail = null);

// Resultado da validação de uma mudança (Fase 9): critérios do rascunho, mudanças além do pedido,
// build e testes do repositório e a variação dos pontos de decisão do fluxo.
public record ChangeValidationResult(
    IReadOnlyList<ValidationCheck> Expectations,
    IReadOnlyList<string> Unexpected,
    int DecisionPointsBefore,
    int DecisionPointsAfter,
    ValidationCheck Build,
    ValidationCheck Tests,
    ValidationVerdict Verdict,
    int Score);

// Compara o fluxo levantado depois da mudança com o que o rascunho pediu. Puro e determinístico.
// Os nós são casados pelo id do símbolo (estável) e, para passos novos, pelo nome Tipo.Metodo ou,
// quando o usuário escreveu os tipos (Tipo.Metodo(string, int)), pela assinatura. Com mais de um
// candidato, vence o que é chamado pelo passo de origem da ligação.
public static class ChangeValidator
{
    public const int MaxUnexpectedListed = 20;

    private static readonly HashSet<NodeKind> DecisionKinds = [NodeKind.Decision, NodeKind.Loop, NodeKind.Try];

    public static (IReadOnlyList<ValidationCheck> Expectations, IReadOnlyList<string> Unexpected) Compare(
        FlowGraph original, FlowGraph draft, FlowGraph after)
    {
        var draftNodes = draft.Nodes.ToDictionary(n => n.Id);
        string? Resolve(string draftNodeId, string? caller = null)
        {
            if (after.Nodes.Any(n => n.Id == draftNodeId)) return draftNodeId;
            var label = draftNodes[draftNodeId].Label;
            var candidates = after.Nodes.Where(n => StepSignature.Matches(label, n)).ToList();
            if (caller is not null && candidates.Count > 1)
            {
                var called = candidates.Where(c => after.Edges.Any(e => e.From == caller && e.To == c.Id)).ToList();
                if (called.Count > 0) candidates = called;
            }
            return candidates.OrderBy(n => n.Kind is NodeKind.Method or NodeKind.Entry ? 0 : 1).Select(n => n.Id).FirstOrDefault();
        }

        bool HasEdge(string from, string to, string? label) =>
            after.Edges.Any(e => e.From == from && e.To == to && (label is null || e.Label == label));

        var expectations = new List<ValidationCheck>();
        foreach (var edge in draft.Edges.Where(e => e.Change == ChangeKind.Added))
        {
            var description = $"{draftNodes[edge.From].Label} chama {draftNodes[edge.To].Label}" + (edge.Label is null ? "" : $" ({edge.Label})");
            var from = Resolve(edge.From);
            var to = Resolve(edge.To, from);
            expectations.Add(from is not null && to is not null && HasEdge(from, to, edge.Label)
                ? new ValidationCheck(description, CheckStatus.Passed)
                : new ValidationCheck(description, CheckStatus.Failed,
                    to is null ? $"{draftNodes[edge.To].Label} não aparece no fluxo levantado." : "A chamada não aparece no fluxo levantado."));
        }

        foreach (var edge in draft.Edges.Where(e => e.Change == ChangeKind.Removed))
        {
            var description = $"{draftNodes[edge.From].Label} não chama mais {draftNodes[edge.To].Label}" + (edge.Label is null ? "" : $" ({edge.Label})");
            var from = Resolve(edge.From);
            var to = Resolve(edge.To);
            expectations.Add(from is not null && to is not null && HasEdge(from, to, edge.Label)
                ? new ValidationCheck(description, CheckStatus.Failed, "A chamada continua no fluxo.")
                : new ValidationCheck(description, CheckStatus.Passed));
        }

        foreach (var node in draft.Nodes.Where(n => n.Change == ChangeKind.Modified))
            expectations.Add(Resolve(node.Id) is not null
                ? new ValidationCheck($"{node.Label} continua no fluxo (regra nova a conferir por você)", CheckStatus.Passed, node.Intent)
                : new ValidationCheck($"{node.Label} continua no fluxo", CheckStatus.Failed, "O passo sumiu do fluxo."));

        return (expectations, Unexpected(original, draft, after));
    }

    // Ligações que mudaram entre o fluxo original e o novo sem estar no rascunho (por nome dos passos).
    private static IReadOnlyList<string> Unexpected(FlowGraph original, FlowGraph draft, FlowGraph after)
    {
        static HashSet<(string, string)> Keys(FlowGraph graph)
        {
            var labels = graph.Nodes.ToDictionary(n => n.Id, n => n.Label);
            return graph.Edges.Where(e => e.Change != ChangeKind.Removed && labels.ContainsKey(e.From) && labels.ContainsKey(e.To))
                .Select(e => (labels[e.From], labels[e.To])).ToHashSet();
        }

        var draftLabels = draft.Nodes.ToDictionary(n => n.Id, n => n.Label);
        var requested = draft.Edges.Where(e => e.Change is not null).Select(e => (draftLabels[e.From], draftLabels[e.To])).ToHashSet();
        var touched = draft.Nodes.Where(n => n.Change is ChangeKind.Added or ChangeKind.Removed).Select(n => n.Label).ToHashSet();

        var before = Keys(original);
        var now = Keys(after);
        var lines = new List<string>();
        foreach (var (from, to) in now.Except(before).Where(k => !requested.Contains(k) && !touched.Contains(k.Item1) && !touched.Contains(k.Item2)).Order())
            lines.Add($"nova chamada fora do pedido: {from} → {to}");
        foreach (var (from, to) in before.Except(now).Where(k => !requested.Contains(k) && !touched.Contains(k.Item1) && !touched.Contains(k.Item2)).Order())
            lines.Add($"chamada sumiu sem pedido: {from} → {to}");
        return lines.Take(MaxUnexpectedListed).ToList();
    }

    // As regras do levantamento original seguem para o fluxo novo nos passos que continuam lá.
    // Passo com regra alterada no rascunho fica sem regra: a antiga não vale mais.
    public static FlowGraph CarryRules(FlowGraph original, FlowGraph draft, FlowGraph after)
    {
        var modified = draft.Nodes.Where(n => n.Change == ChangeKind.Modified).Select(n => n.Id).ToHashSet();
        var rules = original.Nodes.Where(n => n.Rule is not null && !modified.Contains(n.Id)).ToDictionary(n => n.Id, n => n.Rule);
        return after with { Nodes = after.Nodes.Select(n => n.Rule is null && rules.TryGetValue(n.Id, out var rule) ? n with { Rule = rule } : n).ToList() };
    }

    public static int DecisionPoints(FlowGraph graph) => graph.Nodes.Count(n => DecisionKinds.Contains(n.Kind) && n.Change != ChangeKind.Removed);

    // Aprovada: todos os critérios ok, nada além do pedido, build e testes sem falha.
    // Reprovada: build ou testes falharam, ou nenhum critério foi cumprido.
    public static (ValidationVerdict Verdict, int Score) Judge(IReadOnlyList<ValidationCheck> expectations, IReadOnlyList<string> unexpected,
        ValidationCheck build, ValidationCheck tests)
    {
        var passed = expectations.Count(c => c.Status == CheckStatus.Passed);
        var score = expectations.Count == 0 ? 100 : (int)Math.Round(100.0 * passed / expectations.Count);
        var broken = build.Status == CheckStatus.Failed || tests.Status == CheckStatus.Failed;

        if (broken || (expectations.Count > 0 && passed == 0)) return (ValidationVerdict.Rejected, score);
        if (passed == expectations.Count && unexpected.Count == 0) return (ValidationVerdict.Approved, score);
        return (ValidationVerdict.Partial, score);
    }
}
