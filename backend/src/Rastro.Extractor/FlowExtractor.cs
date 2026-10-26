using Microsoft.CodeAnalysis;
using Rastro.Core.Graph;

namespace Rastro.Extractor;

// Porta de entrada do extrator: solution + "Tipo.Metodo" -> grafo do fluxo.
public static class FlowExtractor
{
    public static async Task<FlowGraph> ExtractAsync(
        Solution solution,
        string entryPoint,
        ExtractionOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new ExtractionOptions();

        var entry = await EntryPointResolver.ResolveAsync(solution, entryPoint, ct);
        var di = await DiRegistrationIndex.BuildAsync(solution, ct);
        var baseDirectory = options.BaseDirectory ?? Path.GetDirectoryName(solution.FilePath) ?? string.Empty;

        var builder = new GraphBuilder();
        var walker = new FlowWalker(solution, builder, di, options, baseDirectory);
        var entryNodeId = await walker.WalkMethodAsync(entry, depth: 0, isEntry: true, ct);

        var graph = builder.Build(entryPoint, entryNodeId);
        return await QualityInspector.InspectAsync(graph, walker, ct);
    }
}
