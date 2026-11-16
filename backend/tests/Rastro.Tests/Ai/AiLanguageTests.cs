using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rastro.Ai;
using Rastro.Ai.Providers;
using Rastro.Ai.Settings;
using Rastro.Api.Controllers;
using Rastro.Core.Graph;
using Rastro.Core.Localization;
using Rastro.Mcp;
using Rastro.Tests.Api;

namespace Rastro.Tests.Ai;

// Cenários AI-LANG-NNN: o idioma do pedido no modo provedor (ADR-009). O prompt vai em inglês fixo com a ordem do
// idioma da resposta; as mensagens que voltam para a tela saem no idioma; o cache da regra respeita o idioma.
[Collection(ApiCollection.Name)]
public partial class AiLanguageTests(ApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static TheoryData<string, string, string, string> Languages => new()
    {
        { UiLanguages.EnglishCode, "Write your answer in English.", "refused", "The AI found no target in the code that it could prove." },
        { UiLanguages.PortugueseCode, "Write your answer in Portuguese (Brazil).", "recusada", "A IA não achou no código um destino que pudesse provar." },
        { UiLanguages.SpanishCode, "Write your answer in Spanish.", "rechazada", "La IA no encontró en el código un destino que pudiera probar." }
    };

    private HttpClient ClientIn(string code)
    {
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.Clear();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(code);
        return client;
    }

    private async Task EnableAsync()
    {
        var response = await fixture.Factory.CreateClient().PutAsJsonAsync("/api/settings/ai", new
        {
            enabled = true, provider = (int)AiProvider.Claude, claudeModel = "claude-opus-5-5", claudeKey = "sk-ant-teste",
            openAiModel = (string?)null, openAiKey = (string?)null, geminiModel = (string?)null, geminiKey = (string?)null,
            dailyTokenBudget = 0, useFillHoles = true, useDescribeRules = true, useSuggestions = true, useContextSuggestions = true
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<SurveyDetail> SurveyAsync()
    {
        await using var provider = fixture.McpServices();
        await using var scope = provider.CreateAsyncScope();
        var analyzed = await scope.ServiceProvider.GetRequiredService<RastroTools>()
            .AnalyzeFlow(ApiFixture.RepositoryName, ApiFixture.SolutionFile, "AlertJob.ExecuteAsync");
        var id = Guid.Parse(analyzed.Split(' ')[1]);
        return (await fixture.Factory.CreateClient().GetFromJsonAsync<SurveyDetail>($"/api/surveys/{id}", Json))!;
    }

    private static AiResult RulesFor(AiRequest request, string prefix)
    {
        var handles = Handle().Matches(request.User).Select(m => m.Groups[1].Value).Distinct().ToList();
        return new AiResult(JsonSerializer.Serialize(new { rules = handles.Select(h => new { node = h, rule = $"{prefix} {h}." }) }), 10, 5);
    }

    [Theory] // AI-LANG-001
    [MemberData(nameof(Languages))]
    public async Task CompletarBuraco_PromptEmInglesComAOrdemDoIdiomaEMensagensNoIdioma(string code, string order, string refused, string noProof)
    {
        await EnableAsync();
        var detail = await SurveyAsync();
        var hole = detail.Graph!.Nodes.Single(n => n.Label == "notifier.Notify").Id;
        FakeAiTextService.Responder = _ => new AiResult("""
            {"proposals":[{"target":"PushNotifier.Notify","evidenceFile":"Alerts/Push.cs","evidenceLine":3,"when":null,"reason":"inventado"}]}
            """, 10, 5);

        var body = (await (await ClientIn(code).PostAsJsonAsync($"/api/surveys/{detail.Survey.Id}/ai/fill-hole", new { nodeId = hole }))
            .Content.ReadFromJsonAsync<FillHoleResponse>(Json))!;

        var system = FakeAiTextService.LastRequest!.System;
        Assert.Contains(order, system);
        Assert.Contains("You complete a C# code flow graph", system);
        Assert.DoesNotContain("Você completa", system);
        Assert.Contains("Unresolved call:", FakeAiTextService.LastRequest.User);
        Assert.Contains(body.Result.Messages, m => m.StartsWith($"PushNotifier.Notify: {refused}.", StringComparison.Ordinal));

        FakeAiTextService.Responder = _ => new AiResult("""{"proposals":[]}""", 10, 5);
        var empty = (await (await ClientIn(code).PostAsJsonAsync($"/api/surveys/{detail.Survey.Id}/ai/fill-hole", new { nodeId = hole }))
            .Content.ReadFromJsonAsync<FillHoleResponse>(Json))!;
        Assert.Contains(noProof, empty.Result.Messages);
    }

    [Theory] // AI-LANG-002
    [MemberData(nameof(Languages))]
    public async Task PerguntaEPessoas_PromptEmInglesComAOrdemDoIdioma(string code, string order, string refused, string noProof)
    {
        _ = refused;
        _ = noProof;
        await EnableAsync();
        var detail = await SurveyAsync();
        var real = detail.Graph!.EntryNodeId;
        FakeAiTextService.Responder = _ => new AiResult($$"""{"answer":"ok","seal":"inferred","sources":["{{real}}"],"gap":""}""", 10, 5);

        var ask = await ClientIn(code).PostAsJsonAsync($"/api/surveys/{detail.Survey.Id}/ai/ask", new { question = "Who receives the alert?" });

        Assert.Equal(HttpStatusCode.OK, ask.StatusCode);
        Assert.Contains(order, FakeAiTextService.LastRequest!.System);
        Assert.Contains("You answer questions about a code flow", FakeAiTextService.LastRequest.System);
        Assert.Contains("Entry system:", FakeAiTextService.LastRequest.User);

        FakeAiTextService.Responder = _ => new AiResult("""{"people":[]}""", 10, 5);
        var people = await ClientIn(code).PostAsync($"/api/surveys/{detail.Survey.Id}/c4/ai/people", null);

        Assert.Equal(HttpStatusCode.OK, people.StatusCode);
        Assert.Contains(order, FakeAiTextService.LastRequest.System);
        Assert.Contains("C1 (context) diagram", FakeAiTextService.LastRequest.System);
    }

    [Fact] // AI-LANG-003
    public async Task Descrever_PromptNoIdiomaDoPedido_ECacheNaoMisturaIdiomas()
    {
        await EnableAsync();
        var first = await SurveyAsync();
        var entry = first.Graph!.EntryNodeId;
        var calls = 0;

        // Espanhol: o prompt pede a resposta em espanhol e a regra nasce em espanhol.
        // A regra precisa de ao menos 10 caracteres (ProposalService.MinRuleLength).
        FakeAiTextService.Responder = request => { calls++; return RulesFor(request, "Regla de prueba"); };
        var es = await Describe(ClientIn(UiLanguages.SpanishCode), first.Survey.Id, entry);
        Assert.Contains("Write your answer in Spanish.", FakeAiTextService.LastRequest!.System);
        Assert.Contains("You write, for whoever reads the flow", FakeAiTextService.LastRequest.System);
        Assert.Equal(1, es.Result.Written);
        Assert.Equal("1 regla(s) escrita(s) por la IA, 0 reutilizada(s) de la caché.", es.Result.Messages[0]);

        // Inglês no mesmo código: não devolve a regra em espanhol, pergunta de novo ao provedor.
        var callsBefore = calls;
        FakeAiTextService.Responder = request => { calls++; return RulesFor(request, "Rule for testing"); };
        var second = await SurveyAsync();
        var en = await Describe(ClientIn(UiLanguages.EnglishCode), second.Survey.Id, entry);
        Assert.Equal(callsBefore + 1, calls);
        Assert.Equal(0, en.Result.FromCache);
        Assert.Contains("Write your answer in English.", FakeAiTextService.LastRequest.System);
        Assert.StartsWith("Rule ", en.Survey.Graph!.Nodes.Single(n => n.Id == entry).Rule!.Text);
        Assert.Equal("1 rule(s) written by the AI, 0 reused from the cache.", en.Result.Messages[0]);

        // Espanhol de novo, em outro levantamento: vem do cache, na língua em que nasceu.
        callsBefore = calls;
        var third = await SurveyAsync();
        var again = await Describe(ClientIn(UiLanguages.SpanishCode), third.Survey.Id, entry);
        Assert.Equal(callsBefore, calls);
        Assert.Equal(1, again.Result.FromCache);
        Assert.StartsWith("Regla ", again.Survey.Graph!.Nodes.Single(n => n.Id == entry).Rule!.Text);
        Assert.Equal("0 regla(s) escrita(s) por la IA, 1 reutilizada(s) de la caché.", again.Result.Messages[0]);
    }

    [Theory] // AI-LANG-004
    [InlineData(UiLanguages.EnglishCode, "No step without a rule to write.")]
    [InlineData(UiLanguages.PortugueseCode, "Nenhum passo sem regra para escrever.")]
    [InlineData(UiLanguages.SpanishCode, "Ningún paso sin regla para escribir.")]
    public async Task Descrever_NadaParaEscrever_MensagemNoIdioma(string code, string expected)
    {
        await EnableAsync();
        var detail = await SurveyAsync();
        FakeAiTextService.Responder = request => RulesFor(request, "Qualquer");
        await Describe(ClientIn("pt-BR"), detail.Survey.Id, null);

        var again = await Describe(ClientIn(code), detail.Survey.Id, null);

        Assert.Equal(expected, again.Result.Messages[0]);
    }

    private static async Task<DescribeResponse> Describe(HttpClient client, Guid id, string? entry)
    {
        var response = await client.PostAsJsonAsync($"/api/surveys/{id}/ai/describe",
            new { nodeIds = entry is null ? null : new[] { entry }, overwrite = entry is not null });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DescribeResponse>(Json))!;
    }

    [GeneratedRegex(@"\[(r\d+)\]")]
    private static partial Regex Handle();
}
