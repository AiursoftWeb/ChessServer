using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.ChessServer.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddCompletedGames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompletedGames",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WhiteUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    BlackUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    WhiteName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    BlackName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Result = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    EndReason = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Pgn = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompletedGames", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompletedGames_BlackUserId_FinishedAt",
                table: "CompletedGames",
                columns: new[] { "BlackUserId", "FinishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CompletedGames_WhiteUserId_FinishedAt",
                table: "CompletedGames",
                columns: new[] { "WhiteUserId", "FinishedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompletedGames");
        }
    }
}
