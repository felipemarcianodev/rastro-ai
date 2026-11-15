using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Rastro.Core.Localization;
using Rastro.Infrastructure.Repositories;
using Rastro.Infrastructure.Storage;

namespace Rastro.Tests.Api;

// Cenários API-LANG-NNN: o idioma do servidor (Fase 49, ADR-009).
[Collection(ApiCollection.Name)]
public class LanguageEndpointTests(ApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private record LanguageView(int? Language, int Effective);

    private HttpClient Client => fixture.Factory.CreateClient();

    // Cliente sem o Accept-Language padrão da fixture: vale o config.json.
    private HttpClient ClientWithoutHeader()
    {
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.Clear();
        return client;
    }

    private static async Task<string> ProblemTitleAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("title").GetString()!;
    }

    private static void ClearSaved() => new RepositoryConfigFile(RastroHome.ConfigPath).SetLanguage(null);

    [Fact] // API-LANG-001
    public async Task Get_SemIdiomaGravado_DevolveNuloEOIdiomaEfetivo()
    {
        ClearSaved();

        var view = await Client.GetFromJsonAsync<LanguageView>("/api/settings/language", Json);

        Assert.Null(view!.Language);
        Assert.Equal((int)UiLanguages.FromSystem(), view.Effective);
    }

    [Fact] // API-LANG-002
    public async Task Put_IdiomaValido_GravaNoConfigJsonEDevolveOGravado()
    {
        try
        {
            var response = await Client.PutAsJsonAsync("/api/settings/language", new { language = (int)UiLanguage.Spanish }, Json);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var view = await response.Content.ReadFromJsonAsync<LanguageView>(Json);
            Assert.Equal((int)UiLanguage.Spanish, view!.Language);
            Assert.Equal((int)UiLanguage.Spanish, view.Effective);
            Assert.Equal(UiLanguage.Spanish, new RepositoryConfigFile(RastroHome.ConfigPath).GetLanguage());
            Assert.Contains("\"Language\": \"es\"", await File.ReadAllTextAsync(RastroHome.ConfigPath));
            var again = await Client.GetFromJsonAsync<LanguageView>("/api/settings/language", Json);
            Assert.Equal((int)UiLanguage.Spanish, again!.Language);
        }
        finally { ClearSaved(); }
    }

    [Fact] // API-LANG-003
    public async Task Put_IdiomaForaDoEnum_Devolve400ComCodigoEstavel()
    {
        var response = await Client.PutAsJsonAsync("/api/settings/language", new { language = 9 }, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("LANGUAGE_INVALID", doc.RootElement.GetProperty("code").GetString());
        Assert.Null(new RepositoryConfigFile(RastroHome.ConfigPath).GetLanguage());
    }

    [Fact] // API-LANG-004
    public async Task Put_Nulo_ApagaAChaveEVoltaASeguirOSistema()
    {
        new RepositoryConfigFile(RastroHome.ConfigPath).SetLanguage(UiLanguages.PortugueseCode);

        var response = await Client.PutAsJsonAsync("/api/settings/language", new { language = (int?)null }, Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var view = await response.Content.ReadFromJsonAsync<LanguageView>(Json);
        Assert.Null(view!.Language);
        var text = await File.ReadAllTextAsync(RastroHome.ConfigPath);
        Assert.DoesNotContain("\"Language\"", text);
        // O repositório cadastrado continua no arquivo.
        Assert.Contains(ApiFixture.RepositoryName, text);
    }

    [Fact] // API-LANG-005
    public async Task AcceptLanguage_ManandoSobreOConfig_TextoVemNoIdiomaDoCabecalho()
    {
        try
        {
            await Client.PutAsJsonAsync("/api/settings/language", new { language = (int)UiLanguage.Spanish }, Json);

            var response = await Client.PutAsJsonAsync("/api/settings/language", new { language = 9 }, Json);

            Assert.StartsWith("Idioma inválido", await ProblemTitleAsync(response));
        }
        finally { ClearSaved(); }
    }

    [Fact] // API-LANG-006
    public async Task SemCabecalho_ValeOIdiomaDoConfigJson()
    {
        try
        {
            await Client.PutAsJsonAsync("/api/settings/language", new { language = (int)UiLanguage.Spanish }, Json);

            var response = await ClientWithoutHeader().PutAsJsonAsync("/api/settings/language", new { language = 9 }, Json);

            Assert.StartsWith("Idioma no válido", await ProblemTitleAsync(response));
        }
        finally { ClearSaved(); }
    }

    [Theory] // API-LANG-007
    [InlineData("en-GB", "Invalid language")]
    [InlineData("es-MX", "Idioma no válido")]
    [InlineData("pt-PT", "Idioma inválido")]
    public async Task AcceptLanguage_ComPrefixo_CasaComOIdiomaDoRastro(string header, string expected)
    {
        var client = ClientWithoutHeader();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(header);

        var response = await client.PutAsJsonAsync("/api/settings/language", new { language = 9 }, Json);

        Assert.StartsWith(expected, await ProblemTitleAsync(response));
    }
}
