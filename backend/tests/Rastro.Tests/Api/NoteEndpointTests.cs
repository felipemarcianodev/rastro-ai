using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rastro.Api.Controllers;
using Rastro.Core.Graph;
using Rastro.Core.Surveys;
using Rastro.Mcp;

namespace Rastro.Tests.Api;

// Cenários API-NOTE-NNN: anotação do usuário num passo (Fase 12). A nota é do repositório e do
// passo, então cada teste usa um passo diferente para não depender da ordem (o banco é comum).
[Collection(ApiCollection.Name)]
public class NoteEndpointTests(ApiFixture fixture)
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

    [Fact] // API-NOTE-001
    public async Task Nota_SalvaAtualizaEApagaComTextoVazio()
    {
        var (id, graph) = await SurveyAsync();
        var nodeId = graph.Nodes[1].Id;

        var saved = await Client.PutAsJsonAsync($"/api/surveys/{id}/notes", new NodeNoteInput(nodeId, "  cuidado: chamado também pelo webhook "), Json);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal("cuidado: chamado também pelo webhook", (await saved.Content.ReadFromJsonAsync<NodeNoteView>(Json))!.Text);

        await Client.PutAsJsonAsync($"/api/surveys/{id}/notes", new NodeNoteInput(nodeId, "texto novo"), Json);
        var notes = await Client.GetFromJsonAsync<List<NodeNoteView>>($"/api/surveys/{id}/notes", Json);
        Assert.Equal("texto novo", Assert.Single(notes!, n => n.NodeId == nodeId).Text);

        var deleted = await Client.PutAsJsonAsync($"/api/surveys/{id}/notes", new NodeNoteInput(nodeId, "   "), Json);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        notes = await Client.GetFromJsonAsync<List<NodeNoteView>>($"/api/surveys/{id}/notes", Json);
        Assert.DoesNotContain(notes!, n => n.NodeId == nodeId);
    }

    [Fact] // API-NOTE-002
    public async Task Nota_ApareceNoLevantamentoSeguinteDoMesmoRepositorio()
    {
        var (first, graph) = await SurveyAsync();
        var nodeId = graph.Nodes[2].Id;
        await Client.PutAsJsonAsync($"/api/surveys/{first}/notes", new NodeNoteInput(nodeId, "vale para o próximo"), Json);

        var (second, _) = await SurveyAsync();

        var notes = await Client.GetFromJsonAsync<List<NodeNoteView>>($"/api/surveys/{second}/notes", Json);
        Assert.Contains(notes!, n => n.NodeId == nodeId && n.Text == "vale para o próximo");
        await Client.PutAsJsonAsync($"/api/surveys/{second}/notes", new NodeNoteInput(nodeId, ""), Json);
    }

    [Fact] // API-CROSS-001
    public async Task RegraCruzada_RepositorioComUmFluxoSo_ListaVaziaELevantamentoInexistente404()
    {
        var (id, _) = await SurveyAsync();

        var list = await Client.GetFromJsonAsync<List<CrossFlowView>>($"/api/surveys/{id}/cross-flow", Json);
        var missing = await Client.GetAsync($"/api/surveys/{Guid.NewGuid()}/cross-flow");

        Assert.Empty(list!);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact] // API-NOTE-003
    public async Task Nota_PassoForaDoFluxoOuTextoLongo_Recusa()
    {
        var (id, graph) = await SurveyAsync();

        var outside = await Client.PutAsJsonAsync($"/api/surveys/{id}/notes", new NodeNoteInput("m:nao.existe", "x"), Json);
        var tooLong = await Client.PutAsJsonAsync($"/api/surveys/{id}/notes",
            new NodeNoteInput(graph.Nodes[3].Id, new string('a', NodeNote.MaxTextLength + 1)), Json);
        var missing = await Client.PutAsJsonAsync($"/api/surveys/{Guid.NewGuid()}/notes", new NodeNoteInput("x", "x"), Json);

        Assert.Equal(HttpStatusCode.BadRequest, outside.StatusCode);
        Assert.Contains(ErrorCodes.NoteInvalid, await outside.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
