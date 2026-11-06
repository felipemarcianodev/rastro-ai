using Loja.Api.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Loja.Api.Dados;

public class LojaDbContext(DbContextOptions<LojaDbContext> options) : DbContext(options)
{
    public DbSet<Ficha> Fichas => Set<Ficha>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseNpgsql("Host=localhost;Database=loja").UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Ficha>(b =>
        {
            b.HasKey(f => f.Id);
            b.Property(f => f.Perfil).HasColumnType("jsonb");
            b.Property(f => f.Anotacoes).HasColumnType("jsonb");
            b.HasIndex(f => f.Titulo).HasMethod("gin").HasOperators("gin_trgm_ops");
        });
    }
}
