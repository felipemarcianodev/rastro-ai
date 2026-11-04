using System.Text.Json;
using Rastro.Core;
using Rastro.Core.Graph;

namespace Rastro.Tests.Core;

// Cenários CORE-MAP-NNN: o mapa do fluxo (Fase 30), na loja fictícia: a api-pedidos cancela o
// pedido e pede o estorno à api-pagamentos, que estorna por cartão (gateway) ou boleto.
public class FlowMapBuilderTests
{
    private const string Pag = "pagamentos";

    private static FlowNode M(string id, string label, NodeKind kind = NodeKind.Method, string? system = null) =>
        new(id, kind, label, Symbol: label, Location: new SourceLocation(label.Split('.')[0] + ".cs", 1), System: system);

    private static FlowNode Struct(string id, NodeKind kind, string label, string? condition = null, string? system = null) =>
        new(id, kind, label, Condition: condition, System: system);

    private static FlowEdge E(string from, string to, int order, string? label = null, HttpCall? http = null) =>
        new(from, to, order, EdgeConfidence.Extracted, label, Http: http);

    private static string P(string id) => SystemIds.Prefix(Pag, id);

    // Controller que só repassa para o serviço; o serviço decide, grava, pede o estorno e avisa.
    internal static FlowGraph Loja() => new(
        RastroSchema.Version, "PedidosController.Cancelar", "m:ctrl",
        [
            M("m:ctrl", "PedidosController.Cancelar", NodeKind.Entry),
            M("m:svc", "PedidoService.CancelarAsync"),
            Struct("if:svc:10", NodeKind.Decision, "if (pedido.Enviado)", "pedido.Enviado"),
            Struct("throw:svc:11", NodeKind.Throw, "throw PedidoJaEnviadoException"),
            Struct("if:svc:13", NodeKind.Decision, "if (pedido.Valor > 1000)", "pedido.Valor > 1000"),
            M("m:aud", "Auditoria.Anotar"),
            M("m:repo", "PedidoRepository.Atualizar"),
            new FlowNode("x:db", NodeKind.External, "DbContext.SaveChangesAsync"),
            M("m:cli", "PagamentosClient.EstornarAsync"),
            M("m:avi", "AvisosClient.Notificar"),
            new FlowNode("h:avi:3", NodeKind.Unresolved, "POST api/avisos", Reason: UnresolvedReason.NoMatchingEndpoint),
            M(P("m:est"), "EstornosController.Criar", NodeKind.Method, Pag),
            Struct(P("switch:est:5"), NodeKind.Decision, "switch (meio)", "meio", Pag),
            new FlowNode("x:gw", NodeKind.External, "GatewayCartao.Estornar"),
            M(P("m:bol"), "BoletoService.Registrar", NodeKind.Method, Pag)
        ],
        [
            E("m:ctrl", "m:svc", 1),
            E("m:svc", "if:svc:10", 1),
            E("if:svc:10", "throw:svc:11", 1, "then"),
            E("m:svc", "if:svc:13", 2),
            E("if:svc:13", "m:aud", 1, "then"),
            E("m:svc", "m:repo", 3),
            E("m:repo", "x:db", 1),
            E("m:svc", "m:cli", 4),
            E("m:cli", P("m:est"), 1, http: new HttpCall("POST", "api/estornos")),
            E("m:svc", "m:avi", 5),
            E("m:avi", "h:avi:3", 1, http: new HttpCall("POST", "api/avisos")),
            E(P("m:est"), P("switch:est:5"), 1),
            E(P("switch:est:5"), "x:gw", 1, "case Cartao"),
            E(P("switch:est:5"), P("m:bol"), 2, "case Boleto")
        ]);

    // Dois métodos chamam o mesmo sistema por rotas diferentes, e a mesma entrada é chamada duas vezes.
    internal static FlowGraph DuasChamadas() => new(
        RastroSchema.Version, "Job.Run", "m:e",
        [
            M("m:e", "Job.Run", NodeKind.Entry),
            M("m:a", "Saldo.Ler"),
            M("m:b", "Estorno.Pedir"),
            M(P("m:est"), "PagamentosController.Tratar", NodeKind.Method, Pag),
            Struct(P("if:1"), NodeKind.Decision, "if (ok)", "ok", Pag),
            Struct(P("throw:1"), NodeKind.Throw, "throw Erro", system: Pag)
        ],
        [
            E("m:e", "m:a", 1),
            E("m:e", "m:b", 2),
            E("m:a", P("m:est"), 1, http: new HttpCall("GET", "api/saldo")),
            E("m:b", P("m:est"), 1, http: new HttpCall("POST", "api/estornos")),
            E(P("m:est"), P("if:1"), 1),
            E(P("if:1"), P("throw:1"), 1, "then")
        ]);

    // Três saltos: a entrada chama A, A chama B e B chama C, um sistema por salto.
    private static FlowGraph TresSaltos()
    {
        static string S(string system, string id) => SystemIds.Prefix(system, id);
        return new FlowGraph(
            RastroSchema.Version, "Tela.Salvar", "m:e",
            [
                M("m:e", "Tela.Salvar", NodeKind.Entry),
                M(S("a", "m:1"), "A.Tratar", NodeKind.Method, "a"),
                M(S("a", "m:2"), "A.Passo", NodeKind.Method, "a"),
                M(S("b", "m:1"), "B.Tratar", NodeKind.Method, "b"),
                M(S("b", "m:2"), "B.Passo", NodeKind.Method, "b"),
                M(S("c", "m:1"), "C.Tratar", NodeKind.Method, "c"),
                new FlowNode("x:fora", NodeKind.External, "Servico.Chamar")
            ],
            [
                E("m:e", S("a", "m:1"), 1, http: new HttpCall("POST", "api/a")),
                E(S("a", "m:1"), S("a", "m:2"), 1),
                E(S("a", "m:1"), S("b", "m:1"), 2, http: new HttpCall("POST", "api/b")),
                E(S("b", "m:1"), S("b", "m:2"), 1),
                E(S("b", "m:1"), "x:fora", 2),
                E(S("b", "m:1"), S("c", "m:1"), 3, http: new HttpCall("POST", "api/c"))
            ]);
    }

    private static MapBlock Block(FlowMap map, string nodeId) => Assert.Single(map.Blocks, b => b.NodeId == nodeId);

    [Fact] // CORE-MAP-012
    public void Build_TresSaltos_CadaBlocoSabeOTrechoEAProfundidade()
    {
        var map = FlowMapBuilder.Build(TresSaltos());

        var entry = Block(map, "m:e");
        var a = Block(map, SystemIds.Prefix("a", "m:1"));
        var b = Block(map, SystemIds.Prefix("b", "m:1"));
        var c = Block(map, SystemIds.Prefix("c", "m:1"));
        Assert.Equal([0, 1, 2, 3], new[] { entry, a, b, c }.Select(x => x.Depth));
        Assert.All(new[] { entry, a, b, c }, x => Assert.Equal(x.Id, x.Segment));
        var step = Block(map, SystemIds.Prefix("b", "m:2"));
        Assert.Equal(b.Id, step.Segment);
        Assert.Equal(2, step.Depth);
        var outside = Block(map, "x:fora");
        Assert.Null(outside.Segment);
        Assert.Equal(3, outside.Depth);
    }

    [Fact] // CORE-MAP-013
    public void Build_MesmaEntradaChamadaDuasVezes_LigaAoTrechoJaMontado()
    {
        var map = FlowMapBuilder.Build(DuasChamadas());

        var entry = Assert.Single(map.Blocks, b => b.NodeId == P("m:est"));
        Assert.Single(map.Blocks, b => b.NodeId == P("if:1"));
        var calls = map.Links.Where(l => l.To == entry.Id).ToList();
        Assert.Equal(2, calls.Count);
        Assert.Equal(["GET", "POST"], calls.Select(l => l.Http!.Method));
        // O caminho passa pela decisão do trecho só na primeira chamada.
        Assert.All(map.Paths, p => Assert.Single(p.Choices));
    }

    [Fact] // CORE-MAP-014
    public void Build_MesmoBlocoChamaOMesmoDestinoDuasVezes_UmaLigacaoSo()
    {
        var graph = new FlowGraph(
            RastroSchema.Version, "Job.Run", "m:e",
            [M("m:e", "Job.Run", NodeKind.Entry), new FlowNode("x:db", NodeKind.External, "DbContext.SaveChangesAsync")],
            [E("m:e", "x:db", 1), E("m:e", "x:db", 2)]);

        Assert.Single(FlowMapBuilder.Build(graph).Links);
    }

    [Fact] // CORE-MAP-001
    public void Build_Loja_FaixasNaOrdemEmQueOFluxoEntraEForaPorUltimo()
    {
        var map = FlowMapBuilder.Build(Loja());

        Assert.Equal([FlowMapBuilder.MainLane, Pag, FlowMapBuilder.OutsideLane], map.Lanes.Select(l => l.Id));
        Assert.Equal([MapLaneKind.Code, MapLaneKind.Code, MapLaneKind.Outside], map.Lanes.Select(l => l.Kind));
        Assert.Null(map.Lanes[0].System);
        Assert.Equal(Pag, map.Lanes[1].System);
    }

    [Fact] // CORE-MAP-002
    public void Build_ControllerQueSoRepassa_ServicoFicaTransparenteEOsPassosSobem()
    {
        var map = FlowMapBuilder.Build(Loja());

        Assert.DoesNotContain(map.Blocks, b => b.NodeId == "m:svc");
        Assert.Contains("m:svc", Block(map, "m:ctrl").NodeIds);
        Assert.Equal(MapBlockKind.Entry, Block(map, "m:ctrl").Kind);
        Assert.Equal(MapBlockKind.Step, Block(map, "m:repo").Kind);
        Assert.Equal(MapBlockKind.Step, Block(map, "m:cli").Kind);
    }

    [Fact] // CORE-MAP-003
    public void Build_DecisaoQueLancaExcecao_SobeComODesfechoDeFalha()
    {
        var map = FlowMapBuilder.Build(Loja());

        var decision = Block(map, "if:svc:10");
        Assert.Equal(MapBlockKind.Decision, decision.Kind);
        Assert.Equal("pedido.Enviado", decision.Title);
        var then = decision.Outcomes![0];
        Assert.Equal("then", then.Label);
        Assert.True(then.Ends);
        Assert.True(then.Fails);
        Assert.Contains("throw:svc:11", decision.NodeIds);
        var otherwise = decision.Outcomes[1];
        Assert.Equal("else", otherwise.Label);
        Assert.False(otherwise.Ends);
    }

    [Fact] // CORE-MAP-004
    public void Build_DecisaoQueNaoMudaODesfecho_FicaForaDoMapa()
    {
        var map = FlowMapBuilder.Build(Loja());

        Assert.DoesNotContain(map.Blocks, b => b.NodeId == "if:svc:13");
        Assert.Contains(map.Blocks, b => b.NodeIds.Contains("if:svc:13"));
    }

    [Fact] // CORE-MAP-005
    public void Build_ChamadaHttpResolvida_ViraLigacaoDeChamadaComVerboERota()
    {
        var map = FlowMapBuilder.Build(Loja());

        var entry = Block(map, P("m:est"));
        Assert.Equal(MapBlockKind.Entry, entry.Kind);
        Assert.Equal(Pag, entry.Lane);
        var link = Assert.Single(map.Links, l => l.To == entry.Id);
        Assert.Equal(Block(map, "m:cli").Id, link.From);
        Assert.Equal(MapLinkKind.Call, link.Kind);
        Assert.Equal(new HttpCall("POST", "api/estornos"), link.Http);
    }

    [Fact] // CORE-MAP-006
    public void Build_ExternoEHttpSemDestino_VaoParaAFaixaDeFora()
    {
        var map = FlowMapBuilder.Build(Loja());

        var db = Block(map, "x:db");
        var avisos = Block(map, "h:avi:3");
        Assert.All([db, avisos], b =>
        {
            Assert.Equal(MapBlockKind.Outside, b.Kind);
            Assert.Equal(FlowMapBuilder.OutsideLane, b.Lane);
        });
        Assert.Contains(map.Links, l => l.From == Block(map, "m:repo").Id && l.To == db.Id && l.Kind == MapLinkKind.Call);
        Assert.Equal(new HttpCall("POST", "api/avisos"), Assert.Single(map.Links, l => l.To == avisos.Id).Http);
    }

    [Fact] // CORE-MAP-007
    public void Build_SwitchQueAlcancaForaDoCodigo_SobeComUmDesfechoPorCase()
    {
        var map = FlowMapBuilder.Build(Loja());

        var decision = Block(map, P("switch:est:5"));
        Assert.Equal(Pag, decision.Lane);
        Assert.Equal(["case Cartao", "case Boleto"], decision.Outcomes!.Select(o => o.Label));
        Assert.Equal(Block(map, "x:gw").Id, decision.Outcomes![0].To);
        Assert.Equal(Block(map, P("m:bol")).Id, decision.Outcomes[1].To);
        Assert.Contains(map.Links, l => l.From == decision.Id && l.To == Block(map, P("m:bol")).Id
                                         && l.Kind == MapLinkKind.Outcome && l.Label == "case Boleto");
    }

    [Fact] // CORE-MAP-008
    public void Build_Caminhos_UmPorCombinacaoComOsSemFalhaPrimeiro()
    {
        var map = FlowMapBuilder.Build(Loja());

        Assert.False(map.PathsTruncated);
        Assert.Equal(3, map.Paths.Count);
        Assert.Equal(["p1", "p2", "p3"], map.Paths.Select(p => p.Id));
        Assert.Equal([false, false, true], map.Paths.Select(p => p.Fails));

        var cartao = map.Paths[0];
        Assert.Contains(Block(map, "x:gw").Id, cartao.Blocks);
        Assert.DoesNotContain(Block(map, P("m:bol")).Id, cartao.Blocks);
        Assert.Contains(Block(map, "h:avi:3").Id, cartao.Blocks);

        var enviado = map.Paths[2];
        Assert.Equal([Block(map, "m:ctrl").Id, Block(map, "if:svc:10").Id], enviado.Blocks);
        Assert.Equal(new MapChoice(Block(map, "if:svc:10").Id, 0), Assert.Single(enviado.Choices));
    }

    [Fact] // CORE-MAP-009
    public void Build_MuitasDecisoes_CortaNoLimiteEAvisa()
    {
        var nodes = new List<FlowNode> { M("m:e", "Job.Run", NodeKind.Entry) };
        var edges = new List<FlowEdge>();
        for (var i = 1; i <= 5; i++)
        {
            nodes.Add(Struct($"if:{i}", NodeKind.Decision, $"if (c{i})", $"c{i}"));
            nodes.Add(new FlowNode($"x:{i}", NodeKind.External, $"Fila{i}.Publicar"));
            edges.Add(E("m:e", $"if:{i}", i));
            edges.Add(E($"if:{i}", $"x:{i}", 1, "then"));
        }

        var map = FlowMapBuilder.Build(new FlowGraph(RastroSchema.Version, "Job.Run", "m:e", nodes, edges));

        Assert.True(map.PathsTruncated);
        Assert.Equal(FlowMapBuilder.MaxPaths, map.Paths.Count);
    }

    [Fact] // CORE-MAP-010
    public void Build_MesmoGrafo_DaSempreOMesmoMapa()
    {
        static string Json(FlowMap map) => JsonSerializer.Serialize(map);

        Assert.Equal(Json(FlowMapBuilder.Build(Loja())), Json(FlowMapBuilder.Build(Loja())));
    }

    [Fact] // CORE-MAP-011
    public void Build_EntradaQueNaoExiste_MapaVazio()
    {
        var graph = Loja() with { EntryNodeId = "m:nada" };

        var map = FlowMapBuilder.Build(graph);

        Assert.Empty(map.Blocks);
        Assert.Empty(map.Paths);
    }

    [Fact] // CORE-MAP-012
    public void Build_MetodoQueChamaASiMesmo_NaoEntraEmLaco()
    {
        var graph = new FlowGraph(RastroSchema.Version, "A.Run", "m:a",
            [M("m:a", "A.Run", NodeKind.Entry), M("m:b", "B.Loop"), M("m:c", "C.Fim")],
            [E("m:a", "m:b", 1), E("m:b", "m:b", 1), E("m:b", "m:c", 2), E("m:a", "m:c", 2)]);

        var map = FlowMapBuilder.Build(graph);

        Assert.Contains(map.Blocks, b => b.NodeId == "m:b");
        Assert.Single(map.Paths);
    }
}
