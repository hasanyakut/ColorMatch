using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColorMatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DailyHunt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DailyHunts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ChallengeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Difficulty = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TargetColorHex = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    BaseXp = table.Column<int>(type: "int", nullable: false),
                    MinScore = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyHunts", x => x.Id);
                    table.CheckConstraint("CK_DailyHunts_MinScore", "[MinScore] >= 0 AND [MinScore] <= 1000");
                    table.CheckConstraint("CK_DailyHunts_Xp", "[BaseXp] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "DailyHuntAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DailyHuntId = table.Column<int>(type: "int", nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    DeltaE = table.Column<double>(type: "float", nullable: false),
                    SampledColorHex = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    Passed = table.Column<bool>(type: "bit", nullable: false),
                    XpAwarded = table.Column<int>(type: "int", nullable: false),
                    RegionX = table.Column<double>(type: "float", nullable: false),
                    RegionY = table.Column<double>(type: "float", nullable: false),
                    RegionRadius = table.Column<double>(type: "float", nullable: false),
                    PhotoKey = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    PhotoHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyHuntAttempts", x => x.Id);
                    table.CheckConstraint("CK_DailyHuntAttempts_Score", "[Score] >= 0 AND [Score] <= 1000");
                    table.ForeignKey(
                        name: "FK_DailyHuntAttempts_DailyHunts_DailyHuntId",
                        column: x => x.DailyHuntId,
                        principalTable: "DailyHunts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DailyHuntAttempts_UserProfiles_UserId",
                        column: x => x.UserId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyHuntAttempts_DailyHuntId_Score",
                table: "DailyHuntAttempts",
                columns: new[] { "DailyHuntId", "Score" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyHuntAttempts_UserId_DailyHuntId_Score",
                table: "DailyHuntAttempts",
                columns: new[] { "UserId", "DailyHuntId", "Score" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyHuntAttempts_UserId_PhotoHash",
                table: "DailyHuntAttempts",
                columns: new[] { "UserId", "PhotoHash" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyHunts_ChallengeDate_Difficulty",
                table: "DailyHunts",
                columns: new[] { "ChallengeDate", "Difficulty" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyHuntAttempts");

            migrationBuilder.DropTable(
                name: "DailyHunts");
        }
    }
}
