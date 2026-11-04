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
    public static double CalculateIntrinsicScore(
        DropType quality,
        ItemRarity rarity,
        bool isShiny,
        DungeonDifficultyTier dungeonTier)
    {
        // 1. Bazowa jakość przedmiotu
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

        // 2. Mnożnik Rarity / Liczby slotów na enchanty (0..4 sloty)
        double rarityMultiplier = rarity switch
        {
            ItemRarity.Common => 1.0,      // 0 slotów
            ItemRarity.Uncommon => 1.15,   // 1 slot
            ItemRarity.Rare => 1.35,       // 2 sloty
            ItemRarity.Legendary => 1.75,  // 3 sloty
            ItemRarity.Divine => 2.50,     // 4 sloty
            _ => 1.0
        };

        // 3. Modyfikator Shiny Sprite
        double shinyMultiplier = (isShiny || quality is DropType.Shiny or DropType.Shivine) ? 3.0 : 1.0;

        // 4. Współczynnik trudności dungeona (Shatters & MV najwyżej, potem O3 i inne Exalty)
        double dungeonMultiplier = dungeonTier switch
        {
            DungeonDifficultyTier.Tier6_PinnacleEndgame => 3.0,   // Shatters, Moonlight Village (Umi)
            DungeonDifficultyTier.Tier5_HighExalt => 2.5,         // Oryx's Sanctuary (O3), Adv Kog/Nest
            DungeonDifficultyTier.Tier4_ExaltAndHardEvent => 1.8, // Void, Halls, Fungal, Hard Events
            DungeonDifficultyTier.Tier3_EpicAndRealm => 1.4,      // LotLL, Davy, Nest
            DungeonDifficultyTier.Tier2_Godlands => 1.0,          // UDL, Abyss, Sewers
            DungeonDifficultyTier.Tier1_Early => 0.8,             // Sprite, Snake Pit
            _ => 1.0
        };

        return Math.Round(basePoints * rarityMultiplier * shinyMultiplier * dungeonMultiplier, 1);
    }

    // Buduje posortowaną wystawę (Trophy Case): łączy duplikaty w jedną kartę [xN] i daje mikro-bonus +5% za sztukę
    public static List<TrophyCaseEntry> BuildTrophyCase(IEnumerable<LootDropRecord> allLoot, int maxItems = 24)
    {
        return allLoot
            .GroupBy(l => new { Name = l.ItemName.ToLowerInvariant(), l.IsShiny })
            .Select(group =>
            {
                var bestDrop = group.OrderByDescending(x => x.RarityScore).First();
                int count = group.Count();

                // Mikro-bonus za duplikaty (+5% za każdy kolejny, maks +25%), nigdy nie obniża rangi przedmiotu!
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