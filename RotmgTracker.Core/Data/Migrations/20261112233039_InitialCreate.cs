using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RotmgTrackerV1.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Characters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RotmgCharId = table.Column<int>(type: "INTEGER", nullable: false),
                    ClassSlotNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Class = table.Column<int>(type: "INTEGER", nullable: false),
                    SkinId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsSeasonal = table.Column<bool>(type: "INTEGER", nullable: false),
                    SeasonTag = table.Column<string>(type: "TEXT", nullable: false),
                    IsAlive = table.Column<bool>(type: "INTEGER", nullable: false),
                    MaxedStats = table.Column<int>(type: "INTEGER", nullable: false),
                    BaseFame = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalFame = table.Column<int>(type: "INTEGER", nullable: false),
                    EquipmentIds = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DiedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DeathServer = table.Column<string>(type: "TEXT", nullable: true),
                    DeathDungeon = table.Column<string>(type: "TEXT", nullable: true),
                    KilledBy = table.Column<string>(type: "TEXT", nullable: true),
                    Drops = table.Column<string>(type: "TEXT", nullable: false),
                    LegacyDrops = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Characters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CombatSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DungeonName = table.Column<string>(type: "TEXT", nullable: false),
                    BossName = table.Column<string>(type: "TEXT", nullable: false),
                    BossObjectType = table.Column<int>(type: "INTEGER", nullable: false),
                    GraveDifficulty = table.Column<double>(type: "REAL", nullable: false),
                    CharacterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CharacterName = table.Column<string>(type: "TEXT", nullable: false),
                    CharacterClass = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalBossDamage = table.Column<int>(type: "INTEGER", nullable: false),
                    MyDamage = table.Column<int>(type: "INTEGER", nullable: false),
                    Participants = table.Column<string>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CombatSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DungeonRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CharacterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CharacterName = table.Column<string>(type: "TEXT", nullable: false),
                    CharacterClass = table.Column<int>(type: "INTEGER", nullable: false),
                    DungeonName = table.Column<string>(type: "TEXT", nullable: false),
                    DifficultyTier = table.Column<int>(type: "INTEGER", nullable: false),
                    ServerName = table.Column<string>(type: "TEXT", nullable: false),
                    IsSeasonal = table.Column<bool>(type: "INTEGER", nullable: false),
                    SeasonTag = table.Column<string>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DungeonRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LootHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BagInstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CombatSessionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CharacterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CharacterName = table.Column<string>(type: "TEXT", nullable: false),
                    CharacterClass = table.Column<int>(type: "INTEGER", nullable: false),
                    CharacterSkinId = table.Column<int>(type: "INTEGER", nullable: false),
                    CharacterMaxedStatsAtDrop = table.Column<int>(type: "INTEGER", nullable: false),
                    BaseFameAtDrop = table.Column<int>(type: "INTEGER", nullable: false),
                    AccountName = table.Column<string>(type: "TEXT", nullable: false),
                    IsSeasonal = table.Column<bool>(type: "INTEGER", nullable: false),
                    SeasonTag = table.Column<string>(type: "TEXT", nullable: false),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemName = table.Column<string>(type: "TEXT", nullable: false),
                    ItemTier = table.Column<int>(type: "INTEGER", nullable: false),
                    PhysicalBag = table.Column<int>(type: "INTEGER", nullable: false),
                    BagObjectType = table.Column<int>(type: "INTEGER", nullable: false),
                    BagType = table.Column<int>(type: "INTEGER", nullable: false),
                    Rarity = table.Column<int>(type: "INTEGER", nullable: false),
                    Enchantments = table.Column<string>(type: "TEXT", nullable: false),
                    IsShiny = table.Column<bool>(type: "INTEGER", nullable: false),
                    DungeonName = table.Column<string>(type: "TEXT", nullable: false),
                    DungeonPortalObjectType = table.Column<int>(type: "INTEGER", nullable: false),
                    GraveDifficulty = table.Column<double>(type: "REAL", nullable: false),
                    DungeonDifficulty = table.Column<int>(type: "INTEGER", nullable: false),
                    BossName = table.Column<string>(type: "TEXT", nullable: false),
                    BossObjectType = table.Column<int>(type: "INTEGER", nullable: false),
                    ServerName = table.Column<string>(type: "TEXT", nullable: false),
                    DroppedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RarityScore = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LootHistory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Openings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TotalChests = table.Column<int>(type: "INTEGER", nullable: false),
                    IsExpanded = table.Column<bool>(type: "INTEGER", nullable: false),
                    Chests = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Openings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Quests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuestTitle = table.Column<string>(type: "TEXT", nullable: false),
                    DungeonName = table.Column<string>(type: "TEXT", nullable: false),
                    MarkItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    MarkName = table.Column<string>(type: "TEXT", nullable: false),
                    DungeonPortalObjectType = table.Column<int>(type: "INTEGER", nullable: false),
                    BossName = table.Column<string>(type: "TEXT", nullable: false),
                    RealmBiomeSource = table.Column<string>(type: "TEXT", nullable: false),
                    RewardName = table.Column<string>(type: "TEXT", nullable: false),
                    RewardItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentMarks = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetMarks = table.Column<int>(type: "INTEGER", nullable: false),
                    IsAutoTracked = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Quests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "WishlistTargets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemName = table.Column<string>(type: "TEXT", nullable: false),
                    TargetDungeonName = table.Column<string>(type: "TEXT", nullable: false),
                    MinimumRarity = table.Column<int>(type: "INTEGER", nullable: false),
                    RequireShiny = table.Column<bool>(type: "INTEGER", nullable: false),
                    DesiredEnchantments = table.Column<string>(type: "TEXT", nullable: false),
                    EstimatedDropRate = table.Column<double>(type: "REAL", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsCompleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WishlistTargets", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Characters");

            migrationBuilder.DropTable(
                name: "CombatSessions");

            migrationBuilder.DropTable(
                name: "DungeonRuns");

            migrationBuilder.DropTable(
                name: "LootHistory");

            migrationBuilder.DropTable(
                name: "Openings");

            migrationBuilder.DropTable(
                name: "Quests");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "WishlistTargets");
        }
    }
}
