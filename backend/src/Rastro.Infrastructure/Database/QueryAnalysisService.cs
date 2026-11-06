using Microsoft.Extensions.Logging;
using Rastro.Core.Database;
using Rastro.Extractor.Database;

namespace Rastro.Infrastructure.Database;

// Análise de consulta de um repositório cadastrado (Fase 40, ADR-006): os achados do leitor de SQL do
// dialeto, cruzados com o esquema lido do código. Nada vai a banco nenhum e a consulta não é executada.
// O texto da consulta nunca vai para o log (pode ter dado do cliente num literal).
// Usado pela API (POST api/repositories/{nome}/schema/analyze) e pelo MCP (analyze_query).
public class QueryAnalysisService(DbSchemaService schemas, ILogger<QueryAnalysisService> logger)
{
    // Nulo quando o repositório não está cadastrado.
    public async Task<QueryAnalysis?> AnalyzeAsync(string repositoryName, string sql, CancellationToken ct)
    {
        var schema = await schemas.ReadAsync(repositoryName, ct);
        if (schema is null) return null;

        var analyzer = SqlQueryAnalyzers.For(schema.Dialect) ?? new TSqlQueryAnalyzer();
        var analysis = analyzer.Analyze(sql, schema);
        logger.LogInformation("Consulta analisada no repositório {Repository}: {Statements} instruções, {Findings} achados, leitura com erro: {ParseError}.",
            repositoryName, analysis.Statements, analysis.Findings.Count, analysis.Error is not null);
        return analysis;
    }
}
