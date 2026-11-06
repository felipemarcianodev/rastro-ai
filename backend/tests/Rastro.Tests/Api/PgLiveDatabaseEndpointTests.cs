using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Rastro.Core.Database;
using Rastro.Tests.Extractor;

namespace Rastro.Tests.Api;

// Cenários API-PGL-NNN: o banco vivo do PostgreSQL pela API (Fase 42b), com o banco de mentira do
// dialeto. O real é testado com Testcontainers (INF-PGL-010 a 014).
[Collection(ApiCollection.Name)]
public class PgLiveDatabaseEndpointTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Secret = "Host=db.exemplo.test;Port=5433;Database=loja;Username=rastro_leitura;Password=SenhaDeTeste#123";
    private readonly string _name = "vivopg" + Guid.NewGuid().ToString("N")[..8];
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rastro-tests", "vivopg-" + Guid.NewGuid().ToString("N"));

    private HttpClient Client => fixture.Factory.CreateClient();

    public async Task InitializeAsync()
    {
        FakeLiveDatabase.Reset();
        foreach (var file in Directory.GetFiles(LojaBancoPg.Root, "*.*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(_root, Path.GetRelativePath(LojaBancoPg.Root, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        Assert.Equal(HttpStatusCode.Created, (await Client.PostAsJsonAsync("/api/repositories", new { name = _name, path = _root })).StatusCode);
    }

    public async Task DisposeAsync()
    {
        await Client.DeleteAsync($"/api/repositories/{_name}/connection");
        await Client.DeleteAsync($"/api/repositories/{_name}");
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

    [Fact] // API-PGL-001
    public async Task Conexao_DoPostgreSql_DialetoServidorComPortaENuncaASenha()
    {
        var saved = await Client.PutAsJsonAsync($"/api/repositories/{_name}/connection", new { connectionString = Secret });
        var body = await saved.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.DoesNotContain("SenhaDeTeste", body);
        var view = (await Client.GetFromJsonAsync<JsonObject>($"/api/repositories/{_name}/connection"))!;
        Assert.Equal(((int)SqlDialect.PostgreSql, "db.exemplo.test:5433", "loja"),
            (view["dialect"]!.GetValue<int>(), view["server"]!.GetValue<string>(), view["database"]!.GetValue<string>()));

        // A string que serve aos dois drivers fica com o dialeto do repositório (PostgreSQL).
        await Client.PutAsJsonAsync($"/api/repositories/{_name}/connection", new { connectionString = "Server=db.exemplo.test;Database=loja;User Id=x;Password=SenhaDeTeste#123" });
        Assert.Equal((int)SqlDialect.PostgreSql, (await Client.GetFromJsonAsync<JsonObject>($"/api/repositories/{_name}/connection"))!["dialect"]!.GetValue<int>());

        var url = await Client.PutAsJsonAsync($"/api/repositories/{_name}/connection", new { connectionString = "postgresql://x:SenhaDeTeste@db.exemplo.test/loja" });
        Assert.Equal(HttpStatusCode.BadRequest, url.StatusCode);
        var problem = await url.Content.ReadAsStringAsync();
        Assert.Contains("CONNECTION_INVALID", problem);
        Assert.DoesNotContain("SenhaDeTeste", problem);
    }

    [Fact] // API-PGL-002
    public async Task Plano_PeloDriverDoPostgreSqlComAPortaDoDialeto()
    {
        await Client.PutAsJsonAsync($"/api/repositories/{_name}/connection", new { connectionString = Secret });

        var response = await Client.PostAsJsonAsync($"/api/repositories/{_name}/schema/plan", new { sql = "SELECT id FROM pedidos WHERE status = @status ORDER BY id" });
        var plan = (await response.Content.ReadFromJsonAsync<JsonObject>())!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(SqlDialect.PostgreSql, FakeLiveDatabase.LastDialect);
        Assert.Equal(["@status"], FakeLiveDatabase.LastVariables);
        Assert.Equal(20.5, plan["totalCost"]!.GetValue<double>());
        var scan = plan["operations"]!.AsArray().Single(o => o!["scan"]!.GetValue<bool>())!;
        Assert.Equal(("Seq Scan", "pedidos"), (scan["physicalOp"]!.GetValue<string>(), scan["object"]!.GetValue<string>()));

        // A porta do PostgreSQL recusa antes de ir ao banco.
        FakeLiveDatabase.Reset();
        var denied = await Client.PostAsJsonAsync($"/api/repositories/{_name}/schema/plan", new { sql = "SELECT pg_sleep(30)" });
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        Assert.Contains("pg_sleep", await denied.Content.ReadAsStringAsync());
        Assert.Null(FakeLiveDatabase.LastSql);

        // O catálogo do PostgreSQL de mentira entra como fonte do esquema.
        var test = (await (await Client.PostAsync($"/api/repositories/{_name}/connection/test", null)).Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.True(test["ok"]!.GetValue<bool>());
    }
}
