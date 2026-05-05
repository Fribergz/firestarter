using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Firestarter.Core.Migrations
{
    /// <inheritdoc />
    public partial class ProjectJenkinsPipelinesCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "JenkinsPipelinesCacheJson",
                table: "Projects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "JenkinsPipelinesCachedAt",
                table: "Projects",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "JenkinsPipelinesCacheJson",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "JenkinsPipelinesCachedAt",
                table: "Projects");
        }
    }
}
