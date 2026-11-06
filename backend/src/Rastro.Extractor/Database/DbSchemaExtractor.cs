using Rastro.Core.Database;

namespace Rastro.Extractor.Database;

// Esquema do banco pelo código do repositório (Fase 37, ADR-006): modelo do EF Core e migrations pelo
// Roslyn, .sql e projeto de banco pelo leitor de SQL do dialeto, e a fusão com as divergências. Não
// abre conexão, não compila nem executa nada do cliente, e não usa IA (ADR-001).
public static class DbSchemaExtractor
{
    // extra (Fase 40b): fragmentos de fora do código, como o catálogo do banco vivo, que entram na fusão
    // depois das fontes do código (o código dá o nome; a divergência aparece com a fonte de cada lado).
    public static DbSchema Extract(IReadOnlyList<SchemaFile> files, IReadOnlyList<SchemaFragment>? extra = null)
    {
        var ordered = files.OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToList();
        var code = new CodeIndex(ordered.Where(f => f.RelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)).ToList());
        var scripts = ordered.Where(f => f.RelativePath.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)).ToList();
        var dialect = DialectOf(code, ordered, scripts);

        var fragments = new List<SchemaFragment> { new EfModelReader(code, dialect).Read() };
        fragments.AddRange(new EfMigrationReader(code, dialect).Read());

        if (SqlSchemaReaders.For(dialect) is { } reader)
            fragments.AddRange(scripts.Select(f => reader.Read(f.Text, f.RelativePath, 1, SchemaSourceKind.SqlScript)));
        else if (scripts.Count > 0)
            fragments.Add(SchemaFragment.Empty with
            {
                Notes = [new SchemaNote($"Os arquivos .sql do {dialect} não são lidos ({scripts.Count} arquivo(s)).", scripts[0].RelativePath, 1)]
            });

        if (extra is not null) fragments.AddRange(extra);
        return DbSchemaMerger.Merge(dialect, fragments);
    }

    private static readonly HashSet<string> PostgreSqlWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "serial", "bigserial", "smallserial", "jsonb", "timestamptz", "plpgsql", "bytea", "ilike", "returning", "uuid", "boolean"
    };

    private static readonly HashSet<string> SqlServerWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "nvarchar", "nchar", "datetime2", "datetimeoffset", "uniqueidentifier", "tinyint", "go", "clustered", "nonclustered"
    };

    // O dialeto sai do repositório, sem perguntar: primeiro o provedor do EF Core (UseNpgsql ou
    // UseSqlServer), depois as anotações das migrations (Npgsql: ou SqlServer:) e, sem EF, as marcas dos
    // .sql pelos tokens (serial, jsonb, ::, $$ de um lado; nvarchar, colchete, GO do outro). Sem marca
    // nenhuma, SQL Server, o primeiro suportado (ADR-006).
    public static SqlDialect DialectOf(IReadOnlyList<SchemaFile> files)
    {
        var ordered = files.OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToList();
        var code = new CodeIndex(ordered.Where(f => f.RelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)).ToList());
        return DialectOf(code, ordered, ordered.Where(f => f.RelativePath.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)).ToList());
    }

    private static SqlDialect DialectOf(CodeIndex code, IReadOnlyList<SchemaFile> files, IReadOnlyList<SchemaFile> scripts)
    {
        var npgsql = code.Calls("UseNpgsql");
        var sqlServer = code.Calls("UseSqlServer");
        if (npgsql != sqlServer) return npgsql ? SqlDialect.PostgreSql : SqlDialect.SqlServer;
        if (sqlServer) return SqlDialect.SqlServer;

        var migrations = files.Where(f => f.RelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)).ToList();
        var npgsqlAnnotations = migrations.Count(f => f.Text.Contains("\"Npgsql:", StringComparison.Ordinal));
        var sqlServerAnnotations = migrations.Count(f => f.Text.Contains("\"SqlServer:", StringComparison.Ordinal));
        if (npgsqlAnnotations != sqlServerAnnotations) return npgsqlAnnotations > sqlServerAnnotations ? SqlDialect.PostgreSql : SqlDialect.SqlServer;

        var (pg, ss) = (0, 0);
        foreach (var script in scripts)
        {
            var text = PgSqlText.Read(PgSqlText.WithoutCopyData(script.Text), out _);
            if (text is null) { ss++; continue; }
            foreach (var token in text.Tokens)
            {
                if (token.Token is SqlParser.Tokens.DoubleColon or SqlParser.Tokens.DollarQuotedString) pg++;
                else if (token.Token is SqlParser.Tokens.LeftBracket) ss++;
                else if (token.Token is SqlParser.Tokens.Word { QuoteStyle: null } word)
                {
                    if (PostgreSqlWords.Contains(word.Value)) pg++;
                    else if (SqlServerWords.Contains(word.Value)) ss++;
                }
            }
        }
        return pg > ss ? SqlDialect.PostgreSql : SqlDialect.SqlServer;
    }
}
