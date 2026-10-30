using Rastro.Core.Graph;
using Rastro.Core.Http;
using Rastro.Extractor;

namespace Rastro.Tests.Extractor;

// Cenários EXT-REFIT-NNN: método de interface do Refit vira chamada HTTP, com verbo e template do atributo (Fase 29).
public class RefitCallExtractionTests
{
    private static async Task<ExtractionResult> ExtractAsync(string fixture, string entry, bool httpCalls = true, bool compiles = true)
    {
        var solution = FixtureWorkspace.Load(fixture);
        if (compiles) await FixtureWorkspace.AssertCompilesAsync(solution);
        return await FlowExtractor.ExtractWithCallsAsync(solution, entry, new ExtractionOptions
        {
            HttpCalls = httpCalls,
            BaseDirectory = FixtureWorkspace.FixturesRoot
        });
    }

    [Fact] // EXT-REFIT-001
    public async Task Extrair_InterfaceRefitComPostEGet_ViraNoHComVerboETemplateDoAtributo()
    {
        var result = await ExtractAsync("ApiPedidosRefit", "PedidosController.Cancelar");

        var post = Assert.Single(result.HttpCalls, s => s.Call == new HttpCall("POST", "api/estornos"));
        var node = Assert.Single(result.Graph.Nodes, n => n.Id == post.NodeId);
        Assert.StartsWith("h:ApiPedidosRefit/PedidosController.cs:", node.Id, StringComparison.Ordinal);
        Assert.Equal(NodeKind.Unresolved, node.Kind);
        Assert.Equal("POST api/estornos", node.Label);
        Assert.Equal(UnresolvedReason.NoMatchingEndpoint, node.Reason);
        Assert.Equal(0, post.Depth);

        // {id} vira o buraco da chamada; a query sai.
        Assert.Single(result.HttpCalls, s => s.Call == new HttpCall("GET", "api/estornos/{}"));
        // Nenhum passo ficou "sem implementação".
        Assert.DoesNotContain(result.Graph.Nodes, n => n.Reason == UnresolvedReason.NoImplementation);
    }

    [Fact] // EXT-REFIT-002
    public async Task Extrair_InterfaceRefit_ArestaDoPaiLevaOHttpEOLocalDaChamada()
    {
        var result = await ExtractAsync("ApiPedidosRefit", "PedidosController.Cancelar");

        var site = Assert.Single(result.HttpCalls, s => s.Call.Method == "POST");
        var edge = Assert.Single(result.Graph.Edges, e => e.To == site.NodeId);
        Assert.Equal(new HttpCall("POST", "api/estornos"), edge.Http);
        Assert.Equal(EdgeConfidence.Extracted, edge.Confidence);
        Assert.Equal("ApiPedidosRefit/PedidosController.cs", edge.Location!.File);
        Assert.Equal(FixtureLines.LineOf("ApiPedidosRefit/PedidosController.cs", "public async Task Cancelar", "pagamentos.Criar("), edge.Location.Line);
    }

    [Fact] // EXT-REFIT-003
    public async Task Extrair_DeleteECoringaNoTemplate_LeVerboEBuracos()
    {
        var result = await ExtractAsync("ApiPedidosRefit", "PedidosController.Variacoes");

        Assert.Contains(result.HttpCalls, s => s.Call == new HttpCall("DELETE", "api/estornos/{}"));
        Assert.Contains(result.HttpCalls, s => s.Call == new HttpCall("GET", "api/ajuda/{}"));
    }

    [Fact] // EXT-REFIT-004
    public async Task Extrair_MetodoSemAtributoOuComAtributoDeOutroNamespace_NaoViraChamadaHttp()
    {
        var result = await ExtractAsync("ApiPedidosRefit", "PedidosController.Variacoes");

        // Sincronizar (sem atributo) e Local ([Outro.Get]) seguem a regra de interface sem implementação.
        Assert.Equal(2, result.Graph.Nodes.Count(n => n.Reason == UnresolvedReason.NoImplementation));
        Assert.Equal(2, result.HttpCalls.Count);
    }

    [Fact] // EXT-REFIT-005
    public async Task Extrair_AtributoSemSimboloMasComUsingRefit_AindaViraChamadaHttp()
    {
        // A dll do Refit não foi resolvida: a fixture não compila, e o atributo só tem o nome e o using.
        var result = await ExtractAsync("ApiPedidosRefitSemDll", "PedidosController.Cancelar", compiles: false);

        var site = Assert.Single(result.HttpCalls);
        Assert.Equal(new HttpCall("POST", "api/estornos"), site.Call);
    }

    [Fact] // EXT-REFIT-006
    public async Task Extrair_ChamadasHttpDesligado_InterfaceRefitViraExternoESemHttp()
    {
        var result = await ExtractAsync("ApiPedidosRefit", "PedidosController.Cancelar", httpCalls: false);

        Assert.Empty(result.HttpCalls);
        Assert.DoesNotContain(result.Graph.Nodes, n => n.Id.StartsWith("h:", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Graph.Nodes, n => n.Reason == UnresolvedReason.NoImplementation);
        Assert.Contains(result.Graph.Nodes, n => n.Kind == NodeKind.External && n.Label == "IPagamentosApi.Criar");
        Assert.All(result.Graph.Edges, e => Assert.Null(e.Http));
    }
}
