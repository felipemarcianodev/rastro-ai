using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Rastro.Api.Controllers;
using Rastro.Core.Surveys;

namespace Rastro.Tests.Api;

// Cenários API-SRV-NNN: levantamentos (criar, processar, listar, envelhecer) e segurança de caminho.
[Collection(ApiCollection.Name)]
public class SurveysEndpointTests(ApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private HttpClient Client => fixture.Factory.CreateClient();

    private static object Request(string? repository = ApiFixture.RepositoryName, string? solution = ApiFixture.SolutionFile,
        string? entry = "AlertJob.ExecuteAsync", int? depth = null) =>
        new { repository, solutionPath = solution, entryPoint = entry, maxDepth = depth };

    private async Task<SurveyDetail> WaitFinishedAsync(Guid id)
    {
        for (var i = 0; i < 150; i++)
        {
            var detail = await Client.GetFromJsonAsync<SurveyDetail>($"/api/surveys/{id}", Json);
            if (detail!.Survey.Status is SurveyStatus.Completed or SurveyStatus.Failed) return detail;
            await Task.Delay(200);
        }
        throw new TimeoutException($"Levantamento {id} não terminou em 30 s.");
    }

    private async Task<SurveySummary> CreateAsync(object request)
    {
        var response = await Client.PostAsJsonAsync("/api/surveys", request);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SurveySummary>(Json))!;
    }

    private async Task AssertErrorAsync(object request, string code)
    {
        var response = await Client.PostAsJsonAsync("/api/surveys", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, body.GetProperty("code").GetString());
    }

    [Fact] // API-SRV-001
    public async Task Criar_RequisicaoValida_ProcessaEmSegundoPlanoESalvaOGrafoComOCommit()
    {
        var created = await CreateAsync(Request());

        Assert.Equal(SurveyStatus.Pending, created.Status);
        Assert.Equal(await fixture.HeadAsync(), created.CommitHash);
        Assert.Equal("main", created.Branch);

        var done = await WaitFinishedAsync(created.Id);
        Assert.Equal(SurveyStatus.Completed, done.Survey.Status);
        Assert.NotNull(done.Graph);
        Assert.Equal(done.Graph.Nodes.Count, done.Survey.NodeCount);
        Assert.True(done.Survey.UnresolvedCount > 0);
        Assert.Contains(done.Graph.Nodes, n => n.Id == done.Graph.EntryNodeId);
        Assert.NotNull(done.Survey.CompletedAt);
    }

    [Fact] // API-SRV-002
    public async Task Criar_RepositorioForaDaLista_Retorna400()
    {
        await AssertErrorAsync(Request(repository: "outro"), ErrorCodes.RepositoryNotAllowed);
        await AssertErrorAsync(Request(repository: null), ErrorCodes.RepositoryNotAllowed);
    }

    [Theory] // API-SRV-003
    [InlineData("../fora.slnx")]
    [InlineData("..\\..\\fora.sln")]
    [InlineData("C:/Windows/system.sln")]
    [InlineData("/etc/passwd.sln")]
    [InlineData("Fixture.txt")]
    [InlineData("")]
    public async Task Criar_CaminhoDaSolutionForaDoRepositorioOuInvalido_Retorna400(string solution)
    {
        await AssertErrorAsync(Request(solution: solution), ErrorCodes.SolutionPathInvalid);
    }

    [Fact] // API-SRV-004
    public async Task Criar_SolutionInexistente_Retorna400()
    {
        await AssertErrorAsync(Request(solution: "NaoExiste.slnx"), ErrorCodes.SolutionNotFound);
    }

    [Theory] // API-SRV-005
    [InlineData("", null, ErrorCodes.EntryPointRequired)]
    [InlineData("   ", null, ErrorCodes.EntryPointRequired)]
    [InlineData("AlertJob.ExecuteAsync", 0, ErrorCodes.DepthOutOfRange)]
    [InlineData("AlertJob.ExecuteAsync", 41, ErrorCodes.DepthOutOfRange)]
    public async Task Criar_EntradaOuProfundidadeInvalida_Retorna400(string entry, int? depth, string code)
    {
        await AssertErrorAsync(Request(entry: entry, depth: depth), code);
    }

    [Fact] // API-SRV-006
    public async Task Criar_PontoDeEntradaInexistente_TerminaComoFalhaComMensagemClara()
    {
        var created = await CreateAsync(Request(entry: "AlertJob.NaoExiste"));

        var done = await WaitFinishedAsync(created.Id);
        Assert.Equal(SurveyStatus.Failed, done.Survey.Status);
        Assert.Contains("não encontrado", done.Survey.Error);
        Assert.Null(done.Graph);
    }

    [Fact] // API-SRV-007
    public async Task Listar_MostraOsMaisRecentesPrimeiro()
    {
        var first = await CreateAsync(Request());
        var second = await CreateAsync(Request(depth: 3));
        await WaitFinishedAsync(second.Id);

        var list = await Client.GetFromJsonAsync<List<SurveySummary>>("/api/surveys", Json);
        var ids = list!.Select(s => s.Id).ToList();
        Assert.True(ids.IndexOf(second.Id) < ids.IndexOf(first.Id));
    }

    [Fact] // API-SRV-008
    public async Task Detalhe_IdDesconhecido_Retorna404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/surveys/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/surveys/{Guid.NewGuid()}/stale")).StatusCode);
    }

    [Fact] // API-SRV-009
    public async Task Desatualizado_DepoisDeUmCommitNovo_PassaASerVerdadeiro()
    {
        var created = await CreateAsync(Request());
        await WaitFinishedAsync(created.Id);

        var before = await Client.GetFromJsonAsync<SurveyStaleness>($"/api/surveys/{created.Id}/stale", Json);
        Assert.False(before!.Stale);

        await fixture.NewCommitAsync();

        var after = await Client.GetFromJsonAsync<SurveyStaleness>($"/api/surveys/{created.Id}/stale", Json);
        Assert.True(after!.Stale);
        Assert.Equal(created.CommitHash, after.SurveyCommit);
        Assert.Equal(await fixture.HeadAsync(), after.CurrentCommit);
    }

    [Fact] // API-SRV-010
    public async Task Json_EnumsViajamComoIntENulosSaoOmitidos()
    {
        var created = await CreateAsync(Request());
        await WaitFinishedAsync(created.Id);

        var raw = await Client.GetStringAsync($"/api/surveys/{created.Id}");
        using var doc = JsonDocument.Parse(raw);
        Assert.Equal(2, doc.RootElement.GetProperty("survey").GetProperty("status").GetInt32());
        Assert.False(doc.RootElement.GetProperty("survey").TryGetProperty("error", out _));
        Assert.Equal(JsonValueKind.Number, doc.RootElement.GetProperty("graph").GetProperty("nodes")[0].GetProperty("kind").ValueKind);
    }
}
