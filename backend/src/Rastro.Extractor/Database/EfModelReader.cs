using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Rastro.Core.Database;
using static Rastro.Extractor.Database.CsSyntax;

namespace Rastro.Extractor.Database;

// Modelo do EF Core lido do código (Fase 37, ADR-006): DbContext e DbSet, atributos das entidades,
// OnModelCreating e IEntityTypeConfiguration. Só a árvore de sintaxe: nada do cliente é compilado nem
// executado. Segue as convenções do EF Core (chave "Id", chave estrangeira "<Navegação>Id", nome da
// tabela pelo DbSet) e a do EFCore.NamingConventions (UseSnakeCaseNamingConvention).
// O índice que o EF cria por convenção em cada chave estrangeira é deduzido e marcado (Convention).
// Limites, que viram nota quando aparecem: tipo próprio (OwnsOne, OwnsMany) e configuração montada em
// tempo de execução.
internal sealed class EfModelReader(CodeIndex code, SqlDialect dialect)
{
    private const SchemaSourceKind Kind = SchemaSourceKind.EfModel;

    private static readonly HashSet<string> CollectionTypes = new(StringComparer.Ordinal)
        { "ICollection", "IList", "List", "IEnumerable", "HashSet", "ISet", "IReadOnlyCollection", "IReadOnlyList", "Collection" };

    private readonly Dictionary<string, Entity> _entities = new(StringComparer.Ordinal);
    private readonly List<SchemaNote> _notes = [];
    private bool _snakeCase;

    public SchemaFragment Read()
    {
        _snakeCase = code.Calls("UseSnakeCaseNamingConvention");

        foreach (var (tree, file) in code.Primary.Where(p => !IsGenerated(p.File)))
            foreach (var type in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (IsContext(type)) ReadContext(type, file);
                else if (ConfigurationEntity(type) is { } entity) ReadConfiguration(type, entity, file);
            }

        var tables = _entities.Values
            .Where(e => e.Ready(code, _notes))
            .ToList();
        foreach (var entity in tables) Resolve(entity);
        var relations = tables.SelectMany(Relations).ToList();

        return new SchemaFragment(tables.Select(e => Table(e, relations)).ToList(), [], [], _notes);
    }

    // Migration, *.Designer.cs e o snapshot repetem o modelo em outro formato: quem lê é o EfMigrationReader.
    private static bool IsGenerated(string file) =>
        file.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) || file.EndsWith("ModelSnapshot.cs", StringComparison.OrdinalIgnoreCase);

    private static bool IsContext(ClassDeclarationSyntax type) =>
        type.BaseList?.Types.Any(t => BaseName(t.Type).EndsWith("DbContext", StringComparison.Ordinal)) == true;

    private static string? ConfigurationEntity(ClassDeclarationSyntax type) =>
        type.BaseList?.Types.Select(t => t.Type).OfType<GenericNameSyntax>()
            .Where(g => g.Identifier.ValueText == "IEntityTypeConfiguration" && g.TypeArgumentList.Arguments.Count == 1)
            .Select(g => BaseName(g.TypeArgumentList.Arguments[0]))
            .FirstOrDefault()
        ?? (type.BaseList?.Types.Select(t => t.Type).OfType<QualifiedNameSyntax>().Select(q => q.Right).OfType<GenericNameSyntax>()
            .Where(g => g.Identifier.ValueText == "IEntityTypeConfiguration")
            .Select(g => BaseName(g.TypeArgumentList.Arguments[0]))
            .FirstOrDefault());

    private Entity Get(string clrName, string file, SyntaxNode at)
    {
        if (!_entities.TryGetValue(clrName, out var entity))
        {
            entity = new Entity(clrName, Source(Kind, file, at));
            _entities[clrName] = entity;
        }
        return entity;
    }

    private void ReadContext(ClassDeclarationSyntax context, string file)
    {
        foreach (var property in context.Members.OfType<PropertyDeclarationSyntax>())
        {
            if (property.Type is not GenericNameSyntax { Identifier.ValueText: "DbSet" } set || set.TypeArgumentList.Arguments.Count != 1) continue;
            var entity = Get(BaseName(set.TypeArgumentList.Arguments[0]), file, property);
            entity.SetName ??= property.Identifier.ValueText;
            entity.Source = Source(Kind, file, property);
        }

        var onModelCreating = context.Members.OfType<MethodDeclarationSyntax>().FirstOrDefault(m => m.Identifier.ValueText == "OnModelCreating");
        if (onModelCreating?.ParameterList.Parameters.FirstOrDefault()?.Identifier.ValueText is not { } builder) return;

        foreach (var statement in Statements((SyntaxNode?)onModelCreating.Body ?? onModelCreating.ExpressionBody))
            ReadModelBuilderStatement(statement, builder, file);
    }

    private void ReadConfiguration(ClassDeclarationSyntax configuration, string entityName, string file)
    {
        var configure = configuration.Members.OfType<MethodDeclarationSyntax>().FirstOrDefault(m => m.Identifier.ValueText == "Configure");
        if (configure?.ParameterList.Parameters.FirstOrDefault()?.Identifier.ValueText is not { } builder) return;
        var entity = Get(entityName, file, configuration);
        foreach (var statement in Statements((SyntaxNode?)configure.Body ?? configure.ExpressionBody))
            ReadEntityChain(statement, builder, entity, file);
    }

    // modelBuilder.Entity<T>(b => { ... }) e modelBuilder.Entity<T>().Property(...)...
    private void ReadModelBuilderStatement(ExpressionSyntax statement, string builder, string file)
    {
        var (root, calls) = Chain(statement);
        if (root is not IdentifierNameSyntax id || id.Identifier.ValueText != builder || calls.Count == 0) return;
        if (MethodName(calls[0]) != "Entity" || GenericArgument(calls[0]) is not { } type) return;

        var entity = Get(BaseName(type), file, calls[0]);
        if (Lambda(calls[0].ArgumentList.Arguments.FirstOrDefault()?.Expression) is { } lambda)
            foreach (var inner in Statements(lambda.Body))
                ReadEntityChain(inner, lambda.Parameter, entity, file);
        ApplyCalls(entity, calls.Skip(1).ToList(), file);
    }

    private void ReadEntityChain(ExpressionSyntax statement, string builder, Entity entity, string file)
    {
        var (root, calls) = Chain(statement);
        if (root is IdentifierNameSyntax id && id.Identifier.ValueText == builder) ApplyCalls(entity, calls, file);
    }

    private void ApplyCalls(Entity entity, List<InvocationExpressionSyntax> calls, string file)
    {
        PropertyConfig? property = null;
        IndexConfig? index = null;
        RelationConfig? relation = null;

        foreach (var call in calls)
        {
            var name = MethodName(call);
            var source = Source(Kind, file, call);
            switch (name)
            {
                case "ToTable":
                    entity.Table = String(Arg(call, "name", 0)) ?? entity.Table;
                    entity.Schema = String(Arg(call, "schema", 1)) ?? entity.Schema;
                    property = null; index = null; relation = null;
                    break;
                case "HasKey":
                    entity.Key = Members(call);
                    entity.KeySource = source;
                    property = null; index = null; relation = null;
                    break;
                case "HasNoKey":
                    entity.NoKey = true;
                    break;
                case "Ignore":
                    foreach (var member in Members(call)) entity.Ignored.Add(member);
                    break;
                case "Property":
                    property = Members(call).FirstOrDefault() is { } prop ? entity.Property(prop, source) : null;
                    index = null; relation = null;
                    break;
                case "HasIndex":
                    index = new IndexConfig(Members(call), source)
                    {
                        Name = call.ArgumentList.Arguments.FirstOrDefault()?.Expression is LambdaExpressionSyntax ? String(Arg(call, "name", 1)) : null
                    };
                    entity.Indexes.Add(index);
                    property = null; relation = null;
                    break;
                case "HasOne":
                    relation = new RelationConfig(source) { Dependent = entity.ClrName, Principal = GenericArgument(call) is { } one ? BaseName(one) : null, DependentNavigation = Members(call).FirstOrDefault() };
                    entity.Relations.Add(relation);
                    property = null; index = null;
                    break;
                case "HasMany":
                    relation = new RelationConfig(source) { Principal = entity.ClrName, Dependent = GenericArgument(call) is { } many ? BaseName(many) : null, PrincipalCollection = Members(call).FirstOrDefault() };
                    entity.Relations.Add(relation);
                    property = null; index = null;
                    break;
                case "OwnsOne" or "OwnsMany":
                    _notes.Add(new SchemaNote($"Tipo próprio ({name}) em {entity.ClrName} não é lido: as colunas dele ficam de fora.", file, source.Line));
                    return;
                default:
                    if (property is not null) ApplyProperty(property, name, call, source);
                    else if (index is not null) ApplyIndex(index, name, call);
                    else if (relation is not null) ApplyRelation(relation, name, call);
                    break;
            }
        }
    }

    private static void ApplyProperty(PropertyConfig property, string? name, InvocationExpressionSyntax call, SchemaSource source)
    {
        property.Source = source;
        switch (name)
        {
            case "HasColumnName": property.Column = String(Arg(call, "name", 0)); break;
            case "HasColumnType": property.Type = String(Arg(call, "typeName", 0)); break;
            case "HasMaxLength": property.MaxLength = Int(Arg(call, "maxLength", 0)); break;
            case "HasPrecision":
                property.Precision = Int(Arg(call, "precision", 0));
                property.Scale = Int(Arg(call, "scale", 1));
                break;
            case "IsRequired": property.Required = Bool(Arg(call, "required", 0)) ?? true; break;
            case "IsUnicode": property.Unicode = Bool(Arg(call, "unicode", 0)) ?? true; break;
            case "HasDefaultValueSql": property.Default = String(Arg(call, "sql", 0)); break;
            case "HasDefaultValue": property.Default = call.ArgumentList.Arguments.FirstOrDefault()?.Expression.ToString(); break;
            case "ValueGeneratedNever": property.GeneratedNever = true; break;
            // UseIdentityByDefaultColumn, UseIdentityAlwaysColumn e UseSerialColumn: os do Npgsql.
            case "UseIdentityColumn" or "UseIdentityByDefaultColumn" or "UseIdentityAlwaysColumn" or "UseSerialColumn": property.Identity = true; break;
        }
    }

    private static void ApplyIndex(IndexConfig index, string? name, InvocationExpressionSyntax call)
    {
        switch (name)
        {
            case "IsUnique": index.Unique = Bool(Arg(call, "unique", 0)) ?? true; break;
            case "HasDatabaseName" or "HasName": index.Name = String(Arg(call, "name", 0)); break;
            case "IncludeProperties": index.Include = Members(call); break;
        }
    }

    private static void ApplyRelation(RelationConfig relation, string? name, InvocationExpressionSyntax call)
    {
        switch (name)
        {
            case "WithMany":
                relation.PrincipalCollection ??= Members(call).FirstOrDefault();
                break;
            case "WithOne":
                if (relation.PrincipalCollection is not null)
                    relation.DependentNavigation ??= Members(call).FirstOrDefault();
                break;
            case "HasForeignKey":
                relation.ForeignKey = Members(call);
                if (GenericArgument(call) is { } dependent)
                {
                    // Um para um: quem tem a chave estrangeira é o tipo do HasForeignKey<T>.
                    var other = relation.Dependent == BaseName(dependent) ? relation.Principal : relation.Dependent;
                    relation.Dependent = BaseName(dependent);
                    if (other != relation.Dependent) relation.Principal = other;
                }
                break;
            case "OnDelete":
                relation.OnDelete = Delete(Arg(call, "deleteBehavior", 0));
                break;
        }
    }

    // Nome da tabela, propriedades, chave e o tipo de cada navegação, antes das relações.
    private void Resolve(Entity entity)
    {
        var (decl, file) = entity.Declaration!.Value;
        entity.NameRule = Name;
        var table = Attr(decl, "Table", out var tableAttr) is not null ? String(AttributeArg(tableAttr!, null, 0)) : null;
        entity.Schema ??= tableAttr is null ? null : String(AttributeArg(tableAttr, "Schema"));
        entity.TableName = entity.Table ?? table ?? Name(entity.SetName ?? entity.ClrName);
        if (Attr(decl, "Keyless", out _) is not null) entity.NoKey = true;

        foreach (var (property, propertyFile) in Properties(decl, file))
        {
            var name = property.Identifier.ValueText;
            if (entity.Ignored.Contains(name) || Attr(property, "NotMapped", out _) is not null) continue;

            var (typeName, nullableAnnotated, element) = Shape(property.Type);
            if (element is not null && _entities.ContainsKey(element))
                entity.Collections[name] = element;
            else if (_entities.ContainsKey(typeName))
                entity.References[name] = (typeName, property, propertyFile);
            else if (element is null && (ClrTypeMap.IsValueType(typeName) || code.EnumUnderlying(typeName) is not null || typeName is "string" or "String" or "byte[]"))
                entity.Scalars.Add(new Scalar(name, typeName, nullableAnnotated, property, propertyFile));
            else if (dialect == SqlDialect.PostgreSql && NpgsqlScalar(typeName, element) is { } npgsqlType)
                entity.Scalars.Add(new Scalar(name, npgsqlType, nullableAnnotated, property, propertyFile));
            else if (element is null && code.FindClass(typeName) is not null)
                _notes.Add(new SchemaNote($"{entity.ClrName}.{name} é do tipo {typeName}, que não é entidade do modelo: a propriedade ficou de fora.", propertyFile, Line(property)));
        }

        if (entity.Key is null && !entity.NoKey)
        {
            var keyed = entity.Scalars.Where(s => Attr(s.Declaration, "Key", out _) is not null).Select(s => s.Name).ToList();
            if (keyed.Count > 0)
            {
                entity.Key = keyed;
                entity.KeySource = Source(Kind, file, entity.Scalars.First(s => s.Name == keyed[0]).Declaration);
            }
            else if (Attr(decl, "PrimaryKey", out var pkAttr) is not null)
            {
                entity.Key = pkAttr!.ArgumentList?.Arguments.Where(a => a.NameEquals is null).Select(a => String(a.Expression)).Where(s => s is not null).Select(s => s!).ToList();
                entity.KeySource = Source(Kind, file, pkAttr);
            }
            else if (entity.Scalars.FirstOrDefault(s => s.Name.Equals("Id", StringComparison.OrdinalIgnoreCase) || s.Name.Equals(entity.ClrName + "Id", StringComparison.OrdinalIgnoreCase)) is { } conventional)
            {
                entity.Key = [conventional.Name];
                entity.KeySource = Source(Kind, conventional.File, conventional.Declaration);
            }
        }
    }

    private IEnumerable<Relation> Relations(Entity entity)
    {
        var found = new List<Relation>();

        foreach (var config in entity.Relations)
        {
            var dependent = config.Dependent ?? (config.PrincipalCollection is { } coll && entity.Collections.TryGetValue(coll, out var e) ? e : null);
            var principal = config.Principal ?? (config.DependentNavigation is { } nav && Lookup(dependent)?.References.TryGetValue(nav, out var r) == true ? r.Type : null);
            if (dependent is null || principal is null || Lookup(dependent) is not { } dep || Lookup(principal) is not { } pri) continue;
            var fk = config.ForeignKey is { Count: > 0 } explicitFk ? explicitFk : ConventionalForeignKey(dep, pri, config.DependentNavigation);
            if (fk.Count == 0) continue;
            found.Add(new Relation(dep, pri, fk, config.OnDelete, config.Source));
        }

        foreach (var (navigation, (type, declaration, file)) in entity.References)
        {
            if (Lookup(type) is not { } principal) continue;
            var fk = AttributeForeignKey(entity, navigation, declaration) ?? ConventionalForeignKey(entity, principal, navigation);
            if (fk.Count > 0) found.Add(new Relation(entity, principal, fk, null, Source(Kind, file, declaration)));
        }

        // Coleção sem a navegação de volta: a chave estrangeira fica no outro lado, por convenção.
        foreach (var (collection, element) in entity.Collections)
        {
            if (Lookup(element) is not { } dependent || dependent.References.Values.Any(r => r.Type == entity.ClrName)) continue;
            var fk = ConventionalForeignKey(dependent, entity, null);
            if (fk.Count > 0) found.Add(new Relation(dependent, entity, fk, null, entity.Source));
        }

        return found;
    }

    private Entity? Lookup(string? clrName) =>
        clrName is not null && _entities.TryGetValue(clrName, out var entity) && entity.Declaration is not null ? entity : null;

    // [ForeignKey("ClienteId")] na navegação, ou [ForeignKey("Cliente")] na propriedade da chave.
    private static List<string>? AttributeForeignKey(Entity entity, string navigation, PropertyDeclarationSyntax declaration)
    {
        if (Attr(declaration, "ForeignKey", out var onNav) is not null && String(AttributeArg(onNav!, null, 0)) is { } names)
            return names.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        var onKey = entity.Scalars.Where(s => Attr(s.Declaration, "ForeignKey", out var a) is not null && String(AttributeArg(a!, null, 0)) == navigation).Select(s => s.Name).ToList();
        return onKey.Count > 0 ? onKey : null;
    }

    // As convenções do EF Core: "<Navegação><Chave>", "<Navegação>Id", "<Principal><Chave>" e "<Principal>Id".
    private static List<string> ConventionalForeignKey(Entity dependent, Entity principal, string? navigation)
    {
        var key = principal.Key is { Count: 1 } single ? single[0] : "Id";
        var candidates = new List<string>();
        if (navigation is not null) candidates.AddRange([navigation + key, navigation + "Id"]);
        candidates.AddRange([principal.ClrName + key, principal.ClrName + "Id"]);
        var match = candidates.Select(c => dependent.Scalars.FirstOrDefault(s => s.Name.Equals(c, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(s => s is not null);
        return match is null ? [] : [match.Name];
    }

    private DbTable Table(Entity entity, List<Relation> relations)
    {
        // A relação do OnModelCreating (com OnDelete) vence a mesma relação deduzida pela convenção.
        var mine = relations.Where(r => r.Dependent == entity)
            .OrderBy(r => r.OnDelete is null ? 1 : 0)
            .DistinctBy(r => r.Principal.ClrName + ":" + string.Join(",", r.ForeignKey))
            .ToList();
        var fkProps = mine.SelectMany(r => r.ForeignKey).ToHashSet(StringComparer.Ordinal);
        var keyProps = (entity.NoKey ? null : entity.Key)?.ToHashSet(StringComparer.Ordinal) ?? [];
        var indexProps = entity.Indexes.SelectMany(i => i.Properties).Concat(ClassIndexes(entity).SelectMany(i => i.Properties)).ToHashSet(StringComparer.Ordinal);

        var columns = new List<DbColumn>();
        foreach (var scalar in entity.Scalars)
        {
            entity.Configs.TryGetValue(scalar.Name, out var config);
            var isKey = keyProps.Contains(scalar.Name);
            columns.Add(Column(entity, scalar, config, isKey, isKey || fkProps.Contains(scalar.Name) || indexProps.Contains(scalar.Name),
                isKey && keyProps.Count == 1 && !fkProps.Contains(scalar.Name)));
        }

        var tableName = entity.TableName!;
        DbPrimaryKey? primaryKey = keyProps.Count == 0 ? null
            : new DbPrimaryKey(Conventional("PK_" + tableName), entity.Key!.Select(entity.ColumnOf).ToList(), entity.KeySource ?? entity.Source);

        var foreignKeys = mine.Select(r =>
        {
            var columnsOf = r.ForeignKey.Select(entity.ColumnOf).ToList();
            var required = r.ForeignKey.All(p => columns.FirstOrDefault(c => c.Name == entity.ColumnOf(p))?.Nullable == false);
            return new DbForeignKey(
                Conventional($"FK_{tableName}_{r.Principal.TableName}_{string.Join("_", columnsOf)}"),
                columnsOf,
                DbNames.Display(r.Principal.Schema, r.Principal.TableName!, dialect),
                (r.Principal.Key ?? []).Select(r.Principal.ColumnOf).ToList(),
                r.OnDelete ?? (required ? DeleteAction.Cascade : DeleteAction.NoAction),
                r.Source);
        }).ToList();

        var indexes = entity.Indexes.Concat(ClassIndexes(entity))
            .Where(i => i.Properties.Count > 0)
            .Select(i =>
            {
                var cols = i.Properties.Select(entity.ColumnOf).ToList();
                return new DbIndex(i.Name ?? Conventional($"IX_{tableName}_{string.Join("_", cols)}"), cols, i.Include.Select(entity.ColumnOf).ToList(), i.Unique, i.Source);
            })
            .ToList();

        // A convenção do EF Core: cada FK ganha um índice "IX_<tabela>_<colunas>", a menos que as colunas
        // dela já sejam o começo da chave primária ou de outro índice (Fase 38).
        foreach (var fk in foreignKeys)
        {
            if (primaryKey is not null && Prefix(primaryKey.Columns, fk.Columns)) continue;
            if (indexes.Any(i => Prefix(i.Columns, fk.Columns))) continue;
            indexes.Add(new DbIndex(Conventional($"IX_{tableName}_{string.Join("_", fk.Columns)}"), fk.Columns, [], false, fk.Source, Convention: true));
        }

        return new DbTable(entity.Schema, tableName, columns, primaryKey, foreignKeys, indexes, [entity.Source], entity.ClrName);
    }

    private DbColumn Column(Entity entity, Scalar scalar, PropertyConfig? config, bool isKey, bool keyLike, bool identityCandidate)
    {
        var declaration = scalar.Declaration;
        string? columnAttrName = null, columnAttrType = null;
        if (Attr(declaration, "Column", out var columnAttr) is not null)
        {
            columnAttrName = String(AttributeArg(columnAttr!, null, 0));
            columnAttrType = String(AttributeArg(columnAttr!, "TypeName"));
        }
        var maxLength = config?.MaxLength
            ?? (Attr(declaration, "MaxLength", out var max) is not null ? Int(AttributeArg(max!, null, 0)) : null)
            ?? (Attr(declaration, "StringLength", out var len) is not null ? Int(AttributeArg(len!, null, 0)) : null);
        int? precision = config?.Precision, scale = config?.Scale;
        if (precision is null && Attr(declaration, "Precision", out var prec) is not null)
        {
            precision = Int(AttributeArg(prec!, null, 0));
            scale = Int(AttributeArg(prec!, null, 1));
        }
        bool? unicode = config?.Unicode ?? (Attr(declaration, "Unicode", out var uni) is not null ? Bool(AttributeArg(uni!, null, 0)) ?? true : null);

        var type = config?.Type ?? columnAttrType
            ?? ClrTypeMap.Map(dialect, new ClrColumn(scalar.Type, code.EnumUnderlying(scalar.Type), maxLength, precision, scale, unicode, keyLike));
        if (type is null)
            _notes.Add(new SchemaNote($"{entity.ClrName}.{scalar.Name}: o tipo da coluna sai da convenção do provedor, e o Rastro ainda não conhece a do {dialect}.", scalar.File, Line(declaration)));

        var required = config?.Required ?? (Attr(declaration, "Required", out _) is not null ? true : null);
        var nullable = !isKey && (required is { } r ? !r : scalar.NullableAnnotated);

        var generatedNever = config?.GeneratedNever == true
            || (Attr(declaration, "DatabaseGenerated", out var generated) is not null && AttributeArg(generated!, null, 0)?.ToString().EndsWith("None", StringComparison.Ordinal) == true);
        var identity = config?.Identity == true || (identityCandidate && !generatedNever && ClrTypeMap.IsIdentityCandidate(scalar.Type));

        var source = config?.Source ?? Source(Kind, scalar.File, declaration);
        return new DbColumn(entity.ColumnOf(scalar.Name), type, nullable, config?.Default, identity, [source]);
    }

    private static bool Prefix(IReadOnlyList<string> columns, IReadOnlyList<string> start) =>
        columns.Count >= start.Count && columns.Take(start.Count).SequenceEqual(start, StringComparer.OrdinalIgnoreCase);

    // [Index(nameof(Email), IsUnique = true, Name = "...")] na classe da entidade.
    private static IEnumerable<IndexConfig> ClassIndexes(Entity entity)
    {
        var (decl, file) = entity.Declaration!.Value;
        foreach (var attribute in decl.AttributeLists.SelectMany(l => l.Attributes).Where(a => BaseName(a.Name) is "Index" or "IndexAttribute"))
        {
            var props = attribute.ArgumentList?.Arguments.Where(a => a.NameEquals is null && a.NameColon is null).Select(a => String(a.Expression)).Where(s => s is not null).Select(s => s!).ToList() ?? [];
            yield return new IndexConfig(props, Source(Kind, file, attribute))
            {
                Unique = Bool(AttributeArg(attribute, "IsUnique")) ?? false,
                Name = String(AttributeArg(attribute, "Name"))
            };
        }
    }

    // As propriedades públicas da classe e das bases que estão no repositório (a base primeiro).
    private IEnumerable<(PropertyDeclarationSyntax Property, string File)> Properties(ClassDeclarationSyntax decl, string file, int depth = 0)
    {
        if (depth < 5 && decl.BaseList?.Types.FirstOrDefault()?.Type is { } baseType && code.FindClass(BaseName(baseType)) is { } parent)
            foreach (var inherited in Properties(parent.Class, parent.File, depth + 1))
                yield return inherited;

        foreach (var property in decl.Members.OfType<PropertyDeclarationSyntax>())
        {
            if (!property.Modifiers.Any(SyntaxKind.PublicKeyword) || property.Modifiers.Any(SyntaxKind.StaticKeyword)) continue;
            var hasGetter = property.ExpressionBody is not null || property.AccessorList?.Accessors.Any(a => a.IsKind(SyntaxKind.GetAccessorDeclaration)) == true;
            var computed = property.ExpressionBody is not null;
            if (hasGetter && !computed) yield return (property, file);
        }
    }

    // ("int", true, null) para "int?"; ("ICollection", false, "ItemPedido") para "ICollection<ItemPedido>".
    private static (string Type, bool Nullable, string? Element) Shape(TypeSyntax type)
    {
        var nullable = false;
        if (type is NullableTypeSyntax n) { nullable = true; type = n.ElementType; }
        if (type is ArrayTypeSyntax array) return (array.ElementType.ToString() is "byte" or "Byte" or "System.Byte" ? "byte[]" : array.ToString(), nullable, null);
        if (type is GenericNameSyntax g && CollectionTypes.Contains(g.Identifier.ValueText) && g.TypeArgumentList.Arguments.Count == 1)
            return (g.Identifier.ValueText, nullable, BaseName(g.TypeArgumentList.Arguments[0]));
        if (type is QualifiedNameSyntax { Right: GenericNameSyntax qg } && CollectionTypes.Contains(qg.Identifier.ValueText) && qg.TypeArgumentList.Arguments.Count == 1)
            return (qg.Identifier.ValueText, nullable, BaseName(qg.TypeArgumentList.Arguments[0]));
        if (type is PredefinedTypeSyntax p) return (p.Keyword.ValueText, nullable, null);
        return (BaseName(type), nullable, null);
    }

    // O que o Npgsql mapeia sozinho e o SQL Server não: vetor e lista de tipo simples (text[], integer[]) e
    // JsonDocument ou JsonElement (jsonb).
    private static string? NpgsqlScalar(string typeName, string? element)
    {
        static bool Simple(string t) => ClrTypeMap.IsValueType(t) || t is "string" or "String";
        if (typeName is "JsonDocument" or "JsonElement") return typeName;
        if (element is null && typeName.EndsWith("[]", StringComparison.Ordinal) && Simple(typeName[..^2])) return typeName;
        if (element is not null && typeName is "List" or "IList" && Simple(element)) return element + "[]";
        return null;
    }

    private static string? Attr(MemberDeclarationSyntax member, string name, out AttributeSyntax? attribute) =>
        Attribute(member, name, out attribute);

    private string Name(string name) => _snakeCase ? EfNaming.SnakeCase(name) : name;

    // O nome que o EF dá à chave, à FK e ao índice: com o UseSnakeCaseNamingConvention, em minúsculas
    // (pk_pedidos, fk_pedidos_clientes_cliente_id, ix_pedidos_cliente_id).
    private string Conventional(string name) => _snakeCase ? name.ToLowerInvariant() : name;

    private sealed class Entity(string clrName, SchemaSource source)
    {
        public string ClrName { get; } = clrName;
        public SchemaSource Source { get; set; } = source;
        public string? SetName { get; set; }
        public string? Table { get; set; }
        public string? Schema { get; set; }
        public string? TableName { get; set; }
        public List<string>? Key { get; set; }
        public SchemaSource? KeySource { get; set; }
        public bool NoKey { get; set; }
        public HashSet<string> Ignored { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, PropertyConfig> Configs { get; } = new(StringComparer.Ordinal);
        public List<IndexConfig> Indexes { get; } = [];
        public List<RelationConfig> Relations { get; } = [];
        public List<Scalar> Scalars { get; } = [];
        public Dictionary<string, (string Type, PropertyDeclarationSyntax Declaration, string File)> References { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Collections { get; } = new(StringComparer.Ordinal);
        public (ClassDeclarationSyntax Class, string File)? Declaration { get; private set; }
        public Func<string, string> NameRule { get; set; } = n => n;

        public PropertyConfig Property(string name, SchemaSource source)
        {
            if (!Configs.TryGetValue(name, out var config))
            {
                config = new PropertyConfig();
                Configs[name] = config;
            }
            config.Source = source;
            return config;
        }

        public string ColumnOf(string property)
        {
            if (Configs.TryGetValue(property, out var config) && config.Column is { } explicitName) return explicitName;
            var scalar = Scalars.FirstOrDefault(s => s.Name == property);
            if (scalar is not null && Attribute(scalar.Declaration, "Column", out var attr) is not null && String(AttributeArg(attr!, null, 0)) is { } named)
                return named;
            return NameRule(property);
        }

        // A classe da entidade precisa estar no repositório; sem ela, a tabela fica de fora com uma nota.
        public bool Ready(CodeIndex code, List<SchemaNote> notes)
        {
            Declaration = code.FindClass(ClrName);
            if (Declaration is null)
                notes.Add(new SchemaNote($"A entidade {ClrName} não foi encontrada no repositório: a tabela dela ficou de fora.", Source.File, Source.Line));
            return Declaration is not null;
        }
    }

    private sealed class PropertyConfig
    {
        public SchemaSource? Source { get; set; }
        public string? Column { get; set; }
        public string? Type { get; set; }
        public int? MaxLength { get; set; }
        public int? Precision { get; set; }
        public int? Scale { get; set; }
        public bool? Required { get; set; }
        public bool? Unicode { get; set; }
        public string? Default { get; set; }
        public bool GeneratedNever { get; set; }
        public bool Identity { get; set; }
    }

    private sealed class IndexConfig(List<string> properties, SchemaSource source)
    {
        public List<string> Properties { get; } = properties;
        public SchemaSource Source { get; } = source;
        public bool Unique { get; set; }
        public string? Name { get; set; }
        public List<string> Include { get; set; } = [];
    }

    private sealed class RelationConfig(SchemaSource source)
    {
        public SchemaSource Source { get; } = source;
        public string? Dependent { get; set; }
        public string? Principal { get; set; }
        public string? DependentNavigation { get; set; }
        public string? PrincipalCollection { get; set; }
        public List<string>? ForeignKey { get; set; }
        public DeleteAction? OnDelete { get; set; }
    }

    private sealed record Scalar(string Name, string Type, bool NullableAnnotated, PropertyDeclarationSyntax Declaration, string File);

    private sealed record Relation(Entity Dependent, Entity Principal, List<string> ForeignKey, DeleteAction? OnDelete, SchemaSource Source);
}

// Convenção de nomes do EFCore.NamingConventions, usada quando o código chama UseSnakeCaseNamingConvention.
public static class EfNaming
{
    // "ItensPedido" vira "itens_pedido" e "CriadoEm" vira "criado_em", como o EFCore.NamingConventions.
    public static string SnakeCase(string name)
    {
        var text = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0)
            {
                var previous = name[i - 1];
                var nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                if (char.IsLower(previous) || char.IsDigit(previous) || (char.IsUpper(previous) && nextIsLower))
                    text.Append('_');
            }
            text.Append(char.ToLowerInvariant(c));
        }
        return text.ToString();
    }
}
