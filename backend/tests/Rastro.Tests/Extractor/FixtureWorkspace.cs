using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Rastro.Tests.Extractor;

// Carrega uma pasta de Fixtures num workspace em memória (sem MSBuild): rápido e determinístico.
// Se existir a pasta "<fixture>.Tests", ela vira um segundo projeto (de teste) que referencia o primeiro.
internal static class FixtureWorkspace
{
    private const string GlobalUsings =
        "global using System; global using System.Collections.Generic; global using System.Linq; " +
        "global using System.Net.Http; global using System.Threading.Tasks;";

    public static string FixturesRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public static Solution Load(string fixture)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => Path.GetFileName(p).StartsWith("System.", StringComparison.Ordinal) || Path.GetFileName(p) is "netstandard.dll" or "mscorlib.dll")
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();

        var solution = new AdhocWorkspace().CurrentSolution;
        var mainId = ProjectId.CreateNewId();
        solution = AddProject(solution, mainId, fixture, Path.Combine(FixturesRoot, fixture), references);

        var testsFolder = Path.Combine(FixturesRoot, fixture + ".Tests");
        if (Directory.Exists(testsFolder))
        {
            var testsId = ProjectId.CreateNewId();
            solution = AddProject(solution, testsId, fixture + ".Tests", testsFolder, references)
                .AddProjectReference(testsId, new ProjectReference(mainId));
        }

        return solution;
    }

    private static Solution AddProject(Solution solution, ProjectId id, string name, string folder, IEnumerable<MetadataReference> references)
    {
        var documents = Directory.GetFiles(folder, "*.cs")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => DocumentInfo.Create(
                DocumentId.CreateNewId(id),
                Path.GetFileName(f),
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(File.ReadAllText(f)), VersionStamp.Create(), f)),
                filePath: f))
            .Append(DocumentInfo.Create(
                DocumentId.CreateNewId(id),
                "GlobalUsings.g.cs",
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(GlobalUsings), VersionStamp.Create())),
                filePath: Path.Combine(folder, "GlobalUsings.g.cs")))
            .ToList();

        return solution.AddProject(ProjectInfo.Create(
            id, VersionStamp.Create(), name, name, LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable),
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
            documents: documents,
            metadataReferences: references));
    }

    public static async Task AssertCompilesAsync(Solution solution)
    {
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            var errors = compilation!.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.True(errors.Count == 0, $"A fixture {project.Name} não compila: " + string.Join(Environment.NewLine, errors));
        }
    }
}
