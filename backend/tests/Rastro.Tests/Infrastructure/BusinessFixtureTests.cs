using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rastro.Core.Business;
using Rastro.Core.Localization;
using Rastro.Core.Graph;
using Rastro.Infrastructure.Analysis;
using Rastro.Infrastructure.Configuration;
using Rastro.Infrastructure.Repositories;
using Rastro.Tests.Api;

namespace Rastro.Tests.Infrastructure;

// Cenários INF-BIZ-NNN: as fixtures da visão de negócio (Fase 55). O levantamento sai de
// DetalhePedidoComponent.cancelar, segue para NegocioPedidos e depois para NegocioPagamentos, e o teste confere
// o material que a derivação dos passos-chave usa no grafo BRUTO (FlowGraph, sem a derivação de negócio).
//
// Nós-chave do grafo bruto (Kind | Label | Symbol | Location):
//   Entrada Angular    | Entry    | DetalhePedidoComponent.cancelar | DetalhePedidoComponent.cancelar | detalhe-pedido.component.ts:16
//   Guarda do template | Decision | if (!(pedido.enviado))          | sem Symbol, Condition "!(pedido.enviado)" | detalhe-pedido.component.html:3
//   Guarda do método   | Decision | if (this.pedido.enviado)        | sem Symbol, Condition "this.pedido.enviado" | detalhe-pedido.component.ts:17
//   return antecipado  | Return   | return                          | sem Symbol | detalhe-pedido.component.ts:17
//   Chamada HTTP       | Method   | PedidosService.cancelar         | PedidosService.cancelar | pedidos.service.ts:9 (aresta POST api/pedidos/{}/cancelamento)
//   Entrada pedidos    | Method   | PedidosController.Cancelar      | Fixture.NegocioPedidos.PedidosController.Cancelar(int) | PedidosController.cs:12
//   Prazo (decisão)    | Decision | if (!cancelamento.DentroDoPrazo(id)) | sem Symbol, Condition "!cancelamento.DentroDoPrazo(id)" | PedidosController.cs:14
//   Prazo (consulta)   | Method   | CancelamentoService.DentroDoPrazo | Fixture.NegocioPedidos.CancelamentoService.DentroDoPrazo(int) | Servicos.cs:10
//   Recusa             | Return   | return BadRequest("Fora do prazo de cancelamento.") | sem Symbol | PedidosController.cs:15
//   Cliente HTTP       | Method   | PagamentosClient.EstornarAsync  | Fixture.NegocioPedidos.PagamentosClient.EstornarAsync(int) | Servicos.cs:27 (aresta POST api/estornos)
//   Gravação (Db)      | Method   | CancelamentoService.CancelarAsync | Fixture.NegocioPedidos.CancelamentoService.CancelarAsync(int) | Servicos.cs:13 (Db com 1 acesso)
//   Entrada pagamentos | Method   | EstornosController.Criar        | Fixture.NegocioPagamentos.EstornosController.Criar(EstornoRequest) | EstornosController.cs:13
//   Cartão (decisão)   | Decision | if (request.Meio == "cartao")   | sem Symbol, Condition `request.Meio == "cartao"` | Servicos.cs:33
//   E-mail             | Method   | EmailService.SendAsync          | Fixture.NegocioPagamentos.EmailService.SendAsync(string, string) | Servicos.cs:26
//                        (a interface IEmailService já vem resolvida para a implementação EmailService pelo DI)
//   Fim do caminho     | Method   | ControllerBase.Ok               | Microsoft.AspNetCore.Mvc.ControllerBase.Ok() | Mvc.cs:32 (um nó por sistema, sem nó Return)
[Collection(ApiCollection.Name)]
public class BusinessFixtureTests(ApiFixture fixture) : IAsyncLifetime
{
    private AngularRepo _loja = null!;
    private CrossRepo _pedidos = null!;
    private CrossRepo _pagamentos = null!;

    public async Task InitializeAsync()
    {
        AngularRepo.RequireNode();
        _loja = await AngularRepo.CreateAsync("negocio", fixture: AngularRepo.BusinessFixture);
        _pedidos = await CrossRepo.CreateAsync("pedidos", "NegocioPedidos");
        _pagamentos = await CrossRepo.CreateAsync("pagamentos", "NegocioPagamentos");
    }

    public async Task DisposeAsync()
    {
        await _loja.DisposeAsync();
        await _pedidos.DisposeAsync();
        await _pagamentos.DisposeAsync();
    }

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

    private async Task<CrossAnalysis> AnalyzeAsync()
    {
        var source = new FixtureSolutionSource();
        await using var provider = Services(source);
        using var scope = provider.CreateScope();
        var target = scope.ServiceProvider.GetRequiredService<RepositoryCatalog>().Resolve(_loja.Name, AngularRepo.TsConfig).Target!;
        return await scope.ServiceProvider.GetRequiredService<CrossSystemAnalyzer>()
            .AnalyzeAsync(target, AngularRepo.Entry, 15, [_pedidos.Name, _pagamentos.Name], UiLanguage.Portuguese, CancellationToken.None);
    }

    [Fact] // INF-BIZ-001
    public async Task Analisar_FixturesDeNegocio_CosturaAsTresEntradasEOsDoisSaltosHttp()
    {
        var analysis = await AnalyzeAsync();
        var graph = analysis.Graph;

        var entrada = Assert.Single(graph.Nodes, n => n.Label == "DetalhePedidoComponent.cancelar");
        Assert.Null(entrada.System);
        Assert.Equal(graph.EntryNodeId, entrada.Id);
        var cancelar = Assert.Single(graph.Nodes, n => n.Label == "PedidosController.Cancelar");
        Assert.Equal(_pedidos.Name, cancelar.System);
        var criar = Assert.Single(graph.Nodes, n => n.Label == "EstornosController.Criar");
        Assert.Equal(_pagamentos.Name, criar.System);

        var primeiroSalto = Assert.Single(graph.Edges, e => e.To == cancelar.Id);
        Assert.Equal(new HttpCall("POST", "api/pedidos/{}/cancelamento"), primeiroSalto.Http);
        Assert.Equal("PedidosService.cancelar", graph.Nodes.Single(n => n.Id == primeiroSalto.From).Label);

        var segundoSalto = Assert.Single(graph.Edges, e => e.To == criar.Id);
        Assert.Equal("POST", segundoSalto.Http!.Method);
        Assert.Equal("api/estornos", segundoSalto.Http.Route);
        var cliente = graph.Nodes.Single(n => n.Id == segundoSalto.From);
        Assert.Equal("PagamentosClient.EstornarAsync", cliente.Label);
        Assert.Equal(_pedidos.Name, cliente.System);

        Assert.Equal([_pedidos.Name, _pagamentos.Name], analysis.Segments.Select(s => s.Repository));
        Assert.DoesNotContain(graph.Nodes, n => n.Kind == NodeKind.Unresolved);
    }

    [Fact] // INF-BIZ-002
    public async Task Analisar_FixtureAngularDeNegocio_TrazAGuardaDoTemplateEOReturnAntesDoHttp()
    {
        var graph = (await AnalyzeAsync()).Graph;
        var tela = graph.Nodes.Where(n => n.System is null).ToList();

        // [disabled]="pedido.enviado" no template vira a guarda negada.
        var guardaTemplate = Assert.Single(tela, n => n.Kind == NodeKind.Decision && n.Location!.File.EndsWith(".html", StringComparison.Ordinal));
        Assert.Equal("!(pedido.enviado)", guardaTemplate.Condition);

        // if (this.pedido.enviado) return; antes da chamada, com o Return como destino do ramo.
        var guardaMetodo = Assert.Single(tela, n => n.Kind == NodeKind.Decision && n.Location!.File.EndsWith(".ts", StringComparison.Ordinal));
        Assert.Equal("this.pedido.enviado", guardaMetodo.Condition);
        var retorno = Assert.Single(tela, n => n.Kind == NodeKind.Return);
        Assert.Contains(graph.Edges, e => e.From == guardaMetodo.Id && e.To == retorno.Id);
        Assert.EndsWith("detalhe-pedido.component.ts", retorno.Location!.File);
    }

    [Fact] // INF-BIZ-003
    public async Task Analisar_FixturePedidos_TrazADecisaoDoPrazoEARecusaComBadRequest()
    {
        var graph = (await AnalyzeAsync()).Graph;

        var prazo = Assert.Single(graph.Nodes, n => n.System == _pedidos.Name && n.Kind == NodeKind.Decision);
        Assert.Contains("DentroDoPrazo", prazo.Condition);
        Assert.Contains(graph.Nodes, n => n.Label == "CancelamentoService.DentroDoPrazo" && n.System == _pedidos.Name);

        var recusa = Assert.Single(graph.Nodes, n => n.System == _pedidos.Name && n.Kind == NodeKind.Return);
        Assert.StartsWith("return BadRequest(", recusa.Label);
        Assert.Contains(graph.Edges, e => e.From == prazo.Id && e.To == recusa.Id);
    }

    [Fact] // INF-BIZ-004
    public async Task Analisar_FixturePagamentos_TrazADecisaoDoCartaoEOEmailDeConfirmacao()
    {
        var graph = (await AnalyzeAsync()).Graph;

        var cartao = Assert.Single(graph.Nodes, n => n.System == _pagamentos.Name && n.Kind == NodeKind.Decision);
        Assert.Equal("request.Meio == \"cartao\"", cartao.Condition);

        // IEmailService.SendAsync já vem resolvido para a implementação registrada no DI, e só o ramo do
        // cartão chega a ele: o outro ramo termina sem notificar (a decisão muda o resultado).
        var email = Assert.Single(graph.Nodes, n => n.Label == "EmailService.SendAsync");
        Assert.Equal(_pagamentos.Name, email.System);
        Assert.Contains("EmailService.SendAsync", email.Symbol);
        var saidas = graph.Edges.Where(e => e.From == cartao.Id).ToList();
        Assert.Equal(email.Id, Assert.Single(saidas).To);
        Assert.Equal("then", saidas[0].Label);
    }

    [Fact] // INF-BIZ-005
    public async Task Analisar_FixturePedidos_TrazAEscritaDoCancelamentoNoBanco()
    {
        var graph = (await AnalyzeAsync()).Graph;

        var gravacao = Assert.Single(graph.Nodes, n => n.Label == "CancelamentoService.CancelarAsync");
        Assert.Equal(_pedidos.Name, gravacao.System);
        Assert.NotNull(gravacao.Db);
        Assert.NotEmpty(gravacao.Db);
        // O fim do caminho de pedidos: o controller devolve Ok depois de gravar.
        Assert.Contains(graph.Nodes, n => n.System == _pedidos.Name && n.Label == "ControllerBase.Ok");
    }

    [Fact] // INF-BIZ-006
    public async Task Derivar_FixturesDeNegocio_ReproduzATabela67()
    {
        var analysis = await AnalyzeAsync();
        var source = new BusinessSource(Guid.NewGuid(), _loja.Name, "commit-loja", "main",
            analysis.Segments.Select(s => new BusinessSegmentSource(s.Repository, s.CommitHash, s.Branch)).ToList());

        var flow = BusinessFlowBuilder.Derive(analysis.Graph, source, BusinessViewOptions.Default, UiLanguage.Portuguese);

        // Tipos e ordem da tabela 6.7 da proposta, com dois acréscimos que a própria regra pede: a gravação do
        // cancelamento (R6, escrita no banco) e o [disabled] do template (segunda recusa na tela).
        Assert.Equal(
            [BusinessKind.Start, BusinessKind.Decision, BusinessKind.Rejection, BusinessKind.CrossSystem,
             BusinessKind.Decision, BusinessKind.Rejection, BusinessKind.Step, BusinessKind.CrossSystem,
             BusinessKind.Decision, BusinessKind.Notification, BusinessKind.Rejection, BusinessKind.End],
            flow.Steps.Select(s => s.Kind));
        var steps = flow.Steps;
        Assert.Equal("Bloqueado na tela", steps[2].Label);
        Assert.Equal("POST api/pedidos/{}/cancelamento", steps[3].Label);
        Assert.Equal("Recusa: Fora do prazo de cancelamento.", steps[5].Label);
        Assert.Equal("POST api/estornos", steps[7].Label);
        Assert.Equal("Envia e-mail", steps[9].Label);
        // Fim único no nó de entrada (o fluxo termina ao sair do método de entrada), com Id diferente do Início.
        Assert.Equal("Fim", steps[11].Label);
        Assert.Equal(analysis.Graph.EntryNodeId, steps[11].NodeId);
        Assert.NotEqual(steps[0].Id, steps[11].Id);

        // Referência: o repositório e o commit do trecho certo.
        Assert.Equal((_loja.Name, "commit-loja"), (steps[0].Ref!.Repository, steps[0].Ref!.CommitHash));
        Assert.Equal(_pedidos.Name, steps[5].Ref!.Repository);
        Assert.Equal(await _pedidos.HeadAsync(), steps[5].Ref!.CommitHash);
        Assert.Equal(_pagamentos.Name, steps[9].Ref!.Repository);
        Assert.EndsWith(".html", steps[10].Ref!.File, StringComparison.Ordinal);

        // Ligações: cada decisão mantida tem um ramo para cada lado, inclusive o implícito (else) do cartão.
        string Link(int from, int to) => flow.Edges.Single(e => e.From == steps[from].Id && e.To == steps[to].Id).Label ?? string.Empty;
        Assert.Equal("then", Link(1, 2));
        Assert.Equal("else", Link(1, 3));
        Assert.Equal("then", Link(4, 5));
        Assert.Equal("else", Link(4, 6));
        Assert.Equal("then", Link(8, 9));
        Assert.Equal("else", Link(8, 11));
        Assert.Equal(string.Empty, Link(9, 11));
        Assert.DoesNotContain(flow.Edges, e => e.Inferred);
        Assert.Equal(0, flow.CollapsedDecisions);
    }
}
