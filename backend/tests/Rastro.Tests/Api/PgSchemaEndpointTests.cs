using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Rastro.Ai.Providers;
using Rastro.Ai.Settings;
using Rastro.Mcp;
using Rastro.Tests.Ai;
using Rastro.Tests.Extractor;

namespace Rastro.Tests.Api;

// Cenários API-PG-NNN e MCP-PG-NNN: o repositório PostgreSQL (loja fictícia LojaBancoPg, Fase 42) pela
// API e pelo agente. O dialeto sai do repositório, sem pergunta: o esquema, a análise, a conferência da
// proposta e o texto para o provedor de IA (falso) vêm no dialeto do PostgreSQL.
[Collection(ApiCollection.Name)]
public class PgSchemaEndpointTests(ApiFixture fixture) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Sql = "SELECT c.id, c.nome FROM clientes c WHERE c.email ILIKE '%@gmail.com' AND c.criado_em::date = $1";

    private readonly string _name = "lojapg" + Guid.NewGuid().ToString("N")[..8];
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rastro-tests", "schema-pg-" + Guid.NewGuid().ToString("N"));

    private HttpClient Client => fixture.Factory.CreateClient();

    public async Task InitializeAsync()
    {
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
        await Client.DeleteAsync($"/api/repositories/{_name}");
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Pasta temporária presa por outro processo: o SO limpa depois.
        }
    }

    [Fact] // API-PG-001
    public async Task Schema_LojaPostgreSql_DialetoUmComOsTiposDoNpgsql()
    {
        var schema = await Client.GetFromJsonAsync<JsonObject>($"/api/repositories/{_name}/schema");

        Assert.Equal(1, schema!["dialect"]!.GetValue<int>());
        var clientes = schema["tables"]!.AsArray().Single(t => t!["name"]!.GetValue<string>() == "clientes")!;
        Assert.Equal("jsonb", clientes["columns"]!.AsArray().Single(c => c!["name"]!.GetValue<string>() == "preferencias")!["type"]!.GetValue<string>());
        Assert.Equal(7, schema["tables"]!.AsArray().Count);
    }

    [Fact] // API-PG-002
    public async Task AnalyzeQuery_LojaPostgreSql_IlikeEConversaoComOTrechoNoTexto()
    {
        var response = await Client.PostAsJsonAsync($"/api/repositories/{_name}/schema/analyze", new { sql = Sql });
        var analysis = (await response.Content.ReadFromJsonAsync<JsonObject>())!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, analysis["dialect"]!.GetValue<int>());
        var findings = analysis["findings"]!.AsArray();
        Assert.Equal([1, 2], findings.Select(f => f!["kind"]!.GetValue<int>()));
        Assert.Equal(["c.criado_em::date", "c.email ILIKE '%@gmail.com'"],
            findings.Select(f => Sql.Substring(f!["start"]!.GetValue<int>(), f["length"]!.GetValue<int>())));
    }

    [Fact] // MCP-PG-001
    public async Task Agente_LojaPostgreSql_EsquemaAnaliseEPropostaNoDialeto()
    {
        await using var provider = fixture.McpServices(new Dictionary<string, string?>
        {
            ["Rastro:Repositories:1:Name"] = _name,
            ["Rastro:Repositories:1:Path"] = _root
        });
        await using var scope = provider.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<RastroTools>();

        Assert.Contains("Banco: PostgreSQL.", await tools.GetDbSchema(_name));
        Assert.Contains("c.email ILIKE '%@gmail.com'", await tools.AnalyzeQuery(_name, Sql));
        Assert.StartsWith("O leitor de SQL do PostgreSQL não entendeu a consulta", await tools.AnalyzeQuery(_name, "SELECT id FROM clientes WHERE id = = 1"));

        var accepted = await tools.ProposeQueryRewrite(_name, Sql,
            "SELECT c.id, c.nome FROM clientes c WHERE c.email ILIKE '%@gmail.com' AND c.criado_em >= $1::timestamptz AND c.criado_em < $1::timestamptz + interval '1 day'",
            [new QueryChangeInput("Faixa de datas", "A coluna pura deixa o índice trabalhar")],
            [new QueryIndexInput("clientes", ["criado_em"], null, false, "Filtro por data")]);
        Assert.Contains("Reescrita: aceita.", accepted);
        Assert.Contains("CREATE INDEX \"ix_clientes_criado_em\" ON \"clientes\" (\"criado_em\");", accepted);
    }

    [Fact] // API-PG-003
    public async Task Sugerir_LojaPostgreSql_OProvedorRecebeODialetoEARespostaEConferidaNele()
    {
        var settings = new
        {
            enabled = true,
            provider = (int)AiProvider.Claude,
            claudeModel = "claude-opus-5-5",
            claudeKey = "sk-ant-teste-falsa",
            openAiModel = (string?)null,
            openAiKey = (string?)null,
            geminiModel = (string?)null,
            geminiKey = (string?)null,
            dailyTokenBudget = 0,
            useFillHoles = true,
            useDescribeRules = true,
            useSuggestions = true,
            useContextSuggestions = true
        };
        Assert.Equal(HttpStatusCode.OK, (await Client.PutAsJsonAsync("/api/settings/ai", settings)).StatusCode);
        FakeAiTextService.Responder = _ => new AiResult(JsonSerializer.Serialize(new
        {
            rewrittenSql = "SELECT c.id, c.nome FROM clientes c WHERE c.email ILIKE '%@gmail.com' AND c.criado_em >= $1 LIMIT 10",
            changes = new[] { new { what = "Paginou", why = "Menos linhas." } },
            indexes = Array.Empty<object>()
        }), 10, 5);

        var response = await Client.PostAsJsonAsync($"/api/repositories/{_name}/schema/ai/suggestion", new { sql = Sql });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var review = (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!["review"]!;
        Assert.False(review["rewrite"]!["accepted"]!.GetValue<bool>());
        Assert.Contains(review["rewrite"]!["refusals"]!.AsArray(), r => r!.GetValue<string>().Contains("LIMIT, FETCH ou OFFSET"));
        var request = FakeAiTextService.LastRequest!;
        Assert.Contains("especialista em desempenho de PostgreSQL", request.System);
        Assert.Contains("Não acrescente LIMIT, OFFSET nem FETCH", request.System);
        Assert.Contains("Dialeto: PostgreSQL.", request.User);
    }
}
