using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastro.Api.Controllers;
using Rastro.Core.Graph;
using Rastro.Core.Surveys;
using Rastro.Infrastructure;
using Rastro.Mcp;

namespace Rastro.Tests.Api;

// Cenários API-SURVEY-DEL-NNN: excluir levantamento (Fase 13).
[Collection(ApiCollection.Name)]
public class SurveyDeleteEndpointTests(ApiFixture fixture)
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

    [Fact] // API-SURVEY-DEL-001
    public async Task Excluir_Pronto_SomeComORascunhoEMantemAsAnotacoes()
    {
        var (id, graph) = await SurveyAsync();
        var nodeId = graph.Nodes[4].Id;
        await Client.PutAsJsonAsync($"/api/surveys/{id}/draft", graph, Json);
        await Client.PutAsJsonAsync($"/api/surveys/{id}/notes", new NodeNoteInput(nodeId, "fica depois de excluir"), Json);

        var deleted = await Client.DeleteAsync($"/api/surveys/{id}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/surveys/{id}")).StatusCode);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RastroDbContext>();
        Assert.False(await db.SurveyDrafts.AnyAsync(d => d.SurveyId == id));
        Assert.True(await db.NodeNotes.AnyAsync(n => n.NodeId == nodeId));

        var (other, _) = await SurveyAsync();
        await Client.PutAsJsonAsync($"/api/surveys/{other}/notes", new NodeNoteInput(nodeId, ""), Json);
    }

    [Fact] // API-SURVEY-DEL-002
    public async Task Excluir_EmAnalise_Recusa()
    {
        Guid id;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RastroDbContext>();
            var survey = Survey.Create(ApiFixture.RepositoryName, ApiFixture.SolutionFile, "AlertJob.ExecuteAsync", 5, null, null, DateTimeOffset.UtcNow);
            db.Surveys.Add(survey);
            await db.SaveChangesAsync();
            id = survey.Id;
        }

        var response = await Client.DeleteAsync($"/api/surveys/{id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(ErrorCodes.SurveyRunning, await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await Client.DeleteAsync($"/api/surveys/{Guid.NewGuid()}")).StatusCode);
    }
}
