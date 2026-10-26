using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rastro.Api.Controllers;
using Rastro.Core.Graph;
using Rastro.Mcp;

namespace Rastro.Tests.Api;

// Cenários API-DRAFT-NNN: rascunho de edição salvo, lido, validado e descartado.
[Collection(ApiCollection.Name)]
public class DraftEndpointTests(ApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private HttpClient Client => fixture.Factory.CreateClient();

    private async Task<(Guid Id, FlowGraph Graph)> SurveyAsync()
    {
        await using var provider = fixture.McpServices();
        await using var scope = provider.CreateAsyncScope();
        var analyzed = await scope.ServiceProvider.GetRequiredService<RastroTools>()
            .AnalyzeFlow(ApiFixture.RepositoryName, ApiFixture.SolutionFile, "AlertJob.ExecuteAsync");
        var id = Guid.Parse(analyzed.Split(' ')[1]);
        return (id, (await Client.GetFromJsonAsync<SurveyDetail>($"/api/surveys/{id}", Json))!.Graph!);
    }

    private static FlowGraph Edited(FlowGraph graph)
    {
        var entry = graph.EntryNodeId;
        return graph with
        {
            Nodes = [.. graph.Nodes, new FlowNode("new:validar", NodeKind.Method, "AlertJob.Validar", Change: ChangeKind.Added, Intent: "Validar o alerta antes de enviar.")],
            Edges = [.. graph.Edges, new FlowEdge(entry, "new:validar", 99, EdgeConfidence.Extracted, Change: ChangeKind.Added)]
        };
    }

    [Fact] // API-DRAFT-001
    public async Task SalvarELer_RascunhoValido_VoltaIgualEOExtraidoNaoMuda()
    {
        var (id, graph) = await SurveyAsync();

        var saved = await Client.PutAsJsonAsync($"/api/surveys/{id}/draft", Edited(graph), Json);
        var draft = await Client.GetFromJsonAsync<FlowGraph>($"/api/surveys/{id}/draft", Json);
        var survey = await Client.GetFromJsonAsync<SurveyDetail>($"/api/surveys/{id}", Json);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Contains(draft!.Nodes, n => n.Id == "new:validar" && n.Change == ChangeKind.Added && n.Intent!.StartsWith("Validar"));
        Assert.DoesNotContain(survey!.Graph!.Nodes, n => n.Id == "new:validar");
    }

    [Fact] // API-DRAFT-002
    public async Task Salvar_RascunhoQueApagaPassoDoCodigo_Retorna400ComOsErros()
    {
        var (id, graph) = await SurveyAsync();
        var broken = graph with { Nodes = graph.Nodes.Skip(1).ToList() };

        var response = await Client.PutAsJsonAsync($"/api/surveys/{id}/draft", broken, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ErrorCodes.DraftInvalid, body.GetProperty("code").GetString());
        Assert.True(body.GetProperty("errors").GetArrayLength() > 0);
    }

    [Fact] // API-DRAFT-003
    public async Task Descartar_RemoveORascunho_ESemRascunhoDa404()
    {
        var (id, graph) = await SurveyAsync();
        await Client.PutAsJsonAsync($"/api/surveys/{id}/draft", Edited(graph), Json);

        var deleted = await Client.DeleteAsync($"/api/surveys/{id}/draft");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/surveys/{id}/draft")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.DeleteAsync($"/api/surveys/{id}/draft")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.PutAsJsonAsync($"/api/surveys/{Guid.NewGuid()}/draft", graph, Json)).StatusCode);
    }
}
