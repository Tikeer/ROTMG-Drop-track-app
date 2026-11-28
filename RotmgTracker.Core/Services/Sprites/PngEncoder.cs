using System.Buffers.Binary;
using System.IO.Compression;

namespace RotmgTrackerV1.Core.Services.Sprites;

/// <summary>
/// Dependency-free 32-bit RGBA PNG encoder using BCL ZLibStream.
/// Expects top-down RGBA byte buffer of length width * height * 4.
/// </summary>
public static class PngEncoder
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] EncodeRgba32(byte[] rgba, int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Image dimensions must be positive.");
        if (rgba.Length < width * height * 4)
            throw new ArgumentException("RGBA buffer is smaller than width * height * 4.", nameof(rgba));

        using var output = new MemoryStream();
        output.Write(Signature);

        // IHDR chunk
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr[0..4], width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..8], height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 6; // color type: RGBA
        ihdr[10] = 0; // compression: deflate
        ihdr[11] = 0; // filter: adaptive
        ihdr[12] = 0; // interlace: none
        WriteChunk(output, "IHDR"u8, ihdr);

        // IDAT chunk (zlib-compressed scanlines with filter byte 0 = None per row)
        byte[] compressed;
        using (var idatStream = new MemoryStream())
        {
            using (var zlib = new ZLibStream(idatStream, CompressionLevel.Optimal, leaveOpen: true))
            {
                int stride = width * 4;
                for (int y = 0; y < height; y++)
                {
                    zlib.WriteByte(0); // filter type None
                    zlib.Write(rgba, y * stride, stride);
                }
            }

            compressed = idatStream.ToArray();
        }

        WriteChunk(output, "IDAT"u8, compressed);

        // IEND chunk
        WriteChunk(output, "IEND"u8, ReadOnlySpan<byte>.Empty);

        return output.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> lenBytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lenBytes, data.Length);
        output.Write(lenBytes);
        output.Write(type);
        output.Write(data);

        uint crc = 0xFFFFFFFFu;
        crc = UpdateCrc(crc, type);
        crc = UpdateCrc(crc, data);
        crc ^= 0xFFFFFFFFu;

        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
