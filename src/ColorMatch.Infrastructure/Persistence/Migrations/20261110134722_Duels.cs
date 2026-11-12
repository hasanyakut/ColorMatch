using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColorMatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Duels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Duels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChallengerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OpponentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ColorKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChallengerDone = table.Column<bool>(type: "bit", nullable: false),
                    OpponentDone = table.Column<bool>(type: "bit", nullable: false),
                    WinnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChallengerBest = table.Column<int>(type: "int", nullable: true),
                    OpponentBest = table.Column<int>(type: "int", nullable: true),
                    ChallengerXp = table.Column<int>(type: "int", nullable: false),
                    OpponentXp = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Duels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Duels_UserProfiles_ChallengerId",
                        column: x => x.ChallengerId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Duels_UserProfiles_OpponentId",
                        column: x => x.OpponentId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DuelAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DuelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    DeltaE = table.Column<double>(type: "float", nullable: false),
                    SampledColorHex = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    PhotoHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DuelAttempts", x => x.Id);
                    table.CheckConstraint("CK_DuelAttempts_Score", "[Score] >= 0 AND [Score] <= 1000");
                    table.ForeignKey(
                        name: "FK_DuelAttempts_Duels_DuelId",
                        column: x => x.DuelId,
                        principalTable: "Duels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DuelAttempts_UserProfiles_UserId",
                        column: x => x.UserId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DuelAttempts_DuelId_PhotoHash",
                table: "DuelAttempts",
                columns: new[] { "DuelId", "PhotoHash" });

            migrationBuilder.CreateIndex(
                name: "IX_DuelAttempts_DuelId_UserId",
                table: "DuelAttempts",
                columns: new[] { "DuelId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_DuelAttempts_UserId",
                table: "DuelAttempts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Duels_ChallengerId_Status",
                table: "Duels",
                columns: new[] { "ChallengerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Duels_OpponentId_Status",
                table: "Duels",
                columns: new[] { "OpponentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Duels_Status_ExpiresAtUtc",
                table: "Duels",
                columns: new[] { "Status", "ExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DuelAttempts");

            migrationBuilder.DropTable(
                name: "Duels");
        }
    }
}
