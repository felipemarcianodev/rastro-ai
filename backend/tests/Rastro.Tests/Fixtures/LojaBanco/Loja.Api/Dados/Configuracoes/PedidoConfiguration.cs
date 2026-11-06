using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Loja.Api.Dados.Configuracoes;

public class PedidoConfiguration : IEntityTypeConfiguration<Pedido>
{
    public void Configure(EntityTypeBuilder<Pedido> builder)
    {
        builder.Property(p => p.Status).HasMaxLength(20).IsRequired();
        builder.Property(p => p.Total).HasPrecision(10, 2);
        builder.HasOne(p => p.Cliente).WithMany(c => c.Pedidos).HasForeignKey(p => p.ClienteId).OnDelete(DeleteBehavior.Restrict);
    }
}
