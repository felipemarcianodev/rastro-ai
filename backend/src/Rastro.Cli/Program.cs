using System.Runtime.CompilerServices;
using Microsoft.Build.Locator;
using Rastro.Core;
using Rastro.Core.Architecture;
using Rastro.Core.Graph;
using Rastro.Core.Prompts;
using Rastro.Extractor;

// O MSBuildLocator precisa rodar antes de qualquer tipo do MSBuild ser carregado,
// por isso a análise fica num método separado e sem inlining.
return args switch
{
    ["--version"] => PrintVersion(),
    ["analyze", var solution, var entryPoint, .. var rest] => await Analyze(solution, entryPoint, rest),
    ["c4", var graphFile, .. var rest] => await ExportC4(graphFile, rest),
    [] => PrintUsage(0),
    _ => PrintUsage(1)
};

static int PrintVersion()
{
    Console.WriteLine($"rastro {RastroVersion.Current} (schema v{RastroSchema.Version})");
    return 0;
}

static int PrintUsage(int exitCode)
{
    Console.Error.WriteLine($"rastro {RastroVersion.Current}");
    Console.Error.WriteLine("Uso:");
    Console.Error.WriteLine("  rastro --version");
    Console.Error.WriteLine("  rastro analyze <solution|csproj> <Tipo.Metodo> [--out arquivo.json] [--depth N]");
    Console.Error.WriteLine("  rastro c4 <fluxo.json> [--out workspace.dsl] [--name repositorio]");
    return exitCode;
}

static async Task<int> Analyze(string solutionPath, string entryPoint, string[] rest)
{
    string? outPath = null;
    var depth = new ExtractionOptions().MaxDepth;
    for (var i = 0; i < rest.Length; i++)
    {
        switch (rest[i])
        {
            case "--out" when i + 1 < rest.Length: outPath = rest[++i]; break;
            case "--depth" when i + 1 < rest.Length && int.TryParse(rest[i + 1], out var d) && d > 0: depth = d; i++; break;
            default: return PrintUsage(1);
        }
    }

    if (!MSBuildLocator.IsRegistered) MSBuildLocator.RegisterDefaults();
    return await RunAnalysis(Path.GetFullPath(solutionPath), entryPoint, outPath, depth);
}

// Diagramas C4 (Structurizr DSL) a partir do JSON de um fluxo já levantado.
static async Task<int> ExportC4(string graphFile, string[] rest)
{
    string? outPath = null;
    var name = "repositorio";
    for (var i = 0; i < rest.Length; i++)
    {
        switch (rest[i])
        {
            case "--out" when i + 1 < rest.Length: outPath = rest[++i]; break;
            case "--name" when i + 1 < rest.Length: name = rest[++i]; break;
            default: return PrintUsage(1);
        }
    }
    if (!File.Exists(graphFile))
    {
        Console.Error.WriteLine($"erro: {graphFile} não existe.");
        return 2;
    }

    var graph = FlowGraphJson.Deserialize(await File.ReadAllTextAsync(graphFile));
    var dsl = C4WorkspaceBuilder.Build(new PromptContext(name, Path.GetFileName(graphFile), graph.EntryPoint, null), graph, []);
    if (outPath is null) Console.WriteLine(dsl);
    else await File.WriteAllTextAsync(outPath, dsl);
    return 0;
}

[MethodImpl(MethodImplOptions.NoInlining)]
static async Task<int> RunAnalysis(string solutionPath, string entryPoint, string? outPath, int depth)
{
    try
    {
        using var loaded = await SolutionLoader.LoadAsync(solutionPath);
        foreach (var warning in loaded.Warnings.Take(10))
            Console.Error.WriteLine($"aviso: {warning}");

        var graph = await FlowExtractor.ExtractAsync(loaded.Solution, entryPoint, new ExtractionOptions { MaxDepth = depth });
        var json = FlowGraphJson.Serialize(graph);

        if (outPath is null) Console.WriteLine(json);
        else await File.WriteAllTextAsync(outPath, json);

        var unresolved = graph.Nodes.Count(n => n.Kind == NodeKind.Unresolved);
        Console.Error.WriteLine($"{graph.Nodes.Count} nós, {graph.Edges.Count} arestas, {unresolved} não resolvidos.");
        return 0;
    }
    catch (Exception ex) when (ex is FlowEntryNotFoundException or FileNotFoundException)
    {
        Console.Error.WriteLine($"erro: {ex.Message}");
        return 2;
    }
}
