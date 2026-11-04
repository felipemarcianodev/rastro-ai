using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rastro.Core.Graph;
using Rastro.Mcp;

namespace Rastro.Tests.Api;

// Cenários API-MAP-NNN: a rota do mapa do fluxo (Fase 30).
[Collection(ApiCollection.Name)]
public class MapEndpointTests(ApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private HttpClient Client => fixture.Factory.CreateClient();

    private async Task<Guid> SurveyAsync()
    {
        await using var provider = fixture.McpServices();
        await using var scope = provider.CreateAsyncScope();
        var analyzed = await scope.ServiceProvider.GetRequiredService<RastroTools>()
            .AnalyzeFlow(ApiFixture.RepositoryName, ApiFixture.SolutionFile, "AlertJob.ExecuteAsync");
        return Guid.Parse(analyzed.Split(' ')[1]);
    }

    [Fact] // API-MAP-001
    public async Task Map_LevantamentoPronto_DevolveFaixasBlocosECaminhos()
    {
        var id = await SurveyAsync();

        var map = await Client.GetFromJsonAsync<FlowMap>($"/api/surveys/{id}/map", Json);

        Assert.NotNull(map);
        Assert.Equal(FlowMapBuilder.MainLane, map!.Lanes[0].Id);
        var entry = map.Blocks[0];
        Assert.Equal(MapBlockKind.Entry, entry.Kind);
        Assert.Contains("AlertJob.ExecuteAsync", entry.Title);
        Assert.NotEmpty(map.Paths);
        Assert.All(map.Paths, p => Assert.Equal(entry.Id, p.Blocks[0]));
    }

    [Fact] // API-MAP-002
    public async Task Map_LevantamentoQueNaoExiste_404()
    {
        var response = await Client.GetAsync($"/api/surveys/{Guid.NewGuid()}/map");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
