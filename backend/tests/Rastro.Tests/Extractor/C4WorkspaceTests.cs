using Rastro.Core.Architecture;
using Rastro.Core.Prompts;
using Rastro.Extractor;

namespace Rastro.Tests.Extractor;

// Cenários EXT-C4-NNN: Structurizr DSL gerado do fluxo da fixture Alerts (teste golden).
// Para regravar o esperado depois de uma mudança intencional: RASTRO_UPDATE_GOLDEN=1 dotnet test.
public class C4WorkspaceTests
{
    private static readonly string GoldenPath = Path.Combine(TestPaths.BackendRoot(), "tests", "Rastro.Tests", "Fixtures", "c4-alerts.golden.dsl");

    private static async Task<string> DslAsync(IReadOnlyList<C4Person> people)
    {
        var graph = await FlowExtractor.ExtractAsync(FixtureWorkspace.Load("Alerts"), "AlertJob.ExecuteAsync",
            new ExtractionOptions { BaseDirectory = FixtureWorkspace.FixturesRoot });
        return C4WorkspaceBuilder.Build(new PromptContext("fixture", "Fixture.slnx", "AlertJob.ExecuteAsync", "0123456789"), graph, people);
    }

    [Fact] // EXT-C4-001
    public async Task Dsl_DaFixtureAlerts_IgualAoGolden()
    {
        var dsl = (await DslAsync([new C4Person("Operador", "Acompanha os alertas enviados.")])).ReplaceLineEndings("\n");

        if (Environment.GetEnvironmentVariable("RASTRO_UPDATE_GOLDEN") == "1" || !File.Exists(GoldenPath))
        {
            await File.WriteAllTextAsync(GoldenPath, dsl);
            Assert.Fail($"Golden gravado em {GoldenPath}. Confira o conteúdo e rode de novo.");
        }

        Assert.Equal((await File.ReadAllTextAsync(GoldenPath)).ReplaceLineEndings("\n"), dsl);
    }

    [Fact] // EXT-C4-002
    public async Task Dsl_TemOsQuatroNiveis_ESemPessoaNaoInventaNinguem()
    {
        var dsl = await DslAsync([]);

        Assert.Contains("systemContext sistema \"C1-contexto\"", dsl);
        Assert.Contains("container sistema \"C2-containers\"", dsl);
        Assert.Contains("component sistema.cont1 \"C3-cont1\"", dsl);
        Assert.Contains("dynamic sistema.cont1 \"C4-fluxo\"", dsl);
        Assert.DoesNotContain("= person", dsl);
    }
}
