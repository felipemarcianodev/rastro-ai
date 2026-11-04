using Rastro.Core;
using Rastro.Core.Graph;

namespace Rastro.Tests.Core;

// Cenários CORE-RULE-NNN: as regras do fluxo (Fase 32), na loja fictícia do mapa (CORE-MAP).
public class FlowRuleExtractorTests
{
    private static IReadOnlyList<FlowRule> Rules(FlowGraph graph) => FlowRuleExtractor.Extract(graph, FlowMapBuilder.Build(graph));

    [Fact] // CORE-RULE-001
    public void Extract_DecisaoQueLancaExcecao_ViraBloqueiaComAFonte()
    {
        var rules = Rules(FlowMapBuilderTests.Loja());

        var rule = Assert.Single(rules, r => r.Condition == "pedido.Enviado");
        Assert.Equal(RuleEffect.Blocks, rule.Effect);
        Assert.Equal("Se pedido.Enviado, encerra o fluxo com exceção.", rule.Text);
        Assert.False(rule.FromUser);
        Assert.False(rule.Duplicated);
        var source = Assert.Single(rule.Sources);
        Assert.Equal("if:svc:10", source.NodeId);
        Assert.Equal(FlowMapBuilder.MainLane, source.Lane);
    }

    [Fact] // CORE-RULE-002
    public void Extract_RamoQueChamaForaDoCodigo_ViraEfeitoColateral()
    {
        var rules = Rules(FlowMapBuilderTests.Loja());

        var rule = Assert.Single(rules, r => r.Condition == "meio");
        Assert.Equal(RuleEffect.SideEffect, rule.Effect);
        Assert.Equal("Quando meio é Cartao, chama GatewayCartao.Estornar.", rule.Text);
        Assert.Equal("pagamentos", rule.Sources[0].Lane);
    }

    [Fact] // CORE-RULE-003
    public void Extract_DecisaoForaDoMapa_NaoViraRegra()
    {
        var rules = Rules(FlowMapBuilderTests.Loja());

        Assert.Equal(2, rules.Count);
        Assert.DoesNotContain(rules, r => r.Condition == "pedido.Valor > 1000");
    }

    [Fact] // CORE-RULE-004
    public void Extract_RegraEscritaNoNo_TemPrecedenciaSobreOTextoGerado()
    {
        var graph = FlowMapBuilderTests.Loja();
        graph = graph with
        {
            Nodes = graph.Nodes.Select(n => n.Id == "if:svc:10"
                ? n with { Rule = new NodeRule("Pedido já enviado não pode ser cancelado.", ProposalOrigin.Manual) }
                : n).ToList()
        };

        var rule = Assert.Single(Rules(graph), r => r.Condition == "pedido.Enviado");

        Assert.Equal("Pedido já enviado não pode ser cancelado.", rule.Text);
        Assert.True(rule.FromUser);
    }

    [Fact] // CORE-RULE-005
    public void Extract_MesmaCondicaoEmDoisSistemas_ViraUmaRegraComDuasFontes()
    {
        static FlowEdge E(string from, string to, int order, string? label = null, HttpCall? http = null) =>
            new(from, to, order, EdgeConfidence.Extracted, label, Http: http);
        var api = SystemIds.Prefix("api", string.Empty);

        var graph = new FlowGraph(RastroSchema.Version, "Tela.cancelar", "m:tela",
            [
                new FlowNode("m:tela", NodeKind.Entry, "Tela.cancelar"),
                new FlowNode("if:tela", NodeKind.Decision, "if (this.pedido.enviado)", Condition: "this.pedido.enviado"),
                new FlowNode("ret:tela", NodeKind.Return, "return"),
                new FlowNode("m:http", NodeKind.Method, "PedidosApi.cancelar"),
                new FlowNode(api + "m:ctrl", NodeKind.Method, "PedidosController.Cancelar", System: "api"),
                new FlowNode(api + "if:ctrl", NodeKind.Decision, "if (pedido.Enviado)", Condition: "pedido.Enviado", System: "api"),
                new FlowNode(api + "throw:ctrl", NodeKind.Throw, "throw", System: "api")
            ],
            [
                E("m:tela", "if:tela", 1),
                E("if:tela", "ret:tela", 1, "then"),
                E("m:tela", "m:http", 2),
                E("m:http", api + "m:ctrl", 1, http: new HttpCall("POST", "api/pedidos/{}/cancelamento")),
                E(api + "m:ctrl", api + "if:ctrl", 1),
                E(api + "if:ctrl", api + "throw:ctrl", 1, "then")
            ]);

        var rule = Assert.Single(Rules(graph));

        Assert.True(rule.Duplicated);
        Assert.Equal(RuleEffect.Blocks, rule.Effect);
        Assert.Equal([FlowMapBuilder.MainLane, "api"], rule.Sources.Select(s => s.Lane));
    }

    [Fact] // CORE-RULE-006
    public void Normalize_IgnoraThisEspacosEMaiusculas()
    {
        Assert.Equal(FlowRuleExtractor.Normalize("this.pedido.Enviado "), FlowRuleExtractor.Normalize("pedido . enviado"));
    }
}
