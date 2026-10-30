using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rastro.Api.Controllers;
using Rastro.Core.Graph;
using Rastro.Core.Surveys;
using Rastro.Infrastructure.Analysis;
using Rastro.Tests.Infrastructure;

namespace Rastro.Tests.Api;

// Cenários API-ANG-NNN: o levantamento Angular (Fase 26) pela API, com o Node de verdade ou com um Node falso.
[Collection(ApiCollection.Name)]
public class AngularEndpointTests(ApiFixture fixture) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private AngularRepo _loja = null!;

    private HttpClient Client => fixture.Factory.CreateClient();

    public async Task InitializeAsync()
    {
        _loja = await AngularRepo.CreateAsync();
        var added = await Client.PostAsJsonAsync("/api/repositories", new { name = _loja.Name, path = _loja.Root });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
    }

    public async Task DisposeAsync()
    {
        await Client.DeleteAsync($"/api/repositories/{_loja.Name}");
        await _loja.DisposeAsync();
    }

    // A mesma API, com um Node que existe ou não, sem depender da máquina.
    private HttpClient ClientWithNode(bool available) =>
        fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<INodeRuntime>();
            services.AddSingleton<INodeRuntime>(new FakeNodeRuntime(available));
        })).CreateClient();

    private static object Request(AngularRepo repo) => new { repository = repo.Name, solutionPath = AngularRepo.TsConfig, entryPoint = AngularRepo.Entry };

    [Fact] // API-ANG-001
    public async Task Criar_TsconfigSemNode_Retorna400AngularUnavailable()
    {
        var response = await ClientWithNode(false).PostAsJsonAsync("/api/surveys", Request(_loja));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ErrorCodes.AngularUnavailable, body.GetProperty("code").GetString());
        Assert.Equal("ANGULAR_UNAVAILABLE", body.GetProperty("code").GetString());
    }

    [Fact] // API-ANG-002
    public async Task Health_ComEmSemNode_InformaSeOAngularEstaDisponivel()
    {
        var without = await ClientWithNode(false).GetFromJsonAsync<JsonElement>("/api/health");
        var with = await ClientWithNode(true).GetFromJsonAsync<JsonElement>("/api/health");

        Assert.False(without.GetProperty("angular").GetBoolean());
        Assert.True(with.GetProperty("angular").GetBoolean());
        Assert.Equal("ok", with.GetProperty("status").GetString());
    }

    [Fact] // API-ANG-003
    public async Task Repositorios_ComNodeEAngularJson_SolutionsSugereOTsconfig()
    {
        await File.WriteAllTextAsync(Path.Combine(_loja.Root, "angular.json"), "{}");

        var solutions = await ClientWithNode(true).GetFromJsonAsync<List<string>>($"/api/repositories/{_loja.Name}/solutions");
        var without = await ClientWithNode(false).GetFromJsonAsync<List<string>>($"/api/repositories/{_loja.Name}/solutions");

        Assert.Equal([AngularRepo.TsConfig], solutions);
        Assert.Empty(without!);
    }

    [Fact] // API-ANG-004
    public async Task Criar_TsconfigComNode_LevantaOAngularComLanguageEVerCodigoDoTemplate()
    {
        AngularRepo.RequireNode();
        var client = ClientWithNode(true);

        var response = await client.PostAsJsonAsync("/api/surveys", Request(_loja));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<SurveySummary>(Json))!;
        Assert.Equal(SourceLanguage.Angular, created.Language);

        SurveyDetail? detail = null;
        for (var i = 0; i < 300; i++)
        {
            detail = await client.GetFromJsonAsync<SurveyDetail>($"/api/surveys/{created.Id}", Json);
            if (detail!.Survey.Status is SurveyStatus.Completed or SurveyStatus.Failed) break;
            await Task.Delay(200);
        }
        Assert.Equal(SurveyStatus.Completed, detail!.Survey.Status);
        Assert.Contains(detail.Graph!.Nodes, n => n.Label == "DetalhePedidoComponent.cancelar" && n.Kind == NodeKind.Entry);
        Assert.Contains(detail.Graph.Nodes, n => n.Kind == NodeKind.Unresolved && n.Label == "POST api/pedidos/{}/cancelamento");

        // O JSON leva a linguagem como número (1 = Angular).
        using var raw = JsonDocument.Parse(await client.GetStringAsync($"/api/surveys/{created.Id}"));
        Assert.Equal(1, raw.RootElement.GetProperty("survey").GetProperty("language").GetInt32());

        // "Ver código" de um .html do componente.
        var source = await client.GetAsync($"/api/surveys/{created.Id}/source?file=src/app/pedidos/detalhe-pedido.component.html&line=3");
        Assert.Equal(HttpStatusCode.OK, source.StatusCode);
        var snippet = await source.Content.ReadFromJsonAsync<SourceSnippet>(Json);
        Assert.Contains(snippet!.Lines, l => l.Contains("(click)", StringComparison.Ordinal));
    }

    [Fact] // API-ANG-005
    public async Task Criar_SolutionDotNet_ResumoSaiComLanguageDotNet()
    {
        var response = await Client.PostAsJsonAsync("/api/surveys", new
        {
            repository = ApiFixture.RepositoryName,
            solutionPath = ApiFixture.SolutionFile,
            entryPoint = "AlertJob.ExecuteAsync"
        });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<SurveySummary>(Json))!;

        Assert.Equal(SourceLanguage.DotNet, created.Language);
    }
}
