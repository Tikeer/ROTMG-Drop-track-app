using RotmgTrackerV1.Core.Enums;
using RotmgTrackerV1.Core.Models;
using RotmgTrackerV1.Core.Services;

namespace RotmgTrackerV1.Desktop.Components;

/// <summary>Shared UI helpers used by more than one tab component.</summary>
public static class UiHelpers
{
    
    public static string GetDotClass(DropType t) => t switch
    {
        DropType.RegularWhite or DropType.LegendaryWhite or DropType.EventWhite => "dot-white",
        DropType.ST => "dot-st",
        DropType.UT or DropType.DivineGoodItem => "dot-ut",
        DropType.Shiny or DropType.Shivine => "dot-shiny",
        DropType.Tops => "dot-tops",
        _ => "dot-bp"
    };

    public static (int ObjectType, BagType FallbackBag) GetRepresentativeBag(DropType t) => t switch
    {
        DropType.RegularWhite or DropType.LegendaryWhite or DropType.EventWhite or DropType.Shiny or DropType.Shivine => (0x050C, BagType.WhiteBag),
        DropType.ST => (0x050F, BagType.OrangeBag),
        DropType.Tops => (0x06AC, BagType.RedBag),
        DropType.UT or DropType.DivineGoodItem => (0x0509, BagType.CyanBag),
        DropType.BPs => (0x050B, BagType.BlueBag),
        _ => (0x0507, BagType.PurpleBag)
    };
    public static string FormatDropType(DropType t) => t switch
    {
        DropType.RegularWhite => "Regular White",
        DropType.LegendaryWhite => "Legendary White",
        DropType.DivineGoodItem => "Divine Good Item",
        DropType.EventWhite => "Realm White",
        _ => t.ToString()
    };

    public static string GetRarityBorderColor(ItemRarity rarity, bool isShiny)
    {
        if (isShiny) return "#f472b6";
        return rarity switch
        {
            ItemRarity.Divine => "#f59e0b",
            ItemRarity.Legendary => "#a855f7",
            ItemRarity.Rare => "#38bdf8",
            ItemRarity.Uncommon => "#22c55e",
            _ => "var(--border-color)"
        };
    }

    public static List<List<LootDropRecord>> GroupByBagInstance(IEnumerable<LootDropRecord> records)
    {
        return records
            .GroupBy(r => r.BagInstanceId == Guid.Empty ? r.Id : r.BagInstanceId)
            .Select(g => g.ToList())
            .ToList();
    }

    /// <summary>
    /// Single source of truth for Item Collection progress (matched strictly by ItemId).
    /// </summary>
    public static (int Owned, int Total, double Percent) ComputeCollectionProgress(
        IEnumerable<ItemMetadata> catalogItems,
        IEnumerable<LootDropRecord> loot,
        bool includeTiered = false,
        int minTier = 12)
    {
        var eligible = includeTiered
            ? catalogItems.Where(i => i.Tier <= 0 || i.Tier >= minTier).ToList()
            : catalogItems.Where(i => i.Tier <= 0).ToList();

        var ownedIds = loot
            .Where(l => l.ItemId > 0)
            .Select(l => l.ItemId)
            .ToHashSet();

        int owned = eligible.Count(i => ownedIds.Contains(i.ItemId));
        int total = eligible.Count;
        double pct = total > 0 ? Math.Round((double)owned / total * 100.0, 1) : 0.0;
        return (owned, total, pct);
    }
}