using System.Reflection;
using System.Xml.Linq;

namespace Rastro.Tests;

// ADR-001: o núcleo funciona sem IA. Rastro.Core e Rastro.Extractor nunca referenciam
// Rastro.Ai, Rastro.Mcp nem SDKs de provedores de IA. Este teste não pode ser desligado.
public class ArchitectureTests
{
    private static readonly string[] ForbiddenProjectPrefixes = ["Rastro.Ai", "Rastro.Mcp"];

    private static readonly string[] ForbiddenPackagePrefixes =
    [
        "Anthropic", "OpenAI", "Azure.AI.OpenAI", "Google.GenerativeAI",
        "Google.Cloud.AIPlatform", "Mscc.GenerativeAI", "ModelContextProtocol"
    ];

    public static TheoryData<string> CoreProjects => new() { "Rastro.Core", "Rastro.Extractor" };

    [Theory]
    [MemberData(nameof(CoreProjects))]
    public void ProjetoDoNucleo_Csproj_NaoReferenciaIa(string project)
    {
        var csproj = XDocument.Load(Path.Combine(TestPaths.BackendRoot(), "src", project, $"{project}.csproj"));

        var projectRefs = csproj.Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension((string?)e.Attribute("Include") ?? string.Empty));
        var packageRefs = csproj.Descendants("PackageReference")
            .Select(e => (string?)e.Attribute("Include") ?? string.Empty);

        Assert.DoesNotContain(projectRefs, r => StartsWithAny(r, ForbiddenProjectPrefixes));
        Assert.DoesNotContain(packageRefs, r => StartsWithAny(r, ForbiddenPackagePrefixes));
    }

    [Theory]
    [MemberData(nameof(CoreProjects))]
    public void ProjetoDoNucleo_Assembly_NaoCarregaIa(string project)
    {
        var references = Assembly.Load(project).GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);
        string[] forbidden = [.. ForbiddenProjectPrefixes, .. ForbiddenPackagePrefixes];

        Assert.DoesNotContain(references, r => StartsWithAny(r, forbidden));
    }

    // O modo provedor fica isolado: nem a infraestrutura nem o modo agente dependem do Rastro.Ai.
    [Theory]
    [InlineData("Rastro.Infrastructure")]
    [InlineData("Rastro.Mcp")]
    public void InfraestruturaEModoAgente_NaoDependemDoModoProvedor(string project)
    {
        var csproj = XDocument.Load(Path.Combine(TestPaths.BackendRoot(), "src", project, $"{project}.csproj"));

        Assert.DoesNotContain(csproj.Descendants("ProjectReference"),
            e => Path.GetFileNameWithoutExtension((string?)e.Attribute("Include") ?? string.Empty) == "Rastro.Ai");
    }

    private static bool StartsWithAny(string value, IEnumerable<string> prefixes) =>
        prefixes.Any(p => value.StartsWith(p, StringComparison.OrdinalIgnoreCase));
}
