using System.Text.Json;
using RotmgTrackerV1.Core.Enums;
using RotmgTrackerV1.Core.Models;

namespace RotmgTrackerV1.Core.Services;

public sealed record TrophyHallItem(
    ItemMetadata Meta,
    int OwnedCount,
    ItemRarity BestRarity,
    double BestScore,
    int[] RarityCounts,
    string BossName,
    string DungeonName,
    bool IsStSection = false);

public sealed record TrophyHallBossGroup(
    string BossName,
    List<TrophyHallItem> Items);

public sealed record TrophyHallDungeonRow(
    string DungeonName,
    int PortalObjectType,
    double Graves,
    int Completions,
    List<TrophyHallBossGroup> BossGroups,
    List<TrophyHallItem> StItems,
    List<TrophyHallItem> Items)
{
    public int OwnedCount => Items.Count(i => i.OwnedCount > 0);
    public int TotalCount => Items.Count;
    public double CompletionPercent => TotalCount > 0 ? Math.Round((double)OwnedCount / TotalCount * 100.0, 1) : 0.0;
}

/// <summary>
/// Maps collectible items to their source (Dungeon, Boss) or Dungeon ST section using bundled Data/item_sources.json (v3),
/// optional user override in AppData/RotmgTracker/item_sources.json, and runtime LootHistory overlay (D5).
/// Shiny variants automatically inherit their base item's source(s).
/// </summary>
public sealed class ItemSourceService
{
    private const string UncategorizedDungeon = "Other / Uncategorized";
    private const string RealmDungeonName = "Realm of the Mad God";

    private static readonly HashSet<string> IgnoredOverlayDungeons = new(StringComparer.OrdinalIgnoreCase)
    {
        "", "Nexus", "Vault", "Unknown Dungeon", "Daily Quest Room", "Pet Yard", "Guild Hall", "Cloth Bazaar"
    };

    private sealed record ItemSourceRef(string Dungeon, string Boss, bool IsSt);

    // Normalized base item name -> source mappings (Dungeon, Boss, IsSt)
    private readonly Dictionary<string, List<ItemSourceRef>> _sourcesByBaseItem = new(StringComparer.OrdinalIgnoreCase);
    // Dungeon -> ordered boss list from item_sources.json
    private readonly Dictionary<string, List<string>> _bossOrderByDungeon = new(StringComparer.OrdinalIgnoreCase);

    // Prewarmed / cached default Trophy Hall snapshot (AllCharacters, non-tiered, non-BP, no legacy/reskin)
    private readonly object _cacheLock = new();
    private int _cachedLootCount = -1;
    private int _cachedRunsCount = -1;
    private List<TrophyHallDungeonRow>? _cachedDefaultRows;

    public int MappedBaseItemCount => _sourcesByBaseItem.Count;
    public int MappedDungeonCount { get; private set; }

    public string? GetPrimaryDungeonForItem(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return null;
        string baseName = StripShinyPrefix(itemName);
        if (_sourcesByBaseItem.TryGetValue(baseName, out var list) && list.Count > 0)
            return list[0].Dungeon;
        return null;
    }

    public (string Dungeon, string Boss)? GetPrimarySourceForItem(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return null;
        string baseName = StripShinyPrefix(itemName);
        if (_sourcesByBaseItem.TryGetValue(baseName, out var list) && list.Count > 0)
            return (list[0].Dungeon, list[0].Boss);
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
            .Where(i => i.Quality != DropType.BPs && i.Tier < 0 && !i.IsLegacy && !i.IsReskin)
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

    private void AddMapping(string rawItemName, string dungeon, string boss, bool isSt)
    {
        string baseName = StripShinyPrefix(rawItemName);
        if (string.IsNullOrWhiteSpace(baseName)) return;

        if (!_sourcesByBaseItem.TryGetValue(baseName, out var list))
        {
            list = new List<ItemSourceRef>();
            _sourcesByBaseItem[baseName] = list;
        }

        if (!list.Any(x =>
                string.Equals(x.Dungeon, dungeon, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Boss, boss, StringComparison.OrdinalIgnoreCase) &&
                x.IsSt == isSt))
        {
            list.Add(new ItemSourceRef(dungeon, boss, isSt));
        }
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
                if (string.IsNullOrWhiteSpace(dungeon)) continue;

                // v3 format: { "bosses": [ { "boss": "...", "items": [...] } ], "st": [...] }
                if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    allDungeons.Add(dungeon);
                    var bossOrder = new List<string>();

                    if (prop.Value.TryGetProperty("bosses", out var bossesEl) && bossesEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var bossObj in bossesEl.EnumerateArray())
                        {
                            string boss = bossObj.TryGetProperty("boss", out var bProp) ? (bProp.GetString()?.Trim() ?? dungeon) : dungeon;
                            if (!bossOrder.Contains(boss, StringComparer.OrdinalIgnoreCase))
                                bossOrder.Add(boss);

                            if (bossObj.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var itemEl in itemsEl.EnumerateArray())
                                {
                                    string? rawName = itemEl.GetString();
                                    if (!string.IsNullOrWhiteSpace(rawName))
                                        AddMapping(rawName, dungeon, boss, isSt: false);
                                }
                            }
                        }
                    }

                    if (bossOrder.Count > 0)
                        _bossOrderByDungeon[dungeon] = bossOrder;

                    if (prop.Value.TryGetProperty("st", out var stEl) && stEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var stItemEl in stEl.EnumerateArray())
                        {
                            if (stItemEl.ValueKind == JsonValueKind.String)
                            {
                                string? rawName = stItemEl.GetString();
                                if (!string.IsNullOrWhiteSpace(rawName))
                                    AddMapping(rawName, dungeon, "ST Sets", isSt: true);
                            }
                            else if (stItemEl.ValueKind == JsonValueKind.Object)
                            {
                                string? rawName = stItemEl.TryGetProperty("item", out var iProp) ? iProp.GetString() : null;
                                string? rawBoss = stItemEl.TryGetProperty("boss", out var bProp) ? bProp.GetString() : null;
                                if (!string.IsNullOrWhiteSpace(rawName))
                                    AddMapping(rawName, dungeon, string.IsNullOrWhiteSpace(rawBoss) ? "ST Sets" : rawBoss.Trim(), isSt: true);
                            }
                        }
                    }
                }
                // v2 fallback format: [ "Item1", "Item2" ]
                else if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    allDungeons.Add(dungeon);
                    foreach (var itemEl in prop.Value.EnumerateArray())
                    {
                        string? rawName = itemEl.GetString();
                        if (!string.IsNullOrWhiteSpace(rawName))
                            AddMapping(rawName, dungeon, dungeon, isSt: false);
                    }
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

    private sealed class OwnedAggregate
    {
        public int Count;
        public ItemRarity BestRarity;
        public double BestScore;
        public readonly int[] RarityCounts = new int[5];
    }

    /// <summary>
    /// Builds Trophy Hall dungeon rows grouped by Boss + ST section, with per-rarity ownership counts.
    /// Strict ItemId ownership matching (single source of truth).
    /// </summary>
    public List<TrophyHallDungeonRow> BuildTrophyHall(
        IEnumerable<ItemMetadata> filteredItems,
        IReadOnlyList<LootDropRecord> scopedLoot,
        IReadOnlyList<DungeonRunRecord> scopedRuns,
        GameDataService gameData)
    {
        var ownedById = new Dictionary<int, OwnedAggregate>();
        var overlayByBaseName = new Dictionary<string, List<ItemSourceRef>>(StringComparer.OrdinalIgnoreCase);

        foreach (var drop in scopedLoot)
        {
            if (drop.ItemId > 0)
            {
                if (!ownedById.TryGetValue(drop.ItemId, out var agg))
                {
                    agg = new OwnedAggregate();
                    ownedById[drop.ItemId] = agg;
                }

                agg.Count++;
                if (drop.Rarity > agg.BestRarity) agg.BestRarity = drop.Rarity;
                if (drop.RarityScore > agg.BestScore) agg.BestScore = drop.RarityScore;
                int rIdx = Math.Clamp((int)drop.Rarity, 0, 4);
                agg.RarityCounts[rIdx]++;
            }

            bool isSpecialCollectible = drop.BagType is DropType.RegularWhite or DropType.LegendaryWhite
                or DropType.EventWhite or DropType.ST or DropType.Shiny or DropType.Shivine;

            if (isSpecialCollectible &&
                !string.IsNullOrWhiteSpace(drop.DungeonName) &&
                !IgnoredOverlayDungeons.Contains(drop.DungeonName.Trim()))
            {
                string baseName = StripShinyPrefix(drop.ItemName);
                if (!_sourcesByBaseItem.ContainsKey(baseName))
                {
                    if (!overlayByBaseName.TryGetValue(baseName, out var list))
                    {
                        list = new List<ItemSourceRef>();
                        overlayByBaseName[baseName] = list;
                    }
                    string dName = drop.DungeonName.Trim();
                    string bName = string.IsNullOrWhiteSpace(drop.BossName) ? dName : drop.BossName.Trim();
                    bool isSt = drop.BagType == DropType.ST;
                    if (!list.Any(x => string.Equals(x.Dungeon, dName, StringComparison.OrdinalIgnoreCase)))
                        list.Add(new ItemSourceRef(dName, bName, isSt));
                }
            }
        }

        var compByDungeon = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var run in scopedRuns)
        {
            if (string.IsNullOrWhiteSpace(run.DungeonName)) continue;
            string d = run.DungeonName.Trim();
            compByDungeon[d] = compByDungeon.GetValueOrDefault(d) + 1;
        }

        // Dungeon -> (BossBuckets, StList, SeenIds)
        var bossBucketsByDungeon = new Dictionary<string, Dictionary<string, List<TrophyHallItem>>>(StringComparer.OrdinalIgnoreCase);
        var stByDungeon = new Dictionary<string, List<TrophyHallItem>>(StringComparer.OrdinalIgnoreCase);
        var seenInDungeon = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);

        void EnsureDungeon(string dungeon)
        {
            if (!bossBucketsByDungeon.ContainsKey(dungeon))
            {
                bossBucketsByDungeon[dungeon] = new Dictionary<string, List<TrophyHallItem>>(StringComparer.OrdinalIgnoreCase);
                stByDungeon[dungeon] = new List<TrophyHallItem>();
                seenInDungeon[dungeon] = new HashSet<int>();
            }
        }

        void AddItemToDungeon(string dungeon, string boss, bool isSt, ItemMetadata meta, OwnedAggregate? agg)
        {
            EnsureDungeon(dungeon);
            if (!seenInDungeon[dungeon].Add(meta.ItemId))
                return;

            var counts = agg != null ? (int[])agg.RarityCounts.Clone() : new int[5];
            bool putInSt = isSt || meta.Quality == DropType.ST;
            string displayBoss = !string.IsNullOrWhiteSpace(boss) && !boss.Equals("ST Sets", StringComparison.OrdinalIgnoreCase)
                ? boss
                : (string.IsNullOrWhiteSpace(meta.SetName) ? "ST Item" : meta.SetName);

            var entry = new TrophyHallItem(
                Meta: meta,
                OwnedCount: agg?.Count ?? 0,
                BestRarity: agg?.BestRarity ?? ItemRarity.Common,
                BestScore: agg?.BestScore ?? 0.0,
                RarityCounts: counts,
                BossName: displayBoss,
                DungeonName: dungeon,
                IsStSection: putInSt);

            if (putInSt)
            {
                stByDungeon[dungeon].Add(entry);
            }
            else
            {
                var bDict = bossBucketsByDungeon[dungeon];
                if (!bDict.TryGetValue(boss, out var bList))
                {
                    bList = new List<TrophyHallItem>();
                    bDict[boss] = bList;
                }
                bList.Add(entry);
            }
        }

        foreach (var meta in filteredItems)
        {
            ownedById.TryGetValue(meta.ItemId, out var agg);
            string baseName = StripShinyPrefix(meta.Name);

            bool assigned = false;
            if (_sourcesByBaseItem.TryGetValue(baseName, out var staticRefs))
            {
                foreach (var src in staticRefs)
                {
                    AddItemToDungeon(src.Dungeon, src.Boss, src.IsSt, meta, agg);
                    assigned = true;
                }
            }
            else if (overlayByBaseName.TryGetValue(baseName, out var liveRefs))
            {
                foreach (var src in liveRefs)
                {
                    AddItemToDungeon(src.Dungeon, src.Boss, src.IsSt, meta, agg);
                    assigned = true;
                }
            }

            if (!assigned)
            {
                string fallbackBoss = meta.Quality == DropType.ST && !string.IsNullOrWhiteSpace(meta.SetName)
                    ? meta.SetName
                    : (meta.Tier >= 0 ? "Tiered Gear" : "Event / Special");
                AddItemToDungeon(UncategorizedDungeon, fallbackBoss, isSt: false, meta, agg);
            }
        }

        static int CompareItems(TrophyHallItem a, TrophyHallItem b)
        {
            int ownedCmp = (b.OwnedCount > 0).CompareTo(a.OwnedCount > 0);
            if (ownedCmp != 0) return ownedCmp;
            int shinyCmp = a.Meta.IsShiny.CompareTo(b.Meta.IsShiny);
            if (shinyCmp != 0) return shinyCmp;
            return string.Compare(a.Meta.Name, b.Meta.Name, StringComparison.OrdinalIgnoreCase);
        }

        var rows = new List<TrophyHallDungeonRow>(bossBucketsByDungeon.Count);
        foreach (var (dungeon, bDict) in bossBucketsByDungeon)
        {
            var bossOrder = _bossOrderByDungeon.TryGetValue(dungeon, out var ord) ? ord : null;
            var orderedBosses = bDict
                .OrderBy(kv =>
                {
                    if (bossOrder == null) return 999;
                    int idx = bossOrder.FindIndex(b => string.Equals(b, kv.Key, StringComparison.OrdinalIgnoreCase));
                    return idx >= 0 ? idx : 999;
                })
                .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv =>
                {
                    kv.Value.Sort(CompareItems);
                    return new TrophyHallBossGroup(kv.Key, kv.Value);
                })
                .ToList();

            var stItems = stByDungeon[dungeon];
            stItems.Sort((a, b) =>
            {
                int setCmp = string.Compare(a.Meta.SetName, b.Meta.SetName, StringComparison.OrdinalIgnoreCase);
                if (setCmp != 0) return setCmp;
                return CompareItems(a, b);
            });

            var allItems = new List<TrophyHallItem>();
            foreach (var bg in orderedBosses) allItems.AddRange(bg.Items);
            allItems.AddRange(stItems);

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
                BossGroups: orderedBosses,
                StItems: stItems,
                Items: allItems));
        }

        return rows;
    }
}
