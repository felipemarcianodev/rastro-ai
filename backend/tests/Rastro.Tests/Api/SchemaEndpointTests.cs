using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastro.Core;
using Rastro.Core.Database;
using Rastro.Core.Graph;
using Rastro.Core.Surveys;
using Rastro.Infrastructure;
using Rastro.Mcp;
using Rastro.Tests.Extractor;

namespace Rastro.Tests.Api;

// Cenários API-DB-NNN e MCP-DB-NNN: esquema do banco de um repositório cadastrado (Fase 37), pela API
// (GET api/repositories/{nome}/schema) e pelo agente (get_db_schema), com a loja fictícia LojaBanco
// copiada para uma pasta temporária, com bin/ e obj/ que precisam ficar de fora.
[Collection(ApiCollection.Name)]
public class SchemaEndpointTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly string _name = "loja" + Guid.NewGuid().ToString("N")[..8];
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rastro-tests", "schema-" + Guid.NewGuid().ToString("N"));

    public async Task InitializeAsync()
    {
        foreach (var file in Directory.GetFiles(LojaBanco.Root, "*.*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(_root, Path.GetRelativePath(LojaBanco.Root, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        // Saída de build com uma tabela que não existe: não pode entrar no esquema.
        var obj = Path.Combine(_root, "Loja.Api", "obj");
        Directory.CreateDirectory(obj);
        await File.WriteAllTextAsync(Path.Combine(obj, "gerado.sql"), "CREATE TABLE fantasma (id INT);");

        var client = fixture.Factory.CreateClient();
        var created = await client.PostAsJsonAsync("/api/repositories", new { name = _name, path = _root });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    public async Task DisposeAsync()
    {
        await fixture.Factory.CreateClient().DeleteAsync($"/api/repositories/{_name}");
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Pasta temporária presa por outro processo: o SO limpa depois.
        }
    }

    [Fact] // API-DB-001
    public async Task Schema_LojaFicticia_TabelasDivergenciaENotaSemOQueEstaEmObj()
    {
        var client = fixture.Factory.CreateClient();

        var schema = await client.GetFromJsonAsync<JsonObject>($"/api/repositories/{_name}/schema");

        Assert.Equal(0, schema!["dialect"]!.GetValue<int>());
        var tables = schema["tables"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).ToList();
        Assert.Equal(["clientes", "itens_pedido", "log_eventos", "pagamentos", "pedidos", "produtos"], tables);
        var pedidos = schema["tables"]!.AsArray().Single(t => t!["name"]!.GetValue<string>() == "pedidos")!;
        var status = pedidos["columns"]!.AsArray().Single(c => c!["name"]!.GetValue<string>() == "status")!;
        Assert.Null(status["type"]);

        var divergence = Assert.Single(schema["divergences"]!.AsArray());
        Assert.Equal(0, divergence!["kind"]!.GetValue<int>());
        Assert.Equal(["nvarchar(20)", "nvarchar(30)"], divergence["values"]!.AsArray().Select(v => v!["value"]!.GetValue<string>()));
        Assert.Equal("banco/quebrado.sql", Assert.Single(schema["notes"]!.AsArray())!["file"]!.GetValue<string>());
    }

    [Fact] // API-DB-002
    public async Task Schema_RepositorioDesconhecido_Retorna404ComCodigo()
    {
        var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync("/api/repositories/nao-existe/schema");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("REPOSITORY_NOT_FOUND", (await response.Content.ReadFromJsonAsync<JsonObject>())?["code"]?.GetValue<string>());
    }

    [Fact] // API-DB-003
    public async Task SchemaUsage_LevantamentosDoRepositorio_SoOMaisNovoDeCadaMetodoESoOProprioSistema()
    {
        FlowGraph Graph(params FlowNode[] nodes) => new(RastroSchema.Version, "PedidosController.Listar", nodes[0].Id, nodes, []);
        var antigo = Survey.Create(_name, "Loja.slnx", "PedidosController.Listar", 5, null, null, DateTimeOffset.UtcNow.AddHours(-1));
        antigo.Start(DateTimeOffset.UtcNow);
        antigo.Complete(Graph(new FlowNode("m:a", NodeKind.Entry, "PedidosController.Listar", Db: [new DbAccess("clientes", DbOperation.Read, 3)])), DateTimeOffset.UtcNow);
        var novo = Survey.Create(_name, "Loja.slnx", "PedidosController.Listar", 5, null, null, DateTimeOffset.UtcNow);
        novo.Start(DateTimeOffset.UtcNow);
        novo.Complete(Graph(
            new FlowNode("m:b", NodeKind.Entry, "PedidosController.Listar", Location: new SourceLocation("Loja.Api/Pedidos.cs", 20),
                Db: [new DbAccess("pedidos", DbOperation.Read, 24, "Pedido"), new DbAccess("pagamentos", DbOperation.Insert, 30, "Pagamento")]),
            new FlowNode("m:c", NodeKind.Method, "Estoque.Baixar", System: "outro", Db: [new DbAccess("estoque", DbOperation.Update, 9)])) with
            {
                Findings = [new Finding(FindingKind.QueryInLoop, FindingSeverity.Warning, "m:b", "consulta pedidos dentro de um laço", Target: "pedidos"),
                    new Finding(FindingKind.QueryInLoop, FindingSeverity.Warning, "m:c", "de outro sistema", Target: "estoque")]
            }, DateTimeOffset.UtcNow);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RastroDbContext>();
            db.Surveys.AddRange(antigo, novo);
            await db.SaveChangesAsync();
        }

        try
        {
            var client = fixture.Factory.CreateClient();
            var report = await client.GetFromJsonAsync<JsonObject>($"/api/repositories/{_name}/schema/usage");
            var uses = report!["uses"]!.AsArray();

            Assert.Equal(["pagamentos", "pedidos"], uses.Select(u => u!["target"]!.GetValue<string>()));
            var pedidos = uses[1]!;
            Assert.Equal((0, "m:b", novo.Id.ToString()), (pedidos["operation"]!.GetValue<int>(), pedidos["nodeId"]!.GetValue<string>(), pedidos["surveyId"]!.GetValue<string>()));
            Assert.Equal(24, pedidos["location"]!["line"]!.GetValue<int>());
            var finding = Assert.Single(report["findings"]!.AsArray())!;
            Assert.Equal(("pedidos", 9), (finding["target"]!.GetValue<string>(), finding["kind"]!.GetValue<int>()));
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/repositories/nao-existe/schema/usage")).StatusCode);
        }
        finally
        {
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<RastroDbContext>();
            await db.Surveys.Where(s => s.Repository == _name).ExecuteDeleteAsync();
        }
    }

    [Fact] // API-DBQ-001
    public async Task AnalyzeQuery_LojaFicticia_AchadosComTrechoETabelasDoEsquema()
    {
        var client = fixture.Factory.CreateClient();
        const string sql = "SELECT * FROM pedidos p WHERE YEAR(p.criado_em) = 2026 ORDER BY p.id";

        var response = await client.PostAsJsonAsync($"/api/repositories/{_name}/schema/analyze", new { sql });
        var analysis = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var findings = analysis!["findings"]!.AsArray();
        Assert.Equal([1, 0, 8], findings.Select(f => f!["kind"]!.GetValue<int>()));
        var first = findings[0]!;
        Assert.Equal("YEAR(p.criado_em)", sql.Substring(first["start"]!.GetValue<int>(), first["length"]!.GetValue<int>()));
        // Mesma escala do FindingSeverity (Fase 43a): o valor maior é o mais grave.
        Assert.Equal((int)QuerySeverity.High, first["severity"]!.GetValue<int>());
        Assert.Equal(2, first["severity"]!.GetValue<int>());
        Assert.Equal("pedidos", first["table"]!.GetValue<string>());
        Assert.True(analysis["tables"]![0]!["inSchema"]!.GetValue<bool>());
        Assert.Null(analysis["error"]);
    }

    [Fact] // API-DBQ-002
    public async Task AnalyzeQuery_VaziaLongaOuRepositorioDesconhecido_RecusaComCodigo()
    {
        var client = fixture.Factory.CreateClient();

        var empty = await client.PostAsJsonAsync($"/api/repositories/{_name}/schema/analyze", new { sql = "  " });
        var big = await client.PostAsJsonAsync($"/api/repositories/{_name}/schema/analyze", new { sql = new string('x', 100_001) });
        var unknown = await client.PostAsJsonAsync("/api/repositories/nao-existe/schema/analyze", new { sql = "SELECT 1" });

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal("QUERY_INVALID", (await empty.Content.ReadFromJsonAsync<JsonObject>())?["code"]?.GetValue<string>());
        Assert.Equal(HttpStatusCode.BadRequest, big.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact] // MCP-DBQ-001
    public async Task AnalyzeQueryTool_LojaFicticia_TextoComGravidadeTrechoETabelas()
    {
        await using var provider = fixture.McpServices(new Dictionary<string, string?>
        {
            ["Rastro:Repositories:1:Name"] = _name,
            ["Rastro:Repositories:1:Path"] = _root
        });
        await using var scope = provider.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<RastroTools>();

        var text = await tools.AnalyzeQuery(_name, "SELECT TOP 5 c.id FROM clientes c WHERE c.email LIKE '%@gmail.com'");

        Assert.Contains("1 instrução lida, 1 achado (regra fixa, sem IA; a consulta não foi executada).", text);
        Assert.Contains("- [média] curinga no começo do LIKE, linha 1: c.email LIKE '%@gmail.com'.", text);
        Assert.Contains("- clientes (apelido c)", text);
        Assert.StartsWith("O leitor de T-SQL não entendeu a consulta", await tools.AnalyzeQuery(_name, "SELECT FROM WHERE"));
        Assert.Equal("Repositório não cadastrado. Use list_repositories.", await tools.AnalyzeQuery("nao-existe", "SELECT 1"));
    }

    [Fact] // MCP-DB-001
    public async Task GetDbSchema_LojaFicticia_TextoComFonteDivergenciaETabelaPedida()
    {
        await using var provider = fixture.McpServices(new Dictionary<string, string?>
        {
            ["Rastro:Repositories:1:Name"] = _name,
            ["Rastro:Repositories:1:Path"] = _root
        });
        await using var scope = provider.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<RastroTools>();

        var all = await tools.GetDbSchema(_name);
        Assert.Contains("Banco: SQL Server. 6 tabelas, 2 views, 1 procedures, 1 divergências entre fontes, 2 achados.", all);
        Assert.Contains("Achado: chave estrangeira pedido_id sem índice", all);
        Assert.Contains("[dado pessoal pelo nome]", all);
        Assert.Contains("Divergência de tipo em status: modelo do EF diz nvarchar(20) (modelo do EF, Loja.Api/Dados/Configuracoes/PedidoConfiguration.cs:10); migration diz nvarchar(30)", all);
        Assert.Contains("Procedure sp_baixar_estoque", all);
        Assert.DoesNotContain("fantasma", all);

        var one = await tools.GetDbSchema(_name, "dbo.pagamentos");
        Assert.Contains("Tabela pagamentos", one);
        Assert.Contains("FK pedido_id -> pedidos (id), ao apagar: cascata", one);
        Assert.DoesNotContain("Tabela pedidos", one);
        Assert.DoesNotContain("Índice", one);

        Assert.Equal("Repositório não cadastrado. Use list_repositories.", await tools.GetDbSchema("nao-existe"));
        Assert.StartsWith("Tabela nada não encontrada", await tools.GetDbSchema(_name, "nada"));
    }
}
