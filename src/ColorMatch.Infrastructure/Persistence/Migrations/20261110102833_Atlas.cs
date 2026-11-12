using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColorMatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Atlas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserAtlasColors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ColorKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CollectionKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    UnlockedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAtlasColors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAtlasColors_UserProfiles_UserId",
                        column: x => x.UserId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserAtlasColors_UserId_CollectionKey",
                table: "UserAtlasColors",
                columns: new[] { "UserId", "CollectionKey" });

            migrationBuilder.CreateIndex(
                name: "IX_UserAtlasColors_UserId_ColorKey",
                table: "UserAtlasColors",
                columns: new[] { "UserId", "ColorKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserAtlasColors");
        }
    }
}
