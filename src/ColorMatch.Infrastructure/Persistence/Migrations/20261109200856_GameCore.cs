using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColorMatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GameCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CurrentStreak",
                table: "UserProgress",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "LastQualifyingActivityDate",
                table: "UserProgress",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Level",
                table: "UserProgress",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "LongestStreak",
                table: "UserProgress",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "XpTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<int>(type: "int", nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SourceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    BalanceAfter = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XpTransactions", x => x.Id);
                    table.CheckConstraint("CK_XpTransactions_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_XpTransactions_UserProfiles_UserId",
                        column: x => x.UserId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserProgress_TotalXp",
                table: "UserProgress",
                column: "TotalXp");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserProgress_Streak",
                table: "UserProgress",
                sql: "[CurrentStreak] >= 0 AND [LongestStreak] >= [CurrentStreak]");

            migrationBuilder.CreateIndex(
                name: "IX_XpTransactions_IdempotencyKey",
                table: "XpTransactions",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_XpTransactions_UserId_CreatedAtUtc_Id",
                table: "XpTransactions",
                columns: new[] { "UserId", "CreatedAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "XpTransactions");

            migrationBuilder.DropIndex(
                name: "IX_UserProgress_TotalXp",
                table: "UserProgress");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserProgress_Streak",
                table: "UserProgress");

            migrationBuilder.DropColumn(
                name: "CurrentStreak",
                table: "UserProgress");

            migrationBuilder.DropColumn(
                name: "LastQualifyingActivityDate",
                table: "UserProgress");

            migrationBuilder.DropColumn(
                name: "Level",
                table: "UserProgress");

            migrationBuilder.DropColumn(
                name: "LongestStreak",
                table: "UserProgress");
        }
    }
}
