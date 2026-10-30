using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rastro.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class VisaoConsolidada : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "survey_diagrams",
                columns: table => new
                {
                    survey_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    mermaid = table.Column<string>(type: "TEXT", nullable: false),
                    commit_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_diagrams", x => x.survey_id);
                    table.ForeignKey(
                        name: "fk_survey_diagrams_surveys_survey_id",
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
                name: "survey_diagrams");
        }
    }
}
