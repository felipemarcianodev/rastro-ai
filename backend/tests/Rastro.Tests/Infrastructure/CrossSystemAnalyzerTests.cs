using Microsoft.Extensions.DependencyInjection;
using Rastro.Core.Graph;
using Rastro.Infrastructure.Analysis;
using Rastro.Tests.Api;

namespace Rastro.Tests.Infrastructure;

// Cenários INF-CROSS-NNN: a costura entre sistemas de ponta a ponta (Fase 25), com duas fixtures
// (api-pedidos chama api-pagamentos por HTTP), repositórios git temporários e uma solution aberta por vez.
[Collection(ApiCollection.Name)]
public class CrossSystemAnalyzerTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Cancelar = "PedidosController.Cancelar";
    private const string CriarEstorno = "Fixture.ApiPagamentos.EstornosController.Criar";

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

    private static string Prefixed(CrossRepo repo, string suffix) => SystemIds.Prefix(repo.Name, suffix);

    [Fact] // INF-CROSS-001
    public async Task Analisar_PedidosChamaPagamentos_ChegaAEstornosControllerCriarComIdsPrefixados()
    {
        var source = new FixtureSolutionSource();
        await using var provider = CrossKit.Services(fixture, source, _pedidos, _pagamentos);
        using var scope = provider.CreateScope();

        var analysis = await CrossKit.AnalyzeAsync(scope, _pedidos, Cancelar, 15, _pagamentos);

        var criar = Assert.Single(analysis.Graph.Nodes, n => n.Label == "EstornosController.Criar");
        Assert.Equal(_pagamentos.Name, criar.System);
        Assert.StartsWith(Prefixed(_pagamentos, "m:"), criar.Id, StringComparison.Ordinal);
        Assert.Equal("EstornosController.cs", criar.Location!.File);
        // O primeiro trecho segue sem prefixo e sem sistema.
        var cancelar = Assert.Single(analysis.Graph.Nodes, n => n.Label == "PedidosController.Cancelar");
        Assert.Null(cancelar.System);
        Assert.DoesNotContain("::", cancelar.Id);
        // As duas bifurcações do segundo sistema (cartão, boleto) entram no mesmo grafo.
        Assert.True(analysis.Graph.Nodes.Count(n => n.System == _pagamentos.Name && n.Kind == NodeKind.Decision) >= 2);
        Assert.All(analysis.Graph.Nodes.Where(n => n.System is not null), n => Assert.StartsWith(_pagamentos.Name + "::", n.Id, StringComparison.Ordinal));
        // Toda ponta de aresta existe no grafo (nenhuma aresta ficou apontando para o nó "h:" que saiu).
        var ids = analysis.Graph.Nodes.Select(n => n.Id).ToHashSet();
        Assert.All(analysis.Graph.Edges, e =>
        {
            Assert.Contains(e.From, ids);
            Assert.Contains(e.To, ids);
        });
    }

    [Fact] // INF-CROSS-002
    public async Task Analisar_ChamadaComPar_ArestaHttpTrocaONoHEGuardaVerboRotaELocal()
    {
        var source = new FixtureSolutionSource();
        await using var provider = CrossKit.Services(fixture, source, _pedidos, _pagamentos);
        using var scope = provider.CreateScope();

        var analysis = await CrossKit.AnalyzeAsync(scope, _pedidos, Cancelar, 15, _pagamentos);

        var criar = Assert.Single(analysis.Graph.Nodes, n => n.Label == "EstornosController.Criar");
        var edge = Assert.Single(analysis.Graph.Edges, e => e.To == criar.Id);
        Assert.Equal(new HttpCall("POST", "api/estornos"), edge.Http);
        Assert.Equal(EdgeConfidence.Extracted, edge.Confidence);
        Assert.Equal("PedidosController.cs", edge.Location!.File);
        Assert.Equal("PagamentosClient.EstornarAsync", analysis.Graph.Nodes.Single(n => n.Id == edge.From).Label);
        Assert.DoesNotContain(analysis.Graph.Nodes, n => n.Id.StartsWith("h:", StringComparison.Ordinal) && n.Label == "POST api/estornos");

        var segment = Assert.Single(analysis.Segments);
        Assert.Equal(_pagamentos.Name, segment.Repository);
        Assert.Equal(_pagamentos.Solution, segment.SolutionPath);
        Assert.Equal(CriarEstorno, segment.EntryPoint);
        Assert.Equal(await _pagamentos.HeadAsync(), segment.CommitHash);
        Assert.Equal("main", segment.Branch);
    }

    [Fact] // INF-CROSS-003
    public async Task Analisar_DuasSolutions_SoltaCadaUmaAntesDeAbrirAProxima()
    {
        var source = new FixtureSolutionSource();
        await using var provider = CrossKit.Services(fixture, source, _pedidos, _pagamentos);
        using var scope = provider.CreateScope();

        await CrossKit.AnalyzeAsync(scope, _pedidos, Cancelar, 15, _pagamentos);

        Assert.Equal(
            [$"open:{_pedidos.Name}", $"release:{_pedidos.Name}", $"open:{_pagamentos.Name}", $"release:{_pagamentos.Name}"],
            source.Events);
        Assert.Equal(1, source.MaxOpen);
    }

    [Fact] // INF-CROSS-004
    public async Task Analisar_SemRepositoriosNaCostura_GrafoIdenticoAoDeSempreESemTrechos()
    {
        var source = new FixtureSolutionSource();
        await using var provider = CrossKit.Services(fixture, source, _pedidos, _pagamentos);
        using var scope = provider.CreateScope();

        var plain = await new SolutionAnalyzer(source).AnalyzeAsync(CrossKit.TargetOf(scope, _pedidos), Cancelar, 15, CancellationToken.None);
        var analysis = await CrossKit.AnalyzeAsync(scope, _pedidos, Cancelar, 15);

        Assert.Equal(FlowGraphJson.Serialize(plain), FlowGraphJson.Serialize(analysis.Graph));
        Assert.Empty(analysis.Segments);
        // Sem costura a chamada HTTP continua sendo o nó externo de sempre.
        Assert.DoesNotContain(analysis.Graph.Nodes, n => n.Id.StartsWith("h:", StringComparison.Ordinal));
        Assert.All(analysis.Graph.Nodes, n => Assert.Null(n.System));
    }

    [Fact] // INF-CROSS-005
    public async Task Analisar_ProfundidadeQueNaoSobra_ChamadaViraDepthLimitESemSeguir()
    {
        var source = new FixtureSolutionSource();
        await using var provider = CrossKit.Services(fixture, source, _pedidos, _pagamentos);
        using var scope = provider.CreateScope();

        // Cancelar (0) chama EstornarAsync (1), que faz a chamada: com profundidade 2 não sobra nível para o outro sistema.
        var shallow = await CrossKit.AnalyzeAsync(scope, _pedidos, Cancelar, 2, _pagamentos);

        var hole = Assert.Single(shallow.Graph.Nodes, n => n.Label == "POST api/estornos");
        Assert.Equal(UnresolvedReason.DepthLimit, hole.Reason);
        Assert.Empty(shallow.Segments);
        Assert.DoesNotContain(shallow.Graph.Nodes, n => n.System is not null);

        // Com 4 sobra 2 níveis: o trecho entra, e o que passa do limite dentro dele fica como DepthLimit.
        var deeper = await CrossKit.AnalyzeAsync(scope, _pedidos, Cancelar, 4, _pagamentos);
        Assert.Single(deeper.Segments);
        Assert.Contains(deeper.Graph.Nodes, n => n.Label == "EstornosController.Criar");
    }

    [Fact] // INF-CROSS-006
    public async Task Analisar_RotaSemParEmNenhumRepositorio_FicaNoMatchingEndpointNoPontoDaChamada()
    {
        var source = new FixtureSolutionSource();
        await using var provider = CrossKit.Services(fixture, source, _pedidos, _pagamentos);
        using var scope = provider.CreateScope();

        var analysis = await CrossKit.AnalyzeAsync(scope, _pedidos, Cancelar, 15, _pagamentos);

        // O gateway de cartão fica num host de fora: nenhum repositório da costura o atende.
        var gateway = Assert.Single(analysis.Graph.Nodes, n => n.Label == "POST v1/estornos");
        Assert.Equal(NodeKind.Unresolved, gateway.Kind);
        Assert.Equal(UnresolvedReason.NoMatchingEndpoint, gateway.Reason);
        Assert.Equal(_pagamentos.Name, gateway.System);
        Assert.Contains(analysis.Graph.Edges, e => e.To == gateway.Id && e.Http == new HttpCall("POST", "v1/estornos"));
        // Rota montada em tempo de execução não tem o que casar.
        var dynamic = Assert.Single(analysis.Graph.Nodes, n => n.Label == "GET ?");
        Assert.Equal(UnresolvedReason.RouteNotResolved, dynamic.Reason);
    }

    [Fact] // INF-CROSS-007
    public async Task Analisar_RotaQueCasaComDoisRepositorios_FicaMultipleEndpointsSemEscolher()
    {
        await using var segunda = await CrossRepo.CreateAsync("pagamentos2", "ApiPagamentos");
        var source = new FixtureSolutionSource();
        await using var provider = CrossKit.Services(fixture, source, _pedidos, _pagamentos, segunda);
        using var scope = provider.CreateScope();

        var analysis = await CrossKit.AnalyzeAsync(scope, _pedidos, Cancelar, 15, _pagamentos, segunda);

        var hole = Assert.Single(analysis.Graph.Nodes, n => n.Label == "POST api/estornos");
        Assert.Equal(UnresolvedReason.MultipleEndpoints, hole.Reason);
        Assert.Contains($"{_pagamentos.Name}: EstornosController.Criar", hole.Detail);
        Assert.Contains($"{segunda.Name}: EstornosController.Criar", hole.Detail);
        Assert.Empty(analysis.Segments);
        Assert.DoesNotContain(analysis.Graph.Nodes, n => n.System is not null);
    }

    [Fact] // INF-CROSS-008
    public async Task Analisar_DoisEndpointsQueSeChamam_CicloCortadoNoPrimeiroRetorno()
    {
        await using var ciclo = await CrossRepo.CreateAsync("ciclo", "ApiCiclo");
        var source = new FixtureSolutionSource();
        await using var provider = CrossKit.Services(fixture, source, ciclo);
        using var scope = provider.CreateScope();

        // Ida chama Volta (por HTTP, no mesmo sistema), e Volta chama Ida de volta.
        var analysis = await CrossKit.AnalyzeAsync(scope, ciclo, "CicloController.Ida", 15, ciclo);

        // Volta entra como trecho; a chamada dela de volta a Ida cai no método que já está no grafo.
        var segment = Assert.Single(analysis.Segments);
        Assert.EndsWith("CicloController.Volta", segment.EntryPoint);
        var ida = Assert.Single(analysis.Graph.Nodes, n => n.Label == "CicloController.Ida");
        Assert.Null(ida.System);
        var volta = Assert.Single(analysis.Graph.Nodes, n => n.Label == "CicloController.Volta");
        Assert.Equal(ciclo.Name, volta.System);
        var back = Assert.Single(analysis.Graph.Edges, e => e.From == volta.Id);
        Assert.Equal(ida.Id, back.To);
        Assert.Equal(new HttpCall("POST", "api/ciclo/ida"), back.Http);
        Assert.Equal(analysis.Graph.Nodes.Count, analysis.Graph.Nodes.Select(n => n.Id).Distinct().Count());
    }

    [Fact] // INF-CROSS-009
    public async Task Analisar_RepositorioDeDestinoSemSolution_ChamadaFicaSemSeguirComExplicacao()
    {
        var source = new FixtureSolutionSource();
        await using var provider = CrossKit.Services(fixture, source, _pedidos, _pagamentos);
        using var scope = provider.CreateScope();
        File.Delete(Path.Combine(_pagamentos.Root, _pagamentos.Solution));

        var analysis = await CrossKit.AnalyzeAsync(scope, _pedidos, Cancelar, 15, _pagamentos);

        var hole = Assert.Single(analysis.Graph.Nodes, n => n.Label == "POST api/estornos");
        Assert.Equal(UnresolvedReason.NoMatchingEndpoint, hole.Reason);
        Assert.Contains("não tem solution", hole.Detail);
        Assert.Empty(analysis.Segments);
    }
}
