using Microsoft.EntityFrameworkCore;

namespace Loja.Api.Dados;

public class LojaDbContext(DbContextOptions<LojaDbContext> options) : DbContext(options)
{
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Pedido> Pedidos => Set<Pedido>();
    public DbSet<ItemPedido> ItensPedido => Set<ItemPedido>();
    public DbSet<Pagamento> Pagamentos => Set<Pagamento>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LojaDbContext).Assembly);

        modelBuilder.Entity<ItemPedido>(b =>
        {
            b.HasKey(i => new { i.PedidoId, i.ProdutoId });
            b.Property(i => i.PrecoUnit).HasPrecision(10, 2);
            b.HasOne(i => i.Pedido).WithMany(p => p.Itens).HasForeignKey(i => i.PedidoId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Pagamento>().Property(p => p.Id).UseIdentityAlwaysColumn();
    }
}
