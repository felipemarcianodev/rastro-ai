using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rastro.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "node_notes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    repository = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    node_id = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    text = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_node_notes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "surveys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    repository = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    solution_path = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    entry_point = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    max_depth = table.Column<int>(type: "INTEGER", nullable: false),
                    commit_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    branch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    error = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    schema_version = table.Column<int>(type: "INTEGER", nullable: false),
                    graph_json = table.Column<string>(type: "TEXT", nullable: true),
                    node_count = table.Column<int>(type: "INTEGER", nullable: false),
                    edge_count = table.Column<int>(type: "INTEGER", nullable: false),
                    unresolved_count = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    started_at = table.Column<long>(type: "INTEGER", nullable: true),
                    completed_at = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_surveys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "survey_architectures",
                columns: table => new
                {
                    survey_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    people_json = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_architectures", x => x.survey_id);
                    table.ForeignKey(
                        name: "fk_survey_architectures_surveys_survey_id",
                        column: x => x.survey_id,
                        principalTable: "surveys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "survey_drafts",
                columns: table => new
                {
                    survey_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    graph_json = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_drafts", x => x.survey_id);
                    table.ForeignKey(
                        name: "fk_survey_drafts_surveys_survey_id",
                        column: x => x.survey_id,
                        principalTable: "surveys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "survey_proposals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    survey_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    kind = table.Column<int>(type: "INTEGER", nullable: false),
                    origin = table.Column<int>(type: "INTEGER", nullable: false),
                    node_id = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    target = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    target_node_id = table.Column<string>(type: "TEXT", maxLength: 600, nullable: true),
                    evidence_file = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    when = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    evidence_line = table.Column<int>(type: "INTEGER", nullable: true),
                    text = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    rejection_reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    decided_at = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_proposals", x => x.id);
                    table.ForeignKey(
                        name: "fk_survey_proposals_surveys_survey_id",
                        column: x => x.survey_id,
                        principalTable: "surveys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "survey_validations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    survey_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    new_survey_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    result_json = table.Column<string>(type: "TEXT", nullable: true),
                    error = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    completed_at = table.Column<long>(type: "INTEGER", nullable: true),
                    approved_at = table.Column<long>(type: "INTEGER", nullable: true),
                    document_markdown = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_validations", x => x.id);
                    table.ForeignKey(
                        name: "fk_survey_validations_surveys_new_survey_id",
                        column: x => x.new_survey_id,
                        principalTable: "surveys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_survey_validations_surveys_survey_id",
                        column: x => x.survey_id,
                        principalTable: "surveys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_node_notes_repository_node_id",
                table: "node_notes",
                columns: new[] { "repository", "node_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_survey_proposals_survey_id_created_at",
                table: "survey_proposals",
                columns: new[] { "survey_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_survey_validations_new_survey_id",
                table: "survey_validations",
                column: "new_survey_id");

            migrationBuilder.CreateIndex(
                name: "ix_survey_validations_survey_id_created_at",
                table: "survey_validations",
                columns: new[] { "survey_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_surveys_created_at",
                table: "surveys",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_surveys_repository_entry_point",
                table: "surveys",
                columns: new[] { "repository", "entry_point" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "node_notes");

            migrationBuilder.DropTable(
                name: "survey_architectures");

            migrationBuilder.DropTable(
                name: "survey_drafts");

            migrationBuilder.DropTable(
                name: "survey_proposals");

            migrationBuilder.DropTable(
                name: "survey_validations");

            migrationBuilder.DropTable(
                name: "surveys");
        }
    }
}
