using System.Text;
using Rastro.Core.Localization;

namespace Rastro.Core.Database;

// O que a IA recebe para sugerir a melhora de uma consulta (Fase 41, ADR-006): só as tabelas que a
// consulta usa, com colunas, tipos, nulo, chaves e índices. Determinístico e sem IA: o mesmo esquema e a
// mesma análise dão sempre o mesmo texto. Nunca leva dado das tabelas (o Rastro não os lê) nem a string
// de conexão. Usado pelo provedor (Rastro.Ai) e pelo agente (analyze_query no Rastro.Mcp).
public static class QueryContextText
{
    // Chaves do texto de cada gravidade e de cada tipo de achado (nos .resx, por idioma).
    public static readonly IReadOnlyDictionary<QuerySeverity, string> SeverityKey = new Dictionary<QuerySeverity, string>
    {
        [QuerySeverity.High] = "QueryContext.Severity.High",
        [QuerySeverity.Medium] = "QueryContext.Severity.Medium",
        [QuerySeverity.Low] = "QueryContext.Severity.Low"
    };

    public static readonly IReadOnlyDictionary<QueryFindingKind, string> KindKey = new Dictionary<QueryFindingKind, string>
    {
        [QueryFindingKind.SelectStar] = "QueryContext.Kind.SelectStar",
        [QueryFindingKind.FunctionOnFilterColumn] = "QueryContext.Kind.FunctionOnFilterColumn",
        [QueryFindingKind.LeadingWildcardLike] = "QueryContext.Kind.LeadingWildcardLike",
        [QueryFindingKind.OrAcrossColumns] = "QueryContext.Kind.OrAcrossColumns",
        [QueryFindingKind.ColumnWithoutIndex] = "QueryContext.Kind.ColumnWithoutIndex",
        [QueryFindingKind.JoinWithoutCondition] = "QueryContext.Kind.JoinWithoutCondition",
        [QueryFindingKind.DistinctOverJoin] = "QueryContext.Kind.DistinctOverJoin",
        [QueryFindingKind.CorrelatedSubquery] = "QueryContext.Kind.CorrelatedSubquery",
        [QueryFindingKind.OrderByWithoutLimit] = "QueryContext.Kind.OrderByWithoutLimit",
        [QueryFindingKind.NotInNullable] = "QueryContext.Kind.NotInNullable",
        [QueryFindingKind.WriteWithoutWhere] = "QueryContext.Kind.WriteWithoutWhere",
        [QueryFindingKind.ReadWithoutLimit] = "QueryContext.Kind.ReadWithoutLimit"
    };

    // Um achado por linha: gravidade, tipo, linha, trecho e motivo.
    public static string Findings(QueryAnalysis analysis, UiLanguage language) =>
        string.Join('\n', analysis.Findings.Select(f => LocalizedText.Get("QueryContext.Finding", language,
            LocalizedText.Get(SeverityKey[f.Severity], language), LocalizedText.Get(KindKey[f.Kind], language), f.Line, f.Snippet, f.Message)));

    public static string Tables(QueryAnalysis analysis, DbSchema schema, UiLanguage language)
    {
        string T(string key, params object[] args) => LocalizedText.Get(key, language, args);

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
            text.AppendLine(T("QueryContext.Table", name));
            foreach (var column in table.Columns)
            {
                var key = table.PrimaryKey?.Columns.Contains(column.Name, StringComparer.OrdinalIgnoreCase) == true ? T("QueryContext.Column.PrimaryKeySuffix") : "";
                var nullable = column.Nullable switch { true => T("QueryContext.Column.Nullable"), false => T("QueryContext.Column.NotNullable"), _ => T("QueryContext.Column.NullableDiverges") };
                text.AppendLine($"- {column.Name} {column.Type ?? T("QueryContext.Column.TypeDiverges")}, {nullable}{key}{(column.Identity ? T("QueryContext.Column.IdentitySuffix") : "")}");
            }
            if (table.PrimaryKey is { } pk)
                text.AppendLine(T("QueryContext.PrimaryKey", pk.Name ?? T("QueryContext.Unnamed"), string.Join(", ", pk.Columns)));
            foreach (var index in table.Indexes)
                text.AppendLine(T("QueryContext.Index", index.Name ?? T("QueryContext.Unnamed"), index.Unique ? T("QueryContext.Index.UniqueSuffix") : "", string.Join(", ", index.Columns))
                    + (index.Included.Count == 0 ? "" : T("QueryContext.Index.Includes", string.Join(", ", index.Included))));
            if (table.Indexes.Count == 0) text.AppendLine(T("QueryContext.NoIndexes"));
            foreach (var fk in table.ForeignKeys.Where(f => keys.Contains(DbNames.KeyOf(f.PrincipalTable, schema.Dialect))))
                text.AppendLine(T("QueryContext.ForeignKey", string.Join(", ", fk.Columns), fk.PrincipalTable, string.Join(", ", fk.PrincipalColumns)));
        }
        foreach (var view in views)
            text.AppendLine(T("QueryContext.View", DbNames.Display(view.Schema, view.Name, schema.Dialect), view.Columns.Count == 0 ? T("QueryContext.View.ColumnsUnread") : string.Join(", ", view.Columns)));
        foreach (var outside in analysis.Tables.Where(t => !t.InSchema))
            text.AppendLine(T("QueryContext.OutsideSchema", outside.Name));
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
