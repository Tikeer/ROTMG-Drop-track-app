using RotmgTrackerV1.Core.Enums;
using RotmgTrackerV1.Core.Services;
using RotmgTrackerV1.Sniffer.Crypto;
using RotmgTrackerV1.Sniffer.Diagnostics;

namespace RotmgTrackerV1.Sniffer.Protocol;

public class RotmgStreamProcessor
{
    private sealed class TrackedMapEnemy
    {
        public EnemyMetadata Metadata { get; init; } = null!;
        public float X { get; set; }
        public float Y { get; set; }
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    }

    private sealed class TrackedLootBag
    {
        public Guid BagInstanceId { get; init; } = Guid.NewGuid();
        public int BagObjectType { get; init; }
        public float X { get; init; }
        public float Y { get; init; }
        public Guid? CombatSessionId { get; set; }
        public bool KillHandled { get; set; }
        public DateTime SpawnedUtc { get; init; } = DateTime.UtcNow;
    }

    private sealed class KillCandidate
    {
        public int ObjectId { get; init; }
        public EnemyMetadata Meta { get; init; } = null!;
        public float X { get; init; }
        public float Y { get; init; }
        public DateTime AtUtc { get; init; } = DateTime.UtcNow;
        public bool Consumed { get; set; }
    }

    private readonly TrackerStateService _state;
    private readonly GameDataService _gameData;

    private Rc4Cipher _serverCipher = new(Rc4Cipher.ServerToClientKeyHex);
    private Rc4Cipher _clientCipher = new(Rc4Cipher.ClientToServerKeyHex);

    private readonly List<byte> _serverBuffer = new(65536);
    private readonly List<byte> _clientBuffer = new(8192);

    private uint? _expectedServerSeq;
    private uint? _expectedClientSeq;
    private readonly object _streamLock = new();
    private bool _serverStreamBroken;
    private bool _clientStreamBroken;

    private int _myObjectId = -1;
    private int _currentCharId = 0;
    private DateTime _lastPlayerInvDropTime = DateTime.MinValue;

    private readonly HashSet<(int BagObjectId, int Slot, int ItemId)> _loggedBagItems = new();
    private readonly Dictionary<int, TrackedLootBag> _activeBagsOnMap = new();
    private readonly Dictionary<int, (string Name, RotmgClass Class)> _playersOnMap = new();
    private readonly Dictionary<int, TrackedMapEnemy> _enemiesOnMap = new();
    private EnemyMetadata? _lastDamagedBossOnMap;
    private const double KillBagWindowMs = 1500.0;
    private const float KillRadiusSq = 15f * 15f;
    private const int StatSeasonalId = 24;

    private readonly List<KillCandidate> _killCandidates = new();
    private (float X, float Y, DateTime AtUtc, Guid? SessionId, EnemyMetadata Meta)? _lastConfirmedKill;
    private bool _instanceKillConfirmed;
    private string? _completionDungeon;
    private string? _activeFlow;
    private double _completionGraves;
    private bool? _localSeasonal;
    
    private readonly LocalPlayerStatTracker _localStats = new();
    private RotmgClass? _localClass;
    private int _lastLoggedMaxed = -1;

    // Tryb przechwytywania (diagnostyka); null = wylaczony
    public PacketCaptureLogger? Capture { get; set; }

    public int DecodedPacketsCount { get; private set; }

    public RotmgStreamProcessor(TrackerStateService state, GameDataService gameData)
    {
        _state = state;
        _gameData = gameData;
    }

        

        public void StartFlow(string flowKey)
    {
        lock (_streamLock)
        {
            
            if (_activeFlow == flowKey)
                return; // ten sam SYN przyszedł drugim interfejsem
            
            Capture?.Note($"STREAM_RESET: flow={flowKey}");
            CommitInstanceCompletion();

            _activeFlow = flowKey;
            _serverCipher = new Rc4Cipher(Rc4Cipher.ServerToClientKeyHex);
            _clientCipher = new Rc4Cipher(Rc4Cipher.ClientToServerKeyHex);
            _serverBuffer.Clear();
            _clientBuffer.Clear();
            _expectedServerSeq = null;
            _expectedClientSeq = null;
            _serverStreamBroken = false;
            _clientStreamBroken = false;
            _loggedBagItems.Clear();
            _activeBagsOnMap.Clear();
            _playersOnMap.Clear();
            _enemiesOnMap.Clear();
            _killCandidates.Clear();
            _lastConfirmedKill = null;
            _lastDamagedBossOnMap = null;
            _localSeasonal = null;
        }
    }

    public void ProcessTcpPayload(byte[] payload, uint sequenceNumber, bool isServerToClient, string flowKey)
    {
        if (payload.Length == 0) return;

        lock (_streamLock)
        {
            // Dekodujemy tylko połączenie, którego SYN widzieliśmy jako ostatni.
            // Bez tego bajty starego i nowego połączenia mieszałyby się w jednym szyfrze RC4.
            if (_activeFlow == null || flowKey != _activeFlow) return;

            bool broken = isServerToClient ? _serverStreamBroken : _clientStreamBroken;
            if (broken) return;

            uint? expected = isServerToClient ? _expectedServerSeq : _expectedClientSeq;
            int skip = 0;

            if (expected.HasValue)
            {
                int diff = unchecked((int)(sequenceNumber - expected.Value));

                if (diff < 0)
                {
                    int overlap = -diff;
                    if (overlap >= payload.Length) return;
                    skip = overlap;
                }
                else if (diff > 0)
                {
                    Capture?.Note($"TCP_GAP: {(isServerToClient ? "S->C" : "C->S")} missing={diff} bytes");
                    if (isServerToClient) _serverStreamBroken = true;
                    else _clientStreamBroken = true;
                    return;
                }
            }

            uint next = unchecked(sequenceNumber + (uint)payload.Length);
            byte[] data = skip == 0 ? payload : payload.AsSpan(skip).ToArray();

            if (isServerToClient)
            {
                _expectedServerSeq = next;
                _serverBuffer.AddRange(data);
                ProcessBuffer(_serverBuffer, _serverCipher, isServerToClient: true);
            }
            else
            {
                _expectedClientSeq = next;
                _clientBuffer.AddRange(data);
                ProcessBuffer(_clientBuffer, _clientCipher, isServerToClient: false);
            }
        }
    }

    private void ProcessBuffer(List<byte> buffer, Rc4Cipher cipher, bool isServerToClient)
    {
        while (buffer.Count >= 5)
        {
            int packetLength = (buffer[0] << 24) | (buffer[1] << 16) | (buffer[2] << 8) | buffer[3];
            byte packetId = buffer[4];

            if (packetLength is < 5 or > 500_000)
            {
                Capture?.LogBadFrame(isServerToClient, buffer.Count, buffer.GetRange(0, 5).ToArray());
                buffer.Clear();
                return;
            }

            if (buffer.Count < packetLength)
                return;

            int bodyLength = packetLength - 5;
            byte[] body = new byte[bodyLength];
            buffer.CopyTo(5, body, 0, bodyLength);
            buffer.RemoveRange(0, packetLength);

            cipher.Cipher(body.AsSpan());
            Capture?.LogFrame(isServerToClient, packetId, packetLength, body);
            DecodedPacketsCount++;

            try
            {
                if (isServerToClient)
                    HandleServerPacket(packetId, body);
                else
                    HandleClientPacket(packetId, body);
            }
            catch (Exception ex)
            {
                Capture?.Note($"PARSE_EXCEPTION: {(isServerToClient ? "S->C" : "C->S")} id={packetId}, len={packetLength}, {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private void HandleClientPacket(byte packetId, ReadOnlySpan<byte> body)
    {
        var reader = new RotmgPacketReader(body);

        switch (packetId)
        {
            case RotmgProtocolConstants.PacketLoad:
                if (reader.Remaining >= 4)
                    _currentCharId = reader.ReadInt32();
                break;

            case RotmgProtocolConstants.PacketInvDrop:
                _lastPlayerInvDropTime = DateTime.UtcNow;
                break;
        }
    }

    private void HandleServerPacket(byte packetId, ReadOnlySpan<byte> body)
    {
        var reader = new RotmgPacketReader(body);

        switch (packetId)
        {
            case RotmgProtocolConstants.PacketMapInfo:
                ParseMapInfo(ref reader);
                break;

            case RotmgProtocolConstants.PacketCreateSuccess:
                if (reader.Remaining >= 8)
                {
                    _myObjectId = reader.ReadInt32();
                    _currentCharId = reader.ReadInt32();
                    _localStats.Reset(); _localClass = null; _lastLoggedMaxed = -1;
                }
                break;

            case RotmgProtocolConstants.PacketUpdate:
                ParseUpdate(ref reader, body);
                break;

            case RotmgProtocolConstants.PacketNewTick:
                ParseNewTick(ref reader);
                break;

            case RotmgProtocolConstants.PacketDamage:
                ParseDamage(ref reader);
                break;

            case RotmgProtocolConstants.PacketDeath:
                ParseDeath(ref reader);
                break;
        }
    }

    private void ParseMapInfo(ref RotmgPacketReader reader)
    {
        // Wejście na nową mapę = opuszczenie poprzedniej instancji -> ewentualne zaliczenie dungeona
        CommitInstanceCompletion();

        if (reader.Remaining < 8) return;
        reader.Skip(8); // Width + Height
        string mapName = reader.ReadUtfString();
        string displayName = reader.ReadUtfString();
        reader.ReadUtfString(); // RealmName

        double mapDifficultyGraves = 0;
        if (reader.Remaining >= 13)
        {
            reader.Skip(8); // Fp (uint32) + Background (int32)
            float rawDiff = reader.ReadFloat();
            if (rawDiff is >= 0.5f and <= 10.0f)
                mapDifficultyGraves = Math.Round(rawDiff, 1);
        }

        _loggedBagItems.Clear();
        _activeBagsOnMap.Clear();
        _playersOnMap.Clear();
        _enemiesOnMap.Clear();
        _killCandidates.Clear();
        _lastConfirmedKill = null;
        _lastDamagedBossOnMap = null;

        string cleanName = !string.IsNullOrWhiteSpace(displayName) && !displayName.StartsWith('{')
            ? displayName
            : mapName;

        if (!string.IsNullOrWhiteSpace(cleanName))
        {
            double graves = _gameData.ResolveDungeonGraveDifficulty(cleanName, mapDifficultyGraves);
            var portal = _gameData.ResolvePortalByDungeonName(cleanName);
            _state.EnterDungeon(cleanName, graves, portal?.ObjectType ?? 0);
        }
    }

    private void ParseUpdate(ref RotmgPacketReader reader, ReadOnlySpan<byte> body)
    {
        try
        {
            reader.Skip(8);        // pozycja
            reader.ReadByte();     // 0x19, stały w logu

            int tileCount = reader.ReadCompressedInt();
            reader.Skip(tileCount * 6);

            int newObjectsCount = reader.ReadCompressedInt();

            int firstObjectStart = reader.Position;
            int prevType = -1;
            int prevId = -1;
            List<StatEntry>? prevStats = null;

            for (int i = 0; i < newObjectsCount && reader.Remaining > 0; i++)
            {
                ushort objectType = reader.ReadUInt16();
                int objectId = reader.ReadCompressedInt();
                var (x, y) = reader.ReadWorldPosData();

                if (objectId < 0 || !float.IsFinite(x) || !float.IsFinite(y) || Math.Abs(x) > 5000 || Math.Abs(y) > 5000)
                {
                    // Po desyncu offset nie jest na granicy obiektu - dalsze czytanie dawaloby smieci.
                    int failPos = reader.Position;
                    int from = Math.Max(firstObjectStart, failPos - 1200);
                    int to = Math.Min(body.Length, failPos + 16);
                    string window = to > from ? Convert.ToHexString(body.Slice(from, to - from)) : string.Empty;
                    string prevStatTypes = prevStats == null ? "" : string.Join(",", prevStats.Select(s => s.StatType));

                    Capture?.Note($"UPDATE_DESYNC: object {i}/{newObjectsCount}, type=0x{objectType:X4}, bodyLen={body.Length}, failPos={failPos}, prev=(type=0x{prevType:X4}, id={prevId}, statTypes=[{prevStatTypes}]), hexFrom={from}: {window}");
                    return;
                }

                var stats = reader.ReadStatDataList();
                prevType = objectType;
                prevId = objectId;
                prevStats = stats;

                InspectObjectStatus(objectType, objectId, x, y, stats);
            }

            int dropCount = reader.ReadCompressedInt();
            for (int i = 0; i < dropCount && reader.Remaining > 0; i++)
            {
                int id = reader.ReadCompressedInt();

                if (_enemiesOnMap.TryGetValue(id, out var gone) && gone.Metadata.IsBoss)
                {
                    Capture?.Note($"BOSS_REMOVED: Id={id}, Name=\"{gone.Metadata.DisplayName}\", Category={gone.Metadata.Category}, LastPos=({gone.X:F1}, {gone.Y:F1})");
                    AddKillCandidate(id, gone);
                }

                _activeBagsOnMap.Remove(id);
                _enemiesOnMap.Remove(id);
                _playersOnMap.Remove(id);
            }
        }
        finally
        {
            ConfirmBossKills();
        }
    }

    private void ParseNewTick(ref RotmgPacketReader reader)
    {
        if (reader.Remaining < 16) return;
        reader.Skip(14);

        int statusCount = reader.ReadUInt16();
        for (int i = 0; i < statusCount && reader.Remaining > 0; i++)
        {
            int objectId = reader.ReadCompressedInt();
            var (x, y) = reader.ReadWorldPosData();
            var stats = reader.ReadStatDataList();

            if (_enemiesOnMap.TryGetValue(objectId, out var trackedEnemy))
            {
                trackedEnemy.X = x;
                trackedEnemy.Y = y;
                trackedEnemy.LastSeenUtc = DateTime.UtcNow;
            }
            else if (_activeBagsOnMap.TryGetValue(objectId, out var bagInfo))
            {
                InspectBagContents(bagInfo, objectId, stats);
            }
            else if (objectId == _myObjectId)
            {
                InspectLocalPlayerStats(stats);
            }
            
            var hpStat = stats.FirstOrDefault(s => s.StatType == 1);
            if (hpStat.IntValue > 0)
            {
                // opcjonalnie: trackedEnemy.CurrentHp = hpStat.IntValue;
            }
        }
    }
    
    private void CommitInstanceCompletion()
    {
        if (!_instanceKillConfirmed) return;

        string? name = _completionDungeon;
        double graves = _completionGraves;
        _instanceKillConfirmed = false;
        _completionDungeon = null;

        if (string.IsNullOrWhiteSpace(name) || graves < 0.5) return;
        if (_gameData.ResolvePortalByDungeonName(name) == null) return;

        _state.RecordDungeonCompletion(name, RarityScoreEngine.GraveCountToTier(graves));
        Capture?.Note($"DUNGEON_COMPLETED: {name}, graves={graves:F1}");
    }
    
    private void AddKillCandidate(int objectId, TrackedMapEnemy enemy)
    {
        if (_killCandidates.Any(k => k.ObjectId == objectId && !k.Consumed))
            return;

        _killCandidates.Add(new KillCandidate
        {
            ObjectId = objectId,
            Meta = enemy.Metadata,
            X = enemy.X,
            Y = enemy.Y
        });
    }
    
        private void ConfirmBossKills()
    {
        var now = DateTime.UtcNow;
        _killCandidates.RemoveAll(k => k.Consumed || (now - k.AtUtc).TotalMilliseconds > 5000);

        // 1. Worki, które pojawiły się już PO potwierdzonym killu
        if (_lastConfirmedKill is { } last && last.SessionId.HasValue)
        {
            var late = new List<Guid>();
            foreach (var bag in _activeBagsOnMap.Values)
            {
                if (bag.KillHandled) continue;

                double dt = (bag.SpawnedUtc - last.AtUtc).TotalMilliseconds;
                if (dt < 0 || dt > KillBagWindowMs) continue;

                float dx = bag.X - last.X;
                float dy = bag.Y - last.Y;
                if ((dx * dx) + (dy * dy) > KillRadiusSq) continue;

                bag.KillHandled = true;
                bag.CombatSessionId = last.SessionId;
                late.Add(bag.BagInstanceId);
            }

            if (late.Count > 0)
                _state.AttachCombatSessionToBags(late, last.SessionId.Value);
        }

        if (_killCandidates.Count == 0) return;

        // 2. Nowe kille: kandydat + worek blisko w czasie i przestrzeni (w dowolnej kolejności)
        foreach (var anchor in _killCandidates.ToList())
        {
            if (anchor.Consumed) continue;

            var bags = new List<KeyValuePair<int, TrackedLootBag>>();
            foreach (var entry in _activeBagsOnMap)
            {
                var bag = entry.Value;
                if (bag.KillHandled) continue;
                if (Math.Abs((bag.SpawnedUtc - anchor.AtUtc).TotalMilliseconds) > KillBagWindowMs) continue;

                float dx = bag.X - anchor.X;
                float dy = bag.Y - anchor.Y;
                if ((dx * dx) + (dy * dy) > KillRadiusSq) continue;

                bags.Add(entry);
            }

            if (bags.Count == 0) continue;

            var cluster = _killCandidates
                .Where(k => !k.Consumed)
                .Where(k =>
                {
                    float dx = k.X - anchor.X;
                    float dy = k.Y - anchor.Y;
                    return (dx * dx) + (dy * dy) <= KillRadiusSq;
                })
                .ToList();

            var main = cluster.OrderByDescending(k => k.Meta.MaxHp).First().Meta;
            double graves = _gameData.ResolveDungeonGraveDifficulty(_state.CurrentDungeonName, _state.CurrentDungeonGraves);

            Guid? sessionId = _state.ArchiveCurrentCombatSession(main.DisplayName, main.ObjectType, graves);
            _state.ClearDungeonDps();

            var bagIds = new List<Guid>();
            foreach (var entry in bags)
            {
                entry.Value.KillHandled = true;
                entry.Value.CombatSessionId = sessionId;
                bagIds.Add(entry.Value.BagInstanceId);
            }

            if (sessionId.HasValue)
                _state.AttachCombatSessionToBags(bagIds, sessionId.Value);

            foreach (var c in cluster)
                c.Consumed = true;

            if (cluster.Any(c => c.Meta.Category == "DungeonBoss"))
            {
                _instanceKillConfirmed = true;
                _completionDungeon = _state.CurrentDungeonName;
                _completionGraves = graves;
            }

            _lastConfirmedKill = (anchor.X, anchor.Y, now, sessionId, main);

            Capture?.Note($"BOSS_KILL_CONFIRMED: Name=\"{main.DisplayName}\", Category={main.Category}, Bags={bags.Count}, Candidates={cluster.Count}, Session={(sessionId.HasValue ? "yes" : "no-dps")}");
        }
    }

        private void InspectObjectStatus(int objectType, int objectId, float x, float y, List<StatEntry> stats)
    {
        if (_gameData.TryResolvePlayerClass(objectType, out var playerClass) ||
            RotmgProtocolConstants.TryGetPlayerClass(objectType, out playerClass))
        {
            string? playerName = stats.FirstOrDefault(s => s.StatType == RotmgProtocolConstants.StatName).StringValue;
            if (!string.IsNullOrWhiteSpace(playerName))
                _playersOnMap[objectId] = (playerName, playerClass);

            if (objectId == _myObjectId)
            {
                string statsSummary = string.Join(", ", stats.Select(s => $"{s.StatType}:{(string.IsNullOrEmpty(s.StringValue) ? s.IntValue : s.StringValue)}"));
                Capture?.Note($"PLAYER_STATUS: Id={objectId}, Class={playerClass}, Stats=[{statsSummary}]");

                int seasonalIdx = stats.FindIndex(s => s.StatType == StatSeasonalId);
                if (seasonalIdx >= 0)
                    _localSeasonal = stats[seasonalIdx].IntValue != 0;

                if (!string.IsNullOrWhiteSpace(playerName))
                    _state.HandleNexusPlayerDetected(playerName, playerClass, _localSeasonal ?? true, _currentCharId);

                _localClass = playerClass;
                InspectLocalPlayerStats(stats);
            }
            return;
        }

        if (_gameData.TryResolveEnemy(objectType, out var enemyMeta))
        {
            _enemiesOnMap[objectId] = new TrackedMapEnemy
            {
                Metadata = enemyMeta,
                X = x,
                Y = y,
                LastSeenUtc = DateTime.UtcNow
            };

            if (enemyMeta.IsBoss)
                Capture?.Note($"BOSS_SEEN: Type=0x{objectType:X4},Category={enemyMeta.Category}, Id={objectId}, Name=\"{enemyMeta.DisplayName}\", Pos=({x:F1}, {y:F1})");

            return;
        }

        if (RotmgProtocolConstants.IsTrackedBagContainer(objectType))
        {
            if ((DateTime.UtcNow - _lastPlayerInvDropTime).TotalSeconds < 2.0)
                return;

            if (!_activeBagsOnMap.TryGetValue(objectId, out var trackedBag))
            {
                trackedBag = new TrackedLootBag
                {
                    BagInstanceId = Guid.NewGuid(),
                    BagObjectType = objectType,
                    X = x,
                    Y = y
                };
                _activeBagsOnMap[objectId] = trackedBag;

                // Fioletowe i egg pomijamy w logu - zalałyby capture
                if (objectType is not (0x0507 or 0x0508))
                    Capture?.Note($"BAG_SPAWN: Type=0x{objectType:X4}, Id={objectId}, Pos=({x:F1}, {y:F1})");
            }

            InspectBagContents(trackedBag, objectId, stats);
        }
    }

    private EnemyMetadata? ResolveBossForBagPosition(float bagX, float bagY)
    {
        TrackedMapEnemy? bestBoss = null;
        TrackedMapEnemy? nearest = null;
        float nearestDistSq = float.MaxValue;

        foreach (var enemy in _enemiesOnMap.Values)
        {
            float dx = enemy.X - bagX;
            float dy = enemy.Y - bagY;
            float distSq = (dx * dx) + (dy * dy);
            if (distSq > 64f) continue;

            if (enemy.Metadata.IsBoss)
            {
                if (bestBoss == null || enemy.Metadata.MaxHp > bestBoss.Metadata.MaxHp)
                    bestBoss = enemy;
            }
            else if (distSq < nearestDistSq)
            {
                nearestDistSq = distSq;
                nearest = enemy;
            }
        }

        if (bestBoss != null)
            return bestBoss.Metadata;

        // Boss już zniknął z mapy, ale zabity przed chwilą obok tego worka
        if (_lastConfirmedKill is { } kill && (DateTime.UtcNow - kill.AtUtc).TotalMilliseconds <= 3000)
        {
            float dx = kill.X - bagX;
            float dy = kill.Y - bagY;
            if ((dx * dx) + (dy * dy) <= KillRadiusSq)
                return kill.Meta;
        }

        return nearest?.Metadata ?? _lastDamagedBossOnMap;
    }

        private void InspectBagContents(TrackedLootBag trackedBag, int bagObjectId, List<StatEntry> stats)
    {
        // Fioletowe i egg: pełna zawartość trafia do historii, ale bez logów w capture (limit MaxEventLines)
        bool quiet = trackedBag.BagObjectType is 0x0507 or 0x0508;

        BagType physicalBag = _gameData.ResolveBagType(trackedBag.BagObjectType);
        var matchedBoss = ResolveBossForBagPosition(trackedBag.X, trackedBag.Y);
        double graves = _gameData.ResolveDungeonGraveDifficulty(_state.CurrentDungeonName, _state.CurrentDungeonGraves);
        DungeonDifficultyTier tier = RarityScoreEngine.GraveCountToTier(graves);
        var portal = _gameData.ResolvePortalByDungeonName(_state.CurrentDungeonName);

        foreach (var stat in stats)
        {
            if (stat.StatType is < RotmgProtocolConstants.StatInventory0 or > RotmgProtocolConstants.StatInventory7)
                continue;

            int itemId = stat.IntValue;
            if (itemId <= 0) continue;

            int slotIndex = stat.StatType - RotmgProtocolConstants.StatInventory0;
            if (!_loggedBagItems.Add((bagObjectId, slotIndex, itemId)))
                continue;

            string itemName;
            DropType effectiveQuality;
            bool isShiny = false;
            int itemTier = -1;
            BagType itemBag = physicalBag;

            if (_gameData.TryResolveMark(itemId, out var markMeta))
            {
                // Mark u Tinkerera -> aktualizacja Quest Tracker; do historii trafia jako Other
                if (!quiet)
                    Capture?.Note($"BAG_ITEM: BagId={bagObjectId}, Slot={slotIndex}, ItemId=0x{itemId:X4} (MARK: {markMeta.MarkName})");
                _state.SyncMarkPickedUp(markMeta, portal?.ObjectType ?? 0);

                itemName = markMeta.MarkName;
                effectiveQuality = DropType.Other;
            }
            else if (!_gameData.TryGetItem(itemId, out var itemMeta))
            {
                // Item spoza katalogu (kolekcja + loot-only) - zapisujemy z nazwą zastępczą
                if (!quiet)
                    Capture?.Note($"BAG_ITEM_UNKNOWN: BagId={bagObjectId}, Slot={slotIndex}, ItemId=0x{itemId:X4}");

                itemName = $"Unknown Item (0x{itemId:X4})";
                effectiveQuality = DropType.Other;
            }
            else
            {
                itemBag = _gameData.ResolveBagTypeFromItem(itemMeta.BagTypeId, physicalBag);

                if (!quiet)
                    Capture?.Note($"BAG_ITEM: BagId={bagObjectId}, Slot={slotIndex}, ItemId=0x{itemId:X4} (\"{itemMeta.Name}\", Quality={itemMeta.Quality}, XmlBag={itemMeta.BagTypeId}, Container=0x{trackedBag.BagObjectType:X4})");

                bool special = itemMeta.Quality is DropType.BPs or DropType.Tops or DropType.Tiered;
                bool qualifies = itemMeta.BagTypeId >= 6 || special;

                // Tiered liczy się tylko, gdy FIZYCZNIE leży w białym/pomarańczowym worku
                if (itemMeta.Quality == DropType.Tiered && physicalBag is not (BagType.WhiteBag or BagType.OrangeBag))
                    qualifies = false;

                itemName = itemMeta.Name;
                itemTier = itemMeta.Tier;

                if (qualifies)
                {
                    effectiveQuality = itemMeta.Quality;
                    isShiny = itemMeta.IsShiny;
                }
                else
                {
                    effectiveQuality = DropType.Other;
                }
            }

            // Dla Other pokazujemy kolor kontenera, w którym item faktycznie leżał (nie <BagType> z XML-a itemu)
            if (effectiveQuality == DropType.Other)
                itemBag = physicalBag;

            _state.RecordLootDrop(
                itemName: itemName,
                itemId: itemId,
                bagQuality: effectiveQuality,
                dungeonName: _state.CurrentDungeonName,
                rarity: ItemRarity.Common,
                isShiny: isShiny,
                difficulty: tier,
                physicalBag: itemBag,
                itemTier: itemTier,
                bossName: matchedBoss?.DisplayName ?? "Dungeon Boss",
                bossObjectType: matchedBoss?.ObjectType ?? 0,
                graveDifficulty: graves,
                bagObjectType: trackedBag.BagObjectType,
                dungeonPortalObjectType: portal?.ObjectType ?? 0,
                bagInstanceId: trackedBag.BagInstanceId,
                combatSessionId: trackedBag.CombatSessionId);
        }
    }

        private void InspectLocalPlayerStats(List<StatEntry> stats)
    {
        Capture?.LogLocalPlayerStats(stats);

        int baseFame = _state.ActiveCharacter.BaseFame;
        int skinId = _state.ActiveCharacter.SkinId;
        int[] equip = (int[])_state.ActiveCharacter.EquipmentIds.Clone();
        bool changed = false;

        int maxedStats = _state.ActiveCharacter.MaxedStats;
        _localStats.Apply(stats);
        if (_localClass is { } cls && _localStats.TryComputeMaxed(cls, out int computed, out var b))
        {
            if (computed != _lastLoggedMaxed)
            {
                _lastLoggedMaxed = computed;
                string inTick = string.Join(" ", stats
                    .Where(s => string.IsNullOrEmpty(s.StringValue) && (s.StatType <= 60 || (s.StatType >= 96 && s.StatType <= 112)))
                    .Select(s => $"{s.StatType}:{s.IntValue}"));
                Capture?.Note($"MAXED_RAW: {cls} computed={computed}/8 base=[HP {b.Hp}, MP {b.Mp}, ATT {b.Att}, DEF {b.Def}, SPD {b.Spd}, DEX {b.Dex}, VIT {b.Vit}, WIS {b.Wis}] inTick=[{inTick}]");
            }

            if (_localStats.TryAcceptMaxed(computed, out int accepted))
            {
                // Bazowe staty nie spadają: zapisujemy tylko wzrost względem tego, co już jest w bazie
                if (accepted > maxedStats) { maxedStats = accepted; changed = true; }
                Capture?.Note($"MAXED_STATS: {cls} accepted={accepted}/8 base=[HP {b.Hp}, MP {b.Mp}, ATT {b.Att}, DEF {b.Def}, SPD {b.Spd}, DEX {b.Dex}, VIT {b.Vit}, WIS {b.Wis}] exalt: {_localStats.ExaltBonusesText()}");
            }
        }

        foreach (var s in stats)
        {
            if (s.StatType == RotmgProtocolConstants.StatCurrFame && s.IntValue > 0)
            {
                baseFame = s.IntValue;
                changed = true;
            }
            else if (s.StatType == RotmgProtocolConstants.StatSkinId && s.IntValue > 0)
            {
                skinId = s.IntValue;
                changed = true;
            }
            else if (s.StatType is >= RotmgProtocolConstants.StatInventory0 and <= RotmgProtocolConstants.StatInventory0 + 3)
            {
                equip[s.StatType - RotmgProtocolConstants.StatInventory0] = s.IntValue;
                changed = true;
            }
        }

        if (changed)
            _state.UpdateActiveCharacterStats(maxedStats, baseFame, equip, skinId);
    }

    private void ParseDamage(ref RotmgPacketReader reader)
    {
        if (reader.Remaining < 11) return;

        int targetObjectId = reader.ReadInt32();
        int effectsCount = reader.ReadByte();
        reader.Skip(effectsCount);

        ushort damageAmount = reader.ReadUInt16();
        byte killFlags = reader.ReadByte();
        bool killedTarget = (killFlags & 0x01) != 0;
        reader.Skip(2);        // bulletId (ushort), nie 1 bajt
        int attackerObjectId = reader.ReadInt32();

        if (!_enemiesOnMap.TryGetValue(targetObjectId, out var targetEnemy) || !targetEnemy.Metadata.IsBoss)
            return;

        _lastDamagedBossOnMap = targetEnemy.Metadata;

        if (_playersOnMap.TryGetValue(attackerObjectId, out var attacker))
            _state.RecordPlayerDamage(attackerObjectId, attacker.Name, attacker.Class, damageAmount);

        // Kill z flagi Damage to tylko kandydat; potwierdza go dopiero worek w pobliżu
        if (killedTarget)
        {
            AddKillCandidate(targetObjectId, targetEnemy);
            ConfirmBossKills();
        }
    }

    private void ParseDeath(ref RotmgPacketReader reader)
    {
        string accountId = reader.ReadUtfString();
        int charId = reader.ReadCompressedInt();   // w capture: BA 03 = 442 (compressed int, nie int32)
        string killedBy = reader.ReadUtfString();

        int activeCharId = _state.ActiveCharacter.RotmgCharId;
        Capture?.Note($"DEATH: charId={charId}, currentCharId={_currentCharId}, activeCharId={activeCharId}, killedBy=\"{killedBy}\"");

        // Nie zabijaj aktywnej postaci w bazie, jesli zginela inna (np. nowy lucznik, gdy aktywny jest Knight).
        if (activeCharId > 0 && charId != activeCharId)
        {
            Capture?.Note("DEATH_IGNORED: charId martwej postaci != aktywna postac w aplikacji");
            return;
        }

        _state.KillActiveCharacter(
            string.IsNullOrWhiteSpace(killedBy) ? "Unknown Enemy" : killedBy,
            _state.CurrentDungeonName,
            _state.ActiveCharacter.BaseFame);
    }
}