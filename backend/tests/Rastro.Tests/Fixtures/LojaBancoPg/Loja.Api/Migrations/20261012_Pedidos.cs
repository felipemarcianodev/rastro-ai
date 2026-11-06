using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace Loja.Api.Migrations;

public partial class Pedidos : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "pedidos",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                cliente_id = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                total = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                etiquetas = table.Column<string[]>(type: "text[]", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_pedidos", x => x.id);
                table.ForeignKey(
                    name: "fk_pedidos_clientes_cliente_id",
                    column: x => x.cliente_id,
                    principalTable: "clientes",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "itens_pedido",
            columns: table => new
            {
                pedido_id = table.Column<int>(type: "integer", nullable: false),
                produto_id = table.Column<int>(type: "integer", nullable: false),
                quantidade = table.Column<int>(type: "integer", nullable: false),
                preco_unit = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_itens_pedido", x => new { x.pedido_id, x.produto_id });
                table.ForeignKey(
                    name: "fk_itens_pedido_pedidos_pedido_id",
                    column: x => x.pedido_id,
                    principalTable: "pedidos",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_pedidos_cliente_id",
            table: "pedidos",
            column: "cliente_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "itens_pedido");
        migrationBuilder.DropTable(name: "pedidos");
    }
}
