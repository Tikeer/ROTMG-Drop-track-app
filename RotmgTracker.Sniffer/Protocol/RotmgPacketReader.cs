using System.Buffers.Binary;
using System.Text;

namespace RotmgTrackerV1.Sniffer.Protocol;

public ref struct RotmgPacketReader
{
    private readonly ReadOnlySpan<byte> _buffer;
    private int _position;

    public RotmgPacketReader(ReadOnlySpan<byte> buffer)
    {
        _buffer = buffer;
        _position = 0;
    }

    public int Remaining => _buffer.Length - _position;

    public byte ReadByte() => _buffer[_position++];

    public bool ReadBool() => ReadByte() != 0;

    public short ReadInt16()
    {
        short value = BinaryPrimitives.ReadInt16BigEndian(_buffer.Slice(_position, 2));
        _position += 2;
        return value;
    }

    public int ReadInt32()
    {
        int value = BinaryPrimitives.ReadInt32BigEndian(_buffer.Slice(_position, 4));
        _position += 4;
        return value;
    }

    public string ReadUtfString()
    {
        ushort length = BinaryPrimitives.ReadUInt16BigEndian(_buffer.Slice(_position, 2));
        _position += 2;
        if (length == 0) return string.Empty;

        string result = Encoding.UTF8.GetString(_buffer.Slice(_position, length));
        _position += length;
        return result;
    }

    // Czyta skompresowanego inta używanego w pakietach UPDATE i NEWTICK w RotMG
    public int ReadCompressedInt()
    {
        byte b = ReadByte();
        bool isNegative = (b & 0x40) != 0;
        int shift = 6;
        int value = b & 0x3F;

        while ((b & 0x80) != 0)
        {
            b = ReadByte();
            value |= (b & 0x7F) << shift;
            shift += 7;
        }

        return isNegative ? -value : value;
    }
}