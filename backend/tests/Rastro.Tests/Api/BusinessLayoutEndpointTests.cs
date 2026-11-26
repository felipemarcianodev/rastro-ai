using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastro.Api.Controllers;
using Rastro.Core.Business;
using Rastro.Infrastructure;
using Rastro.Mcp;

namespace Rastro.Tests.Api;

// Cenários API-BLY-NNN: posição dos balões e pontos de dobra da visão de negócio (Fase 58).
[Collection(ApiCollection.Name)]
public class BusinessLayoutEndpointTests(ApiFixture fixture)
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

    private static BusinessLayoutDto Layout(
        IEnumerable<StepPositionDto>? steps = null, IEnumerable<EdgePointsDto>? edges = null) =>
        new((steps ?? []).ToList(), (edges ?? []).ToList());

    private Task<HttpResponseMessage> PutAsync(Guid id, BusinessLayoutDto layout) =>
        Client.PutAsJsonAsync($"/api/surveys/{id}/business/layout", layout, Json);

    private Task<BusinessLayoutDto?> ReadAsync(Guid id) =>
        Client.GetFromJsonAsync<BusinessLayoutDto>($"/api/surveys/{id}/business/layout", Json);

    [Fact] // API-BLY-001
    public async Task Layout_SemSalvo_DevolveListasVazias()
    {
        var id = await SurveyAsync();

        var read = await ReadAsync(id);

        Assert.Empty(read!.Steps);
        Assert.Empty(read.Edges);
    }

    [Fact] // API-BLY-002
    public async Task Layout_SalvaELeDepois_JsonEmCamelCase()
    {
        var id = await SurveyAsync();
        var layout = Layout(
            [new("b1", 10.5, -20), new("b2", 300, 400)],
            [new("b1", "b2", [new LayoutPoint(50, 60), new LayoutPoint(70, 80)])]);

        var saved = await PutAsync(id, layout);
        var raw = await Client.GetStringAsync($"/api/surveys/{id}/business/layout");
        var read = await ReadAsync(id);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var doc = JsonDocument.Parse(raw);
        Assert.Equal("b1", doc.RootElement.GetProperty("steps")[0].GetProperty("stepId").GetString());
        Assert.Equal(10.5, doc.RootElement.GetProperty("steps")[0].GetProperty("x").GetDouble());
        Assert.Equal("b2", doc.RootElement.GetProperty("edges")[0].GetProperty("to").GetString());
        Assert.Equal(2, read!.Steps.Count);
        Assert.Equal(new StepPositionDto("b1", 10.5, -20), read.Steps.Single(s => s.StepId == "b1"));
        Assert.Equal([new LayoutPoint(50, 60), new LayoutPoint(70, 80)], read.Edges.Single().Points);
    }

    [Fact] // API-BLY-003
    public async Task Layout_PutParcial_TrocaSoOQueVeioEMantemOResto()
    {
        var id = await SurveyAsync();
        await PutAsync(id, Layout(
            [new("b1", 1, 1), new("b2", 2, 2)],
            [new("b1", "b2", [new LayoutPoint(5, 5)]), new("b2", "b3", [new LayoutPoint(6, 6)])]));

        await PutAsync(id, Layout([new("b2", 99, 98)], [new("b1", "b2", [])]));
        var read = await ReadAsync(id);

        Assert.Equal(new StepPositionDto("b1", 1, 1), read!.Steps.Single(s => s.StepId == "b1"));
        Assert.Equal(new StepPositionDto("b2", 99, 98), read.Steps.Single(s => s.StepId == "b2"));
        Assert.Empty(read.Edges.Single(e => e.From == "b1").Points);
        Assert.Equal([new LayoutPoint(6, 6)], read.Edges.Single(e => e.From == "b2").Points);
    }

    [Fact] // API-BLY-004
    public async Task Layout_Reorganizar_ApagaPosicoesEPontosDeDobra()
    {
        var id = await SurveyAsync();
        await PutAsync(id, Layout([new("b1", 1, 1)], [new("b1", "b2", [new LayoutPoint(5, 5)])]));

        var deleted = await Client.DeleteAsync($"/api/surveys/{id}/business/layout");
        var read = await ReadAsync(id);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Empty(read!.Steps);
        Assert.Empty(read.Edges);
    }

    [Fact] // API-BLY-005
    public async Task Layout_Reorganizar_NaoApagaAPosicaoDeOutroLevantamento()
    {
        var first = await SurveyAsync();
        var second = await SurveyAsync();
        await PutAsync(first, Layout([new("b1", 1, 1)]));
        await PutAsync(second, Layout([new("b1", 7, 7)]));

        await Client.DeleteAsync($"/api/surveys/{first}/business/layout");

        Assert.Empty((await ReadAsync(first))!.Steps);
        Assert.Equal(7, (await ReadAsync(second))!.Steps.Single().X);
    }

    [Fact] // API-BLY-006
    public async Task Layout_LevantamentoInexistente_Devolve404NasTresRotas()
    {
        var missing = Guid.NewGuid();

        var get = await Client.GetAsync($"/api/surveys/{missing}/business/layout");
        var put = await PutAsync(missing, Layout([new("b1", 1, 1)]));
        var delete = await Client.DeleteAsync($"/api/surveys/{missing}/business/layout");

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    public static TheoryData<string, BusinessLayoutDto> Invalid()
    {
        var steps501 = Enumerable.Range(0, 501).Select(i => new StepPositionDto($"s{i}", 0, 0)).ToList();
        var edges501 = Enumerable.Range(0, 501).Select(i => new EdgePointsDto($"a{i}", $"b{i}", [])).ToList();
        var points21 = Enumerable.Range(0, 21).Select(i => new LayoutPoint(i, i)).ToList();
        return new TheoryData<string, BusinessLayoutDto>
        {
            { "501 passos", new(steps501, []) },
            { "501 ligações", new([], edges501) },
            { "21 pontos", Layout(edges: [new("a", "b", points21)]) },
            { "x fora da faixa", Layout([new("a", 100001, 0)]) },
            { "y fora da faixa", Layout([new("a", 0, -100001)]) },
            { "ponto fora da faixa", Layout(edges: [new("a", "b", [new LayoutPoint(0, 200000)])]) },
            { "stepId com 65", Layout([new(new string('a', 65), 0, 0)]) },
            { "stepId vazio", Layout([new(" ", 0, 0)]) },
            { "from com 65", Layout(edges: [new(new string('a', 65), "b", [])]) },
        };
    }

    [Theory] // API-BLY-007
    [MemberData(nameof(Invalid))]
    public async Task Layout_ForaDosLimites_Recusa400ComCodigoENadaGrava(string cenario, BusinessLayoutDto layout)
    {
        var id = await SurveyAsync();

        var response = await PutAsync(id, layout);
        var read = await ReadAsync(id);

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, cenario);
        Assert.Contains(ErrorCodes.BusinessLayoutInvalid, await response.Content.ReadAsStringAsync());
        Assert.Empty(read!.Steps);
        Assert.Empty(read.Edges);
    }

    [Fact] // API-BLY-010
    public void Limites_NaoFinito_Recusa()
    {
        // NaN e infinito não têm representação em JSON (não chegam pela rede), mas o validador também os recusa.
        Assert.False(BusinessLayoutLimits.IsValid(Layout([new("a", double.NaN, 0)])));
        Assert.False(BusinessLayoutLimits.IsValid(Layout([new("a", 0, double.PositiveInfinity)])));
        Assert.True(BusinessLayoutLimits.IsValid(Layout([new("a", 0, 0)])));
    }

    [Fact] // API-BLY-008
    public async Task Layout_NosLimites_Aceita()
    {
        var id = await SurveyAsync();
        var steps = Enumerable.Range(0, 500).Select(i => new StepPositionDto($"s{i}", -100000, 100000)).ToList();
        var edges = Enumerable.Range(0, 500).Select(i => new EdgePointsDto($"a{i}", $"b{i}", Enumerable.Range(0, 20).Select(p => new LayoutPoint(p, p)).ToList())).ToList();

        var response = await PutAsync(id, new BusinessLayoutDto(steps, edges));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var read = await ReadAsync(id);
        Assert.Equal(500, read!.Steps.Count);
        Assert.Equal(500, read.Edges.Count);
    }

    [Fact] // API-BLY-009
    public async Task Layout_ExcluirLevantamento_ApagaPosicoesESoDele()
    {
        var doomed = await SurveyAsync();
        var kept = await SurveyAsync();
        await PutAsync(doomed, Layout([new("b1", 1, 1)], [new("b1", "b2", [new LayoutPoint(5, 5)])]));
        await PutAsync(kept, Layout([new("b1", 2, 2)]));

        var deleted = await Client.DeleteAsync($"/api/surveys/{doomed}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await using var provider = fixture.McpServices();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RastroDbContext>();
        Assert.False(await db.BusinessStepLayouts.AnyAsync(l => l.ScopeId == doomed));
        Assert.False(await db.BusinessEdgeLayouts.AnyAsync(l => l.ScopeId == doomed));
        Assert.True(await db.BusinessStepLayouts.AnyAsync(l => l.ScopeId == kept));
    }
}
