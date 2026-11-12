using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using RotmgTrackerV1.Core.Enums;

namespace RotmgTrackerV1.Core.Services;

public record ItemMetadata(
    int ItemId,
    string Name,
    DropType Quality,
    int Tier,
    bool IsShiny = false,
    int BagTypeId = 0,
    int SlotType = 0,
    string ForgeTier = "",
    string TextureFile = "",
    int TextureIndex = 0);

public record EnemyMetadata(
    int ObjectType,
    string IdName,
    string DisplayName,
    bool IsBoss,
    int MaxHp,
    string TextureFile = "",
    int TextureIndex = 0,
    string Category = "");   // "", "DungeonBoss", "AdeptEncounter", "VeteranEncounter"

public record PortalMetadata(
    int ObjectType,
    string PortalId,
    string DungeonName,
    double GraveDifficulty,
    string TextureFile = "",
    int TextureIndex = 0);

public record MarkMetadata(
    int ItemId,
    string MarkName,
    string BossName,
    string DungeonName,
    string TextureFile = "",
    int TextureIndex = 0);

public class GameDataService
{
    private sealed record ForgeXmlEntry(
        int LegendaryDismantle,
        int RareDismantle,
        int CommonDismantle,
        bool IsRealmWhite);

    private readonly string _appDataDir;
    private readonly string _itemCatalogPath;
    private readonly string _enemyCatalogPath;
    private readonly string _portalCatalogPath;
    private readonly string _markCatalogPath;
    private readonly string _lootOnlyCatalogPath;
    private readonly object _catalogLock = new();

    private readonly Dictionary<int, ItemMetadata> _items = new();
    private readonly Dictionary<int, EnemyMetadata> _enemies = new();
    private readonly Dictionary<string, PortalMetadata> _portalsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, MarkMetadata> _marks = new();
    private readonly Dictionary<int, ItemMetadata> _lootOnlyItems = new();
    private readonly Dictionary<int, RotmgClass> _playerClasses = new();
    private readonly List<string> _bossReasons = new();

    private readonly Dictionary<int, BagType> _bagContainers = new()
    {
        { 0x0500, BagType.BrownBag },
        { 0x0506, BagType.PinkBag },
        { 0x0507, BagType.PurpleBag },
        { 0x0508, BagType.EggBag },
        { 0x0509, BagType.CyanBag },
        { 0x050B, BagType.BlueBag },
        { 0x050C, BagType.WhiteBag },
        { 0x0510, BagType.WhiteBag },
        { 0x050E, BagType.RedBag },
        { 0x050F, BagType.OrangeBag }
    };

    private static readonly HashSet<int> ValidEquipmentSlotTypes =
    [
        1, 2, 3, 4, 5, 6, 7, 8, 9,
        11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25,
        27, 28, 29, 30, 31, 32, 33, 34
    ];

    private static readonly string[] ExcludedItemKeywords =
    [
        "Rehearsal", "(Rehearsal)", "Admin", "Test ", " Test", "Cheat",
        "Deprecated", "Placeholder", "Shard", "Token", "Mark of ",
        "Mystery ", "Skin", "Pet Stone", "Dye", "Cloth", "Key",
        "Tincture", "Effusion", "Elixir", "Drake Egg", " Shot "
    ];

    private static readonly string[] ExcludedPortalKeywords =
    [
        "Rehearsal", "Legacy ", "(Heroic)", "Arena", "Test",
        "Treasure Room", "Consolation", "Gates", "Graves",
        "Final Battle", "Wine Cellar Short", "Cowardice",
        "Glowing Realm", "Snowball", "Encounter", "Oryxmania",
        "Oryx Mania", "LOD Rock Dragon", "Chicken Chamber"
    ];

    public string ExtractionStatus { get; private set; } = "Not initialized";

    public GameDataService()
    {
        _appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RotmgTracker");
        Directory.CreateDirectory(_appDataDir);

        // Podbicie wersji katalogów dla wymuszenia świeżego skanowania
        _itemCatalogPath = Path.Combine(_appDataDir, "item_catalog_v7.json");
        _enemyCatalogPath = Path.Combine(_appDataDir, "enemy_catalog_v8.json");
        _portalCatalogPath = Path.Combine(_appDataDir, "portal_catalog_v4.json");
        _markCatalogPath = Path.Combine(_appDataDir, "mark_catalog_v4.json");
        _lootOnlyCatalogPath = Path.Combine(_appDataDir, "loot_only_catalog_v1.json");

        LoadCachedCatalogs();

        // Brak pliku loot_only_catalog_v1.json wymusza jednorazowy rescan przy pierwszym starcie
        if (_items.Count == 0 || _enemies.Count == 0 || _portalsByName.Count == 0 || _marks.Count == 0 || _lootOnlyItems.Count == 0)
        {
            TryAutoExtractFromInstalledGame();
        }
    }

    public IReadOnlyCollection<ItemMetadata> GetAllKnownItems() => _items.Values;
    public IReadOnlyCollection<EnemyMetadata> GetAllKnownBosses() => _enemies.Values.Where(e => e.IsBoss).ToList();
    public IReadOnlyCollection<MarkMetadata> GetAllKnownMarks() => _marks.Values;
    public IReadOnlyCollection<PortalMetadata> GetAllKnownPortals() => _portalsByName.Values;
    public IReadOnlyCollection<string> GetAllKnownDungeons() => _portalsByName.Keys.ToList();

    public bool TryResolvePlayerClass(int objectType, out RotmgClass rotmgClass) =>
        _playerClasses.TryGetValue(objectType, out rotmgClass);

    public bool TryResolveEnemy(int objectType, out EnemyMetadata enemy) =>
        _enemies.TryGetValue(objectType, out enemy!);

    public bool TryResolveMark(int itemId, out MarkMetadata mark) =>
        _marks.TryGetValue(itemId, out mark!);

    public BagType ResolveBagType(int containerObjectType) =>
        _bagContainers.GetValueOrDefault(containerObjectType, BagType.BrownBag); // wcześniej domyślnie White
    
    // Kolor worka wynikający z <BagType> przedmiotu w XML
    public BagType ResolveBagTypeFromItem(int xmlBagType, BagType fallback) => xmlBagType switch
    {
        0 => BagType.BrownBag,
        1 => BagType.PinkBag,
        2 => BagType.PurpleBag,
        3 => BagType.EggBag,
        4 => BagType.CyanBag,
        5 => BagType.BlueBag,
        6 => BagType.WhiteBag,
        7 => BagType.RedBag,
        8 => BagType.OrangeBag,
        _ => fallback
    };

    // Zwraca false dla przedmiotow, ktorych nie ma w katalogu (nie zgadujemy ich jakosci).
    // Szuka w kolekcji oraz w katalogu "loot-only" (potki, jajka, smieci - tylko nazwy do Loot History).
    public bool TryGetItem(int itemId, out ItemMetadata item) =>
        _items.TryGetValue(itemId, out item!) || _lootOnlyItems.TryGetValue(itemId, out item!);

    public ItemMetadata ResolveItem(int itemId)
    {
        if (TryGetItem(itemId, out var meta))
            return meta;

        return new ItemMetadata(itemId, $"Unknown Item (0x{itemId:X4})", DropType.Other, -1);
    }
    
    // Przedmioty spoza kolekcji (potki, jajka, śmieci) - tylko nazwy do Loot History, nie trafiają do GetAllKnownItems()
    private void RegisterLootOnlyItem(int objectType, string displayName, XElement obj, string texFile, int texIndex)
    {
        int.TryParse(obj.Element("SlotType")?.Value, out int slotType);
        int.TryParse(obj.Element("BagType")?.Value, out int xmlBagType);

        _lootOnlyItems[objectType] = new ItemMetadata(
            objectType,
            displayName,
            DropType.Other,
            -1,
            false,
            xmlBagType,
            slotType,
            string.Empty,
            texFile,
            texIndex);
    }

    public PortalMetadata? ResolvePortalByDungeonName(string dungeonName)
    {
        if (string.IsNullOrWhiteSpace(dungeonName)) return null;
        string normalized = dungeonName.Trim().Replace('’', '\'');
        return _portalsByName.GetValueOrDefault(normalized);
    }

    public double ResolveDungeonGraveDifficulty(string dungeonName, double mapInfoDifficulty = 0)
    {
        if (!string.IsNullOrWhiteSpace(dungeonName))
        {
            string normalized = dungeonName.Trim().Replace('’', '\'');
            if (_portalsByName.TryGetValue(normalized, out var portal) && portal.GraveDifficulty > 0)
            {
                return Math.Max(portal.GraveDifficulty, mapInfoDifficulty);
            }
        }

        return mapInfoDifficulty > 0 ? mapInfoDifficulty : 3.0;
    }

    public DungeonDifficultyTier ResolveDungeonDifficulty(string dungeonName, double mapInfoDifficulty = 0)
    {
        double graves = ResolveDungeonGraveDifficulty(dungeonName, mapInfoDifficulty);
        return RarityScoreEngine.GraveCountToTier(graves);
    }

    public int TryAutoExtractFromInstalledGame(string? customResourcesAssetsPath = null)
    {
        string? assetsPath = customResourcesAssetsPath ?? FindRotmgResourcesAssetsPath();
        if (string.IsNullOrWhiteSpace(assetsPath) || !File.Exists(assetsPath))
        {
            ExtractionStatus = "RotMG installation not found";
            return 0;
        }

        try
        {
            byte[] rawBytes = File.ReadAllBytes(assetsPath);
            byte[] startPattern = "<Objects>"u8.ToArray();
            byte[] endPattern = "</Objects>"u8.ToArray();

            var xmlDocuments = new List<XDocument>();
            int searchIndex = 0;

            while (searchIndex < rawBytes.Length)
            {
                int startIdx = rawBytes.AsSpan(searchIndex).IndexOf(startPattern);
                if (startIdx < 0) break;
                startIdx += searchIndex;

                int endIdx = rawBytes.AsSpan(startIdx).IndexOf(endPattern);
                if (endIdx < 0) break;
                endIdx += startIdx + endPattern.Length;

                int length = endIdx - startIdx;
                if (length is > 20 and < 25_000_000)
                {
                    string xmlChunk = Encoding.UTF8.GetString(rawBytes, startIdx, length);
                    string sanitized = Regex.Replace(
                        xmlChunk,
                        @"&(?!(?:amp|lt|gt|apos|quot|#\d+|#x[0-9a-fA-F]+);)",
                        "&amp;");

                    try
                    {
                        var doc = XDocument.Parse(sanitized);
                        if (doc.Root != null)
                            xmlDocuments.Add(doc);
                    }
                    catch { }
                }

                searchIndex = endIdx;
            }

            lock (_catalogLock)
            {
                _items.Clear();
                _enemies.Clear();
                _portalsByName.Clear();
                _marks.Clear();
                _bossReasons.Clear();

                var forgeMap = new Dictionary<int, ForgeXmlEntry>();
                var xmlGraveMap = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

                foreach (var doc in xmlDocuments)
                {
                    foreach (var fp in doc.Root!.Elements("ForgeProperties"))
                    {
                        string? typeHex = fp.Attribute("type")?.Value;
                        if (string.IsNullOrWhiteSpace(typeHex) || !TryParseHexOrDec(typeHex, out int itemType))
                            continue;

                        var dismantle = fp.Element("DismantleRequirements");
                        int leg = 0, rare = 0, com = 0;
                        if (dismantle != null)
                        {
                            int.TryParse(dismantle.Attribute("legendary")?.Value, out leg);
                            int.TryParse(dismantle.Attribute("rare")?.Value, out rare);
                            int.TryParse(dismantle.Attribute("common")?.Value, out com);
                        }

                        bool isRealmWhite = fp.Elements("RequireDismantleWithLabel")
                            .Any(e => string.Equals(e.Value.Trim(), "REALMWHITE", StringComparison.OrdinalIgnoreCase));

                        forgeMap[itemType] = new ForgeXmlEntry(leg, rare, com, isRealmWhite);
                    }

                    foreach (var obj in doc.Root.Elements("Object"))
                    {
                        foreach (var cd in obj.Elements("CommonDungeon"))
                        {
                            string dungName = cd.Value.Trim().Replace('’', '\'');
                            string? diffAttr = cd.Attribute("diff")?.Value;
                            if (!string.IsNullOrWhiteSpace(dungName) &&
                                !string.IsNullOrWhiteSpace(diffAttr) &&
                                double.TryParse(diffAttr, NumberStyles.Float, CultureInfo.InvariantCulture, out double cdGraves) &&
                                cdGraves > 0)
                            {
                                if (!xmlGraveMap.TryGetValue(dungName, out double prev) || cdGraves > prev)
                                    xmlGraveMap[dungName] = cdGraves;
                            }
                        }

                        if (obj.Element("Key") != null)
                        {
                            string? diffStr = obj.Element("DungeonDifficulty")?.Value;
                            if (!string.IsNullOrWhiteSpace(diffStr) &&
                                double.TryParse(diffStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double keyGraves) &&
                                keyGraves > 0)
                            {
                                var createPortalNode = obj.Elements("Activate")
                                    .FirstOrDefault(a => string.Equals(a.Value.Trim(), "CreatePortal", StringComparison.OrdinalIgnoreCase));

                                string? targetPortalId = createPortalNode?.Attribute("id")?.Value;
                                if (!string.IsNullOrWhiteSpace(targetPortalId))
                                {
                                    xmlGraveMap[targetPortalId.Trim()] = keyGraves;
                                    string cleanFromPortal = targetPortalId.Replace(" Portal", "", StringComparison.OrdinalIgnoreCase).Trim();
                                    xmlGraveMap[cleanFromPortal] = keyGraves;
                                }

                                string? keyId = obj.Attribute("id")?.Value;
                                if (!string.IsNullOrWhiteSpace(keyId))
                                {
                                    string cleanFromKey = keyId
                                        .Replace(" Solo Key", "", StringComparison.OrdinalIgnoreCase)
                                        .Replace(" Key", "", StringComparison.OrdinalIgnoreCase)
                                        .Trim();
                                    xmlGraveMap[cleanFromKey] = keyGraves;
                                }
                            }
                        }
                    }
                }

                foreach (var doc in xmlDocuments)
                {
                    foreach (var obj in doc.Root!.Elements("Object"))
                    {
                        ParseGameObjectElement(obj, forgeMap, xmlGraveMap);
                    }
                }

                if (_items.Count > 0)
                {
                    SaveCachedCatalogs();
                    int bossCount = _enemies.Values.Count(e => e.IsBoss);
                    int shinyCount = _items.Values.Count(i => i.IsShiny);
                    ExtractionStatus = $"Extracted {_items.Count} items ({shinyCount} shinies), {bossCount} bosses, {_portalsByName.Count} portals & {_marks.Count} marks from XML";
                }
            }

            return _items.Count;
        }
        catch (Exception ex)
        {
            ExtractionStatus = $"Extraction error: {ex.Message}";
            return 0;
        }
    }

        private void ParseGameObjectElement(
    XElement obj,
    Dictionary<int, ForgeXmlEntry> forgeMap,
    Dictionary<string, double> xmlGraveMap)
{
    string? typeHex = obj.Attribute("type")?.Value;
    string? idName = obj.Attribute("id")?.Value?.Trim();
    if (string.IsNullOrWhiteSpace(typeHex) || string.IsNullOrWhiteSpace(idName))
        return;

    if (!TryParseHexOrDec(typeHex, out int objectType))
        return;

    string displayName = (obj.Element("DisplayId")?.Value ?? idName).Trim();
    (string texFile, int texIndex) = ExtractTextureInfo(obj);

    // 1. Klasy postaci (<Player />)
    if (obj.Element("Player") != null && Enum.TryParse<RotmgClass>(idName, true, out var parsedClass))
    {
        _playerClasses[objectType] = parsedClass;
        return;
    }

    // 2. Portale dungeonów (<DungeonPortal />)
    if (obj.Element("DungeonPortal") != null)
    {
        string? dungName = obj.Element("DungeonName")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(dungName))
            return;

        string cleanDung = dungName.Replace(" Portal", "", StringComparison.OrdinalIgnoreCase).Trim().Replace('’', '\'');
        if (cleanDung is "Nexus" or "Vault" or "Daily Quest Room" or "Pet Yard" or "Guild Hall" or "Cloth Bazaar")
            return;

        foreach (var excluded in ExcludedPortalKeywords)
        {
            if (idName.Contains(excluded, StringComparison.OrdinalIgnoreCase) ||
                cleanDung.Contains(excluded, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        double graves = 0;
        string? portalDiffStr = obj.Element("DungeonDifficulty")?.Value ?? obj.Element("Difficulty")?.Value;
        if (!string.IsNullOrWhiteSpace(portalDiffStr))
            double.TryParse(portalDiffStr, NumberStyles.Float, CultureInfo.InvariantCulture, out graves);

        if (graves <= 0 && xmlGraveMap.TryGetValue(idName, out double gFromId))
            graves = gFromId;
        if (graves <= 0 && xmlGraveMap.TryGetValue(cleanDung, out double gFromName))
            graves = gFromName;

        if (!_portalsByName.TryGetValue(cleanDung, out var existingPortal) || graves > existingPortal.GraveDifficulty)
        {
            _portalsByName[cleanDung] = new PortalMetadata(objectType, idName, cleanDung, graves, texFile, texIndex);
        }
        return;
    }

    // 3. Bossowie i Przeciwnicy (<Enemy />)
    if (obj.Element("Enemy") != null)
    {
        if (idName.Contains("Rehearsal", StringComparison.OrdinalIgnoreCase))
            return;

        int maxHp = 0;
        int.TryParse(obj.Element("MaxHitPoints")?.Value, out maxHp);

        string labels = obj.Element("Labels")?.Value ?? obj.Attribute("labels")?.Value ?? string.Empty;
        var enemyLabels = labels
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        bool hasHealthBar = obj.Element("HealthBarBoss") != null;
        bool excluded = enemyLabels.Contains("BEACON_GUARDIAN")
                     || enemyLabels.Contains("LEGIONQUEST")
                     || idName.StartsWith("Retro ", StringComparison.OrdinalIgnoreCase);

        string category = string.Empty;
        if (!excluded)
        {
            if (hasHealthBar && enemyLabels.Contains("VETERAN_ENCOUNTER")) category = "VeteranEncounter";
            else if (hasHealthBar && enemyLabels.Contains("ADEPT_ENCOUNTER")) category = "AdeptEncounter";
            else if (obj.Element("Oryx") != null
                     || enemyLabels.Contains("BOSSFIGHT")
                     || (hasHealthBar && enemyLabels.Contains("BOSS")))
                category = "DungeonBoss";
        }

        bool isBoss = category.Length > 0;

        if (isBoss || maxHp >= 5000)
        {
            _enemies[objectType] = new EnemyMetadata(
                objectType,
                idName,
                displayName,
                isBoss,
                maxHp,
                texFile,
                texIndex,
                category);
        }

        if (isBoss)
        {
            _bossReasons.Add($"{displayName} | 0x{objectType:X4} | hp={maxHp} | cat={category} | labels={labels}");
        }

        return;
    }

    // 4. Marki u Tinkerera (<Mark />) oraz Przedmioty (<Item />)
    if (obj.Element("Item") != null)
    {
        string labels = obj.Element("Labels")?.Value ?? string.Empty;
        var labelTokens = labels
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 4A. Mark u Tinkerera (<Mark /> lub etykieta MARK)
        if (obj.Element("Mark") != null || labelTokens.Contains("MARK"))
        {
            (string bossName, string dungeonName) = ParseMarkDropLocationFromXml(obj, displayName);
            _marks[objectType] = new MarkMetadata(objectType, displayName, bossName, dungeonName, texFile, texIndex);
            return;
        }

        // 4B. Ekwipunek i Blueprinty; reszta (potki, jajka, śmieci) trafia do katalogu loot-only
        if (!IsValidCollectibleEquipment(obj, idName, displayName, labelTokens, out int slotType, out int xmlBagType))
        {
            RegisterLootOnlyItem(objectType, displayName, obj, texFile, texIndex);
            return;
        }

        bool isShiny = labelTokens.Contains("SHINY") ||
                       (texFile.Contains("Shiny", StringComparison.OrdinalIgnoreCase) && idName.EndsWith(" Shiny", StringComparison.OrdinalIgnoreCase));

        string finalItemName = displayName;
        if (isShiny)
        {
            string baseClean = displayName.Replace(" Shiny", "", StringComparison.OrdinalIgnoreCase).Trim();
            finalItemName = baseClean.StartsWith("Shiny ", StringComparison.OrdinalIgnoreCase)
                ? baseClean
                : $"Shiny {baseClean}";
        }
        else if (labelTokens.Contains("RESKIN") ||
                 idName.StartsWith("Retro ", StringComparison.OrdinalIgnoreCase) ||
                 idName.Contains("- Midas", StringComparison.OrdinalIgnoreCase))
        {
            finalItemName = idName;
        }

        int tier = -1;
        string? tierStr = obj.Element("Tier")?.Value;
        if (!string.IsNullOrWhiteSpace(tierStr) && int.TryParse(tierStr, out int parsedTier))
            tier = parsedTier;

        forgeMap.TryGetValue(objectType, out var forgeEntry);
        string forgeTier = ResolveItemForgeTier(forgeEntry, labelTokens);
        DropType quality = DetermineDropQuality(obj, xmlBagType, tier, isShiny, forgeEntry, forgeTier, labelTokens, finalItemName);

        _items[objectType] = new ItemMetadata(
            objectType,
            finalItemName,
            quality,
            tier,
            isShiny,
            xmlBagType,
            slotType,
            forgeTier,
            texFile,
            texIndex);
    }
}

    private static (string BossName, string DungeonName) ParseMarkDropLocationFromXml(XElement markObj, string markName)
    {
        var dropLocationNode = markObj.Element("ExtraTooltipData")?
            .Elements("EffectInfo")
            .FirstOrDefault(e => string.Equals(e.Attribute("name")?.Value, "Drop location", StringComparison.OrdinalIgnoreCase));

        string? desc = dropLocationNode?.Attribute("description")?.Value;
        if (!string.IsNullOrWhiteSpace(desc))
        {
            var match = Regex.Match(desc, @"defeating\s+(?<boss>.+?)\s+in\s+(?:the\s+)?(?<dungeon>.+?)\.?$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string boss = match.Groups["boss"].Value.Trim();
                string dungeon = match.Groups["dungeon"].Value.Trim().Replace('’', '\'');
                return (boss, dungeon);
            }
        }

        string fallbackBoss = markName.Replace("Mark of ", "", StringComparison.OrdinalIgnoreCase).Trim();
        return (fallbackBoss, "Unknown Dungeon");
    }

    private static string ResolveItemForgeTier(ForgeXmlEntry? forge, HashSet<string> labelTokens)
    {
        if (forge != null)
        {
            if (forge.LegendaryDismantle >= 15) return "S";
            if (forge.RareDismantle >= 30) return "A";
            if (forge.RareDismantle >= 15) return "B";
            if (forge.RareDismantle >= 5) return "C";
            if (forge.CommonDismantle >= 15) return "D";
        }

        if (labelTokens.Contains("POWERTIER_SS") || labelTokens.Contains("POWERTIER_S")) return "S";
        if (labelTokens.Contains("POWERTIER_A")) return "A";
        if (labelTokens.Contains("POWERTIER_B")) return "B";
        if (labelTokens.Contains("POWERTIER_C")) return "C";
        if (labelTokens.Contains("POWERTIER_D")) return "D";

        return string.Empty;
    }

    private static DropType DetermineDropQuality(
        XElement obj,
        int xmlBagType,
        int tier,
        bool isShiny,
        ForgeXmlEntry? forge,
        string forgeTier,
        HashSet<string> labelTokens,
        string name)
    {
        if (isShiny) return DropType.Shiny;

        if (name.Contains("Schematic", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Blueprint", StringComparison.OrdinalIgnoreCase))
            return DropType.BPs;

        if (xmlBagType == 8 || labelTokens.Contains("TAB_ST") || labelTokens.Contains("ST") || obj.Attribute("setName") != null)
            return DropType.ST;

        if (xmlBagType == 6)
        {
            // Usunięto sztuczne wymuszenie REALMWHITE -> Event White (pozostaje do decyzji sniffera na podstawie mapy)
            if (forgeTier == "S")
                return DropType.LegendaryWhite;

            return DropType.RegularWhite;
        }

        if (tier >= 13)
            return DropType.Tops;

        if (tier > 0)
            return DropType.Tiered;

        return DropType.UT;
    }

    private static (string File, int Index) ExtractTextureInfo(XElement obj)
    {
        var texNode = obj.Element("Texture") ?? obj.Element("AnimatedTexture");
        if (texNode == null) return (string.Empty, 0);

        string file = texNode.Element("File")?.Value ?? string.Empty;
        string? indexHex = texNode.Element("Index")?.Value;
        int index = 0;
        if (!string.IsNullOrWhiteSpace(indexHex))
            TryParseHexOrDec(indexHex, out index);

        return (file, index);
    }

    private static bool IsValidCollectibleEquipment(
        XElement obj,
        string idName,
        string displayName,
        HashSet<string> labelTokens,
        out int slotType,
        out int xmlBagType)
    {
        slotType = 0;
        xmlBagType = 0;

        // Odrzucamy paczki schematiców w stosach (np. "Schematic x5")
        if (Regex.IsMatch(displayName, @"Schematic\s*x\d+", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(idName, @"Schematic\s*x\d+", RegexOptions.IgnoreCase))
        {
            return false;
        }

        bool isBlueprintOrSchematic = displayName.Contains("Blueprint", StringComparison.OrdinalIgnoreCase) ||
                                      displayName.Contains("Schematic", StringComparison.OrdinalIgnoreCase);

        if (!int.TryParse(obj.Element("SlotType")?.Value, out slotType))
            return false;

        // Każdy prawdziwy przedmiot w grze musi mieć <BagType> > 0 oraz niepusty <Description>
        if (!int.TryParse(obj.Element("BagType")?.Value, out xmlBagType) || xmlBagType <= 0)
            return false;

        if (string.IsNullOrWhiteSpace(obj.Element("Description")?.Value))
            return false;

        // Blueprinty ignorują blokadę na Consumable, ale pozostałe przedmioty jej podlegają
        if (obj.Element("Quantity") != null ||
            (!isBlueprintOrSchematic && obj.Element("Consumable") != null) ||
            obj.Element("Key") != null ||
            obj.Element("Mark") != null ||
            obj.Element("PetFood") != null ||
            obj.Element("Treasure") != null)
        {
            return false;
        }

        if (Regex.IsMatch(displayName, @"^x\d+\s", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(idName, @"^x\d+\s", RegexOptions.IgnoreCase))
        {
            return false;
        }

        foreach (var keyword in ExcludedItemKeywords)
        {
            if (idName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                displayName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Blueprinty pomijają etykietę CONSUMABLE
        if (labelTokens.Contains("PET") || 
            (!isBlueprintOrSchematic && labelTokens.Contains("CONSUMABLE")) ||
            labelTokens.Contains("TOKEN") || 
            labelTokens.Contains("MARK") || 
            labelTokens.Contains("KEY"))
        {
            return false;
        }

        if (!isBlueprintOrSchematic && !ValidEquipmentSlotTypes.Contains(slotType))
            return false;

        return true;
    }

        private static string? FindRotmgResourcesAssetsPath()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var candidateRealmRoots = new List<string>();

        // 1. Katalog uruchomionej gry (Windows: "RotMG Exalt.exe") - najpewniejsze źródło
        try
        {
            foreach (var proc in System.Diagnostics.Process.GetProcessesByName("RotMG Exalt"))
            {
                try
                {
                    string? dir = Path.GetDirectoryName(proc.MainModule?.FileName);
                    if (!string.IsNullOrWhiteSpace(dir))
                        candidateRealmRoots.Add(dir);
                }
                catch { }
                finally { proc.Dispose(); }
            }
        }
        catch { }

        // 2. Standardowe lokalizacje
        candidateRealmRoots.AddRange(
        [
            Path.Combine(localAppData, "RealmOfTheMadGod"),
            Path.Combine(home, ".local", "share", "RealmOfTheMadGod"),
            Path.Combine(home, "Documents", "RealmOfTheMadGod"),
            Path.Combine(docs, "RealmOfTheMadGod"),
            Path.Combine(home, "OneDrive", "Documents", "RealmOfTheMadGod"),
            Path.Combine(home, "OneDrive", "Dokumenty", "RealmOfTheMadGod"),
            Path.Combine(home, ".steam", "steam", "steamapps", "compatdata", "200210", "pfx", "drive_c", "users", "steamuser", "Documents", "RealmOfTheMadGod"),
            Path.Combine(home, ".local", "share", "Steam", "steamapps", "compatdata", "200210", "pfx", "drive_c", "users", "steamuser", "Documents", "RealmOfTheMadGod")
        ]);

        foreach (var root in candidateRealmRoots.Distinct())
        {
            if (!Directory.Exists(root)) continue;

            string prodDir = Path.Combine(root, "Production");
            string searchDir = Directory.Exists(prodDir) ? prodDir : root;

            try
            {
                var found = Directory.EnumerateFiles(searchDir, "resources.assets", SearchOption.AllDirectories)
                    .FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(found))
                    return found;
            }
            catch { }
        }

        return null;
    }

    private static bool TryParseHexOrDec(string raw, out int result)
    {
        raw = raw.Trim();
        if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(raw[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    private void LoadCachedCatalogs()
{
    try
    {
        if (File.Exists(_itemCatalogPath))
        {
            var loadedItems = JsonSerializer.Deserialize<List<ItemMetadata>>(File.ReadAllText(_itemCatalogPath));
            if (loadedItems != null)
                foreach (var item in loadedItems) _items[item.ItemId] = item;
        }

        if (File.Exists(_lootOnlyCatalogPath))
        {
            var loadedLootOnly = JsonSerializer.Deserialize<List<ItemMetadata>>(File.ReadAllText(_lootOnlyCatalogPath));
            if (loadedLootOnly != null)
                foreach (var item in loadedLootOnly) _lootOnlyItems[item.ItemId] = item;
        }

        if (File.Exists(_enemyCatalogPath))
        {
            var loadedEnemies = JsonSerializer.Deserialize<List<EnemyMetadata>>(File.ReadAllText(_enemyCatalogPath));
            if (loadedEnemies != null)
                foreach (var enemy in loadedEnemies) _enemies[enemy.ObjectType] = enemy;
        }

        if (File.Exists(_portalCatalogPath))
        {
            var loadedPortals = JsonSerializer.Deserialize<List<PortalMetadata>>(File.ReadAllText(_portalCatalogPath));
            if (loadedPortals != null)
                foreach (var portal in loadedPortals) _portalsByName[portal.DungeonName] = portal;
        }

        if (File.Exists(_markCatalogPath))
        {
            var loadedMarks = JsonSerializer.Deserialize<List<MarkMetadata>>(File.ReadAllText(_markCatalogPath));
            if (loadedMarks != null)
                foreach (var mark in loadedMarks) _marks[mark.ItemId] = mark;
        }

        if (_items.Count > 0)
        {
            int bossCount = _enemies.Values.Count(e => e.IsBoss);
            int shinyCount = _items.Values.Count(i => i.IsShiny);
            ExtractionStatus = $"Loaded {_items.Count} items ({shinyCount} shinies), {bossCount} bosses, {_portalsByName.Count} portals & {_marks.Count} marks";
        }
    }
    catch { }
}

    private void SaveCachedCatalogs()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(_itemCatalogPath, JsonSerializer.Serialize(_items.Values.ToList(), options));
            File.WriteAllText(_lootOnlyCatalogPath, JsonSerializer.Serialize(_lootOnlyItems.Values.ToList(), options));
            File.WriteAllText(_enemyCatalogPath, JsonSerializer.Serialize(_enemies.Values.ToList(), options));
            File.WriteAllText(_portalCatalogPath, JsonSerializer.Serialize(_portalsByName.Values.ToList(), options));
            File.WriteAllText(_markCatalogPath, JsonSerializer.Serialize(_marks.Values.ToList(), options));
            File.WriteAllLines(Path.Combine(_appDataDir, "boss_reasons.txt"), _bossReasons.OrderBy(x => x));
        }
        catch { }
    }
}