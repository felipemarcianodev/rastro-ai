using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Rastro.Core.Database;
using Rastro.Infrastructure;
using Rastro.Infrastructure.Database.Live;
using Rastro.Tests.Extractor;

namespace Rastro.Tests.Api;

// Banco vivo de mentira para os testes da API: o real (SQL Server) é testado com Testcontainers.
// Guarda o que recebeu, para o teste conferir o que chegaria ao banco.
public class FakeLiveDatabase : ILiveDatabase
{
    public virtual SqlDialect Dialect => SqlDialect.SqlServer;

    public static string? LastConnection { get; private set; }
    public static string? LastSql { get; private set; }
    public static IReadOnlyList<string> LastVariables { get; private set; } = [];
    public static bool Fail { get; set; }

    public const string PlanXml = """
        <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan"><BatchSequence><Batch><Statements>
          <StmtSimple StatementType="SELECT" StatementSubTreeCost="2">
            <QueryPlan>
              <MissingIndexes><MissingIndexGroup Impact="90"><MissingIndex Table="[pedidos]"><ColumnGroup Usage="EQUALITY"><Column Name="[status]" /></ColumnGroup></MissingIndex></MissingIndexGroup></MissingIndexes>
              <RelOp PhysicalOp="Clustered Index Scan" LogicalOp="Clustered Index Scan" EstimateRows="1000" EstimatedTotalSubtreeCost="2">
                <IndexScan><Object Table="[pedidos]" Index="[PK_pedidos]" /></IndexScan>
              </RelOp>
            </QueryPlan>
          </StmtSimple>
        </Statements></Batch></BatchSequence></ShowPlanXML>
        """;

    public Task<int> CountTablesAsync(string connectionString, CancellationToken ct)
    {
        LastConnection = connectionString;
        return Fail ? throw new TimeoutException() : Task.FromResult(6);
    }

    public Task<SchemaFragment> ReadCatalogAsync(string connectionString, CancellationToken ct)
    {
        LastConnection = connectionString;
        if (Fail) throw new TimeoutException();
        var source = new SchemaSource(SchemaSourceKind.LiveDatabase, "banco vivo", 0);
        // Um índice criado à mão em produção, que o código não tem.
        return Task.FromResult(new SchemaFragment(
            [new DbTable(null, "pedidos", [new DbColumn("status", "nvarchar(30)", false, null, false, [source])], null, [],
                [new DbIndex("IX_pedidos_status", ["status"], [], false, source)], [source])],
            [], [], []));
    }

    public Task<string> EstimatedPlanAsync(string connectionString, string sql, IReadOnlyList<string> variables, CancellationToken ct)
    {
        (LastConnection, LastSql, LastVariables, LastDialect) = (connectionString, sql, variables, Dialect);
        return Fail ? throw new InvalidOperationException("caiu") : Task.FromResult(Dialect == SqlDialect.PostgreSql ? PlanJson : PlanXml);
    }

    public static SqlDialect? LastDialect { get; private set; }

    // O EXPLAIN (FORMAT JSON) do PostgreSQL de mentira (Fase 42b): uma varredura de pedidos sob a ordenação.
    public const string PlanJson = """
        [{"Plan": {"Node Type": "Sort", "Total Cost": 20.5, "Plan Rows": 100,
          "Plans": [{"Node Type": "Seq Scan", "Relation Name": "pedidos", "Alias": "p", "Total Cost": 15.0, "Plan Rows": 100}]}}]
        """;

    public static void Reset() => (LastConnection, LastSql, LastVariables, Fail, LastDialect) = (null, null, [], false, null);
}

// O mesmo banco de mentira no dialeto do PostgreSQL (Fase 42b): o real é testado com Testcontainers.
public sealed class FakePgLiveDatabase : FakeLiveDatabase
{
    public override SqlDialect Dialect => SqlDialect.PostgreSql;
}

// Cenários API-DBL-NNN: a conexão com o banco vivo pela API (Fase 40b, ADR-006), com o banco de mentira.
[Collection(ApiCollection.Name)]
public class LiveDatabaseEndpointTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Secret = "Server=db.exemplo.test;Database=loja;User Id=rastro_leitura;Password=SenhaDeTeste#123;TrustServerCertificate=True";
    private readonly string _name = "vivo" + Guid.NewGuid().ToString("N")[..8];
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rastro-tests", "vivo-" + Guid.NewGuid().ToString("N"));

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

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonObject>())?["code"]?.GetValue<string>();

    [Fact] // API-DBL-001
    public async Task Conexao_CadastradaCifradaENuncaDevolvida()
    {
        var client = fixture.Factory.CreateClient();
        Assert.False((await client.GetFromJsonAsync<JsonObject>($"/api/repositories/{_name}/connection"))!["configured"]!.GetValue<bool>());

        var saved = await client.PutAsJsonAsync($"/api/repositories/{_name}/connection", new { connectionString = Secret });
        var body = await saved.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.DoesNotContain("SenhaDeTeste", body);
        Assert.DoesNotContain("rastro_leitura", body);
        var view = await client.GetFromJsonAsync<JsonObject>($"/api/repositories/{_name}/connection");
        Assert.Equal((true, "db.exemplo.test", "loja"), (view!["configured"]!.GetValue<bool>(), view["server"]!.GetValue<string>(), view["database"]!.GetValue<string>()));
        Assert.DoesNotContain("SenhaDeTeste", view.ToJsonString());

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var row = scope.ServiceProvider.GetRequiredService<RastroDbContext>().LiveConnections.Single(c => c.Repository == _name);
        Assert.DoesNotContain("SenhaDeTeste", row.ConnectionProtected);
        Assert.Equal(Secret, await scope.ServiceProvider.GetRequiredService<LiveConnectionStore>().ConnectionStringAsync(_name, default));
    }

    [Fact] // API-DBL-002
    public async Task Conexao_InvalidaSemServidorOuRepositorioDesconhecido_Recusada()
    {
        var client = fixture.Factory.CreateClient();

        var garbage = await client.PutAsJsonAsync($"/api/repositories/{_name}/connection", new { connectionString = "isto não é=uma;string;;=de conexão" });
        var noServer = await client.PutAsJsonAsync($"/api/repositories/{_name}/connection", new { connectionString = "Database=loja;User Id=x;Password=y" });
        var unknown = await client.PutAsJsonAsync("/api/repositories/nao-existe/connection", new { connectionString = Secret });

        Assert.Equal((HttpStatusCode.BadRequest, "CONNECTION_INVALID"), (garbage.StatusCode, await CodeOf(garbage)));
        Assert.Equal((HttpStatusCode.BadRequest, "CONNECTION_INVALID"), (noServer.StatusCode, await CodeOf(noServer)));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact] // API-DBL-003
    public async Task Conexao_DeOutroSite_RecusadaPeloOrigin()
    {
        var client = fixture.Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/repositories/{_name}/connection") { Content = JsonContent.Create(new { connectionString = Secret }) };
        request.Headers.TryAddWithoutValidation("Origin", "https://site-malicioso.test");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False((await client.GetFromJsonAsync<JsonObject>($"/api/repositories/{_name}/connection"))!["configured"]!.GetValue<bool>());
    }

    [Fact] // API-DBL-004
    public async Task TesteEEsquema_ComConexao_CatalogoEntraComoFonteEFalhaViraNota()
    {
        var client = fixture.Factory.CreateClient();
        Assert.Equal("CONNECTION_MISSING", await CodeOf(await client.PostAsync($"/api/repositories/{_name}/connection/test", null)));
        await client.PutAsJsonAsync($"/api/repositories/{_name}/connection", new { connectionString = Secret });

        var test = await (await client.PostAsync($"/api/repositories/{_name}/connection/test", null)).Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal((true, "Conectou: 6 tabelas no catálogo."), (test!["ok"]!.GetValue<bool>(), test["message"]!.GetValue<string>()));
        Assert.Equal(Secret, FakeLiveDatabase.LastConnection);

        var schema = await client.GetFromJsonAsync<JsonObject>($"/api/repositories/{_name}/schema");
        var pedidos = schema!["tables"]!.AsArray().Single(t => t!["name"]!.GetValue<string>() == "pedidos")!;
        Assert.Contains(pedidos["sources"]!.AsArray(), s => s!["kind"]!.GetValue<int>() == (int)SchemaSourceKind.LiveDatabase);
        Assert.Contains(pedidos["indexes"]!.AsArray(), i => i!["name"]!.GetValue<string>() == "IX_pedidos_status");

        FakeLiveDatabase.Fail = true;
        var failed = await (await client.PostAsync($"/api/repositories/{_name}/connection/test", null)).Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal((false, "Não conectou: tempo esgotado."), (failed!["ok"]!.GetValue<bool>(), failed["message"]!.GetValue<string>()));
        var offline = await client.GetFromJsonAsync<JsonObject>($"/api/repositories/{_name}/schema");
        Assert.Contains(offline!["notes"]!.AsArray(), n => n!["message"]!.GetValue<string>() == "O banco vivo não respondeu (tempo esgotado): o esquema saiu só do código.");
        Assert.Equal(6, offline["tables"]!.AsArray().Count);
    }

    [Fact] // API-DBL-005
    public async Task Plano_ConsultaConferidaAntes_SoAPermitidaChegaAoBanco()
    {
        var client = fixture.Factory.CreateClient();
        Assert.Equal("CONNECTION_MISSING", await CodeOf(await client.PostAsJsonAsync($"/api/repositories/{_name}/schema/plan", new { sql = "SELECT 1" })));
        await client.PutAsJsonAsync($"/api/repositories/{_name}/connection", new { connectionString = Secret });

        var hidden = await client.PostAsJsonAsync($"/api/repositories/{_name}/schema/plan", new { sql = "SET SHOWPLAN_XML OFF; DELETE FROM pedidos" });
        Assert.Equal((HttpStatusCode.BadRequest, "PLAN_NOT_ALLOWED"), (hidden.StatusCode, await CodeOf(hidden)));
        Assert.Null(FakeLiveDatabase.LastSql);

        const string sql = "SELECT id FROM pedidos WHERE status = @p0";
        var ok = await client.PostAsJsonAsync($"/api/repositories/{_name}/schema/plan", new { sql });
        var plan = await ok.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal((sql, "@p0"), (FakeLiveDatabase.LastSql, Assert.Single(FakeLiveDatabase.LastVariables)));
        var operation = plan!["operations"]!.AsArray().Single()!;
        Assert.Equal(("Clustered Index Scan", 100.0, true), (operation["physicalOp"]!.GetValue<string>(), operation["percent"]!.GetValue<double>(), operation["scan"]!.GetValue<bool>()));
        Assert.Equal("status", plan["missingIndexes"]![0]!["equality"]![0]!.GetValue<string>());

        FakeLiveDatabase.Fail = true;
        var down = await client.PostAsJsonAsync($"/api/repositories/{_name}/schema/plan", new { sql });
        Assert.Equal((HttpStatusCode.BadGateway, "LIVE_DATABASE_ERROR"), (down.StatusCode, await CodeOf(down)));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/repositories/{_name}/connection")).StatusCode);
        Assert.False((await client.GetFromJsonAsync<JsonObject>($"/api/repositories/{_name}/connection"))!["configured"]!.GetValue<bool>());
    }
}
