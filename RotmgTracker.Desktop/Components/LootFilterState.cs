using RotmgTrackerV1.Core.Enums;
using RotmgTrackerV1.Core.Models;

namespace RotmgTrackerV1.Desktop.Components;

/// <summary>
/// Shared filter state used by both Live Feed (DashboardPanel) and Loot History (LootPanel).
/// Ensures identical filtering semantics (including Shiny matching IsShiny records).
/// </summary>
public sealed class LootFilterState
{
    public static readonly DropType[] FilterableQualities =
    [
        DropType.RegularWhite,
        DropType.LegendaryWhite,
        DropType.EventWhite,
        DropType.ST,
        DropType.BPs,
        DropType.Tops,
        DropType.DivineGoodItem,
        DropType.Shiny,
        DropType.Shivine,
        DropType.Other
    ];

    public DataScope Scope { get; set; } = DataScope.AllCharacters;
    public string SearchQuery { get; set; } = string.Empty;
    public string Dungeon { get; set; } = string.Empty;
    public string Boss { get; set; } = string.Empty;
    public DropType? Quality { get; set; }
    public ItemRarity? Rarity { get; set; }
    public bool ShowOther { get; set; } = true;

    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(SearchQuery) ||
        !string.IsNullOrWhiteSpace(Dungeon) ||
        !string.IsNullOrWhiteSpace(Boss) ||
        Quality.HasValue ||
        Rarity.HasValue;

    public void ClearFilters()
    {
        SearchQuery = string.Empty;
        Dungeon = string.Empty;
        Boss = string.Empty;
        Quality = null;
        Rarity = null;
    }

    public IEnumerable<LootDropRecord> Apply(IEnumerable<LootDropRecord> source)
    {
        var query = source;

        if (!ShowOther && Quality != DropType.Other)
            query = query.Where(l => l.BagType != DropType.Other);

        if (!string.IsNullOrWhiteSpace(Dungeon))
            query = query.Where(l => string.Equals(l.DungeonName, Dungeon, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(Boss))
            query = query.Where(l => string.Equals(l.BossName, Boss, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            string q = SearchQuery.Trim();
            query = query.Where(l =>
                l.ItemName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                l.DungeonName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                l.BossName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                l.CharacterName.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        if (Quality.HasValue)
        {
            DropType qType = Quality.Value;
            query = qType switch
            {
                // Matches the Shiny counter rule: standalone Shiny OR Shiny-flagged items (except Shivine)
                DropType.Shiny => query.Where(l => l.BagType == DropType.Shiny || (l.IsShiny && l.BagType != DropType.Shivine)),
                DropType.Shivine => query.Where(l => l.BagType == DropType.Shivine || (l.IsShiny && l.Rarity == ItemRarity.Divine)),
                DropType.DivineGoodItem => query.Where(l => l.BagType == DropType.DivineGoodItem || (l.Rarity == ItemRarity.Divine && l.BagType != DropType.Other)),
                _ => query.Where(l => l.BagType == qType)
            };
        }

        if (Rarity.HasValue)
            query = query.Where(l => l.Rarity == Rarity.Value);

        return query;
    }
}
