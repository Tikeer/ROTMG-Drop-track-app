using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace RotmgTrackerV1.Core.Services.Sprites;

/// <summary>
/// Minimal, dependency-free reader for Unity SerializedFile (resources.assets) tailored for RotMG Exalt:
/// extracts TextAsset "spritesheet" (JSON) and inline RGBA32 Texture2D atlases ("groundTiles", "characters", "mapObjects").
/// Uses direct stream seeking instead of loading the full 450+ MB file into memory.
/// </summary>
public static class UnityAssetsReader
{
    private const int ClassIdTexture2D = 28;
    private const int ClassIdTextAsset = 49;
    private const int ClassIdMonoBehaviour = 114;
    private const int TextureFormatRgba32 = 4;

    public sealed record TextureSliceInfo(string Name, int Width, int Height, long PixelDataOffset, int PixelDataLength);

    public sealed record SourceSpriteEntry(int AtlasId, int X, int Y, int W, int H);

    public sealed class ParsedAssetsIndex
    {
        public Dictionary<string, TextureSliceInfo> Textures { get; } = new(StringComparer.OrdinalIgnoreCase);
        public long SpritesheetJsonOffset { get; set; }
        public int SpritesheetJsonLength { get; set; }
    }

    /// <summary>
    /// Scans the SerializedFile object table and locates the "spritesheet" TextAsset plus inline RGBA32 atlas textures.
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
            _ = br.ReadInt64(); // pathId
            long byteStart = version >= 22 ? (long)br.ReadUInt64() : br.ReadUInt32();
            uint byteSize = br.ReadUInt32();
            int typeIndex = br.ReadInt32();

            if ((uint)typeIndex >= (uint)types.Length) continue;
            int classId = types[typeIndex];

            if (classId == ClassIdTexture2D && byteSize > 1_000_000)
            {
                long saved = fs.Position;
                long objStart = dataOffset + byteStart;
                TryReadTextureInfo(fs, br, objStart, byteSize, index);
                fs.Position = saved;
            }
            else if (classId == ClassIdTextAsset && byteSize > 5_000_000)
            {
                long saved = fs.Position;
                long objStart = dataOffset + byteStart;
                TryReadSpritesheetLocation(fs, br, objStart, index);
                fs.Position = saved;
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
        if (!name.Equals("groundTiles", StringComparison.OrdinalIgnoreCase) &&
            !name.Equals("characters", StringComparison.OrdinalIgnoreCase) &&
            !name.Equals("mapObjects", StringComparison.OrdinalIgnoreCase))
            return;

        Align4(fs);

        // Unity 6000 / 2022+ Texture2D fields after aligned name:
        // m_ForcedFallbackFormat(4), m_DownscaleFallback(1)+m_IsAlphaChannelOptional(1)+pad(2) => 4 bytes? Wait:
        // Verified empirically on Unity 6000.0.58f2: ints[0]=0, ints[1]=width, ints[2]=height, ints[3]=completeImageSize, ints[4]=mipsStripped, ints[5]=textureFormat(4=RGBA32)
        long headerStart = fs.Position;
        int headerInspectLen = (int)Math.Min(160, byteSize - (headerStart - objStart));
        byte[] hdr = br.ReadBytes(headerInspectLen);
        if (hdr.Length < 28) return;

        int width = BinaryPrimitives.ReadInt32LittleEndian(hdr.AsSpan(4, 4));
        int height = BinaryPrimitives.ReadInt32LittleEndian(hdr.AsSpan(8, 4));
        int textureFormat = BinaryPrimitives.ReadInt32LittleEndian(hdr.AsSpan(20, 4));
        if (width <= 0 || height <= 0 || textureFormat != TextureFormatRgba32) return;

        long expectedPixelBytes = (long)width * height * 4;
        if (expectedPixelBytes <= 0 || expectedPixelBytes > byteSize) return;

        // Locate inline image data array: Unity places m_ImageData right before 16-byte StreamingInfo (offset=0, size=0, path="")
        long candidateLenPos = objStart + byteSize - 16 - expectedPixelBytes - 4;
        long pixelOffset = -1;

        if (candidateLenPos >= headerStart)
        {
            fs.Position = candidateLenPos;
            int lenMarker = br.ReadInt32();
            if (lenMarker == expectedPixelBytes)
                pixelOffset = candidateLenPos + 4;
        }

        // Fallback: scan header for the last occurrence of expectedPixelBytes (m_ImageData length prefix)
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

        // 1. Static sprites: sprites[] -> { spriteSheetName, atlasId, elements[] }
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

        // 2. Animated sprites (players, skins, bosses, portals): prefer action=0 & direction=0 (stand facing right)
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
                    _ => 1       // Any fallback frame
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
