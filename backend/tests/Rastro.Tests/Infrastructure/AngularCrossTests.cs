using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rastro.Core.Graph;
using Rastro.Infrastructure.Analysis;
using Rastro.Infrastructure.Configuration;
using Rastro.Infrastructure.Repositories;
using Rastro.Tests.Api;

namespace Rastro.Tests.Infrastructure;

// Cenários INF-ANG-NNN: o aceite da Fase 26. Do clique em "Cancelar" na tela Angular, passando pela API de
// pedidos, até o gateway da API de pagamentos, num levantamento só, com o Node de verdade e as bifurcações
// dos três sistemas.
[Collection(ApiCollection.Name)]
public class AngularCrossTests(ApiFixture fixture) : IAsyncLifetime
{
    private AngularRepo _loja = null!;
    private CrossRepo _pedidos = null!;
    private CrossRepo _pagamentos = null!;

    public async Task InitializeAsync()
    {
        AngularRepo.RequireNode();
        _loja = await AngularRepo.CreateAsync();
        _pedidos = await CrossRepo.CreateAsync("pedidos", "ApiPedidos");
        _pagamentos = await CrossRepo.CreateAsync("pagamentos", "ApiPagamentos");
    }

    public async Task DisposeAsync()
    {
        await _loja.DisposeAsync();
        await _pedidos.DisposeAsync();
        await _pagamentos.DisposeAsync();
    }

    // Os serviços do Rastro com os três repositórios cadastrados e o extrator Angular apontando para o script
    // do repositório (o TypeScript vem do extractor-ts/node_modules, via RASTRO_TYPESCRIPT).
    private ServiceProvider Services(FixtureSolutionSource source)
    {
        var settings = new Dictionary<string, string?>();
        var repos = new (string Name, string Root)[] { (_loja.Name, _loja.Root), (_pedidos.Name, _pedidos.Root), (_pagamentos.Name, _pagamentos.Root) };
        for (var i = 0; i < repos.Length; i++)
        {
            settings[$"Rastro:Repositories:{i + 1}:Name"] = repos[i].Name;
            settings[$"Rastro:Repositories:{i + 1}:Path"] = repos[i].Root;
        }
        return fixture.McpServices(settings, services =>
        {
            services.AddSingleton<ISolutionSource>(source);
            services.AddSingleton<ITypeScriptExtractor>(sp => new TypeScriptExtractor(
                sp.GetRequiredService<IOptions<RastroOptions>>(), NullLogger<TypeScriptExtractor>.Instance, AngularRepo.ScriptPath));
        });
    }

    private ResolvedTarget Target(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<RepositoryCatalog>().Resolve(_loja.Name, AngularRepo.TsConfig).Target!;

    [Fact] // INF-ANG-001
    public async Task Analisar_AngularPedidosPagamentos_AtravessaOsTresSistemasNumLevantamentoSo()
    {
        var source = new FixtureSolutionSource();
        await using var provider = Services(source);
        using var scope = provider.CreateScope();
        var target = Target(scope);
        Assert.Equal(SourceLanguage.Angular, target.Language);

        var analysis = await scope.ServiceProvider.GetRequiredService<CrossSystemAnalyzer>()
            .AnalyzeAsync(target, AngularRepo.Entry, 15, [_pedidos.Name, _pagamentos.Name], CancellationToken.None);

        // Sistema 1 (Angular): o clique, a guarda do template (o @if que esconde o botão) e a decisão do método.
        var click = Assert.Single(analysis.Graph.Nodes, n => n.Label == "DetalhePedidoComponent.cancelar");
        Assert.Null(click.System);
        Assert.Equal(analysis.Graph.EntryNodeId, click.Id);
        var screenGuards = analysis.Graph.Nodes.Where(n => n.System is null && n.Kind == NodeKind.Decision).ToList();
        Assert.Contains(screenGuards, n => n.Location!.File.EndsWith(".html", StringComparison.Ordinal));
        Assert.Contains(screenGuards, n => n.Location!.File.EndsWith(".ts", StringComparison.Ordinal));

        // Sistema 2 (ApiPedidos): a chamada POST do Angular cai em PedidosController.Cancelar.
        var cancelar = Assert.Single(analysis.Graph.Nodes, n => n.Label == "PedidosController.Cancelar");
        Assert.Equal(_pedidos.Name, cancelar.System);
        var toPedidos = Assert.Single(analysis.Graph.Edges, e => e.To == cancelar.Id);
        Assert.Equal(new HttpCall("POST", "api/pedidos/{}/cancelamento"), toPedidos.Http);
        Assert.Equal("PedidosService.cancelar", analysis.Graph.Nodes.Single(n => n.Id == toPedidos.From).Label);

        // Sistema 3 (ApiPagamentos): o estorno, com as bifurcações dele.
        var estorno = Assert.Single(analysis.Graph.Nodes, n => n.Label == "EstornosController.Criar");
        Assert.Equal(_pagamentos.Name, estorno.System);
        Assert.True(analysis.Graph.Nodes.Count(n => n.System == _pagamentos.Name && n.Kind == NodeKind.Decision) >= 2);

        // Bifurcações nos três sistemas.
        foreach (var system in new string?[] { null, _pedidos.Name, _pagamentos.Name })
            Assert.Contains(analysis.Graph.Nodes, n => n.System == system && n.Kind == NodeKind.Decision);

        // Os trechos além do primeiro: pedidos e pagamentos, nessa ordem, cada um com o commit lido.
        Assert.Equal([_pedidos.Name, _pagamentos.Name], analysis.Segments.Select(s => s.Repository));
        Assert.Equal(await _pedidos.HeadAsync(), analysis.Segments[0].CommitHash);
        Assert.Equal(await _pagamentos.HeadAsync(), analysis.Segments[1].CommitHash);

        // Nenhum nó "h:" sobrou para as chamadas costuradas e toda ponta de aresta existe.
        Assert.DoesNotContain(analysis.Graph.Nodes, n => n.Label == "POST api/pedidos/{}/cancelamento");
        var ids = analysis.Graph.Nodes.Select(n => n.Id).ToHashSet();
        Assert.All(analysis.Graph.Edges, e =>
        {
            Assert.Contains(e.From, ids);
            Assert.Contains(e.To, ids);
        });

        // O Roslyn só abriu as duas solutions .NET, uma por vez; o Angular não passou por ele.
        Assert.Equal(1, source.MaxOpen);
        Assert.DoesNotContain(source.Events, e => e.EndsWith(_loja.Name, StringComparison.Ordinal));
    }

    [Fact] // INF-ANG-002
    public async Task Analisar_AngularSemListaDeCostura_DevolveSoOTrechoAngularComAChamadaNaoResolvida()
    {
        var source = new FixtureSolutionSource();
        await using var provider = Services(source);
        using var scope = provider.CreateScope();

        var analysis = await scope.ServiceProvider.GetRequiredService<CrossSystemAnalyzer>()
            .AnalyzeAsync(Target(scope), AngularRepo.Entry, 15, [], CancellationToken.None);

        Assert.Empty(analysis.Segments);
        Assert.All(analysis.Graph.Nodes, n => Assert.Null(n.System));
        Assert.Contains(analysis.Graph.Nodes, n => n.Kind == NodeKind.Unresolved && n.Label == "POST api/pedidos/{}/cancelamento");
        Assert.Empty(source.Events);
    }

    [Fact] // INF-ANG-003
    public async Task Analisar_AngularComRepositorioSemRotaCorrespondente_ChamadaFicaNaoResolvida()
    {
        var source = new FixtureSolutionSource();
        await using var provider = Services(source);
        using var scope = provider.CreateScope();

        // Só a API de pagamentos na lista: nada atende api/pedidos/{}/cancelamento, então a chamada fica no grafo.
        var analysis = await scope.ServiceProvider.GetRequiredService<CrossSystemAnalyzer>()
            .AnalyzeAsync(Target(scope), AngularRepo.Entry, 15, [_pagamentos.Name], CancellationToken.None);

        Assert.Empty(analysis.Segments);
        var hole = Assert.Single(analysis.Graph.Nodes, n => n.Kind == NodeKind.Unresolved && n.Label == "POST api/pedidos/{}/cancelamento");
        Assert.Equal(UnresolvedReason.NoMatchingEndpoint, hole.Reason);
    }
}
