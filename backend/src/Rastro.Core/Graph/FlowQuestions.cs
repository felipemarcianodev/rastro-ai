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

        // Staff: falha em cada chamada para outro sistema ou para fora.
        foreach (var call in map.Links.Where(l => l.Kind == MapLinkKind.Call && blocks.ContainsKey(l.From) && blocks.ContainsKey(l.To)))
        {
            var from = blocks[call.From];
            var to = blocks[call.To];
            var target = call.Http is { } http ? $"{http.Method} {http.Route ?? "?"}" : to.Title;
            var protectedCall = from.NodeIds.Any(id => nodes.TryGetValue(id, out var n) && n.Kind == NodeKind.Try);
            var answer = protectedCall
                ? $"A chamada sai de {from.Title}, que tem try/catch; o tratamento está nos ramos catch desse bloco."
                : $"A chamada sai de {from.Title} sem try/catch no bloco: a falha sobe para quem chamou o bloco.";
            Add(QuestionRole.Staff, $"O que acontece se a chamada a {target} falhar?",
                new FlowAnswer(answer, AnswerSeal.Extracted, [from.NodeId, to.NodeId]));
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
