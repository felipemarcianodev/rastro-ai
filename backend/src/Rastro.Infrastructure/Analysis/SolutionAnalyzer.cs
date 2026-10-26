using Rastro.Core.Graph;
using Rastro.Extractor;
using Rastro.Infrastructure.Repositories;

namespace Rastro.Infrastructure.Analysis;

public interface ISolutionAnalyzer
{
    Task<FlowGraph> AnalyzeAsync(ResolvedTarget target, string entryPoint, int maxDepth, CancellationToken ct);
}

// Análise de um levantamento: sempre com o código atual (sem cache).
public class SolutionAnalyzer(ISolutionSource solutions) : ISolutionAnalyzer
{
    public async Task<FlowGraph> AnalyzeAsync(ResolvedTarget target, string entryPoint, int maxDepth, CancellationToken ct)
    {
        var handle = await solutions.OpenAsync(target, fresh: true, ct);
        try
        {
            return await FlowExtractor.ExtractAsync(handle.Solution, entryPoint,
                new ExtractionOptions { MaxDepth = maxDepth, BaseDirectory = handle.BaseDirectory }, ct);
        }
        catch (FlowEntryNotFoundException ex)
        {
            throw new AnalysisException(ex.Message, ex);
        }
    }
}
