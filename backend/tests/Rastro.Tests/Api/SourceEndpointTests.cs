using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Rastro.Api.Controllers;
using Rastro.Core.Surveys;

namespace Rastro.Tests.Api;

// Cenários API-SRC-NNN: trecho de código do nó, sempre dentro do repositório.
[Collection(ApiCollection.Name)]
public class SourceEndpointTests(ApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<Guid> SurveyIdAsync()
    {
        var client = fixture.Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/surveys",
            new { repository = ApiFixture.RepositoryName, solutionPath = ApiFixture.SolutionFile, entryPoint = "AlertJob.ExecuteAsync" });
        var created = (await response.Content.ReadFromJsonAsync<SurveySummary>(Json))!;
        for (var i = 0; i < 150; i++)
        {
            var detail = await client.GetFromJsonAsync<SurveyDetail>($"/api/surveys/{created.Id}", Json);
            if (detail!.Survey.Status == SurveyStatus.Completed) return created.Id;
            await Task.Delay(200);
        }
        throw new TimeoutException();
    }

    [Fact] // API-SRC-001
    public async Task Trecho_ArquivoDoGrafo_DevolveAsLinhasEmVoltaDaLinhaPedida()
    {
        var id = await SurveyIdAsync();
        var client = fixture.Factory.CreateClient();

        var snippet = await client.GetFromJsonAsync<SourceSnippet>($"/api/surveys/{id}/source?file=Alerts/AlertJob.cs&line=11&context=2", Json);

        Assert.Equal(9, snippet!.StartLine);
        Assert.Equal(11, snippet.HighlightLine);
        Assert.Equal(5, snippet.Lines.Count);
        Assert.Contains("ExecuteAsync", snippet.Lines[2]);
    }

    [Theory] // API-SRC-002
    [InlineData("../fora.cs")]
    [InlineData("..%2F..%2Ffora.cs")]
    [InlineData("C:/Windows/win.cs")]
    [InlineData("Fixture.slnx")]
    [InlineData("")]
    public async Task Trecho_CaminhoForaDoRepositorioOuNaoCs_Retorna400(string file)
    {
        var id = await SurveyIdAsync();
        var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync($"/api/surveys/{id}/source?file={file}&line=1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ErrorCodes.SourcePathInvalid, body.GetProperty("code").GetString());
    }

    [Fact] // API-SRC-003
    public async Task Trecho_ArquivoInexistenteOuLevantamentoDesconhecido_Retorna404()
    {
        var id = await SurveyIdAsync();
        var client = fixture.Factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/surveys/{id}/source?file=Alerts/Nada.cs&line=1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/surveys/{Guid.NewGuid()}/source?file=Alerts/AlertJob.cs&line=1")).StatusCode);
    }
}
