using Rastro.Core.Database;
using Rastro.Extractor.Database;

namespace Rastro.Tests.Extractor;

// Cenários EXT-PG-040 em diante: o esquema do PostgreSQL lido do código (Fase 43a) na loja fictícia de
// vários inquilinos (LojaMultiTenantPg): CREATE INDEX com método e classe de operador, em .sql e em
// migrationBuilder.Sql, e propriedade de classe guardada em jsonb.
public class PgSchemaFase43aTests
{
    private static readonly Lazy<DbSchema> Loja = new(() => DbSchemaExtractor.Extract(LojaMultiTenantPg.Files()));

    private static DbTable Table(string name) => Assert.Single(Loja.Value.Tables, t => t.Name == name);

    private static DbSchema FromFiles(params (string File, string Text)[] files) =>
        DbSchemaExtractor.Extract(files.Select(f => new SchemaFile(f.File, f.Text)).ToList());

    [Fact] // EXT-PG-040
    public void Extract_CreateIndexComMetodoEClasseDeOperador_GuardaOMetodoESemNota()
    {
        var itens = Table("itens");

        var trigram = Assert.Single(itens.Indexes, i => i.Name == "ix_itens_titulo_trgm");
        Assert.Equal(["titulo"], trigram.Columns);
        Assert.Equal("gin", trigram.Method);
        Assert.Equal(["gin_trgm_ops"], trigram.OperatorClasses);
        Assert.True(trigram.IsTrigram("titulo"));
        Assert.False(trigram.IsTrigram("status"));

        var hash = Assert.Single(itens.Indexes, i => i.Name == "ix_itens_status_hash");
        Assert.Equal("hash", hash.Method);
        Assert.False(hash.IsTrigram("status"));

        // Sem método escrito, nulo (o padrão, btree).
        Assert.Null(Assert.Single(itens.Indexes, i => i.Name == "ix_itens_tenant_loja").Method);
        // Nenhuma nota "Expected ), found gin_trgm_ops".
        Assert.DoesNotContain(Loja.Value.Notes, n => n.Message.Contains("gin_trgm_ops") || n.Message.Contains("Expected"));
    }

    [Fact] // EXT-PG-041
    public void Extract_CreateIndexComVariantes_ColunasDeExpressaoOrdemColacaoIncludeEUnique()
    {
        var schema = FromFiles(("banco/x.sql", """
            CREATE TABLE t (a int, b text, c text, u uuid);
            CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS ux_t ON public.t USING btree (a DESC NULLS LAST, b COLLATE "C" text_pattern_ops) INCLUDE (c);
            CREATE INDEX ix_t_lower ON t (lower(b));
            CREATE INDEX ix_t_gist ON ONLY t USING gist (b gist_trgm_ops(siglen=32), (a + 1) WHERE a > 0);
            """));

        var table = Assert.Single(schema.Tables);

        var unique = Assert.Single(table.Indexes, i => i.Name == "ux_t");
        Assert.Equal(["a", "b"], unique.Columns);
        Assert.Equal(["c"], unique.Included);
        Assert.True(unique.Unique);
        Assert.Equal(["", "text_pattern_ops"], unique.OperatorClasses);
        Assert.Equal(["lower(b)"], Assert.Single(table.Indexes, i => i.Name == "ix_t_lower").Columns);
        var gist = Assert.Single(table.Indexes, i => i.Name == "ix_t_gist");
        Assert.Equal("gist", gist.Method);
        Assert.True(gist.IsTrigram("b"));
        Assert.Empty(schema.Notes);
    }

    [Fact] // EXT-PG-042
    public void Extract_MigrationComSqlDeIndiceTrigramaECreateIndexComMetodo_OIndiceNaoSome()
    {
        var fichas = Table("fichas");

        // O HasMethod/HasOperators do modelo e o SQL da migration são o mesmo índice (mesma coluna): um só.
        var trigram = Assert.Single(fichas.Indexes, i => i.Columns.SequenceEqual(["titulo"]));
        Assert.Equal("gin", trigram.Method);
        Assert.True(trigram.IsTrigram("titulo"));
        Assert.DoesNotContain(Loja.Value.Notes, n => n.Message.Contains("Expected"));

        var migration = FromFiles(("Migrations/20261101_X.cs", """
            using Microsoft.EntityFrameworkCore.Migrations;
            using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
            public partial class X : Migration
            {
                protected override void Up(MigrationBuilder migrationBuilder)
                {
                    migrationBuilder.CreateTable(name: "a", columns: table => new { id = table.Column<int>(type: "integer", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn), nome = table.Column<string>(type: "text", nullable: false), texto = table.Column<string>(type: "text", nullable: false) }, constraints: table => { table.PrimaryKey("pk_a", x => x.id); });
                    migrationBuilder.CreateIndex(name: "ix_a_nome", table: "a", column: "nome", method: "GIN", operators: new[] { "gin_trgm_ops" });
                    migrationBuilder.Sql("CREATE INDEX ix_a_nome_sql ON a USING gist (texto gist_trgm_ops);");
                }
            }
            """));
        var indexes = Assert.Single(migration.Tables).Indexes;
        Assert.Equal(("gin", true), (indexes.Single(i => i.Name == "ix_a_nome").Method, indexes.Single(i => i.Name == "ix_a_nome").IsTrigram("nome")));
        Assert.Equal(("gist", true), (indexes.Single(i => i.Name == "ix_a_nome_sql").Method, indexes.Single(i => i.Name == "ix_a_nome_sql").IsTrigram("texto")));
        Assert.Empty(migration.Notes);
    }

    [Fact] // EXT-PG-043
    public void Extract_PropriedadeDeClasseEmJsonb_ViraColunaDoModeloSemDivergenciaNemNota()
    {
        var fichas = Table("fichas");

        // Perfil e Anotacoes (HasColumnType("jsonb")) e Atributos ([Column(TypeName = "json")]) são colunas.
        Assert.Equal("jsonb", fichas.Columns.Single(c => c.Name == "perfil").Type);
        Assert.Equal("jsonb", fichas.Columns.Single(c => c.Name == "anotacoes").Type);
        Assert.Equal("json", fichas.Columns.Single(c => c.Name == "atributos").Type);
        Assert.Contains(fichas.Columns.Single(c => c.Name == "perfil").Sources, s => s.Kind == SchemaSourceKind.EfModel);

        // Só a propriedade de classe sem tipo de coluna configurado continua de fora, com a divergência e a nota.
        Assert.DoesNotContain(Loja.Value.Divergences, d => d.Table == "fichas" && d.Column is "perfil" or "anotacoes" or "atributos");
        Assert.Contains(Loja.Value.Divergences, d => d is { Table: "fichas", Column: "sem_tipo" });
        var notes = Loja.Value.Notes.Where(n => n.Message.Contains("não é entidade do modelo")).ToList();
        Assert.Equal("Ficha.SemTipo é do tipo Perfil, que não é entidade do modelo: a propriedade ficou de fora.", Assert.Single(notes).Message);
    }
}
