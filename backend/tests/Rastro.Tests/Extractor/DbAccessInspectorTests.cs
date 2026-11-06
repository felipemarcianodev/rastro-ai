using Rastro.Core.Graph;
using Rastro.Extractor;

namespace Rastro.Tests.Extractor;

// Cenários EXT-DBF-NNN: banco nos fluxos (Fase 39) sobre a fixture LojaFluxo, com um pedaço do EF Core
// escrito nos mesmos nomes (a fixture compila só com as referências do .NET).
public class DbAccessInspectorTests
{
    private static Task<FlowGraph> GraphAsync(string entry) =>
        FlowExtractor.ExtractAsync(FixtureWorkspace.Load("LojaFluxo"), entry, new ExtractionOptions { BaseDirectory = FixtureWorkspace.FixturesRoot });

    private static FlowNode Node(FlowGraph graph, string label) => graph.Nodes.Single(n => n.Label == label);

    private static List<Finding> Of(FlowGraph graph, FindingKind kind) => graph.Findings!.Where(f => f.Kind == kind).ToList();

    [Fact] // EXT-DBF-001
    public async Task Passo_ComDbSetEMetodoDoEf_DizATabelaEAOperacao()
    {
        var graph = await GraphAsync("PedidosController.Fechar");

        var fechar = Node(graph, "PedidosController.Fechar").Db!;
        Assert.Contains(fechar, a => a is { Target: "Pedidos", Operation: DbOperation.Insert, Entity: "Pedido" });
        // O nome da tabela sai do esquema ([Table("itens_pedido")]), não do nome da classe.
        Assert.Contains(fechar, a => a is { Target: "itens_pedido", Operation: DbOperation.Delete });
        Assert.Contains(fechar, a => a is { Target: "sp_baixar_estoque", Operation: DbOperation.Execute });
        Assert.Contains(fechar, a => a is { Target: "vendas.sp_cliente", Operation: DbOperation.Execute });
        Assert.Equal(fechar.OrderBy(a => a.Line), fechar);

        var graphListar = await GraphAsync("PedidosController.Listar");
        Assert.Equal([new DbAccess("Pedidos", DbOperation.Read, 10, "Pedido")], Node(graphListar, "PedidoRepositorio.AbertosAsync").Db);
        Assert.Equal("itens_pedido", Assert.Single(Node(graphListar, "PedidoRepositorio.ItensAsync").Db!).Target);
    }

    [Fact] // EXT-DBF-002
    public async Task ConsultaDentroDeLaco_DiretaOuPorMetodo_ViraAchadoNMais1()
    {
        var graph = await GraphAsync("PedidosController.Listar");

        var loop = Of(graph, FindingKind.QueryInLoop);
        Assert.Equal(2, loop.Count);
        Assert.Contains(loop, f => f.Message.StartsWith("PedidosController.Listar consulta Pagamentos dentro de um laço") && f.Location!.Line == 27);
        Assert.Contains(loop, f => f.Message.Contains("chama PedidoRepositorio.ItensAsync dentro de um laço, e ele consulta itens_pedido"));
        Assert.All(loop, f => Assert.Equal(FindingSeverity.Warning, f.Severity));
        Assert.Equal(["Pagamentos", "itens_pedido"], loop.Select(f => f.Target!).Order(StringComparer.Ordinal));
        // A coleção do foreach roda uma vez: AbertosAsync não é N+1.
        Assert.DoesNotContain(loop, f => f.Message.Contains("AbertosAsync"));
    }

    [Fact] // EXT-DBF-003
    public async Task ToListAntesDoWhere_NaVariavel_ViraAchadoNaLinhaDoFiltro()
    {
        var graph = await GraphAsync("PedidosController.Listar");

        var finding = Assert.Single(Of(graph, FindingKind.MaterializeBeforeFilter));
        Assert.Contains("traz Pedidos com ToListAsync e só depois aplica Where", finding.Message);
        Assert.Equal(("LojaFluxo/Pedidos.cs", 11), (finding.Location!.File, finding.Location.Line));
    }

    [Fact] // EXT-DBF-004
    public async Task LeituraSemAsNoTracking_SoNoMetodoQueNaoGrava()
    {
        var listar = await GraphAsync("PedidosController.Listar");
        var finding = Assert.Single(Of(listar, FindingKind.ReadWithoutNoTracking));
        Assert.Equal(Node(listar, "PedidoRepositorio.AbertosAsync").Id, finding.NodeId);
        Assert.Equal(FindingSeverity.Info, finding.Severity);

        // Fechar grava (Add, Remove, SaveChanges): nenhum aviso de rastreamento.
        Assert.Empty(Of(await GraphAsync("PedidosController.Fechar"), FindingKind.ReadWithoutNoTracking));
    }

    [Fact] // EXT-DBF-005
    public async Task ProcedurePorSqlCru_AchadoComOMetodoEAvisoQuandoNaoEstaNoSql()
    {
        var graph = await GraphAsync("PedidosController.Fechar");

        var procedures = Of(graph, FindingKind.ProcedureByRawSql);
        Assert.Equal(2, procedures.Count);
        Assert.Contains(procedures, f => f.Message.Contains("chama a procedure sp_baixar_estoque por ExecuteSqlRaw") && f.Message.Contains("não aparece nos arquivos .sql"));
        Assert.Contains(procedures, f => f.Message.Contains("vendas.sp_cliente por FromSqlRaw"));
        Assert.Equal(FindingCategory.Database, QualityThresholds.CategoryOf(FindingKind.ProcedureByRawSql));
    }

    [Fact] // EXT-DBF-007
    public async Task ComOEsquemaDoRepositorio_ATabelaTemONomeDoBancoEAProcedureDoSqlNaoViraAviso()
    {
        // O repositório inteiro diz o que a solution sozinha não diz: o snake case e a procedure no .sql.
        var files = Directory.GetFiles(Path.Combine(FixtureWorkspace.FixturesRoot, "LojaFluxo"), "*.cs")
            .Select(f => new Rastro.Extractor.Database.SchemaFile("LojaFluxo/" + Path.GetFileName(f), File.ReadAllText(f)))
            .Append(new("Program.cs", "builder.Services.AddDbContext<LojaContext>(o => o.UseSqlServer(cs).UseSnakeCaseNamingConvention());"))
            .Append(new("banco/sp.sql", "CREATE PROCEDURE dbo.sp_baixar_estoque @p INT AS BEGIN SELECT 1; END"))
            .ToList();
        var schema = Rastro.Extractor.Database.DbSchemaExtractor.Extract(files);

        var graph = await FlowExtractor.ExtractAsync(FixtureWorkspace.Load("LojaFluxo"), "PedidosController.Fechar",
            new ExtractionOptions { BaseDirectory = FixtureWorkspace.FixturesRoot, Schema = schema });

        var fechar = Node(graph, "PedidosController.Fechar").Db!;
        Assert.Contains(fechar, a => a is { Target: "pedidos", Operation: DbOperation.Insert });
        var procedure = Assert.Single(Of(graph, FindingKind.ProcedureByRawSql), f => f.Target == "sp_baixar_estoque");
        Assert.DoesNotContain("não aparece", procedure.Message);
    }

    [Fact] // EXT-DBF-006
    public async Task FluxoSemEf_NenhumPassoComBanco()
    {
        var graph = await FlowExtractor.ExtractAsync(FixtureWorkspace.Load("Quality"), "PedidoAppService.AprovarAsync",
            new ExtractionOptions { BaseDirectory = FixtureWorkspace.FixturesRoot });

        Assert.All(graph.Nodes, n => Assert.Null(n.Db));
        Assert.DoesNotContain(graph.Findings!, f => QualityThresholds.CategoryOf(f.Kind) == FindingCategory.Database);
    }
}
