using RotmgTrackerV1.Core.Enums;
using RotmgTrackerV1.Core.Models;

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
}