using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Rastro.Api.Controllers;
using Rastro.Core.Business;
using Rastro.Core.Surveys;
using Rastro.Infrastructure;
using Rastro.Infrastructure.Business;
using Rastro.Mcp;

namespace Rastro.Tests.Api;

// Cenários API-BRD-NNN e MCP-BRD-NNN: quadro de fluxos (Fase 60), pela API e pelo agente. Os levantamentos são o
// AlertJob.ExecuteAsync da fixture Alerts; o repositório "fixture" está no cadastro da API, então o "o código mudou"
// se confere de verdade.
[Collection(ApiCollection.Name)]
public class BoardEndpointTests(ApiFixture fixture)
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

    // Levantamento que ainda não terminou (Pending): não entra em quadro.
    private async Task<Guid> PendingSurveyAsync()
    {
        await using var provider = fixture.McpServices();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RastroDbContext>();
        var survey = Survey.Create(ApiFixture.RepositoryName, ApiFixture.SolutionFile, "AlertJob.ExecuteAsync", 5, null, null, DateTimeOffset.UtcNow);
        db.Surveys.Add(survey);
        await db.SaveChangesAsync();
        return survey.Id;
    }

    private async Task<T> WithDbAsync<T>(Func<RastroDbContext, Task<T>> read)
    {
        await using var provider = fixture.McpServices();
        await using var scope = provider.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<RastroDbContext>());
    }

    private Task<HttpResponseMessage> PostBoardAsync(string? name, IEnumerable<Guid> surveyIds, IEnumerable<string>? titles = null) =>
        Client.PostAsJsonAsync("/api/boards", new { name, surveyIds, titles }, Json);

    private async Task<BoardDetail> CreateBoardAsync(string name, params Guid[] surveyIds)
    {
        var response = await PostBoardAsync(name, surveyIds);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BoardDetail>(Json))!;
    }

    private async Task<BoardDetail> GetBoardAsync(Guid id) =>
        (await Client.GetFromJsonAsync<BoardDetail>($"/api/boards/{id}", Json))!;

    private Task<HttpResponseMessage> PutBoardAsync(Guid id, string? name, string? glossary, IEnumerable<object> lanes) =>
        Client.PutAsJsonAsync($"/api/boards/{id}", new { name, glossary, lanes }, Json);

    private Task<HttpResponseMessage> PutLayoutAsync(Guid id, BoardLayoutDto layout) =>
        Client.PutAsJsonAsync($"/api/boards/{id}/layout", layout, Json);

    private static BoardLayoutDto Layout(IEnumerable<BoardStepPositionDto>? steps = null, IEnumerable<BoardEdgePointsDto>? edges = null) =>
        new((steps ?? []).ToList(), (edges ?? []).ToList());

    private static async Task AssertCodeAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"{response.StatusCode}: {body}");
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(code, doc.RootElement.GetProperty("code").GetString());
    }

    [Fact] // API-BRD-001
    public async Task Criar_DoisLevantamentos_DevolveOQuadroComFaixasEmOrdemETituloDoPontoDeEntrada()
    {
        var first = await SurveyAsync();
        var second = await SurveyAsync();

        var response = await PostBoardAsync("  Cancelamento  ", [second, first], ["Segunda", ""]);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.EndsWith($"/api/boards/{(await response.Content.ReadFromJsonAsync<BoardDetail>(Json))!.Id}", response.Headers.Location!.AbsolutePath);
        var raw = await response.Content.ReadAsStringAsync();
        var board = JsonSerializer.Deserialize<BoardDetail>(raw, Json)!;
        Assert.Equal("Cancelamento", board.Name);
        Assert.Null(board.Glossary);
        Assert.Equal([second, first], board.Lanes.Select(l => l.SurveyId));
        Assert.Equal([0, 1], board.Lanes.Select(l => l.Order));
        Assert.Equal("Segunda", board.Lanes[0].Title);
        Assert.Equal("AlertJob.ExecuteAsync", board.Lanes[1].Title);
        Assert.All(board.Lanes, l =>
        {
            Assert.Equal("AlertJob.ExecuteAsync", l.EntryPoint);
            Assert.Equal([ApiFixture.RepositoryName], l.Repositories);
            Assert.Equal(SurveyStatus.Completed, l.Status);
            Assert.NotNull(l.Flow);
            Assert.NotEmpty(l.Flow!.Steps);
        });
        Assert.Empty(board.Layout.Steps);
        Assert.Empty(board.Layout.Edges);

        // JSON em camelCase e enum como int.
        using var doc = JsonDocument.Parse(raw);
        var lane = doc.RootElement.GetProperty("lanes")[0];
        Assert.Equal(JsonValueKind.Number, lane.GetProperty("status").ValueKind);
        Assert.Equal(JsonValueKind.Number, lane.GetProperty("flow").GetProperty("steps")[0].GetProperty("kind").ValueKind);
        Assert.False(lane.GetProperty("stale").GetBoolean());
        Assert.Equal(JsonValueKind.Array, doc.RootElement.GetProperty("layout").GetProperty("steps").ValueKind);
    }

    [Fact] // API-BRD-002
    public async Task Listar_MostraNomeContagemDeFaixasEData()
    {
        var one = await SurveyAsync();
        var two = await SurveyAsync();
        var board = await CreateBoardAsync("Lista", one, two);

        var list = (await Client.GetFromJsonAsync<List<BoardSummary>>("/api/boards", Json))!;

        var item = Assert.Single(list, b => b.Id == board.Id);
        Assert.Equal("Lista", item.Name);
        Assert.Equal(2, item.LaneCount);
        Assert.True(item.UpdatedAt > DateTimeOffset.UtcNow.AddMinutes(-5));
        using var doc = JsonDocument.Parse(await Client.GetStringAsync("/api/boards"));
        Assert.True(doc.RootElement.EnumerateArray().First(e => e.GetProperty("id").GetGuid() == board.Id).TryGetProperty("laneCount", out _));
    }

    [Fact] // API-BRD-003
    public async Task Criar_ForaDasRegras_RecusaComCodigoENadaGrava()
    {
        var survey = await SurveyAsync();
        var pending = await PendingSurveyAsync();
        var before = (await Client.GetFromJsonAsync<List<BoardSummary>>("/api/boards", Json))!.Count;

        await AssertCodeAsync(await PostBoardAsync("   ", [survey]), HttpStatusCode.BadRequest, ErrorCodes.BoardNameInvalid);
        await AssertCodeAsync(await PostBoardAsync(new string('n', 121), [survey]), HttpStatusCode.BadRequest, ErrorCodes.BoardNameInvalid);
        await AssertCodeAsync(await PostBoardAsync("Duas\nlinhas", [survey]), HttpStatusCode.BadRequest, ErrorCodes.BoardNameInvalid);
        await AssertCodeAsync(await PostBoardAsync("Q", []), HttpStatusCode.BadRequest, ErrorCodes.BoardLanesInvalid);
        await AssertCodeAsync(await PostBoardAsync("Q", Enumerable.Range(0, 13).Select(_ => Guid.NewGuid())), HttpStatusCode.BadRequest, ErrorCodes.BoardLanesInvalid);
        await AssertCodeAsync(await PostBoardAsync("Q", [survey, survey]), HttpStatusCode.BadRequest, ErrorCodes.BoardLanesInvalid);
        await AssertCodeAsync(await PostBoardAsync("Q", [survey], [new string('t', 121)]), HttpStatusCode.BadRequest, ErrorCodes.BoardLanesInvalid);
        await AssertCodeAsync(await PostBoardAsync("Q", [survey], ["a", "b"]), HttpStatusCode.BadRequest, ErrorCodes.BoardLanesInvalid);
        await AssertCodeAsync(await PostBoardAsync("Q", [survey, Guid.NewGuid()]), HttpStatusCode.NotFound, ErrorCodes.BoardSurveyNotFound);
        await AssertCodeAsync(await PostBoardAsync("Q", [survey, pending]), HttpStatusCode.Conflict, ErrorCodes.BoardSurveyNotReady);

        Assert.Equal(before, (await Client.GetFromJsonAsync<List<BoardSummary>>("/api/boards", Json))!.Count);
    }

    [Fact] // API-BRD-004
    public async Task Criar_NosLimites_AceitaDozeFaixasENomeDeCentoEVinte()
    {
        var ids = new List<Guid>();
        for (var i = 0; i < 12; i++) ids.Add(await SurveyAsync());

        var response = await PostBoardAsync(new string('n', 120), ids, [new string('t', 120)]);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var board = (await response.Content.ReadFromJsonAsync<BoardDetail>(Json))!;
        Assert.Equal(12, board.Lanes.Count);
        Assert.Equal(new string('t', 120), board.Lanes[0].Title);
    }

    [Fact] // API-BRD-005
    public async Task Obter_QuadroInexistente_404ComCodigoNasRotasDoQuadro()
    {
        var missing = Guid.NewGuid();
        var survey = await SurveyAsync();

        await AssertCodeAsync(await Client.GetAsync($"/api/boards/{missing}"), HttpStatusCode.NotFound, ErrorCodes.BoardNotFound);
        await AssertCodeAsync(await PutBoardAsync(missing, "X", null, [new { surveyId = survey, order = 0 }]), HttpStatusCode.NotFound, ErrorCodes.BoardNotFound);
        await AssertCodeAsync(await Client.PostAsJsonAsync($"/api/boards/{missing}/lanes", new { surveyId = survey }, Json), HttpStatusCode.NotFound, ErrorCodes.BoardNotFound);
        await AssertCodeAsync(await Client.DeleteAsync($"/api/boards/{missing}"), HttpStatusCode.NotFound, ErrorCodes.BoardNotFound);
        await AssertCodeAsync(await PutLayoutAsync(missing, Layout()), HttpStatusCode.NotFound, ErrorCodes.BoardNotFound);
        await AssertCodeAsync(await Client.DeleteAsync($"/api/boards/{missing}/layout"), HttpStatusCode.NotFound, ErrorCodes.BoardNotFound);
        await AssertCodeAsync(await Client.PutAsJsonAsync($"/api/boards/{missing}/lanes/{survey}/replace", new { newSurveyId = survey }, Json), HttpStatusCode.NotFound, ErrorCodes.BoardNotFound);
    }

    [Fact] // API-BRD-006
    public async Task Atualizar_RenomeiaGravaGlossarioReordenaTituloETiraFaixaComAPosicaoDela()
    {
        var a = await SurveyAsync();
        var b = await SurveyAsync();
        var c = await SurveyAsync();
        var board = await CreateBoardAsync("Antes", a, b, c);
        await PutLayoutAsync(board.Id, Layout([new(a, "s1", 1, 1), new(b, "s1", 2, 2), new(c, "s1", 3, 3)]));

        var response = await PutBoardAsync(board.Id, "  Depois ", "Alerta: aviso ao cliente.\nFila: fila de envio.",
            [new { surveyId = c, order = 0, title = "Primeira" }, new { surveyId = a, order = 5, title = "" }]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<BoardDetail>(Json))!;
        Assert.Equal("Depois", updated.Name);
        Assert.Equal("Alerta: aviso ao cliente.\nFila: fila de envio.", updated.Glossary);
        Assert.Equal([c, a], updated.Lanes.Select(l => l.SurveyId));
        Assert.Equal([0, 1], updated.Lanes.Select(l => l.Order));
        Assert.Equal("Primeira", updated.Lanes[0].Title);
        Assert.Equal("AlertJob.ExecuteAsync", updated.Lanes[1].Title);
        // A posição da faixa que saiu foi junto; as das outras ficam.
        Assert.Equal(new[] { a, c }.Order(), updated.Layout.Steps.Select(s => s.LaneSurveyId).Order());
        Assert.Equal(2, (await Client.GetFromJsonAsync<List<BoardSummary>>("/api/boards", Json))!.Single(s => s.Id == board.Id).LaneCount);

        // Glossário vazio volta a nulo.
        var cleared = (await (await PutBoardAsync(board.Id, "Depois", "  ", [new { surveyId = c, order = 0 }])).Content.ReadFromJsonAsync<BoardDetail>(Json))!;
        Assert.Null(cleared.Glossary);
        Assert.Single(cleared.Lanes);
    }

    [Fact] // API-BRD-007
    public async Task Atualizar_ForaDasRegras_RecusaENadaMuda()
    {
        var a = await SurveyAsync();
        var outside = await SurveyAsync();
        var board = await CreateBoardAsync("Q", a);

        await AssertCodeAsync(await PutBoardAsync(board.Id, "", null, [new { surveyId = a, order = 0 }]), HttpStatusCode.BadRequest, ErrorCodes.BoardNameInvalid);
        await AssertCodeAsync(await PutBoardAsync(board.Id, "Q", new string('g', 8001), [new { surveyId = a, order = 0 }]), HttpStatusCode.BadRequest, ErrorCodes.BoardGlossaryInvalid);
        await AssertCodeAsync(await PutBoardAsync(board.Id, "Q", "linha\u0001com controle", [new { surveyId = a, order = 0 }]), HttpStatusCode.BadRequest, ErrorCodes.BoardGlossaryInvalid);
        await AssertCodeAsync(await PutBoardAsync(board.Id, "Q", null, []), HttpStatusCode.BadRequest, ErrorCodes.BoardLanesInvalid);
        await AssertCodeAsync(await PutBoardAsync(board.Id, "Q", null, [new { surveyId = outside, order = 0 }]), HttpStatusCode.BadRequest, ErrorCodes.BoardLanesInvalid);
        await AssertCodeAsync(await PutBoardAsync(board.Id, "Q", null, [new { surveyId = a, order = 0 }, new { surveyId = a, order = 1 }]), HttpStatusCode.BadRequest, ErrorCodes.BoardLanesInvalid);
        await AssertCodeAsync(await PutBoardAsync(board.Id, "Q", null, [new { surveyId = a, order = 0, title = new string('t', 121) }]), HttpStatusCode.BadRequest, ErrorCodes.BoardLanesInvalid);

        var same = await GetBoardAsync(board.Id);
        Assert.Equal("Q", same.Name);
        Assert.Single(same.Lanes);

        // Glossário no limite (8000) é aceito.
        Assert.Equal(HttpStatusCode.OK, (await PutBoardAsync(board.Id, "Q", new string('g', 8000), [new { surveyId = a, order = 0 }])).StatusCode);
    }

    [Fact] // API-BRD-008
    public async Task AdicionarFaixa_AcrescentaNoFimERecusaRepetidaPendenteAusenteEDecimaTerceira()
    {
        var first = await SurveyAsync();
        var board = await CreateBoardAsync("Q", first);
        var second = await SurveyAsync();

        var added = await Client.PostAsJsonAsync($"/api/boards/{board.Id}/lanes", new { surveyId = second, title = "Segunda" }, Json);

        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var detail = (await added.Content.ReadFromJsonAsync<BoardDetail>(Json))!;
        Assert.Equal([first, second], detail.Lanes.Select(l => l.SurveyId));
        Assert.Equal("Segunda", detail.Lanes[1].Title);
        Assert.Equal(1, detail.Lanes[1].Order);

        await AssertCodeAsync(await Client.PostAsJsonAsync($"/api/boards/{board.Id}/lanes", new { surveyId = second }, Json), HttpStatusCode.Conflict, ErrorCodes.BoardLaneExists);
        await AssertCodeAsync(await Client.PostAsJsonAsync($"/api/boards/{board.Id}/lanes", new { surveyId = Guid.NewGuid() }, Json), HttpStatusCode.NotFound, ErrorCodes.BoardSurveyNotFound);
        await AssertCodeAsync(await Client.PostAsJsonAsync($"/api/boards/{board.Id}/lanes", new { surveyId = await PendingSurveyAsync() }, Json), HttpStatusCode.Conflict, ErrorCodes.BoardSurveyNotReady);
        await AssertCodeAsync(await Client.PostAsJsonAsync($"/api/boards/{board.Id}/lanes", new { surveyId = await SurveyAsync(), title = new string('t', 121) }, Json), HttpStatusCode.BadRequest, ErrorCodes.BoardLanesInvalid);

        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.OK, (await Client.PostAsJsonAsync($"/api/boards/{board.Id}/lanes", new { surveyId = await SurveyAsync() }, Json)).StatusCode);
        Assert.Equal(12, (await GetBoardAsync(board.Id)).Lanes.Count);
        await AssertCodeAsync(await Client.PostAsJsonAsync($"/api/boards/{board.Id}/lanes", new { surveyId = await SurveyAsync() }, Json), HttpStatusCode.BadRequest, ErrorCodes.BoardLanesInvalid);
    }

    [Fact] // API-BRD-009
    public async Task Excluir_ApagaFaixasEPosicoesDoQuadroENaoMexeNoLevantamentoNemEmOutroQuadro()
    {
        var survey = await SurveyAsync();
        var doomed = await CreateBoardAsync("Apagar", survey);
        var kept = await CreateBoardAsync("Ficar", survey);
        await PutLayoutAsync(doomed.Id, Layout([new(survey, "s1", 1, 1)], [new(survey, "s1", "s2", [new LayoutPoint(5, 5)])]));
        await PutLayoutAsync(kept.Id, Layout([new(survey, "s1", 9, 9)]));
        await Client.PutAsJsonAsync($"/api/surveys/{survey}/business/layout", new BusinessLayoutDto([new StepPositionDto("s1", 7, 7)], []), Json);

        var deleted = await Client.DeleteAsync($"/api/boards/{doomed.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/boards/{doomed.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync($"/api/surveys/{survey}")).StatusCode);
        await WithDbAsync(async db =>
        {
            Assert.False(await db.BoardLanes.AnyAsync(l => l.BoardId == doomed.Id));
            Assert.False(await db.BusinessStepLayouts.AnyAsync(l => l.Scope == LayoutScope.Board && l.ScopeId == doomed.Id));
            Assert.False(await db.BusinessEdgeLayouts.AnyAsync(l => l.Scope == LayoutScope.Board && l.ScopeId == doomed.Id));
            Assert.True(await db.BusinessStepLayouts.AnyAsync(l => l.Scope == LayoutScope.Board && l.ScopeId == kept.Id));
            return true;
        });
        Assert.Equal(9, (await GetBoardAsync(kept.Id)).Layout.Steps.Single().X);
        // A posição do levantamento sozinho (Fase 58) não é do quadro.
        Assert.Equal(7, (await Client.GetFromJsonAsync<BusinessLayoutDto>($"/api/surveys/{survey}/business/layout", Json))!.Steps.Single().X);
    }

    [Fact] // API-BRD-010
    public async Task Layout_SalvaPorFaixaEOMesmoLevantamentoTemPosicaoPropriaEmCadaQuadro()
    {
        var a = await SurveyAsync();
        var b = await SurveyAsync();
        var one = await CreateBoardAsync("Um", a, b);
        var two = await CreateBoardAsync("Dois", a);

        var saved = await PutLayoutAsync(one.Id, Layout(
            [new(a, "s1", 10.5, -20), new(b, "s1", 300, 400)],
            [new(a, "s1", "s2", [new LayoutPoint(50, 60), new LayoutPoint(70, 80)])]));
        await PutLayoutAsync(two.Id, Layout([new(a, "s1", 1, 1)]));

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var raw = await saved.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        Assert.Contains(doc.RootElement.GetProperty("steps").EnumerateArray(), e => e.GetProperty("laneSurveyId").GetGuid() == a);
        Assert.Equal(a, doc.RootElement.GetProperty("edges")[0].GetProperty("laneSurveyId").GetGuid());
        var read = (await GetBoardAsync(one.Id)).Layout;
        Assert.Equal(2, read.Steps.Count);
        Assert.Equal(10.5, read.Steps.Single(s => s.LaneSurveyId == a).X);
        Assert.Equal(300, read.Steps.Single(s => s.LaneSurveyId == b).X);
        Assert.Equal([new LayoutPoint(50, 60), new LayoutPoint(70, 80)], read.Edges.Single().Points);
        Assert.Equal(1, (await GetBoardAsync(two.Id)).Layout.Steps.Single().X);

        // PUT parcial: troca só o que veio.
        await PutLayoutAsync(one.Id, Layout([new(a, "s1", 99, 98)]));
        var partial = (await GetBoardAsync(one.Id)).Layout;
        Assert.Equal(99, partial.Steps.Single(s => s.LaneSurveyId == a).X);
        Assert.Equal(300, partial.Steps.Single(s => s.LaneSurveyId == b).X);
        Assert.Single(partial.Edges);
    }

    [Fact] // API-BRD-011
    public async Task Layout_FaixaDeOutroQuadroOuForaDosLimites_Recusa400ENadaGrava()
    {
        var a = await SurveyAsync();
        var other = await SurveyAsync();
        var board = await CreateBoardAsync("Q", a);
        var steps501 = Enumerable.Range(0, 501).Select(i => new BoardStepPositionDto(a, $"s{i}", 0, 0)).ToList();
        var edges501 = Enumerable.Range(0, 501).Select(i => new BoardEdgePointsDto(a, $"a{i}", $"b{i}", [])).ToList();
        var points21 = Enumerable.Range(0, 21).Select(i => new LayoutPoint(i, i)).ToList();

        var cases = new (string Name, BoardLayoutDto Layout)[]
        {
            ("faixa de fora", Layout([new(other, "s1", 0, 0)])),
            ("faixa de fora na ligação", Layout(edges: [new(other, "a", "b", [])])),
            ("faixa vazia", Layout([new(Guid.Empty, "s1", 0, 0)])),
            ("501 passos", new(steps501, [])),
            ("501 ligações", new([], edges501)),
            ("21 pontos", Layout(edges: [new(a, "a", "b", points21)])),
            ("x fora da faixa", Layout([new(a, "s1", 100001, 0)])),
            ("stepId com 65", Layout([new(a, new string('a', 65), 0, 0)])),
            ("stepId vazio", Layout([new(a, " ", 0, 0)]))
        };
        foreach (var (name, layout) in cases)
        {
            var response = await PutLayoutAsync(board.Id, layout);
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, name);
            Assert.Contains(ErrorCodes.BusinessLayoutInvalid, await response.Content.ReadAsStringAsync());
        }
        Assert.Empty((await GetBoardAsync(board.Id)).Layout.Steps);
        Assert.Empty((await GetBoardAsync(board.Id)).Layout.Edges);

        // Nos limites: 500 passos e 500 ligações com 20 pontos.
        var steps500 = Enumerable.Range(0, 500).Select(i => new BoardStepPositionDto(a, $"s{i}", -100000, 100000)).ToList();
        var edges500 = Enumerable.Range(0, 500).Select(i => new BoardEdgePointsDto(a, $"a{i}", $"b{i}", Enumerable.Range(0, 20).Select(p => new LayoutPoint(p, p)).ToList())).ToList();
        Assert.Equal(HttpStatusCode.OK, (await PutLayoutAsync(board.Id, new BoardLayoutDto(steps500, edges500))).StatusCode);
        Assert.Equal(500, (await GetBoardAsync(board.Id)).Layout.Steps.Count);
    }

    [Fact] // API-BRD-012
    public async Task Reorganizar_ApagaUmaFaixaOuOQuadroInteiroEFaixaDesconhecidaDa404()
    {
        var a = await SurveyAsync();
        var b = await SurveyAsync();
        var board = await CreateBoardAsync("Q", a, b);
        var other = await CreateBoardAsync("Outro", a);
        await PutLayoutAsync(board.Id, Layout([new(a, "s1", 1, 1), new(b, "s1", 2, 2)], [new(a, "s1", "s2", []), new(b, "s1", "s2", [])]));
        await PutLayoutAsync(other.Id, Layout([new(a, "s1", 5, 5)]));

        var lane = await Client.DeleteAsync($"/api/boards/{board.Id}/layout?lane={a}");

        Assert.Equal(HttpStatusCode.NoContent, lane.StatusCode);
        var afterLane = (await GetBoardAsync(board.Id)).Layout;
        Assert.Equal(b, afterLane.Steps.Single().LaneSurveyId);
        Assert.Equal(b, afterLane.Edges.Single().LaneSurveyId);
        Assert.Equal(5, (await GetBoardAsync(other.Id)).Layout.Steps.Single().X);

        await AssertCodeAsync(await Client.DeleteAsync($"/api/boards/{board.Id}/layout?lane={Guid.NewGuid()}"), HttpStatusCode.NotFound, ErrorCodes.BoardLaneNotFound);

        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/api/boards/{board.Id}/layout")).StatusCode);
        var all = (await GetBoardAsync(board.Id)).Layout;
        Assert.Empty(all.Steps);
        Assert.Empty(all.Edges);
        Assert.Single((await GetBoardAsync(other.Id)).Layout.Steps);
    }

    [Fact] // API-BRD-013
    public async Task ExcluirLevantamento_TiraAFaixaEAPosicaoDelaENaoApagaOQuadro()
    {
        var doomed = await SurveyAsync();
        var kept = await SurveyAsync();
        var board = await CreateBoardAsync("Q", doomed, kept);
        await PutLayoutAsync(board.Id, Layout([new(doomed, "s1", 1, 1), new(kept, "s1", 2, 2)], [new(doomed, "s1", "s2", [])]));

        var deleted = await Client.DeleteAsync($"/api/surveys/{doomed}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var after = await GetBoardAsync(board.Id);
        Assert.Equal([kept], after.Lanes.Select(l => l.SurveyId));
        Assert.Equal(kept, after.Layout.Steps.Single().LaneSurveyId);
        Assert.Empty(after.Layout.Edges);
        Assert.Equal(1, (await Client.GetFromJsonAsync<List<BoardSummary>>("/api/boards", Json))!.Single(s => s.Id == board.Id).LaneCount);
    }

    [Fact] // API-BRD-014
    public async Task Obter_CodigoMudouDesdeOLevantamento_MarcaAFaixaComoDesatualizada()
    {
        var survey = await SurveyAsync();
        var board = await CreateBoardAsync("Q", survey);
        Assert.False((await GetBoardAsync(board.Id)).Lanes[0].Stale);

        await fixture.NewCommitAsync();

        var lane = (await GetBoardAsync(board.Id)).Lanes[0];
        Assert.True(lane.Stale);
        var stale = await Client.GetFromJsonAsync<SurveyStaleness>($"/api/surveys/{survey}/stale", Json);
        Assert.True(stale!.Stale);
    }

    [Fact] // API-BRD-015
    public async Task Obter_LevantamentoDaFaixaSemGrafo_FaixaSemFluxoENaoQuebraOQuadro()
    {
        var running = await SurveyAsync();
        var done = await SurveyAsync();
        var board = await CreateBoardAsync("Q", running, done);
        // Simula o "Levantando...": o levantamento volta a rodar e ainda não tem grafo.
        await WithDbAsync(async db =>
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE surveys SET status = 1, graph_json = NULL WHERE id = {running}"));

        var lanes = (await GetBoardAsync(board.Id)).Lanes;

        Assert.Equal(SurveyStatus.Running, lanes[0].Status);
        Assert.Null(lanes[0].Flow);
        Assert.NotNull(lanes[1].Flow);
    }

    [Fact] // API-BRD-016
    public async Task TrocarLevantamento_LevaAsPosicoesDosPassosQueContinuamEMantemOrdemETitulo()
    {
        var oldSurvey = await SurveyAsync();
        var middle = await SurveyAsync();
        var newSurvey = await SurveyAsync();
        var board = await CreateBoardAsync("Q", middle, oldSurvey);
        var oldFlow = (await Client.GetFromJsonAsync<BusinessFlow>($"/api/surveys/{oldSurvey}/business", Json))!;
        var newFlow = (await Client.GetFromJsonAsync<BusinessFlow>($"/api/surveys/{newSurvey}/business", Json))!;
        Assert.Equal(oldFlow.Steps.Select(s => s.NodeId), newFlow.Steps.Select(s => s.NodeId));
        await PutBoardAsync(board.Id, "Q", null, [new { surveyId = middle, order = 0 }, new { surveyId = oldSurvey, order = 1, title = "Cancelar" }]);
        var first = oldFlow.Steps[0];
        var second = oldFlow.Steps[1];
        await PutLayoutAsync(board.Id, Layout(
            [new(oldSurvey, first.Id, 11, 12), new(oldSurvey, second.Id, 21, 22), new(middle, "s1", 3, 3)],
            [new(oldSurvey, first.Id, second.Id, [new LayoutPoint(1, 2)])]));

        var response = await Client.PutAsJsonAsync($"/api/boards/{board.Id}/lanes/{oldSurvey}/replace", new { newSurveyId = newSurvey }, Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<ReplaceBoardLaneResponse>(Json))!;
        Assert.Empty(result.RemovedSteps);
        Assert.Equal([middle, newSurvey], result.Board.Lanes.Select(l => l.SurveyId));
        Assert.Equal("Cancelar", result.Board.Lanes[1].Title);
        Assert.Equal(1, result.Board.Lanes[1].Order);
        var layout = result.Board.Layout;
        Assert.DoesNotContain(layout.Steps, s => s.LaneSurveyId == oldSurvey);
        var firstNew = newFlow.Steps[0].Id;
        var secondNew = newFlow.Steps[1].Id;
        Assert.NotEqual(first.Id, firstNew);
        Assert.Equal((11, 12), layout.Steps.Where(s => s.LaneSurveyId == newSurvey && s.StepId == firstNew).Select(s => (s.X, s.Y)).Single());
        Assert.Equal((21, 22), layout.Steps.Where(s => s.LaneSurveyId == newSurvey && s.StepId == secondNew).Select(s => (s.X, s.Y)).Single());
        var edge = Assert.Single(layout.Edges);
        Assert.Equal((newSurvey, firstNew, secondNew), (edge.LaneSurveyId, edge.From, edge.To));
        Assert.Equal([new LayoutPoint(1, 2)], edge.Points);
        Assert.Equal(3, layout.Steps.Single(s => s.LaneSurveyId == middle).X);
        // O levantamento antigo continua existindo.
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync($"/api/surveys/{oldSurvey}")).StatusCode);
    }

    [Fact] // API-BRD-017
    public async Task TrocarLevantamento_PassoQueSaiuVoltaNaListaESuaPosicaoEDescartada()
    {
        var oldSurvey = await SurveyAsync();
        var newSurvey = await SurveyAsync();
        // Um nó que só é visitado vira passo-chave no levantamento antigo (tipo confirmado) e não existe como passo no novo.
        var detail = (await Client.GetFromJsonAsync<SurveyDetail>($"/api/surveys/{oldSurvey}", Json))!;
        var flow = (await Client.GetFromJsonAsync<BusinessFlow>($"/api/surveys/{oldSurvey}/business", Json))!;
        var extra = detail.Graph!.Nodes.First(n => flow.Steps.All(s => s.NodeId != n.Id));
        var kind = await Client.PutAsJsonAsync($"/api/surveys/{oldSurvey}/business/steps/{Uri.EscapeDataString(extra.Id)}/kind", new { kind = (int)BusinessKind.Step }, Json);
        Assert.Equal(HttpStatusCode.OK, kind.StatusCode);
        var withExtra = (await Client.GetFromJsonAsync<BusinessFlow>($"/api/surveys/{oldSurvey}/business", Json))!;
        var gone = withExtra.Steps.Single(s => s.NodeId == extra.Id);
        var kept = withExtra.Steps.First(s => s.NodeId != extra.Id);
        var board = await CreateBoardAsync("Q", oldSurvey);
        await PutLayoutAsync(board.Id, Layout([new(oldSurvey, gone.Id, 1, 1), new(oldSurvey, kept.Id, 2, 2)], [new(oldSurvey, kept.Id, gone.Id, [])]));

        var response = await Client.PutAsJsonAsync($"/api/boards/{board.Id}/lanes/{oldSurvey}/replace", new { newSurveyId = newSurvey }, Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<ReplaceBoardLaneResponse>(Json))!;
        var removed = Assert.Single(result.RemovedSteps);
        Assert.Equal(gone.Id, removed.StepId);
        Assert.Equal(extra.Id, removed.NodeId);
        Assert.Equal(gone.Label, removed.Label);
        var layout = result.Board.Layout;
        Assert.Single(layout.Steps);
        Assert.Equal(2, layout.Steps[0].X);
        Assert.Empty(layout.Edges);
    }

    [Fact] // API-BRD-018
    public async Task TrocarLevantamento_ForaDasRegras_RecusaComCodigoENadaMuda()
    {
        var a = await SurveyAsync();
        var b = await SurveyAsync();
        var outside = await SurveyAsync();
        var board = await CreateBoardAsync("Q", a, b);
        string Url(Guid lane) => $"/api/boards/{board.Id}/lanes/{lane}/replace";

        await AssertCodeAsync(await Client.PutAsJsonAsync(Url(outside), new { newSurveyId = a }, Json), HttpStatusCode.NotFound, ErrorCodes.BoardLaneNotFound);
        await AssertCodeAsync(await Client.PutAsJsonAsync(Url(a), new { newSurveyId = b }, Json), HttpStatusCode.Conflict, ErrorCodes.BoardLaneExists);
        await AssertCodeAsync(await Client.PutAsJsonAsync(Url(a), new { newSurveyId = a }, Json), HttpStatusCode.Conflict, ErrorCodes.BoardLaneExists);
        await AssertCodeAsync(await Client.PutAsJsonAsync(Url(a), new { newSurveyId = Guid.NewGuid() }, Json), HttpStatusCode.NotFound, ErrorCodes.BoardSurveyNotFound);
        await AssertCodeAsync(await Client.PutAsJsonAsync(Url(a), new { newSurveyId = await PendingSurveyAsync() }, Json), HttpStatusCode.Conflict, ErrorCodes.BoardSurveyNotReady);

        Assert.Equal([a, b], (await GetBoardAsync(board.Id)).Lanes.Select(l => l.SurveyId));
    }

    [Fact] // MCP-BRD-001
    public async Task ListBoards_ListaQuadrosComAsFaixas()
    {
        var a = await SurveyAsync();
        var b = await SurveyAsync();
        var board = await CreateBoardAsync("Quadro do agente", a, b);
        await using var provider = fixture.McpServices();
        await using var scope = provider.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<RastroTools>();

        var text = await tools.ListBoards();

        Assert.Contains($"- {board.Id} \"Quadro do agente\": 2 faixas", text);
        Assert.Contains($"    1. \"AlertJob.ExecuteAsync\" levantamento {a}: AlertJob.ExecuteAsync ({ApiFixture.RepositoryName})", text);
        Assert.Contains($"    2. \"AlertJob.ExecuteAsync\" levantamento {b}", text);
    }

    [Fact] // MCP-BRD-002
    public async Task GetBusinessView_ComBoardId_MostraGlossarioEAVisaoDeCadaFaixa()
    {
        var a = await SurveyAsync();
        var b = await SurveyAsync();
        var board = await CreateBoardAsync("Cancelamentos", a, b);
        await PutBoardAsync(board.Id, "Cancelamentos", "Alerta: aviso ao cliente.", [new { surveyId = a, order = 0, title = "Disparo" }, new { surveyId = b, order = 1 }]);
        await using var provider = fixture.McpServices();
        await using var scope = provider.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<RastroTools>();

        var text = await tools.GetBusinessView(boardId: board.Id);

        Assert.Contains("Quadro \"Cancelamentos\": 2 faixas", text);
        Assert.Contains("Glossário", text);
        Assert.Contains("  Alerta: aviso ao cliente.", text);
        Assert.Contains($"Faixa 1 \"Disparo\" (levantamento {a}, entrada AlertJob.ExecuteAsync):", text);
        Assert.Contains($"Faixa 2 \"AlertJob.ExecuteAsync\" (levantamento {b}", text);
        Assert.Equal(2, text.Split("b1 Start").Length - 1);
        Assert.Contains("Ligações:", text);
    }

    [Fact] // MCP-BRD-003
    public async Task GetBusinessView_SemNenhumOuComOsDoisIdsOuQuadroAusente_DizOQueFazer()
    {
        var survey = await SurveyAsync();
        await using var provider = fixture.McpServices();
        await using var scope = provider.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<RastroTools>();

        Assert.Contains("surveyId", await tools.GetBusinessView());
        Assert.Contains("boardId", await tools.GetBusinessView(survey, Guid.NewGuid()));
        Assert.Equal("Quadro não encontrado. Use list_boards.", await tools.GetBusinessView(boardId: Guid.NewGuid()));
        Assert.Contains("b1 Start", await tools.GetBusinessView(survey));
    }

    [Fact] // MCP-BRD-004
    public void Instrucoes_CitamQuadrosEAsDuasFerramentas()
    {
        Assert.Contains("list_boards", McpInstructions.Text);
        Assert.Contains("boardId", McpInstructions.Text);
    }
}

// Cenário API-BRD-019: a migration do quadro guarda o que já havia (posição da visão de negócio da Fase 58).
public class BoardMigrationTests
{
    [Fact] // API-BRD-019
    public async Task Migration_PosicaoDaFase58_FicaComFaixaVaziaNoEscopoSurvey()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rastro-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var options = new DbContextOptionsBuilder<RastroDbContext>();
            RastroDbContext.Configure(options, $"Data Source={Path.Combine(folder, "rastro.db")};Pooling=False");
            await using (var db = new RastroDbContext(options.Options))
            {
                await db.GetService<IMigrator>().MigrateAsync("20261126130645_VisaoDeNegocioTexto");
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO business_layouts (scope, scope_id, step_id, x, y, updated_at) VALUES (0, '11111111-1111-1111-1111-111111111111', 'b1', 12.5, -3, 1)");
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO business_edge_layouts (scope, scope_id, from_step, to_step, points_json, updated_at) VALUES (0, '11111111-1111-1111-1111-111111111111', 'b1', 'b2', '[{{\"x\":1,\"y\":2}}]', 1)");
            }

            await using (var db = new RastroDbContext(options.Options))
            {
                await db.Database.MigrateAsync();
                // Sem ler updated_at: o valor de teste (1) não é uma data ordenável válida.
                var step = await db.BusinessStepLayouts.Select(s => new { s.Scope, s.LaneSurveyId, s.StepId, s.X, s.Y }).SingleAsync();
                Assert.Equal((LayoutScope.Survey, Guid.Empty, "b1", 12.5, -3d), (step.Scope, step.LaneSurveyId, step.StepId, step.X, step.Y));
                var edge = await db.BusinessEdgeLayouts.Select(e => new { e.LaneSurveyId, e.FromStep, e.ToStep }).SingleAsync();
                Assert.Equal((Guid.Empty, "b1", "b2"), (edge.LaneSurveyId, edge.FromStep, edge.ToStep));
                Assert.Empty(await db.Boards.ToListAsync());
            }
        }
        finally
        {
            try { Directory.Delete(folder, true); }
            catch (IOException) { /* arquivo temporário do teste: sobra de uma pasta temporária, não afeta o resultado. */ }
        }
    }
}
