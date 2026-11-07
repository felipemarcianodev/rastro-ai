using Microsoft.SqlServer.TransactSql.ScriptDom;
using Rastro.Core.Database;

namespace Rastro.Extractor.Database;

// .sql e projeto de banco do SQL Server pelo leitor oficial de T-SQL (ScriptDom, MIT): CREATE TABLE,
// ALTER TABLE ... ADD (chaves), CREATE INDEX, CREATE VIEW e CREATE PROCEDURE. O resto do arquivo
// (INSERT de carga, GRANT, DROP) é ignorado. Arquivo que o leitor não entende vira uma nota, com a linha.
public sealed class TSqlSchemaReader : ISqlSchemaReader
{
    public SqlDialect Dialect => SqlDialect.SqlServer;

    public SchemaFragment Read(string text, string file, int firstLine, SchemaSourceKind kind)
    {
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        TSqlFragment? tree;
        IList<ParseError> errors;
        using (var reader = new StringReader(text))
            tree = parser.Parse(reader, out errors);

        var notes = new List<SchemaNote>();
        if (errors.Count > 0)
        {
            var first = errors[0];
            notes.Add(new SchemaNote($"O leitor de T-SQL não entendeu o arquivo ({SqlMessageTranslator.Translate(first.Message)}); o que vem depois do erro pode ter ficado de fora.", file, firstLine + first.Line - 1));
        }
        if (tree is null) return new SchemaFragment([], [], [], notes);

        var visitor = new SchemaVisitor(file, firstLine, kind);
        tree.Accept(visitor);
        return new SchemaFragment(visitor.Tables(), visitor.Views, visitor.Procedures, notes);
    }

    private sealed class SchemaVisitor(string file, int firstLine, SchemaSourceKind kind) : TSqlFragmentVisitor
    {
        private readonly Dictionary<string, TableBuilder> _tables = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _order = [];

        public List<DbView> Views { get; } = [];
        public List<DbProcedure> Procedures { get; } = [];

        public List<DbTable> Tables() => _order.Select(k => _tables[k].Build()).ToList();

        private SchemaSource Source(TSqlFragment fragment) => new(kind, file, firstLine + Math.Max(fragment.StartLine, 1) - 1);

        private TableBuilder Table(SchemaObjectName name, TSqlFragment at)
        {
            var key = DbNames.Key(name.SchemaIdentifier?.Value, name.BaseIdentifier.Value, SqlDialect.SqlServer);
            if (!_tables.TryGetValue(key, out var table))
            {
                table = new TableBuilder(name.SchemaIdentifier?.Value, name.BaseIdentifier.Value, Source(at));
                _tables[key] = table;
                _order.Add(key);
            }
            return table;
        }

        public override void ExplicitVisit(CreateTableStatement node)
        {
            if (node.Definition is null || IsTemporary(node.SchemaObjectName)) return;
            var table = Table(node.SchemaObjectName, node);
            table.Defined = true;
            AddDefinition(table, node.Definition);
        }

        public override void ExplicitVisit(AlterTableAddTableElementStatement node)
        {
            if (node.Definition is null || IsTemporary(node.SchemaObjectName)) return;
            AddDefinition(Table(node.SchemaObjectName, node), node.Definition);
        }

        public override void ExplicitVisit(CreateIndexStatement node)
        {
            if (node.OnName is null || IsTemporary(node.OnName)) return;
            Table(node.OnName, node).Indexes.Add(new DbIndex(
                node.Name?.Value,
                node.Columns.Select(c => ColumnName(c.Column)).ToList(),
                node.IncludeColumns.Select(ColumnName).ToList(),
                node.Unique,
                Source(node)));
        }

        public override void ExplicitVisit(CreateViewStatement node) => AddView(node);

        public override void ExplicitVisit(CreateOrAlterViewStatement node) => AddView(node);

        public override void ExplicitVisit(CreateProcedureStatement node) => AddProcedure(node);

        public override void ExplicitVisit(CreateOrAlterProcedureStatement node) => AddProcedure(node);

        private void AddDefinition(TableBuilder table, TableDefinition definition)
        {
            foreach (var column in definition.ColumnDefinitions)
                AddColumn(table, column);

            foreach (var constraint in definition.TableConstraints)
                AddConstraint(table, constraint, null);

            foreach (var index in definition.Indexes)
                table.Indexes.Add(new DbIndex(index.Name?.Value, index.Columns.Select(c => ColumnName(c.Column)).ToList(),
                    index.IncludeColumns.Select(ColumnName).ToList(), index.Unique, Source(index)));
        }

        private void AddColumn(TableBuilder table, ColumnDefinition column)
        {
            var name = column.ColumnIdentifier.Value;
            bool? nullable = null;
            foreach (var constraint in column.Constraints)
            {
                if (constraint is NullableConstraintDefinition n) nullable = n.Nullable;
                else AddConstraint(table, constraint, name);
            }

            table.Columns.Add(new ColumnBuilder(
                name,
                column.DataType is null ? null : TypeText(column.DataType),
                nullable,
                column.DefaultConstraint?.Expression is { } expression ? Text(expression) : null,
                column.IdentityOptions is not null,
                Source(column)));
        }

        // column: a coluna dona da restrição, quando ela vem na própria definição da coluna.
        private void AddConstraint(TableBuilder table, ConstraintDefinition constraint, string? column)
        {
            switch (constraint)
            {
                case UniqueConstraintDefinition { IsPrimaryKey: true } pk:
                    table.PrimaryKey = new DbPrimaryKey(pk.ConstraintIdentifier?.Value,
                        pk.Columns.Count > 0 ? pk.Columns.Select(c => ColumnName(c.Column)).ToList() : [column ?? string.Empty], Source(pk));
                    break;
                case UniqueConstraintDefinition unique:
                    table.Indexes.Add(new DbIndex(unique.ConstraintIdentifier?.Value,
                        unique.Columns.Count > 0 ? unique.Columns.Select(c => ColumnName(c.Column)).ToList() : [column ?? string.Empty], [], true, Source(unique)));
                    break;
                case ForeignKeyConstraintDefinition fk:
                    table.ForeignKeys.Add(new DbForeignKey(
                        fk.ConstraintIdentifier?.Value,
                        fk.Columns.Count > 0 ? fk.Columns.Select(c => c.Value).ToList() : [column ?? string.Empty],
                        DbNames.Display(fk.ReferenceTableName.SchemaIdentifier?.Value, fk.ReferenceTableName.BaseIdentifier.Value, SqlDialect.SqlServer),
                        fk.ReferencedTableColumns.Select(c => c.Value).ToList(),
                        fk.DeleteAction switch
                        {
                            DeleteUpdateAction.Cascade => DeleteAction.Cascade,
                            DeleteUpdateAction.SetNull => DeleteAction.SetNull,
                            DeleteUpdateAction.SetDefault => DeleteAction.SetDefault,
                            _ => DeleteAction.NoAction
                        },
                        Source(fk)));
                    break;
            }
        }

        private void AddView(ViewStatementBody node)
        {
            var columns = node.Columns.Count > 0
                ? node.Columns.Select(c => c.Value).ToList()
                : SelectColumns(node.SelectStatement?.QueryExpression);
            var tables = new TableCollector();
            node.SelectStatement?.Accept(tables);
            Views.Add(new DbView(node.SchemaObjectName.SchemaIdentifier?.Value, node.SchemaObjectName.BaseIdentifier.Value,
                columns, tables.Reads(), Source(node)));
        }

        private void AddProcedure(ProcedureStatementBody node)
        {
            var tables = new TableCollector();
            node.StatementList?.Accept(tables);
            var name = node.ProcedureReference.Name;
            Procedures.Add(new DbProcedure(
                name.SchemaIdentifier?.Value,
                name.BaseIdentifier.Value,
                node.Parameters.Select(p => new DbParameter(p.VariableName.Value, p.DataType is null ? string.Empty : TypeText(p.DataType), p.Modifier == ParameterModifier.Output)).ToList(),
                tables.Reads(),
                tables.Writes(),
                Source(node)));
        }

        private static List<string> SelectColumns(QueryExpression? query)
        {
            while (query is BinaryQueryExpression binary) query = binary.FirstQueryExpression;
            while (query is QueryParenthesisExpression parenthesis) query = parenthesis.QueryExpression;
            if (query is not QuerySpecification spec) return [];

            var columns = new List<string>();
            foreach (var element in spec.SelectElements)
            {
                if (element is SelectStarExpression) columns.Add("*");
                else if (element is SelectScalarExpression scalar)
                {
                    if (scalar.ColumnName?.Value is { } alias) columns.Add(alias);
                    else if (scalar.Expression is ColumnReferenceExpression reference && reference.MultiPartIdentifier is { } id)
                        columns.Add(id.Identifiers[^1].Value);
                }
            }
            return columns;
        }

        private static bool IsTemporary(SchemaObjectName name) =>
            name.BaseIdentifier?.Value is not { } value || value.StartsWith('#');

        private static string ColumnName(ColumnReferenceExpression column) =>
            column.MultiPartIdentifier?.Identifiers is { Count: > 0 } ids ? ids[^1].Value : string.Empty;
    }

    // Tabelas lidas e gravadas num trecho (view ou corpo de procedure). O alvo de INSERT, UPDATE, DELETE
    // e MERGE é gravado; o alias do alvo ("UPDATE p ... FROM produtos p") volta para o nome da tabela.
    private sealed class TableCollector : TSqlFragmentVisitor
    {
        private readonly List<NamedTableReference> _all = [];
        private readonly List<TableReference> _targets = [];

        public override void Visit(NamedTableReference node) => _all.Add(node);

        public override void Visit(InsertSpecification node) => _targets.Add(node.Target);

        public override void Visit(UpdateSpecification node) => _targets.Add(node.Target);

        public override void Visit(DeleteSpecification node) => _targets.Add(node.Target);

        public override void Visit(MergeSpecification node) => _targets.Add(node.Target);

        public List<string> Reads()
        {
            var targets = _targets.OfType<NamedTableReference>().ToHashSet();
            return _all.Where(t => !targets.Contains(t) && !IsLocal(t))
                .Select(Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public List<string> Writes()
        {
            var aliases = Aliases();
            return _targets.OfType<NamedTableReference>()
                .Where(t => !IsLocal(t))
                .Select(t => t.SchemaObject.SchemaIdentifier is null && aliases.TryGetValue(t.SchemaObject.BaseIdentifier.Value, out var real) ? real : Name(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private Dictionary<string, string> Aliases()
        {
            var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var table in _all.Where(t => t.Alias is not null && !IsLocal(t)))
                aliases.TryAdd(table.Alias.Value, Name(table));
            return aliases;
        }

        private static bool IsLocal(NamedTableReference table) =>
            table.SchemaObject?.BaseIdentifier?.Value is not { } name || name.StartsWith('#') || name.StartsWith('@');

        private static string Name(NamedTableReference table) =>
            DbNames.Display(table.SchemaObject.SchemaIdentifier?.Value, table.SchemaObject.BaseIdentifier.Value, SqlDialect.SqlServer);
    }

    // "nvarchar(30)", "decimal(10,2)", "nvarchar(max)", ou o nome do tipo do usuário.
    private static string TypeText(DataTypeReference type)
    {
        if (type is SqlDataTypeReference sql)
        {
            var name = sql.SqlDataTypeOption.ToString().ToLowerInvariant();
            if (sql.Parameters.Count == 0) return name;
            var args = sql.Parameters.Select(p => p is MaxLiteral ? "max" : p.Value);
            return $"{name}({string.Join(",", args)})";
        }
        return type.Name is { } n ? DbNames.Display(n.SchemaIdentifier?.Value, n.BaseIdentifier.Value, SqlDialect.SqlServer).ToLowerInvariant() : Text(type);
    }

    private static string Text(TSqlFragment fragment)
    {
        if (fragment.ScriptTokenStream is null || fragment.FirstTokenIndex < 0) return string.Empty;
        var parts = new System.Text.StringBuilder();
        for (var i = fragment.FirstTokenIndex; i <= fragment.LastTokenIndex; i++)
            parts.Append(fragment.ScriptTokenStream[i].Text);
        return parts.ToString().Trim();
    }

    private sealed class TableBuilder(string? schema, string name, SchemaSource source)
    {
        public bool Defined { get; set; }
        public List<ColumnBuilder> Columns { get; } = [];
        public DbPrimaryKey? PrimaryKey { get; set; }
        public List<DbForeignKey> ForeignKeys { get; } = [];
        public List<DbIndex> Indexes { get; } = [];

        public DbTable Build()
        {
            var keyColumns = PrimaryKey?.Columns.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            // Sem NULL nem NOT NULL, a coluna aceita nulo (padrão do SQL Server), menos a da chave primária.
            var columns = Columns.Select(c => new DbColumn(c.Name, c.Type,
                c.Nullable ?? (Defined ? !keyColumns.Contains(c.Name) : null),
                c.Default, c.Identity, [c.Source])).ToList();
            return new DbTable(schema, name, columns, PrimaryKey, ForeignKeys, Indexes, [source]);
        }
    }

    private sealed record ColumnBuilder(string Name, string? Type, bool? Nullable, string? Default, bool Identity, SchemaSource Source);
}
