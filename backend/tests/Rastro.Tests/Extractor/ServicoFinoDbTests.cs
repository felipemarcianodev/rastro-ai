using Microsoft.CodeAnalysis;
using Rastro.Core.Database;
using Rastro.Core.Graph;
using Rastro.Extractor;

namespace Rastro.Tests.Extractor;

// Cenários EXT-DBF-018 em diante: banco nos fluxos através de serviço fino e de projetos separados (Fase 43c),
// sobre a fixture TarefasMulti (Domínio) + TarefasMulti.Infra (repositórios).
public class ServicoFinoDbTests
{
    private static Solution Load() => FixtureWorkspace.LoadLayered("TarefasMulti", "TarefasMulti.Infra");

    private static Task<FlowGraph> GraphAsync(string entry) =>
        FlowExtractor.ExtractAsync(Load(), entry, new ExtractionOptions { BaseDirectory = FixtureWorkspace.FixturesRoot });

    private static List<Finding> Of(FlowGraph graph, FindingKind kind) => graph.Findings!.Where(f => f.Kind == kind).ToList();

    [Fact] // EXT-DBF-018
    public async Task Fixture_EmDoisProjetos_Compila()
    {
        await FixtureWorkspace.AssertCompilesAsync(Load());
    }

    [Fact] // EXT-DBF-019
    public async Task InterfaceDerivadaQueEscondeComNew_EmOutroProjeto_ResolveParaOMembroDaInfra()
    {
        var graph = await GraphAsync("BulkService.Todas");

        Assert.DoesNotContain(graph.Nodes, n => n.Kind == NodeKind.Unresolved);
        var all = Assert.Single(graph.Nodes, n => n.Label == "TarefaRepository.GetAllAsync");
        Assert.Equal("Tarefas", Assert.Single(all.Db!).Target);
    }

    [Fact] // EXT-DBF-020
    public async Task InterfaceDerivadaComBaseGenerica_ResolveNaInfraPeloMembroEscondido()
    {
        var graph = await GraphAsync("GenericoService.PelaDerivada");

        Assert.DoesNotContain(graph.Nodes, n => n.Kind == NodeKind.Unresolved);
        Assert.Contains(graph.Nodes, n => n.Label == "TarefaRepository.GetAllAsync");
    }

    [Theory] // EXT-DBF-021
    [InlineData("BulkService.Lote", "TarefaService.GetByIdAsync")]
    [InlineData("BulkService.LoteComAwait", "TarefaService.GetByIdComAwaitAsync")]
    [InlineData("BulkService.LotePelaFachada", "TarefaFachada.Obter")]
    public async Task NMais1_ApareceQuandoOLacoChamaUmServicoFinoQueRepassaAoRepositorio(string entry, string chamado)
    {
        var graph = await GraphAsync(entry);

        var finding = Assert.Single(Of(graph, FindingKind.QueryInLoop));
        Assert.Equal(graph.Nodes.Single(n => n.Kind == NodeKind.Entry).Id, finding.NodeId);
        Assert.Equal("Tarefas", finding.Target);
        Assert.Contains($"chama {chamado} dentro de um laço", finding.Message);
        Assert.Contains("chega a EfRepository.GetByIdAsync, que consulta Tarefas", finding.Message);
    }

    [Theory] // EXT-DBF-022
    [InlineData("BulkService.Filtrar", "TarefaService.ListAsync")]
    [InlineData("BulkService.FiltrarComAwait", "TarefaService.ListComAwaitAsync")]
    [InlineData("BulkService.FiltrarPelaFachada", "TarefaFachada.Listar")]
    public async Task ToListNoRepositorioEWhereNoChamador_AtravessaOServicoFino(string entry, string chamado)
    {
        var graph = await GraphAsync(entry);

        var finding = Assert.Single(Of(graph, FindingKind.MaterializeBeforeFilter));
        Assert.Equal(graph.Nodes.Single(n => n.Kind == NodeKind.Entry).Id, finding.NodeId);
        Assert.Equal("Tarefas", finding.Target);
        Assert.Contains($"recebe Tarefas de {chamado}, ", finding.Message);
        Assert.Contains("que repassa o resultado de TarefaRepository.GetAllAsync, e este já a trouxe com ToListAsync", finding.Message);
        Assert.Contains("só depois aplica Where", finding.Message);
        Assert.Contains("Passe o critério para TarefaRepository.GetAllAsync filtrar", finding.Message);
    }

    [Fact] // EXT-DBF-023
    public async Task ServicoFinoSemLacoESemFiltro_NaoViraAchado()
    {
        var graph = await GraphAsync("BulkService.Todas");

        Assert.Empty(Of(graph, FindingKind.QueryInLoop));
        Assert.Empty(Of(graph, FindingKind.MaterializeBeforeFilter));
    }

    [Fact] // EXT-DBF-024
    public async Task NMais1_ComRecursaoEntreMetodos_TerminaEAchaUmaVez()
    {
        var graph = await GraphAsync("CicloService.Lote");

        var finding = Assert.Single(Of(graph, FindingKind.QueryInLoop));
        Assert.Contains("chama CicloService.Ping dentro de um laço", finding.Message);
        Assert.Equal("Tarefas", finding.Target);
    }

    [Fact] // EXT-DBF-025
    public async Task NMais1_SegueAteQuatroSaltosENaoMais()
    {
        // B1 -> B2 -> B3 -> B4 -> repositório: quatro saltos depois do método chamado.
        var dentro = Assert.Single(Of(await GraphAsync("CadeiasService.LoteB"), FindingKind.QueryInLoop));
        Assert.Contains("chama CadeiaB.B1 dentro de um laço", dentro.Message);

        // Um salto a mais (C5): fora do limite.
        Assert.Empty(Of(await GraphAsync("CadeiasService.LoteC"), FindingKind.QueryInLoop));
    }

    private static DbSchema Esquema(string? method, params string[] operators) => new(SqlDialect.PostgreSql,
    [
        new DbTable(null, "tarefas",
            [new DbColumn("titulo", "text", false, null, false, []), new DbColumn("descricao", "text", false, null, false, [])],
            null, [],
            [new DbIndex("ix_tarefas_titulo", ["titulo"], [], false, new SchemaSource(SchemaSourceKind.EfModel, "x.cs", 1), Method: method, OperatorClasses: operators.Length > 0 ? operators : null)],
            [], Entity: "Tarefa")
    ], [], [], [], [], []);

    private static Task<FlowGraph> BuscaAsync(string entry, DbSchema schema) =>
        FlowExtractor.ExtractAsync(Load(), entry, new ExtractionOptions { BaseDirectory = FixtureWorkspace.FixturesRoot, Schema = schema });

    [Fact] // EXT-DBF-026
    public async Task LikeComCoringaNoComeco_ComIndiceTrigramaNaColuna_NaoEAchado()
    {
        foreach (var method in new[] { "gin", "gist" })
            Assert.Empty(Of(await BuscaAsync("BuscaRepository.Buscar", Esquema(method, "gin_trgm_ops")), FindingKind.LeadingWildcardLike));

        // Contains com ToLower na coluna e "%" na frente: o mesmo membro (Titulo), também atendido.
        Assert.Empty(Of(await BuscaAsync("BuscaRepository.BuscarNoMeio", Esquema("gin", "gin_trgm_ops")), FindingKind.LeadingWildcardLike));
    }

    [Fact] // EXT-DBF-027
    public async Task LikeComCoringaNoComeco_SemTrigramaNaColuna_ContinuaAchado()
    {
        // Índice comum (btree) na coluna, ou trigrama em outra coluna: não atende.
        Assert.Single(Of(await BuscaAsync("BuscaRepository.Buscar", Esquema(null)), FindingKind.LeadingWildcardLike));
        Assert.Single(Of(await BuscaAsync("BuscaRepository.Buscar", Esquema("gin", "jsonb_ops")), FindingKind.LeadingWildcardLike));
        Assert.Single(Of(await BuscaAsync("BuscaRepository.BuscarOutraColuna", Esquema("gin", "gin_trgm_ops")), FindingKind.LeadingWildcardLike));
        // Sem o esquema do repositório: nada afirma o índice.
        Assert.Single(Of(await GraphAsync("BuscaRepository.Buscar"), FindingKind.LeadingWildcardLike));
    }

    [Fact] // EXT-DBF-028
    public async Task MembroHerdadoChamadoPeloReceptorDerivado_FiltraPelosTiposQueImplementamOReceptor()
    {
        var graph = await GraphAsync("PaiService.PeloReceptorDerivado");

        // EfRepository-like (BaseRepo<Tarefa>) não implementa IPaiRepo e sai; PaiRepo reimplementa com "new".
        Assert.DoesNotContain(graph.Nodes, n => n.Kind == NodeKind.Unresolved);
        var all = Assert.Single(graph.Nodes, n => n.Label == "PaiRepo.GetAllAsync");
        Assert.Equal("Tarefas", Assert.Single(all.Db!).Target);
        Assert.DoesNotContain(graph.Nodes, n => n.Label == "BaseRepo.GetAllAsync");
    }

    [Fact] // EXT-DBF-029
    public async Task MembroHerdadoChamadoPelaInterfaceBaseDireto_ContinuaComMaisDeUmaImplementacao()
    {
        var graph = await GraphAsync("PaiService.PelaBaseDireto");

        var unresolved = Assert.Single(graph.Nodes, n => n.Kind == NodeKind.Unresolved);
        Assert.Equal(UnresolvedReason.MultipleImplementations, unresolved.Reason);
    }
}
