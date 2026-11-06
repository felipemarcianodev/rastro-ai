using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Rastro.Core.Database;

namespace Rastro.Extractor.Database;

// Um analisador de consulta por dialeto (ADR-006), como o leitor de esquema: o SQL Server pelo ScriptDom
// (Fase 40) e o PostgreSQL por um a mais (Fase 42). Lê o texto, nunca executa nada.
public interface ISqlQueryAnalyzer
{
    SqlDialect Dialect { get; }

    // schema: o esquema do repositório, para cruzar coluna com índice e com nulo; nulo, só as regras do texto.
    QueryAnalysis Analyze(string sql, DbSchema? schema);
}

public static class SqlQueryAnalyzers
{
    private static readonly ISqlQueryAnalyzer[] All = [new TSqlQueryAnalyzer(), new PgSqlQueryAnalyzer()];

    public static ISqlQueryAnalyzer? For(SqlDialect dialect) => All.FirstOrDefault(a => a.Dialect == dialect);
}

// Achados de gargalo numa consulta T-SQL (Fase 40), pela árvore do leitor oficial (ScriptDom, MIT) e pelo
// esquema lido do código. Cada regra olha a árvore, nunca o texto por expressão regular: comentário,
// string e colchete não enganam. Sem IA (ADR-001).
public sealed partial class TSqlQueryAnalyzer : ISqlQueryAnalyzer
{
    private const int MaxSnippet = 120;

    public SqlDialect Dialect => SqlDialect.SqlServer;

    public QueryAnalysis Analyze(string sql, DbSchema? schema)
    {
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        TSqlFragment? tree;
        IList<ParseError> errors;
        using (var reader = new StringReader(sql))
            tree = parser.Parse(reader, out errors);

        if (errors.Count > 0 || tree is not TSqlScript script)
        {
            var first = errors.FirstOrDefault();
            return new QueryAnalysis(Dialect, 0, [], [],
                new QueryParseError(first?.Message ?? "Texto vazio.", first?.Line ?? 1, first?.Column ?? 1));
        }

        var run = new Run(sql, schema);
        var statements = script.Batches.SelectMany(b => b.Statements).ToList();
        foreach (var statement in statements)
            run.Statement(statement);

        return new QueryAnalysis(Dialect, statements.Count, run.Findings(), run.Tables, null);
    }

    // Uma análise: o texto, o esquema e o que já foi achado.
    private sealed class Run(string sql, DbSchema? schema)
    {
        // Uma tabela apontada por tantas outras é a do tenant ou a do usuário, não o pai de uma lista filha.
        private const int HubMinReferences = 3;
        private readonly HashSet<string> _hubs = HubTables(schema);
        private readonly SqlDialect _dialect = schema?.Dialect ?? SqlDialect.SqlServer;
        private readonly Dictionary<string, DbTable> _tables = (schema?.Tables ?? [])
            .GroupBy(t => DbNames.Key(t.Schema, t.Name, schema!.Dialect))
            .ToDictionary(g => g.Key, g => g.First());
        private readonly HashSet<string> _views = (schema?.Views ?? [])
            .Select(v => DbNames.Key(v.Schema, v.Name, schema!.Dialect))
            .ToHashSet();
        private readonly HashSet<string> _ctes = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<QueryFinding> _findings = [];

        // Coluna sem índice fica para o fim: se a mesma coluna levou o achado de função ou de LIKE, o
        // motivo já está lá e o achado de índice sai.
        private readonly List<(string Column, QueryFinding Finding)> _indexFindings = [];
        private readonly HashSet<string> _covered = new(StringComparer.OrdinalIgnoreCase);
        private QueryExpression? _top;

        public List<QueryTableUse> Tables { get; } = [];

        public List<QueryFinding> Findings() =>
            _findings
                .Concat(_indexFindings.Where(f => !_covered.Contains(f.Column)).Select(f => f.Finding))
                .OrderByDescending(f => f.Severity)
                .ThenBy(f => f.Start)
                .ThenBy(f => f.Kind)
                .ToList();

        public void Statement(TSqlStatement statement)
        {
            switch (statement)
            {
                case SelectStatement select:
                    Ctes(select.WithCtesAndXmlNamespaces);
                    _top = select.QueryExpression;
                    Query(select.QueryExpression, null, outermost: true, inExists: false);
                    break;
                case UpdateStatement update when update.UpdateSpecification is { } spec:
                    Ctes(update.WithCtesAndXmlNamespaces);
                    Write(statement, spec.Target, spec.FromClause, spec.WhereClause, "UPDATE", "altera");
                    foreach (var set in spec.SetClauses.OfType<AssignmentSetClause>())
                        if (set.NewValue is not null) Subqueries(set.NewValue, null);
                    break;
                case DeleteStatement delete when delete.DeleteSpecification is { } spec:
                    Ctes(delete.WithCtesAndXmlNamespaces);
                    Write(statement, spec.Target, spec.FromClause, spec.WhereClause, "DELETE", "apaga");
                    break;
                case InsertStatement insert when insert.InsertSpecification?.InsertSource is SelectInsertSource source:
                    Ctes(insert.WithCtesAndXmlNamespaces);
                    Use(insert.InsertSpecification.Target);
                    Query(source.Select, null, outermost: true, inExists: false);
                    break;
                case InsertStatement insert when insert.InsertSpecification is { } spec:
                    Use(spec.Target);
                    break;
            }
        }

        private void Ctes(WithCtesAndXmlNamespaces? with)
        {
            if (with is null) return;
            foreach (var cte in with.CommonTableExpressions)
                _ctes.Add(cte.ExpressionName.Value);
            foreach (var cte in with.CommonTableExpressions)
                Query(cte.QueryExpression, null, outermost: false, inExists: false);
        }

        // UPDATE e DELETE: sem WHERE, a tabela inteira muda. Com junção no FROM, a junção limita as
        // linhas, e o achado não vale.
        private void Write(TSqlStatement statement, TableReference target, FromClause? from, WhereClause? where, string verb, string action)
        {
            var scope = new Scope(null);
            ReadEqualities(scope, from?.TableReferences ?? [], where?.SearchCondition);
            if (from is not null)
                foreach (var reference in from.TableReferences) Add(scope, reference);
            // "UPDATE p ... FROM produtos p": o alvo é o apelido de uma tabela do FROM, não outra tabela.
            var alias = target is NamedTableReference { SchemaObject.SchemaIdentifier: null } named
                && scope.Refs.Any(r => r.Alias?.Equals(named.SchemaObject.BaseIdentifier.Value, StringComparison.OrdinalIgnoreCase) == true);
            if (!alias) Add(scope, target);

            var joined = from is not null && (from.TableReferences.Count > 1 || from.TableReferences[0] is not NamedTableReference);
            if (where is null && !joined)
            {
                var table = scope.Refs.FirstOrDefault(r => r.Fragment == target) ?? scope.Refs.FirstOrDefault();
                var name = table?.Name ?? "a tabela";
                var end = from?.TableReferences.Count == 1 ? (TSqlFragment)from : target;
                Report(QueryFindingKind.WriteWithoutWhere, QuerySeverity.High, statement.StartOffset, end.StartOffset + end.FragmentLength - statement.StartOffset, statement.StartLine,
                    $"{verb} sem WHERE {action} todas as linhas de {name}. Se é isso mesmo, deixe escrito; se não, falta o filtro.", table?.Table is null ? null : table.Name);
            }
            if (where?.SearchCondition is { } condition)
            {
                CommaJoins(scope, from?.TableReferences ?? [], condition);
                Predicate(condition, scope, join: false);
            }
        }

        // Analisa uma consulta e devolve o escopo dela (as tabelas do FROM), para quem precisa resolver
        // uma coluna da lista do SELECT (o NOT IN).
        private Scope? Query(QueryExpression? query, Scope? parent, bool outermost, bool inExists)
        {
            switch (query)
            {
                case null:
                    return null;
                case QueryParenthesisExpression parenthesis:
                    var inner = Query(parenthesis.QueryExpression, parent, false, inExists);
                    OrderBy(query, outermost, top: null);
                    return inner;
                case BinaryQueryExpression binary:
                    var first = Query(binary.FirstQueryExpression, parent, false, inExists);
                    Query(binary.SecondQueryExpression, parent, false, inExists);
                    OrderBy(query, outermost, top: null);
                    return first;
                case QuerySpecification spec:
                    return Specification(spec, parent, outermost, inExists);
                default:
                    return null;
            }
        }

        private Scope Specification(QuerySpecification spec, Scope? parent, bool outermost, bool inExists)
        {
            var scope = new Scope(parent);
            var references = spec.FromClause?.TableReferences ?? [];
            ReadEqualities(scope, references, spec.WhereClause?.SearchCondition);
            foreach (var reference in references) Add(scope, reference);

            foreach (var element in spec.SelectElements)
            {
                if (element is SelectStarExpression star && !inExists) SelectStar(star, scope);
                else if (element is SelectScalarExpression scalar) Subqueries(scalar.Expression, scope, correlated: true);
            }

            if (spec.UniqueRowFilter == UniqueRowFilter.Distinct && scope.Refs.Count > 1 && Token(spec, TSqlTokenType.Distinct) is { } distinct)
                Report(QueryFindingKind.DistinctOverJoin, QuerySeverity.Low, distinct.Offset, distinct.Text.Length, distinct.Line,
                    "DISTINCT numa consulta com junção costuma esconder linhas repetidas pela junção: o banco junta tudo e depois ordena para tirar as repetidas. Confira a condição da junção ou troque a junção por EXISTS.");

            if (spec.WhereClause?.SearchCondition is { } where)
            {
                CommaJoins(scope, references, where);
                Predicate(where, scope, join: false);
            }
            else if (references.Count > 1)
                CommaJoins(scope, references, null);

            OrderBy(spec, outermost, spec.TopRowFilter, scope);
            if (outermost && ReferenceEquals(spec, _top) && spec.TopRowFilter is null && spec.OffsetClause is null)
                ReadWithoutLimit(spec, scope);
            return scope;
        }

        // ORDER BY só pesa na consulta de fora (numa subconsulta o T-SQL já exige TOP).
        // Lista filha de um pai (WHERE pai_id = @x): o filtro pela chave estrangeira já limita o que se ordena.
        private void OrderBy(QueryExpression query, bool outermost, TopRowFilter? top, Scope? scope = null)
        {
            if (!outermost || query.OrderByClause is not { } order || query.OffsetClause is not null || top is not null || FilteredByForeignKey(scope)) return;
            Report(QueryFindingKind.OrderByWithoutLimit, QuerySeverity.Low, order,
                "Ordena todas as linhas que o filtro deixou, sem TOP nem OFFSET/FETCH: o custo cresce com a tabela. Se a tela mostra uma página, limite.");
        }

        private void SelectStar(SelectStarExpression star, Scope scope)
        {
            var qualifier = star.Qualifier?.Identifiers is { Count: > 0 } ids ? ids[^1].Value : null;
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
            Report(QueryFindingKind.SelectStar, QuerySeverity.Medium, star, message, known.Count == 1 && refs.Count == 1 ? known[0].Name : null);
        }

        // Tabelas do FROM, com as junções conferidas no caminho.
        private void Add(Scope scope, TableReference reference)
        {
            switch (reference)
            {
                case NamedTableReference named:
                    scope.Refs.Add(Use(named));
                    break;
                case QueryDerivedTable derived:
                    Query(derived.QueryExpression, scope, outermost: false, inExists: false);
                    scope.Refs.Add(new TableRef(derived.Alias?.Value ?? "(subconsulta)", null, derived.Alias?.Value, derived, Known: false));
                    break;
                case QualifiedJoin join:
                    Add(scope, join.FirstTableReference);
                    var before = scope.Refs.Count;
                    Add(scope, join.SecondTableReference);
                    var second = scope.Refs.Skip(before).ToList();
                    if (join.SearchCondition is { } on)
                    {
                        if (!Links(on, scope, second))
                            Report(QueryFindingKind.JoinWithoutCondition, QuerySeverity.High, on,
                                $"O ON não compara uma coluna de {Join(second.Select(r => r.Name).ToList())} com as das outras tabelas: a junção vira o produto das tabelas, cada linha com cada linha.",
                                second.FirstOrDefault(r => r.Table is not null)?.Name);
                        Predicate(on, scope, join: true);
                    }
                    break;
                case UnqualifiedJoin cross:
                    Add(scope, cross.FirstTableReference);
                    var start = scope.Refs.Count;
                    Add(scope, cross.SecondTableReference);
                    if (cross.UnqualifiedJoinType == UnqualifiedJoinType.CrossJoin)
                    {
                        var right = scope.Refs.Skip(start).ToList();
                        var left = scope.Refs.Take(start).ToList();
                        var from = cross.FirstTableReference.LastTokenIndex + 1;
                        while (from < cross.LastTokenIndex && cross.ScriptTokenStream[from].TokenType is TSqlTokenType.WhiteSpace or TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment) from++;
                        var token = cross.ScriptTokenStream[from];
                        var end = cross.SecondTableReference.StartOffset + cross.SecondTableReference.FragmentLength;
                        Report(QueryFindingKind.JoinWithoutCondition, QuerySeverity.High, token.Offset, end - token.Offset, token.Line,
                            $"CROSS JOIN junta cada linha de {Join(left.Select(r => r.Name).ToList())} com cada linha de {Join(right.Select(r => r.Name).ToList())}: o resultado é o produto das tabelas. Se não é isso, use JOIN com ON.",
                            right.FirstOrDefault(r => r.Table is not null)?.Name);
                    }
                    break;
                case JoinParenthesisTableReference parenthesis:
                    Add(scope, parenthesis.Join);
                    break;
                default:
                    // Função de tabela, OPENJSON, variável de tabela: colunas desconhecidas.
                    scope.Opaque = true;
                    break;
            }
        }

        private TableRef Use(TableReference reference)
        {
            if (reference is not NamedTableReference named || named.SchemaObject?.BaseIdentifier?.Value is not { } baseName)
                return new TableRef("?", null, null, reference, Known: false);

            var schemaName = named.SchemaObject.SchemaIdentifier?.Value;
            var display = DbNames.Display(schemaName, baseName, _dialect);
            var alias = named.Alias?.Value;
            if (baseName.StartsWith('#') || baseName.StartsWith('@') || (schemaName is null && _ctes.Contains(baseName)))
                return new TableRef(display, null, alias, named, Known: false, BaseName: baseName);

            var key = DbNames.Key(schemaName, baseName, _dialect);
            var table = _tables.GetValueOrDefault(key);
            var inSchema = table is not null || _views.Contains(key);
            if (!Tables.Any(t => t.Name.Equals(display, StringComparison.OrdinalIgnoreCase)))
                Tables.Add(new QueryTableUse(display, alias, inSchema));
            return new TableRef(display, table, alias, named, Known: inSchema, BaseName: baseName);
        }

        // A condição do ON liga a tabela nova às outras: cita uma coluna dela e uma de outra tabela.
        // Coluna que não se resolve (tabela fora do esquema, sem prefixo) conta como ligação: na dúvida, sem achado.
        private bool Links(BooleanExpression on, Scope scope, List<TableRef> second)
        {
            var columns = Columns(on);
            if (columns.Count == 0) return false;
            var refs = new HashSet<TableRef>();
            foreach (var column in columns)
            {
                var resolved = scope.Resolve(column);
                if (resolved.Ref is null) return true;
                refs.Add(resolved.Ref);
            }
            return refs.Any(second.Contains) && refs.Any(r => !second.Contains(r));
        }

        // FROM a, b: cada tabela depois da primeira precisa de uma comparação no WHERE que a ligue a outra.
        private void CommaJoins(Scope scope, IList<TableReference> references, BooleanExpression? where)
        {
            if (references.Count < 2) return;
            var comparisons = where is null ? [] : Comparisons(where);
            for (var i = 1; i < references.Count; i++)
            {
                if (references[i] is not NamedTableReference named) continue;
                var mine = scope.Refs.FirstOrDefault(r => r.Fragment == named);
                if (mine is null) continue;
                var linked = false;
                foreach (var comparison in comparisons)
                {
                    var left = Columns(comparison.FirstExpression).Select(c => scope.Resolve(c).Ref).ToList();
                    var right = Columns(comparison.SecondExpression).Select(c => scope.Resolve(c).Ref).ToList();
                    if (left.Contains(null) || right.Contains(null)) { linked = true; break; }
                    if ((left.Contains(mine) && right.Any(r => r != mine)) || (right.Contains(mine) && left.Any(r => r != mine))) { linked = true; break; }
                }
                if (linked) continue;
                Report(QueryFindingKind.JoinWithoutCondition, QuerySeverity.High, named,
                    $"{mine.Name} entra no FROM pela vírgula, sem uma comparação no WHERE que a ligue às outras tabelas: o resultado é o produto das tabelas, cada linha com cada linha. Use JOIN com ON.",
                    mine.Table is null ? null : mine.Name);
            }
        }

        // Filtro do WHERE ou condição do ON.
        private void Predicate(BooleanExpression expression, Scope scope, bool join)
        {
            switch (expression)
            {
                case BooleanParenthesisExpression parenthesis:
                    Predicate(parenthesis.Expression, scope, join);
                    break;
                case BooleanNotExpression not:
                    Predicate(not.Expression, scope, join);
                    break;
                case BooleanBinaryExpression { BinaryExpressionType: BooleanBinaryExpressionType.Or } or:
                    var operands = new List<BooleanExpression>();
                    Flatten(or, operands);
                    OrAcross(or, operands, scope);
                    foreach (var operand in operands) Predicate(operand, scope, join);
                    break;
                case BooleanBinaryExpression and:
                    Predicate(and.FirstExpression, scope, join);
                    Predicate(and.SecondExpression, scope, join);
                    break;
                case BooleanComparisonExpression comparison:
                    Side(comparison.FirstExpression, scope, join);
                    Side(comparison.SecondExpression, scope, join);
                    break;
                case LikePredicate like:
                    if (LeadingWildcard(like.SecondExpression) is { } pattern && Plain(like.FirstExpression) is { } column)
                        LikeFinding(like, column, pattern, scope);
                    else
                        Side(like.FirstExpression, scope, join);
                    Subqueries(like.SecondExpression, scope, correlated: true);
                    break;
                case InPredicate @in:
                    Side(@in.Expression, scope, join);
                    foreach (var value in @in.Values) Subqueries(value, scope, correlated: true);
                    if (@in.Subquery is { } subquery)
                    {
                        var inner = Query(subquery.QueryExpression, scope, outermost: false, inExists: false);
                        if (@in.NotDefined) NotIn(@in, subquery, inner);
                    }
                    break;
                case BooleanTernaryExpression between:
                    Side(between.FirstExpression, scope, join);
                    Subqueries(between.SecondExpression, scope, correlated: true);
                    Subqueries(between.ThirdExpression, scope, correlated: true);
                    break;
                case BooleanIsNullExpression isNull:
                    Side(isNull.Expression, scope, join);
                    break;
                case ExistsPredicate exists:
                    // A subconsulta do EXISTS é feita para citar a de fora: o banco a junta (semi-junção).
                    Query(exists.Subquery.QueryExpression, scope, outermost: false, inExists: true);
                    break;
            }
        }

        private static void Flatten(BooleanExpression expression, List<BooleanExpression> operands)
        {
            var current = expression;
            while (current is BooleanParenthesisExpression p) current = p.Expression;
            if (current is BooleanBinaryExpression { BinaryExpressionType: BooleanBinaryExpressionType.Or } or)
            {
                Flatten(or.FirstExpression, operands);
                Flatten(or.SecondExpression, operands);
            }
            else operands.Add(expression);
        }

        private void OrAcross(BooleanBinaryExpression or, List<BooleanExpression> operands, Scope scope)
        {
            var sets = operands.Select(o => Columns(o).Select(c => ColumnKey(c, scope)).ToHashSet(StringComparer.OrdinalIgnoreCase)).ToList();
            if (sets.Any(s => s.Count == 0) || sets.All(s => s.SetEquals(sets[0]))) return;
            var names = operands.SelectMany(Columns).Select(Written).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Report(QueryFindingKind.OrAcrossColumns, QuerySeverity.Medium, or,
                $"O OR liga filtros em colunas diferentes ({Join(names)}): um índice só não atende os dois lados, e o banco costuma ler a tabela inteira. Separe em consultas ligadas por UNION ALL, ou confira o plano.");
        }

        // Um lado de comparação: coluna pura (índice), coluna embrulhada em função (achado) ou subconsulta.
        private void Side(ScalarExpression expression, Scope scope, bool join)
        {
            if (Plain(expression) is { } column)
            {
                IndexCheck(column, scope, join);
                return;
            }
            Subqueries(expression, scope, correlated: true);
            if (expression is Literal or VariableReference or GlobalVariableExpression or ScalarSubquery) return;

            var own = Columns(expression).Where(c => scope.Resolve(c).Depth == 0).ToList();
            if (own.Count == 0) return;
            var first = own[0];
            var resolved = scope.Resolve(first);
            var key = ColumnKey(first, scope);
            _covered.Add(key);
            var noIndex = resolved is { Ref.Table: { } table, Column: { } name } && table.Columns.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) && !Covered(table, name, scope, resolved.Ref)
                ? $" E {resolved.Ref.Name}.{name} nem tem índice."
                : "";
            Report(QueryFindingKind.FunctionOnFilterColumn, QuerySeverity.High, expression,
                $"{Wrapper(expression)} roda em cada linha e impede o uso de índice em {Written(first)}: compare a coluna pura (com uma faixa, no caso de data).{noIndex}",
                resolved.Ref?.Table is null ? null : resolved.Ref.Name);
        }

        private void LikeFinding(LikePredicate like, ColumnReferenceExpression column, string pattern, Scope scope)
        {
            var resolved = scope.Resolve(column);
            _covered.Add(ColumnKey(column, scope));
            var index = resolved is { Ref.Table: { } table, Column: { } name } ? IndexOn(table, name) : null;
            var tail = index is null ? "." : $": o índice {index} existe, mas não ajuda.";
            Report(QueryFindingKind.LeadingWildcardLike, QuerySeverity.Medium, like,
                $"Com o curinga no começo ('{pattern}'), o banco pode ler todos os valores de {Written(column)}{tail} Se a busca é pelo fim do texto, guarde uma coluna com o texto invertido ou use a busca de texto completo.",
                resolved.Ref?.Table is null ? null : resolved.Ref.Name);
        }

        private void NotIn(InPredicate @in, ScalarSubquery subquery, Scope? inner)
        {
            if (inner is null || Spec(subquery.QueryExpression) is not { } spec) return;
            if (spec.SelectElements.FirstOrDefault() is not SelectScalarExpression { Expression: var expression } || Plain(expression) is not { } column) return;
            var resolved = inner.Resolve(column);
            if (resolved is not { Ref.Table: { } table, Column: { } name }) return;
            if (table.Columns.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is not { Nullable: true }) return;
            Report(QueryFindingKind.NotInNullable, QuerySeverity.High, @in,
                $"{resolved.Ref.Name}.{name} aceita nulo: se a subconsulta trouxer um nulo, o NOT IN não devolve linha nenhuma. Use NOT EXISTS ou tire o nulo na subconsulta (WHERE {name} IS NOT NULL).",
                resolved.Ref.Name);
        }

        private void IndexCheck(ColumnReferenceExpression column, Scope scope, bool join)
        {
            var resolved = scope.Resolve(column);
            if (resolved is not { Depth: 0, Ref.Table: { } table, Column: { } name }) return;
            if (!table.Columns.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) || Covered(table, name, scope, resolved.Ref)) return;
            var key = ColumnKey(column, scope);
            if (_indexFindings.Any(f => f.Column.Equals(key, StringComparison.OrdinalIgnoreCase))) return;

            var indexes = IndexList(table);
            var have = indexes.Count == 0 ? "a tabela não tem índice nenhum" : $"o esquema tem {Join(indexes)}";
            var what = join ? "na junção" : "no filtro";
            var span = Span(column);
            _indexFindings.Add((key, new QueryFinding(QueryFindingKind.ColumnWithoutIndex, QuerySeverity.Medium, span.Start, span.Length, column.StartLine, Snippet(span.Start, span.Length),
                $"{resolved.Ref.Name}.{name} está {what} e não tem índice que comece por ela, nem um composto cujas colunas iniciais estejam todas no filtro com igualdade ({have}): o banco pode ler a tabela inteira para achar as linhas. Confira o plano.",
                resolved.Ref.Name)));
        }

        // Subconsulta escalar dentro de uma expressão (lista do SELECT, lado de comparação): é analisada
        // e, se cita a consulta de fora, vira achado.
        private void Subqueries(ScalarExpression expression, Scope? scope, bool correlated = false)
        {
            var finder = new SubqueryFinder();
            expression.Accept(finder);
            foreach (var subquery in finder.Found)
            {
                var inner = Query(subquery.QueryExpression, scope, outermost: false, inExists: false);
                if (!correlated || inner is null || scope is null) continue;
                var outer = Columns(subquery.QueryExpression).FirstOrDefault(c => inner.Resolve(c).Depth > 0);
                if (outer is null) continue;
                if (KeyLookup(inner) is { } keyed)
                {
                    // A ligação com a de fora é pela chave primária (ou única) da tabela de dentro: cada linha
                    // vira uma busca pontual, e o custo é baixo.
                    Report(QueryFindingKind.CorrelatedSubquery, QuerySeverity.Low, subquery,
                        $"A subconsulta busca {keyed.Ref.Name} pela chave ({keyed.Column}) usando {Written(outer)}, da consulta de fora: é uma busca pontual por linha, de custo baixo. Se a consulta devolve muitas linhas, um JOIN faz o mesmo numa passada só.",
                        keyed.Ref.Table is null ? null : keyed.Ref.Name);
                    continue;
                }
                Report(QueryFindingKind.CorrelatedSubquery, QuerySeverity.Medium, subquery,
                    $"A subconsulta cita {Written(outer)}, da consulta de fora: o banco pode rodá-la uma vez para cada linha. Troque por uma junção (JOIN com GROUP BY) ou por OUTER APPLY.");
            }
        }

        private static QuerySpecification? Spec(QueryExpression? query)
        {
            while (query is QueryParenthesisExpression p) query = p.QueryExpression;
            while (query is BinaryQueryExpression b) query = b.FirstQueryExpression;
            return query as QuerySpecification;
        }

        // A coluna tem índice que a atenda: um que comece por ela, ou um composto cujas colunas iniciais estão
        // todas no filtro com igualdade (tenant_id = @t AND loja_id = @l atende (tenant_id, loja_id)). A
        // primeira coluna depois das de igualdade também serve (faixa ou ordem).
        private static bool Covered(DbTable table, string column, Scope scope, TableRef owner)
        {
            var equal = scope.EqualityColumns(owner);
            foreach (var columns in IndexColumns(table))
            {
                var at = columns.FindIndex(c => DbNames.Unquote(c).Equals(DbNames.Unquote(column), StringComparison.OrdinalIgnoreCase));
                if (at < 0) continue;
                var run = 0;
                while (run < columns.Count && equal.Contains(DbNames.Unquote(columns[run]))) run++;
                if (at <= run) return true;
            }
            return false;
        }

        private static IEnumerable<List<string>> IndexColumns(DbTable table)
        {
            if (table.PrimaryKey is { } pk) yield return pk.Columns.ToList();
            foreach (var index in table.Indexes) yield return index.Columns.ToList();
        }

        // A comparação "coluna = valor" (Right nulo) ou "coluna = coluna"; IN com lista e IS NULL contam como
        // igualdade. Nulo: o filtro não é de igualdade.
        private static Eq? EqualityOf(BooleanExpression expression)
        {
            switch (Unparenthesize(expression))
            {
                case BooleanComparisonExpression { ComparisonType: BooleanComparisonType.Equals } eq:
                    var left = Plain(eq.FirstExpression);
                    var right = Plain(eq.SecondExpression);
                    if (left is not null) return new Eq(left, right);
                    return right is not null ? new Eq(right, null) : null;
                case InPredicate { NotDefined: false, Subquery: null } list when Plain(list.Expression) is { } column:
                    return new Eq(column, null);
                case BooleanIsNullExpression { IsNot: false } isNull when Plain(isNull.Expression) is { } nullColumn:
                    return new Eq(nullColumn, null);
                default:
                    return null;
            }
        }

        private static BooleanExpression Unparenthesize(BooleanExpression expression)
        {
            while (expression is BooleanParenthesisExpression p) expression = p.Expression;
            return expression;
        }

        private static void Conjuncts(BooleanExpression expression, List<BooleanExpression> list)
        {
            var current = Unparenthesize(expression);
            if (current is BooleanBinaryExpression { BinaryExpressionType: BooleanBinaryExpressionType.And } and)
            {
                Conjuncts(and.FirstExpression, list);
                Conjuncts(and.SecondExpression, list);
            }
            else list.Add(current);
        }

        // As igualdades do WHERE e dos ON, antes de qualquer achado: o índice composto precisa saber o filtro inteiro.
        private static void ReadEqualities(Scope scope, IEnumerable<TableReference> references, BooleanExpression? where)
        {
            if (where is not null)
            {
                var parts = new List<BooleanExpression>();
                Conjuncts(where, parts);
                scope.HasWhere = true;
                scope.OnlyEqualities = true;
                foreach (var part in parts)
                {
                    if (EqualityOf(part) is { } eq) scope.Where.Add(eq);
                    else scope.OnlyEqualities = false;
                }
            }
            foreach (var reference in references) JoinEqualities(scope, reference);
        }

        private static void JoinEqualities(Scope scope, TableReference reference)
        {
            switch (reference)
            {
                case QualifiedJoin join:
                    JoinEqualities(scope, join.FirstTableReference);
                    JoinEqualities(scope, join.SecondTableReference);
                    if (join.SearchCondition is { } on)
                    {
                        var parts = new List<BooleanExpression>();
                        Conjuncts(on, parts);
                        foreach (var part in parts)
                            if (EqualityOf(part) is { } eq) scope.Joined.Add(eq);
                    }
                    break;
                case UnqualifiedJoin cross:
                    JoinEqualities(scope, cross.FirstTableReference);
                    JoinEqualities(scope, cross.SecondTableReference);
                    break;
                case JoinParenthesisTableReference parenthesis:
                    JoinEqualities(scope, parenthesis.Join);
                    break;
            }
        }

        // Os filtros "coluna = valor" do WHERE; nulo quando algum não cai numa tabela conhecida desta consulta.
        private static List<(TableRef Ref, DbTable Table, string Column)>? ValueFilters(Scope scope)
        {
            var found = new List<(TableRef, DbTable, string)>();
            foreach (var eq in scope.Where.Where(e => e.Right is null))
            {
                if (scope.Resolve(eq.Left) is not { Depth: 0, Ref.Table: { } table, Column: { } name } resolved) return null;
                found.Add((resolved.Ref!, table, name));
            }
            return found;
        }

        // Lista filha de um pai: algum filtro de igualdade do WHERE cai numa coluna de chave estrangeira. A
        // chave para uma tabela que muitas outras apontam (o tenant, o usuário) não conta: filtrar por ela não
        // limita o resultado a um pai pequeno.
        private bool FilteredByForeignKey(Scope? scope)
        {
            if (scope is null || ValueFilters(scope) is not { } filters) return false;
            return filters.Any(f => f.Table.ForeignKeys.Any(fk => !_hubs.Contains(ShortName(fk.PrincipalTable))
                && fk.Columns.Any(c => DbNames.Unquote(c).Equals(DbNames.Unquote(f.Column), StringComparison.OrdinalIgnoreCase))));
        }

        // Tabelas apontadas por chave estrangeira de pelo menos HubMinReferences outras tabelas.
        private static HashSet<string> HubTables(DbSchema? schema) => (schema?.Tables ?? [])
            .SelectMany(t => t.ForeignKeys.Select(fk => (From: DbNames.Unquote(t.Name), To: ShortName(fk.PrincipalTable))))
            .Where(r => !r.From.Equals(r.To, StringComparison.OrdinalIgnoreCase))
            .GroupBy(r => r.To, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(r => r.From).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= HubMinReferences)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        private static string ShortName(string table) => DbNames.Unquote(table[(table.LastIndexOf('.') + 1)..]);

        // As colunas filtradas identificam uma linha: a chave primária ou uma única inteira.
        private static bool Identifies(DbTable table, IEnumerable<string> filtered)
        {
            var names = filtered.Select(DbNames.Unquote).ToHashSet(StringComparer.OrdinalIgnoreCase);
            bool All(IEnumerable<string> columns) => columns.Any() && columns.All(c => names.Contains(DbNames.Unquote(c)));
            return (table.PrimaryKey is { } pk && All(pk.Columns)) || table.Indexes.Any(i => i.Unique && All(i.Columns));
        }

        // SELECT sem TOP nem OFFSET/FETCH numa tabela filtrada só por igualdade em coluna que não identifica
        // a linha (o caso típico é só pelo tenant): o resultado cresce com a tabela.
        private void ReadWithoutLimit(QuerySpecification spec, Scope scope)
        {
            if (scope.Opaque || !scope.HasWhere || !scope.OnlyEqualities || spec.WhereClause?.SearchCondition is not { } where) return;
            if (ValueFilters(scope) is not { Count: > 0 } filters) return;
            if (spec.GroupByClause is not null || spec.HavingClause is not null || AggregateOnly(spec)) return;
            if (FilteredByForeignKey(scope)) return;
            foreach (var group in filters.GroupBy(f => f.Ref))
                if (Identifies(group.First().Table, group.Select(f => f.Column))) return;

            var written = Join(filters.Select(f => $"{f.Ref.Name}.{f.Column}").Distinct(StringComparer.OrdinalIgnoreCase).ToList());
            var tables = Join(filters.Select(f => f.Ref.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
            Report(QueryFindingKind.ReadWithoutLimit, QuerySeverity.Low, where,
                $"A consulta não tem TOP nem OFFSET/FETCH e filtra {tables} só por igualdade em {written}, que não identifica uma linha (não é chave primária nem única): o resultado cresce com a tabela. Se a tela mostra uma página, limite.",
                filters[0].Ref.Name);
        }

        // COUNT(*), SUM(x) e parentes sem GROUP BY: uma linha só.
        private static bool AggregateOnly(QuerySpecification spec)
        {
            var finder = new AggregateFinder();
            foreach (var element in spec.SelectElements) element.Accept(finder);
            return finder.Found;
        }

        // A subconsulta liga uma coluna de fora à chave primária (ou única) da tabela de dentro.
        private static (TableRef Ref, string Column)? KeyLookup(Scope inner)
        {
            foreach (var eq in inner.Where.Concat(inner.Joined).Where(e => e.Right is not null))
            {
                var left = inner.Resolve(eq.Left);
                var right = inner.Resolve(eq.Right!);
                foreach (var (mine, other) in new[] { (left, right), (right, left) })
                    if (mine is { Depth: 0, Ref.Table: { } table, Column: { } name } && other.Depth > 0 && Identifies(table, [name]))
                        return (mine.Ref!, name);
            }
            return null;
        }

        // O nome do índice que começa pela coluna (ou da chave primária), para o motivo do LIKE.
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

        private static string Wrapper(ScalarExpression expression) => expression switch
        {
            FunctionCall f => f.FunctionName.Value.ToUpperInvariant() + "()",
            CastCall => "CAST",
            TryCastCall => "TRY_CAST",
            ConvertCall => "CONVERT",
            TryConvertCall => "TRY_CONVERT",
            LeftFunctionCall => "LEFT()",
            RightFunctionCall => "RIGHT()",
            CoalesceExpression => "COALESCE()",
            NullIfExpression => "NULLIF()",
            IIfCall => "IIF()",
            BinaryExpression or UnaryExpression => "A conta",
            ParenthesisExpression p => Wrapper(p.Expression),
            _ => "A expressão"
        };

        private static ColumnReferenceExpression? Plain(ScalarExpression expression)
        {
            while (expression is ParenthesisExpression p) expression = p.Expression;
            return expression is ColumnReferenceExpression { ColumnType: ColumnType.Regular, MultiPartIdentifier.Count: > 0 } column ? column : null;
        }

        // '%@gmail.com', ou '%' + @nome: o curinga no começo do padrão.
        private static string? LeadingWildcard(ScalarExpression pattern)
        {
            var current = pattern;
            while (true)
            {
                switch (current)
                {
                    case ParenthesisExpression p:
                        current = p.Expression;
                        continue;
                    case BinaryExpression { BinaryExpressionType: BinaryExpressionType.Add } add:
                        current = add.FirstExpression;
                        continue;
                    case StringLiteral literal when literal.Value.StartsWith('%') || literal.Value.StartsWith('_'):
                        return literal.Value;
                    default:
                        return null;
                }
            }
        }

        private static string Written(ColumnReferenceExpression column) =>
            string.Join(".", column.MultiPartIdentifier.Identifiers.Select(i => i.Value));

        // A coluna como chave única: a tabela resolvida e o nome; sem resolver, como está escrita.
        private static string ColumnKey(ColumnReferenceExpression column, Scope scope)
        {
            var resolved = scope.Resolve(column);
            return resolved is { Ref: { } r, Column: { } name } ? $"{r.Name}.{name}" : Written(column);
        }

        private static List<ColumnReferenceExpression> Columns(TSqlFragment fragment)
        {
            var collector = new ColumnCollector();
            fragment.Accept(collector);
            return collector.Found;
        }

        private static List<BooleanComparisonExpression> Comparisons(BooleanExpression where)
        {
            var collector = new ComparisonCollector();
            where.Accept(collector);
            return collector.Found;
        }

        private static TSqlParserToken? Token(TSqlFragment fragment, TSqlTokenType type)
        {
            for (var i = fragment.FirstTokenIndex; i <= fragment.LastTokenIndex && i >= 0; i++)
                if (fragment.ScriptTokenStream[i].TokenType == type) return fragment.ScriptTokenStream[i];
            return null;
        }

        private static string Join(IReadOnlyList<string> items) => items.Count switch
        {
            0 => string.Empty,
            1 => items[0],
            _ => string.Join(", ", items.Take(items.Count - 1)) + " e " + items[^1]
        };

        private (int Start, int Length) Span(TSqlFragment fragment) =>
            (Math.Max(fragment.StartOffset, 0), Math.Max(Math.Min(fragment.FragmentLength, sql.Length - Math.Max(fragment.StartOffset, 0)), 0));

        private void Report(QueryFindingKind kind, QuerySeverity severity, TSqlFragment at, string message, string? table = null)
        {
            var span = Span(at);
            Report(kind, severity, span.Start, span.Length, at.StartLine, message, table);
        }

        private void Report(QueryFindingKind kind, QuerySeverity severity, int start, int length, int line, string message, string? table = null)
        {
            if (_findings.Any(f => f.Kind == kind && f.Start == start && f.Length == length)) return;
            _findings.Add(new QueryFinding(kind, severity, start, length, Math.Max(line, 1), Snippet(start, length), message, table));
        }

        private string Snippet(int start, int length)
        {
            var text = Spaces().Replace(sql.Substring(start, length), " ").Trim();
            return text.Length <= MaxSnippet ? text : text[..(MaxSnippet - 1)] + "…";
        }
    }

    // Uma tabela do FROM. Table nulo: fora do esquema (ou subconsulta, CTE, tabela temporária).
    // Known: está no esquema (tabela ou view), para a lista da tela.
    private sealed record TableRef(string Name, DbTable? Table, string? Alias, TSqlFragment Fragment, bool Known, string? BaseName = null)
    {
        public bool Matches(string qualifier) =>
            Alias is not null
                ? Alias.Equals(qualifier, StringComparison.OrdinalIgnoreCase)
                : (BaseName ?? Name).Equals(qualifier, StringComparison.OrdinalIgnoreCase) || Name.Equals(qualifier, StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct Resolution(TableRef? Ref, string? Column, int Depth);

    // Uma igualdade do filtro: coluna = valor (Right nulo) ou coluna = coluna.
    private sealed record Eq(ColumnReferenceExpression Left, ColumnReferenceExpression? Right);

    private static readonly HashSet<string> AggregateFunctions = new(StringComparer.OrdinalIgnoreCase)
        { "count", "count_big", "sum", "avg", "min", "max", "string_agg", "stdev", "var" };

    // Função de agregação na lista do SELECT, sem entrar em subconsulta.
    private sealed class AggregateFinder : TSqlFragmentVisitor
    {
        public bool Found { get; private set; }

        public override void ExplicitVisit(ScalarSubquery node) { }

        public override void Visit(FunctionCall node)
        {
            if (AggregateFunctions.Contains(node.FunctionName.Value)) Found = true;
        }
    }

    // As tabelas de uma consulta, com a de fora como pai (a subconsulta enxerga as tabelas de fora).
    private sealed class Scope(Scope? parent)
    {
        public List<TableRef> Refs { get; } = [];
        public bool Opaque { get; set; }

        // As igualdades do WHERE (Where), as dos ON (Joined) e se o WHERE tem só igualdades.
        public List<Eq> Where { get; } = [];
        public List<Eq> Joined { get; } = [];
        public bool HasWhere { get; set; }
        public bool OnlyEqualities { get; set; }

        // As colunas de uma tabela desta consulta que o filtro compara com igualdade.
        public HashSet<string> EqualityColumns(TableRef owner)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var eq in Where.Concat(Joined))
                foreach (var side in eq.Right is null ? [eq.Left] : new[] { eq.Left, eq.Right })
                    if (Resolve(side) is { Depth: 0, Column: { } name } resolved && ReferenceEquals(resolved.Ref, owner))
                        names.Add(DbNames.Unquote(name));
            return names;
        }

        // Depth 0: tabela desta consulta; 1 em diante: de uma consulta de fora. Ref nulo: não deu para
        // saber (tabela fora do esquema, coluna sem prefixo).
        public Resolution Resolve(ColumnReferenceExpression column)
        {
            var ids = column.MultiPartIdentifier?.Identifiers;
            if (ids is null || ids.Count == 0) return new(null, null, 0);
            var name = ids[^1].Value;
            var depth = 0;
            for (var scope = this; scope is not null; scope = scope.Parent, depth++)
            {
                if (ids.Count >= 2)
                {
                    var qualifier = ids[^2].Value;
                    if (scope.Refs.FirstOrDefault(r => r.Matches(qualifier)) is { } match) return new(match, name, depth);
                    continue;
                }

                var owners = scope.Refs.Where(r => r.Table?.Columns.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) == true).ToList();
                if (owners.Count == 1) return new(owners[0], name, depth);
                if (owners.Count > 1) return new(null, name, depth);
                if (scope.Refs.Count == 1 && scope.Refs[0].Table is null && !scope.Opaque) return new(scope.Refs[0], name, depth);
                // Todas as tabelas daqui são conhecidas e nenhuma tem a coluna: ela vem de fora.
                if (scope.Opaque || scope.Refs.Any(r => r.Table is null)) return new(null, name, depth);
            }
            return new(null, name, 0);
        }

        public Scope? Parent { get; } = parent;
    }

    // Colunas de uma expressão, sem entrar em subconsulta (ela tem as próprias tabelas).
    private sealed class ColumnCollector : TSqlFragmentVisitor
    {
        public List<ColumnReferenceExpression> Found { get; } = [];

        public override void ExplicitVisit(ScalarSubquery node) { }

        public override void ExplicitVisit(QueryDerivedTable node) { }

        public override void Visit(ColumnReferenceExpression node)
        {
            if (node.ColumnType == ColumnType.Regular && node.MultiPartIdentifier?.Count > 0) Found.Add(node);
        }
    }

    private sealed class ComparisonCollector : TSqlFragmentVisitor
    {
        public List<BooleanComparisonExpression> Found { get; } = [];

        public override void ExplicitVisit(ScalarSubquery node) { }

        public override void Visit(BooleanComparisonExpression node) => Found.Add(node);
    }

    // Subconsultas de primeiro nível de uma expressão (a de dentro de outra é achada quando a outra é analisada).
    private sealed class SubqueryFinder : TSqlFragmentVisitor
    {
        public List<ScalarSubquery> Found { get; } = [];

        public override void ExplicitVisit(ScalarSubquery node) => Found.Add(node);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
