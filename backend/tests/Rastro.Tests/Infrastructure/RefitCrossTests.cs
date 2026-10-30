using Microsoft.Extensions.DependencyInjection;
using Rastro.Core.Graph;
using Rastro.Core.Http;
using Rastro.Tests.Api;

namespace Rastro.Tests.Infrastructure;

// Cenários INF-REFIT-NNN: a costura entre sistemas quando a API chama a outra por uma interface Refit (Fase 29).
// api-pedidos (cliente Refit) chama api-pagamentos (controllers).
[Collection(ApiCollection.Name)]
public class RefitCrossTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Cancelar = "PedidosController.Cancelar";

    private CrossRepo _pedidos = null!;
    private CrossRepo _pagamentos = null!;
    private CrossRepo _outro = null!;

    public async Task InitializeAsync()
    {
        _pedidos = await CrossRepo.CreateAsync("pedidos", "ApiPedidosRefit");
        _pagamentos = await CrossRepo.CreateAsync("pagamentos", "ApiPagamentos");
        // Outro sistema cadastrado que não atende as rotas do cliente Refit.
        _outro = await CrossRepo.CreateAsync("ciclo", "ApiCiclo");
    }

    public async Task DisposeAsync()
    {
        await _pedidos.DisposeAsync();
        await _pagamentos.DisposeAsync();
        await _outro.DisposeAsync();
    }

    [Fact] // INF-REFIT-001
    public async Task Analisar_ClienteRefit_ChegaAoControllerDeDestinoComArestaHttp()
    {
        await using var provider = CrossKit.Services(fixture, new FixtureSolutionSource(), _pedidos, _pagamentos);
        using var scope = provider.CreateScope();

        var analysis = await CrossKit.AnalyzeAsync(scope, _pedidos, Cancelar, 15, _pagamentos);

        var criar = Assert.Single(analysis.Graph.Nodes, n => n.Label == "EstornosController.Criar");
        Assert.Equal(_pagamentos.Name, criar.System);
        var edge = Assert.Single(analysis.Graph.Edges, e => e.To == criar.Id);
        Assert.Equal(new HttpCall("POST", "api/estornos"), edge.Http);
        Assert.Equal("PedidosController.cs", edge.Location!.File);

        // {id} do Refit casa com {id:int} do controller de destino.
        var obter = Assert.Single(analysis.Graph.Nodes, n => n.Label == "EstornosController.Obter");
        Assert.Equal(new HttpCall("GET", "api/estornos/{}"), Assert.Single(analysis.Graph.Edges, e => e.To == obter.Id).Http);

        Assert.DoesNotContain(analysis.Graph.Nodes, n => n.Id.StartsWith("h:", StringComparison.Ordinal));
        Assert.DoesNotContain(analysis.Graph.Nodes, n => n.Reason == UnresolvedReason.NoImplementation);
        Assert.Equal(_pagamentos.Name, Assert.Single(analysis.Segments, s => s.EntryPoint.EndsWith("Criar", StringComparison.Ordinal)).Repository);
    }

    [Fact] // INF-REFIT-002
    public async Task Analisar_ClienteRefitSemRepositoriosNaCostura_ViraNoExternoESemNoH()
    {
        await using var provider = CrossKit.Services(fixture, new FixtureSolutionSource(), _pedidos, _pagamentos);
        using var scope = provider.CreateScope();

        var analysis = await CrossKit.AnalyzeAsync(scope, _pedidos, Cancelar, 15);

        // Sem costura, como o HttpClient: nó externo, nunca "sem implementação".
        Assert.Empty(analysis.Segments);
        Assert.Contains(analysis.Graph.Nodes, n => n.Kind == NodeKind.External && n.Label == "IPagamentosApi.Criar");
        Assert.DoesNotContain(analysis.Graph.Nodes, n => n.Reason == UnresolvedReason.NoImplementation);
    }

    [Fact] // INF-REFIT-003
    public async Task Analisar_ClienteRefitParaRepositorioSemARota_FicaNoMatchingEndpoint()
    {
        await using var provider = CrossKit.Services(fixture, new FixtureSolutionSource(), _pedidos, _outro);
        using var scope = provider.CreateScope();

        var analysis = await CrossKit.AnalyzeAsync(scope, _pedidos, Cancelar, 15, _outro);

        Assert.Empty(analysis.Segments);
        var hole = Assert.Single(analysis.Graph.Nodes, n => n.Label == "POST api/estornos");
        Assert.Equal(UnresolvedReason.NoMatchingEndpoint, hole.Reason);
    }
}
