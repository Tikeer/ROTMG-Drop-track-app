using System.Globalization;
using System.Text;
using RotmgTrackerV1.Sniffer.Protocol;

namespace RotmgTrackerV1.Sniffer.Diagnostics;

/// <summary>
/// Tryb przechwytywania (diagnostyka). Zapisuje do pliku tekstowego metadane ramek RotMG,
/// zeby na prawdziwym ruchu sprawdzic, czy ID pakietow, klucze RC4 i parsery sa poprawne.
///
/// Dwa pliki:
///  - capture_*.txt: metadane ramek, tresc tylko dla ID, ktore aplikacja parsuje (jak dotad);
///  - fulldump_*.txt (FullDump): pelne, ZDEKODOWANE pakiety serwera (XML postaci, Vault, exaltacje, czat...)
///    oraz zmiany statow lokalnego gracza. Plik zawiera dane konta i tresc czatu - NIE wysylac go nikomu
///    w calosci, tylko wyniki komend grep.
/// </summary>
public sealed class PacketCaptureLogger : IDisposable
{
    private const int MaxLinesPerPacketId = 1500;
    private const int MaxBadFrameLines = 50;
    private const int MaxEventLinesTotal = 8000;
    private const int MaxEventLinesPerCategory = 1500;
    private const int FullBodyIfShorterThan = 400;
    private const int TruncatedServerBodyBytes = 96;
    private const int ClientBodyBytes = 8;
    private const int SummaryEveryFrames = 100;

    // FullDump
    private const int DefaultDumpLimitPerId = 25;
    private const int DumpHexMaxBytes = 4096;
    private const int MaxLocalStatLines = 3000;
    private const long MaxDumpBytes = 40L * 1024 * 1024;

    // Glosne pakiety, ktorych nie zrzucamy (Update, NewTick, Damage, DamageWithEffect, MapInfo, CreateSuccess, Death, RealmScore)
    private static readonly HashSet<byte> DumpExcludedServerIds = new() { 10, 42, 46, 75, 92, 101, 166, 169 };

    private static readonly Dictionary<byte, int> DumpLimits = new()
    {
        [108] = 20,
        [117] = 80,
        [114] = 80,
        [120] = 40,
        [139] = 80,
        [44] = 400
    };

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
    private StreamWriter? _dumpWriter;
    private string? _summaryPath;
    private int _totalFrames;
    private int _badFrames;
    private int _streamResets;
    private int _eventLines;
    private long _dumpBytes;
    private int _localStatLines;
    private readonly Dictionary<string, int> _eventAttempts = new();
    private readonly Dictionary<byte, int> _dumpCounts = new();
    private readonly Dictionary<int, string> _lastLocalStats = new();

    public bool IsActive { get; private set; }
    public string? FilePath { get; private set; }
    public string? DumpFilePath { get; private set; }

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
            DumpFilePath = Path.Combine(dir, $"fulldump_{stamp}.txt");
            _summaryPath = Path.Combine(dir, $"capture_{stamp}_summary.txt");

            _writer = new StreamWriter(FilePath, append: false, Encoding.UTF8) { AutoFlush = true };
            _dumpWriter = new StreamWriter(DumpFilePath, append: false, Encoding.UTF8) { AutoFlush = true };
            _stats.Clear();
            _eventAttempts.Clear();
            _dumpCounts.Clear();
            _lastLocalStats.Clear();
            _totalFrames = 0;
            _badFrames = 0;
            _streamResets = 0;
            _eventLines = 0;
            _dumpBytes = 0;
            _localStatLines = 0;

            _writer.WriteLine("# RotMG Tracker - capture log (diagnostics only)");
            _writer.WriteLine("# format: time | S->C or C->S | packet id | frame length | body hex (selected ids, truncated)");
            _dumpWriter.WriteLine("# RotMG Tracker - FullDump (decoded server packets + local player stat changes)");
            _dumpWriter.WriteLine("# format: time | S->C | packet id | frame length | #n | decoded [| hex=...]");
            _dumpWriter.WriteLine("# local stats: time | LOCAL_STATS | changed=N | statId:value ... (first frame = full snapshot, later = changes only)");
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
            _dumpWriter?.Dispose();
            _dumpWriter = null;
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

            string category = GetEventCategory(text);
            _eventAttempts.TryGetValue(category, out int attempts);
            _eventAttempts[category] = attempts + 1;

            if (attempts >= MaxEventLinesPerCategory || _eventLines >= MaxEventLinesTotal) return;

            _eventLines++;
            _writer.WriteLine($"{Now()} | EVENT | {text}");
        }
    }

    private static string GetEventCategory(string text)
    {
        int end = text.IndexOfAny([':', ' ']);
        return end > 0 ? text[..end] : text;
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

            if (isServerToClient)
                DumpServerFrameLocked(packetId, frameLength, decryptedBody);

            if (stats.LinesWritten < MaxLinesPerPacketId)
            {
                stats.LinesWritten++;

                string body = string.Empty;
                if (IsBodyAllowed(isServerToClient, packetId))
                {
                    // 183 (JSON Crucible) i 101 (CreateSuccess) zawsze w calosci, bez ucinania do 96 B
                    bool fullBody = isServerToClient &&
                                    (packetId == 183 || packetId == RotmgProtocolConstants.PacketCreateSuccess);

                    int take;
                    if (!isServerToClient)
                        take = ClientBodyBytes;
                    else if (fullBody || decryptedBody.Length < FullBodyIfShorterThan)
                        take = decryptedBody.Length;
                    else
                        take = TruncatedServerBodyBytes;

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
            sb.AppendLine($"event_lines_written={_eventLines} (cap {MaxEventLinesTotal})");
            foreach (var kv in _eventAttempts.Where(k => k.Value > MaxEventLinesPerCategory).OrderBy(k => k.Key))
                sb.AppendLine($"event_truncated: {kv.Key} attempts={kv.Value} cap={MaxEventLinesPerCategory}");
            sb.AppendLine($"dump_bytes={_dumpBytes} (cap {MaxDumpBytes}) local_stat_lines={_localStatLines} (cap {MaxLocalStatLines})");
            foreach (var kv in _dumpCounts.OrderBy(k => k.Key))
            {
                int limit = DumpLimits.GetValueOrDefault(kv.Key, DefaultDumpLimitPerId);
                sb.AppendLine($"dump_id={kv.Key} seen={kv.Value} written={Math.Min(kv.Value, limit)} limit={limit}");
            }
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
    
        /// <summary>
    /// Zapisuje do FullDump zmiany statow lokalnego gracza (pierwsza ramka = pelny snapshot, potem tylko zmiany).
    /// Dzieki temu widac, KIEDY pojawiaja sie staty typu Dust (127/147), Material (71/72), boosty (65-68) itd.
    /// </summary>
    public void LogLocalPlayerStats(IReadOnlyList<StatEntry> stats)
    {
        if (!IsActive) return;

        lock (_lock)
        {
            if (_dumpWriter == null || _localStatLines >= MaxLocalStatLines || _dumpBytes >= MaxDumpBytes) return;

            var changes = new List<string>();
            foreach (var s in stats)
            {
                string value;
                if (s.StatType is 38 or 54)
                    value = "<id>";
                else
                    value = string.IsNullOrEmpty(s.StringValue)
                        ? s.IntValue.ToString(CultureInfo.InvariantCulture)
                        : $"\"{s.StringValue}\"";

                if (_lastLocalStats.TryGetValue(s.StatType, out var previous) && previous == value) continue;

                _lastLocalStats[s.StatType] = value;
                changes.Add($"{s.StatType}:{value}");
            }

            if (changes.Count == 0) return;

            _localStatLines++;
            string line = $"{Now()} | LOCAL_STATS | changed={changes.Count} | {string.Join(" ", changes)}";
            _dumpBytes += line.Length + 2;
            _dumpWriter.WriteLine(line);
        }
    }

    private void DumpServerFrameLocked(byte packetId, int frameLength, ReadOnlySpan<byte> body)
    {
        if (_dumpWriter == null || _dumpBytes >= MaxDumpBytes) return;
        if (DumpExcludedServerIds.Contains(packetId)) return;

        _dumpCounts.TryGetValue(packetId, out int seen);
        _dumpCounts[packetId] = seen + 1;

        int limit = DumpLimits.GetValueOrDefault(packetId, DefaultDumpLimitPerId);
        if (seen >= limit) return;

        string? decoded;
        try
        {
            decoded = DecodeDump(packetId, body);
        }
        catch (Exception ex)
        {
            decoded = $"DECODE_FAILED {ex.GetType().Name}";
        }

        // XML postaci (108) i czat (44) sa tekstem - hex tylko by dublowal rozmiar
        bool withHex = decoded == null || packetId is not (108 or 44);

        var sb = new StringBuilder();
        sb.Append($"{Now()} | S->C | {packetId} | {frameLength} | #{seen + 1} | {decoded ?? string.Empty}");
        if (withHex)
        {
            sb.Append(" | hex=");
            if (body.Length <= DumpHexMaxBytes)
                sb.Append(Convert.ToHexString(body));
            else
                sb.Append(Convert.ToHexString(body[..DumpHexMaxBytes])).Append("...");
        }

        string line = sb.ToString();
        _dumpBytes += line.Length + 2;
        _dumpWriter.WriteLine(line);
    }

    /// <summary>
    /// Dekoduje pakiety, ktorych strukture znamy z definicji protokolu RealmShark/RealmHound (do potwierdzenia na capture).
    /// Zwraca null, gdy nie ma dekodera (wtedy zapisywany jest sam hex).
    /// </summary>
    private static string? DecodeDump(byte id, ReadOnlySpan<byte> body)
    {
        var r = new RotmgPacketReader(body);

        switch (id)
        {
            case 108:
            {
                short size = r.ReadInt16();
                string xml = Encoding.UTF8.GetString(body[2..]).Replace("\r", string.Empty).Replace("\n", "\\n");
                return $"NewCharacterInfo size={size} xmlLen={body.Length - 2} xml={xml}";
            }

            case 114:
            {
                short objType = r.ReadInt16();
                int dex = r.ReadCompressedInt();
                int spd = r.ReadCompressedInt();
                int vit = r.ReadCompressedInt();
                int wis = r.ReadCompressedInt();
                int def = r.ReadCompressedInt();
                int att = r.ReadCompressedInt();
                int mp = r.ReadCompressedInt();
                int hp = r.ReadCompressedInt();
                string cls = RotmgProtocolConstants.TryGetPlayerClass(objType, out var c) ? c.ToString() : "?";
                return $"Exaltation class=0x{(ushort)objType:X4}({cls}) DEX={dex} SPD={spd} VIT={vit} WIS={wis} DEF={def} ATT={att} MP={mp} HP={hp} tail={r.Remaining}";
            }

            case 117:
            {
                bool last = r.ReadBool();
                var objIds = new int[5];
                for (int i = 0; i < objIds.Length; i++) objIds[i] = r.ReadCompressedInt();

                var sb = new StringBuilder($"VaultUpdate last={last} objIds=[{string.Join(",", objIds)}]");
                string[] names = ["vault", "material", "gift", "potion", "spoils"];
                for (int a = 0; a < names.Length; a++)
                {
                    int len = r.ReadCompressedInt();
                    if (len < 0 || len > 5000)
                    {
                        sb.Append($" {names[a]}=BAD_LEN({len})");
                        return sb.ToString();
                    }

                    var items = new int[len];
                    for (int i = 0; i < len; i++) items[i] = r.ReadCompressedInt();
                    sb.Append($" {names[a]}[{len}, filled={items.Count(x => x >= 0)}]={string.Join(",", items)}");
                }

                short vaultCost = r.ReadInt16();
                short materialCost = r.ReadInt16();
                short potionCost = r.ReadInt16();
                short potionMax = r.ReadInt16();
                short nextPotionMax = r.ReadInt16();
                short unknownShort = r.ReadInt16();
                sb.Append($" costs=[vault {vaultCost}, material {materialCost}, potion {potionCost}] potionMax={potionMax}->{nextPotionMax} unk={unknownShort}");

                for (int i = 0; i < 3; i++)
                {
                    string enc = r.ReadUtfString();
                    sb.Append($" enchants{i}Len={enc.Length}");
                }

                sb.Append($" tail={r.Remaining}");
                return sb.ToString();
            }

            case 120:
            {
                byte seasonal = r.ReadByte();
                int len = r.ReadCompressedInt();
                if (len < 0 || len > 5000) return $"ForgeBlueprints seasonal={seasonal} BAD_LEN({len})";

                var ids = new int[len];
                for (int i = 0; i < len; i++) ids[i] = r.ReadCompressedInt();
                return $"ForgeBlueprints seasonal={seasonal} count={len} ids={string.Join(",", ids)} tail={r.Remaining}";
            }

            case 139:
            {
                int charId = r.ReadCompressedInt();
                sbyte hp = (sbyte)r.ReadByte();
                sbyte mp = (sbyte)r.ReadByte();
                sbyte att = (sbyte)r.ReadByte();
                sbyte def = (sbyte)r.ReadByte();
                sbyte spd = (sbyte)r.ReadByte();
                sbyte vit = (sbyte)r.ReadByte();
                sbyte wis = (sbyte)r.ReadByte();
                sbyte dex = (sbyte)r.ReadByte();
                return $"Stats charId={charId} HP={hp} MP={mp} ATT={att} DEF={def} SPD={spd} VIT={vit} WIS={wis} DEX={dex} tail={r.Remaining}";
            }

            case 44:
            {
                string name = r.ReadUtfString();
                int objectId = r.ReadInt32();
                short stars = r.ReadInt16();
                byte bubble = r.ReadByte();
                string recipient = r.ReadUtfString();
                string text = r.ReadUtfString();
                return $"Text name=\"{name}\" objId={objectId} stars={stars} bubble={bubble} recipient=\"{recipient}\" text=\"{text}\"";
            }

            default:
                return null;
        }
    }

    private static bool IsBodyAllowed(bool isServerToClient, byte id) =>
        isServerToClient
            ? id is RotmgProtocolConstants.PacketMapInfo
                or RotmgProtocolConstants.PacketCreateSuccess
                or RotmgProtocolConstants.PacketUpdate
                or RotmgProtocolConstants.PacketNewTick
                or RotmgProtocolConstants.PacketDamage
                or RotmgProtocolConstants.PacketDeath
                or 183
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
                183 => "CrucibleResponse (JSON)",
                108 => "NewCharacterInfo (XML)?",
                114 => "ExaltationBonusChanged?",
                117 => "VaultUpdate?",
                119 => "ForgeResult?",
                120 => "ForgeUnlockedBlueprints?",
                139 => "Stats (charId + 8 x sbyte)?",
                44 => "Text (chat)",
                6 => "QuestFetchResponse?",
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