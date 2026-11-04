namespace Rastro.Core.Graph;

// Perguntas sobre o fluxo (Fase 33), parte sem IA: perguntas prontas no tom de quem revisa (TL,
// Staff, gerente), respondidas só com o que o mapa e as regras provam. A pergunta livre com IA
// usa o mesmo formato de resposta (AnswerSeal e fontes) e fica no Rastro.Ai.

public enum QuestionRole
{
    TechLead = 0,
    Staff = 1,
    Manager = 2
}

// De onde a resposta veio. Viaja como int; a tela traduz pelo Record.
public enum AnswerSeal
{
    // Tirada do código pelo Rastro (mapa, regras, grafo).
    Extracted = 0,

    // Deduzida pela IA a partir do código, com fontes conferidas.
    Inferred = 1,

    // Depende de algo fora do código levantado (outro sistema, SaaS, banco).
    Outside = 2,

    // Nada no código levantado responde.
    None = 3
}

// Sources: ids de nós do grafo que sustentam a resposta (sempre conferidos contra o grafo).
public record FlowAnswer(string Text, AnswerSeal Seal, IReadOnlyList<string> Sources, string? Gap = null);

public record FlowQuestion(string Id, QuestionRole Role, string Text, FlowAnswer Answer);

public static class FlowQuestions
{
    // Lugares listados na resposta de uma falha; os demais viram uma contagem.
    private const int MaxPlaces = 10;

    public static IReadOnlyList<FlowQuestion> Suggest(FlowGraph graph, FlowMap map, IReadOnlyList<FlowRule> rules)
    {
        var nodes = graph.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var blocks = map.Blocks.ToDictionary(b => b.Id, StringComparer.Ordinal);
        var questions = new List<FlowQuestion>();

        void Add(QuestionRole role, string text, FlowAnswer answer) =>
            questions.Add(new FlowQuestion($"q{questions.Count + 1}", role, text, answer));

        // TL: quando o fluxo é recusado.
        var blocking = rules.Where(r => r.Effect == RuleEffect.Blocks).ToList();
        Add(QuestionRole.TechLead, "Em que situações o fluxo é recusado ou encerrado antes do fim?",
            blocking.Count == 0
                ? new FlowAnswer("Nenhuma decisão do fluxo encerra antes do fim: todo caminho chega ao último passo.", AnswerSeal.Extracted, [graph.EntryNodeId])
                : new FlowAnswer(Lines(blocking.Select(r => r.Text)), AnswerSeal.Extracted, SourcesOf(blocking)));

        // TL: efeitos colaterais.
        var outside = map.Blocks.Where(b => b.Kind == MapBlockKind.Outside).ToList();
        var effects = rules.Where(r => r.Effect == RuleEffect.SideEffect).ToList();
        if (outside.Count > 0 || effects.Count > 0)
        {
            var text = new List<string>();
            if (outside.Count > 0) text.Add($"Chama fora do código: {string.Join(", ", outside.Select(b => b.Title))}.");
            text.AddRange(effects.Select(r => r.Text));
            Add(QuestionRole.TechLead, "Quais efeitos o fluxo tem fora do próprio código?",
                new FlowAnswer(Lines(text), outside.Count > 0 ? AnswerSeal.Outside : AnswerSeal.Extracted,
                    outside.Select(b => b.NodeId).Concat(SourcesOf(effects)).Distinct(StringComparer.Ordinal).ToList(),
                    outside.Count > 0 ? "O que acontece do outro lado dessas chamadas não está no código levantado." : null));
        }

        // Staff: falha de cada sistema chamado ou ponto de fora, com os lugares que o chamam (Fase 35).
        var failures = map.Links
            .Where(l => l.Kind == MapLinkKind.Call && blocks.ContainsKey(l.From) && blocks.ContainsKey(l.To))
            .GroupBy(l => FailureTarget(blocks[l.To], nodes), StringComparer.Ordinal);
        foreach (var group in failures)
        {
            var places = group.Select(call => Place(call, blocks[call.From], nodes)).Distinct(StringComparer.Ordinal).ToList();
            var shown = places.Take(MaxPlaces).ToList();
            if (places.Count > MaxPlaces) shown.Add($"E mais {places.Count - MaxPlaces} lugares.");
            var sources = group.SelectMany(call => new[] { blocks[call.From].NodeId, blocks[call.To].NodeId }).Distinct(StringComparer.Ordinal).ToList();
            Add(QuestionRole.Staff, $"O que acontece se {group.Key} falhar?",
                new FlowAnswer(Lines(shown.Prepend($"Chamado de {places.Count} {(places.Count == 1 ? "lugar" : "lugares")}:")), AnswerSeal.Extracted, sources));
        }

        // Staff: regra implementada em mais de um sistema.
        foreach (var rule in rules.Where(r => r.Duplicated))
            Add(QuestionRole.Staff, $"Onde a regra \"{rule.Condition ?? rule.Text}\" está implementada?",
                new FlowAnswer($"Em {rule.Sources.Count} lugares, um por sistema: {string.Join(", ", rule.Sources.Select(s => LaneName(s.Lane)))}. Mudar a regra exige mudar todos.",
                    AnswerSeal.Extracted, rule.Sources.Select(s => s.NodeId).ToList()));

        // Gerente: sistemas envolvidos.
        var lanes = map.Lanes.Where(l => l.Kind == MapLaneKind.Code).Select(l => LaneName(l.Id)).ToList();
        Add(QuestionRole.Manager, "Por quais sistemas a funcionalidade passa?",
            new FlowAnswer(
                $"{lanes.Count} {(lanes.Count == 1 ? "sistema" : "sistemas")} no código levantado ({string.Join(", ", lanes)})"
                + (outside.Count > 0 ? $" e {outside.Count} {(outside.Count == 1 ? "ponto" : "pontos")} fora dele." : "."),
                outside.Count > 0 ? AnswerSeal.Outside : AnswerSeal.Extracted,
                map.Blocks.Where(b => b.Kind == MapBlockKind.Entry).Select(b => b.NodeId).ToList()));

        // Gerente: caminhos e falhas.
        var failing = map.Paths.Count(p => p.Fails);
        Add(QuestionRole.Manager, "Quantos desfechos o fluxo tem e quantos terminam em erro?",
            new FlowAnswer(
                $"{map.Paths.Count}{(map.PathsTruncated ? " ou mais" : string.Empty)} {(map.Paths.Count == 1 ? "caminho" : "caminhos")} de ponta a ponta; "
                + $"{failing} {(failing == 1 ? "termina" : "terminam")} com exceção.",
                AnswerSeal.Extracted, [graph.EntryNodeId]));

        return questions;
    }

    // Quem falha: o sistema chamado; fora do código, o tipo que recebe a chamada (DbContext, e não
    // cada método dele) ou o conjunto das chamadas HTTP que ficaram sem destino.
    private static string FailureTarget(MapBlock to, Dictionary<string, FlowNode> nodes)
    {
        if (to.Kind != MapBlockKind.Outside) return $"o sistema {LaneName(to.Lane)}";
        if (nodes.TryGetValue(to.NodeId, out var node) && node.Kind == NodeKind.Unresolved) return "uma chamada HTTP sem destino resolvido";
        var dot = to.Title.LastIndexOf('.');
        return dot > 0 ? to.Title[..dot] : to.Title;
    }

    private static string Place(MapLink call, MapBlock from, Dictionary<string, FlowNode> nodes)
    {
        var route = call.Http is { } http ? $" ({http.Method} {http.Route ?? "?"})" : string.Empty;
        var guarded = from.NodeIds.Any(id => nodes.TryGetValue(id, out var n) && n.Kind == NodeKind.Try);
        return guarded
            ? $"{from.Title}{route}: tem try/catch; o tratamento está nos ramos catch desse bloco."
            : $"{from.Title}{route}: sem try/catch no bloco; a falha sobe para quem chamou o bloco.";
    }

    // A faixa do primeiro trecho não tem nome de repositório no mapa; a tela troca pelo nome dele.
    private static string LaneName(string lane) => lane switch
    {
        FlowMapBuilder.MainLane => "sistema de entrada",
        FlowMapBuilder.OutsideLane => "fora do código",
        _ => lane
    };

    private static IReadOnlyList<string> SourcesOf(IEnumerable<FlowRule> rules) =>
        rules.SelectMany(r => r.Sources).Select(s => s.NodeId).Distinct(StringComparer.Ordinal).ToList();

    private static string Lines(IEnumerable<string> lines) => string.Join("\n", lines);

    // Confere as fontes de uma resposta contra o grafo: id que não existe sai. Resposta da IA sem
    // nenhuma fonte válida não é mostrada como resposta (vira "sem resposta no código levantado").
    public static FlowAnswer Verify(FlowGraph graph, FlowAnswer answer)
    {
        var ids = graph.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var valid = answer.Sources.Where(ids.Contains).Distinct(StringComparer.Ordinal).ToList();
        if (answer.Seal == AnswerSeal.None) return answer with { Sources = valid };
        if (valid.Count == 0)
            return new FlowAnswer("Nada no código levantado sustenta uma resposta a esta pergunta.", AnswerSeal.None, [],
                "A resposta proposta não citou nenhum passo do fluxo e foi descartada.");
        return answer with { Sources = valid };
    }
}
