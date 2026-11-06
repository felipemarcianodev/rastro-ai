using System.Text;
using Rastro.Core.Database;

namespace Rastro.Mcp;

// O esquema do banco em texto para o agente (get_db_schema): uma tabela por bloco, com a fonte de cada
// item em arquivo:linha e as divergências entre fontes escritas por extenso.
public static class DbSchemaText
{
    public const int MaxLines = 600;

    private static readonly Dictionary<SchemaSourceKind, string> SourceLabel = new()
    {
        [SchemaSourceKind.EfModel] = "modelo do EF",
        [SchemaSourceKind.EfMigration] = "migration",
        [SchemaSourceKind.SqlScript] = ".sql",
        [SchemaSourceKind.LiveDatabase] = "banco vivo"
    };

    private static readonly Dictionary<SqlDialect, string> DialectLabel = new()
    {
        [SqlDialect.SqlServer] = "SQL Server",
        [SqlDialect.PostgreSql] = "PostgreSQL"
    };

    private static readonly Dictionary<DeleteAction, string> DeleteLabel = new()
    {
        [DeleteAction.NoAction] = "sem ação",
        [DeleteAction.Cascade] = "cascata",
        [DeleteAction.SetNull] = "anula",
        [DeleteAction.SetDefault] = "volta ao padrão",
        [DeleteAction.Restrict] = "impede"
    };

    private static readonly Dictionary<SchemaDivergenceKind, string> DivergenceLabel = new()
    {
        [SchemaDivergenceKind.ColumnType] = "tipo",
        [SchemaDivergenceKind.ColumnNullability] = "nulo",
        [SchemaDivergenceKind.ColumnMissing] = "existência da coluna",
        [SchemaDivergenceKind.PrimaryKey] = "chave primária"
    };

    // table: só a tabela pedida (com ou sem schema); sem ela, o esquema inteiro.
    public static string Format(DbSchema schema, string? table)
    {
        var text = new StringBuilder();
        var tables = schema.Tables
            .Where(t => table is null || DbNames.Key(t.Schema, t.Name, schema.Dialect) == DbNames.KeyOf(table, schema.Dialect))
            .ToList();
        if (table is not null && tables.Count == 0) return $"Tabela {table} não encontrada no esquema. Chame get_db_schema sem table para ver todas.";

        text.AppendLine($"Banco: {DialectLabel[schema.Dialect]}. {schema.Tables.Count} tabelas, {schema.Views.Count} views, {schema.Procedures.Count} procedures, {schema.Divergences.Count} divergências entre fontes, {schema.Findings.Count} achados.");

        foreach (var t in tables)
        {
            var name = DbNames.Display(t.Schema, t.Name, schema.Dialect);
            text.AppendLine();
            text.AppendLine($"Tabela {name} ({string.Join("; ", t.Sources.Select(Source))})");
            var pk = t.PrimaryKey?.Columns.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            var fk = t.ForeignKeys.SelectMany(f => f.Columns).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var c in t.Columns)
            {
                var marks = string.Join(" ", new[] { pk.Contains(c.Name) ? "PK" : null, fk.Contains(c.Name) ? "FK" : null, c.Identity ? "identidade" : null, c.Personal ? "dado pessoal pelo nome" : null }.Where(m => m is not null));
                var type = c.Type ?? "tipo divergente";
                var nullable = c.Nullable switch { true => "aceita nulo", false => "não aceita nulo", null => "nulo divergente" };
                var extra = c.Default is null ? string.Empty : $", padrão {c.Default}";
                text.AppendLine($"  - {c.Name}: {type}, {nullable}{extra}{(marks.Length > 0 ? " [" + marks + "]" : string.Empty)}");
            }
            if (t.PrimaryKey is null) text.AppendLine("  Sem chave primária.");
            foreach (var f in t.ForeignKeys)
                text.AppendLine($"  FK {string.Join(", ", f.Columns)} -> {f.PrincipalTable} ({string.Join(", ", f.PrincipalColumns)}), ao apagar: {DeleteLabel[f.OnDelete]} ({Source(f.Source)})");
            foreach (var i in t.Indexes)
                text.AppendLine($"  Índice {i.Name ?? "sem nome"}{(i.Unique ? " (único)" : string.Empty)}: {string.Join(", ", i.Columns)}{(i.Included.Count > 0 ? " inclui " + string.Join(", ", i.Included) : string.Empty)} ({(i.Convention ? "convenção do EF na FK, " : string.Empty)}{Source(i.Source)})");
            foreach (var f in schema.Findings.Where(f => f.Table.Equals(name, StringComparison.OrdinalIgnoreCase)))
                text.AppendLine($"  Achado: {FindingText(f)} ({Source(f.Source)})");
            foreach (var d in schema.Divergences.Where(d => d.Table.Equals(name, StringComparison.OrdinalIgnoreCase)))
                text.AppendLine($"  Divergência de {DivergenceLabel[d.Kind]}{(d.Column.Length > 0 ? " em " + d.Column : string.Empty)}: {string.Join("; ", d.Values.Select(v => $"{SourceLabel[v.Source.Kind]} diz {v.Value} ({Source(v.Source)})"))}. O Rastro não escolhe: confira no código.");
        }

        if (table is null)
        {
            foreach (var v in schema.Views)
                text.AppendLine($"{Environment.NewLine}View {DbNames.Display(v.Schema, v.Name, schema.Dialect)} ({Source(v.Source)}): colunas {Join(v.Columns)}; lê {Join(v.Reads)}.");
            foreach (var p in schema.Procedures)
                text.AppendLine($"{Environment.NewLine}Procedure {DbNames.Display(p.Schema, p.Name, schema.Dialect)} ({Source(p.Source)}): parâmetros {Join(p.Parameters.Select(x => $"{x.Name} {x.Type}{(x.Output ? " saída" : string.Empty)}"))}; lê {Join(p.Reads)}; grava {Join(p.Writes)}.");
            if (schema.Notes.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("O que o Rastro não conseguiu ler:");
                foreach (var n in schema.Notes)
                    text.AppendLine($"  - {n.Message}{(n.File.Length > 0 ? $" ({n.File}:{n.Line})" : string.Empty)}");
            }
        }

        var lines = text.ToString().Split(Environment.NewLine);
        return lines.Length <= MaxLines
            ? text.ToString()
            : string.Join(Environment.NewLine, lines.Take(MaxLines)) + Environment.NewLine + $"(cortado em {MaxLines} linhas: peça uma tabela com o parâmetro table)";
    }

    public static string FindingText(SchemaFinding finding) => finding.Kind switch
    {
        SchemaFindingKind.ForeignKeyWithoutIndex => $"chave estrangeira {string.Join(", ", finding.Columns)} sem índice: a busca pela FK varre a tabela",
        _ => finding.Kind.ToString()
    };

    private static string Source(SchemaSource source) =>
        source.Kind == SchemaSourceKind.LiveDatabase ? SourceLabel[source.Kind] : $"{SourceLabel[source.Kind]}, {source.File}:{source.Line}";

    private static string Join(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count == 0 ? "nada" : string.Join(", ", list);
    }
}
