using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace Loja.Api.Migrations;

// Escrita à mão: a FK pedido_id ficou sem o índice que o EF criaria.
public partial class Pagamentos : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "pagamentos",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                pedido_id = table.Column<int>(type: "integer", nullable: false),
                metodo = table.Column<short>(type: "smallint", nullable: false),
                valor = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                pago_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_pagamentos", x => x.id);
                table.ForeignKey(
                    name: "fk_pagamentos_pedidos_pedido_id",
                    column: x => x.pedido_id,
                    principalTable: "pedidos",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.Sql("CREATE VIEW vw_pagamentos_pendentes AS SELECT id, pedido_id, valor FROM pagamentos WHERE pago_em IS NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "pagamentos");
    }
}
