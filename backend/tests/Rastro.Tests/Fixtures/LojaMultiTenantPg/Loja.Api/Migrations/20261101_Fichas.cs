using Microsoft.EntityFrameworkCore.Migrations;

namespace Loja.Api.Migrations;

public partial class Fichas : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "fichas",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                titulo = table.Column<string>(type: "text", nullable: false),
                perfil = table.Column<string>(type: "jsonb", nullable: false),
                anotacoes = table.Column<string>(type: "jsonb", nullable: false),
                atributos = table.Column<string>(type: "json", nullable: false),
                sem_tipo = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_fichas", x => x.id);
            });

        migrationBuilder.Sql("CREATE INDEX ix_fichas_titulo_trgm ON fichas USING gin (titulo gin_trgm_ops);");
        migrationBuilder.CreateIndex(name: "ix_fichas_tenant_id", table: "fichas", column: "tenant_id");
    }
}
