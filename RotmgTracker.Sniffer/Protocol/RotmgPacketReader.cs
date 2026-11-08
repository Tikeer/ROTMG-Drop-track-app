using System.Buffers.Binary;
using System.Text;

namespace RotmgTrackerV1.Sniffer.Protocol;

public readonly record struct StatEntry(int StatType, int IntValue, string StringValue);

public ref struct RotmgPacketReader
{
    private readonly ReadOnlySpan<byte> _buffer;
    private int _position;

    public RotmgPacketReader(ReadOnlySpan<byte> buffer)
    {
        _buffer = buffer;
        _position = 0;
    }

    public int Position => _position;
    public int Remaining => _buffer.Length - _position;

    public byte ReadByte() => _buffer[_position++];

    public bool ReadBool() => ReadByte() != 0;

    public short ReadInt16()
    {
        short value = BinaryPrimitives.ReadInt16BigEndian(_buffer.Slice(_position, 2));
        _position += 2;
        return value;
    }

    public ushort ReadUInt16()
    {
        ushort value = BinaryPrimitives.ReadUInt16BigEndian(_buffer.Slice(_position, 2));
        _position += 2;
        return value;
    }

    public int ReadInt32()
    {
        int value = BinaryPrimitives.ReadInt32BigEndian(_buffer.Slice(_position, 4));
        _position += 4;
        return value;
    }

    public float ReadFloat()
    {
        float value = BinaryPrimitives.ReadSingleBigEndian(_buffer.Slice(_position, 4));
        _position += 4;
        return value;
    }

    public void Skip(int byteCount)
    {
        _position = Math.Min(_buffer.Length, _position + byteCount);
    }

    public string ReadUtfString()
    {
        if (Remaining < 2) return string.Empty;
        ushort length = ReadUInt16();
        if (length == 0 || Remaining < length) return string.Empty;

        string result = Encoding.UTF8.GetString(_buffer.Slice(_position, length));
        _position += length;
        return result;
    }

    public int ReadCompressedInt()
    {
        byte b = ReadByte();
        bool isNegative = (b & 0x40) != 0;
        int shift = 6;
        int value = b & 0x3F;

        while ((b & 0x80) != 0)
        {
            if (Remaining <= 0) break;
            b = ReadByte();
            value |= (b & 0x7F) << shift;
            shift += 7;
        }

        return isNegative ? -value : value;
    }

    public (float X, float Y) ReadWorldPosData()
    {
        if (Remaining < 8) return (0f, 0f);
        float x = ReadFloat();
        float y = ReadFloat();
        return (x, y);
    }

    // Czyta listę statystyk obiektu (gracza lub worka z lootem)
    public List<StatEntry> ReadStatDataList()
    {
        int count = ReadCompressedInt();
        var list = new List<StatEntry>(Math.Max(0, count));

        for (int i = 0; i < count && Remaining > 0; i++)
        {
            int statType = ReadByte();
            if (RotmgProtocolConstants.IsStringStat(statType))
            {
                string strVal = ReadUtfString();
                list.Add(new StatEntry(statType, 0, strVal));
            }
            else
            {
                int intVal = ReadCompressedInt();
                list.Add(new StatEntry(statType, intVal, string.Empty));
            }

            // Wartość dodatkowa (secondary) dodana w nowym protokole Exalta:
            ReadCompressedInt();
        }

        return list;
    }
}