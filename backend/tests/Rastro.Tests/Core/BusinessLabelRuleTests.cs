using Rastro.Core;
using Rastro.Core.Business;
using Rastro.Core.Graph;
using Rastro.Core.Localization;

namespace Rastro.Tests.Core;

// Cenários CORE-BLR-NNN: a regra de texto da decisão (Fase 59), sem IA e sem ler a cultura da thread, e o que o
// tipo trocado muda na derivação. As condições são as das fixtures da visão de negócio e dos levantamentos reais.
public class BusinessLabelRuleTests
{
    private static readonly Guid SurveyId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static readonly BusinessSource Source = new(SurveyId, "loja", "aaa111", "main", []);

    [Theory] // CORE-BLR-001
    [InlineData("this.pedido.enviado", "Pedido enviado", "Pedido enviado", "Pedido enviado")]
    [InlineData("!(pedido.enviado)", "Pedido not enviado", "Pedido não enviado", "Pedido no enviado")]
    [InlineData("!cancelamento.DentroDoPrazo(id)", "Cancelamento not dentro do prazo", "Cancelamento não dentro do prazo", "Cancelamento no dentro do prazo")]
    [InlineData("request.Meio == \"cartao\"", "Request meio is \"cartao\"", "Request meio é \"cartao\"", "Request meio es \"cartao\"")]
    [InlineData("IsWithinCancellationWindow", "Is within cancellation window", "Is within cancellation window", "Is within cancellation window")]
    [InlineData("!IsWithinCancellationWindow", "Is not within cancellation window", "Não (is within cancellation window)", "No (is within cancellation window)")]
    [InlineData("request.Language is { } value && !UiLanguages.IsDefined(value)",
        "Request language is not empty and ui languages is not defined",
        "Request language não é vazio e não (ui languages is defined)",
        "Request language no es vacío y no (ui languages is defined)")]
    [InlineData("pedido == null", "Pedido is empty", "Pedido é vazio", "Pedido es vacío")]
    [InlineData("itens.Count > 10", "Itens count is greater than 10", "Itens count é maior que 10", "Itens count es mayor que 10")]
    [InlineData("!(a == b)", "A is not b", "A não é b", "A no es b")]
    [InlineData("!(a || b)", "Not (a or b)", "Não (a ou b)", "No (a o b)")]
    [InlineData("pago && ativo", "Pago and ativo", "Pago e ativo", "Pago y ativo")]
    public void Humanize_Condicao_TextoNosTresIdiomas(string condicao, string en, string pt, string es)
    {
        Assert.Equal(en, BusinessLabelRule.Humanize(condicao, UiLanguage.English));
        Assert.Equal(pt, BusinessLabelRule.Humanize(condicao, UiLanguage.Portuguese));
        Assert.Equal(es, BusinessLabelRule.Humanize(condicao, UiLanguage.Spanish));
    }

    [Fact] // CORE-BLR-002
    public void Humanize_CondicaoCompostaLonga_FicaComAPrimeiraParteEReticencias()
    {
        var condicao = "ClienteComCadastroCompletoEAtivoNaBase && PedidoComItensDisponiveisEmEstoqueParaEntrega"
                       + " && PagamentoConfirmadoPeloBanco && EnderecoValidadoPeloCorreio";

        var texto = BusinessLabelRule.Humanize(condicao, UiLanguage.Portuguese);

        Assert.Equal("Cliente com cadastro completo e ativo na base…", texto);
        Assert.True(texto.Length <= BusinessLabelRule.MaxBody);
    }

    [Fact] // CORE-BLR-003
    public void Humanize_TextoLongoSemOperador_CortaNoLimite()
    {
        var texto = BusinessLabelRule.Humanize(new string('a', 400), UiLanguage.English);

        Assert.Equal(BusinessLabelRule.MaxBody, texto.Length);
        Assert.EndsWith("…", texto);
    }

    [Fact] // CORE-BLR-004
    public void Humanize_MesmaEntrada_MesmaSaidaSemDependerDaCulturaDaThread()
    {
        var anterior = Thread.CurrentThread.CurrentUICulture;
        try
        {
            Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo("es");
            var comEs = BusinessLabelRule.Humanize("!pedido.enviado", UiLanguage.English);
            Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo("pt-BR");
            var comPt = BusinessLabelRule.Humanize("!pedido.enviado", UiLanguage.English);

            Assert.Equal("Pedido not enviado", comEs);
            Assert.Equal(comEs, comPt);
        }
        finally
        {
            Thread.CurrentThread.CurrentUICulture = anterior;
        }
    }

    [Fact] // CORE-BLR-005
    public void Humanize_Vazio_DevolveVazio()
    {
        Assert.Equal(string.Empty, BusinessLabelRule.Humanize("  ", UiLanguage.English));
        Assert.Equal(string.Empty, BusinessLabelRule.Humanize(null, UiLanguage.English));
    }

    // ---------- o tipo trocado na derivação ----------

    private static FlowNode M(string id, string label, NodeKind kind = NodeKind.Method) =>
        new(id, kind, label, Symbol: label, Location: new SourceLocation(label.Split('.')[0] + ".cs", 1));

    private static FlowGraph G(FlowNode[] nodes, FlowEdge[] edges) =>
        new(RastroSchema.Version, nodes[0].Id, nodes[0].Id, nodes, edges);

    private static FlowEdge E(string from, string to, int order, string? label = null) =>
        new(from, to, order, EdgeConfidence.Extracted, label);

    private static BusinessFlow Derive(FlowGraph graph, BusinessOverlay? overlay = null) =>
        BusinessFlowBuilder.Derive(graph, Source, BusinessViewOptions.Default, UiLanguage.English, overlay);

    private static FlowGraph Simples() => G(
        [M("m:e", "Ctrl.Cancelar", NodeKind.Entry), M("m:svc", "Svc.Calcular"), M("m:db", "Repo.Ler")],
        [E("m:e", "m:svc", 1), E("m:svc", "m:db", 1)]);

    [Fact] // CORE-BLR-006
    public void Derive_TipoConfirmadoNumNoQueNaoEraPasso_PassaASerPassoChave()
    {
        var overlay = new BusinessOverlay(new Dictionary<string, BusinessKind> { ["m:svc"] = BusinessKind.Step }, BusinessOverlay.Empty.KindProposals);

        var antes = Derive(Simples());
        var depois = Derive(Simples(), overlay);

        Assert.DoesNotContain(antes.Steps, s => s.NodeId == "m:svc");
        var passo = Assert.Single(depois.Steps, s => s.NodeId == "m:svc");
        Assert.Equal(BusinessKind.Step, passo.Kind);
        Assert.Equal("Svc.Calcular", passo.Label);
        Assert.Contains(depois.Edges, e => e.To == passo.Id);
    }

    [Fact] // CORE-BLR-007
    public void Derive_TipoConfirmadoNumPassoExistente_TrocaOTipoSemMudarOId()
    {
        var graph = G(
            [M("m:e", "Ctrl.A", NodeKind.Entry), M("m:db", "Repo.Gravar") with { Db = [new DbAccess("pedidos", DbOperation.Insert, 20)] }],
            [E("m:e", "m:db", 1)]);
        var overlay = new BusinessOverlay(new Dictionary<string, BusinessKind> { ["m:db"] = BusinessKind.Notification }, BusinessOverlay.Empty.KindProposals);

        var antes = Assert.Single(Derive(graph).Steps, s => s.NodeId == "m:db");
        var depois = Assert.Single(Derive(graph, overlay).Steps, s => s.NodeId == "m:db");

        Assert.Equal(BusinessKind.Step, antes.Kind);
        Assert.Equal(BusinessKind.Notification, depois.Kind);
        Assert.Equal(antes.Id, depois.Id);
    }

    [Fact] // CORE-BLR-008
    public void Derive_PropostaDeTipo_VemNoPassoSemMudarODesenho()
    {
        var graph = G(
            [M("m:e", "Ctrl.A", NodeKind.Entry), M("m:db", "Repo.Gravar") with { Db = [new DbAccess("pedidos", DbOperation.Insert, 20)] }],
            [E("m:e", "m:db", 1)]);
        var overlay = new BusinessOverlay(BusinessOverlay.Empty.KindOverrides,
            new Dictionary<string, KindProposal> { ["m:db"] = new(BusinessKind.Notification, "Avisa o cliente.") });

        var flow = Derive(graph, overlay);

        var passo = Assert.Single(flow.Steps, s => s.NodeId == "m:db");
        Assert.Equal(BusinessKind.Step, passo.Kind);
        Assert.Equal(new KindProposal(BusinessKind.Notification, "Avisa o cliente."), passo.KindProposal);
        Assert.False(passo.CodeChanged);
        Assert.All(flow.Steps.Where(s => s.NodeId != "m:db"), s => Assert.Null(s.KindProposal));
    }

    [Fact] // CORE-BLR-009
    public void Derive_TipoConfirmadoNoFim_UsaAChaveEndPipe()
    {
        var graph = G(
            [M("m:e", "Ctrl.A", NodeKind.Entry), M("m:db", "Repo.Gravar") with { Db = [new DbAccess("pedidos", DbOperation.Insert, 20)] }],
            [E("m:e", "m:db", 1)]);
        var overlay = new BusinessOverlay(new Dictionary<string, BusinessKind> { ["end|m:e"] = BusinessKind.Rejection }, BusinessOverlay.Empty.KindProposals);

        var flow = Derive(graph, overlay);

        Assert.Equal(BusinessKind.Start, flow.Steps[0].Kind);
        var fim = flow.Steps[^1];
        Assert.Equal(BusinessKind.Rejection, fim.Kind);
        Assert.Equal(BusinessFlowBuilder.StepIdOf(SurveyId, "end|m:e"), fim.Id);
    }
}
