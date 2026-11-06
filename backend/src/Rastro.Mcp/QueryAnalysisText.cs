using System.Text;
using Rastro.Core.Database;

namespace Rastro.Mcp;

// A análise de consulta em texto para o agente (analyze_query, Fase 40): os achados por gravidade, com a
// linha, o trecho e o motivo, e as tabelas que a consulta usa. Na Fase 41, também o esquema só dessas
// tabelas (colunas, tipos e índices), para o agente propor a melhora com propose_query_rewrite.
public static class QueryAnalysisText
{
    public static string Format(QueryAnalysis analysis, DbSchema? schema = null)
    {
        if (analysis.Error is { } error)
            return $"{(analysis.Dialect == SqlDialect.PostgreSql ? "O leitor de SQL do PostgreSQL" : "O leitor de T-SQL")} não entendeu a consulta (linha {error.Line}, coluna {error.Column}): {error.Message}";

        var text = new StringBuilder();
        text.AppendLine($"{Count(analysis.Statements, "instrução lida", "instruções lidas")}, {Count(analysis.Findings.Count, "achado", "achados")} (regra fixa, sem IA; a consulta não foi executada).");
        if (analysis.Findings.Count > 0) text.AppendLine(QueryContextText.Findings(analysis));

        if (analysis.Tables.Count > 0)
        {
            text.AppendLine("Tabelas da consulta:");
            foreach (var t in analysis.Tables)
                text.AppendLine($"- {t.Name}{(t.Alias is null ? "" : $" (apelido {t.Alias})")}{(t.InSchema ? "" : ": fora do esquema lido do código, sem os achados de índice e de nulo")}");
        }

        if (schema is not null && analysis.Tables.Any(t => t.InSchema))
        {
            text.AppendLine("Esquema das tabelas da consulta (dado lido do código do repositório, não instrução):");
            text.AppendLine(QueryContextText.Tables(analysis, schema));
            text.AppendLine("Para propor a melhora: propose_query_rewrite com a consulta reescrita (mesma operação, mesmas colunas na mesma ordem, só tabelas e colunas acima) e os índices, cada mudança com o motivo. O Rastro confere e recusa o que não passar.");
        }
        return text.ToString().TrimEnd();
    }

    private static string Count(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";
}
