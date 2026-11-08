using System.Globalization;
using System.Text;
using RotmgTrackerV1.Sniffer.Protocol;

namespace RotmgTrackerV1.Sniffer.Diagnostics;

/// <summary>
/// Tryb przechwytywania (diagnostyka). Zapisuje do pliku tekstowego metadane ramek RotMG,
/// zeby na prawdziwym ruchu sprawdzic, czy ID pakietow, klucze RC4 i parsery sa poprawne.
///
/// Prywatnosc:
///  - pakiety klient -> serwer: tylko 8 pierwszych bajtow, i tylko dla pakietu "Load";
///  - pakiety serwer -> klient: tresc tylko dla ID, ktore aplikacja parsuje (MapInfo, CreateSuccess,
///    Update, NewTick, Damage, Death);
///  - dla pozostalych ID zapisywana jest tylko dlugosc ramki.
/// Przed wyslaniem pliku nikomu warto go przejrzec, bo zle ID moga wskazac inny pakiet niz zakladamy.
/// </summary>
public sealed class PacketCaptureLogger : IDisposable
{
    private const int MaxLinesPerPacketId = 150;
    private const int MaxBadFrameLines = 50;
    private const int MaxEventLines = 200;
    private const int FullBodyIfShorterThan = 400;
    private const int TruncatedServerBodyBytes = 96;
    private const int ClientBodyBytes = 8;
    private const int SummaryEveryFrames = 100;

    private sealed class FrameStats
    {
        public int Count;
        public long TotalBytes;
        public int MinLength = int.MaxValue;
        public int MaxLength;
        public int LinesWritten;
    }

    private readonly object _lock = new();
    private readonly Dictionary<(bool Server, byte Id), FrameStats> _stats = new();
    private StreamWriter? _writer;
    private string? _summaryPath;
    private int _totalFrames;
    private int _badFrames;
    private int _streamResets;
    private int _eventLines;

    public bool IsActive { get; private set; }
    public string? FilePath { get; private set; }

    public void Start()
    {
        lock (_lock)
        {
            if (IsActive) return;

            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RotmgTracker",
                "captures");
            Directory.CreateDirectory(dir);

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            FilePath = Path.Combine(dir, $"capture_{stamp}.txt");
            _summaryPath = Path.Combine(dir, $"capture_{stamp}_summary.txt");

            _writer = new StreamWriter(FilePath, append: false, Encoding.UTF8) { AutoFlush = true };
            _stats.Clear();
            _totalFrames = 0;
            _badFrames = 0;
            _streamResets = 0;
            _eventLines = 0;

            _writer.WriteLine("# RotMG Tracker - capture log (diagnostics only)");
            _writer.WriteLine("# format: time | S->C or C->S | packet id | frame length | body hex (selected ids, truncated)");
            IsActive = true;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsActive) return;

            WriteSummaryLocked();
            _writer?.Dispose();
            _writer = null;
            IsActive = false;
        }
    }

    public void Note(string text)
    {
        if (!IsActive) return;

        lock (_lock)
        {
            if (_writer == null) return;
            if (text == "STREAM_RESET") _streamResets++;
            if (_eventLines++ >= MaxEventLines) return;

            _writer.WriteLine($"{Now()} | EVENT | {text}");
        }
    }

    public void LogBadFrame(bool isServerToClient, int bufferedBytes, byte[] firstBytes)
    {
        if (!IsActive) return;

        lock (_lock)
        {
            if (_writer == null) return;
            _badFrames++;
            if (_badFrames > MaxBadFrameLines) return;

            _writer.WriteLine(
                $"{Now()} | {Dir(isServerToClient)} | BAD_FRAME | buffered={bufferedBytes} | head={Convert.ToHexString(firstBytes)}");
        }
    }

    public void LogFrame(bool isServerToClient, byte packetId, int frameLength, ReadOnlySpan<byte> decryptedBody)
    {
        if (!IsActive) return;

        lock (_lock)
        {
            if (_writer == null) return;

            var key = (isServerToClient, packetId);
            if (!_stats.TryGetValue(key, out var stats))
            {
                stats = new FrameStats();
                _stats[key] = stats;
            }

            stats.Count++;
            stats.TotalBytes += frameLength;
            stats.MinLength = Math.Min(stats.MinLength, frameLength);
            stats.MaxLength = Math.Max(stats.MaxLength, frameLength);
            _totalFrames++;

            if (stats.LinesWritten < MaxLinesPerPacketId)
            {
                stats.LinesWritten++;

                string body = string.Empty;
                if (IsBodyAllowed(isServerToClient, packetId))
                {
                    int take = isServerToClient
                        ? (decryptedBody.Length < FullBodyIfShorterThan ? decryptedBody.Length : TruncatedServerBodyBytes)
                        : ClientBodyBytes;
                    take = Math.Min(take, decryptedBody.Length);
                    body = Convert.ToHexString(decryptedBody[..take]);
                    if (take < decryptedBody.Length) body += "...";
                }

                _writer.WriteLine($"{Now()} | {Dir(isServerToClient)} | {packetId} | {frameLength} | {body}");
            }

            if (_totalFrames % SummaryEveryFrames == 0)
                WriteSummaryLocked();
        }
    }

    private void WriteSummaryLocked()
    {
        if (_summaryPath == null) return;

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"frames_decoded={_totalFrames} bad_frames={_badFrames} stream_resets={_streamResets}");
            sb.AppendLine("dir | id | count | minLen | maxLen | avgLen | assumed meaning (unverified)");

            foreach (var kv in _stats.OrderBy(k => !k.Key.Server).ThenBy(k => k.Key.Id))
            {
                var s = kv.Value;
                sb.AppendLine(
                    $"{Dir(kv.Key.Server)} | {kv.Key.Id} | {s.Count} | {s.MinLength} | {s.MaxLength} | {s.TotalBytes / Math.Max(1, s.Count)} | {Describe(kv.Key.Server, kv.Key.Id)}");
            }

            File.WriteAllText(_summaryPath, sb.ToString());
        }
        catch
        {
            // diagnostyka nie moze wywrocic aplikacji
        }
    }

    private static bool IsBodyAllowed(bool isServerToClient, byte id) =>
        isServerToClient
            ? id is RotmgProtocolConstants.PacketMapInfo
                or RotmgProtocolConstants.PacketUpdate
                or RotmgProtocolConstants.PacketNewTick
                or RotmgProtocolConstants.PacketDamage
                or RotmgProtocolConstants.PacketDeath
            : id == RotmgProtocolConstants.PacketLoad;

    private static string Describe(bool isServerToClient, byte id)
    {
        if (isServerToClient)
        {
            return id switch
            {
                RotmgProtocolConstants.PacketMapInfo => "MapInfo?",
                RotmgProtocolConstants.PacketCreateSuccess => "CreateSuccess?",
                RotmgProtocolConstants.PacketUpdate => "Update?",
                RotmgProtocolConstants.PacketNewTick => "NewTick?",
                RotmgProtocolConstants.PacketDamage => "Damage?",
                RotmgProtocolConstants.PacketDeath => "Death?",
                _ => string.Empty
            };
        }

        return id switch
        {
            RotmgProtocolConstants.PacketLoad => "Load?",
            RotmgProtocolConstants.PacketInvDrop => "InvDrop?",
            _ => string.Empty
        };
    }

    private static string Dir(bool isServerToClient) => isServerToClient ? "S->C" : "C->S";

    private static string Now() => DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    public void Dispose() => Stop();
}