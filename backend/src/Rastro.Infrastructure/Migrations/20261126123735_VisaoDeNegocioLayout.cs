using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rastro.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class VisaoDeNegocioLayout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "business_edge_layouts",
                columns: table => new
                {
                    scope = table.Column<int>(type: "INTEGER", nullable: false),
                    scope_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    from_step = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    to_step = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    points_json = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_edge_layouts", x => new { x.scope, x.scope_id, x.from_step, x.to_step });
                });

            migrationBuilder.CreateTable(
                name: "business_layouts",
                columns: table => new
                {
                    scope = table.Column<int>(type: "INTEGER", nullable: false),
                    scope_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    step_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    x = table.Column<double>(type: "REAL", nullable: false),
                    y = table.Column<double>(type: "REAL", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_layouts", x => new { x.scope, x.scope_id, x.step_id });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "business_edge_layouts");

            migrationBuilder.DropTable(
                name: "business_layouts");
        }
    }
}
