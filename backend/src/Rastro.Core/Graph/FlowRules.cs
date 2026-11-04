using System.Text.RegularExpressions;

namespace Rastro.Core.Graph;

// Regras do fluxo (Fase 32): cada decisão do mapa vira uma regra legível, com o efeito e a fonte.
// Determinístico e sem IA (ADR-001). A regra escrita pelo usuário ou proposta (NodeRule, Fase 6)
// tem precedência sobre o texto gerado da condição.

public enum RuleEffect
{
    // Um ramo encerra o fluxo antes do fim (return ou throw).
    Blocks = 0,

    // Os ramos só escolhem o caminho; nenhum encerra nem chama outro sistema.
    Allows = 1,

    // Um ramo chama outro sistema ou algo fora do código.
    SideEffect = 2
}

// Onde a regra está no código: o nó da decisão, o bloco do mapa e a faixa (sistema).
public record RuleSource(string NodeId, string BlockId, string Lane, SourceLocation? Location);

// Text: a regra em frase. Condition: a condição do código, como está. FromUser: o texto veio da
// regra do nó (escrita ou proposta), não do gerador. Duplicated: a mesma condição aparece em mais
// de um sistema (a tela e a API conferem a mesma coisa), com uma fonte por lugar.
public record FlowRule(
    string Id,
    RuleEffect Effect,
    string Text,
    string? Condition,
    bool FromUser,
    bool Duplicated,
    IReadOnlyList<RuleSource> Sources);

public static partial class FlowRuleExtractor
{
    public static IReadOnlyList<FlowRule> Extract(FlowGraph graph, FlowMap map)
    {
        var nodes = graph.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var blocks = map.Blocks.ToDictionary(b => b.Id, StringComparer.Ordinal);
        var calls = map.Links.Where(l => l.Kind == MapLinkKind.Call).ToLookup(l => l.From, StringComparer.Ordinal);

        var drafts = new List<(string Key, RuleEffect Effect, string Text, string? Condition, bool FromUser, RuleSource Source)>();
        foreach (var block in map.Blocks.Where(b => b.Kind == MapBlockKind.Decision && b.Outcomes is { Count: > 0 }))
        {
            var node = nodes.GetValueOrDefault(block.NodeId);
            var condition = block.Condition ?? node?.Condition;
            var effect = EffectOf(block, map, blocks, calls);
            var userText = node?.Rule?.Text;
            var text = string.IsNullOrWhiteSpace(userText) ? Generate(block, condition, effect, blocks, map, calls) : userText!.Trim();
            var key = condition is null ? block.Id : Normalize(condition);
            drafts.Add((key, effect, text, condition, !string.IsNullOrWhiteSpace(userText),
                new RuleSource(block.NodeId, block.Id, block.Lane, block.Location ?? node?.Location)));
        }

        // A mesma condição em sistemas diferentes vira uma regra só; no mesmo sistema, ficam separadas
        // (dois ifs iguais no mesmo código são dois pontos de decisão).
        var rules = new List<FlowRule>();
        foreach (var group in drafts.GroupBy(d => d.Key, StringComparer.Ordinal))
        {
            var items = group.ToList();
            var lanes = items.Select(i => i.Source.Lane).Distinct(StringComparer.Ordinal).Count();
            if (lanes > 1)
            {
                var first = items[0];
                var effect = items.Select(i => i.Effect).Min();
                rules.Add(new FlowRule($"r{rules.Count + 1}", effect, first.Text, first.Condition,
                    items.Any(i => i.FromUser), true, items.Select(i => i.Source).ToList()));
            }
            else
            {
                foreach (var item in items)
                    rules.Add(new FlowRule($"r{rules.Count + 1}", item.Effect, item.Text, item.Condition, item.FromUser, false, [item.Source]));
            }
        }
        return rules;
    }

    private static RuleEffect EffectOf(MapBlock decision, FlowMap map, Dictionary<string, MapBlock> blocks, ILookup<string, MapLink> calls)
    {
        if (decision.Outcomes!.Any(o => o.Ends)) return RuleEffect.Blocks;
        if (decision.Outcomes!.Any(o => OutcomeCalls(decision, o, map, blocks, calls).Count > 0)) return RuleEffect.SideEffect;
        return RuleEffect.Allows;
    }

    // As chamadas para fora ou para outro sistema feitas no ramo: a ligação de chamada que sai da
    // própria decisão com o texto do ramo, e as que saem dos blocos do ramo.
    private static List<string> OutcomeCalls(MapBlock decision, MapOutcome outcome, FlowMap map,
        Dictionary<string, MapBlock> blocks, ILookup<string, MapLink> calls)
    {
        var targets = new List<string>();
        foreach (var link in calls[decision.Id].Where(l => l.Label == outcome.Label))
            targets.Add(CallText(link, blocks));

        if (outcome.To is { } start && blocks.ContainsKey(start))
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { start };
            var queue = new Queue<string>([start]);
            var sequence = map.Links.Where(l => l.Kind != MapLinkKind.Call).ToLookup(l => l.From, StringComparer.Ordinal);
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                if (blocks[id].Lane != decision.Lane && blocks[id].Kind != MapBlockKind.Outside) continue;
                foreach (var link in calls[id])
                    targets.Add(CallText(link, blocks));
                foreach (var next in sequence[id])
                    if (next.From != decision.Id && blocks.ContainsKey(next.To) && blocks[next.To].Lane == decision.Lane
                        && next.To != decision.Id && seen.Add(next.To) && IsInsideBranch(next, decision))
                        queue.Enqueue(next.To);
            }
        }
        return targets.Distinct(StringComparer.Ordinal).ToList();
    }

    // Dentro do ramo: a ligação não volta para a decisão. O mapa liga o bloco seguinte à decisão
    // (não ao fim do ramo), então seguir sequência a partir do primeiro bloco do ramo não sai dele.
    private static bool IsInsideBranch(MapLink link, MapBlock decision) => link.From != decision.Id;

    private static string CallText(MapLink link, Dictionary<string, MapBlock> blocks) =>
        link.Http is { } http
            ? $"{http.Method} {http.Route ?? "?"}"
            : blocks.TryGetValue(link.To, out var target) ? target.Title : link.To;

    private static string Generate(MapBlock decision, string? condition, RuleEffect effect,
        Dictionary<string, MapBlock> blocks, FlowMap map, ILookup<string, MapLink> calls)
    {
        var subject = string.IsNullOrWhiteSpace(condition) ? decision.Title : condition!;
        var outcomes = decision.Outcomes!;
        switch (effect)
        {
            case RuleEffect.Blocks:
                var parts = outcomes.Where(o => o.Ends)
                    .Select(o => $"{When(subject, o.Label)}, encerra o fluxo{(o.Fails ? " com exceção" : string.Empty)}");
                return string.Join("; ", parts) + ".";

            case RuleEffect.SideEffect:
                var effects = outcomes
                    .Select(o => (o, Calls: OutcomeCalls(decision, o, map, blocks, calls)))
                    .Where(x => x.Calls.Count > 0)
                    .Select(x => $"{When(subject, x.o.Label)}, chama {string.Join(", ", x.Calls)}");
                return string.Join("; ", effects) + ".";

            default:
                return $"{subject} decide o caminho entre {string.Join(", ", outcomes.Select(o => o.Label))}.";
        }
    }

    private static string When(string condition, string label) => label switch
    {
        "then" => $"Se {condition}",
        "else" => $"Se não ({condition})",
        _ when label.StartsWith("case ", StringComparison.Ordinal) => $"Quando {condition} é {label[5..]}",
        _ => $"Em {label}"
    };

    // "this.pedido.Enviado " e "pedido.Enviado" são a mesma condição; maiúsculas e espaços não contam.
    public static string Normalize(string condition) =>
        Spaces().Replace(ThisPrefix().Replace(condition, string.Empty), string.Empty).ToLowerInvariant();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\bthis\.")]
    private static partial Regex ThisPrefix();
}
