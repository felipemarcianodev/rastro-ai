using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace Loja.Api.Migrations;

// Como o Npgsql gera: tipos do catálogo do PostgreSQL e a identidade pela anotação Npgsql:.
public partial class Clientes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "clientes",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                cpf = table.Column<string>(type: "char(11)", nullable: true),
                codigo = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                preferencias = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_clientes", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ux_clientes_email",
            table: "clientes",
            column: "email",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "clientes");
    }
}
