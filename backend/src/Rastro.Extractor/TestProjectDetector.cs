using Microsoft.CodeAnalysis;

namespace Rastro.Extractor;

// Projeto de teste: referencia um framework de teste ou tem nome de projeto de teste.
internal static class TestProjectDetector
{
    private static readonly string[] TestFrameworkAssemblies = ["xunit", "nunit", "MSTest", "Microsoft.VisualStudio.TestPlatform", "TUnit"];

    private static readonly string[] TestNameSuffixes = [".Tests", ".Test", ".UnitTests", ".IntegrationTests", ".Specs"];

    public static bool IsTestProject(Project project) =>
        TestNameSuffixes.Any(s => project.Name.EndsWith(s, StringComparison.OrdinalIgnoreCase))
        || project.MetadataReferences
            .OfType<PortableExecutableReference>()
            .Select(r => Path.GetFileName(r.FilePath ?? string.Empty))
            .Any(file => TestFrameworkAssemblies.Any(f => file.StartsWith(f, StringComparison.OrdinalIgnoreCase)));
}
