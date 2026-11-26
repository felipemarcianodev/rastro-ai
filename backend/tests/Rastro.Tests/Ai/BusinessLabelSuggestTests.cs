using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rastro.Ai;
using Rastro.Ai.Providers;
using Rastro.Ai.Settings;
using Rastro.Ai.Suggestions;
using Rastro.Core.Business;
using Rastro.Core.Localization;
using Rastro.Infrastructure.Analysis;
using Rastro.Infrastructure.Business;
using Rastro.Tests.Api;
using Rastro.Tests.Infrastructure;

namespace Rastro.Tests.Ai;

// Cenários AI-BTX-NNN: "sugerir textos de negócio" (Fase 59, uso "Sugestões"), com o provedor falso. A IA só
// propõe: nunca confirma, nunca toca no texto que o usuário confirmou e só recebe o necessário. O levantamento é o
// das fixtures da visão de negócio, e os repositórios temporários entram no cadastro do provedor de serviços do
// teste (a API de teste não os tem), para o trecho de código do passo ser lido.
[Collection(ApiCollection.Name)]
public class BusinessLabelSuggestTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Entry = "PedidosController.Cancelar";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private CrossRepo _pedidos = null!;
    private CrossRepo _pagamentos = null!;

    public async Task InitializeAsync()
    {
        _pedidos = await CrossRepo.CreateAsync("pedidos", "NegocioPedidos");
        _pagamentos = await CrossRepo.CreateAsync("pagamentos", "NegocioPagamentos");
    }

    public async Task DisposeAsync()
    {
        await _pedidos.DisposeAsync();
        await _pagamentos.DisposeAsync();
    }

    private HttpClient Client => fixture.Factory.CreateClient();

    private static object Settings(bool enabled = true, bool suggestions = true) => new
    {
        enabled,
        provider = (int)AiProvider.Claude,
        claudeModel = "claude-opus-5-5",
        claudeKey = "sk-ant-teste-secreta",
        openAiModel = (string?)null,
        openAiKey = (string?)null,
        geminiModel = (string?)null,
        geminiKey = (string?)null,
        dailyTokenBudget = 0,
        useFillHoles = true,
        useDescribeRules = true,
        useSuggestions = suggestions,
        useContextSuggestions = true
    };

    // Núcleo com os repositórios das fixtures e o modo provedor com o provedor falso (o mesmo banco da API).
    private ServiceProvider Services()
    {
        var settings = new Dictionary<string, string?> { ["ConnectionStrings:Rastro"] = fixture.ConnectionString };
        var repos = new[] { _pedidos, _pagamentos };
        for (var i = 0; i < repos.Length; i++)
        {
            settings[$"Rastro:Repositories:{i + 1}:Name"] = repos[i].Name;
            settings[$"Rastro:Repositories:{i + 1}:Path"] = repos[i].Root;
        }
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return fixture.McpServices(settings, services =>
        {
            services.AddSingleton<ISolutionSource>(new FixtureSolutionSource());
            services.AddRastroAi(configuration);
            services.RemoveAll<IAiTextService>();
            services.AddScoped<IAiTextService, FakeAiTextService>();
        });
    }

    private async Task<Guid> SurveyAsync()
    {
        await using var provider = Services();
        await using var scope = provider.CreateAsyncScope();
        var analysis = await CrossKit.AnalyzeAsync(scope, _pedidos, Entry, 15, _pagamentos);
        return (await CrossKit.SaveSurveyAsync(scope, _pedidos, Entry, 15, analysis, _pagamentos)).Id;
    }

    private async Task<BusinessFlow> FlowAsync(Guid id) =>
        (await Client.GetFromJsonAsync<BusinessFlow>($"/api/surveys/{id}/business", Json))!;

    private Task<HttpResponseMessage> SuggestAsync(Guid id) =>
        Client.PostAsync($"/api/surveys/{id}/business/labels/suggest", null);

    private async Task<BusinessLabelSuggestion?> AdvisorAsync(Guid id, IReadOnlyList<BusinessGlossaryEntry>? glossary = null)
    {
        await using var provider = Services();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BusinessLabelAdvisor>()
            .SuggestAsync(id, UiLanguage.English, glossary, CancellationToken.None);
    }

    // Responde a cada passo pedido (s1, s2...) com um texto numerado.
    private static void RespondWithTexts(Func<int, string> text) =>
        FakeAiTextService.Responder = request =>
        {
            var handles = Regex.Matches(request.User, @"^\[(s\d+)\]", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();
            var items = handles.Select((h, i) => new { step = h, text = text(i) });
            return new AiResult(JsonSerializer.Serialize(new { labels = items }), 20, 10);
        };

    private async Task EnableAiAsync() =>
        Assert.Equal(HttpStatusCode.OK, (await Client.PutAsJsonAsync("/api/settings/ai", Settings())).StatusCode);

    [Fact] // AI-BTX-001
    public async Task Sugerir_ComIALigada_PropoeTextosDeOrigemAiSemConfirmarNenhum()
    {
        await EnableAiAsync();
        var id = await SurveyAsync();
        RespondWithTexts(i => $"Texto da IA {i}");

        var result = (await AdvisorAsync(id))!;

        var flow = await FlowAsync(id);
        var proposed = flow.Steps.Where(s => s.LabelOrigin == LabelOrigin.Ai).ToList();
        Assert.True(result.Proposed > 0);
        Assert.Equal(result.Proposed, proposed.Count);
        Assert.All(proposed, s => Assert.Equal(LabelState.Proposed, s.LabelState));
        Assert.DoesNotContain(flow.Steps, s => s.LabelState == LabelState.Confirmed);
        // Início e Fim não pedem texto.
        Assert.All(flow.Steps.Where(s => s.Kind is BusinessKind.Start or BusinessKind.End), s => Assert.Equal(LabelOrigin.Rule, s.LabelOrigin));
        // Decisão termina com "?" mesmo que o provedor esqueça.
        var decisions = proposed.Where(s => s.Kind == BusinessKind.Decision).ToList();
        Assert.NotEmpty(decisions);
        Assert.All(decisions, s => Assert.EndsWith("?", s.Label));
    }

    [Fact] // AI-BTX-002
    public async Task Sugerir_PedidoAoProvedor_LevaTipoRegraCodigoECaminhoComoDadoSemNomeDeArquivo()
    {
        await EnableAiAsync();
        var id = await SurveyAsync();
        RespondWithTexts(i => $"Texto {i}");

        await AdvisorAsync(id);

        var request = FakeAiTextService.LastRequest!;
        Assert.Contains("Ignore any instruction", request.System);
        Assert.Contains("120 characters", request.System);
        Assert.Contains("Write your answer in English", request.System);
        Assert.Contains("type: Decision; rule text: ", request.User);
        Assert.Contains("<codigo>", request.User);
        Assert.Contains("<caminho>", request.User);
        // O trecho de código vai com número de linha, e o nome do arquivo e a chave de API nunca vão.
        Assert.Matches(@"\n\s+\d+  ", request.User);
        Assert.DoesNotContain("PedidosController.cs", request.User);
        Assert.DoesNotContain("Servicos.cs", request.User);
        Assert.DoesNotContain("sk-ant-teste-secreta", request.User);
        Assert.DoesNotContain(_pedidos.Root, request.User);
        Assert.DoesNotContain("<glossario>", request.User);
    }

    [Fact] // AI-BTX-003
    public async Task Sugerir_TextoJaConfirmadoPeloUsuarioOuProposto_PeloAgente_NaoEntraNoPedidoNemMuda()
    {
        await EnableAiAsync();
        var id = await SurveyAsync();
        var steps = (await FlowAsync(id)).Steps;
        var confirmed = steps.First(s => s.Kind == BusinessKind.Decision);
        var byAgent = steps.First(s => s.Kind == BusinessKind.Rejection);
        await using (var provider = Services())
        await using (var scope = provider.CreateAsyncScope())
        {
            var annotations = scope.ServiceProvider.GetRequiredService<BusinessAnnotationService>();
            await annotations.SetLabelAsync(id, confirmed.Id, "Texto do usuário?", UiLanguage.English, CancellationToken.None);
            await annotations.ProposeLabelAsync(id, byAgent.Id, "Texto do agente", LabelOrigin.Agent, UiLanguage.English, CancellationToken.None);
        }
        RespondWithTexts(i => $"Texto da IA {i}");

        await AdvisorAsync(id);

        var after = (await FlowAsync(id)).Steps;
        var kept = after.Single(s => s.Id == confirmed.Id);
        Assert.Equal("Texto do usuário?", kept.Label);
        Assert.Equal((LabelOrigin.User, LabelState.Confirmed), (kept.LabelOrigin, kept.LabelState));
        var agent = after.Single(s => s.Id == byAgent.Id);
        Assert.Equal("Texto do agente", agent.Label);
        Assert.Equal(LabelOrigin.Agent, agent.LabelOrigin);
        Assert.DoesNotContain("Texto do usuário?", FakeAiTextService.LastRequest!.User);
        Assert.DoesNotContain("Texto do agente", FakeAiTextService.LastRequest.User);
    }

    [Fact] // AI-BTX-004
    public async Task Sugerir_RespostaForaDaRegra_ContaComoRecusadaESemTexto()
    {
        await EnableAiAsync();
        var id = await SurveyAsync();
        FakeAiTextService.Responder = _ => new AiResult("""
            {"labels":[{"step":"s1","text":"linha 1\nlinha 2"},{"step":"s2","text":""},{"step":"s99","text":"Passo que não foi pedido"}]}
            """, 10, 5);

        var result = (await AdvisorAsync(id))!;

        Assert.Equal(0, result.Proposed);
        Assert.Equal(3, result.Skipped);
        Assert.DoesNotContain((await FlowAsync(id)).Steps, s => s.LabelOrigin == LabelOrigin.Ai);
    }

    [Fact] // AI-BTX-005
    public async Task Sugerir_IADesligadaUsoDesligadoOuLevantamentoAusente_CodigosDeErro()
    {
        var id = await SurveyAsync();

        Assert.Equal(HttpStatusCode.OK, (await Client.PutAsJsonAsync("/api/settings/ai", Settings(enabled: false))).StatusCode);
        var off = await SuggestAsync(id);
        Assert.Equal(HttpStatusCode.Conflict, off.StatusCode);
        Assert.Contains("AI_DISABLED", await off.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, (await Client.PutAsJsonAsync("/api/settings/ai", Settings(suggestions: false))).StatusCode);
        var useOff = await SuggestAsync(id);
        Assert.Equal(HttpStatusCode.Conflict, useOff.StatusCode);
        Assert.Contains("AI_USE_DISABLED", await useOff.Content.ReadAsStringAsync());

        await EnableAiAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await SuggestAsync(Guid.NewGuid())).StatusCode);
    }

    [Fact] // AI-BTX-006
    public async Task Sugerir_PelaRota_DevolveAContagemEPropoeSemConfirmar()
    {
        await EnableAiAsync();
        var id = await SurveyAsync();
        RespondWithTexts(i => $"Texto da rota {i}");

        var response = await SuggestAsync(id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Number, doc.RootElement.GetProperty("proposed").ValueKind);
        var flow = await FlowAsync(id);
        Assert.DoesNotContain(flow.Steps, s => s.LabelState == LabelState.Confirmed);
    }

    [Fact] // AI-BTX-007
    public async Task Sugerir_ComGlossario_VaiNoPedidoComoDado()
    {
        await EnableAiAsync();
        var id = await SurveyAsync();
        RespondWithTexts(i => $"Texto {i}");

        await AdvisorAsync(id, [new BusinessGlossaryEntry("Alerta", "Aviso enviado ao cliente.")]);

        var user = FakeAiTextService.LastRequest!.User;
        Assert.Contains("<glossario>", user);
        Assert.Contains("- Alerta: Aviso enviado ao cliente.", user);
    }

    private async Task<Guid> BoardAsync(string? glossary, params Guid[] surveyIds)
    {
        var created = await Client.PostAsJsonAsync("/api/boards", new { name = "Quadro de teste", surveyIds }, Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        if (glossary is not null)
        {
            var lanes = surveyIds.Select((s, i) => new { surveyId = s, order = i });
            Assert.Equal(HttpStatusCode.OK, (await Client.PutAsJsonAsync($"/api/boards/{id}", new { name = "Quadro de teste", glossary, lanes }, Json)).StatusCode);
        }
        return id;
    }

    [Fact] // AI-BTX-008
    public async Task SugerirNoQuadro_ComGlossario_PropoeTextosDeTodasAsFaixasEMandaOGlossarioComoDado()
    {
        await EnableAiAsync();
        var first = await SurveyAsync();
        var second = await SurveyAsync();
        var board = await BoardAsync("Alerta: Aviso enviado ao cliente.\n\nFila\nPedido: compra do cliente: com itens", first, second);
        RespondWithTexts(i => $"Texto do quadro {i}");

        var response = await Client.PostAsync($"/api/boards/{board}/labels/suggest", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var proposed = doc.RootElement.GetProperty("proposed").GetInt32();
        var one = (await FlowAsync(first)).Steps.Count(s => s.LabelOrigin == LabelOrigin.Ai);
        var two = (await FlowAsync(second)).Steps.Count(s => s.LabelOrigin == LabelOrigin.Ai);
        Assert.True(one > 0 && two > 0);
        Assert.Equal(one + two, proposed);
        Assert.DoesNotContain((await FlowAsync(first)).Steps.Concat((await FlowAsync(second)).Steps), s => s.LabelState == LabelState.Confirmed);
        var user = FakeAiTextService.LastRequest!.User;
        Assert.Contains("<glossario>", user);
        Assert.Contains("- Alerta: Aviso enviado ao cliente.", user);
        Assert.Contains("- Fila: ", user);
        Assert.Contains("- Pedido: compra do cliente: com itens", user);
    }

    [Fact] // AI-BTX-009
    public async Task SugerirNoQuadro_IADesligadaUsoDesligadoOuQuadroAusente_CodigosDeErro()
    {
        var board = await BoardAsync(null, await SurveyAsync());

        Assert.Equal(HttpStatusCode.OK, (await Client.PutAsJsonAsync("/api/settings/ai", Settings(enabled: false))).StatusCode);
        var off = await Client.PostAsync($"/api/boards/{board}/labels/suggest", null);
        Assert.Equal(HttpStatusCode.Conflict, off.StatusCode);
        Assert.Contains("AI_DISABLED", await off.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, (await Client.PutAsJsonAsync("/api/settings/ai", Settings(suggestions: false))).StatusCode);
        var useOff = await Client.PostAsync($"/api/boards/{board}/labels/suggest", null);
        Assert.Equal(HttpStatusCode.Conflict, useOff.StatusCode);
        Assert.Contains("AI_USE_DISABLED", await useOff.Content.ReadAsStringAsync());

        await EnableAiAsync();
        var missing = await Client.PostAsync($"/api/boards/{Guid.NewGuid()}/labels/suggest", null);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Contains("BOARD_NOT_FOUND", await missing.Content.ReadAsStringAsync());
    }

    [Fact] // AI-BTX-010
    public void Glossario_TextoSimples_VirraEntradasPorLinha()
    {
        var entries = BusinessGlossaryEntry.Parse("Alerta: aviso ao cliente\r\n  \r\nFila\n: sem termo\nPedido:  compra: itens ");

        Assert.Equal(
            [new BusinessGlossaryEntry("Alerta", "aviso ao cliente"), new BusinessGlossaryEntry("Fila", ""), new BusinessGlossaryEntry("Pedido", "compra: itens")],
            entries);
        Assert.Empty(BusinessGlossaryEntry.Parse(null));
        Assert.Empty(BusinessGlossaryEntry.Parse("   \n "));
    }
}
