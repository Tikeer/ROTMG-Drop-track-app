using RotmgTrackerV1.Core.Enums;

namespace RotmgTrackerV1.Core.Models;

public class LootDropRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Grupuje przedmioty wypadłe z tego samego fizycznego worka w jeden kafelek UI
    public Guid BagInstanceId { get; set; } = Guid.NewGuid();

    // Odnośnik do walki DPS (pozwala kliknąć drop i zobaczyć zadany dmg w tej walce)
    public Guid? CombatSessionId { get; set; }

    public Guid CharacterId { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public RotmgClass CharacterClass { get; set; } = RotmgClass.Wizard;
    public int CharacterSkinId { get; set; } = 0;
    public int CharacterMaxedStatsAtDrop { get; set; } = 0;
    public int BaseFameAtDrop { get; set; } = 0;

    public string AccountName { get; set; } = string.Empty;
    public bool IsSeasonal { get; set; } = true;
    public string SeasonTag { get; set; } = "S1";

    // Dane przedmiotu + ID pod sprajt
    public int ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int ItemTier { get; set; } = -1;
    public bool IsTiered => ItemTier > 0;

    // Fizyczny worek + ID obiektu worka pod sprajt (np. 0x0509 = White Bag)
    public Enums.BagType PhysicalBag { get; set; } = Enums.BagType.WhiteBag;
    public int BagObjectType { get; set; } = 0x0509;
    public DropType BagType { get; set; } = DropType.RegularWhite;

    public ItemRarity Rarity { get; set; } = ItemRarity.Common;
    public int EnchantmentSlots => (int)Rarity;
    public string Enchantments { get; set; } = string.Empty;
    public bool IsShiny { get; set; } = false;

    // Dungeon (skala nagrobków 1.0 - 10.0 + sprajt portalu) oraz Boss (sprajt entity)
    public string DungeonName { get; set; } = "Unknown Dungeon";
    public int DungeonPortalObjectType { get; set; } = 0;
    public double GraveDifficulty { get; set; } = 3.0;
    public DungeonDifficultyTier DungeonDifficulty { get; set; } = DungeonDifficultyTier.Tier2_Godlands;

    public string BossName { get; set; } = "Unknown Boss";
    public int BossObjectType { get; set; } = 0;

    public string ServerName { get; set; } = "EUWest";
    public DateTime DroppedAt { get; set; } = DateTime.Now;

    public double RarityScore { get; set; } = 100.0;
}