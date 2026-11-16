using System.Text;
using Rastro.Core.Database;
using Rastro.Core.Localization;

namespace Rastro.Mcp;

// A análise de consulta em texto para o agente (analyze_query, Fase 40): os achados por gravidade, com a
// linha, o trecho e o motivo, e as tabelas que a consulta usa. Na Fase 41, também o esquema só dessas
// tabelas (colunas, tipos e índices), para o agente propor a melhora com propose_query_rewrite.
public static class QueryAnalysisText
{
    public static string Format(QueryAnalysis analysis, UiLanguage language, DbSchema? schema = null)
    {
        string T(string key, params object[] args) => McpText.Get(key, language, args);

        if (analysis.Error is { } error)
            return T("Analysis.ParseError", T(analysis.Dialect == SqlDialect.PostgreSql ? "Analysis.ReaderPostgres" : "Analysis.ReaderTSql"), error.Line, error.Column, error.Message);

        var text = new StringBuilder();
        var statements = analysis.Statements == 1 ? T("Analysis.StatementOne") : T("Analysis.StatementMany", analysis.Statements);
        var findings = analysis.Findings.Count == 1 ? T("Analysis.FindingOne") : T("Analysis.FindingMany", analysis.Findings.Count);
        text.AppendLine(T("Analysis.Summary", statements, findings));
        if (analysis.Findings.Count > 0) text.AppendLine(QueryContextText.Findings(analysis, language));

        if (analysis.Tables.Count > 0)
        {
            text.AppendLine(T("Analysis.Tables"));
            foreach (var t in analysis.Tables)
                text.AppendLine($"- {t.Name}{(t.Alias is null ? "" : T("Analysis.TableAlias", t.Alias))}{(t.InSchema ? "" : T("Analysis.OutOfSchema"))}");
        }

        if (schema is not null && analysis.Tables.Any(t => t.InSchema))
        {
            text.AppendLine(T("Analysis.SchemaHeader"));
            text.AppendLine(QueryContextText.Tables(analysis, schema, language));
            text.AppendLine(T("Analysis.Propose"));
        }
        return text.ToString().TrimEnd();
    }
}
