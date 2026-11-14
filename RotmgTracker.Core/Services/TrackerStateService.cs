using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using RotmgTrackerV1.Core.Data;
using RotmgTrackerV1.Core.Enums;
using RotmgTrackerV1.Core.Models;
using System.Text.Json.Nodes;
using System.Collections.Concurrent;

namespace RotmgTrackerV1.Core.Services;

public class TrackerSaveData
{
    public List<CharacterSession> ActiveCharacters { get; set; } = new();
    public Guid ActiveCharacterId { get; set; }
    public CharacterSession? CurrentCharacter { get; set; }
    public List<CharacterSession> Graveyard { get; set; } = new();
    public List<DungeonMarkQuest> ActiveQuests { get; set; } = new();
    public List<OpeningSession> Openings { get; set; } = new();
    public List<LootDropRecord> LootHistory { get; set; } = new();
    public List<DungeonRunRecord> DungeonRuns { get; set; } = new();
    public List<CombatSessionRecord> CombatHistory { get; set; } = new();
    public List<WishlistTarget> Wishlist { get; set; } = new();
}

public class TrackerStateService
{
    private readonly string _dbFilePath;
    private readonly object _stateLock = new();
    private bool _legacyCountersRestored;
    private int _saveRequested;
    private int _saveWorkerActive;
    private DateTime _lastDpsNotifyUtc = DateTime.MinValue;

    public TrackerSaveData Data { get; private set; } = new();

    public string LinkedAccountName { get; private set; } = string.Empty;
    public string CurrentSeasonTag { get; private set; } = "S1";
    public string CurrentServerName { get; private set; } = "EUWest";

    public string? PendingAccountName { get; private set; }
    public RotmgClass PendingAccountClass { get; private set; } = RotmgClass.Wizard;
    public bool PendingAccountSeasonal { get; private set; } = true;

    public string CurrentDungeonName { get; private set; } = "Nexus";
    public double CurrentDungeonGraves { get; private set; } = 0.0;
    public int CurrentDungeonPortalType { get; private set; } = 0;
    public Guid? LastCompletedCombatSessionId { get; private set; }
    
    public ConcurrentDictionary<int, PlayerDpsEntry> CurrentDungeonPlayers { get; } = new();

    public event Action? OnStateChanged;

    [JsonIgnore]
    public CharacterSession ActiveCharacter
    {
        get
        {
            using var guard = LockState();

            var found = Data.ActiveCharacters.FirstOrDefault(c => c.Id == Data.ActiveCharacterId);
            if (found != null) return found;

            if (Data.ActiveCharacters.Count == 0)
            {
                var defaultChar = CreateFreshCharacter("PPE Wizard", RotmgClass.Wizard, true);
                Data.ActiveCharacters.Add(defaultChar);
            }

            Data.ActiveCharacterId = Data.ActiveCharacters[0].Id;
            return Data.ActiveCharacters[0];
        }
    }

    public TrackerStateService()
    {
        string appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RotmgTracker");

        Directory.CreateDirectory(appDataDir);
        _dbFilePath = Path.Combine(appDataDir, "rotmg_tracker.db");

        InitializeDatabaseAndLoad();
    }

    private void InitializeDatabaseAndLoad()
    {
        DatabaseMigrator.Initialize(_dbFilePath);

        using var db = new TrackerDbContext(_dbFilePath);

        if (!db.Characters.Any() && !db.Openings.Any())
        {
            TryMigrateLegacyJson();
            EnsureDropsInitialized();
            SaveToSqlite();
            return;
        }

        var allChars = db.Characters.ToList();
        Data.ActiveCharacters = allChars.Where(c => c.IsAlive && c.DiedAt == null).ToList();
        Data.Graveyard = allChars.Where(c => !c.IsAlive || c.DiedAt != null).OrderByDescending(c => c.DiedAt).ToList();
        Data.ActiveQuests = db.Quests.ToList();
        Data.Openings = db.Openings.OrderByDescending(o => o.CreatedAt).ToList();
        Data.LootHistory = db.LootHistory.OrderByDescending(l => l.DroppedAt).ToList();
        Data.DungeonRuns = db.DungeonRuns.OrderByDescending(r => r.CompletedAt).ToList();
        Data.CombatHistory = db.CombatSessions.OrderByDescending(c => c.EndedAt).ToList();
        Data.Wishlist = db.WishlistTargets.OrderByDescending(w => w.CreatedAt).ToList();

        var activeSetting = db.Settings.FirstOrDefault(s => s.Key == "ActiveCharacterId");
        if (activeSetting != null && Guid.TryParse(activeSetting.Value, out Guid parsedId))
            Data.ActiveCharacterId = parsedId;

        var accountSetting = db.Settings.FirstOrDefault(s => s.Key == "LinkedAccountName");
        if (accountSetting != null)
            LinkedAccountName = accountSetting.Value;

        _legacyCountersRestored = db.Settings.Any(s => s.Key == "LegacyCountersRestored" && s.Value == "1");

        EnsureDropsInitialized();
        RestoreLegacyCountersOnce();

        // Drops = LegacyDrops + loot z historii (naprawia wcześniej wyzerowane liczniki)
        foreach (var ch in Data.ActiveCharacters.Concat(Data.Graveyard))
            RecalculateCharacterDrops(ch);
    }

    private CharacterSession CreateFreshCharacter(string name, RotmgClass rotmgClass, bool isSeasonal, int rotmgCharId = 0)
    {
        int nextClassSlot = Data.ActiveCharacters.Concat(Data.Graveyard)
            .Count(c => c.Class == rotmgClass) + 1;

        var ch = new CharacterSession
        {
            RotmgCharId = rotmgCharId,
            ClassSlotNumber = nextClassSlot,
            Name = string.IsNullOrWhiteSpace(name) ? $"{rotmgClass} #{nextClassSlot}" : name.Trim(),
            Class = rotmgClass,
            IsSeasonal = isSeasonal,
            SeasonTag = CurrentSeasonTag,
            IsAlive = true
        };

        foreach (var type in Enum.GetValues<DropType>())
            ch.Drops[type] = 0;

        return ch;
    }

    // Rejestruje drop powiązany z workiem (BagInstanceId), trudnością w nagrobkach (1-10) i walką DPS (CombatSessionId)
            public void RecordLootDrop(
        string itemName,
        int itemId,
        DropType bagQuality,
        string? dungeonName = null,
        ItemRarity rarity = ItemRarity.Common,
        bool isShiny = false,
        DungeonDifficultyTier difficulty = DungeonDifficultyTier.Tier2_Godlands,
        BagType physicalBag = BagType.WhiteBag,
        int itemTier = -1,
        string enchantments = "",
        string bossName = "Unknown Boss",
        int bossObjectType = 0,
        double graveDifficulty = 3.5,
        int bagObjectType = 0x0509,
        int dungeonPortalObjectType = 0,
        Guid? bagInstanceId = null,
        Guid? combatSessionId = null)
    {
        using var guard = LockState();

        var current = ActiveCharacter;
        double effectiveGraves = graveDifficulty > 0 ? graveDifficulty : CurrentDungeonGraves;
        double score = RarityScoreEngine.CalculateIntrinsicScore(bagQuality, rarity, isShiny, effectiveGraves, difficulty);

        var record = new LootDropRecord
        {
            BagInstanceId = bagInstanceId ?? Guid.NewGuid(),
            CombatSessionId = combatSessionId,
            CharacterId = current.Id,
            CharacterName = current.FormattedTitle,
            CharacterClass = current.Class,
            CharacterSkinId = current.SkinId,
            CharacterMaxedStatsAtDrop = current.MaxedStats,
            BaseFameAtDrop = current.BaseFame,
            AccountName = LinkedAccountName,
            IsSeasonal = current.IsSeasonal,
            SeasonTag = current.SeasonTag,
            ItemId = itemId,
            ItemName = itemName,
            ItemTier = itemTier,
            PhysicalBag = physicalBag,
            BagObjectType = bagObjectType,
            BagType = bagQuality,
            Rarity = rarity,
            Enchantments = enchantments,
            IsShiny = isShiny || bagQuality is DropType.Shiny or DropType.Shivine,
            DungeonName = string.IsNullOrWhiteSpace(dungeonName) ? CurrentDungeonName : dungeonName,
            DungeonPortalObjectType = dungeonPortalObjectType > 0 ? dungeonPortalObjectType : CurrentDungeonPortalType,
            GraveDifficulty = effectiveGraves,
            DungeonDifficulty = RarityScoreEngine.GraveCountToTier(effectiveGraves),
            BossName = string.IsNullOrWhiteSpace(bossName) ? "Unknown Boss" : bossName,
            BossObjectType = bossObjectType,
            ServerName = CurrentServerName,
            RarityScore = score,
            DroppedAt = DateTime.Now
        };

        Data.LootHistory.Insert(0, record);
        RecalculateCharacterDrops(current);

        foreach (var wish in Data.Wishlist.Where(w => !w.IsCompleted))
        {
            if ((wish.ItemId > 0 && wish.ItemId == itemId) ||
                string.Equals(wish.ItemName, itemName, StringComparison.OrdinalIgnoreCase))
            {
                if (rarity >= wish.MinimumRarity && (!wish.RequireShiny || record.IsShiny))
                {
                    wish.IsCompleted = true;
                    wish.CompletedAt = DateTime.Now;
                }
            }
        }

        SaveAsync();
        NotifyStateChanged();
    }

    public void RemoveLootRecord(Guid lootId)
    {
        using var guard = LockState();

        var record = Data.LootHistory.FirstOrDefault(l => l.Id == lootId);
        if (record == null) return;

        Data.LootHistory.Remove(record);

        var owner = Data.ActiveCharacters.Concat(Data.Graveyard).FirstOrDefault(c => c.Id == record.CharacterId);
        if (owner != null)
            RecalculateCharacterDrops(owner);

        SaveAsync();
        NotifyStateChanged();
    }

    private void RecalculateCharacterDrops(CharacterSession character)
    {
        var charLoot = Data.LootHistory.Where(l => l.CharacterId == character.Id).ToList();
        foreach (var type in Enum.GetValues<DropType>())
        {
            // Other (potki, jajka, śmieci, Marki) są tylko w historii - nie liczą się do statystyk postaci
            if (type == DropType.Other)
            {
                character.Drops[type] = 0;
                continue;
            }

            int legacy = character.LegacyDrops.GetValueOrDefault(type);
            character.Drops[type] = Math.Max(0, legacy + charLoot.Count(l => l.BagType == type));
        }
    }

    // Zapisuje tabelę obrażeń z walki z bossem i zwraca jej ID do przypięcia pod wypadający worek
    public Guid? ArchiveCurrentCombatSession(string bossName, int bossObjectType, double graveDifficulty)
    {
        using var guard = LockState();

        if (CurrentDungeonPlayers.Count == 0) return null;

        var current = ActiveCharacter;
        var participants = CurrentDungeonPlayers.Values
            .OrderByDescending(p => p.TotalDamage)
            .ToList();

        int totalDmg = participants.Sum(p => p.TotalDamage);
        int myDmg = participants.FirstOrDefault(p => p.IsMe)?.TotalDamage ?? 0;

        var session = new CombatSessionRecord
        {
            DungeonName = CurrentDungeonName,
            BossName = bossName,
            BossObjectType = bossObjectType,
            GraveDifficulty = graveDifficulty,
            CharacterId = current.Id,
            CharacterName = current.FormattedTitle,
            CharacterClass = current.Class,
            TotalBossDamage = totalDmg,
            MyDamage = myDmg,
            Participants = participants,
            EndedAt = DateTime.Now
        };

        Data.CombatHistory.Insert(0, session);
        LastCompletedCombatSessionId = session.Id;
        SaveAsync();
        return session.Id;
    }

    public CombatSessionRecord? GetCombatSession(Guid? sessionId)
    {
        using var guard = LockState();

        return sessionId.HasValue
            ? Data.CombatHistory.FirstOrDefault(c => c.Id == sessionId.Value)
            : null;
    }
    
    // Dopina sesję DPS do już zapisanych dropów (worek mógł pojawić się przed potwierdzeniem killa)
    public void AttachCombatSessionToBags(IReadOnlyCollection<Guid> bagInstanceIds, Guid sessionId)
    {
        if (bagInstanceIds.Count == 0) return;

        using var guard = LockState();

        var ids = new HashSet<Guid>(bagInstanceIds);
        foreach (var loot in Data.LootHistory)
        {
            if (ids.Contains(loot.BagInstanceId))
                loot.CombatSessionId = sessionId;
        }

        SaveAsync();
        NotifyStateChanged();
    }

    public void RecordDungeonCompletion(string dungeonName, DungeonDifficultyTier difficulty = DungeonDifficultyTier.Tier2_Godlands)
    {
        if (string.IsNullOrWhiteSpace(dungeonName)) return;

        using var guard = LockState();

        var current = ActiveCharacter;
        Data.DungeonRuns.Insert(0, new DungeonRunRecord
        {
            CharacterId = current.Id,
            CharacterName = current.FormattedTitle,
            CharacterClass = current.Class,
            DungeonName = dungeonName.Trim(),
            DifficultyTier = difficulty,
            ServerName = CurrentServerName,
            IsSeasonal = current.IsSeasonal,
            SeasonTag = current.SeasonTag,
            CompletedAt = DateTime.Now
        });

        SaveAsync();
        NotifyStateChanged();
    }

    // Automatycznie aktualizuje postęp questów, gdy sniffer wykryje Marki w plecaku / Vaulcie
    public void SyncMarkPickedUp(MarkMetadata mark, int portalObjectType = 0)
    {
        using var guard = LockState();

        var quest = Data.ActiveQuests.FirstOrDefault(q =>
            q.MarkItemId == mark.ItemId ||
            string.Equals(q.DungeonName, mark.DungeonName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(q.MarkName, mark.MarkName, StringComparison.OrdinalIgnoreCase));

        if (quest != null)
        {
            quest.MarkItemId = mark.ItemId;
            quest.MarkName = mark.MarkName;
            quest.BossName = mark.BossName;
            quest.DungeonName = mark.DungeonName;
            if (portalObjectType > 0) quest.DungeonPortalObjectType = portalObjectType;
            quest.CurrentMarks = Math.Min(quest.TargetMarks, quest.CurrentMarks + 1);
            SaveAsync();
            NotifyStateChanged();
        }
    }

    public void AddMarkQuestWithMetadata(MarkMetadata mark, int targetMarks, string rewardName = "Mighty Quest Chest", int portalObjectType = 0)
    {
        using var guard = LockState();

        Data.ActiveQuests.Add(new DungeonMarkQuest
        {
            QuestTitle = $"Defeat {mark.BossName}",
            DungeonName = mark.DungeonName,
            MarkItemId = mark.ItemId,
            MarkName = mark.MarkName,
            BossName = mark.BossName,
            DungeonPortalObjectType = portalObjectType,
            RewardName = rewardName,
            TargetMarks = Math.Max(1, targetMarks),
            IsAutoTracked = true
        });
        SaveAsync();
        NotifyStateChanged();
    }

    public int GetRunsSinceWishlistTargetCreated(WishlistTarget target)
    {
        using var guard = LockState();

        return Data.DungeonRuns.Count(r =>
            string.Equals(r.DungeonName, target.TargetDungeonName, StringComparison.OrdinalIgnoreCase) &&
            r.CompletedAt >= target.CreatedAt &&
            (target.CompletedAt == null || r.CompletedAt <= target.CompletedAt));
    }

    public void AddWishlistTarget(string itemName, string dungeonName, ItemRarity minRarity, bool requireShiny, double dropRate)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return;

        using var guard = LockState();

        Data.Wishlist.Insert(0, new WishlistTarget
        {
            ItemName = itemName.Trim(),
            TargetDungeonName = string.IsNullOrWhiteSpace(dungeonName) ? "Any Dungeon" : dungeonName.Trim(),
            MinimumRarity = minRarity,
            RequireShiny = requireShiny,
            EstimatedDropRate = Math.Clamp(dropRate, 0.0001, 1.0)
        });

        SaveAsync();
        NotifyStateChanged();
    }

    public void HandleNexusPlayerDetected(string accountName, RotmgClass charClass, bool isSeasonal, int rotmgCharId = 0)
    {
        if (string.IsNullOrWhiteSpace(accountName)) return;

        using var guard = LockState();

        if (string.Equals(LinkedAccountName, accountName, StringComparison.OrdinalIgnoreCase))
        {
            SyncDetectedCharacter(accountName, charClass, isSeasonal, rotmgCharId);
            NotifyStateChanged();
            return;
        }

        PendingAccountName = accountName;
        PendingAccountClass = charClass;
        PendingAccountSeasonal = isSeasonal;
        NotifyStateChanged();
    }

    public void ConfirmPendingAccount()
    {
        using var guard = LockState();

        if (string.IsNullOrWhiteSpace(PendingAccountName)) return;

        LinkedAccountName = PendingAccountName;
        SyncDetectedCharacter(PendingAccountName, PendingAccountClass, PendingAccountSeasonal, 0);
        PendingAccountName = null;
        SaveAsync();
        NotifyStateChanged();
    }

    public void DismissPendingAccount()
    {
        using var guard = LockState();

        PendingAccountName = null;
        NotifyStateChanged();
    }

    public void UnlinkAccount()
    {
        using var guard = LockState();

        LinkedAccountName = string.Empty;
        SaveAsync();
        NotifyStateChanged();
    }

    private void SyncDetectedCharacter(string accountName, RotmgClass charClass, bool isSeasonal, int rotmgCharId)
    {
        CharacterSession? existing = null;

        if (rotmgCharId > 0)
        {
            // 1. Dokładne dopasowanie po ID postaci z gry
            existing = Data.ActiveCharacters.FirstOrDefault(c => c.RotmgCharId == rotmgCharId);

            // 2. Postać zaimportowana (bez ID) - przypisujemy jej ID z gry
            existing ??= Data.ActiveCharacters.FirstOrDefault(c =>
                c.RotmgCharId == 0 && c.Class == charClass && c.IsSeasonal == isSeasonal);

            if (existing != null && existing.RotmgCharId == 0)
                existing.RotmgCharId = rotmgCharId;
        }
        else
        {
            existing = Data.ActiveCharacters.FirstOrDefault(c => c.Class == charClass && c.IsSeasonal == isSeasonal);
        }

        if (existing != null)
            Data.ActiveCharacterId = existing.Id;
        else
            AddNewCharacter($"{charClass}", charClass, isSeasonal, rotmgCharId);
    }

    public void UpdateActiveCharacterStats(int maxedStats, int baseFame, int[]? equipment = null, int skinId = 0)
    {
        using var guard = LockState();

        var current = ActiveCharacter;
        current.MaxedStats = Math.Clamp(maxedStats, 0, 8);
        current.BaseFame = Math.Max(0, baseFame);
        if (equipment is { Length: 4 })
            current.EquipmentIds = equipment;
        if (skinId > 0)
            current.SkinId = skinId;

        SaveAsync();
        NotifyStateChanged();
    }

    public void ArchiveAndResetCharacter() => KillActiveCharacter("Unknown", CurrentDungeonName, ActiveCharacter.BaseFame);

    public void KillActiveCharacter(string killedBy, string dungeonName, int totalDeathFame)
    {
        using var guard = LockState();

        var deadChar = ActiveCharacter;
        deadChar.IsAlive = false;
        deadChar.DiedAt = DateTime.Now;
        deadChar.KilledBy = killedBy;
        deadChar.DeathDungeon = dungeonName;
        deadChar.DeathServer = CurrentServerName;
        deadChar.TotalFame = Math.Max(deadChar.BaseFame, totalDeathFame);

        Data.Graveyard.Insert(0, deadChar);
        Data.ActiveCharacters.Remove(deadChar);

        if (Data.ActiveCharacters.Count == 0)
        {
            var replacement = CreateFreshCharacter($"{deadChar.Class}", deadChar.Class, deadChar.IsSeasonal);
            Data.ActiveCharacters.Add(replacement);
        }

        Data.ActiveCharacterId = Data.ActiveCharacters[0].Id;
        SaveAsync();
        NotifyStateChanged();
    }

    public void EnterDungeon(string dungeonName, double graveDifficulty = 3.5, int portalObjectType = 0)
    {
        using var guard = LockState();

        CurrentDungeonName = string.IsNullOrWhiteSpace(dungeonName) ? "Unknown Dungeon" : dungeonName;
        CurrentDungeonGraves = graveDifficulty;
        CurrentDungeonPortalType = portalObjectType;
        LastCompletedCombatSessionId = null;
        CurrentDungeonPlayers.Clear();
        NotifyStateChanged();
    }

    public void RecordPlayerDamage(int objectId, string playerName, RotmgClass charClass, int damage)
    {
        if (damage <= 0) return;

        using var guard = LockState();

        if (!CurrentDungeonPlayers.TryGetValue(objectId, out var entry))
        {
            entry = new PlayerDpsEntry
            {
                ObjectId = objectId,
                PlayerName = playerName,
                Class = charClass,
                IsMe = !string.IsNullOrEmpty(LinkedAccountName) &&
                       string.Equals(playerName, LinkedAccountName, StringComparison.OrdinalIgnoreCase)
            };
            CurrentDungeonPlayers[objectId] = entry;
        }

        entry.TotalDamage += damage;
        entry.HitsCount++;

        var now = DateTime.UtcNow;
        if ((now - _lastDpsNotifyUtc).TotalMilliseconds >= 250)
        {
            _lastDpsNotifyUtc = now;
            NotifyStateChanged();
        }
    }

    public void ClearDungeonDps()
    {
        using var guard = LockState();

        CurrentDungeonPlayers.Clear();
        NotifyStateChanged();
    }
    
    private string? FindLegacyJsonPath()
    {
        string[] candidates =
        [
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rotmg_data.json"),
            Path.Combine(Path.GetDirectoryName(_dbFilePath) ?? string.Empty, "rotmg_data.json")
        ];

        return candidates.FirstOrDefault(File.Exists);
    }
    
    private TrackerSaveData? LoadLegacySaveData()
    {
        string? legacyPath = FindLegacyJsonPath();
        if (legacyPath == null) return null;

        try
        {
            if (JsonNode.Parse(File.ReadAllText(legacyPath)) is not JsonObject root) return null;

            // Stary enum RotmgClass: Rogue=4, Archer=5, Huntress=6. Obecny: Archer=4, Huntress=5, Rogue=6.
            static void RemapClass(JsonNode? node)
            {
                if (node is not JsonObject ch) return;
                if (ch["Class"] is not JsonValue v || !v.TryGetValue(out int old)) return;
                ch["Class"] = old switch { 4 => 6, 5 => 4, 6 => 5, _ => old };
            }

            RemapClass(root["CurrentCharacter"]);
            foreach (var key in new[] { "ActiveCharacters", "Graveyard" })
            {
                if (root[key] is JsonArray arr)
                    foreach (var ch in arr) RemapClass(ch);
            }

            return root.Deserialize<TrackerSaveData>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Legacy JSON load failed: {ex.Message}");
            return null;
        }
    }
    
    private static Dictionary<DropType, int> ComputeLegacyCounters(CharacterSession legacyChar, IEnumerable<LootDropRecord> legacyLoot)
    {
        var loot = legacyLoot.Where(l => l.CharacterId == legacyChar.Id).ToList();
        var result = new Dictionary<DropType, int>();

        foreach (var type in Enum.GetValues<DropType>())
        {
            if (type == DropType.Other) continue;

            int counter = legacyChar.Drops.GetValueOrDefault(type);
            int alreadyInHistory = loot.Count(l => l.BagType == type);
            result[type] = Math.Max(0, counter - alreadyInHistory);
        }

        return result;
    }

    private void TryMigrateLegacyJson()
    {
        var legacyData = LoadLegacySaveData();
        if (legacyData == null) return;

        Data = legacyData;
        if (Data.ActiveCharacters.Count == 0 && Data.CurrentCharacter != null)
        {
            Data.ActiveCharacters.Add(Data.CurrentCharacter);
            Data.ActiveCharacterId = Data.CurrentCharacter.Id;
        }

        // Postaci z Graveyard stara wersja nie oznaczała flagą IsAlive
        foreach (var dead in Data.Graveyard)
        {
            dead.IsAlive = false;
            dead.DiedAt ??= dead.CreatedAt;
        }

        foreach (var ch in Data.ActiveCharacters.Concat(Data.Graveyard))
            ch.LegacyDrops = ComputeLegacyCounters(ch, Data.LootHistory);

        _legacyCountersRestored = true;
    }
    
    private void RestoreLegacyCountersOnce()
    {
        if (_legacyCountersRestored) return;

        var legacy = LoadLegacySaveData();
        if (legacy == null) return;

        var legacyChars = legacy.ActiveCharacters.Concat(legacy.Graveyard).ToList();
        if (legacy.CurrentCharacter != null)
            legacyChars.Add(legacy.CurrentCharacter);

        var current = Data.ActiveCharacters.Concat(Data.Graveyard).ToList();
        foreach (var lc in legacyChars.GroupBy(c => c.Id).Select(g => g.First()))
        {
            var target = current.FirstOrDefault(c => c.Id == lc.Id);
            if (target == null) continue;

            target.LegacyDrops = ComputeLegacyCounters(lc, legacy.LootHistory);
        }

        _legacyCountersRestored = true;
        SaveAsync();
    }

    private void EnsureDropsInitialized()
    {
        if (Data.ActiveCharacters.Count == 0)
        {
            var first = CreateFreshCharacter("PPE Wizard", RotmgClass.Wizard, true);
            Data.ActiveCharacters.Add(first);
            Data.ActiveCharacterId = first.Id;
        }

        foreach (var ch in Data.ActiveCharacters.Concat(Data.Graveyard))
        {
            foreach (var type in Enum.GetValues<DropType>())
            {
                if (!ch.Drops.ContainsKey(type))
                    ch.Drops[type] = 0;
            }
        }
    }

    public void SelectCharacter(Guid characterId)
    {
        using var guard = LockState();

        if (Data.ActiveCharacters.Any(c => c.Id == characterId))
        {
            Data.ActiveCharacterId = characterId;
            SaveAsync();
        }
    }

    public void AddNewCharacter(string name, RotmgClass rotmgClass, bool isSeasonal, int rotmgCharId = 0)
    {
        using var guard = LockState();

        var newChar = CreateFreshCharacter(name, rotmgClass, isSeasonal, rotmgCharId);
        Data.ActiveCharacters.Add(newChar);
        Data.ActiveCharacterId = newChar.Id;
        SaveAsync();
    }

    public void DeleteActiveCharacter(Guid characterId)
    {
        using var guard = LockState();

        if (Data.ActiveCharacters.Count <= 1) return;
        Data.ActiveCharacters.RemoveAll(c => c.Id == characterId);
        if (Data.ActiveCharacterId == characterId)
            Data.ActiveCharacterId = Data.ActiveCharacters[0].Id;

        SaveAsync();
    }

    public void ChangeDropCount(DropType type, int delta)
    {
        var current = ActiveCharacter;
        current.LegacyDrops[type] = current.LegacyDrops.GetValueOrDefault(type) + delta;
        RecalculateCharacterDrops(current);
        SaveAsync();
    }

    public void ToggleSeasonal()
    {
        ActiveCharacter.IsSeasonal = !ActiveCharacter.IsSeasonal;
        SaveAsync();
    }

    public void RemoveGraveyardEntry(Guid id)
    {
        using var guard = LockState();

        Data.Graveyard.RemoveAll(c => c.Id == id);
        SaveAsync();
    }

    public void AddQuest(string dungeonName, int targetMarks)
    {
        if (string.IsNullOrWhiteSpace(dungeonName)) return;
        Data.ActiveQuests.Add(new DungeonMarkQuest
        {
            DungeonName = dungeonName.Trim(),
            TargetMarks = Math.Max(1, targetMarks)
        });
        SaveAsync();
    }

    public void ChangeQuestMark(Guid questId, int delta)
    {
        var quest = Data.ActiveQuests.FirstOrDefault(q => q.Id == questId);
        if (quest == null) return;
        quest.CurrentMarks = Math.Max(0, quest.CurrentMarks + delta);
        SaveAsync();
    }

    public void RemoveQuest(Guid questId)
    {
        Data.ActiveQuests.RemoveAll(q => q.Id == questId);
        SaveAsync();
    }

    public void AddOpeningSession(string title)
    {
        Data.Openings.Insert(0, new OpeningSession
        {
            Title = string.IsNullOrWhiteSpace(title) ? $"Opening #{Data.Openings.Count + 1}" : title
        });
        SaveAsync();
    }

    public void RemoveOpeningSession(Guid openingId)
    {
        Data.Openings.RemoveAll(o => o.Id == openingId);
        SaveAsync();
    }

    public void ToggleOpeningExpanded(Guid openingId)
    {
        var opening = Data.Openings.FirstOrDefault(o => o.Id == openingId);
        if (opening == null) return;
        opening.IsExpanded = !opening.IsExpanded;
        SaveAsync();
    }

    public void ChangeOpeningTotalChests(Guid openingId, int delta)
    {
        var opening = Data.Openings.FirstOrDefault(o => o.Id == openingId);
        if (opening == null) return;
        opening.TotalChests = Math.Max(0, opening.TotalChests + delta);
        SaveAsync();
    }

    public void SetOpeningTotalChests(Guid openingId, int value)
    {
        var opening = Data.Openings.FirstOrDefault(o => o.Id == openingId);
        if (opening == null) return;
        opening.TotalChests = Math.Max(0, value);
        SaveAsync();
    }

    public void ChangeChestDrop(Guid openingId, ChestType chestType, bool isWhite, int delta)
    {
        var opening = Data.Openings.FirstOrDefault(o => o.Id == openingId);
        if (opening == null || !opening.Chests.ContainsKey(chestType)) return;

        var stats = opening.Chests[chestType];
        if (isWhite)
            stats.Whites = Math.Max(0, stats.Whites + delta);
        else
            stats.STs = Math.Max(0, stats.STs + delta);

        SaveAsync();
    }

    private void NotifyStateChanged() => OnStateChanged?.Invoke();

    // Użycie: using var guard = LockState(); na początku metody dotykającej stanu.
    // Monitor jest reentrantny, więc zagnieżdżone wywołania są bezpieczne.
    private StateLockScope LockState() => new(_stateLock);

    private readonly struct StateLockScope : IDisposable
    {
        private readonly object _gate;

        public StateLockScope(object gate)
        {
            _gate = gate;
            Monitor.Enter(gate);
        }

        public void Dispose() => Monitor.Exit(_gate);
    }

    public void SaveAsync()
    {
        Interlocked.Exchange(ref _saveRequested, 1);
        if (Interlocked.CompareExchange(ref _saveWorkerActive, 1, 0) != 0)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                while (Volatile.Read(ref _saveRequested) == 1)
                {
                    await Task.Delay(500);
                    Interlocked.Exchange(ref _saveRequested, 0);

                    try
                    {
                        SaveToSqlite();
                    }
                    catch (Exception ex)
                    {
                        // np. kolekcja zmodyfikowana w trakcie snapshotu - ponów
                        System.Diagnostics.Debug.WriteLine($"SaveToSqlite failed: {ex.Message}");
                        Interlocked.Exchange(ref _saveRequested, 1);
                    }
                }
            }
            finally
            {
                Volatile.Write(ref _saveWorkerActive, 0);
                if (Volatile.Read(ref _saveRequested) == 1)
                    SaveAsync();
            }
        });
    }

    private void SaveToSqlite()
    {
        // Cały zapis pod _stateLock: snapshot list ORAZ serializacja encji (np. słownik Drops)
        // muszą być spójne i niemodyfikowane przez wątek sniffera.
        lock (_stateLock)
        {
            var characters = Data.ActiveCharacters.Concat(Data.Graveyard).ToList();
            var quests = Data.ActiveQuests.ToList();
            var openings = Data.Openings.ToList();
            var loot = Data.LootHistory.ToList();
            var runs = Data.DungeonRuns.ToList();
            var combat = Data.CombatHistory.ToList();
            var wishlist = Data.Wishlist.ToList();

            using var db = new TrackerDbContext(_dbFilePath);
            using var tx = db.Database.BeginTransaction();

            db.Characters.ExecuteDelete();
            db.Quests.ExecuteDelete();
            db.Openings.ExecuteDelete();
            db.LootHistory.ExecuteDelete();
            db.DungeonRuns.ExecuteDelete();
            db.CombatSessions.ExecuteDelete();
            db.WishlistTargets.ExecuteDelete();
            db.Settings.ExecuteDelete();

            db.Characters.AddRange(characters);
            db.Quests.AddRange(quests);
            db.Openings.AddRange(openings);
            db.LootHistory.AddRange(loot);
            db.DungeonRuns.AddRange(runs);
            db.CombatSessions.AddRange(combat);
            db.WishlistTargets.AddRange(wishlist);

            db.Settings.Add(new AppSetting { Key = "ActiveCharacterId", Value = Data.ActiveCharacterId.ToString() });
            db.Settings.Add(new AppSetting { Key = "LinkedAccountName", Value = LinkedAccountName });
            if (_legacyCountersRestored)
                db.Settings.Add(new AppSetting { Key = "LegacyCountersRestored", Value = "1" });

            db.SaveChanges();
            tx.Commit();
        }
    }
}