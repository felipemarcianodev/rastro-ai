using System.Text;

namespace Rastro.Core.Database;

// O que a IA recebe para sugerir a melhora de uma consulta (Fase 41, ADR-006): só as tabelas que a
// consulta usa, com colunas, tipos, nulo, chaves e índices. Determinístico e sem IA: o mesmo esquema e a
// mesma análise dão sempre o mesmo texto. Nunca leva dado das tabelas (o Rastro não os lê) nem a string
// de conexão. Usado pelo provedor (Rastro.Ai) e pelo agente (analyze_query no Rastro.Mcp).
public static class QueryContextText
{
    public static readonly IReadOnlyDictionary<QuerySeverity, string> SeverityLabel = new Dictionary<QuerySeverity, string>
    {
        [QuerySeverity.High] = "alta",
        [QuerySeverity.Medium] = "média",
        [QuerySeverity.Low] = "baixa"
    };

    public static readonly IReadOnlyDictionary<QueryFindingKind, string> KindLabel = new Dictionary<QueryFindingKind, string>
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

    // Um achado por linha: gravidade, tipo, linha, trecho e motivo.
    public static string Findings(QueryAnalysis analysis) =>
        string.Join('\n', analysis.Findings.Select(f => $"- [{SeverityLabel[f.Severity]}] {KindLabel[f.Kind]}, linha {f.Line}: {f.Snippet}. {f.Message}"));

    public static string Tables(QueryAnalysis analysis, DbSchema schema)
    {
        var tables = UsedTables(analysis, schema);
        var views = analysis.Tables
            .Where(t => t.InSchema)
            .Select(t => schema.Views.FirstOrDefault(v => Same(v.Schema, v.Name, t.Name, schema.Dialect)))
            .OfType<DbView>()
            .ToList();
        var keys = tables.Select(t => DbNames.Key(t.Schema, t.Name, schema.Dialect)).ToHashSet();

        var text = new StringBuilder();
        foreach (var table in tables)
        {
            var name = DbNames.Display(table.Schema, table.Name, schema.Dialect);
            text.AppendLine($"Tabela {name}:");
            foreach (var column in table.Columns)
            {
                var key = table.PrimaryKey?.Columns.Contains(column.Name, StringComparer.OrdinalIgnoreCase) == true ? ", chave primária" : "";
                var nullable = column.Nullable switch { true => "aceita nulo", false => "não aceita nulo", _ => "nulo divergente entre fontes" };
                text.AppendLine($"- {column.Name} {column.Type ?? "(tipo divergente entre fontes)"}, {nullable}{key}{(column.Identity ? ", identidade" : "")}");
            }
            if (table.PrimaryKey is { } pk)
                text.AppendLine($"  chave primária {pk.Name ?? "(sem nome)"} ({string.Join(", ", pk.Columns)})");
            foreach (var index in table.Indexes)
                text.AppendLine($"  índice {index.Name ?? "(sem nome)"}{(index.Unique ? " único" : "")} ({string.Join(", ", index.Columns)})"
                    + (index.Included.Count == 0 ? "" : $" inclui ({string.Join(", ", index.Included)})"));
            if (table.Indexes.Count == 0) text.AppendLine("  nenhum índice além da chave primária");
            foreach (var fk in table.ForeignKeys.Where(f => keys.Contains(DbNames.KeyOf(f.PrincipalTable, schema.Dialect))))
                text.AppendLine($"  chave estrangeira ({string.Join(", ", fk.Columns)}) aponta para {fk.PrincipalTable} ({string.Join(", ", fk.PrincipalColumns)})");
        }
        foreach (var view in views)
            text.AppendLine($"View {DbNames.Display(view.Schema, view.Name, schema.Dialect)}: colunas {(view.Columns.Count == 0 ? "não lidas" : string.Join(", ", view.Columns))}.");
        foreach (var outside in analysis.Tables.Where(t => !t.InSchema))
            text.AppendLine($"{outside.Name}: fora do esquema lido do código (colunas e índices desconhecidos).");
        return text.ToString().TrimEnd();
    }

    public static List<DbTable> UsedTables(QueryAnalysis analysis, DbSchema schema) =>
        analysis.Tables
            .Where(t => t.InSchema)
            .Select(t => schema.Tables.FirstOrDefault(s => Same(s.Schema, s.Name, t.Name, schema.Dialect)))
            .OfType<DbTable>()
            .DistinctBy(t => DbNames.Key(t.Schema, t.Name, schema.Dialect))
            .ToList();

    private static bool Same(string? schemaName, string name, string display, SqlDialect dialect) =>
        DbNames.Key(schemaName, name, dialect) == DbNames.KeyOf(display, dialect);
}
