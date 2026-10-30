using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastro.Core.Graph;
using Rastro.Core.Surveys;
using Rastro.Infrastructure;
using Rastro.Mcp;
using Rastro.Tests.Api;
using Rastro.Tests.Extractor;
using Rastro.Tests.Infrastructure;

namespace Rastro.Tests.Mcp;

// Cenários MCP-TOOL-NNN: as ferramentas do agente para o fluxo entre sistemas (Fase 25).
[Collection(ApiCollection.Name)]
public class CrossToolsTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Pagamentos = "ApiPagamentos/EstornosController.cs";

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

    private static int ObterLine => FixtureLines.LineOf(Pagamentos, "[HttpGet(", "public Task<string> Obter");
    private static int CriarLine => FixtureLines.LineOf(Pagamentos, "[HttpPost]", "public async Task Criar");

    private sealed record Session(ServiceProvider Provider, AsyncServiceScope Scope, Guid SurveyId, FlowGraph Graph) : IAsyncDisposable
    {
        public RastroTools Tools => Scope.ServiceProvider.GetRequiredService<RastroTools>();

        public string Handle(string label) => $"n{Graph.Nodes.ToList().FindIndex(n => n.Label == label) + 1}";

        public async ValueTask DisposeAsync()
        {
            await Scope.DisposeAsync();
            await Provider.DisposeAsync();
        }
    }

    private async Task<Session> StartAsync(bool cross = true)
    {
        var provider = CrossKit.Services(fixture, new FixtureSolutionSource(), _pedidos, _pagamentos);
        var scope = provider.CreateAsyncScope();
        var list = cross ? new[] { _pagamentos } : [];
        var analysis = await CrossKit.AnalyzeAsync(scope, _pedidos, "PedidosController.Cancelar", 15, list);
        var survey = await CrossKit.SaveSurveyAsync(scope, _pedidos, "PedidosController.Cancelar", 15, analysis, list);
        return new Session(provider, scope, survey.Id, analysis.Graph);
    }

    [Fact] // MCP-TOOL-009
    public async Task ListCrossEdges_LevantamentoComCostura_MostraAsLigacoesEAsChamadasSemDestino()
    {
        await using var session = await StartAsync();

        var text = await session.Tools.ListCrossEdges(session.SurveyId);

        Assert.Contains("Ligações HTTP entre sistemas: 1.", text);
        Assert.Contains($"PagamentosClient.EstornarAsync → {session.Handle("EstornosController.Criar")} EstornosController.Criar: POST api/estornos ({_pedidos.Name} → {_pagamentos.Name}), extraída", text);
        Assert.Contains("Chamadas HTTP sem destino: 2.", text);
        Assert.Contains($"{session.Handle("GET ?")} em {_pedidos.Name}", text);
        Assert.Contains("GET ?", text);
        Assert.Contains("chamada HTTP com rota montada em tempo de execução", text);
        Assert.Contains($"em {_pagamentos.Name}", text);
        Assert.Contains("POST v1/estornos", text);
        Assert.Contains("chamada HTTP sem destino em nenhum repositório da costura", text);
    }

    [Fact] // MCP-TOOL-010
    public async Task ListCrossEdges_LevantamentoSemCostura_DizQueNaoSegueChamadasHttp()
    {
        await using var session = await StartAsync(cross: false);

        var text = await session.Tools.ListCrossEdges(session.SurveyId);

        Assert.Contains("Ligações HTTP entre sistemas: 0.", text);
        Assert.Contains("Chamadas HTTP sem destino: 0.", text);
        Assert.Contains("não segue chamadas HTTP", text);
        Assert.Equal("Levantamento não encontrado ou ainda sem grafo. Use list_surveys.", await session.Tools.ListCrossEdges(Guid.NewGuid()));
    }

    [Fact] // MCP-TOOL-011
    public async Task ProposeEdge_ChamadaHttpComSystemEProva_EntraInferidaEAparecenasLigacoes()
    {
        await using var session = await StartAsync();

        var found = await session.Tools.SearchMethods(session.SurveyId, "EstornosController.Obter", _pagamentos.Name);
        var result = await session.Tools.ProposeEdge(session.SurveyId, session.Handle("GET ?"), "EstornosController.Obter",
            "EstornosController.cs", ObterLine, "A consulta de estorno cai neste GET.", system: _pagamentos.Name);
        var links = await session.Tools.ListCrossEdges(session.SurveyId);

        Assert.Contains($"EstornosController.Obter (EstornosController.cs:{ObterLine})", found);
        Assert.StartsWith("Aceita.", result);
        Assert.Contains("inferida", links);
        Assert.Contains("Ligações HTTP entre sistemas: 2.", links);
        var flow = await session.Tools.GetFlow(session.SurveyId);
        Assert.Contains($"(sistema {_pagamentos.Name})", flow);
    }

    [Fact] // MCP-TOOL-012
    public async Task ProposeEdge_ChamadaHttpSemSystemOuComVerboErrado_ERecusada()
    {
        await using var session = await StartAsync();
        var hole = session.Handle("GET ?");

        var semSystem = await session.Tools.ProposeEdge(session.SurveyId, hole, "EstornosController.Obter", "EstornosController.cs", ObterLine, "chute");
        var verbo = await session.Tools.ProposeEdge(session.SurveyId, hole, "EstornosController.Criar", "EstornosController.cs", CriarLine, "chute", system: _pagamentos.Name);
        var fora = await session.Tools.SearchMethods(session.SurveyId, "Criar", "outro-repositorio");

        Assert.StartsWith("Recusada.", semSystem);
        Assert.Contains("Informe o repositório de destino", semSystem);
        Assert.StartsWith("Recusada.", verbo);
        Assert.Contains("não atende GET", verbo);
        Assert.Contains("não está na costura", fora);
        var db = session.Scope.ServiceProvider.GetRequiredService<RastroDbContext>();
        Assert.Equal(2, await db.SurveyProposals.CountAsync(p => p.SurveyId == session.SurveyId && p.Status == ProposalStatus.Rejected));
    }

    [Fact] // MCP-TOOL-013
    public async Task GetNodeCode_NoDeOutroSistema_LeOArquivoDoRepositorioDele()
    {
        await using var session = await StartAsync();

        var code = await session.Tools.GetNodeCode(session.SurveyId, session.Handle("EstornosController.Criar"), 2);

        Assert.Contains("EstornosController.cs:", code);
        Assert.Contains("Criar", code);
    }

    [Fact] // MCP-TOOL-014
    public void Instrucoes_CitamACosturaEAsFerramentasNovas()
    {
        Assert.Contains("list_cross_edges", McpInstructions.Text);
        Assert.Contains("system", McpInstructions.Text);
    }
}
