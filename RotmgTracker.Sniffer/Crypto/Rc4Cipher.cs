namespace RotmgTrackerV1.Sniffer.Crypto;

public sealed class Rc4Cipher
{
    private readonly byte[] _state = new byte[256];
    private int _x;
    private int _y;

    // Domyślne klucze strumienia RotMG Exalt (Incoming: Server -> Client, Outgoing: Client -> Server)
    public const string ServerToClientKeyHex = "c91d9eec420160730d825604e0";
    public const string ClientToServerKeyHex = "5a4d2016bc16dc64883194ffd9";

    public Rc4Cipher(string hexKey) : this(Convert.FromHexString(hexKey))
    {
    }

    public Rc4Cipher(byte[] key)
    {
        for (int i = 0; i < 256; i++)
            _state[i] = (byte)i;

        int j = 0;
        for (int i = 0; i < 256; i++)
        {
            j = (j + _state[i] + key[i % key.Length]) & 0xFF;
            (_state[i], _state[j]) = (_state[j], _state[i]);
        }
    }

    public void Cipher(Span<byte> data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            _x = (_x + 1) & 0xFF;
            _y = (_y + _state[_x]) & 0xFF;
            (_state[_x], _state[_y]) = (_state[_y], _state[_x]);

            int xorIndex = (_state[_x] + _state[_y]) & 0xFF;
            data[i] ^= _state[xorIndex];
        }
    }
}