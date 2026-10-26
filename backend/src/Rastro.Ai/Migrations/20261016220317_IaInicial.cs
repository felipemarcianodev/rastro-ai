using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rastro.Ai.Migrations
{
    /// <inheritdoc />
    public partial class IaInicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_data_protection_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    friendly_name = table.Column<string>(type: "TEXT", nullable: true),
                    xml = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_data_protection_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ai_rule_cache",
                columns: table => new
                {
                    code_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    text = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_rule_cache", x => x.code_hash);
                });

            migrationBuilder.CreateTable(
                name: "ai_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false),
                    enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    provider = table.Column<int>(type: "INTEGER", nullable: false),
                    claude_key_protected = table.Column<string>(type: "TEXT", nullable: true),
                    claude_model = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    open_ai_key_protected = table.Column<string>(type: "TEXT", nullable: true),
                    open_ai_model = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    gemini_key_protected = table.Column<string>(type: "TEXT", nullable: true),
                    gemini_model = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    daily_token_budget = table.Column<int>(type: "INTEGER", nullable: false),
                    use_fill_holes = table.Column<bool>(type: "INTEGER", nullable: false),
                    use_describe_rules = table.Column<bool>(type: "INTEGER", nullable: false),
                    use_suggestions = table.Column<bool>(type: "INTEGER", nullable: false),
                    use_context_suggestions = table.Column<bool>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ai_usage_days",
                columns: table => new
                {
                    day = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    input_tokens = table.Column<long>(type: "INTEGER", nullable: false),
                    output_tokens = table.Column<long>(type: "INTEGER", nullable: false),
                    calls = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_usage_days", x => x.day);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_data_protection_keys");

            migrationBuilder.DropTable(
                name: "ai_rule_cache");

            migrationBuilder.DropTable(
                name: "ai_settings");

            migrationBuilder.DropTable(
                name: "ai_usage_days");
        }
    }
}
