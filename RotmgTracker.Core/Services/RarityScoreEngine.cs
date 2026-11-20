using RotmgTrackerV1.Core.Enums;
using RotmgTrackerV1.Core.Models;

namespace RotmgTrackerV1.Core.Services;

public class TrophyCaseEntry
{
    public LootDropRecord BestRollDrop { get; set; } = null!;
    public int DuplicateCount { get; set; }
    public double DisplayScore { get; set; }
}

public static class RarityScoreEngine
{
    // Stałe do strojenia punktacji
    private const double ShinyMultiplier = 5.0;

    public static double CalculateIntrinsicScore(
        DropType quality,
        ItemRarity rarity,
        bool isShiny,
        double graveDifficulty,
        DungeonDifficultyTier fallbackTier = DungeonDifficultyTier.Tier2_Godlands,
        string itemName = "")
    {
        // Other (potki, jajka, śmieci, Marki) trafiają tylko do historii - bez punktów rzadkości
        if (quality == DropType.Other)
            return 0.0;

        // 1. Bazowa jakość kategorii dropu (Realm White waży najwięcej spośród białych)
        double basePoints = quality switch
        {
            DropType.Shivine => 550.0,
            DropType.EventWhite => 450.0,
            DropType.LegendaryWhite => 400.0,
            DropType.DivineGoodItem => 350.0,
            DropType.RegularWhite => 250.0,
            DropType.Shiny => 250.0,
            DropType.UT => 200.0,
            DropType.ST => 180.0,
            DropType.BPs => 140.0,
            DropType.Tops => 110.0,
            DropType.Tiered => 50.0,
            _ => 100.0
        };

        // 2. Mnożnik rarity (liczba slotów enchantów 0..4)
        double rarityMultiplier = rarity switch
        {
            ItemRarity.Common => 1.0,
            ItemRarity.Uncommon => 1.2,
            ItemRarity.Rare => 1.5,
            ItemRarity.Legendary => 2.0,
            ItemRarity.Divine => 3.0,
            _ => 1.0
        };

        // 3. Shiny to osobny, duży mnożnik (działa też, gdy kategoria bazowa to np. Realm White)
        double shinyMultiplier = (isShiny || quality is DropType.Shiny or DropType.Shivine) ? ShinyMultiplier : 1.0;

        // 4. Ręczna wartość itemu z item_value.json (przydatność / chciany drop; domyślnie 1.0)
        double itemValue = ItemValueTable.GetMultiplier(itemName);

        // 5. Trudność źródła z nagrobków - tylko niewielki, ograniczony modyfikator (0.9 - 1.25)
        double dungeonMultiplier = GetDungeonGraveMultiplier(graveDifficulty, fallbackTier);

        return Math.Round(basePoints * rarityMultiplier * shinyMultiplier * itemValue * dungeonMultiplier, 1);
    }

    public static double GetDungeonGraveMultiplier(double graveDifficulty, DungeonDifficultyTier fallbackTier)
    {
        if (graveDifficulty > 0)
        {
            return graveDifficulty switch
            {
                >= 9.5 => 1.25, // O3, Moonlight Village, Shatters
                >= 8.0 => 1.18,
                >= 6.5 => 1.12,
                >= 5.0 => 1.06,
                >= 3.0 => 1.00,
                _      => 0.90
            };
        }

        return fallbackTier switch
        {
            DungeonDifficultyTier.Tier6_PinnacleEndgame => 1.25,
            DungeonDifficultyTier.Tier5_HighExalt => 1.18,
            DungeonDifficultyTier.Tier4_ExaltAndHardEvent => 1.12,
            DungeonDifficultyTier.Tier3_EpicAndRealm => 1.06,
            DungeonDifficultyTier.Tier2_Godlands => 1.00,
            _ => 0.90
        };
    }

    public static DungeonDifficultyTier GraveCountToTier(double graves) => graves switch
    {
        >= 9.8 => DungeonDifficultyTier.Tier6_PinnacleEndgame,
        >= 9.0 => DungeonDifficultyTier.Tier5_HighExalt,
        >= 7.0 => DungeonDifficultyTier.Tier4_ExaltAndHardEvent,
        >= 5.0 => DungeonDifficultyTier.Tier3_EpicAndRealm,
        >= 3.0 => DungeonDifficultyTier.Tier2_Godlands,
        _      => DungeonDifficultyTier.Tier1_Early
    };

    public static List<TrophyCaseEntry> BuildTrophyCase(IEnumerable<LootDropRecord> allLoot, int maxItems = 24)
    {
        return allLoot
            .Where(l => l.BagType != DropType.Other)
            .GroupBy(l => new { Name = l.ItemName.ToLowerInvariant(), l.IsShiny })
            .Select(group =>
            {
                var bestDrop = group.OrderByDescending(x => x.RarityScore).First();
                int count = group.Count();
                double dupeBonusMultiplier = 1.0 + Math.Min(0.25, (count - 1) * 0.05);

                return new TrophyCaseEntry
                {
                    BestRollDrop = bestDrop,
                    DuplicateCount = count,
                    DisplayScore = Math.Round(bestDrop.RarityScore * dupeBonusMultiplier, 1)
                };
            })
            .OrderByDescending(e => e.DisplayScore)
            .Take(maxItems)
            .ToList();
    }
}