using System.Globalization;
using System.Text;
using Rastro.Core.Database;

namespace Rastro.Mcp;

// O plano estimado em texto para o agente (get_query_plan, Fase 44): o custo, os operadores mais caros, os
// índices que o próprio banco aponta como faltando e os avisos. Nunca a string de conexão.
public static class QueryPlanText
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");

    public static string Format(QueryPlan plan, LiveConnectionView connection)
    {
        var dialect = connection.Dialect == SqlDialect.PostgreSql ? "PostgreSQL" : "SQL Server";
        var where = string.IsNullOrWhiteSpace(connection.Database) ? connection.Server : $"{connection.Server}, banco {connection.Database}";
        var text = new StringBuilder();
        text.AppendLine($"Plano estimado pelo banco vivo ({dialect}, servidor {where}). A consulta não foi executada e nenhum dado foi lido.");
        text.AppendLine($"Custo total estimado: {Cost(plan.TotalCost)} (unidade do otimizador; serve para comparar consultas, não é tempo).");

        if (plan.Operations.Count > 0)
        {
            text.AppendLine("Operadores mais caros:");
            foreach (var o in plan.Operations)
                text.AppendLine($"- {o.PhysicalOp}{(o.Object is null ? "" : $" em {o.Object}")}: {o.Percent.ToString("0.#", Culture)}% do custo, {o.EstimatedRows.ToString("0.#", Culture)} linhas estimadas{(o.Scan ? ", varredura" : "")}");
        }

        if (plan.MissingIndexes.Count > 0)
        {
            text.AppendLine("Índices que o banco aponta como faltando:");
            foreach (var i in plan.MissingIndexes)
            {
                var keys = string.Join(", ", i.Equality.Concat(i.Inequality));
                var include = i.Include.Count == 0 ? "" : $" incluindo {string.Join(", ", i.Include)}";
                text.AppendLine($"- {i.Table} ({keys}){include}: ganho estimado de {i.Impact.ToString("0.#", Culture)}%");
            }
        }

        if (plan.Warnings.Count > 0)
        {
            text.AppendLine("Avisos do plano:");
            foreach (var w in plan.Warnings) text.AppendLine($"- {w}");
        }
        return text.ToString().TrimEnd();
    }

    public static string Cost(double cost) => cost.ToString("0.####", Culture);
}
