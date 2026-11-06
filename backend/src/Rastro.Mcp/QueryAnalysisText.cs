using System.Text;
using Rastro.Core.Database;

namespace Rastro.Mcp;

// A análise de consulta em texto para o agente (analyze_query, Fase 40): os achados por gravidade, com a
// linha, o trecho e o motivo, e as tabelas que a consulta usa.
public static class QueryAnalysisText
{
    private static readonly Dictionary<QuerySeverity, string> SeverityLabel = new()
    {
        [QuerySeverity.High] = "alta",
        [QuerySeverity.Medium] = "média",
        [QuerySeverity.Low] = "baixa"
    };

    private static readonly Dictionary<QueryFindingKind, string> KindLabel = new()
    {
        [QueryFindingKind.SelectStar] = "SELECT *",
        [QueryFindingKind.FunctionOnFilterColumn] = "função na coluna do filtro",
        [QueryFindingKind.LeadingWildcardLike] = "curinga no começo do LIKE",
        [QueryFindingKind.OrAcrossColumns] = "OR entre colunas diferentes",
        [QueryFindingKind.ColumnWithoutIndex] = "coluna sem índice no filtro ou na junção",
        [QueryFindingKind.JoinWithoutCondition] = "junção sem condição",
        [QueryFindingKind.DistinctOverJoin] = "DISTINCT sobre junção",
        [QueryFindingKind.CorrelatedSubquery] = "subconsulta correlacionada",
        [QueryFindingKind.OrderByWithoutLimit] = "ORDER BY sem limite",
        [QueryFindingKind.NotInNullable] = "NOT IN com coluna que aceita nulo",
        [QueryFindingKind.WriteWithoutWhere] = "UPDATE ou DELETE sem WHERE"
    };

    public static string Format(QueryAnalysis analysis)
    {
        if (analysis.Error is { } error)
            return $"O leitor de T-SQL não entendeu a consulta (linha {error.Line}, coluna {error.Column}): {error.Message}";

        var text = new StringBuilder();
        text.AppendLine($"{Count(analysis.Statements, "instrução lida", "instruções lidas")}, {Count(analysis.Findings.Count, "achado", "achados")} (regra fixa, sem IA; a consulta não foi executada).");
        foreach (var f in analysis.Findings)
            text.AppendLine($"- [{SeverityLabel[f.Severity]}] {KindLabel[f.Kind]}, linha {f.Line}: {f.Snippet}. {f.Message}");

        if (analysis.Tables.Count > 0)
        {
            text.AppendLine("Tabelas da consulta:");
            foreach (var t in analysis.Tables)
                text.AppendLine($"- {t.Name}{(t.Alias is null ? "" : $" (apelido {t.Alias})")}{(t.InSchema ? "" : ": fora do esquema lido do código, sem os achados de índice e de nulo")}");
        }
        return text.ToString().TrimEnd();
    }

    private static string Count(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";
}
