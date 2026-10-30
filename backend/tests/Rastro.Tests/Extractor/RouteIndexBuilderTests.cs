using Rastro.Core.Http;
using Rastro.Extractor;

namespace Rastro.Tests.Extractor;

// Cenários EXT-ROUTE-NNN: índice de rotas só por sintaxe (Fase 25).
public class RouteIndexBuilderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rastro-rotas-" + Guid.NewGuid().ToString("N"));

    public RouteIndexBuilderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact] // EXT-ROUTE-001
    public void Build_FixtureApiPagamentos_ControllerComRouteControllerEVerbos()
    {
        var entries = RouteIndexBuilder.Build(Path.Combine(FixtureWorkspace.FixturesRoot, "ApiPagamentos"));

        Assert.Equal(2, entries.Count);
        var criar = Assert.Single(entries, e => e.Method == "POST");
        Assert.Equal("api/estornos", criar.Template);
        Assert.Equal("Fixture.ApiPagamentos.EstornosController.Criar", criar.Target);
        Assert.Equal("EstornosController.cs", criar.File);
        Assert.Equal(FixtureLines.LineOf("ApiPagamentos/EstornosController.cs", "[HttpPost]", "public async Task Criar"), criar.Line);
        var obter = Assert.Single(entries, e => e.Method == "GET");
        Assert.Equal("api/estornos/{id:int}", obter.Template);
        Assert.Equal("Fixture.ApiPagamentos.EstornosController.Obter", obter.Target);
    }

    [Fact] // EXT-ROUTE-002
    public void Build_MetodoComTemplateAbsolutoEActionENameIgnorado_RespeitaORouting()
    {
        Write("Itens.cs", """
            using Microsoft.AspNetCore.Mvc;
            namespace Loja.Web;

            [ApiController]
            [Route("api/[controller]")]
            public class ItensController : ControllerBase
            {
                [HttpGet("/health/itens")] public void Saude() { }
                [HttpPut("~/legado/{id}")] public void Antigo(int id) { }
                [HttpDelete("{id}", Name = "RemoverItem")] public void Remover(int id) { }
                [Route("custom/[action]")] [HttpPostAttribute] public void Importar() { }
                [HttpPatch] public void Ajustar() { }
                public void SemVerbo() { }
            }
            """);

        var entries = RouteIndexBuilder.Build(_root);

        Assert.Equal(
            [
                ("GET", "health/itens", "Loja.Web.ItensController.Saude"),
                ("PUT", "legado/{id}", "Loja.Web.ItensController.Antigo"),
                ("DELETE", "api/itens/{id}", "Loja.Web.ItensController.Remover"),
                ("POST", "api/itens/custom/importar", "Loja.Web.ItensController.Importar"),
                ("PATCH", "api/itens", "Loja.Web.ItensController.Ajustar")
            ],
            entries.Select(e => (e.Method, e.Template, e.Target)));
    }

    [Fact] // EXT-ROUTE-003
    public void Build_ClasseSemRouteENamespaceEmBlocoComTipoAninhado_AlvoCompleto()
    {
        Write("Sem.cs", """
            namespace Loja
            {
                namespace Web
                {
                    public class Externo
                    {
                        public class Interno
                        {
                            [HttpGet("ping")] public void Ping() { }
                        }
                    }
                }
            }
            """);

        var entry = Assert.Single(RouteIndexBuilder.Build(_root));

        Assert.Equal("ping", entry.Template);
        Assert.Equal("Loja.Web.Externo.Interno.Ping", entry.Target);
    }

    [Fact] // EXT-ROUTE-004
    public void Build_MinimalApiComMapGroupEmVariavelEEncadeado_PrefixaEIgnoraLambda()
    {
        Write("Endpoints.cs", """
            namespace Loja.Api;

            public static class PedidosEndpoints
            {
                public static void Mapear(WebApplication app)
                {
                    var grupo = app.MapGroup("api/pedidos").RequireAuthorization();
                    grupo.MapGet("{id}", Obter);
                    grupo.MapPost("", PedidosEndpoints.Criar);
                    app.MapDelete("/api/legado/{id}", Remover);
                    app.MapGet("lambda", () => "x");
                    app.MapGroup("v2").MapGet("ping", Ping);
                }

                static Task Obter(int id) => Task.CompletedTask;
                static Task Criar() => Task.CompletedTask;
                static Task Remover(int id) => Task.CompletedTask;
                static Task Ping() => Task.CompletedTask;
            }
            """);

        var entries = RouteIndexBuilder.Build(_root);

        Assert.Equal(
            [
                ("GET", "api/pedidos/{id}", "Loja.Api.PedidosEndpoints.Obter"),
                ("POST", "api/pedidos", "Loja.Api.PedidosEndpoints.Criar"),
                ("DELETE", "api/legado/{id}", "Loja.Api.PedidosEndpoints.Remover"),
                ("GET", "v2/ping", "Loja.Api.PedidosEndpoints.Ping")
            ],
            entries.Select(e => (e.Method, e.Template, e.Target)));
    }

    [Theory] // EXT-ROUTE-005
    [InlineData("bin/Controller.cs")]
    [InlineData("obj/Controller.cs")]
    [InlineData("node_modules/pkg/Controller.cs")]
    [InlineData(".git/Controller.cs")]
    [InlineData("dist/Controller.cs")]
    [InlineData("Loja.Tests/Controller.cs")]
    [InlineData("Testes/IntegrationTests/Controller.cs")]
    public void Build_PastasDeBuildDependenciaETeste_FicamForaDoIndice(string relative)
    {
        Write(relative, """[Route("x")] public class XController { [HttpGet] public void Ver() { } }""");
        Write("Real.cs", """[Route("real")] public class RealController { [HttpGet] public void Ver() { } }""");

        var entry = Assert.Single(RouteIndexBuilder.Build(_root));

        Assert.Equal("real", entry.Template);
    }

    [Fact] // EXT-ROUTE-006
    public void Build_VariosArquivos_OrdemDeterministicaPorArquivoELinha()
    {
        Write("B.cs", """[Route("b")] public class BController { [HttpGet] public void Ver() { } }""");
        Write("A.cs", """[Route("a")] public class AController { [HttpGet] public void Ver() { } [HttpPost] public void Criar() { } }""");

        var entries = RouteIndexBuilder.Build(_root);

        Assert.Equal(["A.cs", "A.cs", "B.cs"], entries.Select(e => e.File));
        Assert.Equal(["a", "a", "b"], entries.Select(e => e.Template));
        Assert.Equal(entries, RouteIndexBuilder.Build(_root));
    }

    [Fact] // EXT-ROUTE-007
    public void Build_CasaComORotaChamadaPeloSistemaDePedidos()
    {
        var entries = RouteIndexBuilder.Build(Path.Combine(FixtureWorkspace.FixturesRoot, "ApiPagamentos"));
        var indexes = new Dictionary<string, IReadOnlyList<RouteEntry>> { ["pagamentos"] = entries };

        var found = Assert.Single(RouteMatcher.Match(new Rastro.Core.Graph.HttpCall("POST", "api/estornos"), indexes));

        Assert.Equal("Fixture.ApiPagamentos.EstornosController.Criar", found.Entry.Target);
    }
}
