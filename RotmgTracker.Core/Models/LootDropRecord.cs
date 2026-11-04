using RotmgTrackerV1.Core.Enums;

namespace RotmgTrackerV1.Core.Models;

public class LootDropRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Relacja do postaci, która wydropiła przedmiot (nigdy nie ginie po śmierci postaci)
    public Guid CharacterId { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public RotmgClass CharacterClass { get; set; } = RotmgClass.Wizard;
    public int CharacterMaxedStatsAtDrop { get; set; } = 0;
    public int BaseFameAtDrop { get; set; } = 0;

    public string AccountName { get; set; } = string.Empty;
    public bool IsSeasonal { get; set; } = true;
    public string SeasonTag { get; set; } = "S1";

    // Dane przedmiotu
    public int ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int ItemTier { get; set; } = -1; // -1 dla UT/ST, 1..14 dla Tiered Items
    public bool IsTiered => ItemTier > 0;

    // Fizyczny worek vs analityczna kategoria jakości
    public Enums.BagType PhysicalBag { get; set; } = Enums.BagType.WhiteBag;
    public DropType BagType { get; set; } = DropType.RegularWhite;

    // Rzadkość, sloty na enchanty i Shiny
    public ItemRarity Rarity { get; set; } = ItemRarity.Common;
    public int EnchantmentSlots => (int)Rarity;
    public string Enchantments { get; set; } = string.Empty;
    public bool IsShiny { get; set; } = false;

    // Metadane lokacji, serwera i trudności dungeona
    public string DungeonName { get; set; } = "Unknown Dungeon";
    public DungeonDifficultyTier DungeonDifficulty { get; set; } = DungeonDifficultyTier.Tier2_Godlands;
    public string ServerName { get; set; } = "EUWest";
    public DateTime DroppedAt { get; set; } = DateTime.Now;

    // Wyliczony przez RarityScoreEngine wynik punktowy
    public double RarityScore { get; set; } = 100.0;
}