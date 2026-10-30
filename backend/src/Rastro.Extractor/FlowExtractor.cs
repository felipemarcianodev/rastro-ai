using Microsoft.CodeAnalysis;
using Rastro.Core.Graph;

namespace Rastro.Extractor;

// Chamada HTTP encontrada no percurso: o nó "h:" que a representa, o verbo e a rota e a profundidade
// do método que chama (a costura usa para saber quanto de profundidade sobra no outro sistema).
public record HttpCallSite(string NodeId, HttpCall Call, int Depth);

public record ExtractionResult(FlowGraph Graph, IReadOnlyList<HttpCallSite> HttpCalls);

// Porta de entrada do extrator: solution + "Tipo.Metodo" -> grafo do fluxo.
public static class FlowExtractor
{
    public static async Task<FlowGraph> ExtractAsync(
        Solution solution,
        string entryPoint,
        ExtractionOptions? options = null,
        CancellationToken ct = default) =>
        (await ExtractWithCallsAsync(solution, entryPoint, options, ct)).Graph;

    // Igual ao ExtractAsync, e devolve também as chamadas HTTP (só preenchidas com options.HttpCalls).
    public static async Task<ExtractionResult> ExtractWithCallsAsync(
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
        return new ExtractionResult(await QualityInspector.InspectAsync(graph, walker, ct), walker.HttpCalls);
    }
}
