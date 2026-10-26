using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rastro.Api.Controllers;
using Rastro.Core.Architecture;
using Rastro.Mcp;

namespace Rastro.Tests.Api;

// Cenários API-C4-NNN e MCP-TOOL-013: exportação C4 em Structurizr DSL e pessoas do C1.
[Collection(ApiCollection.Name)]
public class C4EndpointTests(ApiFixture fixture)
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

    [Fact] // API-C4-001
    public async Task C4_SemPessoas_TemOsQuatroNiveisESemPessoa()
    {
        var id = await SurveyAsync();

        var export = await Client.GetFromJsonAsync<C4Export>($"/api/surveys/{id}/c4", Json);

        Assert.Empty(export!.People);
        Assert.Contains("workspace \"fixture: AlertJob.ExecuteAsync\"", export.Dsl);
        Assert.Contains("\"C1-contexto\"", export.Dsl);
        Assert.Contains("\"C4-fluxo\"", export.Dsl);
        Assert.DoesNotContain("= person", export.Dsl);
    }

    [Fact] // API-C4-002
    public async Task Pessoas_SalvasEntramNoC1_EFicamParaODownload()
    {
        var id = await SurveyAsync();

        var saved = await Client.PutAsJsonAsync($"/api/surveys/{id}/c4/people",
            new[] { new C4Person("  Operador ", "Acompanha os alertas."), new C4Person("Gestor", "  ") }, Json);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var export = (await saved.Content.ReadFromJsonAsync<C4Export>(Json))!;
        Assert.Equal([new C4Person("Operador", "Acompanha os alertas."), new C4Person("Gestor", null)], export.People);
        var file = await Client.GetAsync($"/api/surveys/{id}/c4/workspace.dsl");
        Assert.Equal("workspace.dsl", file.Content.Headers.ContentDisposition?.FileName);
        var dsl = await file.Content.ReadAsStringAsync();
        Assert.Contains("pessoa1 = person \"Operador\" \"Acompanha os alertas.\"", dsl);
        Assert.Contains("pessoa2 -> sistema.cont1 \"Usa\"", dsl);
    }

    [Fact] // API-C4-004
    public async Task Pessoas_SugeridaPelaIA_GuardaAOrigemESaiInferidaNoC1()
    {
        var id = await SurveyAsync();

        var saved = await Client.PutAsJsonAsync($"/api/surveys/{id}/c4/people",
            new[] { new C4Person("Cliente", "Compra.", true), new C4Person("Gestor", null, false) }, Json);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var export = (await saved.Content.ReadFromJsonAsync<C4Export>(Json))!;
        Assert.Equal([new C4Person("Cliente", "Compra.", true), new C4Person("Gestor", null)], export.People);
        Assert.Contains("pessoa1 = person \"Cliente\" \"Compra.\" \"Inferida\"", export.Dsl);
        Assert.Contains("pessoa2 = person \"Gestor\" \"\"\n", export.Dsl.ReplaceLineEndings("\n"));
        Assert.Contains("element \"Inferida\" {", export.Dsl);
    }

    [Fact] // API-C4-003
    public async Task Pessoas_ComNomeRepetidoOuVazio_Retorna400()
    {
        var id = await SurveyAsync();

        var response = await Client.PutAsJsonAsync($"/api/surveys/{id}/c4/people",
            new[] { new C4Person("Operador", null), new C4Person("operador", null), new C4Person("", null) }, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("C4_PEOPLE_INVALID", body);
        Assert.Contains("mesmo nome", body);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/surveys/{Guid.NewGuid()}/c4")).StatusCode);
    }

    [Fact] // MCP-TOOL-013
    public async Task GetC4_PeloMcp_EIgualAoDaApi()
    {
        var id = await SurveyAsync();
        await using var provider = fixture.McpServices();
        await using var scope = provider.CreateAsyncScope();

        var viaMcp = await scope.ServiceProvider.GetRequiredService<RastroTools>().GetC4(id);
        var viaApi = (await Client.GetFromJsonAsync<C4Export>($"/api/surveys/{id}/c4", Json))!.Dsl;

        Assert.Equal(viaApi, viaMcp);
    }
}
