using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rastro.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class QuadroDeFluxos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_business_layouts",
                table: "business_layouts");

            migrationBuilder.DropPrimaryKey(
                name: "pk_business_edge_layouts",
                table: "business_edge_layouts");

            migrationBuilder.AddColumn<Guid>(
                name: "lane_survey_id",
                table: "business_layouts",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "lane_survey_id",
                table: "business_edge_layouts",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddPrimaryKey(
                name: "pk_business_layouts",
                table: "business_layouts",
                columns: new[] { "scope", "scope_id", "lane_survey_id", "step_id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_business_edge_layouts",
                table: "business_edge_layouts",
                columns: new[] { "scope", "scope_id", "lane_survey_id", "from_step", "to_step" });

            migrationBuilder.CreateTable(
                name: "boards",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    glossary = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_boards", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "board_lanes",
                columns: table => new
                {
                    board_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    survey_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    order = table.Column<int>(type: "INTEGER", nullable: false),
                    title = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_board_lanes", x => new { x.board_id, x.survey_id });
                    table.ForeignKey(
                        name: "fk_board_lanes_boards_board_id",
                        column: x => x.board_id,
                        principalTable: "boards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_board_lanes_surveys_survey_id",
                        column: x => x.survey_id,
                        principalTable: "surveys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_board_lanes_survey_id",
                table: "board_lanes",
                column: "survey_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "board_lanes");

            migrationBuilder.DropTable(
                name: "boards");

            migrationBuilder.DropPrimaryKey(
                name: "pk_business_layouts",
                table: "business_layouts");

            migrationBuilder.DropPrimaryKey(
                name: "pk_business_edge_layouts",
                table: "business_edge_layouts");

            migrationBuilder.DropColumn(
                name: "lane_survey_id",
                table: "business_layouts");

            migrationBuilder.DropColumn(
                name: "lane_survey_id",
                table: "business_edge_layouts");

            migrationBuilder.AddPrimaryKey(
                name: "pk_business_layouts",
                table: "business_layouts",
                columns: new[] { "scope", "scope_id", "step_id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_business_edge_layouts",
                table: "business_edge_layouts",
                columns: new[] { "scope", "scope_id", "from_step", "to_step" });
        }
    }
}
