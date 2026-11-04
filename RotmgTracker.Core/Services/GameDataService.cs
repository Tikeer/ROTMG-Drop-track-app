using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using RotmgTrackerV1.Core.Enums;

namespace RotmgTrackerV1.Core.Services;

public record ItemMetadata(
    int ItemId,
    string Name,
    DropType Quality,
    int Tier,
    string OriginDungeon,
    bool IsShiny = false,
    int BagTypeId = 0);

public class GameDataService
{
    private readonly string _appDataDir;
    private readonly string _catalogFilePath;
    private readonly object _catalogLock = new();

    private readonly Dictionary<int, ItemMetadata> _items = new();
    private readonly Dictionary<int, BagType> _bagContainers = new()
    {
        { 0x0500, BagType.BrownBag },
        { 0x0506, BagType.PurpleBag },
        { 0x0507, BagType.CyanBag },
        { 0x0508, BagType.BlueBag },
        { 0x0509, BagType.WhiteBag },
        { 0x050B, BagType.WhiteBag },
        { 0x050E, BagType.OrangeBag },
        { 0x050F, BagType.RedBag }
    };

    // Pełna tabela wag trudności dungeonów
    private static readonly Dictionary<string, DungeonDifficultyTier> DungeonTiers = new(StringComparer.OrdinalIgnoreCase)
    {
        // Tier 6: Pinnacle Endgame (3.0x)
        { "The Shatters", DungeonDifficultyTier.Tier6_PinnacleEndgame },
        { "Moonlight Village", DungeonDifficultyTier.Tier6_PinnacleEndgame },

        // Tier 5: High Exaltation (2.5x)
        { "Oryx's Sanctuary", DungeonDifficultyTier.Tier5_HighExalt },
        { "Advanced Kogbold Steamworks", DungeonDifficultyTier.Tier5_HighExalt },
        { "Advanced Nest", DungeonDifficultyTier.Tier5_HighExalt },
        { "Plagued Nest", DungeonDifficultyTier.Tier5_HighExalt },

        // Tier 4: Standard Exaltation & Hard Event Dungeons (1.8x)
        { "The Void", DungeonDifficultyTier.Tier4_ExaltAndHardEvent },
        { "Lost Halls", DungeonDifficultyTier.Tier4_ExaltAndHardEvent },
        { "Cultist Hideout", DungeonDifficultyTier.Tier4_ExaltAndHardEvent },
        { "Fungal Cavern", DungeonDifficultyTier.Tier4_ExaltAndHardEvent },
        { "Crystal Cavern", DungeonDifficultyTier.Tier4_ExaltAndHardEvent },
        { "Kogbold Steamworks", DungeonDifficultyTier.Tier4_ExaltAndHardEvent },
        { "Spectral Penitentiary", DungeonDifficultyTier.Tier4_ExaltAndHardEvent },
        { "Ice Citadel", DungeonDifficultyTier.Tier4_ExaltAndHardEvent },
        { "The Nest", DungeonDifficultyTier.Tier4_ExaltAndHardEvent },
        { "Neo Katalund", DungeonDifficultyTier.Tier4_ExaltAndHardEvent },
        { "Neo Malogia", DungeonDifficultyTier.Tier4_ExaltAndHardEvent },
        { "Neo Untaris", DungeonDifficultyTier.Tier4_ExaltAndHardEvent },
        { "Neo Forax", DungeonDifficultyTier.Tier4_ExaltAndHardEvent },
        { "Infernal Abyss of Demons", DungeonDifficultyTier.Tier4_ExaltAndHardEvent },
        { "Heroic Undead Lair", DungeonDifficultyTier.Tier4_ExaltAndHardEvent },
        { "Hidden Interregnum", DungeonDifficultyTier.Tier4_ExaltAndHardEvent },

        // Tier 3: Epic Dungeons & Realm Events (1.4x)
        { "Davy Jones' Locker", DungeonDifficultyTier.Tier3_EpicAndRealm },
        { "Ice Cave", DungeonDifficultyTier.Tier3_EpicAndRealm },
        { "Parasite Chambers", DungeonDifficultyTier.Tier3_EpicAndRealm },
        { "Tomb of the Ancients", DungeonDifficultyTier.Tier3_EpicAndRealm },
        { "Ocean Trench", DungeonDifficultyTier.Tier3_EpicAndRealm },
        { "Realm Event", DungeonDifficultyTier.Tier3_EpicAndRealm },
        { "Oryx's Chamber", DungeonDifficultyTier.Tier3_EpicAndRealm },
        { "Wine Cellar", DungeonDifficultyTier.Tier3_EpicAndRealm },
        { "Oryx's Castle", DungeonDifficultyTier.Tier3_EpicAndRealm },
        { "The Crawling Depths", DungeonDifficultyTier.Tier3_EpicAndRealm },
        { "Woodland Labyrinth", DungeonDifficultyTier.Tier3_EpicAndRealm },
        { "Deadwater Docks", DungeonDifficultyTier.Tier3_EpicAndRealm },
        { "Puppet Master's Encore", DungeonDifficultyTier.Tier3_EpicAndRealm },
        { "Cnidarian Reef", DungeonDifficultyTier.Tier3_EpicAndRealm },
        { "The Tavern", DungeonDifficultyTier.Tier3_EpicAndRealm },
        { "Sulfurous Wetlands", DungeonDifficultyTier.Tier3_EpicAndRealm },
        { "Mountain Temple", DungeonDifficultyTier.Tier3_EpicAndRealm },
        { "Lair of Draconis", DungeonDifficultyTier.Tier3_EpicAndRealm },
        { "The Third Dimension", DungeonDifficultyTier.Tier3_EpicAndRealm },
        { "Lair of Shaitan", DungeonDifficultyTier.Tier3_EpicAndRealm },
        { "Secluded Thicket", DungeonDifficultyTier.Tier3_EpicAndRealm },
        { "High Tech Terror", DungeonDifficultyTier.Tier3_EpicAndRealm },
        { "Legacy Heroic Undead Lair", DungeonDifficultyTier.Tier3_EpicAndRealm },
        { "Legacy Heroic Abyss of Demons", DungeonDifficultyTier.Tier3_EpicAndRealm },
        { "Mad God Mayhem", DungeonDifficultyTier.Tier3_EpicAndRealm },
        { "Battle for the Nexus", DungeonDifficultyTier.Tier3_EpicAndRealm },
        { "Belladonna's Garden", DungeonDifficultyTier.Tier3_EpicAndRealm },

        // Tier 2: Godlands (1.0x)
        { "Undead Lair", DungeonDifficultyTier.Tier2_Godlands },
        { "Abyss of Demons", DungeonDifficultyTier.Tier2_Godlands },
        { "Toxic Sewers", DungeonDifficultyTier.Tier2_Godlands },
        { "Puppet Master's Theatre", DungeonDifficultyTier.Tier2_Godlands },
        { "Mad Lab", DungeonDifficultyTier.Tier2_Godlands },
        { "Cave of a Thousand Treasures", DungeonDifficultyTier.Tier2_Godlands },
        { "Ancient Ruins", DungeonDifficultyTier.Tier2_Godlands },
        { "Magic Woods", DungeonDifficultyTier.Tier2_Godlands },
        { "Sprite World", DungeonDifficultyTier.Tier2_Godlands },
        { "Candyland Hunting Grounds", DungeonDifficultyTier.Tier2_Godlands },
        { "Cursed Library", DungeonDifficultyTier.Tier2_Godlands },
        { "Manor of the Immortals", DungeonDifficultyTier.Tier2_Godlands },
        { "Haunted Cemetery", DungeonDifficultyTier.Tier2_Godlands },
        { "The Machine", DungeonDifficultyTier.Tier2_Godlands },
        { "The Inner Workings", DungeonDifficultyTier.Tier2_Godlands },
        { "Malogia", DungeonDifficultyTier.Tier2_Godlands },
        { "Untaris", DungeonDifficultyTier.Tier2_Godlands },
        { "Katalund", DungeonDifficultyTier.Tier2_Godlands },
        { "Forax", DungeonDifficultyTier.Tier2_Godlands },
        { "Rainbow Road", DungeonDifficultyTier.Tier2_Godlands },
        { "Santa's Workshop", DungeonDifficultyTier.Tier2_Godlands },

        // Tier 1: Early (0.8x)
        { "Snake Pit", DungeonDifficultyTier.Tier1_Early },
        { "Pirate Cave", DungeonDifficultyTier.Tier1_Early },
        { "Forest Maze", DungeonDifficultyTier.Tier1_Early },
        { "Spider Den", DungeonDifficultyTier.Tier1_Early },
        { "Forbidden Jungle", DungeonDifficultyTier.Tier1_Early },
        { "The Hive", DungeonDifficultyTier.Tier1_Early },
        { "Beachzone", DungeonDifficultyTier.Tier1_Early }
    };

    public string ExtractionStatus { get; private set; } = "Not initialized";

    public GameDataService()
    {
        _appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RotmgTracker");
        Directory.CreateDirectory(_appDataDir);
        _catalogFilePath = Path.Combine(_appDataDir, "item_catalog.json");

        LoadCachedCatalog();

        // Jeśli katalog jest pusty (pierwszy start), automatycznie wyciągnij dane z zainstalowanego RotMG Exalt
        if (_items.Count == 0)
        {
            TryAutoExtractFromInstalledGame();
        }
    }

    public IReadOnlyCollection<ItemMetadata> GetAllKnownItems() => _items.Values;
    public IReadOnlyCollection<string> GetAllKnownDungeons() => DungeonTiers.Keys;

    public BagType ResolveBagType(int containerObjectType) =>
        _bagContainers.GetValueOrDefault(containerObjectType, BagType.WhiteBag);

    public DungeonDifficultyTier ResolveDungeonDifficulty(string dungeonName)
    {
        if (string.IsNullOrWhiteSpace(dungeonName))
            return DungeonDifficultyTier.Tier2_Godlands;

        string normalized = dungeonName.Trim().Replace('’', '\'');
        if (DungeonTiers.TryGetValue(normalized, out var tier))
            return tier;

        if (normalized.Contains("Shatters", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Moonlight", StringComparison.OrdinalIgnoreCase))
            return DungeonDifficultyTier.Tier6_PinnacleEndgame;

        if (normalized.Contains("Sanctuary", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Advanced", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Plagued", StringComparison.OrdinalIgnoreCase))
            return DungeonDifficultyTier.Tier5_HighExalt;

        return DungeonDifficultyTier.Tier2_Godlands;
    }

    public ItemMetadata ResolveItem(int itemId)
    {
        if (_items.TryGetValue(itemId, out var meta))
            return meta;

        return new ItemMetadata(itemId, $"Unknown Item (0x{itemId:X4})", DropType.RegularWhite, -1, "RotMG");
    }
    
    public int TryAutoExtractFromInstalledGame(string? customResourcesAssetsPath = null)
    {
        string? assetsPath = customResourcesAssetsPath ?? FindRotmgResourcesAssetsPath();
        if (string.IsNullOrWhiteSpace(assetsPath) || !File.Exists(assetsPath))
        {
            ExtractionStatus = "RotMG installation not found in Documents/RealmOfTheMadGod/Production";
            return 0;
        }

        try
        {
            byte[] rawBytes = File.ReadAllBytes(assetsPath);
            byte[] startPattern = "<Objects>"u8.ToArray();
            byte[] endPattern = "</Objects>"u8.ToArray();

            int totalImported = 0;
            int searchIndex = 0;

            lock (_catalogLock)
            {
                while (searchIndex < rawBytes.Length)
                {
                    int startIdx = rawBytes.AsSpan(searchIndex).IndexOf(startPattern);
                    if (startIdx < 0) break;
                    startIdx += searchIndex;

                    int endIdx = rawBytes.AsSpan(startIdx).IndexOf(endPattern);
                    if (endIdx < 0) break;
                    endIdx += startIdx + endPattern.Length;

                    int length = endIdx - startIdx;
                    // Pojedynczy plik XML w resources.assets ma zazwyczaj od kilku KB do ~15 MB
                    if (length is > 20 and < 25_000_000)
                    {
                        string xmlChunk = Encoding.UTF8.GetString(rawBytes, startIdx, length);
                        totalImported += ParseObjectsXmlContent(xmlChunk);
                    }

                    searchIndex = endIdx;
                }

                if (totalImported > 0)
                {
                    SaveCachedCatalog();
                    ExtractionStatus = $"Loaded {_items.Count} items directly from RotMG resources.assets";
                }
            }

            return totalImported;
        }
        catch (Exception ex)
        {
            ExtractionStatus = $"Extraction error: {ex.Message}";
            return 0;
        }
    }

    private static string? FindRotmgResourcesAssetsPath()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        // Wszystkie lokalizacje głównego folderu gry na macOS, Windows i Linux (w tym Steam Proton AppID 200210)
        string[] candidateRealmRoots =
        [
            Path.Combine(home, ".local", "share", "RealmOfTheMadGod"),
            Path.Combine(home, "Documents", "RealmOfTheMadGod"),
            Path.Combine(docs, "RealmOfTheMadGod"),
            Path.Combine(home, "OneDrive", "Documents", "RealmOfTheMadGod"),
            Path.Combine(home, "OneDrive", "Dokumenty", "RealmOfTheMadGod"),
            Path.Combine(home, ".steam", "steam", "steamapps", "compatdata", "200210", "pfx", "drive_c", "users", "steamuser", "Documents", "RealmOfTheMadGod"),
            Path.Combine(home, ".local", "share", "Steam", "steamapps", "compatdata", "200210", "pfx", "drive_c", "users", "steamuser", "Documents", "RealmOfTheMadGod"),
            Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam", "steamapps", "compatdata", "200210", "pfx", "drive_c", "users", "steamuser", "Documents", "RealmOfTheMadGod")
        ];

        foreach (var root in candidateRealmRoots.Distinct())
        {
            if (!Directory.Exists(root)) continue;

            // Szukamy najpierw w Production, a potem w całym folderze RealmOfTheMadGod
            string prodDir = Path.Combine(root, "Production");
            string searchDir = Directory.Exists(prodDir) ? prodDir : root;

            try
            {
                var found = Directory.EnumerateFiles(searchDir, "resources.assets", SearchOption.AllDirectories)
                    .FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(found))
                    return found;
            }
            catch
            {
                // Ignoruj foldery bez uprawnień odczytu
            }
        }

        return null;
    }

    private int ParseObjectsXmlContent(string xmlContent)
    {
        int count = 0;

        // Naprawa niezakodowanych znaków '&' w opisach przedmiotów DECA, które psują standardowy parser XML
        string sanitizedXml = System.Text.RegularExpressions.Regex.Replace(
            xmlContent,
            @"&(?!(?:amp|lt|gt|apos|quot|#\d+|#x[0-9a-fA-F]+);)",
            "&amp;");

        try
        {
            var doc = XDocument.Parse(sanitizedXml);
            if (doc.Root == null) return 0;

            foreach (var obj in doc.Root.Elements("Object"))
            {
                string? typeHex = obj.Attribute("type")?.Value;
                string? idName = obj.Attribute("id")?.Value;
                if (string.IsNullOrWhiteSpace(typeHex) || string.IsNullOrWhiteSpace(idName))
                    continue;

                if (!TryParseHexOrDec(typeHex, out int objectType))
                    continue;

                if (obj.Element("Item") != null)
                {
                    string displayName = obj.Element("DisplayId")?.Value ?? idName;
                    bool isShiny = obj.Element("Shiny") != null ||
                                   idName.Contains("Shiny", StringComparison.OrdinalIgnoreCase) ||
                                   displayName.Contains("Shiny", StringComparison.OrdinalIgnoreCase);

                    int tier = -1;
                    string? tierStr = obj.Element("Tier")?.Value;
                    if (!string.IsNullOrWhiteSpace(tierStr) && int.TryParse(tierStr, out int parsedTier))
                        tier = parsedTier;

                    int xmlBagType = 0;
                    int.TryParse(obj.Element("BagType")?.Value, out xmlBagType);

                    string labels = obj.Element("Labels")?.Value ?? string.Empty;
                    DropType quality = DetermineDropQuality(xmlBagType, tier, isShiny, labels, displayName);

                    _items[objectType] = new ItemMetadata(objectType, displayName, quality, tier, "RotMG", isShiny, xmlBagType);
                    count++;
                }
            }
        }
        catch
        {
            // Ignoruj bloki binarne niebędące poprawnym XML-em
        }

        return count;
    }

    private static DropType DetermineDropQuality(int xmlBagType, int tier, bool isShiny, string labels, string name)
    {
        if (isShiny) return DropType.Shiny;
        if (name.Contains("Schematic", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Blueprint", StringComparison.OrdinalIgnoreCase))
            return DropType.BPs;

        if (xmlBagType == 8 || labels.Contains("ST", StringComparison.OrdinalIgnoreCase))
            return DropType.ST;

        if (xmlBagType == 6)
            return DropType.RegularWhite;

        if (tier >= 13)
            return DropType.Tops;

        if (tier > 0)
            return DropType.Tiered;

        return DropType.UT;
    }

    private static bool TryParseHexOrDec(string raw, out int result)
    {
        raw = raw.Trim();
        if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(raw[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    private void LoadCachedCatalog()
    {
        if (!File.Exists(_catalogFilePath)) return;

        try
        {
            string json = File.ReadAllText(_catalogFilePath);
            var loaded = JsonSerializer.Deserialize<List<ItemMetadata>>(json);
            if (loaded == null) return;

            foreach (var item in loaded)
                _items[item.ItemId] = item;

            if (_items.Count > 0)
                ExtractionStatus = $"Loaded {_items.Count} items from local cache (item_catalog.json)";
        }
        catch { }
    }

    private void SaveCachedCatalog()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(_items.Values.ToList(), options);
            File.WriteAllText(_catalogFilePath, json);
        }
        catch { }
    }
}