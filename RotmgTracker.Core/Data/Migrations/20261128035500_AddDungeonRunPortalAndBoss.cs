using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RotmgTrackerV1.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDungeonRunPortalAndBoss : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BossName",
                table: "DungeonRuns",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "PortalId",
                table: "DungeonRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BossName",
                table: "DungeonRuns");

            migrationBuilder.DropColumn(
                name: "PortalId",
                table: "DungeonRuns");
        }
    }
}
