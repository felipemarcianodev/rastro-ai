using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Rastro.Core.Database;
using static Rastro.Extractor.Database.CsSyntax;

namespace Rastro.Extractor.Database;

// Migrations do EF Core lidas do código (Fase 37, ADR-006): o Up de cada migration, na ordem do id
// ([Migration("20261012_Pedidos")]), aplicado num estado só. O resultado é o banco que as migrations
// constroem, com a migration e a linha de cada coluna, chave e índice. O SQL solto (migrationBuilder.Sql)
// passa pelo leitor de SQL do dialeto. Os argumentos são lidos pelo nome, como o EF gera.
internal sealed class EfMigrationReader(CodeIndex code, SqlDialect dialect)
{
    private const SchemaSourceKind Kind = SchemaSourceKind.EfMigration;

    private readonly Dictionary<string, TableState> _tables = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];
    private readonly List<SchemaFragment> _sql = [];
    private readonly List<SchemaNote> _notes = [];

    public IReadOnlyList<SchemaFragment> Read()
    {
        var migrations = code.Primary
            .SelectMany(p => p.Tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Select(c => (Class: c, p.File)))
            .Where(m => m.Class.BaseList?.Types.Any(t => BaseName(t.Type) == "Migration") == true)
            .Select(m => (m.Class, m.File, Id: MigrationId(m.Class, m.File)))
            .OrderBy(m => m.Id, StringComparer.Ordinal)
            .ToList();

        foreach (var (migration, file, _) in migrations)
        {
            var up = migration.Members.OfType<MethodDeclarationSyntax>().FirstOrDefault(m => m.Identifier.ValueText == "Up");
            if (up?.ParameterList.Parameters.FirstOrDefault()?.Identifier.ValueText is not { } builder) continue;
            foreach (var statement in Statements((SyntaxNode?)up.Body ?? up.ExpressionBody))
            {
                var (root, calls) = Chain(statement);
                if (root is IdentifierNameSyntax id && id.Identifier.ValueText == builder && calls.Count > 0)
                    Apply(calls[0], file);
            }
        }

        var fragments = new List<SchemaFragment> { new(_order.Where(_tables.ContainsKey).Select(k => _tables[k].Build()).ToList(), [], [], _notes) };
        fragments.AddRange(_sql);
        return fragments;
    }

    // O id do [Migration("...")] (o que o EF usa para ordenar) ou, como o atributo costuma ficar no
    // .Designer.cs, o nome do arquivo, que o EF gera com o mesmo id ("20261012_Pedidos.cs").
    private static string MigrationId(ClassDeclarationSyntax migration, string file) =>
        Attribute(migration, "Migration", out var attribute) is not null && String(AttributeArg(attribute!, null, 0)) is { } id
            ? id
            : Path.GetFileNameWithoutExtension(file);

    private void Apply(InvocationExpressionSyntax call, string file)
    {
        var source = Source(Kind, file, call);
        var table = String(Arg(call, "table"));
        var schema = String(Arg(call, "schema"));
        switch (MethodName(call))
        {
            case "CreateTable":
                CreateTable(call, file, source);
                break;
            case "DropTable":
                if (String(Arg(call, "name", 0)) is { } dropped) _tables.Remove(Key(String(Arg(call, "schema")), dropped));
                break;
            case "RenameTable":
                if (String(Arg(call, "name", 0)) is { } old && String(Arg(call, "newName")) is { } renamed && _tables.Remove(Key(schema, old), out var moved))
                {
                    moved.Name = renamed;
                    moved.Schema = String(Arg(call, "newSchema")) ?? moved.Schema;
                    var key = Key(moved.Schema, renamed);
                    _tables[key] = moved;
                    _order.Add(key);
                }
                break;
            case "AddColumn" or "AlterColumn":
                if (Find(schema, table) is { } target && String(Arg(call, "name")) is { } column)
                {
                    var built = Column(call, column, source);
                    var index = target.Columns.FindIndex(c => c.Name.Equals(column, StringComparison.OrdinalIgnoreCase));
                    if (index >= 0) target.Columns[index] = built; else target.Columns.Add(built);
                }
                break;
            case "DropColumn":
                if (Find(schema, table) is { } from && String(Arg(call, "name")) is { } droppedColumn)
                    from.Columns.RemoveAll(c => c.Name.Equals(droppedColumn, StringComparison.OrdinalIgnoreCase));
                break;
            case "RenameColumn":
                if (Find(schema, table) is { } owner && String(Arg(call, "name")) is { } before && String(Arg(call, "newName")) is { } after)
                {
                    var index = owner.Columns.FindIndex(c => c.Name.Equals(before, StringComparison.OrdinalIgnoreCase));
                    if (index >= 0) owner.Columns[index] = owner.Columns[index] with { Name = after };
                }
                break;
            case "CreateIndex":
                if (Find(schema, table) is { } indexed)
                    indexed.Indexes.Add(new DbIndex(String(Arg(call, "name")), ColumnsArg(call, "column", "columns"),
                        Strings(Arg(call, "include")), Bool(Arg(call, "unique")) ?? false, source));
                break;
            case "DropIndex":
                if (Find(schema, table) is { } unindexed && String(Arg(call, "name")) is { } indexName)
                    unindexed.Indexes.RemoveAll(i => i.Name == indexName);
                break;
            case "AddForeignKey":
                if (Find(schema, table) is { } dependent)
                    dependent.ForeignKeys.Add(ForeignKey(call, source));
                break;
            case "DropForeignKey":
                if (Find(schema, table) is { } loose && String(Arg(call, "name")) is { } fkName)
                    loose.ForeignKeys.RemoveAll(f => f.Name == fkName);
                break;
            case "AddPrimaryKey":
                if (Find(schema, table) is { } keyed)
                    keyed.PrimaryKey = new DbPrimaryKey(String(Arg(call, "name")), ColumnsArg(call, "column", "columns"), source);
                break;
            case "DropPrimaryKey":
                if (Find(schema, table) is { } unkeyed) unkeyed.PrimaryKey = null;
                break;
            case "Sql":
                if (Arg(call, "sql", 0) is { } sqlArg && String(sqlArg) is { } sql)
                {
                    if (SqlSchemaReaders.For(dialect) is { } reader)
                        _sql.Add(reader.Read(sql, file, Line(sqlArg), Kind));
                }
                else
                    _notes.Add(new SchemaNote("SQL montado em tempo de execução numa migration: o Rastro não lê.", file, source.Line));
                break;
        }
    }

    private void CreateTable(InvocationExpressionSyntax call, string file, SchemaSource source)
    {
        if (String(Arg(call, "name", 0)) is not { } name) return;
        var table = new TableState(String(Arg(call, "schema")), name, source);

        // columns: table => new { id = table.Column<int>(...), ... }
        if (Lambda(Arg(call, "columns")) is { Body: AnonymousObjectCreationExpressionSyntax columns })
            foreach (var member in columns.Initializers)
            {
                if (member.Expression is not InvocationExpressionSyntax column) continue;
                var (_, chain) = Chain(member.Expression);
                var definition = chain.FirstOrDefault(c => MethodName(c) == "Column") ?? column;
                var columnName = String(Arg(definition, "name")) ?? member.NameEquals?.Name.Identifier.ValueText ?? LastName(member.Expression) ?? string.Empty;
                var built = Column(definition, columnName, Source(Kind, file, member));
                if (chain.Any(c => MethodName(c) == "Annotation" && String(Arg(c, "name", 0)) is { } annotation && annotation.EndsWith(":Identity", StringComparison.Ordinal)))
                    built = built with { Identity = true };
                table.Columns.Add(built);
            }

        // constraints: table => { table.PrimaryKey(...); table.ForeignKey(...); }
        if (Lambda(Arg(call, "constraints")) is { } constraints)
            foreach (var statement in Statements(constraints.Body))
            {
                if (statement is not InvocationExpressionSyntax constraint) continue;
                var at = Source(Kind, file, constraint);
                switch (MethodName(constraint))
                {
                    case "PrimaryKey":
                        table.PrimaryKey = new DbPrimaryKey(String(Arg(constraint, "name", 0)), LambdaOrColumns(constraint), at);
                        break;
                    case "ForeignKey":
                        table.ForeignKeys.Add(ForeignKey(constraint, at));
                        break;
                    case "UniqueConstraint":
                        table.Indexes.Add(new DbIndex(String(Arg(constraint, "name", 0)), LambdaOrColumns(constraint), [], true, at));
                        break;
                }
            }

        var key = Key(table.Schema, name);
        _tables[key] = table;
        _order.Remove(key);
        _order.Add(key);
    }

    // table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false); AddColumn e AlterColumn têm os mesmos argumentos.
    private DbColumn Column(InvocationExpressionSyntax call, string name, SchemaSource source)
    {
        var type = String(Arg(call, "type"));
        if (type is null && GenericArgument(call) is { } clr)
        {
            var clrName = clr is NullableTypeSyntax n ? n.ElementType.ToString() : clr.ToString();
            type = ClrTypeMap.Map(dialect, new ClrColumn(clrName, null, Int(Arg(call, "maxLength")), Int(Arg(call, "precision")), Int(Arg(call, "scale")), Bool(Arg(call, "unicode")), false));
        }
        var defaultValue = String(Arg(call, "defaultValueSql")) ?? Arg(call, "defaultValue")?.ToString();
        // Sem "nullable", o EF entende que a coluna não aceita nulo.
        return new DbColumn(name, type, Bool(Arg(call, "nullable")) ?? false, defaultValue, false, [source]);
    }

    private DbForeignKey ForeignKey(InvocationExpressionSyntax call, SchemaSource source)
    {
        var columns = Arg(call, "column") is LambdaExpressionSyntax || Arg(call, "columns") is LambdaExpressionSyntax
            ? LambdaMembers(Arg(call, "column") ?? Arg(call, "columns"))
            : ColumnsArg(call, "column", "columns");
        var principalColumns = ColumnsArg(call, "principalColumn", "principalColumns");
        var principal = DbNames.Display(String(Arg(call, "principalSchema")), String(Arg(call, "principalTable")) ?? string.Empty, dialect);
        return new DbForeignKey(String(Arg(call, "name", 0)), columns, principal, principalColumns,
            Delete(Arg(call, "onDelete")) ?? DeleteAction.NoAction, source);
    }

    // "column: x => x.id" ou "columns: x => new { x.a, x.b }" nas restrições do CreateTable.
    private static List<string> LambdaOrColumns(InvocationExpressionSyntax call)
    {
        var lambda = Arg(call, "columns") ?? Arg(call, "column") ?? Arg(call, "columns", 1);
        return lambda is LambdaExpressionSyntax ? LambdaMembers(lambda) : Strings(lambda);
    }

    private static List<string> ColumnsArg(InvocationExpressionSyntax call, string single, string many) =>
        String(Arg(call, single)) is { } one ? [one] : Strings(Arg(call, many));

    private TableState? Find(string? schema, string? table) =>
        table is not null && _tables.TryGetValue(Key(schema, table), out var state) ? state : null;

    private string Key(string? schema, string name) => DbNames.Key(schema, name, dialect);

    private sealed class TableState(string? schema, string name, SchemaSource source)
    {
        public string? Schema { get; set; } = schema;
        public string Name { get; set; } = name;
        public List<DbColumn> Columns { get; } = [];
        public DbPrimaryKey? PrimaryKey { get; set; }
        public List<DbForeignKey> ForeignKeys { get; } = [];
        public List<DbIndex> Indexes { get; } = [];

        public DbTable Build() => new(Schema, Name, Columns.ToList(), PrimaryKey, ForeignKeys.ToList(), Indexes.ToList(), [source]);
    }
}
