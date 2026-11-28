using System.Text.Json;
using RotmgTrackerV1.Core.Enums;

namespace RotmgTrackerV1.Core.Services.Sprites;

public readonly record struct SpriteRect(int X, int Y, int W, int H);

/// <summary>
/// Generates and caches a compact packed PNG sprite atlas + sprites.json manifest in AppData/RotmgTracker/sprites
/// directly from the local RotMG Exalt resources.assets (no Deca assets ever committed to git — D15).
/// </summary>
public sealed class SpriteAtlasService
{
    private const int ManifestVersion = 3;
    private const int Padding = 1;
    private const string GuiAtlasSourceKey = "__GUI_ATLAS__";

    private readonly GameDataService _gameData;
    private readonly string _spritesDir;
    private readonly string _atlasPath;
    private readonly string _manifestPath;
    private readonly object _lock = new();
    private int _buildRunning;

    private Dictionary<string, SpriteRect> _entries = new(StringComparer.OrdinalIgnoreCase);

    public bool IsReady { get; private set; }
    public bool IsBuilding => Volatile.Read(ref _buildRunning) == 1;
    public int AtlasWidth { get; private set; }
    public int AtlasHeight { get; private set; }
    public int SpriteCount => _entries.Count;
    public string AtlasDataUrl { get; private set; } = string.Empty;
    public string StatusMessage { get; private set; } = "Sprite atlas not loaded";
    public string SpritesFolder => _spritesDir;

    public event Action? Changed;

    public SpriteAtlasService(GameDataService gameData, string? customSpritesDir = null)
    {
        _gameData = gameData;
        _spritesDir = customSpritesDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RotmgTracker",
            "sprites");
        Directory.CreateDirectory(_spritesDir);
        _atlasPath = Path.Combine(_spritesDir, "atlas.png");
        _manifestPath = Path.Combine(_spritesDir, "sprites.json");
    }

    /// <summary>
    /// Fast startup load: loads cached atlas from disk if present, then rebuilds in background only if missing or outdated.
    /// </summary>
    public Task EnsureBuiltAsync(string? customResourcesAssetsPath = null)
    {
        var cachedManifest = TryLoadCachedAtlas();

        string? assetsPath = customResourcesAssetsPath ?? _gameData.FindResourcesAssetsPath();
        if (string.IsNullOrWhiteSpace(assetsPath) || !File.Exists(assetsPath))
        {
            if (!IsReady)
                StatusMessage = "RotMG resources.assets not found — sprite atlas unavailable";
            Changed?.Invoke();
            return Task.CompletedTask;
        }

        var fi = new FileInfo(assetsPath);
        bool upToDate = cachedManifest != null
                        && cachedManifest.Version == ManifestVersion
                        && cachedManifest.SourceFileLength == fi.Length
                        && cachedManifest.SourceLastWriteTicks == fi.LastWriteTimeUtc.Ticks
                        && cachedManifest.Sprites.Count > 0;

        if (upToDate)
        {
            Changed?.Invoke();
            return Task.CompletedTask;
        }

        return RebuildAsync(assetsPath);
    }

    /// <summary>
    /// Rebuilds atlas.png and sprites.json from resources.assets on a dedicated LongRunning background thread.
    /// </summary>
    public Task<bool> RebuildAsync(string? customResourcesAssetsPath = null)
    {
        if (Interlocked.CompareExchange(ref _buildRunning, 1, 0) != 0)
            return Task.FromResult(false);

        StatusMessage = "Building sprite atlas from resources.assets...";
        Changed?.Invoke();

        return Task.Factory.StartNew(() =>
        {
            try
            {
                return BuildAtlasInternal(customResourcesAssetsPath);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Sprite atlas build error: {ex.Message}";
                return false;
            }
            finally
            {
                Volatile.Write(ref _buildRunning, 0);
                Changed?.Invoke();
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private AtlasManifest? TryLoadCachedAtlas()
    {
        try
        {
            if (!File.Exists(_atlasPath) || !File.Exists(_manifestPath))
                return null;

            var manifest = JsonSerializer.Deserialize<AtlasManifest>(File.ReadAllText(_manifestPath));
            if (manifest == null || manifest.Version != ManifestVersion || manifest.AtlasWidth <= 0 || manifest.AtlasHeight <= 0)
                return null;

            byte[] pngBytes = File.ReadAllBytes(_atlasPath);
            if (pngBytes.Length < 32)
                return null;

            var entries = new Dictionary<string, SpriteRect>(manifest.Sprites, StringComparer.OrdinalIgnoreCase);
            string dataUrl = "data:image/png;base64," + Convert.ToBase64String(pngBytes);

            lock (_lock)
            {
                _entries = entries;
                AtlasWidth = manifest.AtlasWidth;
                AtlasHeight = manifest.AtlasHeight;
                AtlasDataUrl = dataUrl;
                IsReady = true;
                StatusMessage = $"Loaded cached atlas ({entries.Count} sprites, {AtlasWidth}x{AtlasHeight}, {pngBytes.Length / 1024} KB)";
            }

            return manifest;
        }
        catch
        {
            return null;
        }
    }

    private bool BuildAtlasInternal(string? customResourcesAssetsPath)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        string? assetsPath = customResourcesAssetsPath ?? _gameData.FindResourcesAssetsPath();
        if (string.IsNullOrWhiteSpace(assetsPath) || !File.Exists(assetsPath))
        {
            StatusMessage = "RotMG resources.assets not found";
            return false;
        }

        var requiredRefs = _gameData.GetAllRequiredSpriteRefs();
        if (requiredRefs.Count == 0)
        {
            StatusMessage = "No catalog sprites to pack (scan game files first)";
            return false;
        }

        var neededKeys = new HashSet<(string Sheet, int Index)>(SpriteKeyComparer.Instance);
        foreach (var r in requiredRefs)
            neededKeys.Add((r.TextureFile, r.TextureIndex));

        var assetsIndex = UnityAssetsReader.ReadIndex(assetsPath);
        var resolved = UnityAssetsReader.ReadSpritesheetLookup(assetsPath, assetsIndex, neededKeys);
        var guiSprites = UnityAssetsReader.ReadGuiAtlasSprites(assetsPath, assetsIndex, UnityAssetsReader.WantedGuiSprites);

        if (resolved.Count == 0 && guiSprites.Count == 0)
        {
            StatusMessage = "Could not resolve any sprites from resources.assets spritesheet";
            return false;
        }

        // Filter to entries whose target texture exists and bounds are valid
        // IsUnityBottomUpRect = true for GUI Atlas (y=0 is bottom of texture), false for spritesheet JSON (y=0 is top)
        var packItems = new List<(string Key, UnityAssetsReader.SourceSpriteEntry Src, string TexName, bool IsUnityBottomUpRect)>(resolved.Count + guiSprites.Count);
        foreach (var (key, src) in resolved)
        {
            string? texName = UnityAssetsReader.AtlasIdToTextureName(src.AtlasId);
            if (texName == null || !assetsIndex.Textures.TryGetValue(texName, out var texInfo))
                continue;

            if (src.X < 0 || src.Y < 0 || src.W <= 0 || src.H <= 0 ||
                src.X + src.W > texInfo.Width || src.Y + src.H > texInfo.Height)
                continue;

            packItems.Add((MakeKey(key.Sheet, key.Index), src, texName, false));
        }

        foreach (var g in guiSprites)
        {
            packItems.Add((MakeUiKey(g.Name), new UnityAssetsReader.SourceSpriteEntry(0, g.X, g.Y, g.W, g.H), GuiAtlasSourceKey, true));
        }

        if (packItems.Count == 0)
        {
            StatusMessage = "No valid sprite bounds found in resources.assets textures";
            return false;
        }

        // Shelf-pack sorted by height desc, width desc
        packItems.Sort((a, b) =>
        {
            int cmp = b.Src.H.CompareTo(a.Src.H);
            return cmp != 0 ? cmp : b.Src.W.CompareTo(a.Src.W);
        });

        int atlasWidth = 1024;
        var placements = new List<(string Key, UnityAssetsReader.SourceSpriteEntry Src, string TexName, bool IsUnityBottomUpRect, SpriteRect Dest)>(packItems.Count);
        int curX = Padding;
        int curY = Padding;
        int shelfHeight = 0;

        foreach (var item in packItems)
        {
            if (item.Src.W + Padding * 2 > atlasWidth)
                atlasWidth = NextPowerOfTwo(item.Src.W + Padding * 2);

            if (curX + item.Src.W + Padding > atlasWidth)
            {
                curX = Padding;
                curY += shelfHeight + Padding;
                shelfHeight = 0;
            }

            placements.Add((item.Key, item.Src, item.TexName, item.IsUnityBottomUpRect, new SpriteRect(curX, curY, item.Src.W, item.Src.H)));
            curX += item.Src.W + Padding;
            if (item.Src.H > shelfHeight)
                shelfHeight = item.Src.H;
        }

        int atlasHeight = Math.Max(16, curY + shelfHeight + Padding);
        byte[] atlasRgba = new byte[atlasWidth * atlasHeight * 4];
        int dstStride = atlasWidth * 4;

        // Copy pixels one source texture at a time (keeps peak RAM <= 134 MB and finishes in ~500 ms)
        using (var fs = new FileStream(assetsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            foreach (var texGroup in placements.GroupBy(p => p.TexName, StringComparer.OrdinalIgnoreCase))
            {
                if (texGroup.Key == GuiAtlasSourceKey)
                {
                    var guiTex = assetsIndex.GuiAtlasTexture;
                    if (guiTex == null || string.IsNullOrWhiteSpace(guiTex.ExternalFileName)) continue;

                    string dataDir = Path.GetDirectoryName(assetsPath) ?? string.Empty;
                    string ressPath = Path.Combine(dataDir, guiTex.ExternalFileName);
                    if (!File.Exists(ressPath)) continue;

                    using var rfs = new FileStream(ressPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    int srcStride = guiTex.Width * 4;
                    foreach (var p in texGroup)
                    {
                        int copyBytes = p.Src.W * 4;
                        byte[] rowBuf = new byte[copyBytes];
                        for (int dy = 0; dy < p.Src.H; dy++)
                        {
                            // GUI Atlas textureRect.y is already bottom-up; flip to top-down PNG row order
                            int srcRow = p.Src.Y + (p.Src.H - 1 - dy);
                            rfs.Position = guiTex.PixelDataOffset + (long)srcRow * srcStride + p.Src.X * 4L;
                            rfs.ReadExactly(rowBuf);
                            int dstOffset = (p.Dest.Y + dy) * dstStride + p.Dest.X * 4;
                            Buffer.BlockCopy(rowBuf, 0, atlasRgba, dstOffset, copyBytes);
                        }
                    }
                    continue;
                }

                if (!assetsIndex.Textures.TryGetValue(texGroup.Key, out var texInfo))
                    continue;

                byte[] texBuffer = new byte[texInfo.PixelDataLength];
                fs.Position = texInfo.PixelDataOffset;
                fs.ReadExactly(texBuffer);

                int mainSrcStride = texInfo.Width * 4;

                foreach (var p in texGroup)
                {
                    int copyBytes = p.Src.W * 4;
                    for (int dy = 0; dy < p.Src.H; dy++)
                    {
                        // Unity Texture2D pixels are bottom-up; flip Y to standard top-down PNG
                        int srcRow = texInfo.Height - 1 - (p.Src.Y + dy);
                        int srcOffset = srcRow * mainSrcStride + p.Src.X * 4;
                        int dstOffset = (p.Dest.Y + dy) * dstStride + p.Dest.X * 4;
                        Buffer.BlockCopy(texBuffer, srcOffset, atlasRgba, dstOffset, copyBytes);
                    }
                }
            }
        }

        byte[] pngBytes = PngEncoder.EncodeRgba32(atlasRgba, atlasWidth, atlasHeight);

        var fi = new FileInfo(assetsPath);
        var entries = new Dictionary<string, SpriteRect>(placements.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var p in placements)
            entries[p.Key] = p.Dest;

        var manifest = new AtlasManifest
        {
            Version = ManifestVersion,
            AtlasWidth = atlasWidth,
            AtlasHeight = atlasHeight,
            SourceFileLength = fi.Length,
            SourceLastWriteTicks = fi.LastWriteTimeUtc.Ticks,
            Sprites = entries
        };

        Directory.CreateDirectory(_spritesDir);
        string tmpPng = _atlasPath + ".tmp";
        string tmpJson = _manifestPath + ".tmp";
        File.WriteAllBytes(tmpPng, pngBytes);
        File.WriteAllText(tmpJson, JsonSerializer.Serialize(manifest));
        File.Move(tmpPng, _atlasPath, overwrite: true);
        File.Move(tmpJson, _manifestPath, overwrite: true);

        string dataUrl = "data:image/png;base64," + Convert.ToBase64String(pngBytes);

        lock (_lock)
        {
            _entries = entries;
            AtlasWidth = atlasWidth;
            AtlasHeight = atlasHeight;
            AtlasDataUrl = dataUrl;
            IsReady = true;
            StatusMessage = $"Built atlas ({entries.Count} sprites, {atlasWidth}x{atlasHeight}, {pngBytes.Length / 1024} KB in {sw.ElapsedMilliseconds} ms)";
        }

        return true;
    }

    public bool TryGetSprite(string? textureFile, int textureIndex, out SpriteRect rect)
    {
        if (!IsReady || string.IsNullOrWhiteSpace(textureFile))
        {
            rect = default;
            return false;
        }

        lock (_lock)
        {
            return _entries.TryGetValue(MakeKey(textureFile, textureIndex), out rect);
        }
    }

    public bool TryGetItemSprite(int itemId, out SpriteRect rect)
    {
        if (itemId > 0 && _gameData.TryGetItem(itemId, out var meta))
            return TryGetSprite(meta.TextureFile, meta.TextureIndex, out rect);

        rect = default;
        return false;
    }

    public bool TryGetMarkSprite(int markItemId, out SpriteRect rect)
    {
        if (markItemId > 0 && _gameData.TryResolveMark(markItemId, out var mark))
            return TryGetSprite(mark.TextureFile, mark.TextureIndex, out rect);

        rect = default;
        return false;
    }

    public bool TryGetBagSprite(int bagObjectType, BagType fallbackBagType, out SpriteRect rect)
    {
        if (_gameData.TryGetBagSprite(bagObjectType, fallbackBagType, out var meta))
            return TryGetSprite(meta.TextureFile, meta.TextureIndex, out rect);

        rect = default;
        return false;
    }

    public bool TryGetUiSprite(string? uiSpriteName, out SpriteRect rect)
    {
        if (!IsReady || string.IsNullOrWhiteSpace(uiSpriteName))
        {
            rect = default;
            return false;
        }

        lock (_lock)
        {
            return _entries.TryGetValue(MakeUiKey(uiSpriteName), out rect);
        }
    }

    public bool TryGetRarityIconSprite(ItemRarity rarity, out SpriteRect rect)
    {
        string? name = rarity switch
        {
            ItemRarity.Uncommon => "RarityIcon_1",
            ItemRarity.Rare => "RarityIcon_2",
            ItemRarity.Legendary => "RarityIcon_3",
            ItemRarity.Divine => "RarityIcon_4",
            _ => null
        };
        return TryGetUiSprite(name, out rect);
    }

    public bool TryGetPortalSprite(string? dungeonName, int portalObjectType, out SpriteRect rect)
    {
        if (!string.IsNullOrWhiteSpace(dungeonName) &&
            dungeonName.Contains("Realm of the Mad God", StringComparison.OrdinalIgnoreCase))
        {
            return TryGetSprite("lofiEnvironment", 0x7b, out rect);
        }

        PortalMetadata? portal = portalObjectType > 0
            ? _gameData.ResolvePortalByObjectType(portalObjectType)
            : null;
        portal ??= !string.IsNullOrWhiteSpace(dungeonName)
            ? _gameData.ResolvePortalByDungeonName(dungeonName)
            : null;

        if (portal != null)
            return TryGetSprite(portal.TextureFile, portal.TextureIndex, out rect);

        rect = default;
        return false;
    }

    public bool TryGetBossSprite(int bossObjectType, string? bossName, out SpriteRect rect)
    {
        EnemyMetadata? enemy = null;
        if (bossObjectType > 0)
            _gameData.TryResolveEnemy(bossObjectType, out enemy);
        enemy ??= !string.IsNullOrWhiteSpace(bossName)
            ? _gameData.ResolveEnemyByName(bossName)
            : null;

        if (enemy != null)
            return TryGetSprite(enemy.TextureFile, enemy.TextureIndex, out rect);

        rect = default;
        return false;
    }

    public bool TryGetClassOrSkinSprite(RotmgClass rotmgClass, int skinId, out SpriteRect rect)
    {
        if (skinId > 0 && _gameData.TryGetSkinSprite(skinId, out var skin) &&
            TryGetSprite(skin.TextureFile, skin.TextureIndex, out rect))
            return true;

        if (_gameData.TryGetClassSprite(rotmgClass, out var cls))
            return TryGetSprite(cls.TextureFile, cls.TextureIndex, out rect);

        rect = default;
        return false;
    }

    private static string MakeKey(string sheet, int index) => $"{sheet}:{index}";
    private static string MakeUiKey(string uiSpriteName) => $"ui:{uiSpriteName}";

    private static int NextPowerOfTwo(int v)
    {
        int p = 1;
        while (p < v) p <<= 1;
        return p;
    }

    private sealed class AtlasManifest
    {
        public int Version { get; set; }
        public int AtlasWidth { get; set; }
        public int AtlasHeight { get; set; }
        public long SourceFileLength { get; set; }
        public long SourceLastWriteTicks { get; set; }
        public Dictionary<string, SpriteRect> Sprites { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class SpriteKeyComparer : IEqualityComparer<(string Sheet, int Index)>
    {
        public static readonly SpriteKeyComparer Instance = new();

        public bool Equals((string Sheet, int Index) x, (string Sheet, int Index) y) =>
            x.Index == y.Index && string.Equals(x.Sheet, y.Sheet, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Sheet, int Index) obj) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Sheet), obj.Index);
    }
}
