using System.Text.Json;
using Rastro.Core.Database;

namespace Rastro.Infrastructure.Database.Live;

// Lê o EXPLAIN (FORMAT JSON) do PostgreSQL (Fase 42b): as operações mais caras pelo custo próprio (o
// total do nó menos o dos filhos) e as varreduras da tabela inteira (Seq Scan). O PostgreSQL não diz qual
// índice falta, então essa lista vem vazia; os achados sem IA cobrem a coluna sem índice.
public static class ExplainPlanReader
{
    public static QueryPlan Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Array) root = root.EnumerateArray().FirstOrDefault();
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Plan", out var plan))
            throw new JsonException("O EXPLAIN não devolveu o plano.");

        var total = Number(plan, "Total Cost");
        var operations = new List<PlanOperation>();
        Walk(plan, total, operations);
        return new QueryPlan(total,
            operations.OrderByDescending(o => o.Cost).ThenBy(o => o.PhysicalOp, StringComparer.Ordinal).Take(QueryPlan.MaxOperations).ToList(),
            [], []);
    }

    private static void Walk(JsonElement node, double total, List<PlanOperation> into)
    {
        var children = node.TryGetProperty("Plans", out var plans) && plans.ValueKind == JsonValueKind.Array ? plans.EnumerateArray().ToList() : [];
        var own = Math.Max(Number(node, "Total Cost") - children.Sum(c => Number(c, "Total Cost")), 0);
        var type = Text(node, "Node Type") ?? "?";
        var relation = Text(node, "Relation Name");
        var index = Text(node, "Index Name");
        var target = relation is null ? index : index is null ? relation : $"{relation}.{index}";
        var logical = Text(node, "Join Type") is { } join ? $"{join} Join" : Text(node, "Strategy") ?? type;
        into.Add(new PlanOperation(type, logical, target, Math.Round(own, 4),
            total > 0 ? Math.Round(own / total * 100, 1) : 0, Number(node, "Plan Rows"), type == "Seq Scan"));
        foreach (var child in children) Walk(child, total, into);
    }

    private static double Number(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? number : 0;

    private static string? Text(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
