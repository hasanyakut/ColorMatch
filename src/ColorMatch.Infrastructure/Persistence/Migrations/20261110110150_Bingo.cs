using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColorMatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Bingo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BingoBoards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BingoBoards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BingoBoards_UserProfiles_UserId",
                        column: x => x.UserId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BingoAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BoardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CellIndex = table.Column<int>(type: "int", nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    DeltaE = table.Column<double>(type: "float", nullable: false),
                    SampledColorHex = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    Passed = table.Column<bool>(type: "bit", nullable: false),
                    PhotoHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BingoAttempts", x => x.Id);
                    table.CheckConstraint("CK_BingoAttempts_Score", "[Score] >= 0 AND [Score] <= 1000");
                    table.ForeignKey(
                        name: "FK_BingoAttempts_BingoBoards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "BingoBoards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BingoAttempts_UserProfiles_UserId",
                        column: x => x.UserId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BingoCells",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BoardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Index = table.Column<int>(type: "int", nullable: false),
                    ColorKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BestScore = table.Column<int>(type: "int", nullable: true),
                    CompletedByAttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BingoCells", x => x.Id);
                    table.CheckConstraint("CK_BingoCells_Index", "[Index] >= 0 AND [Index] <= 8");
                    table.ForeignKey(
                        name: "FK_BingoCells_BingoBoards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "BingoBoards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BingoAttempts_BoardId_CellIndex",
                table: "BingoAttempts",
                columns: new[] { "BoardId", "CellIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_BingoAttempts_BoardId_PhotoHash",
                table: "BingoAttempts",
                columns: new[] { "BoardId", "PhotoHash" });

            migrationBuilder.CreateIndex(
                name: "IX_BingoAttempts_UserId",
                table: "BingoAttempts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BingoBoards_UserId_Active",
                table: "BingoBoards",
                column: "UserId",
                unique: true,
                filter: "[Status] = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_BingoBoards_UserId_CreatedAtUtc",
                table: "BingoBoards",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BingoCells_BoardId_Index",
                table: "BingoCells",
                columns: new[] { "BoardId", "Index" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BingoAttempts");

            migrationBuilder.DropTable(
                name: "BingoCells");

            migrationBuilder.DropTable(
                name: "BingoBoards");
        }
    }
}
