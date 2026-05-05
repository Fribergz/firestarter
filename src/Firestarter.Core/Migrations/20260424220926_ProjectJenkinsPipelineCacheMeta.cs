using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Firestarter.Core.Migrations
{
    /// <inheritdoc />
    public partial class ProjectJenkinsPipelineCacheMeta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "JenkinsPipelinesCacheComplete",
                table: "Projects",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "JenkinsPipelinesCacheRunTake",
                table: "Projects",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "JenkinsPipelinesCacheComplete",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "JenkinsPipelinesCacheRunTake",
                table: "Projects");
        }
    }
}
