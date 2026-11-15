using System.Net.Http.Json;
using System.Text.Json;
using Rastro.Api.Controllers;
using Rastro.Core.Graph;
using Rastro.Core.Localization;
using Rastro.Core.Surveys;

namespace Rastro.Tests.Api;

// Cenários API-LANG-1NN: o worker grava os achados do levantamento no idioma do config.json (Fase 50, ADR-009).
// O texto fica no idioma em que nasceu: trocar o idioma depois não retraduz o que já está gravado.
[Collection(ApiCollection.Name)]
public class SurveyLanguageTests(ApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private record LanguageView(int? Language, int Effective);

    private HttpClient Client => fixture.Factory.CreateClient();

    // Pela rota da tela: ela grava no config.json e recarrega a configuração, e o worker lê o idioma pelo monitor de opções.
    private async Task SetAsync(UiLanguage? language)
    {
        var response = await Client.PutAsJsonAsync("/api/settings/language", new { language = language is { } l ? (int?)l : null }, Json);
        response.EnsureSuccessStatusCode();
    }

    private async Task<(Guid Id, FlowGraph Graph)> SurveyGraphAsync()
    {
        var response = await Client.PostAsJsonAsync("/api/surveys",
            new { repository = ApiFixture.RepositoryName, solutionPath = ApiFixture.SolutionFile, entryPoint = "AlertJob.ExecuteAsync", maxDepth = 1 });
        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<SurveySummary>(Json))!;
        for (var i = 0; i < 150; i++)
        {
            var detail = await Client.GetFromJsonAsync<SurveyDetail>($"/api/surveys/{created.Id}", Json);
            if (detail!.Survey.Status == SurveyStatus.Completed) return (created.Id, detail.Graph!);
            Assert.NotEqual(SurveyStatus.Failed, detail.Survey.Status);
            await Task.Delay(200);
        }
        throw new TimeoutException($"Levantamento {created.Id} não terminou em 30 s.");
    }

    // O passo "limite de profundidade" nasce no idioma do config.json (a fixture Alerts não tem achado de qualidade).
    private static void AssertDepthLabel(FlowGraph graph, string expected) =>
        Assert.Contains(graph.Nodes, n => n.Reason == UnresolvedReason.DepthLimit && n.Label == expected);

    [Fact] // API-LANG-101
    public async Task Levantar_ComIdiomaNoConfig_GravaOsAchadosNesseIdiomaENaoRetraduz()
    {
        try
        {
            await SetAsync(UiLanguage.Spanish);
            var (spanishId, spanish) = await SurveyGraphAsync();
            AssertDepthLabel(spanish, "límite de profundidad");

            await SetAsync(UiLanguage.English);
            var (_, english) = await SurveyGraphAsync();
            AssertDepthLabel(english, "depth limit");

            await SetAsync(UiLanguage.Portuguese);
            var (_, portuguese) = await SurveyGraphAsync();
            AssertDepthLabel(portuguese, "limite de profundidade");

            // O primeiro levantamento continua em espanhol: o texto fica no idioma em que nasceu.
            var again = await Client.GetFromJsonAsync<SurveyDetail>($"/api/surveys/{spanishId}", Json);
            AssertDepthLabel(again!.Graph!, "límite de profundidad");
        }
        finally
        {
            await SetAsync(null);
        }
    }
}
