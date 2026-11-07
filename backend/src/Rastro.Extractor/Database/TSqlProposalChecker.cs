using System.Text;
using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Rastro.Core.Database;

namespace Rastro.Extractor.Database;

// Uma conferência de proposta por dialeto (Fase 41, ADR-006), como o leitor de esquema e o analisador:
// o SQL Server pelo ScriptDom e o PostgreSQL por mais uma (Fase 42). Só lê o texto, nunca executa nada.
public interface ISqlProposalChecker
{
    SqlDialect Dialect { get; }

    QueryProposalReview Review(string originalSql, QueryProposal proposal, DbSchema schema);
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

    public QueryProposalReview Review(string originalSql, QueryProposal proposal, DbSchema schema)
    {
        var analyzer = new TSqlQueryAnalyzer();
        var before = analyzer.Analyze(originalSql, schema);
        var rewrite = Rewrite(originalSql, proposal.RewrittenSql, schema, analyzer);
        var changes = proposal.Changes
            .Select(c => new QueryChange(Cut(c.What), Cut(c.Why)))
            .Where(c => c.What.Length > 0)
            .Take(QueryProposalLimits.MaxChanges)
            .ToList();
        return new QueryProposalReview(proposal.Origin, before.Findings.Count, rewrite, changes, Indexes(proposal.Indexes, schema));
    }

    private static RewriteReview? Rewrite(string original, string? rewritten, DbSchema schema, TSqlQueryAnalyzer analyzer)
    {
        if (string.IsNullOrWhiteSpace(rewritten) || Spaces().Replace(rewritten, " ").Trim() == Spaces().Replace(original, " ").Trim())
            return null;
        var sql = rewritten.Trim();
        if (sql.Length > QueryLimits.MaxSqlLength)
            return Refused(sql, "A consulta reescrita passa de 100 mil caracteres.");

        var (rewrittenTree, error) = Parse(sql);
        if (rewrittenTree is null)
            return Refused(sql, $"O leitor de T-SQL não entendeu a consulta reescrita ({error}).");
        var guard = TSqlPlanGuard.Check(sql);
        if (!guard.Allowed)
            return Refused(sql, $"A consulta reescrita não passa pela conferência do Rastro: {guard.Reason}");

        var (originalTree, _) = Parse(original);
        var originalStatements = originalTree?.Batches.SelectMany(b => b.Statements).ToList() ?? [];
        if (originalStatements.Count != 1)
            return Refused(sql, originalStatements.Count == 0
                ? "A consulta original não foi lida pelo leitor de T-SQL: não há com o que comparar."
                : $"A consulta original tem {originalStatements.Count} instruções: a conferência compara uma instrução só.");

        var source = originalStatements[0];
        var target = rewrittenTree.Batches.Single().Statements.Single();
        var refusals = new List<string>();

        if (Kind(source) != Kind(target))
            refusals.Add($"A consulta original é um {Kind(source)} e a reescrita é um {Kind(target)}: a reescrita precisa fazer a mesma operação.");
        else if (WriteTarget(source, schema.Dialect) is { } sourceTable && WriteTarget(target, schema.Dialect) is { } targetTable && sourceTable.Key != targetTable.Key)
            refusals.Add($"A consulta original muda {sourceTable.Name} e a reescrita muda {targetTable.Name}: a reescrita precisa mudar a mesma tabela.");

        var originalRefs = References.Of(source, schema);
        var refs = References.Of(target, schema);
        Tables(refs, originalRefs, refusals);
        Columns(target, refs, ColumnNames(source), refusals);
        if (source is SelectStatement originalSelect && target is SelectStatement rewrittenSelect)
        {
            SameColumns(originalSelect, originalRefs, rewrittenSelect, refs, refusals);
            // Limitar as linhas muda o resultado: é decisão de quem usa a consulta, não da reescrita.
            if (!Limited(originalSelect) && Limited(rewrittenSelect))
                refusals.Add("A reescrita limita as linhas (TOP ou OFFSET/FETCH) e a original não: o resultado muda. Se a tela mostra uma página, isso é decisão sua, fora da reescrita.");
        }

        if (refusals.Count > 0)
            return new RewriteReview(sql, false, refusals.Distinct().Take(MaxRefusals).ToList(), null);
        return new RewriteReview(sql, true, [], analyzer.Analyze(sql, schema));
    }

    private static RewriteReview Refused(string sql, string reason) => new(sql, false, [reason], null);

    private static (TSqlScript? Script, string? Error) Parse(string sql)
    {
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        IList<ParseError> errors;
        TSqlFragment? tree;
        using (var reader = new StringReader(sql))
            tree = parser.Parse(reader, out errors);
        if (errors.Count == 0 && tree is TSqlScript script) return (script, null);
        var first = errors.FirstOrDefault();
        return (null, first is null ? "texto vazio" : $"linha {first.Line}, coluna {first.Column}: {SqlMessageTranslator.Translate(first.Message)}");
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

    private static void Tables(References refs, References original, List<string> refusals)
    {
        foreach (var r in refs.Named)
        {
            if (r.Cte || r.Known) continue;
            if (original.Named.Any(o => o.Key == r.Key)) continue;
            refusals.Add(r.Temporary
                ? $"A reescrita usa {r.Name}, que a consulta original não usa."
                : $"A reescrita cita a tabela {r.Name}, que não está no esquema lido do código.");
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
    private static void Columns(TSqlStatement statement, References refs, HashSet<string> originalColumns, List<string> refusals)
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
                        refusals.Add($"A reescrita cita {written}, mas {qualifier} não é tabela nem apelido da consulta.");
                    continue;
                }
                if (!cited && match.Columns is { } known && !known.Contains(name, StringComparer.OrdinalIgnoreCase))
                    refusals.Add($"A reescrita cita {written}, e a coluna {name} não existe em {match.Name}.");
                continue;
            }

            if (cited || aliases.Found.Contains(name) || refs.All.Count == 0 || refs.All.Any(r => r.Columns is null)) continue;
            if (!refs.All.Any(r => r.Columns!.Contains(name, StringComparer.OrdinalIgnoreCase)))
                refusals.Add($"A reescrita cita a coluna {name}, que não existe em nenhuma tabela da consulta ({string.Join(", ", refs.All.Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase))}).");
        }
    }

    private static void SameColumns(SelectStatement original, References originalRefs, SelectStatement rewritten, References refs, List<string> refusals)
    {
        var before = Output(original, originalRefs);
        var after = Output(rewritten, refs);
        if (before is null || after is null)
        {
            refusals.Add("Não dá para conferir que a reescrita devolve as mesmas colunas: um SELECT * cita uma tabela de colunas desconhecidas. Liste as colunas na consulta.");
            return;
        }
        if (before.Count != after.Count)
        {
            refusals.Add($"A consulta original devolve {Columns(before.Count)} ({Names(before)}) e a reescrita devolve {Columns(after.Count)}: a reescrita precisa devolver as mesmas colunas, na mesma ordem.");
            return;
        }
        for (var i = 0; i < before.Count; i++)
        {
            var same = before[i] is null ? after[i] is null : after[i]?.Equals(before[i], StringComparison.OrdinalIgnoreCase) == true;
            if (same) continue;
            refusals.Add($"A coluna {i + 1} muda: a original devolve {before[i] ?? "uma expressão sem nome"} e a reescrita devolve {after[i] ?? "uma expressão sem nome"}. Mantenha os mesmos nomes, na mesma ordem.");
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

    private static string Columns(int n) => n == 1 ? "1 coluna" : $"{n} colunas";

    private static string Names(List<string?> names) => string.Join(", ", names.Select(n => n ?? "(sem nome)"));

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

    private static List<IndexReview> Indexes(IReadOnlyList<IndexSuggestion> suggestions, DbSchema schema)
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
                    ? $"{tableName} é uma view: o índice vai na tabela."
                    : $"A tabela {tableName} não está no esquema lido do código."));
                continue;
            }
            var display = DbNames.Display(table.Schema, table.Name, schema.Dialect);
            if (columns.Count == 0)
            {
                reviews.Add(Refuse("O índice não tem coluna."));
                continue;
            }
            var missing = columns.Concat(include).FirstOrDefault(c => !table.Columns.Any(t => t.Name.Equals(c, StringComparison.OrdinalIgnoreCase)));
            if (missing is not null)
            {
                reviews.Add(Refuse($"A coluna {missing} não existe em {display}."));
                continue;
            }
            // Os nomes como o esquema escreve.
            columns = columns.Select(c => table.Columns.First(t => t.Name.Equals(c, StringComparison.OrdinalIgnoreCase)).Name).ToList();
            include = include.Select(c => table.Columns.First(t => t.Name.Equals(c, StringComparison.OrdinalIgnoreCase)).Name).ToList();

            if (Existing(table, columns) is { } existing)
            {
                reviews.Add(Refuse($"Já existe {existing}, que começa por essas colunas."));
                continue;
            }
            if (reviews.Any(r => r.Accepted && r.Table.Equals(display, StringComparison.OrdinalIgnoreCase) && r.Columns.SequenceEqual(columns, StringComparer.OrdinalIgnoreCase)))
            {
                reviews.Add(Refuse("Repete outro índice desta proposta."));
                continue;
            }
            reviews.Add(new IndexReview(display, columns, include, s.Unique, reason, true, null, CreateIndex(table, columns, include, s.Unique, schema.Dialect)));
        }
        return reviews;
    }

    private static List<string> Clean(IReadOnlyList<string>? names) =>
        (names ?? []).Select(DbNames.Unquote).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    // A chave primária ou o índice cujas colunas começam pelas colunas sugeridas, na mesma ordem.
    private static string? Existing(DbTable table, List<string> columns)
    {
        static bool Starts(IReadOnlyList<string> have, List<string> want) =>
            have.Count >= want.Count && want.Select((c, i) => have[i].Equals(c, StringComparison.OrdinalIgnoreCase)).All(x => x);

        if (table.PrimaryKey is { } pk && Starts(pk.Columns, columns))
            return $"a chave primária{(pk.Name is null ? "" : " " + pk.Name)} ({string.Join(", ", pk.Columns)})";
        return table.Indexes.FirstOrDefault(i => Starts(i.Columns, columns)) is { } index
            ? $"o índice {index.Name ?? "sem nome"} ({string.Join(", ", index.Columns)})"
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

        public static References Of(TSqlStatement statement, DbSchema schema)
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
                    result.All.Add(new TableUse(alias ?? "(subconsulta)", string.Empty, alias, null, null, false, false, false, reference));
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
