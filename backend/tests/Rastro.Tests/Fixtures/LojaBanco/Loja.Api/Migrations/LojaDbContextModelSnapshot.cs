using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Loja.Api.Migrations;

// O snapshot repete o modelo em outro formato e não é fonte: o tamanho 99 daqui não pode aparecer no esquema.
[DbContext(typeof(LojaDbContext))]
partial class LojaDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity("Loja.Api.Dominio.Pedido", b =>
        {
            b.Property<string>("Status").HasMaxLength(99).HasColumnType("nvarchar(99)");
        });
    }
}
