using Microsoft.EntityFrameworkCore.Migrations;

namespace Loja.Api.Migrations;

public partial class Clientes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "clientes",
            columns: table => new
            {
                id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                nome = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                cpf = table.Column<string>(type: "char(11)", nullable: true),
                criado_em = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_clientes", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "UX_clientes_email",
            table: "clientes",
            column: "email",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "clientes");
    }
}
