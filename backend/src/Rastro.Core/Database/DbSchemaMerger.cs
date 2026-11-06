namespace Rastro.Core.Database;

// Junta o que cada fonte leu num esquema só (Fase 37). A mesma tabela em fontes diferentes vira uma
// tabela, com as fontes de cada coluna. Quando as fontes discordam no tipo ou no nulo, a coluna fica
// sem valor e a divergência mostra cada versão com a fonte: o Rastro não escolhe em silêncio.
// Na chave primária divergente, a tabela fica com a da fonte de maior prioridade (para não parecer
// "tabela sem chave") e a divergência mostra as outras.
public static class DbSchemaMerger
{
    public const string Present = "existe";
    public const string Absent = "não existe";

    public static DbSchema Merge(SqlDialect dialect, IEnumerable<SchemaFragment> fragments)
    {
        var list = fragments.ToList();
        var divergences = new List<SchemaDivergence>();
        var findings = new List<SchemaFinding>();

        var tables = list.SelectMany(f => f.Tables)
            .GroupBy(t => DbNames.Key(t.Schema, t.Name, dialect))
            .Select(g => MergeTable(dialect, g.OrderBy(t => First(t.Sources), SourceOrder.Instance).ToList(), divergences, findings))
            .OrderBy(t => DbNames.Display(t.Schema, t.Name, dialect), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var tableOrder = tables.Select((t, i) => (DbNames.Display(t.Schema, t.Name, dialect), i))
            .ToDictionary(x => x.Item1, x => x.i, StringComparer.OrdinalIgnoreCase);
        var orderedDivergences = divergences
            .OrderBy(d => tableOrder.GetValueOrDefault(d.Table, int.MaxValue))
            .ToList();

        var views = list.SelectMany(f => f.Views)
            .OrderBy(v => v.Source, SourceOrder.Instance)
            .DistinctBy(v => DbNames.Key(v.Schema, v.Name, dialect))
            .Select(v => v with { Schema = DbNames.NormalizeSchema(v.Schema, dialect), Name = DbNames.Unquote(v.Name) })
            .OrderBy(v => DbNames.Display(v.Schema, v.Name, dialect), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var procedures = list.SelectMany(f => f.Procedures)
            .OrderBy(p => p.Source, SourceOrder.Instance)
            .DistinctBy(p => DbNames.Key(p.Schema, p.Name, dialect))
            .Select(p => p with { Schema = DbNames.NormalizeSchema(p.Schema, dialect), Name = DbNames.Unquote(p.Name) })
            .OrderBy(p => DbNames.Display(p.Schema, p.Name, dialect), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var notes = list.SelectMany(f => f.Notes)
            .OrderBy(n => n.File, StringComparer.Ordinal)
            .ThenBy(n => n.Line)
            .ToList();

        var orderedFindings = findings
            .OrderBy(f => tableOrder.GetValueOrDefault(f.Table, int.MaxValue))
            .ThenBy(f => string.Join(",", f.Columns), StringComparer.Ordinal)
            .ToList();

        return new DbSchema(dialect, tables, views, procedures, orderedDivergences, notes, orderedFindings);
    }

    private static DbTable MergeTable(SqlDialect dialect, List<DbTable> group, List<SchemaDivergence> divergences, List<SchemaFinding> findings)
    {
        var head = group[0];
        var schema = group.Select(t => DbNames.NormalizeSchema(t.Schema, dialect)).FirstOrDefault(s => s is not null);
        var name = DbNames.Unquote(head.Name);
        var display = DbNames.Display(schema, name, dialect);

        var columns = new List<DbColumn>();
        foreach (var byName in group.SelectMany(t => t.Columns).GroupBy(c => DbNames.Unquote(c.Name), StringComparer.OrdinalIgnoreCase))
        {
            var versions = byName.ToList();
            var type = Agree(versions.Where(c => c.Type is not null).Select(c => (Value: c.Type!, Key: DbNames.NormalizeType(c.Type!), Source: First(c.Sources))).ToList(),
                SchemaDivergenceKind.ColumnType, display, byName.Key, divergences);
            var nullable = Agree(versions.Where(c => c.Nullable is not null).Select(c => (Value: c.Nullable!.Value ? "aceita nulo" : "não aceita nulo", Key: c.Nullable!.Value.ToString(), Source: First(c.Sources))).ToList(),
                SchemaDivergenceKind.ColumnNullability, display, byName.Key, divergences);

            columns.Add(new DbColumn(
                DbNames.Unquote(versions[0].Name),
                type is null ? null : versions.First(c => c.Type is not null && DbNames.NormalizeType(c.Type) == type).Type,
                nullable is null ? null : bool.Parse(nullable),
                versions.Select(c => c.Default).FirstOrDefault(d => d is not null),
                versions.Any(c => c.Identity),
                versions.SelectMany(c => c.Sources).Distinct().OrderBy(s => s, SourceOrder.Instance).ToList(),
                PersonalData.IsPersonal(DbNames.Unquote(versions[0].Name))));
        }

        MissingColumns(group, display, divergences);

        var keys = group.Where(t => t.PrimaryKey is not null).Select(t => t.PrimaryKey!).ToList();
        var distinctKeys = keys.DistinctBy(k => string.Join(",", k.Columns.Select(c => DbNames.Unquote(c).ToLowerInvariant()))).ToList();
        if (distinctKeys.Count > 1)
            divergences.Add(new SchemaDivergence(SchemaDivergenceKind.PrimaryKey, display, string.Empty,
                distinctKeys.Select(k => new DivergentValue(string.Join(", ", k.Columns), k.Source)).ToList()));

        var foreignKeys = group.SelectMany(t => t.ForeignKeys)
            .Select(fk => fk with { PrincipalTable = DisplayOf(fk.PrincipalTable, dialect) })
            .DistinctBy(fk => string.Join(",", fk.Columns).ToLowerInvariant() + ">" + DbNames.KeyOf(fk.PrincipalTable, dialect) + "(" + string.Join(",", fk.PrincipalColumns).ToLowerInvariant() + ")")
            .ToList();

        // A migration e o .sql dizem o que o banco tem: com eles, o índice deduzido da convenção do EF
        // sai, e a FK que eles criam sem índice vira achado. Sem eles, vale o modelo, com a convenção.
        // Um ALTER TABLE solto num .sql traz a FK, mas não diz a tabela inteira: só a migration, o
        // CREATE TABLE e o banco vivo (versões com colunas) tiram a convenção.
        var outside = group.Where(t => First(t.Sources).Kind != SchemaSourceKind.EfModel).ToList();
        var physical = outside.Any(t => t.Columns.Count > 0);
        var indexes = group.SelectMany(t => t.Indexes)
            .Where(i => !i.Convention || !physical)
            .OrderBy(i => i.Convention ? 1 : 0)
            .DistinctBy(i => string.Join(",", i.Columns).ToLowerInvariant() + (i.Unique ? "|u" : string.Empty))
            .ToList();

        var primaryKey = keys.FirstOrDefault();
        // Só o que o banco tem cobre a FK: um HasIndex do modelo que a migration não criou não conta.
        var realKey = outside.Select(t => t.PrimaryKey).FirstOrDefault(k => k is not null);
        var realIndexes = outside.SelectMany(t => t.Indexes).ToList();
        if (physical)
            foreach (var fk in outside.SelectMany(t => t.ForeignKeys).DistinctBy(fk => string.Join(",", fk.Columns).ToLowerInvariant()))
            {
                var covered = (realKey is not null && Leads(realKey.Columns, fk.Columns)) || realIndexes.Any(i => Leads(i.Columns, fk.Columns));
                if (!covered) findings.Add(new SchemaFinding(SchemaFindingKind.ForeignKeyWithoutIndex, display, fk.Columns.Select(DbNames.Unquote).ToList(), fk.Source));
            }

        return new DbTable(schema, name, columns, primaryKey, foreignKeys, indexes,
            group.SelectMany(t => t.Sources).Distinct().OrderBy(s => s, SourceOrder.Instance).ToList(),
            group.Select(t => t.Entity).FirstOrDefault(e => e is not null));
    }

    // O índice serve à FK quando as primeiras colunas dele são as colunas da FK (em qualquer ordem).
    private static bool Leads(IReadOnlyList<string> indexColumns, IReadOnlyList<string> fkColumns) =>
        indexColumns.Count >= fkColumns.Count
        && indexColumns.Take(fkColumns.Count).Select(c => DbNames.Unquote(c).ToLowerInvariant()).ToHashSet()
            .SetEquals(fkColumns.Select(c => DbNames.Unquote(c).ToLowerInvariant()));

    // Devolve a chave do valor em que todas as versões concordam, ou nulo (sem versão ou com divergência registrada).
    private static string? Agree(List<(string Value, string Key, SchemaSource Source)> versions, SchemaDivergenceKind kind, string table, string column, List<SchemaDivergence> divergences)
    {
        if (versions.Count == 0) return null;
        var distinct = versions.DistinctBy(v => v.Key).ToList();
        if (distinct.Count == 1) return distinct[0].Key;

        divergences.Add(new SchemaDivergence(kind, table, column,
            versions.Select(v => new DivergentValue(v.Value, v.Source)).ToList()));
        return null;
    }

    // Só entre modelo e migration, que descrevem a mesma tabela inteira; um .sql pode trazer só parte dela.
    private static void MissingColumns(List<DbTable> group, string table, List<SchemaDivergence> divergences)
    {
        var model = group.Where(t => First(t.Sources).Kind == SchemaSourceKind.EfModel).ToList();
        var migration = group.Where(t => First(t.Sources).Kind == SchemaSourceKind.EfMigration).ToList();
        if (model.Count == 0 || migration.Count == 0) return;

        Compare(model, migration);
        Compare(migration, model);

        void Compare(List<DbTable> from, List<DbTable> other)
        {
            var otherNames = other.SelectMany(t => t.Columns).Select(c => DbNames.Unquote(c.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var column in from.SelectMany(t => t.Columns).Where(c => !otherNames.Contains(DbNames.Unquote(c.Name))))
                divergences.Add(new SchemaDivergence(SchemaDivergenceKind.ColumnMissing, table, DbNames.Unquote(column.Name),
                [
                    new DivergentValue(Present, First(column.Sources)),
                    new DivergentValue(Absent, First(other[0].Sources))
                ]));
        }
    }

    private static string DisplayOf(string qualified, SqlDialect dialect)
    {
        var dot = qualified.LastIndexOf('.');
        return dot < 0 ? DbNames.Unquote(qualified) : DbNames.Display(qualified[..dot], qualified[(dot + 1)..], dialect);
    }

    private static SchemaSource First(IReadOnlyList<SchemaSource> sources) =>
        sources.Count > 0 ? sources[0] : new SchemaSource(SchemaSourceKind.LiveDatabase, string.Empty, 0);

    private sealed class SourceOrder : IComparer<SchemaSource>
    {
        public static readonly SourceOrder Instance = new();

        public int Compare(SchemaSource? x, SchemaSource? y)
        {
            if (x is null || y is null) return x is null ? (y is null ? 0 : -1) : 1;
            var kind = x.Kind.CompareTo(y.Kind);
            if (kind != 0) return kind;
            var file = string.CompareOrdinal(x.File, y.File);
            return file != 0 ? file : x.Line.CompareTo(y.Line);
        }
    }
}
