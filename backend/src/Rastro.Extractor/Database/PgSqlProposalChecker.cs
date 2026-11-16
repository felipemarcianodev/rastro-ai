using System.Text;
using System.Text.RegularExpressions;
using SqlParser;
using SqlParser.Ast;
using Rastro.Core.Database;
using Rastro.Core.Localization;
using Txt = Rastro.Extractor.Resources.Database;

namespace Rastro.Extractor.Database;

// Conferência da proposta de melhora de uma consulta do PostgreSQL (Fase 42), as mesmas regras do SQL
// Server (Fase 41) pela árvore do SqlParserCS: a IA (agente ou provedor) propõe, o núcleo confere, e o
// que não passa volta recusado com o motivo. Determinística e sem IA (ADR-001). A reescrita precisa ser
// lida pelo leitor de SQL do PostgreSQL e passar pela porta do banco vivo (PgSqlPlanGuard), fazer a mesma
// operação na mesma tabela, citar só tabelas e colunas que existem (ou que a original já citava), num
// SELECT devolver as mesmas colunas com os mesmos nomes e na mesma ordem (o nome que o PostgreSQL dá:
// o da coluna, o da função ou o apelido), e não limitar as linhas (LIMIT, FETCH ou OFFSET) que a original
// não limita. O CREATE INDEX é escrito pelo Rastro, com aspas duplas e o nome em até 63 caracteres.
public sealed partial class PgSqlProposalChecker : ISqlProposalChecker
{
    private const int MaxRefusals = 6;
    private const int MaxIndexName = 63;
    private const SqlDialect Pg = SqlDialect.PostgreSql;

    // A linha que o ON CONFLICT DO UPDATE recusou: existe só durante a instrução.
    private static readonly HashSet<string> PseudoTables = new(StringComparer.OrdinalIgnoreCase) { "excluded" };

    public SqlDialect Dialect => Pg;

    public QueryProposalReview Review(string originalSql, QueryProposal proposal, DbSchema schema, UiLanguage language)
    {
        var analyzer = new PgSqlQueryAnalyzer();
        var before = analyzer.Analyze(originalSql, schema, language);
        var rewrite = Rewrite(originalSql, proposal.RewrittenSql, schema, analyzer, language);
        var changes = proposal.Changes
            .Select(c => new QueryChange(Cut(c.What), Cut(c.Why)))
            .Where(c => c.What.Length > 0)
            .Take(QueryProposalLimits.MaxChanges)
            .ToList();
        return new QueryProposalReview(proposal.Origin, before.Findings.Count, rewrite, changes, Indexes(proposal.Indexes, schema, language));
    }

    private static RewriteReview? Rewrite(string original, string? rewritten, DbSchema schema, PgSqlQueryAnalyzer analyzer, UiLanguage language)
    {
        string T(string key, params object[] args) => Txt.Get(key, language, args);
        if (string.IsNullOrWhiteSpace(rewritten) || Spaces().Replace(rewritten, " ").Trim() == Spaces().Replace(original, " ").Trim())
            return null;
        var sql = rewritten.Trim();
        if (sql.Length > QueryLimits.MaxSqlLength)
            return Refused(sql, T("Check.RewriteTooLong"));

        var rewrittenStatements = PgSqlText.Parse(sql, out var error, language);
        if (rewrittenStatements is null)
            return Refused(sql, T("Check.RewriteNotUnderstoodPg", error?.Line ?? 1, error?.Column ?? 1, error?.Message ?? string.Empty));
        var guard = PgSqlPlanGuard.Check(sql, language);
        if (!guard.Allowed)
            return Refused(sql, T("Check.FailsGuard", guard.Reason ?? string.Empty));

        var originalStatements = PgSqlText.Parse(original, out _, language) ?? [];
        if (originalStatements.Count != 1)
            return Refused(sql, originalStatements.Count == 0
                ? T("Check.OriginalNotReadPg")
                : T("Check.OriginalMultiple", originalStatements.Count));

        var source = originalStatements[0];
        var target = rewrittenStatements[0];
        var refusals = new List<string>();

        if (Kind(source) != Kind(target))
            refusals.Add(T("Check.DifferentOperation", Kind(source), Kind(target)));
        else if (WriteTarget(source) is { } sourceTable && WriteTarget(target) is { } targetTable && sourceTable.Key != targetTable.Key)
            refusals.Add(T("Check.DifferentTable", sourceTable.Name, targetTable.Name));

        var originalRefs = References.Of(source, schema, language);
        var refs = References.Of(target, schema, language);
        Tables(refs, originalRefs, refusals, language);
        Columns(target, refs, ColumnNames(source), refusals, language);
        if (source is Statement.Select originalSelect && target is Statement.Select rewrittenSelect)
        {
            SameColumns(originalSelect, originalRefs, rewrittenSelect, refs, refusals, language);
            if (!Limited(originalSelect.Query) && Limited(rewrittenSelect.Query))
                refusals.Add(T("Check.LimitsRowsPg"));
        }

        if (refusals.Count > 0)
            return new RewriteReview(sql, false, refusals.Distinct().Take(MaxRefusals).ToList(), null);
        return new RewriteReview(sql, true, [], analyzer.Analyze(sql, schema, language));
    }

    private static RewriteReview Refused(string sql, string reason) => new(sql, false, [reason], null);

    private static string Kind(Statement statement) => statement switch
    {
        Statement.Select => "SELECT",
        Statement.Update => "UPDATE",
        Statement.Delete => "DELETE",
        Statement.Insert => "INSERT",
        Statement.Merge => "MERGE",
        _ => statement.GetType().Name
    };

    private static (string Key, string Name)? WriteTarget(Statement statement)
    {
        var name = statement switch
        {
            Statement.Update { Table.Relation: TableFactor.Table t } => t.Name,
            Statement.Delete { DeleteOperation.From.From: { Count: > 0 } from } when from[0].Relation is TableFactor.Table t => t.Name,
            Statement.Insert { InsertOperation.Name: { } n } => n,
            Statement.Merge { Table: TableFactor.Table t } => t.Name,
            _ => null
        };
        if (name is null) return null;
        return (DbNames.Key(PgAst.SchemaOf(name), PgAst.Last(name), Pg), DbNames.Display(PgAst.SchemaOf(name), PgAst.Last(name), Pg));
    }

    private static void Tables(References refs, References original, List<string> refusals, UiLanguage language)
    {
        foreach (var r in refs.Named)
        {
            if (r.Cte || r.Known) continue;
            if (original.Named.Any(o => o.Key == r.Key)) continue;
            refusals.Add(Txt.Get("Check.TableNotInSchema", language, r.Name));
        }
    }

    private static List<IReadOnlyList<string>> ColumnRefs(Statement statement) =>
        PgAst.Descendants(statement, n => PgSqlQueryAnalyzer.IsVariable(n))
            .Where(n => !PgSqlQueryAnalyzer.IsVariable(n))
            .Select(PgSqlQueryAnalyzer.Parts)
            .OfType<IReadOnlyList<string>>()
            .ToList();

    private static HashSet<string> ColumnNames(Statement statement) =>
        ColumnRefs(statement).Select(c => c[^1]).Concat(TargetColumns(statement)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    // As colunas que a instrução grava: a lista do INSERT e o SET do UPDATE.
    private static List<string> TargetColumns(Statement statement) => statement switch
    {
        Statement.Insert { InsertOperation.Columns: { } columns } => columns.Select(c => c.Value).ToList(),
        Statement.Update { Assignments: { } assignments } => assignments
            .SelectMany(a => PgAst.Descendants(a).OfType<ObjectName>().Take(1))
            .Select(PgAst.Last)
            .ToList(),
        _ => []
    };

    private static void Columns(Statement statement, References refs, HashSet<string> originalColumns, List<string> refusals, UiLanguage language)
    {
        var aliases = PgAst.Descendants(statement).OfType<SelectItem.ExpressionWithAlias>()
            .Select(a => a.Alias.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var ids in ColumnRefs(statement))
        {
            var name = ids[^1];
            var cited = originalColumns.Contains(name);
            var written = string.Join(".", ids);

            if (ids.Count >= 2)
            {
                var qualifier = ids[^2];
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

            if (cited || aliases.Contains(name) || refs.All.Count == 0 || refs.All.Any(r => r.Columns is null)) continue;
            if (!refs.All.Any(r => r.Columns!.Contains(name, StringComparer.OrdinalIgnoreCase)))
                refusals.Add(Txt.Get("Check.ColumnNowhere", language, name, string.Join(", ", refs.All.Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase))));
        }

        // A coluna gravada (INSERT e SET) precisa existir na tabela que muda.
        var target = refs.Target;
        if (target?.Columns is not { } targetColumns) return;
        foreach (var column in TargetColumns(statement))
            if (!originalColumns.Contains(column) && !targetColumns.Contains(column, StringComparer.OrdinalIgnoreCase))
                refusals.Add(Txt.Get("Check.WrittenColumnMissing", language, column, target.Name));
    }

    private static void SameColumns(Statement.Select original, References originalRefs, Statement.Select rewritten, References refs, List<string> refusals, UiLanguage language)
    {
        var before = Output(original.Query, originalRefs);
        var after = Output(rewritten.Query, refs);
        if (before is null || after is null)
        {
            refusals.Add(Txt.Get("Check.StarUnknown", language));
            return;
        }
        if (before.Count != after.Count)
        {
            refusals.Add(Txt.Get("Check.ColumnCountDiffers", language, Count(before.Count, language), Names(before, language), Count(after.Count, language)));
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

    private static bool Limited(Query query)
    {
        while (true)
        {
            if (query.Limit is not null || query.Fetch is not null || query.Offset is not null) return true;
            if (query.Body is SetExpression.QueryExpression inner) { query = inner.Query; continue; }
            return false;
        }
    }

    private static string Count(int n, UiLanguage language) => n == 1 ? Txt.Get("Check.ColumnCountOne", language) : Txt.Get("Check.ColumnCountMany", language, n);

    private static string Names(List<string?> names, UiLanguage language) => string.Join(", ", names.Select(n => n ?? Txt.Get("Check.NoName", language)));

    private static List<string?>? Output(Query query, References refs)
    {
        var body = query.Body;
        while (true)
        {
            if (body is SetExpression.QueryExpression inner) { body = inner.Query.Body; continue; }
            if (body is SetExpression.SetOperation operation) { body = operation.Left; continue; }
            break;
        }
        if (body is not SetExpression.SelectExpression { Select: var select }) return null;

        var from = new List<TableFactor>();
        foreach (var table in select.From ?? []) FromOrder(table, from);
        var scope = from.Select(f => refs.All.FirstOrDefault(r => ReferenceEquals(r.Fragment, f))).OfType<TableUse>().ToList();

        var names = new List<string?>();
        foreach (var item in select.Projection ?? [])
        {
            switch (item)
            {
                case SelectItem.ExpressionWithAlias aliased:
                    names.Add(aliased.Alias.Value);
                    break;
                case SelectItem.UnnamedExpression unnamed:
                    names.Add(ColumnName(unnamed.Expression));
                    break;
                case SelectItem.Wildcard:
                    if (scope.Count == 0 || scope.Any(r => r.Columns is null)) return null;
                    foreach (var r in scope) names.AddRange(r.Columns!);
                    break;
                case SelectItem.QualifiedWildcard qualified:
                    var match = scope.Where(r => r.Matches(PgAst.Last(qualified.Name))).Take(1).ToList();
                    if (match.Count == 0 || match[0].Columns is null) return null;
                    names.AddRange(match[0].Columns!);
                    break;
                default:
                    names.Add(null);
                    break;
            }
        }
        return names;
    }

    // O nome que o PostgreSQL dá à coluna sem apelido: o da coluna, o da função, "case"; a conta não tem nome.
    private static string? ColumnName(Expression? expression) => expression switch
    {
        Expression.Identifier or Expression.CompoundIdentifier => PgSqlQueryAnalyzer.Parts(expression)![^1],
        Expression.Cast cast => ColumnName(cast.Expression),
        Expression.Nested nested => ColumnName(nested.Expression),
        Expression.Function function => PgAst.Last(function.Name),
        Expression.Case => "case",
        _ => null
    };

    private static void FromOrder(TableWithJoins table, List<TableFactor> into)
    {
        Factor(table.Relation, into);
        foreach (var join in table.Joins ?? []) Factor(join.Relation, into);
    }

    private static void Factor(TableFactor? factor, List<TableFactor> into)
    {
        if (factor is TableFactor.NestedJoin { TableWithJoins: { } nested }) FromOrder(nested, into);
        else if (factor is not null) into.Add(factor);
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
            reviews.Add(new IndexReview(display, columns, include, s.Unique, reason, true, null, CreateIndex(table, columns, include, s.Unique)));
        }
        return reviews;
    }

    private static List<string> Clean(IReadOnlyList<string>? names) =>
        (names ?? []).Select(DbNames.Unquote).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

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

    private static string CreateIndex(DbTable table, List<string> columns, List<string> include, bool unique)
    {
        var name = IndexNameChars().Replace($"ix_{table.Name}_{string.Join("_", columns)}", "_").ToLowerInvariant();
        if (name.Length > MaxIndexName) name = name[..MaxIndexName];
        var target = DbNames.NormalizeSchema(table.Schema, Pg) is { } s ? $"{Quote(s)}.{Quote(table.Name)}" : Quote(table.Name);
        var text = new StringBuilder($"CREATE {(unique ? "UNIQUE " : "")}INDEX {Quote(name)} ON {target} ({string.Join(", ", columns.Select(Quote))})");
        if (include.Count > 0) text.Append($" INCLUDE ({string.Join(", ", include.Select(Quote))})");
        return text.Append(';').ToString();
    }

    private static string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";

    private static string Cut(string? text)
    {
        var clean = Spaces().Replace(text ?? string.Empty, " ").Trim();
        return clean.Length <= QueryProposalLimits.MaxText ? clean : clean[..QueryProposalLimits.MaxText] + "…";
    }

    // Uma tabela citada pela instrução. Columns nulo: colunas desconhecidas (subconsulta, CTE, função de
    // tabela, fora do esquema, view sem as colunas lidas). Known: tabela ou view do esquema.
    private sealed record TableUse(string Name, string Key, string? Alias, string? BaseName, IReadOnlyList<string>? Columns, bool Known, bool Cte, object Fragment)
    {
        public bool Matches(string qualifier) =>
            Alias is not null
                ? Alias.Equals(qualifier, StringComparison.OrdinalIgnoreCase)
                : (BaseName ?? Name).Equals(qualifier, StringComparison.OrdinalIgnoreCase) || Name.Equals(qualifier, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class References
    {
        public List<TableUse> All { get; } = [];
        public TableUse? Target { get; private set; }
        public IEnumerable<TableUse> Named => All.Where(r => r.Fragment is TableFactor.Table or ObjectName);

        public static References Of(Statement statement, DbSchema schema, UiLanguage language)
        {
            var ctes = PgAst.Descendants(statement).OfType<CommonTableExpression>().Select(c => c.Alias.Name.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var result = new References();

            if (statement is Statement.Insert { InsertOperation.Name: { } insertName })
                result.All.Add(Use(insertName, null, insertName, ctes, schema));

            foreach (var factor in PgAst.Descendants(statement).OfType<TableFactor>())
            {
                if (factor is TableFactor.NestedJoin) continue;
                var alias = factor.Alias?.Name.Value;
                if (factor is TableFactor.Table { Args: null, Name: { } name })
                    result.All.Add(Use(name, alias, factor, ctes, schema));
                else
                    result.All.Add(new TableUse(alias ?? Txt.Get("Query.SubqueryName", language), string.Empty, alias, null, null, false, false, factor));
            }

            var targetName = statement switch
            {
                Statement.Insert { InsertOperation.Name: { } n } => n,
                Statement.Update { Table.Relation: TableFactor.Table t } => t.Name,
                Statement.Delete { DeleteOperation.From.From: { Count: > 0 } from } when from[0].Relation is TableFactor.Table t => t.Name,
                _ => null
            };
            if (targetName is not null)
            {
                var key = DbNames.Key(PgAst.SchemaOf(targetName), PgAst.Last(targetName), Pg);
                result.Target = result.All.FirstOrDefault(r => r.Key == key);
            }
            return result;
        }

        private static TableUse Use(ObjectName name, string? alias, object fragment, HashSet<string> ctes, DbSchema schema)
        {
            var baseName = PgAst.Last(name);
            var schemaName = PgAst.SchemaOf(name);
            var display = DbNames.Display(schemaName, baseName, schema.Dialect);
            var key = DbNames.Key(schemaName, baseName, schema.Dialect);
            var cte = schemaName is null && ctes.Contains(baseName);
            var table = schema.Tables.FirstOrDefault(t => DbNames.Key(t.Schema, t.Name, schema.Dialect) == key);
            var view = table is null ? schema.Views.FirstOrDefault(v => DbNames.Key(v.Schema, v.Name, schema.Dialect) == key) : null;
            IReadOnlyList<string>? columns = cte ? null
                : table is not null ? table.Columns.Select(c => c.Name).ToList()
                : view is { Columns.Count: > 0 } && !view.Columns.Contains("*") ? view.Columns
                : null;
            return new TableUse(display, key, alias, baseName, columns, !cte && (table is not null || view is not null), cte, fragment);
        }
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"[^A-Za-z0-9_]")]
    private static partial Regex IndexNameChars();
}
