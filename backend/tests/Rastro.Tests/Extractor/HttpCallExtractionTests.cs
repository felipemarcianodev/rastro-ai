using Rastro.Core.Graph;
using Rastro.Core.Http;
using Rastro.Extractor;

namespace Rastro.Tests.Extractor;

// Cenários EXT-HTTP-NNN: chamadas HttpClient viram nós próprios, com verbo e rota (Fase 25).
public class HttpCallExtractionTests
{
    private static async Task<ExtractionResult> ExtractAsync(string fixture, string entry, bool httpCalls = true)
    {
        var solution = FixtureWorkspace.Load(fixture);
        await FixtureWorkspace.AssertCompilesAsync(solution);
        return await FlowExtractor.ExtractWithCallsAsync(solution, entry, new ExtractionOptions
        {
            HttpCalls = httpCalls,
            BaseDirectory = FixtureWorkspace.FixturesRoot
        });
    }

    private static HttpCallSite Site(ExtractionResult result, string verb, string? route) =>
        Assert.Single(result.HttpCalls, s => s.Call.Method == verb && s.Call.Route == route);

    [Fact] // EXT-HTTP-001
    public async Task Extrair_ChamadaComRotaLiteral_ViraNoHComVerboRotaEProfundidade()
    {
        var result = await ExtractAsync("ApiPedidos", "PedidosController.Cancelar");

        var site = Site(result, "POST", "api/estornos");
        var node = Assert.Single(result.Graph.Nodes, n => n.Id == site.NodeId);
        Assert.StartsWith("h:ApiPedidos/PedidosController.cs:", node.Id, StringComparison.Ordinal);
        Assert.Equal(NodeKind.Unresolved, node.Kind);
        Assert.Equal("POST api/estornos", node.Label);
        Assert.Equal(UnresolvedReason.NoMatchingEndpoint, node.Reason);
        Assert.Contains("PostAsJsonAsync", node.Detail);
        // Cancelar (0) chama EstornarAsync (1), que é quem faz a chamada.
        Assert.Equal(1, site.Depth);
    }

    [Fact] // EXT-HTTP-002
    public async Task Extrair_ChamadaComRota_ArestaDoPaiLevaOHttpEOLocalDaChamada()
    {
        var result = await ExtractAsync("ApiPedidos", "PedidosController.Cancelar");

        var site = Site(result, "POST", "api/estornos");
        var edge = Assert.Single(result.Graph.Edges, e => e.To == site.NodeId);
        Assert.Equal(new HttpCall("POST", "api/estornos"), edge.Http);
        Assert.Equal(EdgeConfidence.Extracted, edge.Confidence);
        Assert.Equal("ApiPedidos/PedidosController.cs", edge.Location!.File);
        Assert.Equal(FixtureLines.LineOf("ApiPedidos/PedidosController.cs", "EstornarAsync(int", "PostAsJsonAsync"), edge.Location.Line);
        var parent = Assert.Single(result.Graph.Nodes, n => n.Id == edge.From);
        Assert.Equal("PagamentosClient.EstornarAsync", parent.Label);
    }

    [Fact] // EXT-HTTP-003
    public async Task Extrair_RotaQueChegaPorParametro_FicaRouteNotResolvedNoMetodoDoCliente()
    {
        var result = await ExtractAsync("ApiPedidos", "PedidosController.Cancelar");

        // O controller passa "api/estornos/" + id, mas a chamada HTTP está em ConsultarAsync e lá a
        // rota é o parâmetro: o extrator não segue o valor entre métodos.
        var site = Site(result, "GET", null);
        var node = Assert.Single(result.Graph.Nodes, n => n.Id == site.NodeId);
        Assert.Equal("GET ?", node.Label);
        Assert.Equal(UnresolvedReason.RouteNotResolved, node.Reason);
        Assert.Equal(1, site.Depth);
    }

    [Fact] // EXT-HTTP-004
    public async Task Extrair_VariacoesDeRota_ResolveLiteralConstanteInterpoladaEBaseNaoConstante()
    {
        var result = await ExtractAsync("ApiPedidos", "ChamadasHttp.Variacoes");

        Site(result, "GET", "api/pedidos/lista");                 // literal
        Site(result, "PUT", "api/pedidos/itens");                 // interpolada só com constante
        Site(result, "DELETE", "api/pedidos/{}/itens");           // base não constante no começo sai; id vira {}
        Site(result, "GET", "api/estornos/{}");                   // concatenação com +
        Site(result, "PATCH", "api/estornos/ajuste");             // host, porta, query e fragmento saem
        Assert.All(result.HttpCalls, s => Assert.Equal(0, s.Depth));
    }

    [Fact] // EXT-HTTP-005
    public async Task Extrair_RotaVindaDeVariavel_FicaRouteNotResolved()
    {
        var result = await ExtractAsync("ApiPedidos", "ChamadasHttp.Variacoes");

        var get = Site(result, "GET", null);
        var delete = Site(result, "DELETE", null);
        foreach (var site in new[] { get, delete })
        {
            var node = Assert.Single(result.Graph.Nodes, n => n.Id == site.NodeId);
            Assert.Equal(UnresolvedReason.RouteNotResolved, node.Reason);
        }
    }

    [Fact] // EXT-HTTP-006
    public async Task Extrair_SendAsyncComRequisicaoInline_LeVerboERota()
    {
        var result = await ExtractAsync("ApiPedidos", "ChamadasHttp.Variacoes");

        Site(result, "POST", "api/estornos");
        Assert.Contains(result.HttpCalls, s => s.Call == new HttpCall("DELETE", null));
    }

    [Fact] // EXT-HTTP-007
    public async Task Extrair_SendAsyncComRequisicaoMontadaAntes_ViraSendSemRota()
    {
        var result = await ExtractAsync("ApiPedidos", "ChamadasHttp.Montada");

        var site = Assert.Single(result.HttpCalls);
        Assert.Equal(new HttpCall("SEND", null), site.Call);
        Assert.Equal(UnresolvedReason.RouteNotResolved, result.Graph.Nodes.Single(n => n.Id == site.NodeId).Reason);
    }

    [Fact] // EXT-HTTP-008
    public async Task Extrair_ChamadasHttpDesligado_GrafoIgualAoDeAntesSemNosH()
    {
        var off = await ExtractAsync("ApiPedidos", "ChamadasHttp.Externo", httpCalls: false);
        var plain = await FlowExtractor.ExtractAsync(
            FixtureWorkspace.Load("ApiPedidos"), "ChamadasHttp.Externo",
            new ExtractionOptions { BaseDirectory = FixtureWorkspace.FixturesRoot });

        Assert.Empty(off.HttpCalls);
        Assert.DoesNotContain(off.Graph.Nodes, n => n.Id.StartsWith("h:", StringComparison.Ordinal));
        Assert.All(off.Graph.Edges, e => Assert.Null(e.Http));
        var external = Assert.Single(off.Graph.Nodes, n => n.Kind == NodeKind.External);
        Assert.Equal("HttpClient.PostAsync", external.Label);
        Assert.Equal(FlowGraphJson.Serialize(plain), FlowGraphJson.Serialize(off.Graph));
    }

    [Fact] // EXT-HTTP-009
    public async Task Extrair_ChamadasHttpLigadoNoSistemaQueAtende_GatewayDeForaViraNoHSemDestino()
    {
        var result = await ExtractAsync("ApiPagamentos", "EstornosController.Criar");

        var site = Site(result, "POST", "v1/estornos");
        var node = Assert.Single(result.Graph.Nodes, n => n.Id == site.NodeId);
        Assert.Equal(UnresolvedReason.NoMatchingEndpoint, node.Reason);
        Assert.Equal(1, site.Depth);
        // As duas decisões do fluxo do sistema que atende continuam no grafo.
        Assert.Equal(2, result.Graph.Nodes.Count(n => n.Kind == NodeKind.Decision));
    }
}
