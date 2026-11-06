using SqlParser;
using SqlParser.Ast;
using SqlParser.Tokens;
using Rastro.Core.Database;

namespace Rastro.Extractor.Database;

// .sql do PostgreSQL pelo SqlParserCS (MIT, código gerenciado; ADR-006): CREATE TABLE (com serial,
// identidade e as chaves na coluna ou na tabela), ALTER TABLE ... ADD (chaves e colunas), CREATE INDEX,
// CREATE VIEW (e a materializada) e CREATE FUNCTION e CREATE PROCEDURE. O arquivo é separado nas
// instruções pelos tokens (o ponto e vírgula dentro de string e de corpo $$ não conta), e cada uma é
// lida sozinha: o que o leitor não entende numa instrução não derruba as outras. O resto do arquivo
// (SET, COPY, GRANT, CREATE EXTENSION, o que o pg_dump escreve) é ignorado. Nada é executado.
public sealed class PgSqlSchemaReader : ISqlSchemaReader
{
    private const SqlDialect Pg = SqlDialect.PostgreSql;

    public SqlDialect Dialect => Pg;

    public SchemaFragment Read(string text, string file, int firstLine, SchemaSourceKind kind)
    {
        var notes = new List<SchemaNote>();
        text = PgSqlText.WithoutCopyData(text);
        var tokens = PgSqlText.Read(text, out var tokenError);
        if (tokens is null)
        {
            notes.Add(new SchemaNote($"O leitor de SQL do PostgreSQL não entendeu o arquivo ({tokenError?.Message}): ele ficou de fora.", file, firstLine + (tokenError?.Line ?? 1) - 1));
            return new SchemaFragment([], [], [], notes);
        }

        var builder = new Builder(file, firstLine, kind);
        var failures = new List<(int Line, string Message)>();
        foreach (var (from, to) in tokens.Statements)
        {
            var head = Head(tokens, from, to);
            if (head == Statement.Other) continue;
            var line = tokens.Tokens[from].Line;
            if (head == Statement.Routine)
            {
                builder.Routine(tokens, from, to);
                continue;
            }
            if (head == Statement.AlterTable && builder.AlterIdentity(tokens, from, to)) continue;

            var start = tokens.Tokens[from].Start;
            var end = tokens.Tokens[to - 1].Start + tokens.Tokens[to - 1].Length;
            var parsed = PgSqlText.Parse(text[start..end], out var error);
            if (parsed is null || parsed.Count == 0)
            {
                failures.Add((line + (error?.Line ?? 1) - 1, error?.Message ?? "instrução vazia"));
                continue;
            }
            foreach (var statement in parsed) builder.Add(statement, line);
        }

        if (failures.Count > 0)
        {
            var (failLine, message) = failures[0];
            var more = failures.Count == 1 ? "" : $" (e mais {failures.Count - 1} instrução(ões) do mesmo arquivo)";
            notes.Add(new SchemaNote($"O leitor de SQL do PostgreSQL não entendeu uma instrução ({message}){more}: ela ficou de fora.", file, firstLine + failLine - 1));
        }
        return new SchemaFragment(builder.Tables(), builder.Views, builder.Procedures, notes);
    }

    private enum Statement
    {
        Other = 0,
        Table = 1,
        Index = 2,
        View = 3,
        AlterTable = 4,
        Routine = 5
    }

    // O que a instrução é, pelas primeiras palavras.
    private static Statement Head(PgSqlText text, int from, int to)
    {
        var words = new List<string>();
        for (var i = from; i < to && words.Count < 6; i++)
            words.Add(text.Tokens[i].Token is Word { QuoteStyle: null } w ? w.Value.ToUpperInvariant() : "");
        if (words.Count < 2) return Statement.Other;
        if (words[0] == "ALTER" && words[1] == "TABLE") return Statement.AlterTable;
        if (words[0] != "CREATE") return Statement.Other;
        foreach (var word in words.Skip(1))
        {
            switch (word)
            {
                case "OR" or "REPLACE" or "TEMP" or "TEMPORARY" or "UNLOGGED" or "UNIQUE" or "MATERIALIZED" or "GLOBAL" or "LOCAL" or "RECURSIVE":
                    continue;
                case "TABLE": return Statement.Table;
                case "INDEX": return Statement.Index;
                case "VIEW": return Statement.View;
                case "FUNCTION" or "PROCEDURE": return Statement.Routine;
                default: return Statement.Other;
            }
        }
        return Statement.Other;
    }

    private sealed class Builder(string file, int firstLine, SchemaSourceKind kind)
    {
        private readonly Dictionary<string, TableBuilder> _tables = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _order = [];

        public List<DbView> Views { get; } = [];
        public List<DbProcedure> Procedures { get; } = [];

        public List<DbTable> Tables() => _order.Select(k => _tables[k].Build()).ToList();

        private SchemaSource Source(int line) => new(kind, file, firstLine + Math.Max(line, 1) - 1);

        private TableBuilder Table(ObjectName name, int line)
        {
            var key = DbNames.Key(PgAst.SchemaOf(name), PgAst.Last(name), Pg);
            if (!_tables.TryGetValue(key, out var table))
            {
                table = new TableBuilder(PgAst.SchemaOf(name), PgAst.Last(name), Source(line));
                _tables[key] = table;
                _order.Add(key);
            }
            return table;
        }

        public void Add(SqlParser.Ast.Statement statement, int line)
        {
            switch (statement)
            {
                case SqlParser.Ast.Statement.CreateTable { Element: { Temporary: false, Name: not null } create }:
                    var table = Table(create.Name, line);
                    table.Defined = true;
                    foreach (var column in create.Columns ?? []) AddColumn(table, column, line);
                    foreach (var constraint in create.Constraints ?? []) AddConstraint(table, constraint, line);
                    break;
                case SqlParser.Ast.Statement.CreateIndex { Element: { TableName: not null } index }:
                    Table(index.TableName, line).Indexes.Add(new DbIndex(
                        index.Name is null ? null : PgAst.Last(index.Name),
                        (index.Columns ?? []).Select(c => PgSqlQueryAnalyzer.Plain(c.Expression) is { } parts ? parts[^1] : c.Expression.ToSql()).ToList(),
                        (index.Include ?? []).Select(i => i.Value).ToList(),
                        index.Unique,
                        Source(line)));
                    break;
                case SqlParser.Ast.Statement.AlterTable { Name: not null } alter:
                    var target = Table(alter.Name, line);
                    foreach (var operation in alter.Operations ?? [])
                    {
                        if (operation is AlterTableOperation.AddConstraint { TableConstraint: { } constraint }) AddConstraint(target, constraint, line);
                        else if (operation is AlterTableOperation.AddColumn { ColumnDef: { } column }) AddColumn(target, column, line);
                        else if (operation is AlterTableOperation.AlterColumn { ColumnName: { } name } altered
                            && altered.ToSql().Contains("nextval(", StringComparison.OrdinalIgnoreCase))
                            target.MarkIdentity(name.Value);
                    }
                    break;
                case SqlParser.Ast.Statement.CreateView { Name: not null } view:
                    var columns = view.Columns is { Count: > 0 } named
                        ? named.Select(c => c.Name.Value).ToList()
                        : ProjectionNames(view.Query?.Query);
                    Views.Add(new DbView(PgAst.SchemaOf(view.Name), PgAst.Last(view.Name), columns, view.Query is null ? [] : TablesOf(view.Query), Source(line)));
                    break;
            }
        }

        // pg_dump escreve a identidade à parte: ALTER TABLE x ALTER COLUMN id ADD GENERATED ... AS IDENTITY.
        public bool AlterIdentity(PgSqlText text, int from, int to)
        {
            var generated = -1;
            for (var i = from; i < to - 1; i++)
                if (text.Tokens[i].Is("ADD") && text.Tokens[i + 1].Is("GENERATED")) { generated = i; break; }
            if (generated < 0) return false;
            var column = text.Find("COLUMN", from, generated);
            var tableAt = text.Find("TABLE", from, to);
            if (column < 0 || tableAt < 0) return true;
            var name = Name(text, tableAt + 1, column, out _);
            if (name is null || text.Tokens[column + 1].Word is not { } columnName) return true;
            var key = DbNames.Key(name.Value.Schema, name.Value.Name, Pg);
            if (_tables.TryGetValue(key, out var table)) table.MarkIdentity(columnName);
            return true;
        }

        private void AddColumn(TableBuilder table, ColumnDef column, int line)
        {
            var name = column.Name.Value;
            var type = column.DataType?.ToSql().ToLowerInvariant();
            bool? nullable = null;
            string? defaultValue = null;
            var identity = type is "serial" or "serial4" or "bigserial" or "serial8" or "smallserial" or "serial2";
            foreach (var definition in column.Options ?? [])
            {
                var constraintName = definition.Name?.Value;
                switch (definition.Option)
                {
                    case ColumnOption.NotNull:
                        nullable = false;
                        break;
                    case ColumnOption.Null:
                        nullable = true;
                        break;
                    case ColumnOption.Default d:
                        defaultValue = d.Expression?.ToSql();
                        if (defaultValue?.Contains("nextval(", StringComparison.OrdinalIgnoreCase) == true) identity = true;
                        break;
                    case ColumnOption.Generated { GenerationExpr: null }:
                    case ColumnOption.Identity:
                        identity = true;
                        break;
                    case ColumnOption.Unique { IsPrimary: true }:
                        table.PrimaryKey = new DbPrimaryKey(constraintName, [name], Source(line));
                        break;
                    case ColumnOption.Unique:
                        table.Indexes.Add(new DbIndex(constraintName, [name], [], true, Source(line)));
                        break;
                    case ColumnOption.ForeignKey fk when fk.Name is not null:
                        table.ForeignKeys.Add(new DbForeignKey(constraintName, [name], Display(fk.Name),
                            (fk.ReferredColumns ?? []).Select(c => c.Value).ToList(), Delete(fk.OnDeleteAction), Source(line)));
                        break;
                }
            }
            table.Columns.Add(new ColumnBuilder(name, type, nullable, defaultValue, identity, Source(line)));
        }

        private void AddConstraint(TableBuilder table, TableConstraint constraint, int line)
        {
            switch (constraint)
            {
                case TableConstraint.Unique { IsPrimaryKey: true } pk:
                    table.PrimaryKey = new DbPrimaryKey(pk.Name?.Value, (pk.Columns ?? []).Select(c => c.Value).ToList(), Source(line));
                    break;
                case TableConstraint.Unique unique:
                    table.Indexes.Add(new DbIndex(unique.Name?.Value, (unique.Columns ?? []).Select(c => c.Value).ToList(), [], true, Source(line)));
                    break;
                case TableConstraint.ForeignKey { ForeignTable: not null } fk:
                    table.ForeignKeys.Add(new DbForeignKey(fk.Name?.Value, (fk.Columns ?? []).Select(c => c.Value).ToList(), Display(fk.ForeignTable),
                        (fk.ReferredColumns ?? []).Select(c => c.Value).ToList(), Delete(fk.OnDelete), Source(line)));
                    break;
            }
        }

        // CREATE [OR REPLACE] FUNCTION|PROCEDURE nome(parâmetros) ... AS $$ corpo $$: pelos tokens, porque o
        // leitor não cobre toda a sintaxe do PostgreSQL aqui (RETURNS SETOF, o modo IN). As tabelas que o
        // corpo lê e grava saem dos tokens do corpo (o PL/pgSQL não é SQL puro).
        public void Routine(PgSqlText text, int from, int to)
        {
            var at = from + 1;
            while (at < to && !text.Tokens[at].Is("FUNCTION") && !text.Tokens[at].Is("PROCEDURE")) at++;
            var open = at + 1;
            while (open < to && text.Tokens[open].Token is not LeftParen) open++;
            var name = Name(text, at + 1, open, out _);
            if (name is null || open >= to) return;

            var close = Close(text, open, to);
            var parameters = Parameters(text, open + 1, close);

            var body = Body(text, close, to);
            var (reads, writes) = body is null ? ([], []) : BodyTables(body);
            Procedures.Add(new DbProcedure(name.Value.Schema, name.Value.Name, parameters, reads, writes, Source(text.Tokens[from].Line)));
        }

        private static readonly HashSet<string> TypeStarts = new(StringComparer.OrdinalIgnoreCase)
        {
            "character", "double", "timestamp", "time", "bit", "interval", "national", "varying"
        };

        private static List<DbParameter> Parameters(PgSqlText text, int from, int to)
        {
            var parameters = new List<DbParameter>();
            var start = from;
            var depth = 0;
            for (var i = from; i <= to; i++)
            {
                if (i < to && text.Tokens[i].Token is LeftParen) depth++;
                else if (i < to && text.Tokens[i].Token is RightParen) depth--;
                if (i < to && !(depth == 0 && text.Tokens[i].Token is Comma)) continue;
                if (i > start) parameters.Add(Parameter(text, start, i, parameters.Count));
                start = i + 1;
            }
            return parameters;
        }

        private static DbParameter Parameter(PgSqlText text, int from, int to, int index)
        {
            var output = false;
            if (text.Tokens[from].Is("IN") || text.Tokens[from].Is("VARIADIC")) from++;
            else if (text.Tokens[from].Is("OUT") || text.Tokens[from].Is("INOUT")) { output = true; from++; }
            var end = to;
            for (var i = from; i < to; i++)
                if (text.Tokens[i].Is("DEFAULT") || text.Tokens[i].Token is Equal) { end = i; break; }
            string? name = null;
            if (end - from >= 2 && text.Tokens[from].Word is { } first && !TypeStarts.Contains(first)
                && text.Tokens[from + 1].Token is Word)
            {
                name = first;
                from++;
            }
            var type = from < end ? Spaced(text.Sql[text.Tokens[from].Start..(text.Tokens[end - 1].Start + text.Tokens[end - 1].Length)]).ToLowerInvariant() : string.Empty;
            return new DbParameter(name ?? $"${index + 1}", type, output);
        }

        // O corpo da rotina: a string (ou o $$) que vem depois do AS.
        private static string? Body(PgSqlText text, int from, int to)
        {
            var asAt = text.Find("AS", from, to);
            if (asAt < 0) return null;
            for (var i = asAt + 1; i < to; i++)
            {
                var token = text.Tokens[i];
                var raw = text.Sql.Substring(token.Start, token.Length).Trim();
                if (token.Token is DollarQuotedString)
                {
                    var tagEnd = raw.IndexOf('$', 1);
                    if (tagEnd < 0 || raw.Length < 2 * (tagEnd + 1)) return null;
                    return raw[(tagEnd + 1)..^(tagEnd + 1)];
                }
                if (token.Token is SingleQuotedString && raw.Length >= 2) return raw[1..^1].Replace("''", "'");
                if (token.Token is not Word) return null;
            }
            return null;
        }

        private static readonly HashSet<string> FromInsideFunction = new(StringComparer.OrdinalIgnoreCase)
        {
            "extract", "substring", "trim", "overlay", "position"
        };

        // As tabelas que o corpo lê e grava, pelos tokens: INSERT INTO, UPDATE, DELETE FROM e MERGE INTO
        // gravam; FROM, JOIN e USING leem. A tabela do CTE (WITH nome AS) não conta.
        private static (List<string> Reads, List<string> Writes) BodyTables(string body)
        {
            var reads = new List<string>();
            var writes = new List<string>();
            var text = PgSqlText.Read(body, out _);
            if (text is null) return (reads, writes);
            var tokens = text.Tokens;
            var ctes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i + 2 < tokens.Count; i++)
                if (tokens[i].Word is { } cte && tokens[i + 1].Is("AS") && tokens[i + 2].Token is LeftParen) ctes.Add(cte);

            var parens = new Stack<string?>();
            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (token.Token is LeftParen) { parens.Push(i > 0 ? tokens[i - 1].Word : null); continue; }
                if (token.Token is RightParen) { if (parens.Count > 0) parens.Pop(); continue; }

                List<string>? into = null;
                var at = i + 1;
                if ((token.Is("INSERT") || token.Is("MERGE")) && i + 1 < tokens.Count && tokens[i + 1].Is("INTO")) { into = writes; at = i + 2; }
                else if (token.Is("UPDATE") && !(i > 0 && (tokens[i - 1].Is("FOR") || tokens[i - 1].Is("DO") || tokens[i - 1].Is("OR") || tokens[i - 1].Is("ON")))) into = writes;
                else if (token.Is("DELETE") && i + 1 < tokens.Count && tokens[i + 1].Is("FROM")) { into = writes; at = i + 2; }
                else if (token.Is("FROM") && !(i > 0 && tokens[i - 1].Is("DELETE")) && !(parens.Count > 0 && parens.Peek() is { } fn && FromInsideFunction.Contains(fn))) into = reads;
                else if (token.Is("JOIN") || token.Is("USING")) into = reads;
                if (into is null) continue;

                while (at < tokens.Count && (tokens[at].Is("ONLY") || tokens[at].Is("LATERAL"))) at++;
                if (Name(text, at, tokens.Count, out var next) is not { } name) continue;
                // Nome seguido de parêntese no FROM é função de tabela (unnest, generate_series), não tabela.
                if (into == reads && next < tokens.Count && tokens[next].Token is LeftParen) continue;
                if (name.Schema is null && ctes.Contains(name.Name)) continue;
                if (name.Schema is "pg_catalog" or "information_schema") continue;
                var display = DbNames.Display(name.Schema, name.Name, Pg);
                if (!into.Contains(display, StringComparer.OrdinalIgnoreCase)) into.Add(display);
            }
            return (reads, writes);
        }

        private static List<string> ProjectionNames(SqlParser.Ast.Query? query)
        {
            var body = query?.Body;
            while (body is SetExpression.SetOperation operation) body = operation.Left;
            while (body is SetExpression.QueryExpression inner) body = inner.Query.Body;
            if (body is not SetExpression.SelectExpression { Select: var select }) return [];
            var columns = new List<string>();
            foreach (var item in select.Projection ?? [])
            {
                switch (item)
                {
                    case SelectItem.Wildcard or SelectItem.QualifiedWildcard:
                        columns.Add("*");
                        break;
                    case SelectItem.ExpressionWithAlias aliased:
                        columns.Add(aliased.Alias.Value);
                        break;
                    case SelectItem.UnnamedExpression unnamed when PgSqlQueryAnalyzer.Plain(unnamed.Expression) is { } parts:
                        columns.Add(parts[^1]);
                        break;
                }
            }
            return columns;
        }

        // As tabelas que a view lê: as do FROM e das junções, inclusive nas subconsultas, sem os CTEs.
        private static List<string> TablesOf(SqlParser.Ast.Statement.Select select)
        {
            var ctes = PgAst.Descendants(select).OfType<CommonTableExpression>().Select(c => c.Alias.Name.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return PgAst.Descendants(select).OfType<TableFactor.Table>()
                .Where(t => t.Args is null && t.Name is not null && !(PgAst.SchemaOf(t.Name) is null && ctes.Contains(PgAst.Last(t.Name))))
                .Select(t => Display(t.Name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string Display(ObjectName name) => DbNames.Display(PgAst.SchemaOf(name), PgAst.Last(name), Pg);

        private static DeleteAction Delete(ReferentialAction? action) => action switch
        {
            ReferentialAction.Cascade => DeleteAction.Cascade,
            ReferentialAction.SetNull => DeleteAction.SetNull,
            ReferentialAction.SetDefault => DeleteAction.SetDefault,
            ReferentialAction.Restrict => DeleteAction.Restrict,
            _ => DeleteAction.NoAction
        };
    }

    // nome ou schema.nome a partir de from; next: o token depois do nome.
    private static (string? Schema, string Name)? Name(PgSqlText text, int from, int to, out int next)
    {
        next = from;
        if (from >= to || text.Tokens[from].Word is not { } first) return null;
        if (from + 2 < to && text.Tokens[from + 1].Token is Period && text.Tokens[from + 2].Word is { } second)
        {
            next = from + 3;
            return (first, second);
        }
        next = from + 1;
        return (null, first);
    }

    private static int Close(PgSqlText text, int open, int to)
    {
        var depth = 0;
        for (var i = open; i < to; i++)
        {
            if (text.Tokens[i].Token is LeftParen) depth++;
            else if (text.Tokens[i].Token is RightParen && --depth == 0) return i;
        }
        return to;
    }

    private static string Spaced(string text) => string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private sealed class TableBuilder(string? schema, string name, SchemaSource source)
    {
        public bool Defined { get; set; }
        public List<ColumnBuilder> Columns { get; } = [];
        public DbPrimaryKey? PrimaryKey { get; set; }
        public List<DbForeignKey> ForeignKeys { get; } = [];
        public List<DbIndex> Indexes { get; } = [];

        public void MarkIdentity(string column)
        {
            var index = Columns.FindIndex(c => c.Name.Equals(column, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) Columns[index] = Columns[index] with { Identity = true };
        }

        public DbTable Build()
        {
            var keyColumns = PrimaryKey?.Columns.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            // Sem nome escrito, o nome que o PostgreSQL dá (o mesmo que o catálogo do banco vivo mostra):
            // tabela_pkey, tabela_coluna_key, tabela_coluna_fkey e tabela_coluna_idx.
            var primaryKey = PrimaryKey is { Name: null } pk ? pk with { Name = $"{name}_pkey" } : PrimaryKey;
            var foreignKeys = ForeignKeys.Select(f => f.Name is null ? f with { Name = $"{name}_{string.Join("_", f.Columns)}_fkey" } : f).ToList();
            var indexes = Indexes.Select(i => i.Name is null ? i with { Name = $"{name}_{string.Join("_", i.Columns)}_{(i.Unique ? "key" : "idx")}" } : i).ToList();
            // Sem NULL nem NOT NULL, a coluna aceita nulo (padrão do PostgreSQL), menos a da chave primária.
            var columns = Columns.Select(c => new DbColumn(c.Name, c.Type,
                keyColumns.Contains(c.Name) ? false : c.Nullable ?? (Defined ? true : null),
                c.Default, c.Identity, [c.Source])).ToList();
            return new DbTable(schema, name, columns, primaryKey, foreignKeys, indexes, [source]);
        }
    }

    private sealed record ColumnBuilder(string Name, string? Type, bool? Nullable, string? Default, bool Identity, SchemaSource Source);
}
