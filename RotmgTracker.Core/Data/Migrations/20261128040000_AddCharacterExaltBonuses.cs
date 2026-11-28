using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RotmgTrackerV1.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCharacterExaltBonuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExaltBonuses",
                table: "Characters",
                type: "TEXT",
                nullable: false,
                defaultValue: "[0,0,0,0,0,0,0,0]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExaltBonuses",
                table: "Characters");
        }
    }
}
