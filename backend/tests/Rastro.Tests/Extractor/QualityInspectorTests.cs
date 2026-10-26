using Rastro.Core.Graph;
using Rastro.Core.Prompts;
using Rastro.Extractor;

namespace Rastro.Tests.Extractor;

// Cenários EXT-QUA-NNN: métricas e achados de qualidade e DDD (Fase 10) sobre a fixture Quality.
public class QualityInspectorTests
{
    private static async Task<FlowGraph> GraphAsync() =>
        await FlowExtractor.ExtractAsync(FixtureWorkspace.Load("Quality"), "PedidoAppService.AprovarAsync",
            new ExtractionOptions { BaseDirectory = FixtureWorkspace.FixturesRoot });

    private static IReadOnlyList<Finding> Of(FlowGraph graph, FindingKind kind) => graph.Findings!.Where(f => f.Kind == kind).ToList();

    [Fact] // EXT-QUA-001
    public async Task Metricas_DoMetodoComplexo_ContamDecisoesAninhamentoEDependencias()
    {
        var graph = await GraphAsync();

        var classificar = graph.Nodes.Single(n => n.Label == "PedidoAppService.Classificar").Metrics!;
        // if, &&, if, ||, for, if, while, if, 3 braços de switch (o descarte não conta), catch = 12 + 1
        Assert.Equal(13, classificar.Cyclomatic);
        Assert.Equal(5, classificar.MaxNesting);
        // Só as interfaces e serviços injetados contam (8).
        Assert.Equal(8, classificar.Dependencies);
        Assert.Contains(Of(graph, FindingKind.HighComplexity), f => f.Message.Contains("complexidade ciclomática 13"));
        Assert.Single(Of(graph, FindingKind.DeepNesting));
        Assert.Single(Of(graph, FindingKind.TooManyDependencies));
    }

    [Fact] // EXT-QUA-002
    public async Task CatchVazio_ECatchQueEngoleSemUsarAExcecao_SaoApontados()
    {
        var silent = Of(await GraphAsync(), FindingKind.SilentCatch);

        Assert.Contains(silent, f => f.Severity == FindingSeverity.Critical && f.Message.StartsWith("catch (tudo) vazio"));
        Assert.Contains(silent, f => f.Severity == FindingSeverity.Warning && f.Message.Contains("catch (InvalidOperationException)"));
        Assert.All(silent, f => Assert.Equal("Quality/Application.cs", f.Location!.File));
    }

    [Fact] // EXT-QUA-003
    public async Task ServiceLocator_ForaDeJob_EAviso()
    {
        var locator = Assert.Single(Of(await GraphAsync(), FindingKind.ServiceLocator));

        Assert.Equal(FindingSeverity.Warning, locator.Severity);
        Assert.Contains("GetService", locator.Message);
    }

    [Fact] // EXT-QUA-004
    public async Task Ddd_EntidadeAlteradaEGravadaForaDoDominio_SoNaAplicacao()
    {
        var graph = await GraphAsync();

        var changed = Of(graph, FindingKind.EntityChangedOutsideDomain);
        Assert.Equal(["PedidoAppService.AprovarAsync altera Pedido.Status direto, fora da entidade e do serviço de domínio que a valida.",
            "PedidoAppService.AprovarAsync altera Pedido.Total direto, fora da entidade e do serviço de domínio que a valida."],
            changed.Select(f => f.Message).Order());
        // O serviço de domínio e o repositório também alteram ou gravam, e estão no lugar certo.
        Assert.DoesNotContain(graph.Findings!, f => f.Message.StartsWith("PedidoDomainService.") || f.Message.StartsWith("PedidoRepository."));

        var stored = Assert.Single(Of(graph, FindingKind.PersistenceOutsideDomain));
        Assert.Contains("grava Pedido com IPedidoRepository.Atualizar", stored.Message);
    }

    [Fact] // EXT-QUA-005
    public async Task SetterPublico_SoDasPropriedadesDaPropriaEntidade_UmaVez()
    {
        var setter = Assert.Single(Of(await GraphAsync(), FindingKind.PublicSetterOnEntity));

        Assert.Contains("Pedido tem setter público em 2 propriedade(s) (Status, Total)", setter.Message);
    }

    [Fact] // EXT-QUA-006
    public async Task Fixture_SemProblemas_NaoGeraAchadosDeDdd()
    {
        var graph = await FlowExtractor.ExtractAsync(FixtureWorkspace.Load("Alerts"), "AlertJob.ExecuteAsync",
            new ExtractionOptions { BaseDirectory = FixtureWorkspace.FixturesRoot });

        Assert.NotNull(graph.Findings);
        Assert.DoesNotContain(graph.Findings!, f => QualityThresholds.CategoryOf(f.Kind) == FindingCategory.Ddd);
        Assert.All(graph.Nodes.Where(n => n.Kind is NodeKind.Entry or NodeKind.Method && n.Location is not null), n => Assert.NotNull(n.Metrics));
    }

    [Fact] // EXT-QUA-007
    public async Task PromptDeRefatoracao_TemLocalCaminhoOQueFazerECriterio()
    {
        var graph = await GraphAsync();
        var finding = Of(graph, FindingKind.EntityChangedOutsideDomain).First(f => f.Message.Contains("Pedido.Status"));

        var prompt = RefactorPromptBuilder.Build(new PromptContext("repo", "App.slnx", "PedidoAppService.AprovarAsync", "abcdef123"), graph, finding);

        Assert.Contains("# Refatoração no fluxo `PedidoAppService.AprovarAsync`", prompt);
        Assert.Contains("commit `abcdef1`", prompt);
        Assert.Contains("Atenção: PedidoAppService.AprovarAsync altera Pedido.Status direto", prompt);
        Assert.Contains("- Local: `Quality/Application.cs:", prompt);
        Assert.Contains("Mova a alteração para um método da entidade", prompt);
        Assert.Contains("Nenhuma atribuição a propriedade da entidade fora dela e do domínio.", prompt);
        Assert.Contains("Ao levantar o fluxo de novo no Rastro, este achado não aparece mais.", prompt);
    }
}
