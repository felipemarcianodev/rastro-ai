using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rastro.Api.Controllers;
using Microsoft.EntityFrameworkCore;
using Rastro.Core.Surveys;
using Rastro.Infrastructure;
using Rastro.Mcp;

namespace Rastro.Tests.Api;

// Cenários API-DIA-NNN: visão consolidada em Mermaid, salva por levantamento (Fase 27).
[Collection(ApiCollection.Name)]
public class DiagramEndpointTests(ApiFixture fixture) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private HttpClient Client => fixture.Factory.CreateClient();

    // O banco é compartilhado entre os testes: sem diagramas de outros testes para não herdar por acidente.
    public async Task InitializeAsync()
    {
        await using var provider = fixture.McpServices();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<RastroDbContext>().SurveyDiagrams.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Guid> SurveyAsync(string entryPoint = "AlertJob.ExecuteAsync")
    {
        await using var provider = fixture.McpServices();
        await using var scope = provider.CreateAsyncScope();
        var analyzed = await scope.ServiceProvider.GetRequiredService<RastroTools>()
            .AnalyzeFlow(ApiFixture.RepositoryName, ApiFixture.SolutionFile, entryPoint);
        return Guid.Parse(analyzed.Split(' ')[1]);
    }

    [Fact] // API-DIA-001
    public async Task Diagrama_SemSalvo_Devolve404()
    {
        var id = await SurveyAsync();

        var response = await Client.GetAsync($"/api/surveys/{id}/diagram");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact] // API-DIA-002
    public async Task Diagrama_SalvaComCommitELeDepoisIgual()
    {
        var id = await SurveyAsync();
        var survey = await Client.GetFromJsonAsync<SurveyDetail>($"/api/surveys/{id}", Json);
        const string text = "flowchart TD\n  a[\"Início\"] --> b[\"Fim\"]\n";

        var saved = await Client.PutAsJsonAsync($"/api/surveys/{id}/diagram", new SurveyDiagramInput(text), Json);
        var view = await saved.Content.ReadFromJsonAsync<SurveyDiagramView>(Json);
        var read = await Client.GetFromJsonAsync<SurveyDiagramView>($"/api/surveys/{id}/diagram", Json);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(text, view!.Mermaid);
        Assert.Equal(survey!.Survey.CommitHash, view.CommitHash);
        Assert.Equal(text, read!.Mermaid);

        var updated = await Client.PutAsJsonAsync($"/api/surveys/{id}/diagram", new SurveyDiagramInput("flowchart TD\n  x"), Json);
        Assert.Equal("flowchart TD\n  x", (await updated.Content.ReadFromJsonAsync<SurveyDiagramView>(Json))!.Mermaid);
    }

    [Fact] // API-DIA-003
    public async Task Diagrama_VazioOuAcimaDoLimite_Recusa400()
    {
        var id = await SurveyAsync();

        var empty = await Client.PutAsJsonAsync($"/api/surveys/{id}/diagram", new SurveyDiagramInput("  \n "), Json);
        var missing = await Client.PutAsJsonAsync($"/api/surveys/{id}/diagram", new SurveyDiagramInput(null), Json);
        var tooLong = await Client.PutAsJsonAsync($"/api/surveys/{id}/diagram", new SurveyDiagramInput(new string('a', SurveyDiagram.MaxLength + 1)), Json);

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Contains(ErrorCodes.DiagramInvalid, await empty.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Contains(ErrorCodes.DiagramInvalid, await tooLong.Content.ReadAsStringAsync());
    }

    [Fact] // API-DIA-004
    public async Task Diagrama_Apaga_Devolve204EDepois404()
    {
        var id = await SurveyAsync();
        await Client.PutAsJsonAsync($"/api/surveys/{id}/diagram", new SurveyDiagramInput("flowchart TD\n  a"), Json);

        var deleted = await Client.DeleteAsync($"/api/surveys/{id}/diagram");
        var read = await Client.GetAsync($"/api/surveys/{id}/diagram");
        var again = await Client.DeleteAsync($"/api/surveys/{id}/diagram");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact] // API-DIA-005
    public async Task Diagrama_LevantamentoInexistente_Devolve404()
    {
        var missing = Guid.NewGuid();

        var put = await Client.PutAsJsonAsync($"/api/surveys/{missing}/diagram", new SurveyDiagramInput("flowchart TD\n  a"), Json);
        var get = await Client.GetAsync($"/api/surveys/{missing}/diagram");

        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact] // API-DIA-006
    public async Task Diagrama_ExcluirLevantamento_ApagaODiagrama()
    {
        var id = await SurveyAsync();
        await Client.PutAsJsonAsync($"/api/surveys/{id}/diagram", new SurveyDiagramInput("flowchart TD\n  a"), Json);

        var deleted = await Client.DeleteAsync($"/api/surveys/{id}");
        var read = await Client.GetAsync($"/api/surveys/{id}/diagram");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
    }

    [Fact] // API-DIA-007
    public async Task Diagrama_LevantamentoNovo_HerdaDoAnteriorComFromSurveyId()
    {
        var previous = await SurveyAsync();
        await Client.PutAsJsonAsync($"/api/surveys/{previous}/diagram", new SurveyDiagramInput("flowchart TD\n  a"), Json);
        var current = await SurveyAsync();

        var read = await Client.GetFromJsonAsync<SurveyDiagramView>($"/api/surveys/{current}/diagram", Json);

        Assert.Equal("flowchart TD\n  a", read!.Mermaid);
        Assert.Equal(previous, read.FromSurveyId);
    }

    [Fact] // API-DIA-008
    public async Task Diagrama_OutroPontoDeEntrada_NaoHerda()
    {
        var other = await SurveyAsync("Fixture.Alerts.AlertJob.ExecuteAsync");
        await Client.PutAsJsonAsync($"/api/surveys/{other}/diagram", new SurveyDiagramInput("flowchart TD\n  a"), Json);
        var current = await SurveyAsync();

        var read = await Client.GetAsync($"/api/surveys/{current}/diagram");

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
    }

    [Fact] // API-DIA-009
    public async Task Diagrama_ProprioTemPrioridadeSobreOHerdado()
    {
        var previous = await SurveyAsync();
        await Client.PutAsJsonAsync($"/api/surveys/{previous}/diagram", new SurveyDiagramInput("flowchart TD\n  a"), Json);
        var current = await SurveyAsync();
        await Client.PutAsJsonAsync($"/api/surveys/{current}/diagram", new SurveyDiagramInput("flowchart TD\n  b"), Json);

        var read = await Client.GetFromJsonAsync<SurveyDiagramView>($"/api/surveys/{current}/diagram", Json);

        Assert.Equal("flowchart TD\n  b", read!.Mermaid);
        Assert.Null(read.FromSurveyId);
    }

    [Fact] // API-DIA-010
    public async Task Diagrama_NaoHerdaDeLevantamentoMaisNovo()
    {
        var older = await SurveyAsync();
        var newer = await SurveyAsync();
        await Client.PutAsJsonAsync($"/api/surveys/{newer}/diagram", new SurveyDiagramInput("flowchart TD\n  a"), Json);

        var read = await Client.GetAsync($"/api/surveys/{older}/diagram");

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
    }

    [Theory] // CORE-DIA-001
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("flowchart TD", true)]
    public void Validate_VazioOuTexto_RespeitaOLimite(string text, bool ok)
    {
        Assert.Equal(ok, SurveyDiagram.Validate(text) is null);
        Assert.NotNull(SurveyDiagram.Validate(new string('a', SurveyDiagram.MaxLength + 1)));
        Assert.Null(SurveyDiagram.Validate(new string('a', SurveyDiagram.MaxLength)));
    }
}
