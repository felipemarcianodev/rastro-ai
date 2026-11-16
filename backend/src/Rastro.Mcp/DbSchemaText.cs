using System.Text;
using Rastro.Core.Database;
using Rastro.Core.Localization;

namespace Rastro.Mcp;

// O esquema do banco em texto para o agente (get_db_schema): uma tabela por bloco, com a fonte de cada
// item em arquivo:linha e as divergências entre fontes escritas por extenso, no idioma de quem usa (ADR-009).
public static class DbSchemaText
{
    public const int MaxLines = 600;

    private static readonly Dictionary<SchemaSourceKind, string> SourceKey = new()
    {
        [SchemaSourceKind.EfModel] = "Schema.Source.EfModel",
        [SchemaSourceKind.EfMigration] = "Schema.Source.EfMigration",
        [SchemaSourceKind.SqlScript] = "Schema.Source.SqlScript",
        [SchemaSourceKind.LiveDatabase] = "Schema.Source.LiveDatabase"
    };

    private static readonly Dictionary<SqlDialect, string> DialectLabel = new()
    {
        [SqlDialect.SqlServer] = "SQL Server",
        [SqlDialect.PostgreSql] = "PostgreSQL"
    };

    private static readonly Dictionary<DeleteAction, string> DeleteKey = new()
    {
        [DeleteAction.NoAction] = "Schema.Delete.NoAction",
        [DeleteAction.Cascade] = "Schema.Delete.Cascade",
        [DeleteAction.SetNull] = "Schema.Delete.SetNull",
        [DeleteAction.SetDefault] = "Schema.Delete.SetDefault",
        [DeleteAction.Restrict] = "Schema.Delete.Restrict"
    };

    private static readonly Dictionary<SchemaDivergenceKind, string> DivergenceKey = new()
    {
        [SchemaDivergenceKind.ColumnType] = "Schema.Divergence.ColumnType",
        [SchemaDivergenceKind.ColumnNullability] = "Schema.Divergence.ColumnNullability",
        [SchemaDivergenceKind.ColumnMissing] = "Schema.Divergence.ColumnMissing",
        [SchemaDivergenceKind.PrimaryKey] = "Schema.Divergence.PrimaryKey"
    };

    // table: só a tabela pedida (com ou sem schema); sem ela, o esquema inteiro.
    public static string Format(DbSchema schema, string? table, UiLanguage language)
    {
        string T(string key, params object[] args) => McpText.Get(key, language, args);
        string Src(SchemaSource source) => Source(source, language);
        string Join(IEnumerable<string> items)
        {
            var list = items.ToList();
            return list.Count == 0 ? T("Schema.Nothing") : string.Join(", ", list);
        }

        var text = new StringBuilder();
        var tables = schema.Tables
            .Where(t => table is null || DbNames.Key(t.Schema, t.Name, schema.Dialect) == DbNames.KeyOf(table, schema.Dialect))
            .ToList();
        if (table is not null && tables.Count == 0) return T("Schema.TableNotFound", table);

        text.AppendLine(T("Schema.Header", DialectLabel[schema.Dialect], schema.Tables.Count, schema.Views.Count, schema.Procedures.Count, schema.Divergences.Count, schema.Findings.Count));

        foreach (var t in tables)
        {
            var name = DbNames.Display(t.Schema, t.Name, schema.Dialect);
            text.AppendLine();
            text.AppendLine(T("Schema.Table", name, string.Join("; ", t.Sources.Select(Src))));
            var pk = t.PrimaryKey?.Columns.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            var fk = t.ForeignKeys.SelectMany(f => f.Columns).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var c in t.Columns)
            {
                var marks = string.Join(" ", new[] { pk.Contains(c.Name) ? "PK" : null, fk.Contains(c.Name) ? "FK" : null, c.Identity ? T("Schema.Identity") : null, c.Personal ? T("Schema.Personal") : null }.Where(m => m is not null));
                var type = c.Type ?? T("Schema.TypeDivergent");
                var nullable = c.Nullable switch { true => T("Schema.Nullable"), false => T("Schema.NotNullable"), null => T("Schema.NullDivergent") };
                var extra = c.Default is null ? string.Empty : T("Schema.Default", c.Default);
                text.AppendLine($"  - {c.Name}: {type}, {nullable}{extra}{(marks.Length > 0 ? " [" + marks + "]" : string.Empty)}");
            }
            if (t.PrimaryKey is null) text.AppendLine("  " + T("Schema.NoPrimaryKey"));
            foreach (var f in t.ForeignKeys)
                text.AppendLine("  " + T("Schema.ForeignKey", string.Join(", ", f.Columns), f.PrincipalTable, string.Join(", ", f.PrincipalColumns), T(DeleteKey[f.OnDelete]), Src(f.Source)));
            foreach (var i in t.Indexes)
            {
                var unique = i.Unique ? T("Schema.IndexUnique") : string.Empty;
                var includes = i.Included.Count > 0 ? T("Schema.IndexIncludes", string.Join(", ", i.Included)) : string.Empty;
                var convention = i.Convention ? T("Schema.IndexConvention") + ", " : string.Empty;
                text.AppendLine($"  {T("Schema.Index")} {i.Name ?? T("Schema.IndexNoName")}{unique}: {string.Join(", ", i.Columns)}{includes} ({convention}{Src(i.Source)})");
            }
            foreach (var f in schema.Findings.Where(f => f.Table.Equals(name, StringComparison.OrdinalIgnoreCase)))
                text.AppendLine(T("Schema.Finding", FindingText(f, language), Src(f.Source)));
            foreach (var d in schema.Divergences.Where(d => d.Table.Equals(name, StringComparison.OrdinalIgnoreCase)))
                text.AppendLine(T("Schema.Divergence", T(DivergenceKey[d.Kind]), d.Column.Length > 0 ? T("Schema.DivergenceIn", d.Column) : string.Empty,
                    string.Join("; ", d.Values.Select(v => T("Schema.SourceSays", T(SourceKey[v.Source.Kind]), v.Value, Src(v.Source))))));
        }

        if (table is null)
        {
            foreach (var v in schema.Views)
                text.AppendLine($"{Environment.NewLine}{T("Schema.View", DbNames.Display(v.Schema, v.Name, schema.Dialect), Src(v.Source), Join(v.Columns), Join(v.Reads))}");
            foreach (var p in schema.Procedures)
                text.AppendLine($"{Environment.NewLine}{T("Schema.Procedure", DbNames.Display(p.Schema, p.Name, schema.Dialect), Src(p.Source), Join(p.Parameters.Select(x => $"{x.Name} {x.Type}{(x.Output ? T("Schema.ParamOutput") : string.Empty)}")), Join(p.Reads), Join(p.Writes))}");
            if (schema.Notes.Count > 0)
            {
                text.AppendLine();
                text.AppendLine(T("Schema.Notes"));
                foreach (var n in schema.Notes)
                    text.AppendLine($"  - {n.Message}{(n.File.Length > 0 ? $" ({n.File}:{n.Line})" : string.Empty)}");
            }
        }

        var lines = text.ToString().Split(Environment.NewLine);
        return lines.Length <= MaxLines
            ? text.ToString()
            : string.Join(Environment.NewLine, lines.Take(MaxLines)) + Environment.NewLine + T("Schema.Cut", MaxLines);
    }

    public static string FindingText(SchemaFinding finding, UiLanguage language) => finding.Kind switch
    {
        SchemaFindingKind.ForeignKeyWithoutIndex => McpText.Get("Schema.FindingFkNoIndex", language, string.Join(", ", finding.Columns)),
        _ => finding.Kind.ToString()
    };

    private static string Source(SchemaSource source, UiLanguage language) =>
        source.Kind == SchemaSourceKind.LiveDatabase
            ? McpText.Get(SourceKey[source.Kind], language)
            : $"{McpText.Get(SourceKey[source.Kind], language)}, {source.File}:{source.Line}";
}
