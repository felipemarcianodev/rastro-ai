using System.Text.RegularExpressions;
using SqlParser;
using SqlParser.Ast;
using Rastro.Core.Database;
using AstQuery = SqlParser.Ast.Query;

namespace Rastro.Extractor.Database;

// Achados de gargalo numa consulta do PostgreSQL (Fase 42), pela árvore do SqlParserCS (MIT, ADR-006) e
// pelo esquema lido do código. As mesmas regras do SQL Server (Fase 40), no dialeto do PostgreSQL: o
// limite é LIMIT ou FETCH, a subconsulta correlacionada vira LEFT JOIN LATERAL, e entram o ILIKE com o
// curinga no começo e a conversão com :: na coluna do filtro. Cada regra olha a árvore, nunca o texto
// por expressão regular: comentário, string e aspas não enganam. Sem IA (ADR-001).
public sealed partial class PgSqlQueryAnalyzer : ISqlQueryAnalyzer
{
    private const int MaxSnippet = 120;

    public SqlDialect Dialect => SqlDialect.PostgreSql;

    public QueryAnalysis Analyze(string sql, DbSchema? schema)
    {
        var text = PgSqlText.Read(sql, out var tokenError);
        var statements = text is null ? null : PgSqlText.Parse(sql, out tokenError);
        if (text is null || statements is null)
            return new QueryAnalysis(Dialect, 0, [], [],
                new QueryParseError(tokenError?.Message ?? "Texto vazio.", tokenError?.Line ?? 1, tokenError?.Column ?? 1));

        var run = new Run(text, schema);
        for (var i = 0; i < statements.Count; i++)
        {
            var range = statements.Count == text.Statements.Count ? text.Statements[i] : (0, text.Tokens.Count);
            run.Statement(statements[i], range);
        }
        return new QueryAnalysis(Dialect, statements.Count, run.Findings(), run.Tables, null);
    }

    // Variável da consulta: $1 ou @p0. O leitor do PostgreSQL lê o @p0 (o parâmetro do Npgsql e o que o
    // Rastro tira do FromSql) como o operador @ sobre um nome, e o nome não é coluna.
    internal static bool IsVariable(object node) =>
        node is Expression.LiteralValue { Value: Value.Placeholder }
        || node is Expression.UnaryOp { Op: UnaryOperator.PGAbs, Expression: Expression.Identifier };

    // Coluna pura: nome ou nome com prefixo, sem função em volta.
    internal static IReadOnlyList<string>? Plain(Expression? expression)
    {
        while (expression is Expression.Nested n) expression = n.Expression;
        return Parts(expression);
    }

    internal static IReadOnlyList<string>? Parts(object? node) => node switch
    {
        Expression.Identifier id => [id.Ident.Value],
        Expression.CompoundIdentifier compound when compound.Idents.Count > 0 => compound.Idents.Select(i => i.Value).ToList(),
        _ => null
    };

    // As colunas de um nó, sem entrar em subconsulta (ela tem as próprias tabelas) nem em variável.
    internal static List<Expression> Columns(object node)
    {
        var found = new List<Expression>();
        if (Parts(node) is not null) found.Add((Expression)node);
        if (Parts(node) is not null || IsVariable(node)) return found;
        foreach (var child in PgAst.Descendants(node, n => n is Query || IsVariable(n) || Parts(n) is not null))
            if (Parts(child) is not null) found.Add((Expression)child);
        return found;
    }

    private static bool IsComparison(BinaryOperator op) =>
        op is BinaryOperator.Eq or BinaryOperator.NotEq or BinaryOperator.Lt or BinaryOperator.Gt or BinaryOperator.LtEq or BinaryOperator.GtEq;

    // Uma análise: o texto, o esquema e o que já foi achado.
    private sealed class Run(PgSqlText text, DbSchema? schema)
    {
        private readonly SqlDialect _dialect = schema?.Dialect ?? SqlDialect.PostgreSql;
        private readonly Dictionary<string, DbTable> _tables = (schema?.Tables ?? [])
            .GroupBy(t => DbNames.Key(t.Schema, t.Name, schema!.Dialect))
            .ToDictionary(g => g.Key, g => g.First());
        private readonly HashSet<string> _views = (schema?.Views ?? [])
            .Select(v => DbNames.Key(v.Schema, v.Name, schema!.Dialect))
            .ToHashSet();
        private readonly HashSet<string> _ctes = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<QueryFinding> _findings = [];
        private readonly List<(string Column, QueryFinding Finding)> _indexFindings = [];
        private readonly HashSet<string> _covered = new(StringComparer.OrdinalIgnoreCase);
        private (int From, int To) _range;

        public List<QueryTableUse> Tables { get; } = [];

        public List<QueryFinding> Findings() =>
            _findings
                .Concat(_indexFindings.Where(f => !_covered.Contains(f.Column)).Select(f => f.Finding))
                .OrderBy(f => f.Severity)
                .ThenBy(f => f.Start)
                .ThenBy(f => f.Kind)
                .ToList();

        public void Statement(Statement statement, (int From, int To) range)
        {
            _range = range;
            switch (statement)
            {
                case Statement.Select select:
                    Query(select.Query, null, outermost: true, inExists: false);
                    break;
                case Statement.Update update:
                    var targets = new List<TableWithJoins> { update.Table };
                    if (update.From is not null) targets.Add(update.From);
                    Write(update.Table, targets, update.From is not null, update.Selection, $"UPDATE {update.Table.ToSql()}", "UPDATE", "altera", null);
                    foreach (var assignment in update.Assignments ?? [])
                        foreach (var expression in PgAst.Children(assignment).OfType<Expression>())
                            Subqueries(expression, null);
                    break;
                case Statement.Delete { DeleteOperation: { } delete }:
                    var from = delete.From?.From ?? [];
                    if (from.Count == 0) break;
                    var joined = from.Count > 1 || from[0].Joins is { Count: > 0 } || delete.Using is not null;
                    Write(from[0], from.ToList(), joined, delete.Selection, $"DELETE FROM {from[0].ToSql()}", "DELETE", "apaga", delete.Using);
                    break;
                case Statement.Insert { InsertOperation: { } insert }:
                    Use(insert.Name, null, insert.Name);
                    if (insert.Source?.Query is { Body: not SetExpression.ValuesExpression } source)
                        Query(source, null, outermost: true, inExists: false);
                    break;
            }
        }

        // UPDATE e DELETE: sem WHERE, a tabela inteira muda. Com outra tabela (FROM no UPDATE, USING no
        // DELETE), a junção limita as linhas, e o achado não vale.
        private void Write(TableWithJoins target, List<TableWithJoins> tables, bool joined, Expression? where, string written, string verb, string action, TableFactor? extra)
        {
            var scope = new Scope(null);
            foreach (var table in tables) Add(scope, table);
            if (extra is not null) AddFactor(scope, extra);
            if (where is null && !joined)
            {
                var table = scope.Refs.FirstOrDefault();
                var span = text.Locate(written, _range.From, _range.To);
                Report(QueryFindingKind.WriteWithoutWhere, QuerySeverity.High, span,
                    $"{verb} sem WHERE {action} todas as linhas de {table?.Name ?? "a tabela"}. Se é isso mesmo, deixe escrito; se não, falta o filtro.",
                    table?.Table is null ? null : table.Name);
            }
            if (where is not null)
            {
                CommaJoins(scope, tables, where);
                Predicate(where, scope, join: false);
            }
        }

        private Scope? Query(Query? query, Scope? parent, bool outermost, bool inExists)
        {
            if (query is null) return null;
            if (query.With?.CteTables is { } ctes)
            {
                foreach (var cte in ctes) _ctes.Add(cte.Alias.Name.Value);
                foreach (var cte in ctes) Query(cte.Query, null, outermost: false, inExists: false);
            }
            var scope = Body(query.Body, parent, inExists);
            if (outermost && query.OrderBy is { Expressions.Count: > 0 } order && query.Limit is null && query.Fetch is null)
            {
                var written = "ORDER BY " + string.Join(", ", order.Expressions.Select(e => e.ToSql()));
                Report(QueryFindingKind.OrderByWithoutLimit, QuerySeverity.Low, text.Locate(written, _range.From, _range.To, Used(QueryFindingKind.OrderByWithoutLimit)),
                    "Ordena todas as linhas que o filtro deixou, sem LIMIT nem FETCH: o custo cresce com a tabela. Se a tela mostra uma página, limite.");
            }
            return scope;
        }

        private Scope? Body(SetExpression? body, Scope? parent, bool inExists) => body switch
        {
            SetExpression.SelectExpression select => Specification(select.Select, parent, inExists),
            SetExpression.SetOperation operation => First(Body(operation.Left, parent, inExists), Body(operation.Right, parent, inExists)),
            SetExpression.QueryExpression inner => Query(inner.Query, parent, outermost: false, inExists),
            _ => null
        };

        private static Scope? First(Scope? first, Scope? _) => first;

        private Scope Specification(Select select, Scope? parent, bool inExists)
        {
            var scope = new Scope(parent);
            var from = select.From?.ToList() ?? [];
            foreach (var table in from) Add(scope, table);

            foreach (var item in select.Projection ?? [])
            {
                switch (item)
                {
                    case SelectItem.Wildcard when !inExists:
                        SelectStar(null, "*", scope);
                        break;
                    case SelectItem.QualifiedWildcard qualified when !inExists:
                        SelectStar(PgAst.Last(qualified.Name), qualified.ToSql(), scope);
                        break;
                    case SelectItem.UnnamedExpression unnamed:
                        Subqueries(unnamed.Expression, scope, correlated: true);
                        break;
                    case SelectItem.ExpressionWithAlias aliased:
                        Subqueries(aliased.Expression, scope, correlated: true);
                        break;
                }
            }

            if (select.Distinct is DistinctFilter.Distinct && scope.Refs.Count > 1)
                Report(QueryFindingKind.DistinctOverJoin, QuerySeverity.Low, text.Locate("DISTINCT", _range.From, _range.To, Used(QueryFindingKind.DistinctOverJoin)),
                    "DISTINCT numa consulta com junção costuma esconder linhas repetidas pela junção: o banco junta tudo e depois ordena para tirar as repetidas. Confira a condição da junção ou troque a junção por EXISTS.");

            if (select.Selection is { } where)
            {
                CommaJoins(scope, from, where);
                Predicate(where, scope, join: false);
            }
            else if (from.Count > 1)
                CommaJoins(scope, from, null);
            return scope;
        }

        private void SelectStar(string? qualifier, string written, Scope scope)
        {
            var refs = qualifier is null ? scope.Refs : scope.Refs.Where(r => r.Matches(qualifier)).ToList();
            var known = refs.Where(r => r.Table is not null).ToList();
            string message;
            if (known.Count > 0 && known.Count == refs.Count)
            {
                var count = known.Sum(r => r.Table!.Columns.Count);
                var personal = known.SelectMany(r => r.Table!.Columns.Where(c => c.Personal).Select(c => $"{r.Name}.{c.Name}")).ToList();
                var tables = Join(known.Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
                message = $"Traz todas as {count} colunas de {tables}"
                    + (personal.Count == 0 ? "" : $", inclusive {Join(personal)}, {(personal.Count == 1 ? "marcada" : "marcadas")} como dado pessoal")
                    + ". Liste só as colunas que a tela usa: menos dado lido e trafegado, e a consulta não muda quando entra coluna nova.";
            }
            else
                message = "Traz todas as colunas, inclusive as que ninguém usa, e muda sozinha quando entra coluna nova. Liste só as colunas que a tela usa.";
            Report(QueryFindingKind.SelectStar, QuerySeverity.Medium, LocateStar(written), message, known.Count == 1 && refs.Count == 1 ? known[0].Name : null);
        }

        // O * da lista do SELECT: o primeiro que vem logo depois do SELECT, de uma vírgula ou do prefixo.
        private (int Start, int Length, int Line) LocateStar(string written)
        {
            if (written != "*") return text.Locate(written, _range.From, _range.To, Used(QueryFindingKind.SelectStar));
            var used = Used(QueryFindingKind.SelectStar);
            for (var i = _range.From + 1; i < _range.To; i++)
            {
                if (text.Tokens[i].Token is not SqlParser.Tokens.Multiply || used.Contains(text.Tokens[i].Start)) continue;
                var before = text.Tokens[i - 1];
                if (before.Is("SELECT") || before.Is("DISTINCT") || before.Is("ALL") || before.Token is SqlParser.Tokens.Comma)
                    return text.Span(i, i);
            }
            return text.Locate("*", _range.From, _range.To, used);
        }

        private void Add(Scope scope, TableWithJoins table)
        {
            if (table.Relation is not null) AddFactor(scope, table.Relation);
            foreach (var join in table.Joins ?? [])
            {
                var before = scope.Refs.Count;
                if (join.Relation is not null) AddFactor(scope, join.Relation);
                var second = scope.Refs.Skip(before).ToList();
                switch (join.JoinOperator)
                {
                    case JoinOperator.CrossJoin:
                        var left = scope.Refs.Take(before).ToList();
                        Report(QueryFindingKind.JoinWithoutCondition, QuerySeverity.High, text.Locate("CROSS JOIN " + join.Relation?.ToSql(), _range.From, _range.To, Used(QueryFindingKind.JoinWithoutCondition)),
                            $"CROSS JOIN junta cada linha de {Join(left.Select(r => r.Name).ToList())} com cada linha de {Join(second.Select(r => r.Name).ToList())}: o resultado é o produto das tabelas. Se não é isso, use JOIN com ON.",
                            second.FirstOrDefault(r => r.Table is not null)?.Name);
                        break;
                    default:
                        if (join.JoinOperator is not null && PgAst.Children(join.JoinOperator).OfType<JoinConstraint.On>().FirstOrDefault() is { } on)
                        {
                            if (!Links(on.Expression, scope, second))
                                Report(QueryFindingKind.JoinWithoutCondition, QuerySeverity.High, text.Locate(on.Expression.ToSql(), _range.From, _range.To, Used(QueryFindingKind.JoinWithoutCondition)),
                                    $"O ON não compara uma coluna de {Join(second.Select(r => r.Name).ToList())} com as das outras tabelas: a junção vira o produto das tabelas, cada linha com cada linha.",
                                    second.FirstOrDefault(r => r.Table is not null)?.Name);
                            Predicate(on.Expression, scope, join: true);
                        }
                        break;
                }
            }
        }

        private void AddFactor(Scope scope, TableFactor factor)
        {
            switch (factor)
            {
                case TableFactor.Table { Args: null } table:
                    scope.Refs.Add(Use(table.Name, table.Alias?.Name.Value, table));
                    break;
                case TableFactor.Derived derived:
                    Query(derived.SubQuery, scope, outermost: false, inExists: false);
                    scope.Refs.Add(new TableRef(derived.Alias?.Name.Value ?? "(subconsulta)", null, derived.Alias?.Name.Value, derived, Known: false));
                    break;
                case TableFactor.NestedJoin { TableWithJoins: { } nested }:
                    Add(scope, nested);
                    break;
                default:
                    // Função de tabela (generate_series, unnest, jsonb_to_recordset): colunas desconhecidas.
                    scope.Opaque = true;
                    break;
            }
        }

        private TableRef Use(ObjectName name, string? alias, object fragment)
        {
            var baseName = PgAst.Last(name);
            var schemaName = PgAst.SchemaOf(name);
            var display = DbNames.Display(schemaName, baseName, _dialect);
            if (schemaName is null && _ctes.Contains(baseName))
                return new TableRef(display, null, alias, fragment, Known: false, BaseName: baseName);

            var key = DbNames.Key(schemaName, baseName, _dialect);
            var table = _tables.GetValueOrDefault(key);
            var inSchema = table is not null || _views.Contains(key);
            if (!Tables.Any(t => t.Name.Equals(display, StringComparison.OrdinalIgnoreCase)))
                Tables.Add(new QueryTableUse(display, alias, inSchema));
            return new TableRef(display, table, alias, fragment, Known: inSchema, BaseName: baseName);
        }

        private bool Links(Expression on, Scope scope, List<TableRef> second)
        {
            var columns = Columns(on);
            if (columns.Count == 0) return false;
            var refs = new HashSet<TableRef>();
            foreach (var column in columns)
            {
                var resolved = scope.Resolve(Parts(column)!);
                if (resolved.Ref is null) return true;
                refs.Add(resolved.Ref);
            }
            return refs.Any(second.Contains) && refs.Any(r => !second.Contains(r));
        }

        // FROM a, b: cada tabela depois da primeira precisa de uma comparação no WHERE que a ligue a outra.
        private void CommaJoins(Scope scope, List<TableWithJoins> from, Expression? where)
        {
            if (from.Count < 2) return;
            var comparisons = where is null ? [] : Comparisons(where);
            for (var i = 1; i < from.Count; i++)
            {
                if (from[i].Relation is not TableFactor.Table named) continue;
                var mine = scope.Refs.FirstOrDefault(r => ReferenceEquals(r.Fragment, named));
                if (mine is null) continue;
                var linked = false;
                foreach (var comparison in comparisons)
                {
                    var left = Columns(comparison.Left).Select(c => scope.Resolve(Parts(c)!).Ref).ToList();
                    var right = Columns(comparison.Right).Select(c => scope.Resolve(Parts(c)!).Ref).ToList();
                    if (left.Contains(null) || right.Contains(null)) { linked = true; break; }
                    if ((left.Contains(mine) && right.Any(r => r != mine)) || (right.Contains(mine) && left.Any(r => r != mine))) { linked = true; break; }
                }
                if (linked) continue;
                Report(QueryFindingKind.JoinWithoutCondition, QuerySeverity.High, text.Locate(named.ToSql(), _range.From, _range.To, Used(QueryFindingKind.JoinWithoutCondition)),
                    $"{mine.Name} entra no FROM pela vírgula, sem uma comparação no WHERE que a ligue às outras tabelas: o resultado é o produto das tabelas, cada linha com cada linha. Use JOIN com ON.",
                    mine.Table is null ? null : mine.Name);
            }
        }

        private void Predicate(Expression expression, Scope scope, bool join)
        {
            switch (expression)
            {
                case Expression.Nested nested:
                    Predicate(nested.Expression, scope, join);
                    break;
                case Expression.UnaryOp { Op: UnaryOperator.Not } not:
                    Predicate(not.Expression, scope, join);
                    break;
                case Expression.BinaryOp { Op: BinaryOperator.Or } or:
                    var operands = new List<Expression>();
                    Flatten(or, operands);
                    OrAcross(or, operands, scope);
                    foreach (var operand in operands) Predicate(operand, scope, join);
                    break;
                case Expression.BinaryOp { Op: BinaryOperator.And } and:
                    Predicate(and.Left, scope, join);
                    Predicate(and.Right, scope, join);
                    break;
                case Expression.BinaryOp comparison when IsComparison(comparison.Op):
                    Side(comparison.Left, scope, join);
                    Side(comparison.Right, scope, join);
                    break;
                case Expression.Like { Expression: not null } like:
                    LikeLike(like, like.Expression, like.Pattern, "LIKE", scope, join);
                    break;
                case Expression.ILike { Expression: not null } ilike:
                    LikeLike(ilike, ilike.Expression, ilike.Pattern, "ILIKE", scope, join);
                    break;
                case Expression.InList list:
                    Side(list.Expression, scope, join);
                    foreach (var value in list.List ?? []) Subqueries(value, scope, correlated: true);
                    break;
                case Expression.InSubquery inSubquery:
                    if (inSubquery.Expression is not null) Side(inSubquery.Expression, scope, join);
                    var inner = Query(inSubquery.SubQuery, scope, outermost: false, inExists: false);
                    if (inSubquery.Negated) NotIn(inSubquery, inner);
                    break;
                case Expression.Between between:
                    Side(between.Expression, scope, join);
                    Subqueries(between.Low, scope, correlated: true);
                    Subqueries(between.High, scope, correlated: true);
                    break;
                case Expression.IsNull isNull:
                    Side(isNull.Expression, scope, join);
                    break;
                case Expression.IsNotNull isNotNull:
                    Side(isNotNull.Expression, scope, join);
                    break;
                case Expression.Exists exists:
                    // A subconsulta do EXISTS é feita para citar a de fora: o banco a junta (semi-junção).
                    Query(exists.SubQuery, scope, outermost: false, inExists: true);
                    break;
                case Expression.AnyOp any:
                    Side(any.Left, scope, join);
                    Subqueries(any.Right, scope, correlated: true);
                    break;
                case Expression.AllOp all:
                    Side(all.Left, scope, join);
                    Subqueries(all.Right, scope, correlated: true);
                    break;
            }
        }

        private void LikeLike(Expression like, Expression value, Expression pattern, string op, Scope scope, bool join)
        {
            if (LeadingWildcard(pattern) is { } literal && Plain(value) is { } column)
                LikeFinding(like, column, literal, op, scope);
            else
                Side(value, scope, join);
            Subqueries(pattern, scope, correlated: true);
        }

        private static void Flatten(Expression expression, List<Expression> operands)
        {
            var current = expression;
            while (current is Expression.Nested n) current = n.Expression;
            if (current is Expression.BinaryOp { Op: BinaryOperator.Or } or)
            {
                Flatten(or.Left, operands);
                Flatten(or.Right, operands);
            }
            else operands.Add(expression);
        }

        private void OrAcross(Expression or, List<Expression> operands, Scope scope)
        {
            var sets = operands.Select(o => Columns(o).Select(c => ColumnKey(c, scope)).ToHashSet(StringComparer.OrdinalIgnoreCase)).ToList();
            if (sets.Any(s => s.Count == 0) || sets.All(s => s.SetEquals(sets[0]))) return;
            var names = operands.SelectMany(Columns).Select(Written).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Report(QueryFindingKind.OrAcrossColumns, QuerySeverity.Medium, text.Locate(or.ToSql(), _range.From, _range.To, Used(QueryFindingKind.OrAcrossColumns)),
                $"O OR liga filtros em colunas diferentes ({Join(names)}): um índice só não atende os dois lados, e o banco costuma ler a tabela inteira. Separe em consultas ligadas por UNION ALL, ou confira o plano.");
        }

        // Um lado de comparação: coluna pura (índice), coluna embrulhada em função ou conversão (achado) ou subconsulta.
        private void Side(Expression expression, Scope scope, bool join)
        {
            if (Plain(expression) is { } column)
            {
                IndexCheck(expression, column, scope, join);
                return;
            }
            Subqueries(expression, scope, correlated: true);
            if (expression is Expression.LiteralValue or Expression.Subquery || IsVariable(expression)) return;

            var own = Columns(expression).Where(c => scope.Resolve(Parts(c)!).Depth == 0).ToList();
            if (own.Count == 0) return;
            var first = own[0];
            var resolved = scope.Resolve(Parts(first)!);
            _covered.Add(ColumnKey(first, scope));
            var noIndex = resolved is { Ref.Table: { } table, Column: { } name } && table.Columns.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) && !HasIndex(table, name)
                ? $" E {resolved.Ref.Name}.{name} nem tem índice."
                : "";
            var cast = Unwrap(expression) is Expression.Cast { Kind: CastKind.DoubleColon };
            var advice = cast
                ? "compare a coluna pura e converta o valor, não a coluna (com uma faixa, no caso de data)"
                : "compare a coluna pura (com uma faixa, no caso de data)";
            Report(QueryFindingKind.FunctionOnFilterColumn, QuerySeverity.High, text.Locate(expression.ToSql(), _range.From, _range.To, Used(QueryFindingKind.FunctionOnFilterColumn)),
                $"{Wrapper(expression)} roda em cada linha e impede o uso de índice em {Written(first)}: {advice}.{noIndex}",
                resolved.Ref?.Table is null ? null : resolved.Ref.Name);
        }

        private void LikeFinding(Expression like, IReadOnlyList<string> column, string pattern, string op, Scope scope)
        {
            var resolved = scope.Resolve(column);
            _covered.Add(ColumnKey(column, scope));
            var index = resolved is { Ref.Table: { } table, Column: { } name } ? IndexOn(table, name) : null;
            var tail = index is null ? "." : $": o índice {index} existe, mas não ajuda.";
            var search = op == "ILIKE"
                ? " Se a busca é por um pedaço do texto, um índice trigrama (pg_trgm, GIN) atende o ILIKE."
                : " Se a busca é pelo fim do texto, guarde uma coluna com o texto invertido ou use a busca de texto completo.";
            Report(QueryFindingKind.LeadingWildcardLike, QuerySeverity.Medium, text.Locate(like.ToSql(), _range.From, _range.To, Used(QueryFindingKind.LeadingWildcardLike)),
                $"Com o curinga no começo ('{pattern}'), o {op} lê todos os valores de {string.Join(".", column)}{tail}{search}",
                resolved.Ref?.Table is null ? null : resolved.Ref.Name);
        }

        private void NotIn(Expression.InSubquery inSubquery, Scope? inner)
        {
            if (inner is null || FirstSelect(inSubquery.SubQuery) is not { } select) return;
            var item = select.Projection?.FirstOrDefault();
            var expression = item switch
            {
                SelectItem.UnnamedExpression u => u.Expression,
                SelectItem.ExpressionWithAlias a => a.Expression,
                _ => null
            };
            if (Plain(expression) is not { } column) return;
            var resolved = inner.Resolve(column);
            if (resolved is not { Ref.Table: { } table, Column: { } name }) return;
            if (table.Columns.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is not { Nullable: true }) return;
            Report(QueryFindingKind.NotInNullable, QuerySeverity.High, text.Locate(inSubquery.ToSql(), _range.From, _range.To, Used(QueryFindingKind.NotInNullable)),
                $"{resolved.Ref.Name}.{name} aceita nulo: se a subconsulta trouxer um nulo, o NOT IN não devolve linha nenhuma. Use NOT EXISTS ou tire o nulo na subconsulta (WHERE {name} IS NOT NULL).",
                resolved.Ref.Name);
        }

        private void IndexCheck(Expression expression, IReadOnlyList<string> column, Scope scope, bool join)
        {
            var resolved = scope.Resolve(column);
            if (resolved is not { Depth: 0, Ref.Table: { } table, Column: { } name }) return;
            if (!table.Columns.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) || HasIndex(table, name)) return;
            var key = ColumnKey(column, scope);
            if (_indexFindings.Any(f => f.Column.Equals(key, StringComparison.OrdinalIgnoreCase))) return;

            var indexes = IndexList(table);
            var have = indexes.Count == 0 ? "a tabela não tem índice nenhum" : $"o esquema tem {Join(indexes)}";
            var what = join ? "na junção" : "no filtro";
            var span = text.Locate(expression.ToSql(), _range.From, _range.To);
            _indexFindings.Add((key, new QueryFinding(QueryFindingKind.ColumnWithoutIndex, QuerySeverity.Medium, span.Start, span.Length, span.Line, Snippet(span.Start, span.Length),
                $"{resolved.Ref.Name}.{name} está {what} e não tem índice que comece por ela ({have}): o banco lê a tabela inteira para achar as linhas.",
                resolved.Ref.Name)));
        }

        // Subconsulta escalar dentro de uma expressão (lista do SELECT, lado de comparação): é analisada
        // e, se cita a consulta de fora, vira achado.
        private void Subqueries(Expression? expression, Scope? scope, bool correlated = false)
        {
            if (expression is null) return;
            var found = expression is Expression.Subquery self
                ? [self]
                : PgAst.Descendants(expression, n => n is Expression.Subquery or AstQuery).OfType<Expression.Subquery>().ToList();
            foreach (var subquery in found)
            {
                var inner = Query(subquery.Query, scope, outermost: false, inExists: false);
                if (!correlated || inner is null || scope is null) continue;
                var outer = Columns(subquery.Query).FirstOrDefault(c => inner.Resolve(Parts(c)!).Depth > 0);
                if (outer is null) continue;
                Report(QueryFindingKind.CorrelatedSubquery, QuerySeverity.Medium, text.Locate(subquery.ToSql(), _range.From, _range.To, Used(QueryFindingKind.CorrelatedSubquery)),
                    $"A subconsulta cita {Written(outer)}, da consulta de fora: o banco pode rodá-la uma vez para cada linha. Troque por uma junção (JOIN com GROUP BY) ou por LEFT JOIN LATERAL.");
            }
        }

        private static Select? FirstSelect(Query query)
        {
            var body = query.Body;
            while (true)
            {
                switch (body)
                {
                    case SetExpression.SelectExpression s: return s.Select;
                    case SetExpression.SetOperation o: body = o.Left; continue;
                    case SetExpression.QueryExpression q: body = q.Query.Body; continue;
                    default: return null;
                }
            }
        }

        private static List<Expression.BinaryOp> Comparisons(Expression where)
        {
            var list = new List<Expression.BinaryOp>();
            if (where is Expression.BinaryOp root && IsComparison(root.Op)) list.Add(root);
            list.AddRange(PgAst.Descendants(where, n => n is AstQuery).OfType<Expression.BinaryOp>().Where(b => IsComparison(b.Op)));
            return list;
        }

        private static bool HasIndex(DbTable table, string column) =>
            (table.PrimaryKey?.Columns.FirstOrDefault()?.Equals(column, StringComparison.OrdinalIgnoreCase) ?? false)
            || table.Indexes.Any(i => i.Columns.FirstOrDefault()?.Equals(column, StringComparison.OrdinalIgnoreCase) ?? false);

        private static string? IndexOn(DbTable table, string column)
        {
            if (table.Indexes.FirstOrDefault(i => i.Columns.FirstOrDefault()?.Equals(column, StringComparison.OrdinalIgnoreCase) ?? false) is { } index)
                return index.Name ?? $"de {column}";
            return table.PrimaryKey?.Columns.FirstOrDefault()?.Equals(column, StringComparison.OrdinalIgnoreCase) == true ? table.PrimaryKey.Name ?? "da chave primária" : null;
        }

        private static List<string> IndexList(DbTable table)
        {
            var list = new List<string>();
            if (table.PrimaryKey is { } pk) list.Add($"{pk.Name ?? "a chave primária"} ({string.Join(", ", pk.Columns)})");
            list.AddRange(table.Indexes.Select(i => $"{i.Name ?? "um índice"} ({string.Join(", ", i.Columns)})"));
            return list;
        }

        private static Expression Unwrap(Expression expression)
        {
            while (expression is Expression.Nested n) expression = n.Expression;
            return expression;
        }

        private static string Wrapper(Expression expression) => Unwrap(expression) switch
        {
            Expression.Function f => PgAst.Last(f.Name).ToUpperInvariant() + "()",
            Expression.Cast { Kind: CastKind.DoubleColon } c => $"A conversão ::{c.DataType.ToSql().ToLowerInvariant()}",
            Expression.Cast => "CAST",
            Expression.Extract => "EXTRACT()",
            Expression.BinaryOp or Expression.UnaryOp => "A conta",
            _ => "A expressão"
        };

        // '%@gmail.com', ou '%' || $1: o curinga no começo do padrão.
        private static string? LeadingWildcard(Expression pattern)
        {
            var current = pattern;
            while (true)
            {
                switch (current)
                {
                    case Expression.Nested n:
                        current = n.Expression;
                        continue;
                    case Expression.BinaryOp { Op: BinaryOperator.StringConcat } concat:
                        current = concat.Left;
                        continue;
                    case Expression.LiteralValue { Value: Value.SingleQuotedString s } when s.Value.StartsWith('%') || s.Value.StartsWith('_'):
                        return s.Value;
                    case Expression.LiteralValue { Value: Value.EscapedStringLiteral e } when e.Value.StartsWith('%') || e.Value.StartsWith('_'):
                        return e.Value;
                    default:
                        return null;
                }
            }
        }

        private static string Written(Expression column) => string.Join(".", Parts(column)!);

        private static string ColumnKey(Expression column, Scope scope) => ColumnKey(Parts(column)!, scope);

        private static string ColumnKey(IReadOnlyList<string> column, Scope scope)
        {
            var resolved = scope.Resolve(column);
            return resolved is { Ref: { } r, Column: { } name } ? $"{r.Name}.{name}" : string.Join(".", column);
        }

        private static string Join(IReadOnlyList<string> items) => items.Count switch
        {
            0 => string.Empty,
            1 => items[0],
            _ => string.Join(", ", items.Take(items.Count - 1)) + " e " + items[^1]
        };

        private HashSet<int> Used(QueryFindingKind kind) => _findings.Where(f => f.Kind == kind).Select(f => f.Start).ToHashSet();

        private void Report(QueryFindingKind kind, QuerySeverity severity, (int Start, int Length, int Line) span, string message, string? table = null)
        {
            if (_findings.Any(f => f.Kind == kind && f.Start == span.Start && f.Length == span.Length)) return;
            _findings.Add(new QueryFinding(kind, severity, span.Start, span.Length, Math.Max(span.Line, 1), Snippet(span.Start, span.Length), message, table));
        }

        private string Snippet(int start, int length)
        {
            var clean = Spaces().Replace(text.Sql.Substring(start, length), " ").Trim();
            return clean.Length <= MaxSnippet ? clean : clean[..(MaxSnippet - 1)] + "…";
        }
    }

    // Uma tabela do FROM. Table nulo: fora do esquema (ou subconsulta, CTE, função de tabela).
    private sealed record TableRef(string Name, DbTable? Table, string? Alias, object Fragment, bool Known, string? BaseName = null)
    {
        public bool Matches(string qualifier) =>
            Alias is not null
                ? Alias.Equals(qualifier, StringComparison.OrdinalIgnoreCase)
                : (BaseName ?? Name).Equals(qualifier, StringComparison.OrdinalIgnoreCase) || Name.Equals(qualifier, StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct Resolution(TableRef? Ref, string? Column, int Depth);

    private sealed class Scope(Scope? parent)
    {
        public List<TableRef> Refs { get; } = [];
        public bool Opaque { get; set; }
        public Scope? Parent { get; } = parent;

        public Resolution Resolve(IReadOnlyList<string> ids)
        {
            if (ids.Count == 0) return new(null, null, 0);
            var name = ids[^1];
            var depth = 0;
            for (var scope = this; scope is not null; scope = scope.Parent, depth++)
            {
                if (ids.Count >= 2)
                {
                    var qualifier = ids[^2];
                    if (scope.Refs.FirstOrDefault(r => r.Matches(qualifier)) is { } match) return new(match, name, depth);
                    continue;
                }

                var owners = scope.Refs.Where(r => r.Table?.Columns.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) == true).ToList();
                if (owners.Count == 1) return new(owners[0], name, depth);
                if (owners.Count > 1) return new(null, name, depth);
                if (scope.Refs.Count == 1 && scope.Refs[0].Table is null && !scope.Opaque) return new(scope.Refs[0], name, depth);
                if (scope.Opaque || scope.Refs.Any(r => r.Table is null)) return new(null, name, depth);
            }
            return new(null, name, 0);
        }
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
