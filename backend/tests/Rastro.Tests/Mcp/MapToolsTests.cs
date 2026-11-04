using Microsoft.Extensions.DependencyInjection;
using Rastro.Core.Graph;
using Rastro.Mcp;
using Rastro.Tests.Api;
using Rastro.Tests.Infrastructure;

namespace Rastro.Tests.Mcp;

// Cenários MCP-MAP-NNN: mapa e regras do fluxo pelo agente (Fases 30, 32 e 33), na loja fictícia
// extraída de verdade (api-pedidos cancela e pede o estorno à api-pagamentos).
[Collection(ApiCollection.Name)]
public class MapToolsTests(ApiFixture fixture) : IAsyncLifetime
{
    private CrossRepo _pedidos = null!;
    private CrossRepo _pagamentos = null!;

    public async Task InitializeAsync()
    {
        _pedidos = await CrossRepo.CreateAsync("pedidos", "ApiPedidos");
        _pagamentos = await CrossRepo.CreateAsync("pagamentos", "ApiPagamentos");
    }

    public async Task DisposeAsync()
    {
        await _pedidos.DisposeAsync();
        await _pagamentos.DisposeAsync();
    }

    [Fact] // MCP-MAP-001
    public async Task MapaERegras_LojaComCostura_FaixasDosDoisSistemasEAChamadaDoEstorno()
    {
        await using var provider = CrossKit.Services(fixture, new FixtureSolutionSource(), _pedidos, _pagamentos);
        await using var scope = provider.CreateAsyncScope();
        var analysis = await CrossKit.AnalyzeAsync(scope, _pedidos, "PedidosController.Cancelar", 15, [_pagamentos]);
        var survey = await CrossKit.SaveSurveyAsync(scope, _pedidos, "PedidosController.Cancelar", 15, analysis, [_pagamentos]);
        var tools = scope.ServiceProvider.GetRequiredService<RastroTools>();

        var map = FlowMapBuilder.Build(analysis.Graph);
        Assert.Equal(FlowMapBuilder.MainLane, map.Lanes[0].Id);
        Assert.Contains(map.Lanes, l => l.System == _pagamentos.Name);
        var call = Assert.Single(map.Links, l => l.Http is { Method: "POST", Route: "api/estornos" } && map.Blocks.Single(b => b.Id == l.To).Kind == MapBlockKind.Entry);
        Assert.Equal(_pagamentos.Name, map.Blocks.Single(b => b.Id == call.To).Lane);
        Assert.NotEmpty(map.Paths);

        var mapText = await tools.GetFlowMap(survey.Id);
        Assert.Contains($"Faixa {_pagamentos.Name}", mapText);
        Assert.Contains("POST api/estornos", mapText);

        var rulesText = await tools.GetFlowRules(survey.Id);
        Assert.False(string.IsNullOrWhiteSpace(rulesText));
        Assert.Equal("Levantamento não encontrado ou ainda sem grafo. Use list_surveys.", await tools.GetFlowMap(Guid.NewGuid()));
    }
}
