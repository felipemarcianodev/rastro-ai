using Rastro.Core.Graph;
using Rastro.Core.Localization;
using Rastro.Extractor;

namespace Rastro.Tests.Extractor;

// Cenários CORE-LANG-2NN: o texto dos achados que o levantamento grava no grafo sai no idioma pedido (Fase 50,
// ADR-009), sem chave crua e sem sobra de outro idioma.
public class LanguageFindingsTests
{
    private static Task<FlowGraph> QualityAsync(UiLanguage language) =>
        FlowExtractor.ExtractAsync(FixtureWorkspace.Load("Quality"), "PedidoAppService.AprovarAsync",
            new ExtractionOptions { BaseDirectory = FixtureWorkspace.FixturesRoot, Language = language });

    private static Task<FlowGraph> DbAsync(string entry, UiLanguage language) =>
        FlowExtractor.ExtractAsync(FixtureWorkspace.Load("LojaFluxo"), entry,
            new ExtractionOptions { BaseDirectory = FixtureWorkspace.FixturesRoot, Language = language });

    private static readonly string[] RawKeyMarkers = ["Quality.", "Db.", "Flow.", "Cross."];

    private static void AssertNoRawKey(IEnumerable<string> texts)
    {
        foreach (var text in texts)
        {
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.All(RawKeyMarkers, marker => Assert.DoesNotContain(marker, text, StringComparison.Ordinal));
            Assert.DoesNotContain("{0}", text, StringComparison.Ordinal);
        }
    }

    [Theory] // CORE-LANG-201
    [InlineData(UiLanguage.English, "cyclomatic complexity 13", "Empty catch (everything)", "public setter on 2 property(ies)")]
    [InlineData(UiLanguage.Spanish, "complejidad ciclomática 13", "catch (todo) vacío", "setter público en 2 propiedad(es)")]
    [InlineData(UiLanguage.Portuguese, "complexidade ciclomática 13", "catch (tudo) vazio", "setter público em 2 propriedade(s)")]
    public async Task Qualidade_NoIdiomaPedido_TodosOsAchadosSaemTraduzidosSemChaveCrua(
        UiLanguage language, string complexity, string emptyCatch, string setter)
    {
        var graph = await QualityAsync(language);

        var messages = graph.Findings!.Select(f => f.Message).ToList();
        AssertNoRawKey(messages);
        Assert.Contains(messages, m => m.Contains(complexity, StringComparison.Ordinal));
        Assert.Contains(messages, m => m.StartsWith(emptyCatch, StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains(setter, StringComparison.Ordinal));
        // Nomes do código do cliente ficam como estão em qualquer idioma.
        Assert.Contains(messages, m => m.Contains("PedidoAppService.Classificar", StringComparison.Ordinal));
    }

    [Theory] // CORE-LANG-202
    [InlineData(UiLanguage.English)]
    [InlineData(UiLanguage.Spanish)]
    public async Task Qualidade_OutrosIdiomas_NaoTemSobraDePortugues(UiLanguage language)
    {
        var graph = await QualityAsync(language);

        string[] portuguese = ["dentro do outro", "faz coisas demais", "caminhos demais", "sem log nem tratamento", "responsabilidades demais"];
        Assert.All(graph.Findings!, f => Assert.All(portuguese, marker => Assert.DoesNotContain(marker, f.Message, StringComparison.Ordinal)));
    }

    [Theory] // CORE-LANG-203
    [InlineData(UiLanguage.English, "inside a loop: one trip to the database per iteration (N+1)", "loads Pedidos with ToListAsync and only then applies Where")]
    [InlineData(UiLanguage.Spanish, "dentro de un bucle: un viaje a la base de datos en cada vuelta (N+1)", "trae Pedidos con ToListAsync y solo después aplica Where")]
    [InlineData(UiLanguage.Portuguese, "dentro de um laço: uma ida ao banco a cada volta (N+1)", "traz Pedidos com ToListAsync e só depois aplica Where")]
    public async Task AcessoAoBanco_NoIdiomaPedido_NMais1EToListAntesDoFiltroSaemTraduzidos(
        UiLanguage language, string inLoop, string beforeFilter)
    {
        var graph = await DbAsync("PedidosController.Listar", language);

        var findings = graph.Findings!.ToList();
        AssertNoRawKey(findings.Select(f => f.Message));
        Assert.Contains(findings, f => f.Kind == FindingKind.QueryInLoop && f.Message.Contains(inLoop, StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Kind == FindingKind.MaterializeBeforeFilter && f.Message.Contains(beforeFilter, StringComparison.Ordinal));
        // Tabela e método do cliente ficam como estão; o alvo do achado não é texto traduzido.
        Assert.Contains(findings, f => f.Kind == FindingKind.QueryInLoop && f.Target == "itens_pedido"
                                       && f.Message.Contains("PedidoRepositorio.ItensAsync", StringComparison.Ordinal));
    }

    [Theory] // CORE-LANG-205
    [InlineData(UiLanguage.English, "the compiler checks neither the name nor the parameters. It does not appear in the repository .sql files.")]
    [InlineData(UiLanguage.Spanish, "el compilador no verifica el nombre ni los parámetros. No aparece en los archivos .sql del repositorio.")]
    public async Task AcessoAoBanco_ProcedureChamadaPorSqlCru_TextoNoIdiomaPedido(UiLanguage language, string expected)
    {
        var graph = await DbAsync("PedidosController.Fechar", language);

        var procedure = Assert.Single(graph.Findings!, f => f.Kind == FindingKind.ProcedureByRawSql && f.Target == "sp_baixar_estoque");
        Assert.Contains(expected, procedure.Message, StringComparison.Ordinal);
        AssertNoRawKey(graph.Findings!.Select(f => f.Message));
    }

    [Theory] // CORE-LANG-206
    [InlineData(UiLanguage.English, "depth limit")]
    [InlineData(UiLanguage.Spanish, "límite de profundidad")]
    [InlineData(UiLanguage.Portuguese, "limite de profundidade")]
    public async Task RotuloDoLimiteDeProfundidade_SaiNoIdiomaPedido(UiLanguage language, string expected)
    {
        var graph = await FlowExtractor.ExtractAsync(FixtureWorkspace.Load("Alerts"), "AlertJob.ExecuteAsync",
            new ExtractionOptions { BaseDirectory = FixtureWorkspace.FixturesRoot, MaxDepth = 1, Language = language });

        Assert.Contains(graph.Nodes, n => n.Kind == NodeKind.Unresolved && n.Reason == UnresolvedReason.DepthLimit && n.Label == expected);
    }

    [Theory] // CORE-LANG-207
    [InlineData(UiLanguage.English, "resolves a dependency with")]
    [InlineData(UiLanguage.Spanish, "resuelve una dependencia con")]
    public async Task ServiceLocator_NoIdiomaPedido(UiLanguage language, string expected)
    {
        var graph = await QualityAsync(language);

        var locator = Assert.Single(graph.Findings!, f => f.Kind == FindingKind.ServiceLocator);
        Assert.Contains(expected, locator.Message, StringComparison.Ordinal);
        AssertNoRawKey([locator.Message]);
    }
}
