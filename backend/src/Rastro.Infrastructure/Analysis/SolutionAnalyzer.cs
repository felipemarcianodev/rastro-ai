using Rastro.Core.Graph;
using Rastro.Extractor;
using Rastro.Infrastructure.Database;
using Rastro.Infrastructure.Repositories;

namespace Rastro.Infrastructure.Analysis;

public interface ISolutionAnalyzer
{
    Task<FlowGraph> AnalyzeAsync(ResolvedTarget target, string entryPoint, int maxDepth, CancellationToken ct);
}

// Análise de um levantamento: sempre com o código atual (sem cache). O esquema do banco do repositório
// (Fase 39) dá o nome das tabelas que cada passo lê e grava.
public class SolutionAnalyzer(ISolutionSource solutions, DbSchemaService? schemas = null) : ISolutionAnalyzer
{
    public async Task<FlowGraph> AnalyzeAsync(ResolvedTarget target, string entryPoint, int maxDepth, CancellationToken ct)
    {
        var schema = schemas is null ? null : await schemas.ReadAsync(target.RepositoryName, ct);
        var handle = await solutions.OpenAsync(target, fresh: true, ct);
        try
        {
            return await FlowExtractor.ExtractAsync(handle.Solution, entryPoint,
                new ExtractionOptions { MaxDepth = maxDepth, BaseDirectory = handle.BaseDirectory, Schema = schema }, ct);
        }
        catch (FlowEntryNotFoundException ex)
        {
            throw new AnalysisException(ex.Message, ex);
        }
    }
}
