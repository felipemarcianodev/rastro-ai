using System.Text;
using Rastro.Core.Database;
using Rastro.Core.Localization;

namespace Rastro.Mcp;

// O plano estimado em texto para o agente (get_query_plan, Fase 44): o custo, os operadores mais caros, os
// índices que o próprio banco aponta como faltando e os avisos. Nunca a string de conexão. Números pelo idioma ativo.
public static class QueryPlanText
{
    public static string Format(QueryPlan plan, LiveConnectionView connection, UiLanguage language)
    {
        string T(string key, params object[] args) => McpText.Get(key, language, args);
        var culture = language.Culture();

        var dialect = connection.Dialect == SqlDialect.PostgreSql ? "PostgreSQL" : "SQL Server";
        var where = string.IsNullOrWhiteSpace(connection.Database) ? connection.Server : T("PlanText.ServerDatabase", connection.Server ?? string.Empty, connection.Database);
        var text = new StringBuilder();
        text.AppendLine(T("PlanText.Header", dialect, where ?? string.Empty));
        text.AppendLine(T("PlanText.Cost", Cost(plan.TotalCost, language)));

        if (plan.Operations.Count > 0)
        {
            text.AppendLine(T("PlanText.Operators"));
            foreach (var o in plan.Operations)
                text.AppendLine(T("PlanText.Operator", o.PhysicalOp, o.Object is null ? "" : T("PlanText.OperatorOn", o.Object),
                    o.Percent.ToString("0.#", culture), o.EstimatedRows.ToString("0.#", culture), o.Scan ? T("PlanText.Scan") : ""));
        }

        if (plan.MissingIndexes.Count > 0)
        {
            text.AppendLine(T("PlanText.MissingIndexes"));
            foreach (var i in plan.MissingIndexes)
            {
                var keys = string.Join(", ", i.Equality.Concat(i.Inequality));
                var include = i.Include.Count == 0 ? "" : T("PlanText.Including", string.Join(", ", i.Include));
                text.AppendLine(T("PlanText.MissingIndex", i.Table, keys, include, i.Impact.ToString("0.#", culture)));
            }
        }

        if (plan.Warnings.Count > 0)
        {
            text.AppendLine(T("PlanText.Warnings"));
            foreach (var w in plan.Warnings) text.AppendLine($"- {w}");
        }
        return text.ToString().TrimEnd();
    }

    public static string Cost(double cost, UiLanguage language) => cost.ToString("0.####", language.Culture());
}
