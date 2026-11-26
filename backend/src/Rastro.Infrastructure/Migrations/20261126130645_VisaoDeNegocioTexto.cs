using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rastro.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class VisaoDeNegocioTexto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "business_kind_overrides",
                columns: table => new
                {
                    survey_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    node_id = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    kind = table.Column<int>(type: "INTEGER", nullable: false),
                    origin = table.Column<int>(type: "INTEGER", nullable: false),
                    state = table.Column<int>(type: "INTEGER", nullable: false),
                    reason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_kind_overrides", x => new { x.survey_id, x.node_id });
                    table.ForeignKey(
                        name: "fk_business_kind_overrides_surveys_survey_id",
                        column: x => x.survey_id,
                        principalTable: "surveys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "business_labels",
                columns: table => new
                {
                    survey_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    node_id = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    code_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    text = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    origin = table.Column<int>(type: "INTEGER", nullable: false),
                    state = table.Column<int>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_labels", x => new { x.survey_id, x.node_id });
                    table.ForeignKey(
                        name: "fk_business_labels_surveys_survey_id",
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
                name: "business_kind_overrides");

            migrationBuilder.DropTable(
                name: "business_labels");
        }
    }
}
