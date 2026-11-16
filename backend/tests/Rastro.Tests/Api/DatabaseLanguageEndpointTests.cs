using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Rastro.Tests.Extractor;

namespace Rastro.Tests.Api;

// Cenários DB-LANG-NNN: a API devolve os textos do banco no idioma do Accept-Language (Fase 50, ADR-009), com o
// banco vivo de mentira. O título do erro é o que a tela mostra quando não traduz pelo código.
[Collection(ApiCollection.Name)]
public class DatabaseLanguageEndpointTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Secret = "Server=db.exemplo.test;Database=loja;User Id=rastro_leitura;Password=SenhaDeTeste#123;TrustServerCertificate=True";
    private readonly string _name = "idioma" + Guid.NewGuid().ToString("N")[..8];
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rastro-tests", "idioma-" + Guid.NewGuid().ToString("N"));

    public async Task InitializeAsync()
    {
        FakeLiveDatabase.Reset();
        foreach (var file in Directory.GetFiles(LojaBanco.Root, "*.*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(_root, Path.GetRelativePath(LojaBanco.Root, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        var created = await fixture.Factory.CreateClient().PostAsJsonAsync("/api/repositories", new { name = _name, path = _root });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    public async Task DisposeAsync()
    {
        var client = fixture.Factory.CreateClient();
        await client.DeleteAsync($"/api/repositories/{_name}/connection");
        await client.DeleteAsync($"/api/repositories/{_name}");
        FakeLiveDatabase.Reset();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Pasta temporária presa por outro processo: o SO limpa depois.
        }
    }

    private HttpClient ClientIn(string language)
    {
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.Clear();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);
        return client;
    }

    private static async Task<string?> TitleOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonObject>())?["title"]?.GetValue<string>();

    [Theory] // DB-LANG-101
    [InlineData("en-US", "The estimated plan is only produced for SELECT, INSERT, UPDATE, DELETE or MERGE")]
    [InlineData("pt-BR", "O plano estimado só sai para SELECT, INSERT, UPDATE, DELETE ou MERGE")]
    [InlineData("es-MX", "El plan estimado solo se genera para SELECT, INSERT, UPDATE, DELETE o MERGE")]
    public async Task Plano_ConsultaRecusada_TituloDoPlanNotAllowedNoIdiomaDoPedido(string language, string expected)
    {
        var client = ClientIn(language);
        await client.PutAsJsonAsync($"/api/repositories/{_name}/connection", new { connectionString = Secret });

        var response = await client.PostAsJsonAsync($"/api/repositories/{_name}/schema/plan", new { sql = "SET SHOWPLAN_XML OFF" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.StartsWith(expected, await TitleOf(response), StringComparison.Ordinal);
        Assert.Null(FakeLiveDatabase.LastSql);
    }

    [Fact] // DB-LANG-102
    public async Task Plano_BancoCaiu_TituloDoLiveDatabaseErrorEmIngles()
    {
        var client = ClientIn("en-US");
        await client.PutAsJsonAsync($"/api/repositories/{_name}/connection", new { connectionString = Secret });
        FakeLiveDatabase.Fail = true;

        var response = await client.PostAsJsonAsync($"/api/repositories/{_name}/schema/plan", new { sql = "SELECT id FROM pedidos WHERE status = @p0" });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var title = await TitleOf(response);
        Assert.StartsWith("The plan did not come from the live database:", title, StringComparison.Ordinal);
        Assert.DoesNotContain("SenhaDeTeste", title);
    }

    [Fact] // DB-LANG-103
    public async Task Conexao_StringInvalidaETeste_MensagensNoIdiomaDoPedido()
    {
        var english = ClientIn("en-US");
        var invalid = await english.PutAsJsonAsync($"/api/repositories/{_name}/connection", new { connectionString = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("Connection string empty or too long.", await TitleOf(invalid));

        await english.PutAsJsonAsync($"/api/repositories/{_name}/connection", new { connectionString = Secret });
        var spanish = await ClientIn("es").PostAsync($"/api/repositories/{_name}/connection/test", null);
        Assert.Equal("Conectó: 6 tablas en el catálogo.", (await spanish.Content.ReadFromJsonAsync<JsonObject>())!["message"]!.GetValue<string>());
        var englishTest = await english.PostAsync($"/api/repositories/{_name}/connection/test", null);
        Assert.Equal("Connected: 6 tables in the catalog.", (await englishTest.Content.ReadFromJsonAsync<JsonObject>())!["message"]!.GetValue<string>());
    }

    [Fact] // DB-LANG-104
    public async Task Esquema_BancoVivoForaDoAr_NotaEmIngles()
    {
        var client = ClientIn("en-US");
        await client.PutAsJsonAsync($"/api/repositories/{_name}/connection", new { connectionString = Secret });
        FakeLiveDatabase.Fail = true;

        var schema = await client.GetFromJsonAsync<JsonObject>($"/api/repositories/{_name}/schema");

        Assert.Contains(schema!["notes"]!.AsArray(), n => n!["message"]!.GetValue<string>() == "The live database did not respond (timed out): the schema came from the code only.");
    }

    [Fact] // DB-LANG-105
    public async Task AnaliseDeConsulta_AcceptLanguageIngles_AchadosEmIngles()
    {
        var client = ClientIn("en-US");

        var analysis = await (await client.PostAsJsonAsync($"/api/repositories/{_name}/schema/analyze", new { sql = "SELECT * FROM pedidos" })).Content.ReadFromJsonAsync<JsonObject>();

        var message = analysis!["findings"]!.AsArray().Select(f => f!["message"]!.GetValue<string>()).First();
        Assert.StartsWith("Brings back all", message, StringComparison.Ordinal);
        var portuguese = await (await fixture.Factory.CreateClient().PostAsJsonAsync($"/api/repositories/{_name}/schema/analyze", new { sql = "SELECT * FROM pedidos" })).Content.ReadFromJsonAsync<JsonObject>();
        Assert.StartsWith("Traz todas as", portuguese!["findings"]![0]!["message"]!.GetValue<string>(), StringComparison.Ordinal);
    }
}
