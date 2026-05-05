using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Firestarter.Core.Migrations
{
    /// <inheritdoc />
    public partial class ExtensionSettingsValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SettingsValuesJson",
                table: "Extensions",
                type: "TEXT",
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SettingsValuesJson",
                table: "Extensions");
        }
    }
}
