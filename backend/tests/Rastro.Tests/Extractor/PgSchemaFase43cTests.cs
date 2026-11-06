using Rastro.Core.Database;
using Rastro.Extractor.Database;

namespace Rastro.Tests.Extractor;

// Cenários EXT-PG-044 em diante: o esquema do PostgreSQL lido do código no reteste da Fase 43a (Fase 43c).
public class PgSchemaFase43cTests
{
    private const string Migration = """
        using Microsoft.EntityFrameworkCore.Migrations;
        public partial class Cria : Migration
        {
            protected override void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(name: "paineis", columns: table => new { id = table.Column<Guid>(type: "uuid", nullable: false), entries = table.Column<string>(type: "jsonb", nullable: false), meta = table.Column<string>(type: "jsonb", nullable: true), nota = table.Column<string>(type: "jsonb", nullable: false) }, constraints: table => { table.PrimaryKey("pk_paineis", x => x.id); });
            }
        }
        """;

    private static DbSchema Extract(string configuration) => DbSchemaExtractor.Extract([
        new SchemaFile("Migrations/20261105_Cria.cs", Migration),
        new SchemaFile("Dominio/Painel.cs", """
            using System.Collections.Generic;
            public class Painel
            {
                public Guid Id { get; set; }
                public Dictionary<string, string> Entries { get; set; } = new();
                public System.Collections.Generic.IDictionary<string, int>? Meta { get; set; }
                public Dictionary<string, string> Nota { get; set; } = new();
            }
            """),
        new SchemaFile("Dados/Ctx.cs", $$"""
            using Microsoft.EntityFrameworkCore;
            public class Ctx : DbContext
            {
                public DbSet<Painel> Paineis => Set<Painel>();
                protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseNpgsql("Host=x").UseSnakeCaseNamingConvention();
                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    modelBuilder.Entity<Painel>(builder =>
                    {
                        {{configuration}}
                    });
                }
            }
            """)]);

    [Fact] // EXT-PG-044
    public void Extract_DictionaryEmJsonbComConversao_NaOrdemQueForViraColunaSemDivergencia()
    {
        var schema = Extract("""
            builder.Property(x => x.Entries).HasColumnType("jsonb").HasConversion(v => v.ToString(), v => new Dictionary<string, string>());
            builder.Property(x => x.Meta).HasConversion(v => v.ToString(), v => null).HasColumnType("jsonb");
            builder.Property(x => x.Nota).HasColumnType("jsonb");
            """);

        var table = Assert.Single(schema.Tables);
        foreach (var column in new[] { "entries", "meta", "nota" })
        {
            Assert.Equal("jsonb", table.Columns.Single(c => c.Name == column).Type);
            Assert.Contains(table.Columns.Single(c => c.Name == column).Sources, s => s.Kind == SchemaSourceKind.EfModel);
        }
        Assert.DoesNotContain(schema.Divergences, d => d.Table == "paineis");
        Assert.DoesNotContain(schema.Notes, n => n.Message.Contains("não é entidade do modelo"));
    }

    [Fact] // EXT-PG-045
    public void Extract_DictionarySemTipoDeColunaConfigurado_ContinuaDeForaDoModelo()
    {
        var schema = Extract("""
            builder.Property(x => x.Nota).HasColumnType("jsonb");
            """);

        // Entries e Meta não têm tipo de coluna: ficam fora do modelo e a migration os mostra como divergência.
        Assert.Contains(schema.Divergences, d => d is { Table: "paineis", Column: "entries" });
        Assert.DoesNotContain(schema.Divergences, d => d is { Table: "paineis", Column: "nota" });
        Assert.Contains(schema.Divergences, d => d is { Table: "paineis", Column: "meta" });
    }

    [Fact] // EXT-PG-046
    public void Extract_JsonbComConversaoMultilinhaEValueComparerViaMetadata_ViraColunaSemDivergencia()
    {
        var schema = Extract("""
            builder.Property(x => x.Entries)
                .HasColumnName("entries")
                .HasColumnType("jsonb")
                .HasDefaultValueSql("'{}'::jsonb")
                .HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new Dictionary<string, string>())
                .Metadata.SetValueComparer(
                    new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<Dictionary<string, string>>(
                        (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
                        v => v.Aggregate(0, (h, kv) => HashCode.Combine(h, kv.Key.GetHashCode(), kv.Value.GetHashCode())),
                        v => new Dictionary<string, string>(v)));
            builder.Property(x => x.Meta).HasColumnType("jsonb");
            builder.Property(x => x.Nota).HasColumnType("jsonb");
            """);

        var table = Assert.Single(schema.Tables);
        Assert.Equal("jsonb", table.Columns.Single(c => c.Name == "entries").Type);
        Assert.DoesNotContain(schema.Divergences, d => d is { Table: "paineis", Column: "entries" });
    }
}
