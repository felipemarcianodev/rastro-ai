using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rastro.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CosturaEntreSistemas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cross_repositories_json",
                table: "surveys",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "route_indexes",
                columns: table => new
                {
                    repository = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    commit_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    entries_json = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_route_indexes", x => new { x.repository, x.commit_hash });
                });

            migrationBuilder.CreateTable(
                name: "survey_segments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    survey_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    order = table.Column<int>(type: "INTEGER", nullable: false),
                    repository = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    solution_path = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    entry_point = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    commit_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    branch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_segments", x => x.id);
                    table.ForeignKey(
                        name: "fk_survey_segments_surveys_survey_id",
                        column: x => x.survey_id,
                        principalTable: "surveys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_survey_segments_survey_id_order",
                table: "survey_segments",
                columns: new[] { "survey_id", "order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "route_indexes");

            migrationBuilder.DropTable(
                name: "survey_segments");

            migrationBuilder.DropColumn(
                name: "cross_repositories_json",
                table: "surveys");
        }
    }
}
