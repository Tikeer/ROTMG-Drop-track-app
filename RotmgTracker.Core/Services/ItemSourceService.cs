using System.Text.Json;
using RotmgTrackerV1.Core.Enums;
using RotmgTrackerV1.Core.Models;

namespace RotmgTrackerV1.Core.Services;

public sealed record TrophyHallItem(
    ItemMetadata Meta,
    int OwnedCount,
    ItemRarity BestRarity,
    double BestScore);

public sealed record TrophyHallDungeonRow(
    string DungeonName,
    int PortalObjectType,
    double Graves,
    int Completions,
    List<TrophyHallItem> Items)
{
    public int OwnedCount => Items.Count(i => i.OwnedCount > 0);
    public int TotalCount => Items.Count;
    public double CompletionPercent => TotalCount > 0 ? Math.Round((double)OwnedCount / TotalCount * 100.0, 1) : 0.0;
}

/// <summary>
/// Maps collectible items to their source dungeon(s) using bundled Data/item_sources.json,
/// optional user override in AppData/RotmgTracker/item_sources.json, and runtime LootHistory overlay (D5).
/// Shiny variants automatically inherit their base item's source dungeon(s).
/// </summary>
public sealed class ItemSourceService
{
    private const string UncategorizedDungeon = "Other / Uncategorized";
    private const string RealmDungeonName = "Realm of the Mad God";

    private static readonly HashSet<string> IgnoredOverlayDungeons = new(StringComparer.OrdinalIgnoreCase)
    {
        "", "Nexus", "Vault", "Unknown Dungeon", "Daily Quest Room", "Pet Yard", "Guild Hall", "Cloth Bazaar"
    };

    // Normalized base item name -> list of source dungeon names
    private readonly Dictionary<string, HashSet<string>> _baseSourcesByItem = new(StringComparer.OrdinalIgnoreCase);

    // Prewarmed / cached default Trophy Hall snapshot (AllCharacters, no search/category filters, non-tiered, non-BP)
    private readonly object _cacheLock = new();
    private int _cachedLootCount = -1;
    private int _cachedRunsCount = -1;
    private List<TrophyHallDungeonRow>? _cachedDefaultRows;

    public int MappedBaseItemCount => _baseSourcesByItem.Count;
    public int MappedDungeonCount { get; private set; }

    /// <summary>
    /// Returns the primary known source dungeon for an item (strip "Shiny " prefix automatically), or null if unmapped.
    /// </summary>
    public string? GetPrimaryDungeonForItem(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return null;
        string baseName = StripShinyPrefix(itemName);
        if (_baseSourcesByItem.TryGetValue(baseName, out var dungeons) && dungeons.Count > 0)
            return dungeons.First();
        return null;
    }

    /// <summary>
    /// Pre-computes the default Trophy Hall view during AppStartup so opening Item Collection is instant.
    /// </summary>
    public void PrewarmDefaultTrophyHall(GameDataService gameData, TrackerStateService state)
    {
        var loot = state.GetLootSnapshot(DataScope.AllCharacters);
        var runs = state.GetDungeonRunsSnapshot(DataScope.AllCharacters);
        var defaultItems = gameData.GetAllKnownItems()
            .Where(i => i.Quality != DropType.BPs && i.Tier <= 0)
            .OrderByDescending(i => i.IsShiny)
            .ThenBy(i => i.Name)
            .ToList();

        var rows = BuildTrophyHall(defaultItems, loot, runs, gameData);
        lock (_cacheLock)
        {
            _cachedLootCount = loot.Count;
            _cachedRunsCount = runs.Count;
            _cachedDefaultRows = rows;
        }
    }

    public bool TryGetCachedDefaultTrophyHall(int lootCount, int runsCount, out List<TrophyHallDungeonRow> rows)
    {
        lock (_cacheLock)
        {
            if (_cachedDefaultRows != null && _cachedLootCount == lootCount && _cachedRunsCount == runsCount)
            {
                rows = _cachedDefaultRows;
                return true;
            }
        }
        rows = default!;
        return false;
    }

    public void UpdateCachedDefaultTrophyHall(int lootCount, int runsCount, List<TrophyHallDungeonRow> rows)
    {
        lock (_cacheLock)
        {
            _cachedLootCount = lootCount;
            _cachedRunsCount = runsCount;
            _cachedDefaultRows = rows;
        }
    }

    public ItemSourceService()
    {
        LoadFromFile(Path.Combine(AppContext.BaseDirectory, "Data", "item_sources.json"));

        string userOverride = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RotmgTracker",
            "item_sources.json");
        if (File.Exists(userOverride))
            LoadFromFile(userOverride);
    }

    private void LoadFromFile(string path)
    {
        if (!File.Exists(path)) return;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("dungeons", out var dungeonsEl) ||
                dungeonsEl.ValueKind != JsonValueKind.Object)
                return;

            var allDungeons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in dungeonsEl.EnumerateObject())
            {
                string dungeon = prop.Name.Trim();
                if (string.IsNullOrWhiteSpace(dungeon) || prop.Value.ValueKind != JsonValueKind.Array)
                    continue;

                allDungeons.Add(dungeon);
                foreach (var itemEl in prop.Value.EnumerateArray())
                {
                    string? rawName = itemEl.GetString();
                    if (string.IsNullOrWhiteSpace(rawName)) continue;

                    string baseName = StripShinyPrefix(rawName);
                    if (!_baseSourcesByItem.TryGetValue(baseName, out var set))
                    {
                        set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        _baseSourcesByItem[baseName] = set;
                    }
                    set.Add(dungeon);
                }
            }

            MappedDungeonCount = allDungeons.Count;
        }
        catch
        {
            // Corrupted override file is ignored silently
        }
    }

    public static string StripShinyPrefix(string name)
    {
        string trimmed = (name ?? string.Empty).Trim();
        if (trimmed.StartsWith("Shiny ", StringComparison.OrdinalIgnoreCase))
            return trimmed[6..].Trim();
        return trimmed;
    }

    /// <summary>
    /// Builds Trophy Hall dungeon rows for the given filtered items, scoped loot snapshot, and scoped dungeon runs.
    /// Strict ItemId ownership matching (single source of truth).
    /// </summary>
    public List<TrophyHallDungeonRow> BuildTrophyHall(
        IEnumerable<ItemMetadata> filteredItems,
        IReadOnlyList<LootDropRecord> scopedLoot,
        IReadOnlyList<DungeonRunRecord> scopedRuns,
        GameDataService gameData)
    {
        // Aggregate owned stats by ItemId (single source of truth)
        var ownedById = new Dictionary<int, (int Count, ItemRarity BestRarity, double BestScore)>();
        // Overlay: observed (baseItemName -> dungeons) from actual drops in LootHistory
        var overlayByBaseName = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var drop in scopedLoot)
        {
            if (drop.ItemId > 0)
            {
                if (ownedById.TryGetValue(drop.ItemId, out var prev))
                {
                    ownedById[drop.ItemId] = (
                        prev.Count + 1,
                        drop.Rarity > prev.BestRarity ? drop.Rarity : prev.BestRarity,
                        Math.Max(prev.BestScore, drop.RarityScore));
                }
                else
                {
                    ownedById[drop.ItemId] = (1, drop.Rarity, drop.RarityScore);
                }
            }

            // Only overlay LootHistory for unmapped White/ST/Shiny items (never pollute already-mapped dungeons with random event/chest/Tops drops)
            bool isSpecialCollectible = drop.BagType is DropType.RegularWhite or DropType.LegendaryWhite
                or DropType.EventWhite or DropType.ST or DropType.Shiny or DropType.Shivine;

            if (isSpecialCollectible &&
                !string.IsNullOrWhiteSpace(drop.DungeonName) &&
                !IgnoredOverlayDungeons.Contains(drop.DungeonName.Trim()))
            {
                string baseName = StripShinyPrefix(drop.ItemName);
                if (!_baseSourcesByItem.ContainsKey(baseName))
                {
                    if (!overlayByBaseName.TryGetValue(baseName, out var dset))
                    {
                        dset = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        overlayByBaseName[baseName] = dset;
                    }
                    dset.Add(drop.DungeonName.Trim());
                }
            }
        }

        // Completions count per dungeon name (case-insensitive)
        var compByDungeon = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var run in scopedRuns)
        {
            if (string.IsNullOrWhiteSpace(run.DungeonName)) continue;
            string d = run.DungeonName.Trim();
            compByDungeon[d] = compByDungeon.GetValueOrDefault(d) + 1;
        }

        // Group filtered items into dungeon buckets
        var buckets = new Dictionary<string, List<TrophyHallItem>>(StringComparer.OrdinalIgnoreCase);
        var seenPerBucket = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);

        void AddToBucket(string dungeon, TrophyHallItem entry)
        {
            if (!buckets.TryGetValue(dungeon, out var list))
            {
                list = new List<TrophyHallItem>();
                buckets[dungeon] = list;
                seenPerBucket[dungeon] = new HashSet<int>();
            }
            if (seenPerBucket[dungeon].Add(entry.Meta.ItemId))
                list.Add(entry);
        }

        foreach (var meta in filteredItems)
        {
            var stats = ownedById.TryGetValue(meta.ItemId, out var s)
                ? s
                : (Count: 0, BestRarity: ItemRarity.Common, BestScore: 0.0);

            var entry = new TrophyHallItem(meta, stats.Count, stats.BestRarity, stats.BestScore);
            string baseName = StripShinyPrefix(meta.Name);

            bool assigned = false;
            if (_baseSourcesByItem.TryGetValue(baseName, out var staticSources))
            {
                foreach (var d in staticSources)
                {
                    AddToBucket(d, entry);
                    assigned = true;
                }
            }

            if (overlayByBaseName.TryGetValue(baseName, out var liveSources))
            {
                foreach (var d in liveSources)
                {
                    AddToBucket(d, entry);
                    assigned = true;
                }
            }

            if (!assigned)
            {
                AddToBucket(UncategorizedDungeon, entry);
            }
        }

        var rows = new List<TrophyHallDungeonRow>(buckets.Count);
        foreach (var (dungeon, items) in buckets)
        {
            // Sort items within a dungeon: owned first, then non-shiny before shiny, then by name
            items.Sort((a, b) =>
            {
                int ownedCmp = (b.OwnedCount > 0).CompareTo(a.OwnedCount > 0);
                if (ownedCmp != 0) return ownedCmp;
                int shinyCmp = a.Meta.IsShiny.CompareTo(b.Meta.IsShiny);
                if (shinyCmp != 0) return shinyCmp;
                return string.Compare(a.Meta.Name, b.Meta.Name, StringComparison.OrdinalIgnoreCase);
            });

            var portal = gameData.ResolvePortalByDungeonName(dungeon);
            double graves = dungeon.Equals(UncategorizedDungeon, StringComparison.OrdinalIgnoreCase)
                ? 0.0
                : dungeon.Equals(RealmDungeonName, StringComparison.OrdinalIgnoreCase)
                    ? 6.0
                    : portal?.GraveDifficulty ?? 0.0;

            rows.Add(new TrophyHallDungeonRow(
                DungeonName: dungeon,
                PortalObjectType: portal?.ObjectType ?? 0,
                Graves: graves,
                Completions: compByDungeon.GetValueOrDefault(dungeon),
                Items: items));
        }

        return rows;
    }
}
