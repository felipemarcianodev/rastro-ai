using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
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
        Assert.Contains("Banco: SQL Server. 6 tabelas, 2 views, 1 procedures, 1 divergências entre fontes.", all);
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
