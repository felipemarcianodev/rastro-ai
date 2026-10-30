using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Rastro.Ai;
using Rastro.Ai.Providers;
using Rastro.Ai.Settings;
using Rastro.Api.Controllers;
using Rastro.Core.Graph;
using Rastro.Core.Surveys;
using Rastro.Tests.Api;
using Rastro.Tests.Extractor;
using Rastro.Tests.Infrastructure;

namespace Rastro.Tests.Ai;

// Cenários AI-CROSS-NNN: o modo provedor na costura entre sistemas (Fase 25). O buraco é uma chamada HTTP
// sem destino: a IA recebe os repositórios da costura e as rotas deles, devolve o repositório de destino
// (system) e a proposta passa pelo mesmo validador de prova. Nós de outro sistema leem o código do
// repositório deles.
[Collection(ApiCollection.Name)]
public class CrossHoleFillerTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Pagamentos = "ApiPagamentos/EstornosController.cs";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private CrossRepo _pedidos = null!;
    private CrossRepo _pagamentos = null!;

    private HttpClient Client => fixture.Factory.CreateClient();

    public async Task InitializeAsync()
    {
        _pedidos = await CrossRepo.CreateAsync("pedidos", "ApiPedidos");
        _pagamentos = await CrossRepo.CreateAsync("pagamentos", "ApiPagamentos");
        foreach (var repo in new[] { _pedidos, _pagamentos })
            Assert.Equal(HttpStatusCode.Created, (await Client.PostAsJsonAsync("/api/repositories", new { name = repo.Name, path = repo.Root })).StatusCode);

        var settings = await Client.PutAsJsonAsync("/api/settings/ai", new
        {
            enabled = true, provider = (int)AiProvider.Claude, claudeModel = "claude-opus-5-5", claudeKey = "sk-ant-teste",
            openAiModel = (string?)null, openAiKey = (string?)null, geminiModel = (string?)null, geminiKey = (string?)null,
            dailyTokenBudget = 0, useFillHoles = true, useDescribeRules = true, useSuggestions = true, useContextSuggestions = true
        });
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
    }

    public async Task DisposeAsync()
    {
        foreach (var repo in new[] { _pedidos, _pagamentos })
        {
            await Client.DeleteAsync($"/api/repositories/{repo.Name}");
            await repo.DisposeAsync();
        }
    }

    private static int ObterLine => FixtureLines.LineOf(Pagamentos, "[HttpGet(", "public Task<string> Obter");

    // Levantamento de Cancelar com a costura ligada: sobra a chamada "GET ?" (rota montada em tempo de execução).
    private async Task<(Guid Id, string HoleId)> SurveyAsync()
    {
        var response = await Client.PostAsJsonAsync("/api/surveys", new
        {
            repository = _pedidos.Name,
            solutionPath = _pedidos.Solution,
            entryPoint = "PedidosController.Cancelar",
            crossRepositories = new[] { _pagamentos.Name }
        });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<SurveySummary>(Json))!;
        for (var i = 0; i < 300; i++)
        {
            var detail = await Client.GetFromJsonAsync<SurveyDetail>($"/api/surveys/{created.Id}", Json);
            if (detail!.Survey.Status is SurveyStatus.Completed or SurveyStatus.Failed)
            {
                Assert.Equal(SurveyStatus.Completed, detail.Survey.Status);
                return (created.Id, detail.Graph!.Nodes.Single(n => n.Label == "GET ?").Id);
            }
            await Task.Delay(200);
        }
        throw new TimeoutException($"Levantamento {created.Id} não terminou.");
    }

    private string ProposalJson(string? system) => JsonSerializer.Serialize(new
    {
        proposals = new[]
        {
            new
            {
                target = "EstornosController.Obter", evidenceFile = "EstornosController.cs", evidenceLine = ObterLine,
                when = (string?)null, reason = "A consulta de estorno cai neste GET.", system
            }
        }
    });

    private async Task<HttpResponseMessage> FillAsync(Guid id, string nodeId) =>
        await Client.PostAsJsonAsync($"/api/surveys/{id}/ai/fill-hole", new { nodeId });

    [Fact] // AI-CROSS-001
    public async Task CompletarBuraco_ChamadaHttpComSystem_EntraInferidaComOrigemProvedorNoSistemaDeDestino()
    {
        var (id, hole) = await SurveyAsync();
        FakeAiTextService.Responder = _ => new AiResult(ProposalJson(_pagamentos.Name), 300, 80);

        var response = await FillAsync(id, hole);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<FillHoleResponse>(Json))!;
        Assert.Equal(1, body.Result.Accepted);
        var edge = Assert.Single(body.Survey.Graph!.Edges, e => e.From == hole);
        Assert.Equal(EdgeConfidence.Inferred, edge.Confidence);
        Assert.Equal(ProposalOrigin.Provider, edge.Origin);
        Assert.Equal(_pagamentos.Name, body.Survey.Graph.Nodes.Single(n => n.Id == edge.To).System);
    }

    [Fact] // AI-CROSS-002
    public async Task CompletarBuraco_ChamadaHttp_PedidoLevaOsRepositoriosDaCosturaEAsRotasDeles()
    {
        var (id, hole) = await SurveyAsync();
        FakeAiTextService.Responder = _ => new AiResult("""{"proposals":[]}""", 10, 5);

        await FillAsync(id, hole);

        var user = FakeAiTextService.LastRequest!.User;
        Assert.Contains("Repositórios da costura", user);
        Assert.Contains(_pagamentos.Name, user);
        Assert.Contains("EstornosController.Obter", user);
        Assert.Contains("<codigo>", user);
        Assert.Contains("system", FakeAiTextService.LastRequest.System);
    }

    [Fact] // AI-CROSS-003
    public async Task CompletarBuraco_ChamadaHttpSemSystemNaResposta_ERecusadaESemAresta()
    {
        var (id, hole) = await SurveyAsync();
        FakeAiTextService.Responder = _ => new AiResult(ProposalJson(null), 300, 80);

        var response = await FillAsync(id, hole);

        var body = (await response.Content.ReadFromJsonAsync<FillHoleResponse>(Json))!;
        Assert.Equal(1, body.Result.Proposed);
        Assert.Equal(0, body.Result.Accepted);
        Assert.Contains(body.Result.Messages, m => m.Contains("Informe o repositório de destino", StringComparison.Ordinal));
        Assert.DoesNotContain(body.Survey.Graph!.Edges, e => e.From == hole);
    }

    [Fact] // AI-CROSS-004
    public async Task Descrever_NoDeOutroSistema_LeOCodigoDoRepositorioDele()
    {
        var (id, hole) = await SurveyAsync();
        FakeAiTextService.Responder = _ => new AiResult(ProposalJson(_pagamentos.Name), 300, 80);
        var filled = (await (await FillAsync(id, hole)).Content.ReadFromJsonAsync<FillHoleResponse>(Json))!;
        var destination = filled.Survey.Graph!.Edges.Single(e => e.From == hole).To;
        FakeAiTextService.Responder = _ => new AiResult("""{"rules":[{"node":"r1","rule":"Consulta o estorno pelo identificador."}]}""", 100, 30);

        var response = await Client.PostAsJsonAsync($"/api/surveys/{id}/ai/describe", new { nodeIds = new[] { destination }, overwrite = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("BuscarAsync", FakeAiTextService.LastRequest!.User);
    }
}
