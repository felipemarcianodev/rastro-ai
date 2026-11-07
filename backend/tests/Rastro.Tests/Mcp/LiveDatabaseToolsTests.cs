using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rastro.Infrastructure.Database.Live;
using Rastro.Mcp;
using Rastro.Tests.Api;
using Rastro.Tests.Extractor;

namespace Rastro.Tests.Mcp;

// Cenários MCP-TOOL-015 a 017: o banco vivo no modo agente (Fase 44). O cadastro da conexão é pela API (a tela);
// o servidor MCP só lê o que a API cifrou, com o banco de mentira no lugar do SQL Server.
[Collection(ApiCollection.Name)]
public class LiveDatabaseToolsTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Secret = "Server=db.exemplo.test;Database=loja;User Id=rastro_leitura;Password=SenhaDeTeste#123;TrustServerCertificate=True";
    private const string Sql = "SELECT p.id, p.total FROM pedidos p WHERE YEAR(p.criado_em) = 2026";
    private const string Rewritten = "SELECT p.id, p.total FROM pedidos p WHERE p.criado_em >= '2026-01-01' AND p.criado_em < '2027-01-01'";

    private readonly string _name = "mcpv" + Guid.NewGuid().ToString("N")[..8];
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rastro-tests", "mcpv-" + Guid.NewGuid().ToString("N"));

    private HttpClient Client => fixture.Factory.CreateClient();

    public async Task InitializeAsync()
    {
        FakeLiveDatabase.Reset();
        foreach (var file in Directory.GetFiles(LojaBanco.Root, "*.*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(_root, Path.GetRelativePath(LojaBanco.Root, file));
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

    private ServiceProvider Services() => fixture.McpServices(
        new Dictionary<string, string?> { ["Rastro:Repositories:1:Name"] = _name, ["Rastro:Repositories:1:Path"] = _root },
        services =>
        {
            services.RemoveAll<ILiveDatabase>();
            services.AddSingleton<ILiveDatabase, FakeLiveDatabase>();
            services.AddSingleton<ILiveDatabase, FakePgLiveDatabase>();
        });

    private async Task SaveConnectionAsync() =>
        Assert.Equal(HttpStatusCode.OK, (await Client.PutAsJsonAsync($"/api/repositories/{_name}/connection", new { connectionString = Secret })).StatusCode);

    [Fact] // MCP-TOOL-015
    public async Task GetQueryPlan_SemBancoVivo_DizQueOUsuarioCadastraNaTelaESemCustoNaReescrita()
    {
        await using var provider = Services();
        await using var scope = provider.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<RastroTools>();

        var plan = await tools.GetQueryPlan(_name, Sql);
        var rewrite = await tools.ProposeQueryRewrite(_name, Sql, Rewritten);

        Assert.StartsWith("Este repositório não tem banco vivo cadastrado.", plan);
        Assert.Contains("tela do banco do Rastro", plan);
        Assert.Null(FakeLiveDatabase.LastSql);
        Assert.Contains("Reescrita: aceita.", rewrite);
        Assert.DoesNotContain("Custo estimado no banco vivo", rewrite);
        Assert.Equal("Repositório não cadastrado. Use list_repositories.", await tools.GetQueryPlan("nao-existe", Sql));
        Assert.StartsWith("Informe uma consulta", await tools.GetQueryPlan(_name, "  "));
    }

    [Fact] // MCP-TOOL-016
    public async Task GetQueryPlan_ComBancoVivo_PlanoLegivelConferidoAntesESemAStringDeConexao()
    {
        await SaveConnectionAsync();
        await using var provider = Services();
        await using var scope = provider.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<RastroTools>();

        var hidden = await tools.GetQueryPlan(_name, "SET SHOWPLAN_XML OFF; DELETE FROM pedidos");
        Assert.StartsWith("O Rastro recusou a consulta antes de pedir o plano:", hidden);
        Assert.Null(FakeLiveDatabase.LastSql);

        const string sql = "SELECT id FROM pedidos WHERE status = @p0";
        var plan = await tools.GetQueryPlan(_name, sql);

        // A conexão cifrada pela API chegou ao driver decifrada: o processo do MCP lê as chaves da API.
        Assert.Equal(Secret, FakeLiveDatabase.LastConnection);
        Assert.Equal(sql, FakeLiveDatabase.LastSql);
        Assert.StartsWith("Plano estimado pelo banco vivo (SQL Server, servidor db.exemplo.test, banco loja).", plan);
        Assert.Contains("Custo total estimado: 2 ", plan);
        Assert.Contains("- Clustered Index Scan em ", plan);
        Assert.Contains("100% do custo", plan);
        Assert.Contains("varredura", plan);
        Assert.Contains("Índices que o banco aponta como faltando:", plan);
        Assert.Contains("(status)", plan);
        foreach (var text in new[] { hidden, plan })
        {
            Assert.DoesNotContain("SenhaDeTeste", text);
            Assert.DoesNotContain("rastro_leitura", text);
            Assert.DoesNotContain("Password", text);
        }

        FakeLiveDatabase.Fail = true;
        var down = await tools.GetQueryPlan(_name, sql);
        Assert.StartsWith("O plano não veio do banco vivo:", down);
        Assert.DoesNotContain("SenhaDeTeste", down);
    }

    [Fact] // MCP-TOOL-017
    public async Task ProposeQueryRewrite_ComBancoVivo_TrazOCustoEstimadoDaOriginalEDaReescrita()
    {
        await SaveConnectionAsync();
        await using var provider = Services();
        await using var scope = provider.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<RastroTools>();

        var accepted = await tools.ProposeQueryRewrite(_name, Sql, Rewritten);

        Assert.Contains("Reescrita: aceita.", accepted);
        Assert.Contains("Custo estimado no banco vivo (plano estimado, sem executar): 2 na original, 2 na reescrita.", accepted);
        Assert.Equal(Rewritten, FakeLiveDatabase.LastSql);
        Assert.DoesNotContain("SenhaDeTeste", accepted);

        var refused = await tools.ProposeQueryRewrite(_name, Sql, "SELECT p.id, p.total FROM pedidos_velhos p");
        Assert.Contains("Reescrita: recusada.", refused);
        Assert.DoesNotContain("Custo estimado no banco vivo", refused);

        FakeLiveDatabase.Fail = true;
        var down = await tools.ProposeQueryRewrite(_name, Sql, Rewritten);
        Assert.Contains("Custo estimado no banco vivo: não foi possível comparar.", down);
        Assert.DoesNotContain("SenhaDeTeste", down);
    }
}
