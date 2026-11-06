using Rastro.Core.Database;
using Rastro.Extractor.Database;

namespace Rastro.Tests.Extractor;

// Cenários EXT-DB-NNN: esquema do banco lido do código (Fase 37) na loja fictícia LojaBanco: DbContext
// com UseSnakeCaseNamingConvention, configuração, migrations (uma escrita à mão, sem o índice da FK) e
// .sql do projeto de banco (produtos, tabela sem PK, view, procedure e um arquivo quebrado).
public class DbSchemaExtractorTests
{
    private static readonly Lazy<DbSchema> Loja = new(() => DbSchemaExtractor.Extract(LojaBanco.Files()));

    private static DbTable Table(string name) => Assert.Single(Loja.Value.Tables, t => t.Name == name);

    private static DbColumn Column(string table, string column) => Assert.Single(Table(table).Columns, c => c.Name == column);

    [Fact] // EXT-DB-001
    public void Extract_LojaFicticia_TabelasDasTresFontesNoDialetoSqlServer()
    {
        Assert.Equal(SqlDialect.SqlServer, Loja.Value.Dialect);
        Assert.Equal(["clientes", "itens_pedido", "log_eventos", "pagamentos", "pedidos", "produtos"], Loja.Value.Tables.Select(t => t.Name));
        Assert.Equal([SchemaSourceKind.EfModel, SchemaSourceKind.EfMigration], Table("pedidos").Sources.Select(s => s.Kind));
        Assert.Equal(SchemaSourceKind.SqlScript, Assert.Single(Table("produtos").Sources).Kind);
        Assert.Equal("Loja.Api/Dados/LojaDbContext.cs", Table("pedidos").Sources[0].File);
    }

    [Fact] // EXT-DB-002
    public void Extract_ModeloComSnakeCaseEConvencoes_ColunasTiposENulosComoOEfGeraria()
    {
        Assert.Equal(["id", "nome", "email", "cpf", "criado_em"], Table("clientes").Columns.Select(c => c.Name));
        Assert.Equal("char(11)", Column("clientes", "cpf").Type);
        Assert.True(Column("clientes", "cpf").Nullable);
        Assert.Equal("nvarchar(120)", Column("clientes", "nome").Type);
        Assert.False(Column("clientes", "nome").Nullable);
        Assert.True(Column("clientes", "id").Identity);
        Assert.Equal("tinyint", Column("pagamentos", "metodo").Type);
        Assert.Equal("decimal(10,2)", Column("pagamentos", "valor").Type);
        Assert.True(Column("pagamentos", "pago_em").Nullable);
        Assert.DoesNotContain(Table("pedidos").Columns, c => c.Name == "pago");
        // Sem divergência fora do status: modelo e migration concordam no resto.
        Assert.Single(Loja.Value.Divergences);
    }

    [Fact] // EXT-DB-003
    public void Extract_TamanhoDoStatusDiferenteEntreModeloEMigration_MostraAsDuasSemEscolher()
    {
        var status = Column("pedidos", "status");
        Assert.Null(status.Type);
        Assert.False(status.Nullable);

        var divergence = Assert.Single(Loja.Value.Divergences);
        Assert.Equal(SchemaDivergenceKind.ColumnType, divergence.Kind);
        Assert.Equal(("pedidos", "status"), (divergence.Table, divergence.Column));
        Assert.Collection(divergence.Values,
            v =>
            {
                Assert.Equal("nvarchar(20)", v.Value);
                Assert.Equal(new SchemaSource(SchemaSourceKind.EfModel, "Loja.Api/Dados/Configuracoes/PedidoConfiguration.cs", 10), v.Source);
            },
            v =>
            {
                Assert.Equal("nvarchar(30)", v.Value);
                Assert.Equal(SchemaSourceKind.EfMigration, v.Source.Kind);
                Assert.Equal("Loja.Api/Migrations/20261012_Pedidos.cs", v.Source.File);
            });
    }

    [Fact] // EXT-DB-004
    public void Extract_ChavesCompostaEstrangeirasEIndices_DoModeloDaMigrationEDoSql()
    {
        Assert.Equal(["pedido_id", "produto_id"], Table("itens_pedido").PrimaryKey!.Columns);
        Assert.Equal(["pedidos", "produtos"], Table("itens_pedido").ForeignKeys.Select(f => f.PrincipalTable).Order());
        var produto = Assert.Single(Table("itens_pedido").ForeignKeys, f => f.PrincipalTable == "produtos");
        Assert.Equal(SchemaSourceKind.SqlScript, produto.Source.Kind);

        var cliente = Assert.Single(Table("pedidos").ForeignKeys);
        Assert.Equal(["cliente_id"], cliente.Columns);
        Assert.Equal(("clientes", DeleteAction.Restrict), (cliente.PrincipalTable, cliente.OnDelete));

        Assert.Equal(DeleteAction.Cascade, Assert.Single(Table("pagamentos").ForeignKeys).OnDelete);
        // A migration escrita à mão deixou a FK de pagamentos sem índice.
        Assert.Empty(Table("pagamentos").Indexes);
        Assert.Equal("IX_pedidos_cliente_id", Assert.Single(Table("pedidos").Indexes).Name);
        var email = Assert.Single(Table("clientes").Indexes);
        Assert.True(email.Unique);
        Assert.Equal(["email"], email.Columns);
    }

    [Fact] // EXT-DB-005
    public void Extract_ProjetoDeBanco_TabelaSemChaveViewEProcedure()
    {
        Assert.Null(Table("log_eventos").PrimaryKey);
        Assert.Equal("((0))", Column("produtos", "estoque").Default);
        Assert.True(Column("produtos", "id").Identity);
        Assert.Equal("nvarchar(160)", Column("produtos", "nome").Type);

        var view = Assert.Single(Loja.Value.Views, v => v.Name == "vw_pedidos_por_cliente");
        Assert.Equal(["cliente_id", "nome", "pedidos"], view.Columns);
        Assert.Equal(["clientes", "pedidos"], view.Reads);

        var fromMigration = Assert.Single(Loja.Value.Views, v => v.Name == "vw_pagamentos_pendentes");
        Assert.Equal(SchemaSourceKind.EfMigration, fromMigration.Source.Kind);
        Assert.Equal(["pagamentos"], fromMigration.Reads);

        var procedure = Assert.Single(Loja.Value.Procedures);
        Assert.Equal("sp_baixar_estoque", procedure.Name);
        Assert.Equal([new DbParameter("@pedido_id", "int", false), new DbParameter("@linhas", "int", true)], procedure.Parameters);
        Assert.Equal(["produtos", "log_eventos"], procedure.Writes);
        Assert.Contains("itens_pedido", procedure.Reads);
    }

    [Fact] // EXT-DB-006
    public void Extract_ArquivoSqlQuebradoESnapshot_ViraNotaESnapshotNaoEFonte()
    {
        var note = Assert.Single(Loja.Value.Notes);
        Assert.Equal("banco/quebrado.sql", note.File);
        Assert.DoesNotContain(Loja.Value.Tables.SelectMany(t => t.Columns), c => c.Type == "nvarchar(99)");
        Assert.DoesNotContain(Loja.Value.Tables.SelectMany(t => t.Sources), s => s.File.EndsWith("ModelSnapshot.cs", StringComparison.Ordinal));
    }

    [Fact] // EXT-DB-007
    public void Extract_MesmoCodigo_MesmoEsquema()
    {
        var again = DbSchemaExtractor.Extract(LojaBanco.Files().AsEnumerable().Reverse().ToList());

        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(Loja.Value), System.Text.Json.JsonSerializer.Serialize(again));
    }

    [Fact] // EXT-DB-008
    public void Extract_RepositorioSemBanco_EsquemaVazioSemNota()
    {
        var schema = DbSchemaExtractor.Extract([new SchemaFile("src/Servico.cs", "public class Servico { public int Somar(int a, int b) => a + b; }")]);

        Assert.Empty(schema.Tables);
        Assert.Empty(schema.Notes);
    }

    [Fact] // EXT-DB-014
    public void Extract_FkCriadaSemIndiceNaMigrationENoSql_AchadoSemFalsoPositivoDaConvencao()
    {
        Assert.Collection(Loja.Value.Findings,
            f =>
            {
                Assert.Equal((SchemaFindingKind.ForeignKeyWithoutIndex, "itens_pedido"), (f.Kind, f.Table));
                Assert.Equal(["produto_id"], f.Columns);
                Assert.Equal(("banco/produtos.sql", SchemaSourceKind.SqlScript), (f.Source.File, f.Source.Kind));
            },
            f =>
            {
                Assert.Equal("pagamentos", f.Table);
                Assert.Equal(["pedido_id"], f.Columns);
                Assert.Equal(("Loja.Api/Migrations/20261020_Pagamentos.cs", SchemaSourceKind.EfMigration), (f.Source.File, f.Source.Kind));
            });
        // A convenção do modelo não esconde o que a migration não criou, e não aparece como índice.
        Assert.DoesNotContain(Loja.Value.Tables.SelectMany(t => t.Indexes), i => i.Convention);
        // pedidos.cliente_id tem o índice da migration; itens_pedido.pedido_id é o começo da PK.
        Assert.DoesNotContain(Loja.Value.Findings, f => f.Table == "pedidos" || f.Columns.Contains("pedido_id") && f.Table == "itens_pedido");
    }

    [Fact] // EXT-DB-015
    public void Extract_ColunasComNomeDeDadoPessoal_MarcadasSoPeloNome()
    {
        Assert.True(Column("clientes", "email").Personal);
        Assert.True(Column("clientes", "cpf").Personal);
        Assert.False(Column("clientes", "nome").Personal);
        Assert.False(Column("produtos", "nome").Personal);
        Assert.False(Column("pedidos", "status").Personal);
    }

    [Fact] // EXT-DB-009
    public void SnakeCase_NomesDoCSharp_ComoOEfCoreNamingConventions()
    {
        Assert.Equal("itens_pedido", EfNaming.SnakeCase("ItensPedido"));
        Assert.Equal("criado_em", EfNaming.SnakeCase("CriadoEm"));
        Assert.Equal("url_base", EfNaming.SnakeCase("URLBase"));
        Assert.Equal("id", EfNaming.SnakeCase("Id"));
    }
}

// A loja fictícia em arquivos, com os caminhos relativos à pasta da fixture.
internal static class LojaBanco
{
    public static string Root => Path.Combine(FixtureWorkspace.FixturesRoot, "LojaBanco");

    public static List<SchemaFile> Files() =>
        Directory.GetFiles(Root, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".sql", StringComparison.Ordinal))
            .Select(f => new SchemaFile(Path.GetRelativePath(Root, f).Replace('\\', '/'), File.ReadAllText(f)))
            .OrderBy(f => f.RelativePath, StringComparer.Ordinal)
            .ToList();
}
