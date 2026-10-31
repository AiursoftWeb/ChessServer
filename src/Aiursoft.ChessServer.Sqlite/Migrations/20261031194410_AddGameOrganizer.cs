using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.ChessServer.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddGameOrganizer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MetadataJson",
                table: "CompletedGames",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrganizerUserId",
                table: "CompletedGames",
                type: "TEXT",
                maxLength: 450,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_CompletedGames_OrganizerUserId_FinishedAt",
                table: "CompletedGames",
                columns: new[] { "OrganizerUserId", "FinishedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CompletedGames_OrganizerUserId_FinishedAt",
                table: "CompletedGames");

            migrationBuilder.DropColumn(
                name: "MetadataJson",
                table: "CompletedGames");

            migrationBuilder.DropColumn(
                name: "OrganizerUserId",
                table: "CompletedGames");
        }
    }
}
