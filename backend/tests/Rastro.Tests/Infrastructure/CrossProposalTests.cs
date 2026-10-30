using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastro.Core.Graph;
using Rastro.Core.Surveys;
using Rastro.Infrastructure;
using Rastro.Infrastructure.Proposals;
using Rastro.Tests.Api;
using Rastro.Tests.Extractor;

namespace Rastro.Tests.Infrastructure;

// Cenários INF-CROSS-NNN: proposta de destino para uma chamada HTTP sem destino (Fase 25). A prova é a de
// sempre mais o índice de rotas: o método precisa atender o verbo e a rota da chamada.
[Collection(ApiCollection.Name)]
public class CrossProposalTests(ApiFixture fixture) : IAsyncLifetime
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

    // Levantamento de Cancelar com a costura ligada: sobra a chamada "GET ?" (rota montada em tempo de execução)
    // e o "POST v1/estornos" do gateway (host de fora), que nenhum repositório atende.
    private async Task<(ServiceProvider Provider, Survey Survey, FlowNode Dynamic)> SetupAsync(bool withSegment = true)
    {
        var provider = CrossKit.Services(fixture, new FixtureSolutionSource(), _pedidos, _pagamentos);
        using var scope = provider.CreateScope();
        var analysis = await CrossKit.AnalyzeAsync(scope, _pedidos, "PedidosController.Cancelar", 15, _pagamentos);
        var survey = await CrossKit.SaveSurveyAsync(scope, _pedidos, "PedidosController.Cancelar", 15, analysis, _pagamentos);
        if (!withSegment)
        {
            var db = scope.ServiceProvider.GetRequiredService<RastroDbContext>();
            db.SurveySegments.RemoveRange(db.SurveySegments.Where(s => s.SurveyId == survey.Id));
            await db.SaveChangesAsync();
        }
        return (provider, survey, analysis.Graph.Nodes.Single(n => n.Label == "GET ?"));
    }

    private static async Task<ProposalOutcome> ProposeAsync(
        ServiceProvider provider, Survey survey, FlowNode hole, string target, int line, string? system)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ProposalService>().ProposeEdgeAsync(
            survey.Id, hole.Id, target, "EstornosController.cs", line, "O GET de consulta cai neste método.",
            ProposalOrigin.Agent, CancellationToken.None, system: system);
    }

    private static async Task<FlowGraph> GraphAsync(ServiceProvider provider, Guid surveyId)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RastroDbContext>();
        return (await db.Surveys.AsNoTracking().SingleAsync(s => s.Id == surveyId)).ReadGraph()!;
    }

    [Fact] // INF-CROSS-013
    public async Task ProporAresta_HttpComAProvaCerta_EntraInferidaComAChamadaEOsIdsDoSistema()
    {
        var (provider, survey, hole) = await SetupAsync();
        await using var owner = provider;

        var outcome = await ProposeAsync(provider, survey, hole, "EstornosController.Obter", ObterLine, _pagamentos.Name);

        Assert.True(outcome.Accepted, outcome.Message);
        var graph = await GraphAsync(provider, survey.Id);
        var edge = Assert.Single(graph.Edges, e => e.From == hole.Id);
        Assert.Equal(EdgeConfidence.Inferred, edge.Confidence);
        Assert.Equal(ProposalOrigin.Agent, edge.Origin);
        Assert.Equal(new HttpCall("GET", null), edge.Http);
        Assert.StartsWith(_pagamentos.Name + "::", edge.To, StringComparison.Ordinal);
        var destination = graph.Nodes.Single(n => n.Id == edge.To);
        Assert.Equal(_pagamentos.Name, destination.System);
        Assert.Equal("EstornosController.Obter", destination.Label);
    }

    [Fact] // INF-CROSS-014
    public async Task ProporAresta_HttpComVerboErrado_ERecusadaEOGrafoNaoMuda()
    {
        var (provider, survey, hole) = await SetupAsync();
        await using var owner = provider;
        var before = await GraphAsync(provider, survey.Id);

        // A chamada é GET e o Criar atende POST.
        var outcome = await ProposeAsync(provider, survey, hole, "EstornosController.Criar", CriarLine, _pagamentos.Name);

        Assert.False(outcome.Accepted);
        Assert.Contains("não atende GET", outcome.Message);
        Assert.Equal(before.Edges.Count, (await GraphAsync(provider, survey.Id)).Edges.Count);
    }

    [Fact] // INF-CROSS-015
    public async Task ProporAresta_HttpSemSystemOuComSystemForaDaCostura_ERecusada()
    {
        var (provider, survey, hole) = await SetupAsync();
        await using var owner = provider;

        var without = await ProposeAsync(provider, survey, hole, "EstornosController.Obter", ObterLine, null);
        var outside = await ProposeAsync(provider, survey, hole, "EstornosController.Obter", ObterLine, _pedidos.Name);

        Assert.False(without.Accepted);
        Assert.Contains("Informe o repositório de destino", without.Message);
        Assert.False(outside.Accepted);
        Assert.Contains(_pagamentos.Name, outside.Message);
    }

    [Fact] // INF-CROSS-016
    public async Task ProporAresta_HttpComRotaQueNaoCasa_ERecusadaPeloIndice()
    {
        var (provider, survey, _) = await SetupAsync();
        await using var owner = provider;
        var graph = await GraphAsync(provider, survey.Id);
        var gateway = graph.Nodes.Single(n => n.Label == "POST v1/estornos");

        // O Criar atende POST api/estornos, e a chamada é para v1/estornos num host de fora.
        var outcome = await ProposeAsync(provider, survey, gateway, "EstornosController.Criar", CriarLine, _pagamentos.Name);

        Assert.False(outcome.Accepted);
        Assert.Contains("não casa com a rota chamada", outcome.Message);
    }

    [Fact] // INF-CROSS-017
    public async Task ProporAresta_HttpDepoisDeCommitNovoNoDestino_ERecusadaPeloCodigoQueMudou()
    {
        var (provider, survey, hole) = await SetupAsync();
        await using var owner = provider;
        await _pagamentos.NewCommitAsync();

        var outcome = await ProposeAsync(provider, survey, hole, "EstornosController.Obter", ObterLine, _pagamentos.Name);

        Assert.False(outcome.Accepted);
        Assert.Contains($"O código de {_pagamentos.Name} mudou", outcome.Message);
    }

    [Fact] // INF-CROSS-018
    public async Task ProporAresta_HttpEmSistemaSemTrecho_CriaOTrechoParaOStaleVigiarOSistema()
    {
        var (provider, survey, hole) = await SetupAsync(withSegment: false);
        await using var owner = provider;

        var outcome = await ProposeAsync(provider, survey, hole, "EstornosController.Obter", ObterLine, _pagamentos.Name);

        Assert.True(outcome.Accepted, outcome.Message);
        using var scope = provider.CreateScope();
        var segment = await scope.ServiceProvider.GetRequiredService<RastroDbContext>().SurveySegments.AsNoTracking().SingleAsync(s => s.SurveyId == survey.Id);
        Assert.Equal(1, segment.Order);
        Assert.Equal(_pagamentos.Name, segment.Repository);
        Assert.Equal("EstornosController.Obter", segment.EntryPoint);
        Assert.Equal(await _pagamentos.HeadAsync(), segment.CommitHash);
    }
}
