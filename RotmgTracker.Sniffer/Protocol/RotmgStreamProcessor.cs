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
        public Guid? CombatSessionId { get; init; }
    }

    private readonly TrackerStateService _state;
    private readonly GameDataService _gameData;

    private Rc4Cipher _serverCipher = new(Rc4Cipher.ServerToClientKeyHex);
    private Rc4Cipher _clientCipher = new(Rc4Cipher.ClientToServerKeyHex);

    private readonly List<byte> _serverBuffer = new(65536);
    private readonly List<byte> _clientBuffer = new(8192);

    private uint? _expectedServerSeq;
    private uint? _expectedClientSeq;

    private int _myObjectId = -1;
    private int _currentCharId = 0;
    private DateTime _lastPlayerInvDropTime = DateTime.MinValue;

    private readonly HashSet<(int BagObjectId, int Slot, int ItemId)> _loggedBagItems = new();
    private readonly Dictionary<int, TrackedLootBag> _activeBagsOnMap = new();
    private readonly Dictionary<int, (string Name, RotmgClass Class)> _playersOnMap = new();
    private readonly Dictionary<int, TrackedMapEnemy> _enemiesOnMap = new();
    private EnemyMetadata? _lastDamagedBossOnMap;
    
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

    public void ResetStream()
    {
        Capture?.Note("STREAM_RESET");
        _serverCipher = new Rc4Cipher(Rc4Cipher.ServerToClientKeyHex);
        _clientCipher = new Rc4Cipher(Rc4Cipher.ClientToServerKeyHex);
        _serverBuffer.Clear();
        _clientBuffer.Clear();
        _expectedServerSeq = null;
        _expectedClientSeq = null;
        _loggedBagItems.Clear();
        _activeBagsOnMap.Clear();
        _playersOnMap.Clear();
        _enemiesOnMap.Clear();
        _lastDamagedBossOnMap = null;
    }

    public void ProcessTcpPayload(byte[] payload, uint sequenceNumber, bool isServerToClient)
    {
        if (payload.Length == 0) return;

        if (isServerToClient)
        {
            if (_expectedServerSeq.HasValue && sequenceNumber < _expectedServerSeq.Value)
                return;

            _expectedServerSeq = sequenceNumber + (uint)payload.Length;
            _serverBuffer.AddRange(payload);
            ProcessBuffer(_serverBuffer, _serverCipher, isServerToClient: true);
        }
        else
        {
            if (_expectedClientSeq.HasValue && sequenceNumber < _expectedClientSeq.Value)
                return;

            _expectedClientSeq = sequenceNumber + (uint)payload.Length;
            _clientBuffer.AddRange(payload);
            ProcessBuffer(_clientBuffer, _clientCipher, isServerToClient: false);
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
            catch { }
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
                ParseUpdate(ref reader);
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

    private void ParseUpdate(ref RotmgPacketReader reader)
    {
        reader.Skip(8);        // pozycja
        reader.ReadByte();     // 0x19, stały w logu

        int tileCount = reader.ReadCompressedInt();
        reader.Skip(tileCount * 6);

        int newObjectsCount = reader.ReadCompressedInt();
        for (int i = 0; i < newObjectsCount && reader.Remaining > 0; i++)
        {
            ushort objectType = reader.ReadUInt16();
            int objectId = reader.ReadCompressedInt();
            var (x, y) = reader.ReadWorldPosData();
            
            if (objectId < 0 || !float.IsFinite(x) || !float.IsFinite(y) || Math.Abs(x) > 5000 || Math.Abs(y) > 5000)
            {
                Capture?.Note($"UPDATE_DESYNC: object {i}/{newObjectsCount}, type=0x{objectType:X4}");
                return;
            }
            
            var stats = reader.ReadStatDataList();

            InspectObjectStatus(objectType, objectId, x, y, stats);
        }

        int dropCount = reader.ReadCompressedInt();
        for (int i = 0; i < dropCount && reader.Remaining > 0; i++)
        {
            int id = reader.ReadCompressedInt();
            _activeBagsOnMap.Remove(id);
            _enemiesOnMap.Remove(id);
            _playersOnMap.Remove(id);
            
            if (_enemiesOnMap.TryGetValue(id, out var gone) && gone.Metadata.IsBoss)
                Capture?.Note($"BOSS_REMOVED: Id={id}, Name=\"{gone.Metadata.DisplayName}\"");
            
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
                // NOTE: Gracz lokalny (ObjectId, klasa, staty)
                string statsSummary = string.Join(", ", stats.Select(s => $"{s.StatType}:{(string.IsNullOrEmpty(s.StringValue) ? s.IntValue : s.StringValue)}"));
                Capture?.Note($"PLAYER_STATUS: Id={objectId}, Class={playerClass}, Stats=[{statsSummary}]");

                if (!string.IsNullOrWhiteSpace(playerName))
                    _state.HandleNexusPlayerDetected(playerName, playerClass, true, _currentCharId);

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
            {
                _lastDamagedBossOnMap = enemyMeta;
                // NOTE: Wykryty Boss na mapie
                Capture?.Note($"BOSS_SEEN: Type=0x{objectType:X4},Category={enemyMeta.Category}, Id={objectId}, Name=\"{enemyMeta.DisplayName}\", Pos=({x:F1}, {y:F1})");
            }

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
                    Y = y,
                    CombatSessionId = _state.LastCompletedCombatSessionId
                };
                _activeBagsOnMap[objectId] = trackedBag;

                // NOTE: Zespawnowany worek na mapie
                Capture?.Note($"BAG_SPAWN: Type=0x{objectType:X4}, Id={objectId}, Pos=({x:F1}, {y:F1})");
            }

            InspectBagContents(trackedBag, objectId, stats);
        }
    }

    private EnemyMetadata? ResolveBossForBagPosition(float bagX, float bagY)
    {
        TrackedMapEnemy? closestBoss = null;
        float bestDistSq = float.MaxValue;

        foreach (var enemy in _enemiesOnMap.Values)
        {
            float dx = enemy.X - bagX;
            float dy = enemy.Y - bagY;
            float distSq = (dx * dx) + (dy * dy);

            if (distSq <= 64f)
            {
                float weightedDist = enemy.Metadata.IsBoss ? distSq * 0.25f : distSq;
                if (weightedDist < bestDistSq)
                {
                    bestDistSq = weightedDist;
                    closestBoss = enemy;
                }
            }
        }

        return closestBoss?.Metadata ?? _lastDamagedBossOnMap;
    }

    private void InspectBagContents(TrackedLootBag trackedBag, int bagObjectId, List<StatEntry> stats)
    {
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

            // Sprawdź czy w worku wypadł Mark u Tinkerera -> automatycznie zaktualizuj Quest Tracker
            if (_gameData.TryResolveMark(itemId, out var markMeta))
            {
                Capture?.Note($"BAG_ITEM: BagId={bagObjectId}, Slot={slotIndex}, ItemId=0x{itemId:X4} (MARK: {markMeta.MarkName})");
                _state.SyncMarkPickedUp(markMeta, portal?.ObjectType ?? 0);
                continue;
            }

            if (!_gameData.TryGetItem(itemId, out var itemMeta))
            {
                Capture?.Note($"BAG_ITEM_UNKNOWN: BagId={bagObjectId}, Slot={slotIndex}, ItemId=0x{itemId:X4}");
                continue;
            }

            // Zwykłe potki/śmieci z niebieskich i cyan worków nas nie interesują
            bool special = itemMeta.Quality is DropType.BPs or DropType.Tops or DropType.Tiered;
            if (itemMeta.BagTypeId < 6 && !special)
                continue;

            BagType itemBag = _gameData.ResolveBagTypeFromItem(itemMeta.BagTypeId, physicalBag);

            Capture?.Note($"BAG_ITEM: BagId={bagObjectId}, Slot={slotIndex}, ItemId=0x{itemId:X4} (\"{itemMeta.Name}\", Quality={itemMeta.Quality}, XmlBag={itemMeta.BagTypeId}, Container=0x{trackedBag.BagObjectType:X4})");

            if (itemMeta.Quality == DropType.Tiered && itemBag is not (BagType.WhiteBag or BagType.OrangeBag))
                continue;

            // Jeśli to White Bag z dungeona o trudności >= 9.0 nagrobków (O3, Shatters, MV, Adv), nadaj jakość LegendaryWhite
            DropType effectiveQuality = itemMeta.Quality;
            if (physicalBag == BagType.WhiteBag && !itemMeta.IsShiny && graves >= 9.0 && effectiveQuality == DropType.RegularWhite)
            {
                effectiveQuality = DropType.LegendaryWhite;
            }

            _state.RecordLootDrop(
                itemName: itemMeta.Name,
                itemId: itemId,
                bagQuality: effectiveQuality,
                dungeonName: _state.CurrentDungeonName,
                rarity: ItemRarity.Common,
                isShiny: itemMeta.IsShiny,
                difficulty: tier,
                physicalBag: itemBag,
                itemTier: itemMeta.Tier,
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
        int baseFame = _state.ActiveCharacter.BaseFame;
        int skinId = _state.ActiveCharacter.SkinId;
        int[] equip = (int[])_state.ActiveCharacter.EquipmentIds.Clone();
        bool changed = false;
        
        int maxedStats = _state.ActiveCharacter.MaxedStats;
        _localStats.Apply(stats);
        if (_localClass is { } cls && _localStats.TryComputeMaxed(cls, out int computed, out var b))
        {
            if (computed != maxedStats) { maxedStats = computed; changed = true; }
            if (computed != _lastLoggedMaxed)
            {
                _lastLoggedMaxed = computed;
                Capture?.Note($"MAXED_STATS: {cls} {computed}/8 base=[HP {b.Hp}, MP {b.Mp}, ATT {b.Att}, DEF {b.Def}, SPD {b.Spd}, DEX {b.Dex}, VIT {b.Vit}, WIS {b.Wis}] exalt: {_localStats.ExaltBonusesText()}");
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

        if (_enemiesOnMap.TryGetValue(targetObjectId, out var targetEnemy) && targetEnemy.Metadata.IsBoss)
        {
            _lastDamagedBossOnMap = targetEnemy.Metadata;
        }

        if (_enemiesOnMap.TryGetValue(targetObjectId, out var dmgTarget) && dmgTarget.Metadata.IsBoss &&
            _playersOnMap.TryGetValue(attackerObjectId, out var attacker))
        {
            _state.RecordPlayerDamage(attackerObjectId, attacker.Name, attacker.Class, damageAmount);
        }

        // Gdy Boss ginie: zapisz ukończenie dungeona oraz zarchiwizuj sesję DPS pod wypadający za chwilę worek!
        if (killedTarget && _enemiesOnMap.TryGetValue(targetObjectId, out var deadEnemy) && deadEnemy.Metadata.IsBoss)
        {
            double graves = _gameData.ResolveDungeonGraveDifficulty(_state.CurrentDungeonName, _state.CurrentDungeonGraves);
            var tier = RarityScoreEngine.GraveCountToTier(graves);

            _state.ArchiveCurrentCombatSession(deadEnemy.Metadata.DisplayName, deadEnemy.Metadata.ObjectType, graves);
            if (deadEnemy.Metadata.Category == "DungeonBoss")
                _state.RecordDungeonCompletion(_state.CurrentDungeonName, tier);
        }
    }

    private void ParseDeath(ref RotmgPacketReader reader)
    {
        string accountId = reader.ReadUtfString();
        int charId = reader.ReadInt32();
        string killedBy = reader.ReadUtfString();
        reader.Skip(8);
        int totalFame = reader.Remaining >= 4 ? reader.ReadInt32() : _state.ActiveCharacter.BaseFame;

        _state.KillActiveCharacter(
            string.IsNullOrWhiteSpace(killedBy) ? "Unknown Enemy" : killedBy,
            _state.CurrentDungeonName,
            totalFame);
    }
}