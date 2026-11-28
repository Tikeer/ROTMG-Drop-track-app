using PacketDotNet;
using RotmgTrackerV1.Core.Enums;
using RotmgTrackerV1.Core.Services;
using RotmgTrackerV1.Sniffer.Diagnostics;
using RotmgTrackerV1.Sniffer.Protocol;
using SharpPcap;

namespace RotmgTrackerV1.Sniffer.Services;

public class RotmgSnifferService : IDisposable
{
    private readonly TrackerStateService _state;
    private readonly GameDataService _gameData;
    private readonly RotmgStreamProcessor _streamProcessor;
    private readonly List<ICaptureDevice> _activeDevices = new();

    public bool IsListening { get; private set; }
    public string StatusMessage { get; private set; } = "Sniffer Idle";
    public int PacketsCaptured { get; private set; }

    public PacketCaptureLogger Capture { get; } = new();
    public bool CaptureEnabled => Capture.IsActive;

    /// <summary>Optional D17 predicate controlling which physical bag colors are recorded to LootHistory.</summary>
    public Func<BagType, bool>? ShouldRecordBag
    {
        get => _streamProcessor.ShouldRecordBag;
        set => _streamProcessor.ShouldRecordBag = value;
    }

    // Wlacza/wylacza zapis diagnostyki ramek do pliku (patrz PacketCaptureLogger)
    public void ToggleCapture()
    {
        if (Capture.IsActive)
        {
            _streamProcessor.Capture = null;
            Capture.Stop();
            StatusMessage = $"Capture saved: {Capture.FilePath}";
        }
        else
        {
            Capture.Start();
            _streamProcessor.Capture = Capture;
            StatusMessage = $"Capturing to: {Capture.FilePath}";
        }
    }

    public RotmgSnifferService(TrackerStateService state, GameDataService gameData)
    {
        _state = state;
        _gameData = gameData;
        _streamProcessor = new RotmgStreamProcessor(state, gameData);
    }

    public void StartListening()
    {
        if (IsListening) return;

        try
        {
            var devices = CaptureDeviceList.Instance;
            if (devices.Count == 0)
            {
                StatusMessage = OperatingSystem.IsWindows()
                    ? "No capture adapters found — install Npcap (npcap.com) with WinPcap API-compatible mode enabled."
                    : OperatingSystem.IsMacOS()
                        ? "No network interfaces found — install ChmodBPF: brew install --cask wireshark-chmodbpf"
                        : "No network interfaces found — ensure libpcap is installed and capture permissions are granted.";
                return;
            }

            string? lastOpenError = null;
            foreach (var dev in devices)
            {
                try
                {
                    dev.OnPacketArrival += OnPacketArrival;
                    dev.Open(DeviceModes.Promiscuous, 1000);
                    dev.Filter = "tcp port 2050";
                    dev.StartCapture();
                    _activeDevices.Add(dev);
                }
                catch (Exception ex)
                {
                    lastOpenError = ex.Message;
                }
            }

            if (_activeDevices.Count > 0)
            {
                IsListening = true;
                StatusMessage = $"Listening passively on TCP:2050 ({_activeDevices.Count} interfaces)";
            }
            else
            {
                StatusMessage = OperatingSystem.IsWindows()
                    ? $"Could not open capture adapter — verify Npcap is installed (npcap.com) or run as Administrator. ({lastOpenError ?? "access denied"})"
                    : OperatingSystem.IsMacOS()
                        ? "No BPF capture permission — run in Terminal: brew install --cask wireshark-chmodbpf (then reboot or re-login)."
                        : $"Capture permission denied — grant cap_net_raw/cap_net_admin or run with sudo. ({lastOpenError ?? "permission denied"})";
            }
        }
        catch (DllNotFoundException)
        {
            StatusMessage = OperatingSystem.IsWindows()
                ? "Npcap driver (wpcap.dll) not found — install Npcap from https://npcap.com (enable WinPcap API-compatible mode)."
                : "libpcap library not found — install libpcap for passive packet capture.";
        }
        catch (Exception ex)
        {
            StatusMessage = OperatingSystem.IsWindows() && ex.Message.Contains("wpcap", StringComparison.OrdinalIgnoreCase)
                ? "Npcap driver not found — install Npcap from https://npcap.com (enable WinPcap API-compatible mode)."
                : $"Sniffer Error: {ex.Message}";
        }
    }

    public void StopListening()
    {
        foreach (var dev in _activeDevices)
        {
            try
            {
                dev.OnPacketArrival -= OnPacketArrival;
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
        try
        {
            var rawCapture = e.GetPacket();
            var packet = Packet.ParsePacket(rawCapture.LinkLayerType, rawCapture.Data);
            var tcpPacket = packet.Extract<TcpPacket>();
            var ipPacket = packet.Extract<IPPacket>();
            if (tcpPacket == null || ipPacket == null) return;

            bool isServerToClient = tcpPacket.SourcePort == 2050;

            // Klucz połączenia: klient (adres:port) + adres serwera, identyczny w obu kierunkach
            string flowKey = isServerToClient
                ? $"{ipPacket.DestinationAddress}:{tcpPacket.DestinationPort}-{ipPacket.SourceAddress}"
                : $"{ipPacket.SourceAddress}:{tcpPacket.SourcePort}-{ipPacket.DestinationAddress}";

            if (tcpPacket.Synchronize)
            {
                _streamProcessor.StartFlow(flowKey);
                _streamProcessor.NoteSynSequence(flowKey, isServerToClient, tcpPacket.SequenceNumber);
                return;
            }

            if (tcpPacket.PayloadData == null || tcpPacket.PayloadData.Length == 0)
                return;

            PacketsCaptured++;

            _streamProcessor.ProcessTcpPayload(
                tcpPacket.PayloadData,
                tcpPacket.SequenceNumber,
                isServerToClient,
                flowKey);
        }
        catch (Exception ex)
        {
            Capture.Note($"SNIFFER_EXCEPTION: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Jedyny pozostawiony tester: symulacja obrażeń na bossie do weryfikacji Live DPS
    public void SimulateDungeonDpsFight()
    {
        PacketsCaptured += 5;
        if (_state.CurrentDungeonPlayers.Count == 0)
        {
            _state.EnterDungeon("Oryx's Sanctuary", 9.5);
        }

        string myName = string.IsNullOrEmpty(_state.LinkedAccountName) ? "Tikeer" : _state.LinkedAccountName;
        _state.RecordPlayerDamage(101, myName, _state.ActiveCharacter.Class, Random.Shared.Next(18000, 42000));
        _state.RecordPlayerDamage(102, "UmiEnjoyer", RotmgClass.Warrior, Random.Shared.Next(15000, 38000));
        _state.RecordPlayerDamage(103, "RealmHoundFan", RotmgClass.Paladin, Random.Shared.Next(12000, 29000));
        _state.RecordPlayerDamage(104, "BlueStarKnight", RotmgClass.Knight, Random.Shared.Next(6000, 16000));
    }

    public void Dispose()
    {
        StopListening();
        Capture.Dispose();
    }
}