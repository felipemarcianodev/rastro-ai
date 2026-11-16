using System.Text;
using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Rastro.Core.Database;
using Rastro.Core.Localization;
using Txt = Rastro.Extractor.Resources.Database;

namespace Rastro.Extractor.Database;

// Uma conferência de proposta por dialeto (Fase 41, ADR-006), como o leitor de esquema e o analisador:
// o SQL Server pelo ScriptDom e o PostgreSQL por mais uma (Fase 42). Só lê o texto, nunca executa nada.
public interface ISqlProposalChecker
{
    SqlDialect Dialect { get; }

    // language: o idioma das recusas e dos achados, que vão para a tela (ADR-009).
    QueryProposalReview Review(string originalSql, QueryProposal proposal, DbSchema schema, UiLanguage language);
}

public static class SqlProposalCheckers
{
    private static readonly ISqlProposalChecker[] All = [new TSqlProposalChecker(), new PgSqlProposalChecker()];

    public static ISqlProposalChecker? For(SqlDialect dialect) => All.FirstOrDefault(c => c.Dialect == dialect);
}

// Conferência da proposta de melhora de uma consulta T-SQL (Fase 41), no papel do validador de prova da
// aresta (Fase 4): a IA (agente ou provedor) propõe, o núcleo confere pela árvore do leitor oficial e
// pelo esquema, e o que não passa volta recusado com o motivo. Determinística e sem IA (ADR-001).
// A consulta reescrita precisa:
// - ser lida pelo leitor de T-SQL e passar pela porta do banco vivo (uma instrução, sem SET, EXEC nem DDL);
// - fazer a mesma operação na mesma tabela (SELECT continua SELECT; UPDATE, DELETE, INSERT e MERGE no mesmo alvo);
// - citar só tabelas e colunas que existem no esquema (ou que a original já citava);
// - num SELECT, devolver as mesmas colunas, com os mesmos nomes e na mesma ordem, sem limitar as linhas
//   (TOP ou OFFSET/FETCH) que a original não limita.
// O índice sugerido só usa colunas que existem e não repete um índice (nem a chave primária) que já
// começa pelas mesmas colunas. O comando CREATE INDEX é escrito pelo Rastro, não pela IA.
public sealed partial class TSqlProposalChecker : ISqlProposalChecker
{
    private const int MaxRefusals = 6;
    private const int MaxIndexName = 128;

    // Funções de data cujo primeiro argumento é a parte da data (year, day): o leitor o lê como coluna.
    private static readonly HashSet<string> DatePartFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "DATEADD", "DATEDIFF", "DATEDIFF_BIG", "DATEPART", "DATENAME", "DATETRUNC", "DATE_BUCKET"
    };

    // Tabelas do OUTPUT e dos gatilhos: existem só durante a instrução.
    private static readonly HashSet<string> PseudoTables = new(StringComparer.OrdinalIgnoreCase) { "inserted", "deleted" };

    public SqlDialect Dialect => SqlDialect.SqlServer;

    public QueryProposalReview Review(string originalSql, QueryProposal proposal, DbSchema schema, UiLanguage language)
    {
        var analyzer = new TSqlQueryAnalyzer();
        var before = analyzer.Analyze(originalSql, schema, language);
        var rewrite = Rewrite(originalSql, proposal.RewrittenSql, schema, analyzer, language);
        var changes = proposal.Changes
            .Select(c => new QueryChange(Cut(c.What), Cut(c.Why)))
            .Where(c => c.What.Length > 0)
            .Take(QueryProposalLimits.MaxChanges)
            .ToList();
        return new QueryProposalReview(proposal.Origin, before.Findings.Count, rewrite, changes, Indexes(proposal.Indexes, schema, language));
    }

    private static RewriteReview? Rewrite(string original, string? rewritten, DbSchema schema, TSqlQueryAnalyzer analyzer, UiLanguage language)
    {
        string T(string key, params object[] args) => Txt.Get(key, language, args);
        if (string.IsNullOrWhiteSpace(rewritten) || Spaces().Replace(rewritten, " ").Trim() == Spaces().Replace(original, " ").Trim())
            return null;
        var sql = rewritten.Trim();
        if (sql.Length > QueryLimits.MaxSqlLength)
            return Refused(sql, T("Check.RewriteTooLong"));

        var (rewrittenTree, error) = Parse(sql, language);
        if (rewrittenTree is null)
            return Refused(sql, T("Check.RewriteNotUnderstoodTsql", error!));
        var guard = TSqlPlanGuard.Check(sql, language);
        if (!guard.Allowed)
            return Refused(sql, T("Check.FailsGuard", guard.Reason ?? string.Empty));

        var (originalTree, _) = Parse(original, language);
        var originalStatements = originalTree?.Batches.SelectMany(b => b.Statements).ToList() ?? [];
        if (originalStatements.Count != 1)
            return Refused(sql, originalStatements.Count == 0
                ? T("Check.OriginalNotReadTsql")
                : T("Check.OriginalMultiple", originalStatements.Count));

        var source = originalStatements[0];
        var target = rewrittenTree.Batches.Single().Statements.Single();
        var refusals = new List<string>();

        if (Kind(source) != Kind(target))
            refusals.Add(T("Check.DifferentOperation", Kind(source), Kind(target)));
        else if (WriteTarget(source, schema.Dialect) is { } sourceTable && WriteTarget(target, schema.Dialect) is { } targetTable && sourceTable.Key != targetTable.Key)
            refusals.Add(T("Check.DifferentTable", sourceTable.Name, targetTable.Name));

        var originalRefs = References.Of(source, schema, language);
        var refs = References.Of(target, schema, language);
        Tables(refs, originalRefs, refusals, language);
        Columns(target, refs, ColumnNames(source), refusals, language);
        if (source is SelectStatement originalSelect && target is SelectStatement rewrittenSelect)
        {
            SameColumns(originalSelect, originalRefs, rewrittenSelect, refs, refusals, language);
            // Limitar as linhas muda o resultado: é decisão de quem usa a consulta, não da reescrita.
            if (!Limited(originalSelect) && Limited(rewrittenSelect))
                refusals.Add(T("Check.LimitsRowsTsql"));
        }

        if (refusals.Count > 0)
            return new RewriteReview(sql, false, refusals.Distinct().Take(MaxRefusals).ToList(), null);
        return new RewriteReview(sql, true, [], analyzer.Analyze(sql, schema, language));
    }

    private static RewriteReview Refused(string sql, string reason) => new(sql, false, [reason], null);

    private static (TSqlScript? Script, string? Error) Parse(string sql, UiLanguage language)
    {
        var tree = TSqlParsing.Parse(sql, out var errors);
        if (errors.Count == 0 && tree is TSqlScript script) return (script, null);
        var first = errors.FirstOrDefault();
        return (null, first is null ? Txt.Get("Guard.EmptyText", language) : Txt.Get("Check.ErrorAt", language, first.Line, first.Column, SqlMessageTranslator.Translate(first.Message, language)));
    }

    private static string Kind(TSqlStatement statement) => statement switch
    {
        SelectStatement => "SELECT",
        UpdateStatement => "UPDATE",
        DeleteStatement => "DELETE",
        InsertStatement => "INSERT",
        MergeStatement => "MERGE",
        _ => statement.GetType().Name
    };

    // A tabela que a instrução muda. "UPDATE p ... FROM pedidos p": o alvo é o apelido de uma tabela do FROM.
    private static (string Key, string Name)? WriteTarget(TSqlStatement statement, SqlDialect dialect)
    {
        (TableReference? target, FromClause? from) = statement switch
        {
            UpdateStatement { UpdateSpecification: { } u } => (u.Target, u.FromClause),
            DeleteStatement { DeleteSpecification: { } d } => (d.Target, d.FromClause),
            InsertStatement { InsertSpecification: { } i } => (i.Target, (FromClause?)null),
            MergeStatement { MergeSpecification: { } m } => (m.Target, (FromClause?)null),
            _ => ((TableReference?)null, (FromClause?)null)
        };
        if (target is not NamedTableReference { SchemaObject.BaseIdentifier.Value: { } baseName } named) return null;
        var schemaName = named.SchemaObject.SchemaIdentifier?.Value;
        if (schemaName is null && from is not null)
        {
            var finder = new NamedFinder();
            from.Accept(finder);
            if (finder.Found.FirstOrDefault(n => n.Alias?.Value.Equals(baseName, StringComparison.OrdinalIgnoreCase) == true) is { SchemaObject.BaseIdentifier.Value: { } aliased } real)
                (schemaName, baseName) = (real.SchemaObject.SchemaIdentifier?.Value, aliased);
        }
        return (DbNames.Key(schemaName, baseName, dialect), DbNames.Display(schemaName, baseName, dialect));
    }

    private static void Tables(References refs, References original, List<string> refusals, UiLanguage language)
    {
        foreach (var r in refs.Named)
        {
            if (r.Cte || r.Known) continue;
            if (original.Named.Any(o => o.Key == r.Key)) continue;
            refusals.Add(r.Temporary
                ? Txt.Get("Check.TemporaryTable", language, r.Name)
                : Txt.Get("Check.TableNotInSchema", language, r.Name));
        }
    }

    private static HashSet<string> ColumnNames(TSqlStatement statement)
    {
        var collector = new ColumnFinder();
        statement.Accept(collector);
        return collector.Found.Select(c => c.MultiPartIdentifier.Identifiers[^1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    // Toda coluna citada precisa existir numa tabela da consulta. Coluna que a original já citava passa (o
    // esquema lido do código pode estar atrás do banco); tabela de colunas desconhecidas não recusa nada.
    private static void Columns(TSqlStatement statement, References refs, HashSet<string> originalColumns, List<string> refusals, UiLanguage language)
    {
        var dateParts = new DatePartFinder();
        statement.Accept(dateParts);
        var columns = new ColumnFinder();
        statement.Accept(columns);
        var aliases = new AliasFinder();
        statement.Accept(aliases);

        foreach (var column in columns.Found)
        {
            if (dateParts.Found.Contains(column)) continue;
            var ids = column.MultiPartIdentifier.Identifiers;
            var name = ids[^1].Value;
            var cited = originalColumns.Contains(name);
            var written = string.Join(".", ids.Select(i => i.Value));

            if (ids.Count >= 2)
            {
                var qualifier = ids[^2].Value;
                var match = refs.All.FirstOrDefault(r => r.Matches(qualifier));
                if (match is null)
                {
                    if (!PseudoTables.Contains(qualifier))
                        refusals.Add(Txt.Get("Check.QualifierUnknown", language, written, qualifier));
                    continue;
                }
                if (!cited && match.Columns is { } known && !known.Contains(name, StringComparer.OrdinalIgnoreCase))
                    refusals.Add(Txt.Get("Check.ColumnNotInTable", language, written, name, match.Name));
                continue;
            }

            if (cited || aliases.Found.Contains(name) || refs.All.Count == 0 || refs.All.Any(r => r.Columns is null)) continue;
            if (!refs.All.Any(r => r.Columns!.Contains(name, StringComparer.OrdinalIgnoreCase)))
                refusals.Add(Txt.Get("Check.ColumnNowhere", language, name, string.Join(", ", refs.All.Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase))));
        }
    }

    private static void SameColumns(SelectStatement original, References originalRefs, SelectStatement rewritten, References refs, List<string> refusals, UiLanguage language)
    {
        var before = Output(original, originalRefs);
        var after = Output(rewritten, refs);
        if (before is null || after is null)
        {
            refusals.Add(Txt.Get("Check.StarUnknown", language));
            return;
        }
        if (before.Count != after.Count)
        {
            refusals.Add(Txt.Get("Check.ColumnCountDiffers", language, ColumnCount(before.Count, language), Names(before, language), ColumnCount(after.Count, language)));
            return;
        }
        for (var i = 0; i < before.Count; i++)
        {
            var same = before[i] is null ? after[i] is null : after[i]?.Equals(before[i], StringComparison.OrdinalIgnoreCase) == true;
            if (same) continue;
            refusals.Add(Txt.Get("Check.ColumnChanged", language, i + 1, before[i] ?? Txt.Get("Check.UnnamedExpression", language), after[i] ?? Txt.Get("Check.UnnamedExpression", language)));
            return;
        }
    }

    private static bool Limited(SelectStatement select)
    {
        var query = select.QueryExpression;
        if (query?.OffsetClause is not null) return true;
        while (query is QueryParenthesisExpression p)
        {
            query = p.QueryExpression;
            if (query?.OffsetClause is not null) return true;
        }
        return query is QuerySpecification { TopRowFilter: not null };
    }

    private static string ColumnCount(int n, UiLanguage language) => n == 1 ? Txt.Get("Check.ColumnCountOne", language) : Txt.Get("Check.ColumnCountMany", language, n);

    private static string Names(List<string?> names, UiLanguage language) => string.Join(", ", names.Select(n => n ?? Txt.Get("Check.NoName", language)));

    // As colunas que a consulta de fora devolve; nulo quando um * cita tabela de colunas desconhecidas.
    private static List<string?>? Output(SelectStatement select, References refs)
    {
        var query = select.QueryExpression;
        while (query is QueryParenthesisExpression p) query = p.QueryExpression;
        while (query is BinaryQueryExpression b) query = b.FirstQueryExpression;
        while (query is QueryParenthesisExpression p2) query = p2.QueryExpression;
        if (query is not QuerySpecification spec) return null;

        var from = new List<TableReferenceWithAlias>();
        foreach (var reference in spec.FromClause?.TableReferences ?? []) FromOrder(reference, from);
        var scope = from.Select(f => refs.All.FirstOrDefault(r => ReferenceEquals(r.Fragment, f))).OfType<TableUse>().ToList();

        var names = new List<string?>();
        foreach (var element in spec.SelectElements)
        {
            switch (element)
            {
                case SelectScalarExpression scalar:
                    names.Add(scalar.ColumnName?.Value is { } alias
                        ? DbNames.Unquote(alias)
                        : scalar.Expression is ColumnReferenceExpression { MultiPartIdentifier.Count: > 0 } c ? c.MultiPartIdentifier.Identifiers[^1].Value : null);
                    break;
                case SelectStarExpression star:
                    var qualifier = star.Qualifier?.Identifiers is { Count: > 0 } ids ? ids[^1].Value : null;
                    var expanded = qualifier is null ? scope : scope.Where(r => r.Matches(qualifier)).Take(1).ToList();
                    if (expanded.Count == 0 || expanded.Any(r => r.Columns is null)) return null;
                    foreach (var r in expanded) names.AddRange(r.Columns!);
                    break;
                default:
                    names.Add(null);
                    break;
            }
        }
        return names;
    }

    private static void FromOrder(TableReference reference, List<TableReferenceWithAlias> into)
    {
        switch (reference)
        {
            case JoinTableReference join:
                FromOrder(join.FirstTableReference, into);
                FromOrder(join.SecondTableReference, into);
                break;
            case JoinParenthesisTableReference parenthesis:
                FromOrder(parenthesis.Join, into);
                break;
            case TableReferenceWithAlias aliased:
                into.Add(aliased);
                break;
        }
    }

    private static List<IndexReview> Indexes(IReadOnlyList<IndexSuggestion> suggestions, DbSchema schema, UiLanguage language)
    {
        var reviews = new List<IndexReview>();
        foreach (var s in suggestions.Take(QueryProposalLimits.MaxIndexes))
        {
            var columns = Clean(s.Columns);
            var include = Clean(s.Include).Where(c => !columns.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
            var reason = Cut(s.Reason);
            var tableName = DbNames.Unquote(s.Table);
            IndexReview Refuse(string why) => new(tableName, columns, include, s.Unique, reason, false, why, null);

            var key = DbNames.KeyOf(tableName, schema.Dialect);
            var table = schema.Tables.FirstOrDefault(t => DbNames.Key(t.Schema, t.Name, schema.Dialect) == key);
            if (table is null)
            {
                reviews.Add(Refuse(schema.Views.Any(v => DbNames.Key(v.Schema, v.Name, schema.Dialect) == key)
                    ? Txt.Get("Check.IndexOnView", language, tableName)
                    : Txt.Get("Check.IndexTableMissing", language, tableName)));
                continue;
            }
            var display = DbNames.Display(table.Schema, table.Name, schema.Dialect);
            if (columns.Count == 0)
            {
                reviews.Add(Refuse(Txt.Get("Check.IndexNoColumn", language)));
                continue;
            }
            var missing = columns.Concat(include).FirstOrDefault(c => !table.Columns.Any(t => t.Name.Equals(c, StringComparison.OrdinalIgnoreCase)));
            if (missing is not null)
            {
                reviews.Add(Refuse(Txt.Get("Check.IndexColumnMissing", language, missing, display)));
                continue;
            }
            // Os nomes como o esquema escreve.
            columns = columns.Select(c => table.Columns.First(t => t.Name.Equals(c, StringComparison.OrdinalIgnoreCase)).Name).ToList();
            include = include.Select(c => table.Columns.First(t => t.Name.Equals(c, StringComparison.OrdinalIgnoreCase)).Name).ToList();

            if (Existing(table, columns, language) is { } existing)
            {
                reviews.Add(Refuse(Txt.Get("Check.IndexExists", language, existing)));
                continue;
            }
            if (reviews.Any(r => r.Accepted && r.Table.Equals(display, StringComparison.OrdinalIgnoreCase) && r.Columns.SequenceEqual(columns, StringComparer.OrdinalIgnoreCase)))
            {
                reviews.Add(Refuse(Txt.Get("Check.IndexRepeated", language)));
                continue;
            }
            reviews.Add(new IndexReview(display, columns, include, s.Unique, reason, true, null, CreateIndex(table, columns, include, s.Unique, schema.Dialect)));
        }
        return reviews;
    }

    private static List<string> Clean(IReadOnlyList<string>? names) =>
        (names ?? []).Select(DbNames.Unquote).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    // A chave primária ou o índice cujas colunas começam pelas colunas sugeridas, na mesma ordem.
    private static string? Existing(DbTable table, List<string> columns, UiLanguage language)
    {
        static bool Starts(IReadOnlyList<string> have, List<string> want) =>
            have.Count >= want.Count && want.Select((c, i) => have[i].Equals(c, StringComparison.OrdinalIgnoreCase)).All(x => x);

        if (table.PrimaryKey is { } pk && Starts(pk.Columns, columns))
            return pk.Name is null
                ? Txt.Get("Check.ExistingPrimaryKey", language, string.Join(", ", pk.Columns))
                : Txt.Get("Check.ExistingPrimaryKeyNamed", language, pk.Name, string.Join(", ", pk.Columns));
        return table.Indexes.FirstOrDefault(i => Starts(i.Columns, columns)) is { } index
            ? Txt.Get("Check.ExistingIndex", language, index.Name ?? Txt.Get("Check.Unnamed", language), string.Join(", ", index.Columns))
            : null;
    }

    private static string CreateIndex(DbTable table, List<string> columns, List<string> include, bool unique, SqlDialect dialect)
    {
        var name = IndexNameChars().Replace($"IX_{table.Name}_{string.Join("_", columns)}", "_");
        if (name.Length > MaxIndexName) name = name[..MaxIndexName];
        var target = DbNames.NormalizeSchema(table.Schema, dialect) is { } s ? $"{Quote(s)}.{Quote(table.Name)}" : Quote(table.Name);
        var text = new StringBuilder($"CREATE {(unique ? "UNIQUE " : "")}INDEX {Quote(name)} ON {target} ({string.Join(", ", columns.Select(Quote))})");
        if (include.Count > 0) text.Append($" INCLUDE ({string.Join(", ", include.Select(Quote))})");
        return text.Append(';').ToString();
    }

    private static string Quote(string name) => "[" + name.Replace("]", "]]") + "]";

    private static string Cut(string? text)
    {
        var clean = Spaces().Replace(text ?? string.Empty, " ").Trim();
        return clean.Length <= QueryProposalLimits.MaxText ? clean : clean[..QueryProposalLimits.MaxText] + "…";
    }

    // Uma tabela citada pela instrução. Columns nulo: colunas desconhecidas (subconsulta, CTE, temporária,
    // fora do esquema, view sem as colunas lidas). Known: tabela ou view do esquema.
    private sealed record TableUse(string Name, string Key, string? Alias, string? BaseName, IReadOnlyList<string>? Columns, bool Known, bool Cte, bool Temporary, TSqlFragment Fragment)
    {
        public bool Matches(string qualifier) =>
            Alias is not null
                ? Alias.Equals(qualifier, StringComparison.OrdinalIgnoreCase)
                : (BaseName ?? Name).Equals(qualifier, StringComparison.OrdinalIgnoreCase) || Name.Equals(qualifier, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class References
    {
        public List<TableUse> All { get; } = [];
        public IEnumerable<TableUse> Named => All.Where(r => r.Fragment is NamedTableReference);

        public static References Of(TSqlStatement statement, DbSchema schema, UiLanguage language)
        {
            var ctes = new CteFinder();
            statement.Accept(ctes);
            var finder = new AliasedFinder();
            statement.Accept(finder);

            var result = new References();
            foreach (var reference in finder.Found)
            {
                var alias = reference.Alias?.Value;
                if (reference is NamedTableReference { SchemaObject.BaseIdentifier.Value: { } baseName } named)
                {
                    var schemaName = named.SchemaObject.SchemaIdentifier?.Value;
                    var display = DbNames.Display(schemaName, baseName, schema.Dialect);
                    var key = DbNames.Key(schemaName, baseName, schema.Dialect);
                    var temporary = baseName.StartsWith('#') || baseName.StartsWith('@');
                    var cte = schemaName is null && ctes.Found.Contains(baseName);
                    var table = schema.Tables.FirstOrDefault(t => DbNames.Key(t.Schema, t.Name, schema.Dialect) == key);
                    var view = table is null ? schema.Views.FirstOrDefault(v => DbNames.Key(v.Schema, v.Name, schema.Dialect) == key) : null;
                    IReadOnlyList<string>? columns = cte || temporary ? null
                        : table is not null ? table.Columns.Select(c => c.Name).ToList()
                        : view is { Columns.Count: > 0 } ? view.Columns
                        : null;
                    result.All.Add(new TableUse(display, key, alias, baseName, columns, !cte && !temporary && (table is not null || view is not null), cte, temporary, reference));
                }
                else
                    result.All.Add(new TableUse(alias ?? Txt.Get("Query.SubqueryName", language), string.Empty, alias, null, null, false, false, false, reference));
            }
            // "UPDATE p ... FROM pedidos p": o alvo p é o apelido de outra tabela da instrução, não uma tabela.
            result.All.RemoveAll(r => r.Fragment is NamedTableReference { SchemaObject.SchemaIdentifier: null } && r.Alias is null
                && result.All.Any(o => !ReferenceEquals(o, r) && o.Alias?.Equals(r.BaseName, StringComparison.OrdinalIgnoreCase) == true));
            return result;
        }
    }

    private sealed class AliasedFinder : TSqlFragmentVisitor
    {
        public List<TableReferenceWithAlias> Found { get; } = [];

        public override void Visit(TableReferenceWithAlias node) => Found.Add(node);
    }

    private sealed class NamedFinder : TSqlFragmentVisitor
    {
        public List<NamedTableReference> Found { get; } = [];

        public override void Visit(NamedTableReference node) => Found.Add(node);
    }

    private sealed class CteFinder : TSqlFragmentVisitor
    {
        public HashSet<string> Found { get; } = new(StringComparer.OrdinalIgnoreCase);

        public override void Visit(CommonTableExpression node) => Found.Add(node.ExpressionName.Value);
    }

    private sealed class ColumnFinder : TSqlFragmentVisitor
    {
        public List<ColumnReferenceExpression> Found { get; } = [];

        public override void Visit(ColumnReferenceExpression node)
        {
            if (node.ColumnType == ColumnType.Regular && node.MultiPartIdentifier?.Count > 0) Found.Add(node);
        }
    }

    // Apelidos da lista do SELECT: o ORDER BY pode citá-los sem tabela.
    private sealed class AliasFinder : TSqlFragmentVisitor
    {
        public HashSet<string> Found { get; } = new(StringComparer.OrdinalIgnoreCase);

        public override void Visit(SelectScalarExpression node)
        {
            if (node.ColumnName?.Value is { } name) Found.Add(DbNames.Unquote(name));
        }
    }

    private sealed class DatePartFinder : TSqlFragmentVisitor
    {
        public HashSet<TSqlFragment> Found { get; } = new(ReferenceEqualityComparer.Instance);

        public override void Visit(FunctionCall node)
        {
            if (DatePartFunctions.Contains(node.FunctionName.Value) && node.Parameters.Count > 0 && node.Parameters[0] is ColumnReferenceExpression part)
                Found.Add(part);
        }
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"[^A-Za-z0-9_]")]
    private static partial Regex IndexNameChars();
}
