using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rastro.Api.Controllers;
using Rastro.Core;
using Rastro.Core.Graph;
using Rastro.Core.Surveys;
using Rastro.Infrastructure;
using Rastro.Tests.Infrastructure;

namespace Rastro.Tests.Api;

// Cenários API-CROSS-NNN: levantamento que atravessa sistemas (Fase 25) pela API: lista de costura,
// trechos no resumo, "desatualizado" por trecho, código-fonte por sistema, edição travada no primeiro
// sistema e regra cruzada entre levantamentos com trecho em comum.
[Collection(ApiCollection.Name)]
public class CrossSurveyEndpointTests(ApiFixture fixture) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private CrossRepo _pedidos = null!;
    private CrossRepo _pagamentos = null!;

    private HttpClient Client => fixture.Factory.CreateClient();

    public async Task InitializeAsync()
    {
        _pedidos = await CrossRepo.CreateAsync("pedidos", "ApiPedidos");
        _pagamentos = await CrossRepo.CreateAsync("pagamentos", "ApiPagamentos");
        foreach (var repo in new[] { _pedidos, _pagamentos })
        {
            var added = await Client.PostAsJsonAsync("/api/repositories", new { name = repo.Name, path = repo.Root });
            Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        }
    }

    public async Task DisposeAsync()
    {
        foreach (var repo in new[] { _pedidos, _pagamentos })
        {
            await Client.DeleteAsync($"/api/repositories/{repo.Name}");
            await repo.DisposeAsync();
        }
    }

    private async Task<HttpResponseMessage> PostSurveyAsync(IReadOnlyList<string>? cross, string? repository = null) =>
        await Client.PostAsJsonAsync("/api/surveys", new
        {
            repository = repository ?? _pedidos.Name,
            solutionPath = _pedidos.Solution,
            entryPoint = "PedidosController.Cancelar",
            crossRepositories = cross
        });

    private async Task<SurveyDetail> CrossSurveyAsync()
    {
        var response = await PostSurveyAsync([_pagamentos.Name]);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<SurveySummary>(Json))!;
        for (var i = 0; i < 300; i++)
        {
            var detail = await Client.GetFromJsonAsync<SurveyDetail>($"/api/surveys/{created.Id}", Json);
            if (detail!.Survey.Status is SurveyStatus.Completed or SurveyStatus.Failed)
            {
                Assert.Equal(SurveyStatus.Completed, detail.Survey.Status);
                return detail;
            }
            await Task.Delay(200);
        }
        throw new TimeoutException($"Levantamento {created.Id} não terminou.");
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, body.GetProperty("code").GetString());
    }

    [Fact] // API-CROSS-001
    public async Task Criar_CosturaComRepositorioNaoCadastrado_Retorna400()
    {
        await AssertErrorAsync(await PostSurveyAsync([_pagamentos.Name, "nao-cadastrado"]), ErrorCodes.RepositoryNotAllowed);
    }

    [Fact] // API-CROSS-002
    public async Task Criar_CosturaComMaisDeDezRepositorios_Retorna400()
    {
        var many = Enumerable.Range(1, 11).Select(i => $"repo-{i}").ToList();

        await AssertErrorAsync(await PostSurveyAsync(many), ErrorCodes.CrossRepositoriesInvalid);
    }

    [Fact] // API-CROSS-003
    public async Task Criar_CosturaValida_ResumoTrazARepositoriosETrechosEOGrafoAtravessaOSistema()
    {
        var detail = await CrossSurveyAsync();

        Assert.Equal([_pagamentos.Name], detail.Survey.CrossRepositories);
        var segment = Assert.Single(detail.Survey.Segments);
        Assert.Equal(1, segment.Order);
        Assert.Equal(_pagamentos.Name, segment.Repository);
        Assert.Equal(_pagamentos.Solution, segment.SolutionPath);
        Assert.EndsWith("EstornosController.Criar", segment.EntryPoint);
        Assert.Equal(await _pagamentos.HeadAsync(), segment.CommitHash);
        Assert.Equal(RastroSchema.Version, detail.Survey.SchemaVersion);
        Assert.Contains(detail.Graph!.Nodes, n => n.Label == "EstornosController.Criar" && n.System == _pagamentos.Name);
        Assert.Contains(detail.Graph.Edges, e => e.Http == new HttpCall("POST", "api/estornos"));

        // A lista traz os trechos na mesma resposta (uma consulta só para todos os levantamentos).
        var list = await Client.GetFromJsonAsync<List<SurveySummary>>("/api/surveys", Json);
        Assert.Single(Assert.Single(list!, s => s.Id == detail.Survey.Id).Segments);
    }

    [Fact] // API-CROSS-004
    public async Task Criar_SemCostura_ResumoSaiSemRepositoriosNemTrechos()
    {
        var response = await PostSurveyAsync(null, repository: _pedidos.Name);
        var created = (await response.Content.ReadFromJsonAsync<SurveySummary>(Json))!;

        Assert.Empty(created.CrossRepositories);
        Assert.Empty(created.Segments);
    }

    [Fact] // API-CROSS-005
    public async Task Json_ResumoComCostura_UsaCamelCaseEOrdemDoTrechoComoNumero()
    {
        var detail = await CrossSurveyAsync();

        var raw = await Client.GetStringAsync($"/api/surveys/{detail.Survey.Id}");
        using var doc = JsonDocument.Parse(raw);
        var survey = doc.RootElement.GetProperty("survey");
        Assert.Equal(_pagamentos.Name, survey.GetProperty("crossRepositories")[0].GetString());
        var segment = survey.GetProperty("segments")[0];
        Assert.Equal(1, segment.GetProperty("order").GetInt32());
        Assert.Equal(_pagamentos.Name, segment.GetProperty("repository").GetString());
        var http = doc.RootElement.GetProperty("graph").GetProperty("edges").EnumerateArray()
            .First(e => e.TryGetProperty("http", out _)).GetProperty("http");
        Assert.Equal("POST", http.GetProperty("method").GetString());
        Assert.Equal("api/estornos", http.GetProperty("route").GetString());
    }

    [Fact] // API-CROSS-006
    public async Task Desatualizado_CommitNovoSoNoSegundoSistema_ApontaOTrecho()
    {
        var detail = await CrossSurveyAsync();
        var before = await Client.GetFromJsonAsync<SurveyStaleness>($"/api/surveys/{detail.Survey.Id}/stale", Json);
        Assert.False(before!.Stale);
        Assert.False(Assert.Single(before.Segments).Stale);

        await _pagamentos.NewCommitAsync();

        var after = await Client.GetFromJsonAsync<SurveyStaleness>($"/api/surveys/{detail.Survey.Id}/stale", Json);
        Assert.True(after!.Stale);
        var segment = Assert.Single(after.Segments);
        Assert.Equal(_pagamentos.Name, segment.Repository);
        Assert.True(segment.Stale);
        Assert.Equal(detail.Survey.Segments[0].CommitHash, segment.SurveyCommit);
        Assert.Equal(await _pagamentos.HeadAsync(), segment.CurrentCommit);
        // O primeiro sistema não mudou.
        Assert.Equal(detail.Survey.CommitHash, after.CurrentCommit);
    }

    [Fact] // API-CROSS-007
    public async Task Codigo_ComSystemDoTrecho_LeOArquivoDoOutroRepositorio()
    {
        var detail = await CrossSurveyAsync();
        var criar = detail.Graph!.Nodes.Single(n => n.Label == "EstornosController.Criar");

        var snippet = await Client.GetFromJsonAsync<SourceSnippet>(
            $"/api/surveys/{detail.Survey.Id}/source?file={criar.Location!.File}&line={criar.Location.Line}&context=1&system={_pagamentos.Name}", Json);
        var own = await Client.GetFromJsonAsync<SourceSnippet>(
            $"/api/surveys/{detail.Survey.Id}/source?file=PedidosController.cs&line=10&context=1&system={_pedidos.Name}", Json);

        Assert.Contains(snippet!.Lines, l => l.Contains("Criar"));
        Assert.Contains(own!.Lines, l => l.Contains("Cancelar") || l.Contains("PedidosController") || l.Contains("HttpPost"));
    }

    [Fact] // API-CROSS-008
    public async Task Codigo_ComSystemDeForaDoLevantamento_Retorna400()
    {
        var detail = await CrossSurveyAsync();

        var response = await Client.GetAsync($"/api/surveys/{detail.Survey.Id}/source?file=EstornosController.cs&line=1&system={ApiFixture.RepositoryName}");

        await AssertErrorAsync(response, ErrorCodes.SourcePathInvalid);
        // Sem o system, o arquivo é procurado no repositório do levantamento, que não tem este.
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/surveys/{detail.Survey.Id}/source?file=EstornosController.cs&line=1")).StatusCode);
    }

    [Fact] // API-CROSS-009
    public async Task SalvarRascunho_MudancaEmPassoDeOutroSistema_ERecusada_EmPassoDoPrimeiroNao()
    {
        var detail = await CrossSurveyAsync();
        var graph = detail.Graph!;
        var foreign = graph.Nodes.First(n => n.System is not null && n.Kind == NodeKind.Method);

        var removed = graph with { Nodes = graph.Nodes.Select(n => n.Id == foreign.Id ? n with { Change = ChangeKind.Removed } : n).ToList() };
        var refused = await Client.PutAsJsonAsync($"/api/surveys/{detail.Survey.Id}/draft", removed, Json);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var body = await refused.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ErrorCodes.DraftInvalid, body.GetProperty("code").GetString());
        Assert.Contains(body.GetProperty("errors").EnumerateArray(), e => e.GetString() == "Edição só no primeiro sistema do fluxo.");

        var first = graph.Nodes.First(n => n.System is null && n.Kind == NodeKind.Method && n.Id != graph.EntryNodeId);
        var allowed = graph with { Nodes = graph.Nodes.Select(n => n.Id == first.Id ? n with { Change = ChangeKind.Removed } : n).ToList() };
        Assert.Equal(HttpStatusCode.OK, (await Client.PutAsJsonAsync($"/api/surveys/{detail.Survey.Id}/draft", allowed, Json)).StatusCode);
    }

    [Fact] // API-CROSS-010
    public async Task RegraCruzada_LevantamentosComTrechoEmComum_SaoIrmaos()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var shared = "xr-" + suffix;
        const string save = "App.IPedidoRepository.Update(App.Pedido, System.Threading.CancellationToken)";

        FlowGraph Flow(string entry, bool guard)
        {
            var nodes = new List<FlowNode>
            {
                new("e", NodeKind.Entry, entry),
                new("w", NodeKind.Method, "IPedidoRepository.Update", Symbol: save)
            };
            var edges = new List<FlowEdge> { new("e", "w", 0, EdgeConfidence.Extracted) };
            if (guard)
            {
                nodes.Add(new FlowNode("g", NodeKind.Method, "PedidoValidator.Validar", Symbol: "App.PedidoValidator.Validar(App.Pedido)"));
                edges.Add(new FlowEdge("e", "g", 1, EdgeConfidence.Extracted));
            }
            return new FlowGraph(RastroSchema.Version, entry, "e", nodes, edges);
        }

        Survey Done(string repository, string entry, bool guard)
        {
            var survey = Survey.Create(repository, "x.slnx", entry, 5, null, null, DateTimeOffset.UtcNow);
            survey.Start(DateTimeOffset.UtcNow);
            survey.Complete(Flow(entry, guard), DateTimeOffset.UtcNow);
            return survey;
        }

        var target = Done("xa-" + suffix, "Webhook.Receber", guard: false);
        var siblings = new[] { Done(shared, "Pedido.Criar", true), Done(shared, "Pedido.Editar", true) };
        var stranger = Done("xz-" + suffix, "Pedido.Excluir", true);

        await using (var provider = fixture.McpServices())
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RastroDbContext>();
            db.Surveys.AddRange([target, .. siblings, stranger]);
            db.SurveySegments.Add(SurveySegment.Create(target.Id, 1, shared, "x.slnx", "Ns.Pedido.Salvar", null, null));
            await db.SaveChangesAsync();
        }

        var flows = await Client.GetFromJsonAsync<List<CrossFlowView>>($"/api/surveys/{target.Id}/cross-flow", Json);

        // Sem a Fase 25 os irmãos eram só os do mesmo repositório principal; agora entram os que têm o repositório do trecho.
        var finding = Assert.Single(flows!);
        Assert.Equal("Pedido", finding.Entity);
        Assert.Equal("PedidoValidator.Validar", finding.Guard);
        Assert.Equal(["Pedido.Criar", "Pedido.Editar"], finding.Flows);
    }
}
