using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rastro.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DrawioLayout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "drawio_layouts",
                columns: table => new
                {
                    survey_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source = table.Column<int>(type: "INTEGER", nullable: false),
                    cells_json = table.Column<string>(type: "TEXT", nullable: false),
                    annotations_xml = table.Column<string>(type: "TEXT", nullable: true),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_drawio_layouts", x => new { x.survey_id, x.source });
                    table.ForeignKey(
                        name: "fk_drawio_layouts_surveys_survey_id",
                        column: x => x.survey_id,
                        principalTable: "surveys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "drawio_layouts");
        }
    }
}
