using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rastro.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class QuadroDrawio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "drawio_annotations",
                table: "boards",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "drawio_annotations",
                table: "boards");
        }
    }
}
