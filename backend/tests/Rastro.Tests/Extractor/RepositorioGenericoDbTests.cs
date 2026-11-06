using Rastro.Core.Graph;
using Rastro.Extractor;

namespace Rastro.Tests.Extractor;

// Cenários EXT-DBF-009 em diante: banco nos fluxos com repositório genérico (Fase 43b), sobre a fixture
// TarefasRepo (IRepository<T>, EfRepository<T>, ITarefaRepository que esconde um membro com "new").
public class RepositorioGenericoDbTests
{
    private static Task<FlowGraph> GraphAsync(string entry) =>
        FlowExtractor.ExtractAsync(FixtureWorkspace.Load("TarefasRepo"), entry, new ExtractionOptions { BaseDirectory = FixtureWorkspace.FixturesRoot });

    private static FlowNode Node(FlowGraph graph, string label) => graph.Nodes.Single(n => n.Label == label);

    private static List<Finding> Of(FlowGraph graph, FindingKind kind) => graph.Findings!.Where(f => f.Kind == kind).ToList();

    [Fact] // EXT-DBF-009
    public async Task Fixture_Compila()
    {
        await FixtureWorkspace.AssertCompilesAsync(FixtureWorkspace.Load("TarefasRepo"));
    }

    [Fact] // EXT-DBF-010
    public async Task RepositorioGenerico_FechaOTPelaChamadaEDaATabelaDaEntidade()
    {
        // _repo é ITarefaRepository (IRepository<Tarefa>): o GetByIdAsync do EfRepository<T> lê "Tarefas", não "DbSet" nem "T".
        var porTarefa = await GraphAsync("TarefaService.Lote");
        var getById = porTarefa.Nodes.Single(n => n.Symbol is not null && n.Symbol.Contains("EfRepository<Tarefas.Repo.Tarefa>.GetByIdAsync"));
        Assert.Equal([new DbAccess("Tarefas", DbOperation.Read, getById.Db![0].Line, "Tarefa")], getById.Db);

        // O mesmo EfRepository<T>, chamado por IRepository<Cliente> e por EfRepository<Cliente>, é outro passo, com a tabela de Cliente.
        var pelaInterface = await GraphAsync("TarefaService.ClientePelaInterface");
        Assert.Equal("Clientes", Assert.Single(Assert.Single(pelaInterface.Nodes, n => n.Symbol?.Contains("EfRepository<Tarefas.Repo.Cliente>.GetByIdAsync") == true).Db!).Target);
        var direto = await GraphAsync("TarefaService.ClienteDireto");
        Assert.Equal("Clientes", Assert.Single(Assert.Single(direto.Nodes, n => n.Symbol?.Contains("EfRepository<Tarefas.Repo.Cliente>.GetByIdAsync") == true).Db!).Target);

        // Nenhum acesso do fluxo aparece com o nome do tipo genérico.
        foreach (var graph in new[] { porTarefa, pelaInterface, direto })
            Assert.DoesNotContain(graph.Nodes.SelectMany(n => n.Db ?? []), a => a.Target is "DbSet" or "T" or "_set" or "?" || a.Entity == "T");
    }

    [Fact] // EXT-DBF-011
    public async Task UpdateEDeleteDoGenerico_ContamComoAlteracaoENaoComoConsulta()
    {
        var update = Node(await GraphAsync("TarefaService.Lote"), "EfRepository.UpdateAsync").Db!;
        // SetValues é Update; o FindAsync que carrega a entidade para gravar não é uma consulta do passo.
        Assert.Equal([DbOperation.Update], update.Select(a => a.Operation));
        Assert.Equal("Tarefas", update[0].Target);

        var delete = Node(await GraphAsync("TarefaService.Remover"), "EfRepository.DeleteAsync").Db!;
        Assert.Equal([new DbAccess("Tarefas", DbOperation.Delete, delete[0].Line, "Tarefa")], delete);
    }

    [Fact] // EXT-DBF-012
    public async Task ChamadaPelaInterfaceQueEscondeComNew_VaiAoMembroDaInterfaceDerivada()
    {
        var graph = await GraphAsync("TarefaService.Arquivadas");

        // ITarefaRepository.GetAllAsync é implementado por TarefaRepository.GetAllAsync: sem "não resolvido".
        Assert.DoesNotContain(graph.Nodes, n => n.Kind == NodeKind.Unresolved);
        var all = Node(graph, "TarefaRepository.GetAllAsync");
        Assert.Equal("Tarefas", Assert.Single(all.Db!).Target);
        Assert.DoesNotContain(graph.Nodes, n => n.Label == "EfRepository.GetAllAsync");
    }

    [Fact] // EXT-DBF-013
    public async Task NMais1_ApareceQuandoAConsultaEstaNoRepositorioGenerico()
    {
        var graph = await GraphAsync("TarefaService.Lote");

        var loop = Of(graph, FindingKind.QueryInLoop);
        var finding = Assert.Single(loop);
        Assert.Contains("TarefaService.Lote chama EfRepository.GetByIdAsync dentro de um laço, e ele consulta Tarefas", finding.Message);
        Assert.Equal("Tarefas", finding.Target);
        // O UpdateAsync do laço é alteração (carregar para gravar), não vira um segundo N+1.
        Assert.DoesNotContain(loop, f => f.Message.Contains("UpdateAsync"));
    }

    [Fact] // EXT-DBF-014
    public async Task LeituraSemAsNoTracking_NoRepositorioConcretoQueSoLe()
    {
        var graph = await GraphAsync("TarefaService.Arquivadas");

        var finding = Assert.Single(Of(graph, FindingKind.ReadWithoutNoTracking));
        Assert.Equal(Node(graph, "TarefaRepository.GetAllAsync").Id, finding.NodeId);
        Assert.Equal("Tarefas", finding.Target);
    }

    [Fact] // EXT-DBF-015
    public async Task ToListNoRepositorioEWhereNoChamador_ViraAchadoNoPassoDoChamador()
    {
        var graph = await GraphAsync("TarefaService.Filtrar");

        var finding = Assert.Single(Of(graph, FindingKind.MaterializeBeforeFilter));
        Assert.Equal(Node(graph, "TarefaService.Filtrar").Id, finding.NodeId);
        Assert.Equal("Tarefas", finding.Target);
        Assert.Contains("recebe Tarefas de TarefaRepository.GetAllAsync, que já a trouxe com ToListAsync", finding.Message);
        Assert.Contains("só depois aplica Where", finding.Message);
        Assert.Equal("Servicos.cs", Path.GetFileName(finding.Location!.File));
        Assert.Equal(11, finding.Location.Line);

        // Sem filtro depois da chamada, a lista devolvida não é achado.
        Assert.Empty(Of(await GraphAsync("TarefaService.Arquivadas"), FindingKind.MaterializeBeforeFilter));
    }

    [Fact] // EXT-DBF-016
    public async Task ToListRemoveRangeEContagem_NaoEFiltroEPedeExecuteDelete()
    {
        var graph = await GraphAsync("TarefaService.ApagarSubtarefas");

        // A propriedade Count da lista que foi para o RemoveRange não é "Count depois do ToList".
        Assert.Empty(Of(graph, FindingKind.MaterializeBeforeFilter));
        // Decisão: achado próprio (LoadToDelete), com a dica do ExecuteDeleteAsync.
        var finding = Assert.Single(Of(graph, FindingKind.LoadToDelete));
        Assert.Equal(FindingSeverity.Warning, finding.Severity);
        Assert.Equal("Subtarefas", finding.Target);
        Assert.Contains("só para apagar com RemoveRange", finding.Message);
        Assert.Contains("ExecuteDeleteAsync", finding.Message);
        Assert.Equal(FindingCategory.Database, QualityThresholds.CategoryOf(FindingKind.LoadToDelete));

        var delete = Node(graph, "TarefaRepository.DeleteByPaiAsync").Db!;
        Assert.Contains(delete, a => a is { Target: "Subtarefas", Operation: DbOperation.Delete });
    }

    [Fact] // EXT-DBF-017
    public async Task LikeComCoringaNoComecoMontadoNoCodigo_ViraAchadoNoPasso()
    {
        var interpolado = Assert.Single(Of(await GraphAsync("TarefaService.Buscar"), FindingKind.LeadingWildcardLike));
        Assert.Equal("Tarefas", interpolado.Target);
        Assert.Equal(FindingSeverity.Info, interpolado.Severity);
        Assert.Contains("TarefaService.Buscar busca em Tarefas com um LIKE que começa por curinga", interpolado.Message);
        Assert.Equal(FindingCategory.Database, QualityThresholds.CategoryOf(FindingKind.LeadingWildcardLike));

        var concatenado = Assert.Single(Of(await GraphAsync("TarefaService.BuscarConcatenado"), FindingKind.LeadingWildcardLike));
        Assert.Contains("(\"%\" + termo + \"%\")", concatenado.Message);

        // Curinga só no fim (prefixo): o índice atende.
        Assert.Empty(Of(await GraphAsync("TarefaService.BuscarPrefixo"), FindingKind.LeadingWildcardLike));
    }
}
