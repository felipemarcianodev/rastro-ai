using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rastro.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BancoVivo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "live_connections",
                columns: table => new
                {
                    repository = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    connection_protected = table.Column<string>(type: "TEXT", nullable: false),
                    server = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    database = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_connections", x => x.repository);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "live_connections");
        }
    }
}
