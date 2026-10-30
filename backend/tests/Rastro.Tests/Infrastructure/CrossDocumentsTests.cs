using Microsoft.Extensions.DependencyInjection;
using Rastro.Core.Architecture;
using Rastro.Core.Prompts;
using Rastro.Core.Validation;
using Rastro.Tests.Api;

namespace Rastro.Tests.Infrastructure;

// Cenários INF-CROSS-NNN: C4 e documentação de um fluxo que atravessa sistemas (Fase 25).
[Collection(ApiCollection.Name)]
public class CrossDocumentsTests(ApiFixture fixture) : IAsyncLifetime
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

    private async Task<Rastro.Core.Graph.FlowGraph> GraphAsync()
    {
        await using var provider = CrossKit.Services(fixture, new FixtureSolutionSource(), _pedidos, _pagamentos);
        using var scope = provider.CreateScope();
        return (await CrossKit.AnalyzeAsync(scope, _pedidos, "PedidosController.Cancelar", 15, _pagamentos)).Graph;
    }

    [Fact] // INF-CROSS-019
    public async Task C4_FluxoEntreSistemas_CadaSistemaViraSoftwareSystemLigadoPorHttp()
    {
        var graph = await GraphAsync();

        var dsl = C4WorkspaceBuilder.Build(new PromptContext(_pedidos.Name, _pedidos.Solution, "PedidosController.Cancelar", null), graph, []);

        Assert.Contains($"sis1 = softwareSystem \"{_pagamentos.Name}\"", dsl);
        Assert.Contains("-> sis1 \"chama por HTTP: POST api/estornos\" \"HTTP\"", dsl);
        Assert.Contains("container sis1 \"C2-sis1\"", dsl);
        // Os passos do outro sistema não viram componentes do sistema do levantamento.
        Assert.DoesNotContain("component \"EstornosController\"", dsl);
        Assert.Contains("component \"PedidosController\"", dsl);
    }

    [Fact] // INF-CROSS-020
    public async Task Documentacao_FluxoEntreSistemas_MermaidGanhaUmSubgraphPorSistema()
    {
        var graph = await GraphAsync();
        var ok = new ValidationCheck("build", CheckStatus.Passed);
        var result = new ChangeValidationResult([], [], 0, 0, ok, ok, ValidationVerdict.Approved, 100);

        var doc = FlowDocumentBuilder.Build(new PromptContext(_pedidos.Name, _pedidos.Solution, "PedidosController.Cancelar", null),
            graph, graph, graph, result, null, DateTimeOffset.UtcNow);

        Assert.Contains($"subgraph sistema1[\"{_pedidos.Name}\"]", doc);
        Assert.Contains($"subgraph sistema2[\"{_pagamentos.Name}\"]", doc);
        Assert.Contains("|\"POST api/estornos\"|", doc);
    }

    [Fact] // INF-CROSS-021
    public async Task Documentacao_FluxoDeUmSistemaSo_MermaidSemSubgraph()
    {
        await using var provider = CrossKit.Services(fixture, new FixtureSolutionSource(), _pedidos);
        using var scope = provider.CreateScope();
        var graph = (await CrossKit.AnalyzeAsync(scope, _pedidos, "PedidosController.Cancelar", 15)).Graph;
        var ok = new ValidationCheck("build", CheckStatus.Passed);
        var result = new ChangeValidationResult([], [], 0, 0, ok, ok, ValidationVerdict.Approved, 100);

        var doc = FlowDocumentBuilder.Build(new PromptContext(_pedidos.Name, _pedidos.Solution, "PedidosController.Cancelar", null),
            graph, graph, graph, result, null, DateTimeOffset.UtcNow);

        Assert.DoesNotContain("subgraph", doc);
    }
}
