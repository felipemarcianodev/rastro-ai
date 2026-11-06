using System.Globalization;
using System.Xml.Linq;
using Rastro.Core.Database;

namespace Rastro.Infrastructure.Database.Live;

// Lê o XML do SET SHOWPLAN_XML (Fase 40b): as operações mais caras (o custo próprio de cada operador,
// tirado o dos filhos), as varreduras, os índices que o banco diz faltar e os avisos do plano.
public static class ShowPlanReader
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
    private static readonly HashSet<string> Scans = new(StringComparer.OrdinalIgnoreCase) { "Table Scan", "Clustered Index Scan", "Index Scan" };

    public static QueryPlan Read(string xml)
    {
        var operations = new List<PlanOperation>();
        var missing = new List<MissingIndex>();
        var warnings = new List<string>();
        double total = 0;

        foreach (var document in Documents(xml))
        {
            foreach (var statement in document.Descendants(Ns + "StmtSimple").Where(s => s.Element(Ns + "QueryPlan") is not null))
            {
                var plan = statement.Element(Ns + "QueryPlan")!;
                var statementCost = Number(statement, "StatementSubTreeCost");
                total += statementCost;

                foreach (var op in plan.Descendants(Ns + "RelOp"))
                {
                    var children = op.Descendants(Ns + "RelOp").Where(c => c.Ancestors(Ns + "RelOp").First() == op);
                    var own = Math.Max(0, Number(op, "EstimatedTotalSubtreeCost") - children.Sum(c => Number(c, "EstimatedTotalSubtreeCost")));
                    var physical = (string?)op.Attribute("PhysicalOp") ?? "?";
                    operations.Add(new PlanOperation(physical, (string?)op.Attribute("LogicalOp") ?? physical, ObjectOf(op), own, 0,
                        Number(op, "EstimateRows"), Scans.Contains(physical)));
                }

                foreach (var group in plan.Descendants(Ns + "MissingIndexGroup"))
                    foreach (var index in group.Elements(Ns + "MissingIndex"))
                        missing.Add(new MissingIndex(
                            Unquote((string?)index.Attribute("Table")),
                            Columns(index, "EQUALITY"),
                            Columns(index, "INEQUALITY"),
                            Columns(index, "INCLUDE"),
                            Math.Round(Number(group, "Impact"), 1)));

                foreach (var warning in plan.Descendants(Ns + "Warnings").Elements())
                    warnings.Add(Warning(warning));
            }
        }

        var ranked = operations
            .Select(o => o with { Cost = Math.Round(o.Cost, 6), Percent = total > 0 ? Math.Round(o.Cost / total * 100, 1) : 0 })
            .OrderByDescending(o => o.Cost)
            .Take(QueryPlan.MaxOperations)
            .ToList();
        return new QueryPlan(Math.Round(total, 6), ranked, missing, warnings.Distinct().ToList());
    }

    // Um documento por linha devolvida (um por lote); o leitor junta com quebra de linha.
    private static IEnumerable<XDocument> Documents(string xml)
    {
        foreach (var part in xml.Split("<ShowPlanXML", StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("<?xml", StringComparison.Ordinal) && !trimmed.Contains("</ShowPlanXML>", StringComparison.Ordinal)) continue;
            yield return XDocument.Parse("<ShowPlanXML" + part.TrimEnd());
        }
    }

    // A tabela (ou "tabela.índice") que o próprio operador lê, sem descer para os operadores filhos.
    private static string? ObjectOf(XElement op)
    {
        var own = op.Descendants(Ns + "Object").FirstOrDefault(o => o.Ancestors(Ns + "RelOp").First() == op);
        if (own is null) return null;
        var table = Unquote((string?)own.Attribute("Table"));
        var index = Unquote((string?)own.Attribute("Index"));
        return table.Length == 0 ? null : index.Length == 0 ? table : $"{table}.{index}";
    }

    private static List<string> Columns(XElement index, string usage) =>
        index.Elements(Ns + "ColumnGroup")
            .Where(g => string.Equals((string?)g.Attribute("Usage"), usage, StringComparison.OrdinalIgnoreCase))
            .SelectMany(g => g.Elements(Ns + "Column"))
            .Select(c => Unquote((string?)c.Attribute("Name")))
            .ToList();

    private static string Warning(XElement warning) => warning.Name.LocalName switch
    {
        "PlanAffectingConvert" => $"Conversão implícita que pode impedir a busca no índice: {(string?)warning.Attribute("Expression")}",
        "ColumnsWithNoStatistics" => "Coluna sem estatística: " + string.Join(", ", warning.Descendants(Ns + "ColumnReference").Select(c => Unquote((string?)c.Attribute("Column")))),
        "NoJoinPredicate" => "Junção sem predicado: o banco junta cada linha com cada linha.",
        "UnmatchedIndexes" => "Índice filtrado que o banco não pôde usar.",
        "SpillToTempDb" => "O banco estima usar o tempdb por falta de memória.",
        var other => $"Aviso do plano: {other}"
    };

    private static double Number(XElement element, string attribute) =>
        double.TryParse((string?)element.Attribute(attribute), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static string Unquote(string? name) => DbNames.Unquote(name);
}
