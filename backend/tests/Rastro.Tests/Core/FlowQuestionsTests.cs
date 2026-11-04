using Rastro.Core;
using Rastro.Core.Graph;

namespace Rastro.Tests.Core;

// Cenários CORE-ASK-NNN: perguntas prontas sobre o fluxo e a conferência das fontes (Fase 33).
public class FlowQuestionsTests
{
    private static IReadOnlyList<FlowQuestion> Questions(FlowGraph graph)
    {
        var map = FlowMapBuilder.Build(graph);
        return FlowQuestions.Suggest(graph, map, FlowRuleExtractor.Extract(graph, map));
    }

    [Fact] // CORE-ASK-001
    public void Suggest_Loja_TemPerguntasDosTresPapeisComFonteNoGrafo()
    {
        var graph = FlowMapBuilderTests.Loja();
        var ids = graph.Nodes.Select(n => n.Id).ToHashSet();

        var questions = Questions(graph);

        Assert.Contains(questions, q => q.Role == QuestionRole.TechLead);
        Assert.Contains(questions, q => q.Role == QuestionRole.Staff);
        Assert.Contains(questions, q => q.Role == QuestionRole.Manager);
        Assert.All(questions, q => Assert.All(q.Answer.Sources, s => Assert.Contains(s, ids)));
        Assert.Equal(questions.Select((_, i) => $"q{i + 1}"), questions.Select(q => q.Id));
    }

    [Fact] // CORE-ASK-002
    public void Suggest_QuandoERecusado_ListaAsRegrasQueBloqueiam()
    {
        var question = Assert.Single(Questions(FlowMapBuilderTests.Loja()), q => q.Text.StartsWith("Em que situações", StringComparison.Ordinal));

        Assert.Equal(AnswerSeal.Extracted, question.Answer.Seal);
        Assert.Contains("pedido.Enviado", question.Answer.Text);
        Assert.Contains("if:svc:10", question.Answer.Sources);
    }

    [Fact] // CORE-ASK-003
    public void Suggest_ChamadaParaOutroSistema_PerguntaDaFalhaDizSeHaTryCatch()
    {
        var question = Assert.Single(Questions(FlowMapBuilderTests.Loja()),
            q => q.Text == "O que acontece se o sistema pagamentos falhar?");

        Assert.Equal(QuestionRole.Staff, question.Role);
        Assert.Contains("POST api/estornos", question.Answer.Text);
        Assert.Contains("sem try/catch", question.Answer.Text);
    }

    [Fact] // CORE-ASK-006
    public void Suggest_VariasChamadasAoMesmoSistema_ViramUmaPerguntaComOsLugares()
    {
        var graph = FlowMapBuilderTests.DuasChamadas();

        var failures = Questions(graph).Where(q => q.Role == QuestionRole.Staff && q.Text.EndsWith("falhar?", StringComparison.Ordinal)).ToList();

        var question = Assert.Single(failures, q => q.Text == "O que acontece se o sistema pagamentos falhar?");
        Assert.Contains("Chamado de 2 lugares:", question.Answer.Text);
        Assert.Contains("GET api/saldo", question.Answer.Text);
        Assert.Contains("POST api/estornos", question.Answer.Text);
        Assert.Contains("m:a", question.Answer.Sources);
        Assert.Contains("m:b", question.Answer.Sources);
    }

    [Fact] // CORE-ASK-007
    public void Suggest_MetodosDoMesmoTipoDeFora_ViramUmaPerguntaEHttpSemDestinoOutra()
    {
        var questions = Questions(FlowMapBuilderTests.Loja());

        Assert.Single(questions, q => q.Text == "O que acontece se DbContext falhar?");
        Assert.Single(questions, q => q.Text == "O que acontece se uma chamada HTTP sem destino resolvido falhar?");
        Assert.Single(questions, q => q.Text == "O que acontece se GatewayCartao falhar?");
    }

    [Fact] // CORE-ASK-008
    public void Suggest_MuitosLugares_ListaDezEContaOsDemais()
    {
        const int total = 13;
        var nodes = new List<FlowNode> { new("m:e", NodeKind.Entry, "Job.Run"), new("x:db", NodeKind.External, "DbContext.SaveChangesAsync") };
        var edges = new List<FlowEdge>();
        for (var i = 0; i < total; i++)
        {
            nodes.Add(new FlowNode($"m:{i}", NodeKind.Method, $"Passo{i}.Rodar"));
            edges.Add(new FlowEdge("m:e", $"m:{i}", i, EdgeConfidence.Extracted));
            edges.Add(new FlowEdge($"m:{i}", "x:db", 1, EdgeConfidence.Extracted));
        }

        var question = Assert.Single(Questions(new FlowGraph(RastroSchema.Version, "Job.Run", "m:e", nodes, edges)), q => q.Text == "O que acontece se DbContext falhar?");

        Assert.Contains($"Chamado de {total} lugares:", question.Answer.Text);
        Assert.Contains("E mais 3 lugares.", question.Answer.Text);
    }

    [Fact] // CORE-ASK-004
    public void Suggest_SistemasEDesfechos_ContamFaixasEForaDoCodigo()
    {
        var questions = Questions(FlowMapBuilderTests.Loja());

        var systems = Assert.Single(questions, q => q.Text == "Por quais sistemas a funcionalidade passa?");
        Assert.Equal(AnswerSeal.Outside, systems.Answer.Seal);
        Assert.StartsWith("2 sistemas", systems.Answer.Text, StringComparison.Ordinal);
        var paths = Assert.Single(questions, q => q.Text.StartsWith("Quantos desfechos", StringComparison.Ordinal));
        Assert.Equal("3 caminhos de ponta a ponta; 1 termina com exceção.", paths.Answer.Text);
    }

    [Fact] // CORE-ASK-005
    public void Verify_FonteQueNaoExiste_SaiEFonteNenhumaViraSemResposta()
    {
        var graph = FlowMapBuilderTests.Loja();

        var partial = FlowQuestions.Verify(graph, new FlowAnswer("ok", AnswerSeal.Inferred, ["m:svc", "m:inventado"]));
        var none = FlowQuestions.Verify(graph, new FlowAnswer("ok", AnswerSeal.Inferred, ["m:inventado"]));

        Assert.Equal(["m:svc"], partial.Sources);
        Assert.Equal(AnswerSeal.Inferred, partial.Seal);
        Assert.Equal(AnswerSeal.None, none.Seal);
        Assert.Empty(none.Sources);
        Assert.NotNull(none.Gap);
    }
}
