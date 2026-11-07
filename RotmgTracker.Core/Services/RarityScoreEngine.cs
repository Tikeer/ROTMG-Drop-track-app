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
        double graveDifficulty,
        DungeonDifficultyTier fallbackTier = DungeonDifficultyTier.Tier2_Godlands)
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

        // 4. Współczynnik trudności z oficjalnych nagrobków w grze (1.0 - 10.0 Graves)
        double dungeonMultiplier = GetDungeonGraveMultiplier(graveDifficulty, fallbackTier);

        return Math.Round(basePoints * rarityMultiplier * shinyMultiplier * dungeonMultiplier, 1);
    }

    public static double GetDungeonGraveMultiplier(double graveDifficulty, DungeonDifficultyTier fallbackTier)
    {
        if (graveDifficulty > 0)
        {
            return graveDifficulty switch
            {
                >= 10.0 => 3.00, // 10/10 Graves: The Shatters
                >= 9.5  => 2.75, // 9.5/10 Graves: Moonlight Village, Oryx's Sanctuary (O3)
                >= 9.0  => 2.40, // 9.0/10 Graves: Advanced Kogbold, Plagued Nest, Stromwell
                >= 8.0  => 2.00, // 8.0-8.5/10 Graves: The Void, Lost Halls, Spectral Penitentiary, Kogbold
                >= 6.5  => 1.65, // 6.5-7.5/10 Graves: Fungal, Crystal, The Nest, Cultist, Ice Citadel
                >= 5.0  => 1.35, // 5.0-6.0/10 Graves: Epic Dungeons, Wine Cellar (O2), Shaitan, Encore
                >= 3.0  => 1.00, // 3.0-4.5/10 Graves: Godlands (UDL, Abyss, Sewers, Oryx's Castle/O1)
                _       => 0.80  // 1.0-2.5/10 Graves: Early (Pirate Cave, Snake Pit, Sprite World)
            };
        }

        return fallbackTier switch
        {
            DungeonDifficultyTier.Tier6_PinnacleEndgame => 3.00,
            DungeonDifficultyTier.Tier5_HighExalt => 2.65,
            DungeonDifficultyTier.Tier4_ExaltAndHardEvent => 1.90,
            DungeonDifficultyTier.Tier3_EpicAndRealm => 1.35,
            DungeonDifficultyTier.Tier2_Godlands => 1.00,
            _ => 0.80
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