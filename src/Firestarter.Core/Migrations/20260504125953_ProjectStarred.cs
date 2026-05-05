using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Firestarter.Core.Migrations
{
    /// <inheritdoc />
    public partial class ProjectStarred : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Starred",
                table: "Projects",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Starred",
                table: "Projects");
        }
    }
}
