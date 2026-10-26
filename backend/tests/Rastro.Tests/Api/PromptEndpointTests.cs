using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rastro.Api.Controllers;
using Rastro.Core.Graph;
using Rastro.Mcp;

namespace Rastro.Tests.Api;

// Cenários API-PROMPT-NNN e MCP-TOOL-009/010: prompt da mudança pela API e pelo MCP; impacto.
[Collection(ApiCollection.Name)]
public class PromptEndpointTests(ApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private HttpClient Client => fixture.Factory.CreateClient();

    private async Task<(Guid Id, FlowGraph Graph)> SurveyWithDraftAsync(bool withDraft = true)
    {
        await using var provider = fixture.McpServices();
        await using var scope = provider.CreateAsyncScope();
        var analyzed = await scope.ServiceProvider.GetRequiredService<RastroTools>()
            .AnalyzeFlow(ApiFixture.RepositoryName, ApiFixture.SolutionFile, "AlertJob.ExecuteAsync");
        var id = Guid.Parse(analyzed.Split(' ')[1]);
        var graph = (await Client.GetFromJsonAsync<SurveyDetail>($"/api/surveys/{id}", Json))!.Graph!;
        if (withDraft)
        {
            var draft = graph with
            {
                Nodes = [.. graph.Nodes, new FlowNode("new:audit", NodeKind.Method, "AlertAudit.Registrar", Change: ChangeKind.Added, Intent: "Registrar cada alerta enviado.")],
                Edges = [.. graph.Edges, new FlowEdge(graph.EntryNodeId, "new:audit", 99, EdgeConfidence.Extracted, Change: ChangeKind.Added)]
            };
            Assert.Equal(HttpStatusCode.OK, (await Client.PutAsJsonAsync($"/api/surveys/{id}/draft", draft, Json)).StatusCode);
        }
        return (id, graph);
    }

    [Fact] // API-PROMPT-001
    public async Task Prompt_ComRascunho_DevolveOTextoEONumeroDeMudancas()
    {
        var (id, _) = await SurveyWithDraftAsync();

        var prompt = await Client.GetFromJsonAsync<ChangePrompt>($"/api/surveys/{id}/prompt", Json);

        Assert.Equal(1, prompt!.Changes);
        Assert.Contains("Criar o passo `AlertAudit.Registrar`", prompt.Prompt);
        Assert.Contains("chamado por `AlertJob.ExecuteAsync` (Alerts/AlertJob.cs:11)", prompt.Prompt);
    }

    [Fact] // API-PROMPT-002
    public async Task Prompt_SemRascunho_Retorna404()
    {
        var (id, _) = await SurveyWithDraftAsync(withDraft: false);

        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/surveys/{id}/prompt")).StatusCode);
    }

    [Fact] // MCP-TOOL-009
    public async Task GetChangePrompt_PeloMcp_EIgualAoDaApi()
    {
        var (id, _) = await SurveyWithDraftAsync();
        await using var provider = fixture.McpServices();
        await using var scope = provider.CreateAsyncScope();

        var viaMcp = await scope.ServiceProvider.GetRequiredService<RastroTools>().GetChangePrompt(id);
        var viaApi = (await Client.GetFromJsonAsync<ChangePrompt>($"/api/surveys/{id}/prompt", Json))!.Prompt;

        Assert.Equal(viaApi, viaMcp);
    }

    [Fact] // MCP-TOOL-010
    public async Task ImpactOf_MostraOsCaminhosAteOPassoEOQueVemDepois()
    {
        var (id, graph) = await SurveyWithDraftAsync(withDraft: false);
        await using var provider = fixture.McpServices();
        await using var scope = provider.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<RastroTools>();
        var send = graph.Nodes.ToList().FindIndex(n => n.Label == "TelegramAlertSender.SendAsync");

        var impact = await tools.ImpactOf(id, $"n{send + 1}");

        Assert.Contains("Impacto de TelegramAlertSender.SendAsync", impact);
        Assert.Contains("AlertJob.ExecuteAsync", impact);
        Assert.Contains("HttpClient.PostAsync", impact);
    }

    [Fact] // API-PROMPT-003 e MCP-TOOL-012
    public async Task Achados_FluxoSemAchados_ListaVaziaEPromptForaDaLista404()
    {
        var (id, graph) = await SurveyWithDraftAsync(withDraft: false);
        await using var provider = fixture.McpServices();
        await using var scope = provider.CreateAsyncScope();

        Assert.NotNull(graph.Findings);
        Assert.Equal("Nenhum achado de qualidade neste fluxo.", await scope.ServiceProvider.GetRequiredService<RastroTools>().ListFindings(id));
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/surveys/{id}/findings/0/prompt")).StatusCode);
    }
}
