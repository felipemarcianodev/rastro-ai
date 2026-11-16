using Rastro.Core.Localization;

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

    public static IReadOnlyList<FlowQuestion> Suggest(FlowGraph graph, FlowMap map, IReadOnlyList<FlowRule> rules, UiLanguage language)
    {
        string T(string key, params object[] args) => LocalizedText.Get(key, language, args);

        var nodes = graph.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var blocks = map.Blocks.ToDictionary(b => b.Id, StringComparer.Ordinal);
        var questions = new List<FlowQuestion>();

        void Add(QuestionRole role, string text, FlowAnswer answer) =>
            questions.Add(new FlowQuestion($"q{questions.Count + 1}", role, text, answer));

        // TL: quando o fluxo é recusado.
        var blocking = rules.Where(r => r.Effect == RuleEffect.Blocks).ToList();
        Add(QuestionRole.TechLead, T("Questions.Blocking.Question"),
            blocking.Count == 0
                ? new FlowAnswer(T("Questions.Blocking.None"), AnswerSeal.Extracted, [graph.EntryNodeId])
                : new FlowAnswer(Lines(blocking.Select(r => r.Text)), AnswerSeal.Extracted, SourcesOf(blocking)));

        // TL: efeitos colaterais.
        var outside = map.Blocks.Where(b => b.Kind == MapBlockKind.Outside).ToList();
        var effects = rules.Where(r => r.Effect == RuleEffect.SideEffect).ToList();
        if (outside.Count > 0 || effects.Count > 0)
        {
            var text = new List<string>();
            if (outside.Count > 0) text.Add(T("Questions.Effects.CallsOutside", string.Join(", ", outside.Select(b => b.Title))));
            text.AddRange(effects.Select(r => r.Text));
            Add(QuestionRole.TechLead, T("Questions.Effects.Question"),
                new FlowAnswer(Lines(text), outside.Count > 0 ? AnswerSeal.Outside : AnswerSeal.Extracted,
                    outside.Select(b => b.NodeId).Concat(SourcesOf(effects)).Distinct(StringComparer.Ordinal).ToList(),
                    outside.Count > 0 ? T("Questions.Effects.Gap") : null));
        }

        // Staff: falha de cada sistema chamado ou ponto de fora, com os lugares que o chamam (Fase 35).
        var failures = map.Links
            .Where(l => l.Kind == MapLinkKind.Call && blocks.ContainsKey(l.From) && blocks.ContainsKey(l.To))
            .GroupBy(l => FailureTarget(blocks[l.To], nodes, language), StringComparer.Ordinal);
        foreach (var group in failures)
        {
            var places = group.Select(call => Place(call, blocks[call.From], nodes, language)).Distinct(StringComparer.Ordinal).ToList();
            var shown = places.Take(MaxPlaces).ToList();
            if (places.Count > MaxPlaces) shown.Add(T("Questions.Failure.MorePlaces", places.Count - MaxPlaces));
            var sources = group.SelectMany(call => new[] { blocks[call.From].NodeId, blocks[call.To].NodeId }).Distinct(StringComparer.Ordinal).ToList();
            Add(QuestionRole.Staff, T("Questions.Failure.Question", group.Key),
                new FlowAnswer(Lines(shown.Prepend(T("Questions.Failure.CalledFrom", places.Count, T(places.Count == 1 ? "Questions.Unit.Place.One" : "Questions.Unit.Place.Many")))), AnswerSeal.Extracted, sources));
        }

        // Staff: regra implementada em mais de um sistema.
        foreach (var rule in rules.Where(r => r.Duplicated))
            Add(QuestionRole.Staff, T("Questions.Duplicated.Question", rule.Condition ?? rule.Text),
                new FlowAnswer(T("Questions.Duplicated.Answer", rule.Sources.Count, string.Join(", ", rule.Sources.Select(s => LaneName(s.Lane, language)))),
                    AnswerSeal.Extracted, rule.Sources.Select(s => s.NodeId).ToList()));

        // Gerente: sistemas envolvidos.
        var lanes = map.Lanes.Where(l => l.Kind == MapLaneKind.Code).Select(l => LaneName(l.Id, language)).ToList();
        Add(QuestionRole.Manager, T("Questions.Systems.Question"),
            new FlowAnswer(
                T("Questions.Systems.Answer", lanes.Count, T(lanes.Count == 1 ? "Questions.Unit.System.One" : "Questions.Unit.System.Many"), string.Join(", ", lanes))
                + (outside.Count > 0 ? T("Questions.Systems.Outside", outside.Count, T(outside.Count == 1 ? "Questions.Unit.Point.One" : "Questions.Unit.Point.Many")) : T("Questions.Systems.End")),
                outside.Count > 0 ? AnswerSeal.Outside : AnswerSeal.Extracted,
                map.Blocks.Where(b => b.Kind == MapBlockKind.Entry).Select(b => b.NodeId).ToList()));

        // Gerente: caminhos e falhas.
        var failing = map.Paths.Count(p => p.Fails);
        Add(QuestionRole.Manager, T("Questions.Outcomes.Question"),
            new FlowAnswer(
                T("Questions.Outcomes.Answer", map.Paths.Count, map.PathsTruncated ? T("Questions.Outcomes.OrMore") : string.Empty,
                    T(map.Paths.Count == 1 ? "Questions.Unit.Path.One" : "Questions.Unit.Path.Many"),
                    failing, T(failing == 1 ? "Questions.Unit.Ends.One" : "Questions.Unit.Ends.Many")),
                AnswerSeal.Extracted, [graph.EntryNodeId]));

        return questions;
    }

    // Quem falha: o sistema chamado; fora do código, o tipo que recebe a chamada (DbContext, e não
    // cada método dele) ou o conjunto das chamadas HTTP que ficaram sem destino.
    private static string FailureTarget(MapBlock to, Dictionary<string, FlowNode> nodes, UiLanguage language)
    {
        if (to.Kind != MapBlockKind.Outside) return LocalizedText.Get("Questions.Failure.TargetSystem", language, LaneName(to.Lane, language));
        if (nodes.TryGetValue(to.NodeId, out var node) && node.Kind == NodeKind.Unresolved) return LocalizedText.Get("Questions.Failure.TargetUnresolvedHttp", language);
        var dot = to.Title.LastIndexOf('.');
        return dot > 0 ? to.Title[..dot] : to.Title;
    }

    private static string Place(MapLink call, MapBlock from, Dictionary<string, FlowNode> nodes, UiLanguage language)
    {
        var route = call.Http is { } http ? $" ({http.Method} {http.Route ?? "?"})" : string.Empty;
        var guarded = from.NodeIds.Any(id => nodes.TryGetValue(id, out var n) && n.Kind == NodeKind.Try);
        return LocalizedText.Get(guarded ? "Questions.Place.Guarded" : "Questions.Place.Unguarded", language, from.Title, route);
    }

    // A faixa do primeiro trecho não tem nome de repositório no mapa; a tela troca pelo nome dele.
    private static string LaneName(string lane, UiLanguage language) => lane switch
    {
        FlowMapBuilder.MainLane => LocalizedText.Get("Questions.Lane.Main", language),
        FlowMapBuilder.OutsideLane => LocalizedText.Get("Questions.Lane.Outside", language),
        _ => lane
    };

    private static IReadOnlyList<string> SourcesOf(IEnumerable<FlowRule> rules) =>
        rules.SelectMany(r => r.Sources).Select(s => s.NodeId).Distinct(StringComparer.Ordinal).ToList();

    private static string Lines(IEnumerable<string> lines) => string.Join("\n", lines);

    // Confere as fontes de uma resposta contra o grafo: id que não existe sai. Resposta da IA sem
    // nenhuma fonte válida não é mostrada como resposta (vira "sem resposta no código levantado").
    public static FlowAnswer Verify(FlowGraph graph, FlowAnswer answer, UiLanguage language)
    {
        var ids = graph.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var valid = answer.Sources.Where(ids.Contains).Distinct(StringComparer.Ordinal).ToList();
        if (answer.Seal == AnswerSeal.None) return answer with { Sources = valid };
        if (valid.Count == 0)
            return new FlowAnswer(LocalizedText.Get("Questions.Verify.NoSupport", language), AnswerSeal.None, [],
                LocalizedText.Get("Questions.Verify.Discarded", language));
        return answer with { Sources = valid };
    }
}
