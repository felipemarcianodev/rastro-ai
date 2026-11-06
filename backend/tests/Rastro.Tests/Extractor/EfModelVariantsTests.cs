using Rastro.Core.Database;
using Rastro.Extractor.Database;

namespace Rastro.Tests.Extractor;

// Cenários EXT-DB-010 em diante: outras formas de escrever o modelo do EF Core, sem snake case.
public class EfModelVariantsTests
{
    private const string Model = """
        using Microsoft.EntityFrameworkCore;
        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;

        public class VendasContext : DbContext
        {
            public DbSet<Produto> Produtos { get; set; } = null!;
            public DbSet<Categoria> Categorias { get; set; } = null!;
            public DbSet<Auditoria> Auditorias { get; set; } = null!;

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<Produto>().HasIndex(p => new { p.CategoriaCodigo, p.Nome }).IsUnique().HasDatabaseName("UX_Produto_Nome").IncludeProperties(p => p.Preco);
                modelBuilder.Entity<Categoria>(b =>
                {
                    b.ToTable("Categoria", "catalogo");
                    b.HasMany(c => c.Produtos).WithOne(p => p.Categoria).HasForeignKey(p => p.CategoriaCodigo).OnDelete(DeleteBehavior.SetNull);
                });
                modelBuilder.Entity<Auditoria>().HasNoKey();
            }
        }

        public abstract class Entidade
        {
            public Guid Id { get; set; }
            public DateTimeOffset CriadoEm { get; set; }
        }

        [Table("Produto")]
        public class Produto : Entidade
        {
            [Required, StringLength(80)]
            public string Nome { get; set; } = "";

            [Column("PrecoVenda")]
            public decimal Preco { get; set; }

            public string? CategoriaCodigo { get; set; }
            public Categoria? Categoria { get; set; }
            public byte[] Foto { get; set; } = [];
        }

        public class Categoria
        {
            [Key]
            public string Codigo { get; set; } = "";
            public List<Produto> Produtos { get; set; } = new();
        }

        public class Auditoria
        {
            public string Texto { get; set; } = "";
            public long Momento { get; set; }
        }
        """;

    private static readonly Lazy<DbSchema> Schema = new(() => DbSchemaExtractor.Extract([new SchemaFile("src/Vendas.cs", Model)]));

    private static DbTable Table(string name) => Assert.Single(Schema.Value.Tables, t => t.Name == name);

    [Fact] // EXT-DB-010
    public void Extract_AtributosEConfiguracaoFluente_NomesChavesETipos()
    {
        Assert.Equal(["Auditorias", "Categoria", "Produto"], Schema.Value.Tables.Select(t => t.Name).Order());
        Assert.Equal("catalogo", Table("Categoria").Schema);

        var produto = Table("Produto");
        Assert.Equal(["Id", "CriadoEm", "Nome", "PrecoVenda", "CategoriaCodigo", "Foto"], produto.Columns.Select(c => c.Name));
        Assert.Equal(["Id"], produto.PrimaryKey!.Columns);
        var id = produto.Columns[0];
        Assert.Equal(("uniqueidentifier", false), (id.Type, id.Identity));
        Assert.Equal("datetimeoffset", produto.Columns[1].Type);
        Assert.Equal(("nvarchar(80)", (bool?)false), (produto.Columns[2].Type, produto.Columns[2].Nullable));
        Assert.Equal("decimal(18,2)", produto.Columns[3].Type);
        // Chave estrangeira e índice com texto sem tamanho viram nvarchar(450), como o EF faz.
        Assert.Equal(("nvarchar(450)", (bool?)true), (produto.Columns[4].Type, produto.Columns[4].Nullable));
        Assert.Equal("varbinary(max)", produto.Columns[5].Type);
        Assert.Equal("nvarchar(450)", Table("Categoria").Columns.Single(c => c.Name == "Codigo").Type);
    }

    [Fact] // EXT-DB-011
    public void Extract_HasManyWithOneEIndiceFluente_RelacaoComODestinoNoSchemaEIndiceUnico()
    {
        var fk = Assert.Single(Table("Produto").ForeignKeys);
        Assert.Equal(["CategoriaCodigo"], fk.Columns);
        Assert.Equal("catalogo.Categoria", fk.PrincipalTable);
        Assert.Equal(["Codigo"], fk.PrincipalColumns);
        Assert.Equal(DeleteAction.SetNull, fk.OnDelete);

        var index = Assert.Single(Table("Produto").Indexes);
        Assert.Equal(("UX_Produto_Nome", true), (index.Name, index.Unique));
        Assert.Equal(["CategoriaCodigo", "Nome"], index.Columns);
        Assert.Equal(["PrecoVenda"], index.Included);
    }

    [Fact] // EXT-DB-012
    public void Extract_HasNoKey_TabelaSemChaveESemIdentidade()
    {
        var auditoria = Table("Auditorias");
        Assert.Null(auditoria.PrimaryKey);
        Assert.All(auditoria.Columns, c => Assert.False(c.Identity));
        Assert.Equal("bigint", auditoria.Columns.Single(c => c.Name == "Momento").Type);
    }

    [Fact] // EXT-DB-013
    public void Extract_DbSetDeEntidadeForaDoRepositorio_NotaEmVezDeTabela()
    {
        var schema = DbSchemaExtractor.Extract([new SchemaFile("src/Ctx.cs", "public class Ctx : DbContext { public DbSet<Externa> Externas { get; set; } }")]);

        Assert.Empty(schema.Tables);
        var note = Assert.Single(schema.Notes);
        Assert.Contains("Externa", note.Message);
        Assert.Equal(("src/Ctx.cs", 1), (note.File, note.Line));
    }
}
