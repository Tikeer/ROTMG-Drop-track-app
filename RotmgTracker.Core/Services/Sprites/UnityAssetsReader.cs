using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace RotmgTrackerV1.Core.Services.Sprites;

/// <summary>
/// Minimal, dependency-free reader for Unity SerializedFile (resources.assets + resources.assets.resS) tailored for RotMG Exalt:
/// - extracts TextAsset "spritesheet" (JSON) and inline RGBA32 Texture2D atlases ("groundTiles", "characters", "mapObjects")
/// - extracts UI sprites (RarityIcon_1..4, material_rare/legendary, shiny_item_icon, etc.) from Unity SpriteAtlas ("GUI Atlas")
///   stored in resources.assets.resS.
/// Uses direct stream seeking instead of loading the full 450+ MB file into memory.
/// </summary>
public static class UnityAssetsReader
{
    private const int ClassIdTexture2D = 28;
    private const int ClassIdTextAsset = 49;
    private const int ClassIdMonoBehaviour = 114;
    private const int ClassIdSprite = 213;
    private const int ClassIdSpriteAtlas = 687078895;
    private const int TextureFormatRgba32 = 4;

    public sealed record TextureSliceInfo(
        string Name,
        int Width,
        int Height,
        long PixelDataOffset,
        int PixelDataLength,
        string? ExternalFileName = null);

    public sealed record SourceSpriteEntry(int AtlasId, int X, int Y, int W, int H);

    /// <summary>Rect in Unity GUI Atlas (y is bottom-up Unity texture coordinate, i.e., row 0 = bottom).</summary>
    public sealed record GuiAtlasSpriteEntry(string Name, int X, int Y, int W, int H);

    public sealed class ParsedAssetsIndex
    {
        public Dictionary<string, TextureSliceInfo> Textures { get; } = new(StringComparer.OrdinalIgnoreCase);
        public long SpritesheetJsonOffset { get; set; }
        public int SpritesheetJsonLength { get; set; }
        public TextureSliceInfo? GuiAtlasTexture { get; set; }
        public long GuiSpriteAtlasOffset { get; set; }
        public int GuiSpriteAtlasSize { get; set; }
        public Dictionary<long, (long Offset, int Size)> SpriteObjects { get; } = new();
    }

    public static readonly HashSet<string> WantedGuiSprites = new(StringComparer.OrdinalIgnoreCase)
    {
        "RarityIcon_1",
        "RarityIcon_2",
        "RarityIcon_3",
        "RarityIcon_4",
        "shiny_item_icon",
        "white_shiny_item_icon_0",
        "material_common",
        "material_rare",
        "material_legendary",
        "material_mythical",
        "fame_icon",
        "gold_icon",
        "StarIcon",
        "Star",
        "QuestTextStar"
    };

    /// <summary>
    /// Scans the SerializedFile object table and locates the "spritesheet" TextAsset, inline RGBA32 atlases,
    /// and the streamed "GUI Atlas" Texture2D + SpriteAtlas metadata.
    /// </summary>
    public static ParsedAssetsIndex ReadIndex(string resourcesAssetsPath)
    {
        var index = new ParsedAssetsIndex();

        using var fs = new FileStream(resourcesAssetsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true);

        byte[] header = br.ReadBytes(48);
        if (header.Length < 48)
            throw new InvalidDataException("resources.assets is too small to be a valid Unity SerializedFile.");

        uint version = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8, 4));
        byte endian;
        long dataOffset;

        if (version >= 22)
        {
            endian = header[16];
            dataOffset = (long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(32, 8));
            fs.Position = 48;
        }
        else
        {
            endian = header[16];
            dataOffset = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(12, 4));
            fs.Position = 20;
        }

        if (endian != 0)
            throw new NotSupportedException("Big-endian Unity SerializedFile is not supported.");

        // Skip null-terminated Unity version string
        while (fs.Position < fs.Length)
        {
            int b = fs.ReadByte();
            if (b <= 0) break;
        }

        _ = br.ReadInt32(); // targetPlatform
        bool enableTypeTree = br.ReadByte() != 0;
        if (enableTypeTree)
            throw new NotSupportedException("Unity assets with embedded TypeTree are not supported by this reader.");

        int typeCount = br.ReadInt32();
        int[] types = new int[typeCount];
        for (int i = 0; i < typeCount; i++)
        {
            int classId = br.ReadInt32();
            _ = br.ReadByte(); // isStrippedType
            _ = br.ReadInt16(); // scriptTypeIndex
            if (classId == ClassIdMonoBehaviour)
                fs.Position += 16; // scriptId hash
            fs.Position += 16; // oldTypeHash
            types[i] = classId;
        }

        int objectCount = br.ReadInt32();
        for (int i = 0; i < objectCount; i++)
        {
            Align4(fs);
            long pathId = br.ReadInt64();
            long byteStart = version >= 22 ? (long)br.ReadUInt64() : br.ReadUInt32();
            uint byteSize = br.ReadUInt32();
            int typeIndex = br.ReadInt32();

            if ((uint)typeIndex >= (uint)types.Length) continue;
            int classId = types[typeIndex];

            if (classId == ClassIdTexture2D)
            {
                if (byteSize > 1_000_000 || byteSize is >= 140 and <= 260)
                {
                    long saved = fs.Position;
                    TryReadTextureInfo(fs, br, dataOffset + byteStart, byteSize, index);
                    fs.Position = saved;
                }
            }
            else if (classId == ClassIdTextAsset && byteSize > 5_000_000)
            {
                long saved = fs.Position;
                TryReadSpritesheetLocation(fs, br, dataOffset + byteStart, index);
                fs.Position = saved;
            }
            else if (classId == ClassIdSpriteAtlas)
            {
                long saved = fs.Position;
                TryReadSpriteAtlasLocation(fs, br, dataOffset + byteStart, (int)byteSize, index);
                fs.Position = saved;
            }
            else if (classId == ClassIdSprite)
            {
                index.SpriteObjects[pathId] = (dataOffset + byteStart, (int)byteSize);
            }
        }

        return index;
    }

    private static void TryReadTextureInfo(
        FileStream fs,
        BinaryReader br,
        long objStart,
        uint byteSize,
        ParsedAssetsIndex index)
    {
        fs.Position = objStart;
        int nameLen = br.ReadInt32();
        if (nameLen <= 0 || nameLen > 128) return;

        string name = Encoding.UTF8.GetString(br.ReadBytes(nameLen));
        bool isMainInlineAtlas =
            name.Equals("groundTiles", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("characters", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("mapObjects", StringComparison.OrdinalIgnoreCase);
        bool isGuiAtlas = name.Contains("GUI Atlas", StringComparison.OrdinalIgnoreCase);

        if (!isMainInlineAtlas && !isGuiAtlas)
            return;

        Align4(fs);

        long headerStart = fs.Position;
        int headerInspectLen = (int)Math.Min(180, byteSize - (headerStart - objStart));
        byte[] hdr = br.ReadBytes(headerInspectLen);
        if (hdr.Length < 28) return;

        int width = BinaryPrimitives.ReadInt32LittleEndian(hdr.AsSpan(4, 4));
        int height = BinaryPrimitives.ReadInt32LittleEndian(hdr.AsSpan(8, 4));
        int textureFormat = BinaryPrimitives.ReadInt32LittleEndian(hdr.AsSpan(20, 4));
        if (width <= 0 || height <= 0 || textureFormat != TextureFormatRgba32) return;

        long expectedPixelBytes = (long)width * height * 4;
        if (expectedPixelBytes <= 0) return;

        if (isGuiAtlas)
        {
            // Streamed Texture2D: StreamingInfo is at the end: uint64 offset, uint32 size, int32 pathLen, utf8 path (+ align4)
            // Find "resources.assets.resS" (or any ".resS") inside hdr
            byte[] ressMarker = ".resS"u8.ToArray();
            int markerPos = hdr.AsSpan().IndexOf(ressMarker);
            if (markerPos < 16) return;

            // Walk back to the int32 string length right before the path string
            int strEnd = markerPos + ressMarker.Length;
            int pathLenPos = -1;
            for (int probe = Math.Max(16, markerPos - 64); probe <= markerPos - 4; probe++)
            {
                int candidateLen = BinaryPrimitives.ReadInt32LittleEndian(hdr.AsSpan(probe, 4));
                if (candidateLen == strEnd - (probe + 4))
                {
                    pathLenPos = probe;
                    break;
                }
            }

            if (pathLenPos < 12) return;
            string externalFile = Encoding.UTF8.GetString(hdr, pathLenPos + 4, strEnd - (pathLenPos + 4));
            uint streamSize = BinaryPrimitives.ReadUInt32LittleEndian(hdr.AsSpan(pathLenPos - 4, 4));
            ulong streamOffset = BinaryPrimitives.ReadUInt64LittleEndian(hdr.AsSpan(pathLenPos - 12, 8));

            if (streamSize == expectedPixelBytes)
            {
                index.GuiAtlasTexture = new TextureSliceInfo(
                    name, width, height, (long)streamOffset, (int)streamSize, externalFile);
            }
            return;
        }

        if (expectedPixelBytes > byteSize) return;

        // Inline Texture2D: m_ImageData ends right before 16-byte StreamingInfo (offset=0, size=0, path="")
        long candidateLenPos = objStart + byteSize - 16 - expectedPixelBytes - 4;
        long pixelOffset = -1;

        if (candidateLenPos >= headerStart)
        {
            fs.Position = candidateLenPos;
            int lenMarker = br.ReadInt32();
            if (lenMarker == expectedPixelBytes)
                pixelOffset = candidateLenPos + 4;
        }

        if (pixelOffset < 0)
        {
            Span<byte> marker = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(marker, (int)expectedPixelBytes);
            int lastIdx = -1;
            for (int offset = 16; offset <= hdr.Length - 4; offset += 4)
            {
                if (hdr.AsSpan(offset, 4).SequenceEqual(marker))
                    lastIdx = offset;
            }
            if (lastIdx >= 0)
                pixelOffset = headerStart + lastIdx + 4;
        }

        if (pixelOffset > 0 && pixelOffset + expectedPixelBytes <= objStart + byteSize)
        {
            index.Textures[name] = new TextureSliceInfo(name, width, height, pixelOffset, (int)expectedPixelBytes);
        }
    }

    private static void TryReadSpritesheetLocation(
        FileStream fs,
        BinaryReader br,
        long objStart,
        ParsedAssetsIndex index)
    {
        fs.Position = objStart;
        int nameLen = br.ReadInt32();
        if (nameLen <= 0 || nameLen > 64) return;

        string name = Encoding.UTF8.GetString(br.ReadBytes(nameLen));
        if (!string.Equals(name, "spritesheet", StringComparison.Ordinal))
            return;

        Align4(fs);
        int jsonLen = br.ReadInt32();
        if (jsonLen <= 0) return;

        index.SpritesheetJsonOffset = fs.Position;
        index.SpritesheetJsonLength = jsonLen;
    }

    private static void TryReadSpriteAtlasLocation(
        FileStream fs,
        BinaryReader br,
        long objStart,
        int byteSize,
        ParsedAssetsIndex index)
    {
        fs.Position = objStart;
        int nameLen = br.ReadInt32();
        if (nameLen <= 0 || nameLen > 64) return;

        string name = Encoding.UTF8.GetString(br.ReadBytes(nameLen));
        if (!string.Equals(name, "GUI Atlas", StringComparison.OrdinalIgnoreCase))
            return;

        index.GuiSpriteAtlasOffset = objStart;
        index.GuiSpriteAtlasSize = byteSize;
    }

    /// <summary>
    /// Resolves named UI sprites (e.g. RarityIcon_1..4, material_rare/legendary, shiny_item_icon)
    /// from Unity's GUI Atlas SpriteAtlas object.
    /// </summary>
    public static List<GuiAtlasSpriteEntry> ReadGuiAtlasSprites(
        string resourcesAssetsPath,
        ParsedAssetsIndex index,
        HashSet<string> wantedNames)
    {
        var results = new List<GuiAtlasSpriteEntry>();
        if (index.GuiSpriteAtlasOffset <= 0 || index.GuiSpriteAtlasSize <= 0 || index.GuiAtlasTexture == null)
            return results;

        using var fs = new FileStream(resourcesAssetsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        fs.Position = index.GuiSpriteAtlasOffset;
        byte[] raw = new byte[index.GuiSpriteAtlasSize];
        fs.ReadExactly(raw);

        int p = 0;
        int nameLen = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(p, 4));
        p += 4 + nameLen;
        if ((p & 3) != 0) p += 4 - (p & 3);

        int packedCount = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(p, 4));
        p += 4;
        long[] packedPids = new long[packedCount];
        for (int i = 0; i < packedCount; i++)
        {
            packedPids[i] = BinaryPrimitives.ReadInt64LittleEndian(raw.AsSpan(p + 4, 8));
            p += 12;
        }

        int namesCount = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(p, 4));
        p += 4;
        string[] names = new string[namesCount];
        for (int i = 0; i < namesCount; i++)
        {
            int sl = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(p, 4));
            p += 4;
            names[i] = Encoding.UTF8.GetString(raw, p, sl);
            p += sl;
            if ((p & 3) != 0) p += 4 - (p & 3);
        }

        int renderMapStart = p + 4; // skip m_RenderDataMap count
        int pairCount = Math.Min(packedCount, namesCount);
        ReadOnlySpan<byte> mapSpan = raw.AsSpan(renderMapStart);

        for (int i = 0; i < pairCount; i++)
        {
            string sname = names[i];
            if (!wantedNames.Contains(sname)) continue;
            if (!index.SpriteObjects.TryGetValue(packedPids[i], out var sprLoc)) continue;

            fs.Position = sprLoc.Offset;
            byte[] sraw = new byte[sprLoc.Size];
            fs.ReadExactly(sraw);

            // Match the Sprite's 24-byte m_RenderDataKey (GUID 16B + long 8B) inside m_RenderDataMap
            int maxProbe = Math.Min(sraw.Length - 24, 200);
            for (int kOff = 40; kOff <= maxProbe; kOff += 4)
            {
                ReadOnlySpan<byte> key = sraw.AsSpan(kOff, 24);
                bool allZero = true;
                for (int z = 0; z < 24; z++)
                {
                    if (key[z] != 0) { allZero = false; break; }
                }
                if (allZero) continue;

                int relPos = mapSpan.IndexOf(key);
                if (relPos < 0) continue;

                int d = renderMapStart + relPos + 24;
                if (d + 44 > raw.Length) break;

                // SpriteAtlasData layout (Unity 6000): texture PPtr(12), alphaTexture PPtr(12), secondaryTextures(4), textureRect(16)
                float tx = BinaryPrimitives.ReadSingleLittleEndian(raw.AsSpan(d + 28, 4));
                float ty = BinaryPrimitives.ReadSingleLittleEndian(raw.AsSpan(d + 32, 4));
                float tw = BinaryPrimitives.ReadSingleLittleEndian(raw.AsSpan(d + 36, 4));
                float th = BinaryPrimitives.ReadSingleLittleEndian(raw.AsSpan(d + 40, 4));

                int ix = (int)MathF.Round(tx);
                int iy = (int)MathF.Round(ty);
                int iw = (int)MathF.Round(tw);
                int ih = (int)MathF.Round(th);

                if (iw > 0 && ih > 0 &&
                    ix >= 0 && iy >= 0 &&
                    ix + iw <= index.GuiAtlasTexture.Width &&
                    iy + ih <= index.GuiAtlasTexture.Height)
                {
                    results.Add(new GuiAtlasSpriteEntry(sname, ix, iy, iw, ih));
                }
                break;
            }
        }

        return results;
    }

    /// <summary>
    /// Parses the "spritesheet" JSON TextAsset into a lookup of (spriteSheetName, index) -> source atlas rect.
    /// Uses per-element aId (1 = groundTiles, 2 = characters, 4 = mapObjects), NOT sheet-level atlasId.
    /// For animatedSprites, prefers canonical idle frame (action == 0, direction == 0).
    /// </summary>
    public static Dictionary<(string Sheet, int Index), SourceSpriteEntry> ReadSpritesheetLookup(
        string resourcesAssetsPath,
        ParsedAssetsIndex index,
        HashSet<(string Sheet, int Index)> neededKeys)
    {
        var result = new Dictionary<(string Sheet, int Index), SourceSpriteEntry>();
        if (index.SpritesheetJsonOffset <= 0 || index.SpritesheetJsonLength <= 0)
            return result;

        byte[] jsonBytes;
        using (var fs = new FileStream(resourcesAssetsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            fs.Position = index.SpritesheetJsonOffset;
            jsonBytes = new byte[index.SpritesheetJsonLength];
            fs.ReadExactly(jsonBytes);
        }

        using var doc = JsonDocument.Parse(jsonBytes);
        var root = doc.RootElement;

        if (root.TryGetProperty("sprites", out var spritesArr) && spritesArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var sheet in spritesArr.EnumerateArray())
            {
                string? sheetName = sheet.TryGetProperty("spriteSheetName", out var sn) ? sn.GetString() : null;
                if (string.IsNullOrWhiteSpace(sheetName)) continue;
                int sheetAtlasId = sheet.TryGetProperty("atlasId", out var sa) ? sa.GetInt32() : 4;

                if (!sheet.TryGetProperty("elements", out var elements) || elements.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var elem in elements.EnumerateArray())
                {
                    if (!elem.TryGetProperty("index", out var idxProp)) continue;
                    int idx = idxProp.GetInt32();
                    if (!neededKeys.Contains((sheetName, idx))) continue;

                    int aId = elem.TryGetProperty("aId", out var aidProp) ? aidProp.GetInt32() : sheetAtlasId;
                    if (!TryReadPosition(elem, out int x, out int y, out int w, out int h)) continue;

                    result[(sheetName, idx)] = new SourceSpriteEntry(aId, x, y, w, h);
                }
            }
        }

        var animPriority = new Dictionary<(string Sheet, int Index), int>();
        if (root.TryGetProperty("animatedSprites", out var animArr) && animArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var anim in animArr.EnumerateArray())
            {
                string? sheetName = anim.TryGetProperty("spriteSheetName", out var sn) ? sn.GetString() : null;
                if (string.IsNullOrWhiteSpace(sheetName)) continue;
                if (!anim.TryGetProperty("index", out var idxProp)) continue;
                int idx = idxProp.GetInt32();

                var key = (sheetName, idx);
                if (!neededKeys.Contains(key)) continue;

                int action = anim.TryGetProperty("action", out var act) ? act.GetInt32() : 99;
                int direction = anim.TryGetProperty("direction", out var dir) ? dir.GetInt32() : 99;
                int priority = (action, direction) switch
                {
                    (0, 0) => 4, // Stand facing right (canonical RotMG icon)
                    (0, 2) => 3, // Stand facing down
                    (0, _) => 2, // Stand any direction
                    _ => 1
                };

                if (animPriority.TryGetValue(key, out int existingPrio) && existingPrio >= priority)
                    continue;

                if (!anim.TryGetProperty("spriteData", out var sd)) continue;
                int aId = sd.TryGetProperty("aId", out var aidProp) ? aidProp.GetInt32() : 2;
                if (!TryReadPosition(sd, out int x, out int y, out int w, out int h)) continue;

                animPriority[key] = priority;
                result[key] = new SourceSpriteEntry(aId, x, y, w, h);
            }
        }

        return result;
    }

    public static string? AtlasIdToTextureName(int atlasId) => atlasId switch
    {
        1 => "groundTiles",
        2 => "characters",
        4 => "mapObjects",
        _ => null
    };

    private static bool TryReadPosition(JsonElement container, out int x, out int y, out int w, out int h)
    {
        x = y = w = h = 0;
        if (!container.TryGetProperty("position", out var pos)) return false;
        x = pos.GetProperty("x").GetInt32();
        y = pos.GetProperty("y").GetInt32();
        w = pos.GetProperty("w").GetInt32();
        h = pos.GetProperty("h").GetInt32();
        return w > 0 && h > 0;
    }

    private static void Align4(FileStream fs)
    {
        long rem = fs.Position & 3;
        if (rem != 0)
            fs.Position += 4 - rem;
    }
}
