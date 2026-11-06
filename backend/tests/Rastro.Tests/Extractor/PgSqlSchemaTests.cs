using Rastro.Core.Database;
using Rastro.Extractor.Database;

namespace Rastro.Tests.Extractor;

// Cenários EXT-PG-NNN: esquema do PostgreSQL lido do código (Fase 42) na loja fictícia LojaBancoPg, a
// mesma loja da LojaBanco no provedor Npgsql: DbContext com UseNpgsql e UseSnakeCaseNamingConvention,
// migrations como o Npgsql gera (tipos do catálogo e a identidade pela anotação Npgsql:) e .sql do
// PostgreSQL (serial, jsonb, view materializada, função em PL/pgSQL, procedure, trecho do pg_dump com
// COPY e um arquivo quebrado).
public class PgSqlSchemaTests
{
    private static readonly Lazy<DbSchema> Loja = new(() => DbSchemaExtractor.Extract(LojaBancoPg.Files()));

    private static DbTable Table(string name) => Assert.Single(Loja.Value.Tables, t => t.Name == name);

    private static DbColumn Column(string table, string column) => Assert.Single(Table(table).Columns, c => c.Name == column);

    private static DbSchema FromSql(params (string File, string Text)[] files) =>
        DbSchemaExtractor.Extract(files.Select(f => new SchemaFile(f.File, f.Text)).ToList());

    [Fact] // EXT-PG-001
    public void Extract_LojaComUseNpgsql_DialetoPostgreSqlComAsTresFontes()
    {
        Assert.Equal(SqlDialect.PostgreSql, Loja.Value.Dialect);
        Assert.Equal(["clientes", "cupons", "itens_pedido", "log_eventos", "pagamentos", "pedidos", "produtos"], Loja.Value.Tables.Select(t => t.Name));
        Assert.Equal([SchemaSourceKind.EfModel, SchemaSourceKind.EfMigration], Table("pedidos").Sources.Select(s => s.Kind));
        Assert.Equal(SchemaSourceKind.SqlScript, Assert.Single(Table("produtos").Sources).Kind);
        // O schema public é o padrão do PostgreSQL: some do nome, como o dbo no SQL Server.
        Assert.Null(Table("produtos").Schema);
    }

    [Fact] // EXT-PG-002
    public void Extract_ModeloDoNpgsql_TiposComoAMigrationEscreve()
    {
        Assert.Equal("character varying(120)", Column("clientes", "nome").Type);
        Assert.Equal("uuid", Column("clientes", "codigo").Type);
        Assert.Equal("jsonb", Column("clientes", "preferencias").Type);
        Assert.True(Column("clientes", "preferencias").Nullable);
        Assert.Equal("timestamp with time zone", Column("clientes", "criado_em").Type);
        Assert.Equal("char(11)", Column("clientes", "cpf").Type);
        Assert.Equal("text[]", Column("pedidos", "etiquetas").Type);
        Assert.Equal("numeric(10,2)", Column("pedidos", "total").Type);
        Assert.Equal("smallint", Column("pagamentos", "metodo").Type);
        Assert.Equal("bigint", Column("pagamentos", "id").Type);
        // Só o status diverge (20 no modelo, 30 na migration): o resto do modelo bate com o que o Npgsql gera.
        var divergence = Assert.Single(Loja.Value.Divergences);
        Assert.Equal(("pedidos", "status"), (divergence.Table, divergence.Column));
        Assert.Equal(["character varying(20)", "character varying(30)"], divergence.Values.Select(v => v.Value));
    }

    [Fact] // EXT-PG-003
    public void Extract_IdentidadeDoNpgsql_PelaConvencaoPelaAnotacaoEPeloUseIdentityAlwaysColumn()
    {
        Assert.True(Column("clientes", "id").Identity);
        Assert.True(Column("pagamentos", "id").Identity);
        Assert.False(Column("pedidos", "cliente_id").Identity);
        Assert.True(Column("produtos", "id").Identity);
        Assert.Equal("serial", Column("produtos", "id").Type);
    }

    [Fact] // EXT-PG-004
    public void Extract_SnakeCase_ChaveFkEIndiceComONomeQueOEfDa()
    {
        Assert.Equal("pk_pedidos", Table("pedidos").PrimaryKey!.Name);
        Assert.Equal("fk_pedidos_clientes_cliente_id", Assert.Single(Table("pedidos").ForeignKeys).Name);
        Assert.Equal(DeleteAction.Restrict, Table("pedidos").ForeignKeys[0].OnDelete);
        Assert.Equal("ix_pedidos_cliente_id", Assert.Single(Table("pedidos").Indexes).Name);
        Assert.Equal("ux_clientes_email", Assert.Single(Table("clientes").Indexes).Name);
    }

    [Fact] // EXT-PG-005
    public void Extract_CreateTableDoPostgreSql_SerialPadraoChaveNaColunaEIndiceUsingBtree()
    {
        var produtos = Table("produtos");
        Assert.Equal(["id", "nome", "preco", "estoque", "atributos", "codigo", "criado_em"], produtos.Columns.Select(c => c.Name));
        Assert.Equal(["id"], produtos.PrimaryKey!.Columns);
        // A chave sem nome escrito ganha o nome que o PostgreSQL dá.
        Assert.Equal("produtos_pkey", produtos.PrimaryKey.Name);
        Assert.False(Column("produtos", "id").Nullable);
        Assert.Equal("varchar(160)", Column("produtos", "nome").Type);
        Assert.Equal("0", Column("produtos", "estoque").Default);
        Assert.Equal("gen_random_uuid()", Column("produtos", "codigo").Default);
        Assert.True(Column("produtos", "atributos").Nullable);
        var index = Assert.Single(produtos.Indexes);
        Assert.Equal(("ix_produtos_nome", "nome"), (index.Name, index.Columns.Single()));
        Assert.Equal(new SchemaSource(SchemaSourceKind.SqlScript, "banco/produtos.sql", 5), produtos.Sources[0]);
        // SET e CREATE EXTENSION do mesmo arquivo ficam de fora, sem nota.
        Assert.DoesNotContain(Loja.Value.Notes, n => n.File == "banco/produtos.sql");
    }

    [Fact] // EXT-PG-006
    public void Extract_FkPorAlterTableSemIndice_ViraAchadoComoNoSqlServer()
    {
        var fk = Assert.Single(Table("itens_pedido").ForeignKeys, f => f.PrincipalTable == "produtos");
        Assert.Equal(("fk_itens_pedido_produtos_produto_id", "produto_id"), (fk.Name, fk.Columns.Single()));
        Assert.Equal([("itens_pedido", "produto_id"), ("pagamentos", "pedido_id")],
            Loja.Value.Findings.Select(f => (f.Table, f.Columns.Single())));
    }

    [Fact] // EXT-PG-007
    public void Extract_ViewsNormalMaterializadaEDaMigration_ColunasETabelasLidas()
    {
        Assert.Equal(["mv_vendas_por_dia", "vw_pagamentos_pendentes", "vw_pedidos_por_cliente"], Loja.Value.Views.Select(v => v.Name));
        var porCliente = Loja.Value.Views.Single(v => v.Name == "vw_pedidos_por_cliente");
        Assert.Equal(["cliente_id", "nome", "pedidos"], porCliente.Columns);
        Assert.Equal(["clientes", "pedidos"], porCliente.Reads);
        var vendas = Loja.Value.Views.Single(v => v.Name == "mv_vendas_por_dia");
        Assert.Equal(["dia", "total"], vendas.Columns);
        Assert.Equal(["pedidos"], vendas.Reads);
        Assert.Equal(SchemaSourceKind.EfMigration, Loja.Value.Views.Single(v => v.Name == "vw_pagamentos_pendentes").Source.Kind);
    }

    [Fact] // EXT-PG-008
    public void Extract_FuncaoEProcedure_ParametrosETabelasDoCorpoSemSepararNoPontoEVirgulaDoCifrao()
    {
        var baixar = Assert.Single(Loja.Value.Procedures, p => p.Name == "baixar_estoque");
        Assert.Equal([("p_pedido_id", "integer", false), ("linhas", "integer", true)], baixar.Parameters.Select(p => (p.Name, p.Type, p.Output)));
        Assert.Equal(["itens_pedido"], baixar.Reads);
        Assert.Equal(["produtos", "log_eventos"], baixar.Writes);
        Assert.Equal(new SchemaSource(SchemaSourceKind.SqlScript, "banco/fn_baixar_estoque.sql", 2), baixar.Source);

        var arquivar = Assert.Single(Loja.Value.Procedures, p => p.Name == "arquivar_pedidos");
        Assert.Equal([("p_antes", "timestamptz", false)], arquivar.Parameters.Select(p => (p.Name, p.Type, p.Output)));
        Assert.Equal(["pedidos"], arquivar.Writes);
    }

    [Fact] // EXT-PG-009
    public void Extract_TrechoDoPgDump_IdentidadeAPartePkPorAlterTableECargaDoCopyIgnorada()
    {
        var cupons = Table("cupons");
        Assert.Equal(["id", "codigo", "desconto"], cupons.Columns.Select(c => c.Name));
        Assert.True(Column("cupons", "id").Identity);
        Assert.Equal(("cupons_pkey", "id"), (cupons.PrimaryKey!.Name, cupons.PrimaryKey.Columns.Single()));
        var index = Assert.Single(cupons.Indexes);
        Assert.True(index.Unique);
        // A linha D'AVILA10 do COPY tem um apóstrofo: se fosse lida como SQL, o resto do arquivo se perdia.
        Assert.DoesNotContain(Loja.Value.Notes, n => n.File == "banco/cupons_dump.sql");
    }

    [Fact] // EXT-PG-010
    public void Extract_ArquivoQuebrado_ViraNotaComALinhaENaoDerrubaOResto()
    {
        var note = Assert.Single(Loja.Value.Notes);
        Assert.Equal(("banco/quebrado.sql", 2), (note.File, note.Line));
        Assert.Contains("O leitor de SQL do PostgreSQL não entendeu uma instrução", note.Message);
    }

    [Fact] // EXT-PG-011
    public void Extract_SinonimosDoMesmoTipo_NaoDivergemETamanhoDiferenteDiverge()
    {
        var schema = FromSql(
            ("a.sql", "CREATE TABLE t (id int PRIMARY KEY, nome varchar(20), criado timestamptz, ativo bool, n serial, valor decimal(5,2));"),
            ("b.sql", "CREATE TABLE t (id integer PRIMARY KEY, nome character varying(30), criado timestamp with time zone, ativo boolean, n integer, valor numeric(5,2));"));

        Assert.Equal(SqlDialect.PostgreSql, schema.Dialect);
        var divergence = Assert.Single(schema.Divergences);
        Assert.Equal(("t", "nome"), (divergence.Table, divergence.Column));
        Assert.Equal("integer", DbNames.NormalizeType("int4", SqlDialect.PostgreSql));
        Assert.Equal("charactervarying(10)", DbNames.NormalizeType("VARCHAR (10)", SqlDialect.PostgreSql));
        // No SQL Server, os nomes continuam como estão.
        Assert.Equal("int", DbNames.NormalizeType("int", SqlDialect.SqlServer));
    }

    [Fact] // EXT-PG-012
    public void DialectOf_SemProvedorNoCodigo_PelasMigrationsOuPelasMarcasDosSql()
    {
        Assert.Equal(SqlDialect.PostgreSql, DbSchemaExtractor.DialectOf([new SchemaFile("db/a.sql", "CREATE TABLE a (id serial PRIMARY KEY, dados jsonb);")]));
        Assert.Equal(SqlDialect.SqlServer, DbSchemaExtractor.DialectOf([new SchemaFile("db/a.sql", "CREATE TABLE [dbo].[a] ([id] INT IDENTITY(1,1), [nome] NVARCHAR(10))\nGO")]));
        Assert.Equal(SqlDialect.PostgreSql, DbSchemaExtractor.DialectOf(
            [new SchemaFile("M/1_Inicio.cs", "class M { void Up(MigrationBuilder b) { b.CreateTable(name: \"a\", columns: t => new { id = t.Column<int>(type: \"integer\").Annotation(\"Npgsql:ValueGenerationStrategy\", 1) }); } }")]));
        // Sem marca nenhuma, o SQL Server, o primeiro suportado.
        Assert.Equal(SqlDialect.SqlServer, DbSchemaExtractor.DialectOf([new SchemaFile("db/a.sql", "CREATE TABLE a (id int);")]));
        Assert.Equal(SqlDialect.SqlServer, DbSchemaExtractor.Extract(LojaBanco.Files()).Dialect);
    }
}

// A loja fictícia do PostgreSQL em arquivos, com os caminhos relativos à pasta da fixture.
internal static class LojaBancoPg
{
    public static string Root => Path.Combine(FixtureWorkspace.FixturesRoot, "LojaBancoPg");

    public static List<SchemaFile> Files() =>
        Directory.GetFiles(Root, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".sql", StringComparison.Ordinal))
            .Select(f => new SchemaFile(Path.GetRelativePath(Root, f).Replace('\\', '/'), File.ReadAllText(f)))
            .OrderBy(f => f.RelativePath, StringComparer.Ordinal)
            .ToList();
}
