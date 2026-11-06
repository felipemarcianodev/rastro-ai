using Microsoft.Extensions.Logging;
using Rastro.Core.Database;
using Rastro.Extractor.Database;

namespace Rastro.Infrastructure.Database;

// O esquema e a análise sem IA de uma consulta: o que a IA recebe (só as tabelas que a consulta usa).
public record QueryProposalContext(DbSchema Schema, QueryAnalysis Analysis);

// Porta única da proposta de melhora de consulta (Fase 41), dos dois modos de IA (ADR-001): o provedor
// (Rastro.Ai) e o agente (propose_query_rewrite no Rastro.Mcp) entregam a proposta aqui, e a conferência
// do núcleo decide o que aparece. Sem IA nenhuma aqui. Nada é aplicado nem executado. O texto da
// consulta nunca vai para o log (pode ter dado do cliente num literal).
public class QueryProposalService(DbSchemaService schemas, ILogger<QueryProposalService> logger)
{
    // Nulo quando o repositório não está cadastrado.
    public async Task<QueryProposalContext?> ContextAsync(string repositoryName, string sql, CancellationToken ct)
    {
        var schema = await schemas.ReadAsync(repositoryName, ct);
        if (schema is null) return null;
        var analyzer = SqlQueryAnalyzers.For(schema.Dialect) ?? new TSqlQueryAnalyzer();
        return new QueryProposalContext(schema, analyzer.Analyze(sql, schema));
    }

    public QueryProposalReview Review(string repositoryName, QueryProposalContext context, string sql, QueryProposal proposal)
    {
        var checker = SqlProposalCheckers.For(context.Schema.Dialect) ?? new TSqlProposalChecker();
        var review = checker.Review(sql, proposal, context.Schema);
        var rewrite = review.Rewrite is null ? "nenhuma" : review.Rewrite.Accepted ? "aceita" : "recusada";
        logger.LogInformation("Proposta de melhora de consulta conferida no repositório {Repository} ({Origin}): reescrita {Rewrite}, {AcceptedIndexes} de {Indexes} índices aceitos.",
            repositoryName, proposal.Origin, rewrite, review.Indexes.Count(i => i.Accepted), review.Indexes.Count);
        return review;
    }

    // Nulo quando o repositório não está cadastrado.
    public async Task<QueryProposalReview?> ReviewAsync(string repositoryName, string sql, QueryProposal proposal, CancellationToken ct) =>
        await ContextAsync(repositoryName, sql, ct) is { } context ? Review(repositoryName, context, sql, proposal) : null;
}
