using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Firestarter.Core.Migrations
{
    /// <inheritdoc />
    public partial class ProjectBranchesMrsSyncedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "BranchesMrsSyncedAt",
                table: "Projects",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BranchesMrsSyncedAt",
                table: "Projects");
        }
    }
}
