using Microsoft.EntityFrameworkCore.Migrations;

namespace Loja.Api.Migrations;

public partial class Pedidos : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "pedidos",
            columns: table => new
            {
                id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                cliente_id = table.Column<int>(type: "int", nullable: false),
                status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                total = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                criado_em = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_pedidos", x => x.id);
                table.ForeignKey(
                    name: "FK_pedidos_clientes_cliente_id",
                    column: x => x.cliente_id,
                    principalTable: "clientes",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "itens_pedido",
            columns: table => new
            {
                pedido_id = table.Column<int>(type: "int", nullable: false),
                produto_id = table.Column<int>(type: "int", nullable: false),
                quantidade = table.Column<int>(type: "int", nullable: false),
                preco_unit = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_itens_pedido", x => new { x.pedido_id, x.produto_id });
                table.ForeignKey(
                    name: "FK_itens_pedido_pedidos_pedido_id",
                    column: x => x.pedido_id,
                    principalTable: "pedidos",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_pedidos_cliente_id",
            table: "pedidos",
            column: "cliente_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "itens_pedido");
        migrationBuilder.DropTable(name: "pedidos");
    }
}
