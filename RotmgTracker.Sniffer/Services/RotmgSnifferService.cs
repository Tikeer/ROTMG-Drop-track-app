using PacketDotNet;
using RotmgTrackerV1.Core.Enums;
using RotmgTrackerV1.Core.Services;
using RotmgTrackerV1.Sniffer.Crypto;
using SharpPcap;

namespace RotmgTrackerV1.Sniffer.Services;

public class RotmgSnifferService : IDisposable
{
    private readonly TrackerStateService _state;
    private readonly List<ICaptureDevice> _activeDevices = new();
    private Rc4Cipher _incomingCipher = new(Rc4Cipher.ServerToClientKeyHex);

    public bool IsListening { get; private set; }
    public string StatusMessage { get; private set; } = "Sniffer Idle";
    public int PacketsCaptured { get; private set; }

    public RotmgSnifferService(TrackerStateService state)
    {
        _state = state;
    }

    public void StartListening()
    {
        if (IsListening) return;

        try
        {
            var devices = CaptureDeviceList.Instance;
            if (devices.Count == 0)
            {
                StatusMessage = "No network interfaces found (check chmodbpf / Npcap)";
                return;
            }

            foreach (var dev in devices)
            {
                try
                {
                    dev.OnPacketArrival += OnPacketArrival;
                    dev.Open(DeviceModes.Promiscuous, 1000);
                    // Pasywny filtr BPF: przechwytuje wyłącznie pakiety gry RotMG (TCP port 2050)
                    dev.Filter = "tcp port 2050";
                    dev.StartCapture();
                    _activeDevices.Add(dev);
                }
                catch
                {
                    // Ignoruj nieaktywne lub wirtualne interfejsy bez uprawnień
                }
            }

            if (_activeDevices.Count > 0)
            {
                IsListening = true;
                StatusMessage = $"Listening passively on port 2050 ({_activeDevices.Count} interfaces)";
            }
            else
            {
                StatusMessage = "Permission denied on /dev/bpf (Run: brew install --cask wireshark-chmodbpf)";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Sniffer Error: {ex.Message}";
        }
    }

    public void StopListening()
    {
        foreach (var dev in _activeDevices)
        {
            try
            {
                dev.StopCapture();
                dev.Close();
            }
            catch { }
        }

        _activeDevices.Clear();
        IsListening = false;
        StatusMessage = "Sniffer Stopped";
    }

    private void OnPacketArrival(object sender, PacketCapture e)
    {
        var rawCapture = e.GetPacket();
        var packet = Packet.ParsePacket(rawCapture.LinkLayerType, rawCapture.Data);
        var tcpPacket = packet.Extract<TcpPacket>();

        if (tcpPacket == null || tcpPacket.PayloadData == null || tcpPacket.PayloadData.Length == 0)
            return;

        // Nowa sesja TCP (wejście na serwer / do dungeona) -> reset klucza strumienia RC4
        if (tcpPacket.Synchronize)
        {
            _incomingCipher = new Rc4Cipher(Rc4Cipher.ServerToClientKeyHex);
            return;
        }

        if (tcpPacket.SourcePort == 2050)
        {
            PacketsCaptured++;
            ProcessServerStream(tcpPacket.PayloadData);
        }
    }

    private void ProcessServerStream(byte[] payload)
    {
        // W pełnej pętli tutaj składamy ramki [4B Length][1B PacketId][Payload] i deszyfrujemy przez _incomingCipher
        if (payload.Length < 5) return;

        Span<byte> body = payload.AsSpan(5);
        _incomingCipher.Cipher(body);
    }

    // --- SYMULATOR PAKIETÓW DO TESTÓW NA MACU BEZ WŁĄCZONEJ GRY ---
    public void SimulateNexusDetection(string playerNick = "Tikeer", RotmgClass charClass = RotmgClass.Wizard)
    {
        PacketsCaptured++;
        _state.HandleNexusPlayerDetected(playerNick, charClass, true);
    }

    public void SimulateBagDrop()
    {
        PacketsCaptured++;
        var sampleItems = new (string Name, int Id, DropType Bag, string Dungeon)[]
        {
            ("Divinity", 0x0912, DropType.RegularWhite, "Oryx's Sanctuary"),
            ("Doom Bow", 0x0c02, DropType.RegularWhite, "Undead Lair"),
            ("Shiny Doom Bow", 0x7c02, DropType.Shiny, "Undead Lair"),
            ("Void Bow", 0x2366, DropType.LegendaryWhite, "The Void"),
            ("Pixie-Enchanted Sword", 0x0976, DropType.ST, "Candyland")
        };

        var pick = sampleItems[Random.Shared.Next(sampleItems.Length)];
        _state.RecordLootDrop(pick.Name, pick.Id, pick.Bag, pick.Dungeon);
    }

    public void SimulateDungeonDpsFight()
    {
        PacketsCaptured += 5;
        if (_state.CurrentDungeonPlayers.Count == 0)
        {
            _state.EnterDungeon("Oryx's Sanctuary (O3)");
        }

        string myName = string.IsNullOrEmpty(_state.LinkedAccountName) ? "Tikeer" : _state.LinkedAccountName;
        _state.RecordPlayerDamage(101, myName, _state.ActiveCharacter.Class, Random.Shared.Next(18000, 42000));
        _state.RecordPlayerDamage(102, "UmiEnjoyer", RotmgClass.Warrior, Random.Shared.Next(15000, 38000));
        _state.RecordPlayerDamage(103, "RealmHoundFan", RotmgClass.Paladin, Random.Shared.Next(12000, 29000));
        _state.RecordPlayerDamage(104, "BlueStarKnight", RotmgClass.Knight, Random.Shared.Next(6000, 16000));
    }

    public void Dispose() => StopListening();
}