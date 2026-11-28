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
        DropType.BPs => (0x050E, BagType.GoldBag),
        _ => (0x0507, BagType.PurpleBag)
    };
    public static string FormatDropType(DropType t) => t switch
    {
        DropType.RegularWhite => "Regular White",
        DropType.LegendaryWhite => "Legendary White",
        DropType.DivineGoodItem => "Divine Good Item",
        DropType.EventWhite => "Realm White",
        DropType.BPs => "Gold Bag",
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
    /// Shared eligibility predicate for Item Collection & Profile progress.
    /// Excludes Blueprints and T0 starter gear; Legacy/Reskins hidden by default; Tiered split into Weapon/Armor vs Ability/Ring.
    /// </summary>
    public static bool MatchesCollectionBaseFilter(
        ItemMetadata item,
        bool includeTiered = false,
        int minWeaponArmorTier = 12,
        int minAbilityRingTier = 6,
        bool includeLegacy = false,
        bool includeReskins = false)
    {
        if (item.Quality == DropType.BPs) return false;
        if (!includeLegacy && item.IsLegacy) return false;
        if (!includeReskins && item.IsReskin) return false;

        // Non-tiered (UT / ST / Shiny): Tier < 0 (T0 starter gear has Tier == 0 and is excluded!)
        if (item.Tier < 0)
            return true;

        if (!includeTiered || item.Tier == 0)
            return false;

        if (item.IsWeaponOrArmor)
            return minWeaponArmorTier > 0 && item.Tier >= minWeaponArmorTier;

        if (item.IsAbilityOrRing)
            return minAbilityRingTier > 0 && item.Tier >= minAbilityRingTier;

        return false;
    }

    /// <summary>
    /// Single source of truth for Item Collection progress (matched strictly by ItemId; Blueprints, T0, Legacy, Reskins excluded by default).
    /// </summary>
    public static (int Owned, int Total, double Percent) ComputeCollectionProgress(
        IEnumerable<ItemMetadata> catalogItems,
        IEnumerable<LootDropRecord> loot,
        bool includeTiered = false,
        int minWeaponArmorTier = 12,
        int minAbilityRingTier = 6,
        bool includeLegacy = false,
        bool includeReskins = false)
    {
        var eligible = catalogItems
            .Where(i => MatchesCollectionBaseFilter(i, includeTiered, minWeaponArmorTier, minAbilityRingTier, includeLegacy, includeReskins))
            .ToList();

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