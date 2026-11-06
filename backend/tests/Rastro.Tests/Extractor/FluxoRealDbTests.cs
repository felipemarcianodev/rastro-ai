using Microsoft.CodeAnalysis;
using Rastro.Core.Graph;
using Rastro.Extractor;

namespace Rastro.Tests.Extractor;

// Cenários EXT-DBF-030 em diante: a forma real da lista em variável com filtro em memória, a consulta que já
// limita no banco e o N+1 de escrita (Fase 43d), sobre a fixture TarefasMulti (Fluxo43d.cs).
public class FluxoRealDbTests
{
    private static Solution Load() => FixtureWorkspace.LoadLayered("TarefasMulti", "TarefasMulti.Infra");

    private static Task<FlowGraph> GraphAsync(string entry) =>
        FlowExtractor.ExtractAsync(Load(), entry, new ExtractionOptions { BaseDirectory = FixtureWorkspace.FixturesRoot });

    private static List<Finding> Of(FlowGraph graph, FindingKind kind) => graph.Findings!.Where(f => f.Kind == kind).ToList();

    [Fact] // EXT-DBF-030
    public async Task ListaEmVariavelAtribuidaEmRamosEFiltradaPorReatribuicao_EAchadoUmaVez()
    {
        var graph = await GraphAsync("FiltroService.Filtrar");

        // Três ramos atribuem a mesma variável, e vários Where em memória vêm depois (reatribuídos e encadeados):
        // um achado só, no primeiro filtro.
        var finding = Assert.Single(Of(graph, FindingKind.MaterializeBeforeFilter));
        Assert.Equal("Itens", finding.Target);
        Assert.Contains("só depois aplica Where", finding.Message);
        Assert.Contains("ListagemRepository.", finding.Message);
        Assert.Contains("e este já a trouxe com ToListAsync", finding.Message);
    }

    [Fact] // EXT-DBF-031
    public async Task ListaDeUmCondicionalComAwaitENoFiltroSeguinte_ApontaOPrimeiroFiltro()
    {
        var graph = await GraphAsync("FiltroService.ComCondicional");

        var finding = Assert.Single(Of(graph, FindingKind.MaterializeBeforeFilter));
        Assert.Equal("Itens", finding.Target);
        Assert.Contains("só depois aplica OrderBy", finding.Message);
    }

    [Fact] // EXT-DBF-032
    public async Task ListaSoContadaOuDevolvidaComoVeio_NaoEAchado()
    {
        Assert.Empty(Of(await GraphAsync("FiltroService.SoContagem"), FindingKind.MaterializeBeforeFilter));
        Assert.Empty(Of(await GraphAsync("FiltroService.SemFiltro"), FindingKind.MaterializeBeforeFilter));
    }

    [Theory] // EXT-DBF-033
    [InlineData("BuscaGlobalService.Buscar")]
    [InlineData("BuscaGlobalService.BuscarPaginado")]
    public async Task RepositorioQueJaFiltraELimitaNoBanco_OFiltroEmMemoriaNaoEAchado(string entry)
    {
        var graph = await GraphAsync(entry);

        Assert.Empty(Of(graph, FindingKind.MaterializeBeforeFilter));
        Assert.Contains(graph.Nodes, n => n.Label == "EspacoRepository." + (entry.EndsWith("Paginado", StringComparison.Ordinal) ? "PaginaAsync" : "BuscarPorNomeAsync"));
    }

    [Theory] // EXT-DBF-034
    [InlineData("BuscaGlobalService.BuscarSemLimite")]
    [InlineData("BuscaGlobalService.LimiteSemWhere")]
    public async Task RepositorioQueSoFiltraOuSoLimita_OFiltroEmMemoriaContinuaAchado(string entry)
    {
        var finding = Assert.Single(Of(await GraphAsync(entry), FindingKind.MaterializeBeforeFilter));
        Assert.Equal("Espacos", finding.Target);
    }

    [Fact] // EXT-DBF-035
    public async Task NMaisUmDeEscrita_PegaSqlCruExecuteUpdateEServicoFinoComSqlInterpolado()
    {
        var graph = await GraphAsync("LoteService.Apagar");

        var findings = Of(graph, FindingKind.QueryInLoop);
        Assert.Equal(3, findings.Count);
        Assert.All(findings, f => Assert.Contains("(N+1 de escrita)", f.Message));
        Assert.All(findings, f => Assert.Equal(graph.Nodes.Single(n => n.Kind == NodeKind.Entry).Id, f.NodeId));

        // SQL cru verbatim: a tabela sai do texto do UPDATE.
        var raw = Assert.Single(findings, f => f.Target == "itens");
        Assert.Contains("chama ReferenciaService.RemoverReferenciasAsync dentro de um laço", raw.Message);
        Assert.Contains("grava em itens com ExecuteSqlInterpolatedAsync", raw.Message);

        var update = Assert.Single(findings, f => f.Target == "Diarios");
        Assert.Contains("chama DiarioRepository.LimparReferenciaAsync dentro de um laço", update.Message);
        Assert.Contains("grava em Diarios com ExecuteUpdateAsync", update.Message);

        // Serviço fino no meio: ContadorService repassa ao repositório que grava com SQL interpolado.
        var counter = Assert.Single(findings, f => f.Target == "espacos");
        Assert.Contains("chama ContadorService.Incrementar dentro de um laço, e ele chega a ContadorRepository.IncrementarContadorAsync, que grava em espacos", counter.Message);
    }

    [Fact] // EXT-DBF-036
    public async Task NMaisUmDeEscrita_ExecuteDeleteNoRepositorioGenericoFechadoPelaInterface()
    {
        var graph = await GraphAsync("LoteService.ApagarComentarios");

        var finding = Assert.Single(Of(graph, FindingKind.QueryInLoop));
        Assert.Equal("Comentario", finding.Target);
        Assert.Contains("chama EfApagador.ApagarAsync dentro de um laço", finding.Message);
        Assert.Contains("grava em Comentario com ExecuteDeleteAsync", finding.Message);
    }

    [Theory] // EXT-DBF-037
    [InlineData("LoteService.AtualizarRastreados")]
    [InlineData("LoteService.ApagarUm")]
    [InlineData("LoteService.LacoSoComIds")]
    public async Task GravacaoSemLacoOuSoRastreada_NaoViraNMaisUmDeEscrita(string entry)
    {
        Assert.Empty(Of(await GraphAsync(entry), FindingKind.QueryInLoop));
    }
}
