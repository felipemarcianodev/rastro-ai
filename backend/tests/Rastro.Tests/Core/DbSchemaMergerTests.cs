using Rastro.Core.Database;

namespace Rastro.Tests.Core;

// Cenários CORE-DB-NNN: fusão das fontes do esquema (Fase 37). A mesma tabela em fontes diferentes vira
// uma só; quando as fontes discordam, a divergência aparece com cada versão e o Rastro não escolhe.
public class DbSchemaMergerTests
{
    private static SchemaSource Src(SchemaSourceKind kind, int line = 1) => new(kind, kind + ".txt", line);

    private static DbColumn Col(string name, string? type, bool? nullable, SchemaSourceKind kind) =>
        new(name, type, nullable, null, false, [Src(kind)]);

    private static DbTable Tab(string? schema, string name, SchemaSourceKind kind, DbPrimaryKey? pk = null, IReadOnlyList<DbForeignKey>? fks = null, params DbColumn[] columns) =>
        new(schema, name, columns, pk, fks ?? [], [], [Src(kind)]);

    private static SchemaFragment Frag(params DbTable[] tables) => new(tables, [], [], []);

    [Fact] // CORE-DB-001
    public void Merge_MesmaTabelaComSchemaEColchetes_UmaTabelaSoComTipoNormalizado()
    {
        var schema = DbSchemaMerger.Merge(SqlDialect.SqlServer,
        [
            Frag(Tab(null, "pedidos", SchemaSourceKind.EfMigration, columns: Col("status", "nvarchar(30)", false, SchemaSourceKind.EfMigration))),
            Frag(Tab("dbo", "[pedidos]", SchemaSourceKind.SqlScript, columns: Col("[status]", "NVARCHAR (30)", false, SchemaSourceKind.SqlScript)))
        ]);

        var table = Assert.Single(schema.Tables);
        Assert.Null(table.Schema);
        Assert.Equal("pedidos", table.Name);
        var column = Assert.Single(table.Columns);
        Assert.Equal("nvarchar(30)", column.Type);
        Assert.Equal(2, column.Sources.Count);
        Assert.Empty(schema.Divergences);
    }

    [Fact] // CORE-DB-002
    public void Merge_NuloDiferenteEntreFontes_ColunaSemValorEDivergenciaComAsDuas()
    {
        var schema = DbSchemaMerger.Merge(SqlDialect.SqlServer,
        [
            Frag(Tab(null, "clientes", SchemaSourceKind.EfModel, columns: Col("cpf", "char(11)", true, SchemaSourceKind.EfModel))),
            Frag(Tab(null, "clientes", SchemaSourceKind.EfMigration, columns: Col("cpf", "char(11)", false, SchemaSourceKind.EfMigration)))
        ]);

        var column = Assert.Single(Assert.Single(schema.Tables).Columns);
        Assert.Null(column.Nullable);
        Assert.Equal("char(11)", column.Type);
        var divergence = Assert.Single(schema.Divergences);
        Assert.Equal(SchemaDivergenceKind.ColumnNullability, divergence.Kind);
        Assert.Equal(["aceita nulo", "não aceita nulo"], divergence.Values.Select(v => v.Value));
        Assert.Equal([SchemaSourceKind.EfModel, SchemaSourceKind.EfMigration], divergence.Values.Select(v => v.Source.Kind));
    }

    [Fact] // CORE-DB-003
    public void Merge_ColunaSoNoModelo_DivergenciaDeExistencia()
    {
        var schema = DbSchemaMerger.Merge(SqlDialect.SqlServer,
        [
            Frag(Tab(null, "pedidos", SchemaSourceKind.EfModel, columns: [Col("id", "int", false, SchemaSourceKind.EfModel), Col("cupom", "nvarchar(20)", true, SchemaSourceKind.EfModel)])),
            Frag(Tab(null, "pedidos", SchemaSourceKind.EfMigration, columns: Col("id", "int", false, SchemaSourceKind.EfMigration)))
        ]);

        var divergence = Assert.Single(schema.Divergences);
        Assert.Equal((SchemaDivergenceKind.ColumnMissing, "cupom"), (divergence.Kind, divergence.Column));
        Assert.Equal([DbSchemaMerger.Present, DbSchemaMerger.Absent], divergence.Values.Select(v => v.Value));
    }

    [Fact] // CORE-DB-004
    public void Merge_ColunaSoNoSql_NaoEDivergencia()
    {
        var schema = DbSchemaMerger.Merge(SqlDialect.SqlServer,
        [
            Frag(Tab(null, "pedidos", SchemaSourceKind.EfModel, columns: Col("id", "int", false, SchemaSourceKind.EfModel))),
            Frag(Tab(null, "pedidos", SchemaSourceKind.SqlScript, columns: Col("observacao", "nvarchar(max)", true, SchemaSourceKind.SqlScript)))
        ]);

        Assert.Equal(["id", "observacao"], Assert.Single(schema.Tables).Columns.Select(c => c.Name));
        Assert.Empty(schema.Divergences);
    }

    [Fact] // CORE-DB-005
    public void Merge_ChavePrimariaDiferente_FicaADaPrimeiraFonteEDivergenciaMostraAsDuas()
    {
        var schema = DbSchemaMerger.Merge(SqlDialect.SqlServer,
        [
            Frag(Tab(null, "itens", SchemaSourceKind.EfModel, new DbPrimaryKey("PK_itens", ["pedido_id", "produto_id"], Src(SchemaSourceKind.EfModel)))),
            Frag(Tab(null, "itens", SchemaSourceKind.EfMigration, new DbPrimaryKey("PK_itens", ["id"], Src(SchemaSourceKind.EfMigration))))
        ]);

        Assert.Equal(["pedido_id", "produto_id"], Assert.Single(schema.Tables).PrimaryKey!.Columns);
        var divergence = Assert.Single(schema.Divergences);
        Assert.Equal(SchemaDivergenceKind.PrimaryKey, divergence.Kind);
        Assert.Equal(["pedido_id, produto_id", "id"], divergence.Values.Select(v => v.Value));
    }

    [Fact] // CORE-DB-006
    public void Merge_MesmaChaveEstrangeiraEmDuasFontes_UmaSoComODestinoSemSchemaPadrao()
    {
        DbForeignKey Fk(SchemaSourceKind kind, string principal) => new(null, ["cliente_id"], principal, ["id"], DeleteAction.Cascade, Src(kind));
        var schema = DbSchemaMerger.Merge(SqlDialect.SqlServer,
        [
            Frag(Tab(null, "pedidos", SchemaSourceKind.EfModel, fks: [Fk(SchemaSourceKind.EfModel, "clientes")])),
            Frag(Tab(null, "pedidos", SchemaSourceKind.SqlScript, fks: [Fk(SchemaSourceKind.SqlScript, "[dbo].[clientes]")]))
        ]);

        var fk = Assert.Single(Assert.Single(schema.Tables).ForeignKeys);
        Assert.Equal("clientes", fk.PrincipalTable);
        Assert.Equal(SchemaSourceKind.EfModel, fk.Source.Kind);
    }

    [Fact] // CORE-DB-007
    public void Merge_PostgreSql_SchemaPublicEOPadrao()
    {
        var schema = DbSchemaMerger.Merge(SqlDialect.PostgreSql,
        [
            Frag(Tab("public", "pedidos", SchemaSourceKind.SqlScript)),
            Frag(Tab("vendas", "pedidos", SchemaSourceKind.SqlScript))
        ]);

        Assert.Equal([null, "vendas"], schema.Tables.Select(t => t.Schema));
        Assert.Equal("vendas.pedidos", DbNames.Display("vendas", "pedidos", SqlDialect.PostgreSql));
    }
}
