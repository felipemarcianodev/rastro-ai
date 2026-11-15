using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rastro.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FalhasEmCodigo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "failure",
                table: "surveys",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "failure_args_json",
                table: "surveys",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "failure",
                table: "survey_validations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "failure_args_json",
                table: "survey_validations",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "refusal",
                table: "survey_proposals",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refusal_args_json",
                table: "survey_proposals",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "failure",
                table: "surveys");

            migrationBuilder.DropColumn(
                name: "failure_args_json",
                table: "surveys");

            migrationBuilder.DropColumn(
                name: "failure",
                table: "survey_validations");

            migrationBuilder.DropColumn(
                name: "failure_args_json",
                table: "survey_validations");

            migrationBuilder.DropColumn(
                name: "refusal",
                table: "survey_proposals");

            migrationBuilder.DropColumn(
                name: "refusal_args_json",
                table: "survey_proposals");
        }
    }
}
