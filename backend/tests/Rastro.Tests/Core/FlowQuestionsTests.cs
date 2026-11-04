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
            q => q.Text == "O que acontece se a chamada a POST api/estornos falhar?");

        Assert.Equal(QuestionRole.Staff, question.Role);
        Assert.Contains("sem try/catch", question.Answer.Text);
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
