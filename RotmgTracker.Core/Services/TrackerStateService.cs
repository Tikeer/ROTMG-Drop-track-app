using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using RotmgTrackerV1.Core.Data;
using RotmgTrackerV1.Core.Enums;
using RotmgTrackerV1.Core.Models;

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
    public List<WishlistTarget> Wishlist { get; set; } = new();
}

public class TrackerStateService
{
    private readonly string _dbFilePath;
    private readonly object _dbLock = new();

    public TrackerSaveData Data { get; private set; } = new();

    public string LinkedAccountName { get; private set; } = string.Empty;
    public string CurrentSeasonTag { get; private set; } = "S1";
    public string CurrentServerName { get; private set; } = "EUWest";

    public string? PendingAccountName { get; private set; }
    public RotmgClass PendingAccountClass { get; private set; } = RotmgClass.Wizard;
    public bool PendingAccountSeasonal { get; private set; } = true;

    public string CurrentDungeonName { get; private set; } = "Nexus";
    public Dictionary<int, PlayerDpsEntry> CurrentDungeonPlayers { get; } = new();

    public event Action? OnStateChanged;

    [JsonIgnore]
    public CharacterSession ActiveCharacter
    {
        get
        {
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
        using var db = new TrackerDbContext(_dbFilePath);
        db.Database.EnsureCreated();
        EnsureSchemaUpgraded(db);

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
        Data.Wishlist = db.WishlistTargets.OrderByDescending(w => w.CreatedAt).ToList();

        var activeSetting = db.Settings.FirstOrDefault(s => s.Key == "ActiveCharacterId");
        if (activeSetting != null && Guid.TryParse(activeSetting.Value, out Guid parsedId))
            Data.ActiveCharacterId = parsedId;

        var accountSetting = db.Settings.FirstOrDefault(s => s.Key == "LinkedAccountName");
        if (accountSetting != null)
            LinkedAccountName = accountSetting.Value;

        EnsureDropsInitialized();
    }

    // Automatycznie aktualizuje istniejący plik SQLite o nowe kolumny i tabele bez utraty danych
    private static void EnsureSchemaUpgraded(TrackerDbContext db)
    {
        string[] migrations =
        [
            "CREATE TABLE IF NOT EXISTS DungeonRuns (Id TEXT PRIMARY KEY, CharacterId TEXT NOT NULL, CharacterName TEXT NOT NULL, CharacterClass INTEGER NOT NULL, DungeonName TEXT NOT NULL, DifficultyTier INTEGER NOT NULL, ServerName TEXT NOT NULL, IsSeasonal INTEGER NOT NULL, SeasonTag TEXT NOT NULL, CompletedAt TEXT NOT NULL);",
            "CREATE TABLE IF NOT EXISTS WishlistTargets (Id TEXT PRIMARY KEY, ItemId INTEGER NOT NULL, ItemName TEXT NOT NULL, TargetDungeonName TEXT NOT NULL, MinimumRarity INTEGER NOT NULL, RequireShiny INTEGER NOT NULL, DesiredEnchantments TEXT NOT NULL, EstimatedDropRate REAL NOT NULL, CreatedAt TEXT NOT NULL, CompletedAt TEXT NULL, IsCompleted INTEGER NOT NULL);"
        ];

        foreach (var sql in migrations)
        {
            try { db.Database.ExecuteSqlRaw(sql); } catch { }
        }

        string[] alterColumns =
        [
            "ALTER TABLE Characters ADD COLUMN RotmgCharId INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Characters ADD COLUMN ClassSlotNumber INTEGER NOT NULL DEFAULT 1;",
            "ALTER TABLE Characters ADD COLUMN SkinId INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Characters ADD COLUMN SeasonTag TEXT NOT NULL DEFAULT 'S1';",
            "ALTER TABLE Characters ADD COLUMN IsAlive INTEGER NOT NULL DEFAULT 1;",
            "ALTER TABLE Characters ADD COLUMN MaxedStats INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Characters ADD COLUMN BaseFame INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Characters ADD COLUMN TotalFame INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Characters ADD COLUMN EquipmentIds TEXT NOT NULL DEFAULT '[0,0,0,0]';",
            "ALTER TABLE Characters ADD COLUMN DeathServer TEXT NULL;",
            "ALTER TABLE Characters ADD COLUMN DeathDungeon TEXT NULL;",
            "ALTER TABLE Characters ADD COLUMN KilledBy TEXT NULL;",
            "ALTER TABLE LootHistory ADD COLUMN CharacterId TEXT NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';",
            "ALTER TABLE LootHistory ADD COLUMN CharacterMaxedStatsAtDrop INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE LootHistory ADD COLUMN BaseFameAtDrop INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE LootHistory ADD COLUMN SeasonTag TEXT NOT NULL DEFAULT 'S1';",
            "ALTER TABLE LootHistory ADD COLUMN ItemTier INTEGER NOT NULL DEFAULT -1;",
            "ALTER TABLE LootHistory ADD COLUMN PhysicalBag INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE LootHistory ADD COLUMN Rarity INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE LootHistory ADD COLUMN Enchantments TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE LootHistory ADD COLUMN IsShiny INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE LootHistory ADD COLUMN DungeonDifficulty INTEGER NOT NULL DEFAULT 2;",
            "ALTER TABLE LootHistory ADD COLUMN ServerName TEXT NOT NULL DEFAULT 'EUWest';",
            "ALTER TABLE LootHistory ADD COLUMN RarityScore REAL NOT NULL DEFAULT 100.0;"
        ];

        foreach (var alter in alterColumns)
        {
            try { db.Database.ExecuteSqlRaw(alter); } catch { }
        }
    }

    // --- 1. IDENTYFIKACJA POSTACI I AUTOMATYCZNA NUMERACJA SLOTÓW (np. Archer #1, Archer #2) ---
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

    // --- 2. REJESTRACJA DROPÓW + DYNAMICZNA SYNCHRONIZACJA LICZNIKÓW + RARITY SCORE ---
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
        string enchantments = "")
    {
        var current = ActiveCharacter;
        double score = RarityScoreEngine.CalculateIntrinsicScore(bagQuality, rarity, isShiny, difficulty);

        var record = new LootDropRecord
        {
            CharacterId = current.Id,
            CharacterName = current.FormattedTitle,
            CharacterClass = current.Class,
            CharacterMaxedStatsAtDrop = current.MaxedStats,
            BaseFameAtDrop = current.BaseFame,
            AccountName = LinkedAccountName,
            IsSeasonal = current.IsSeasonal,
            SeasonTag = current.SeasonTag,
            ItemId = itemId,
            ItemName = itemName,
            ItemTier = itemTier,
            PhysicalBag = physicalBag,
            BagType = bagQuality,
            Rarity = rarity,
            Enchantments = enchantments,
            IsShiny = isShiny || bagQuality is DropType.Shiny or DropType.Shivine,
            DungeonName = string.IsNullOrWhiteSpace(dungeonName) ? CurrentDungeonName : dungeonName,
            DungeonDifficulty = difficulty,
            ServerName = CurrentServerName,
            RarityScore = score,
            DroppedAt = DateTime.Now
        };

        Data.LootHistory.Insert(0, record);
        RecalculateCharacterDrops(current);

        // Automatyczne odhaczenie celu na Wishliście, jeśli wypadł szukany przedmiot
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
        var record = Data.LootHistory.FirstOrDefault(l => l.Id == lootId);
        if (record == null) return;

        Data.LootHistory.Remove(record);

        // Natychmiastowe przeliczenie licznika postaci na podstawie LootHistory (Single Source of Truth)
        var owner = Data.ActiveCharacters.Concat(Data.Graveyard).FirstOrDefault(c => c.Id == record.CharacterId);
        if (owner != null)
            RecalculateCharacterDrops(owner);

        SaveAsync();
        NotifyStateChanged();
    }

    private void RecalculateCharacterDrops(CharacterSession character)
    {
        var charLoot = Data.LootHistory.Where(l => l.CharacterId == character.Id).ToList();
        if (charLoot.Count == 0) return;

        foreach (var type in Enum.GetValues<DropType>())
        {
            character.Drops[type] = charLoot.Count(l => l.BagType == type);
        }
    }

    // --- 3. LICZNIK UKOŃCZONYCH DUNGEONÓW & INTEGRACJA Z WISHLISTĄ ---
    public void RecordDungeonCompletion(string dungeonName, DungeonDifficultyTier difficulty = DungeonDifficultyTier.Tier2_Godlands)
    {
        if (string.IsNullOrWhiteSpace(dungeonName)) return;

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

    public int GetRunsSinceWishlistTargetCreated(WishlistTarget target)
    {
        return Data.DungeonRuns.Count(r =>
            string.Equals(r.DungeonName, target.TargetDungeonName, StringComparison.OrdinalIgnoreCase) &&
            r.CompletedAt >= target.CreatedAt &&
            (target.CompletedAt == null || r.CompletedAt <= target.CompletedAt));
    }

    public void AddWishlistTarget(string itemName, string dungeonName, ItemRarity minRarity, bool requireShiny, double dropRate)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return;

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

    // --- 4. OBSŁUGA KONTA, POSTACI I GRAVEYARDU (WZOROWANE NA REALMEYE) ---
    public void HandleNexusPlayerDetected(string accountName, RotmgClass charClass, bool isSeasonal, int rotmgCharId = 0)
    {
        if (string.IsNullOrWhiteSpace(accountName)) return;

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
        if (string.IsNullOrWhiteSpace(PendingAccountName)) return;

        LinkedAccountName = PendingAccountName;
        SyncDetectedCharacter(PendingAccountName, PendingAccountClass, PendingAccountSeasonal, 0);
        PendingAccountName = null;
        SaveAsync();
        NotifyStateChanged();
    }

    public void DismissPendingAccount()
    {
        PendingAccountName = null;
        NotifyStateChanged();
    }

    public void UnlinkAccount()
    {
        LinkedAccountName = string.Empty;
        SaveAsync();
        NotifyStateChanged();
    }

    private void SyncDetectedCharacter(string accountName, RotmgClass charClass, bool isSeasonal, int rotmgCharId)
    {
        CharacterSession? existing = null;

        if (rotmgCharId > 0)
            existing = Data.ActiveCharacters.FirstOrDefault(c => c.RotmgCharId == rotmgCharId);

        existing ??= Data.ActiveCharacters.FirstOrDefault(c => c.Class == charClass && c.IsSeasonal == isSeasonal);

        if (existing != null)
        {
            Data.ActiveCharacterId = existing.Id;
        }
        else
        {
            AddNewCharacter($"{charClass}", charClass, isSeasonal, rotmgCharId);
        }
    }

    public void UpdateActiveCharacterStats(int maxedStats, int baseFame, int[]? equipment = null)
    {
        var current = ActiveCharacter;
        current.MaxedStats = Math.Clamp(maxedStats, 0, 8);
        current.BaseFame = Math.Max(0, baseFame);
        if (equipment is { Length: 4 })
            current.EquipmentIds = equipment;

        SaveAsync();
        NotifyStateChanged();
    }

    public void ArchiveAndResetCharacter() => KillActiveCharacter("Unknown", CurrentDungeonName, ActiveCharacter.BaseFame);

    public void KillActiveCharacter(string killedBy, string dungeonName, int totalDeathFame)
    {
        var deadChar = ActiveCharacter;
        deadChar.IsAlive = false;
        deadChar.DiedAt = DateTime.Now;
        deadChar.KilledBy = killedBy;
        deadChar.DeathDungeon = dungeonName;
        deadChar.DeathServer = CurrentServerName;
        deadChar.TotalFame = Math.Max(deadChar.BaseFame, totalDeathFame);

        // Postać przechodzi do Graveyardu, ale jej UUID i powiązane z nią dropy w LootHistory pozostają nienaruszone!
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

    // --- 5. LIVE DUNGEON DPS ---
    public void EnterDungeon(string dungeonName)
    {
        CurrentDungeonName = string.IsNullOrWhiteSpace(dungeonName) ? "Unknown Dungeon" : dungeonName;
        CurrentDungeonPlayers.Clear();
        NotifyStateChanged();
    }

    public void RecordPlayerDamage(int objectId, string playerName, RotmgClass charClass, int damage)
    {
        if (damage <= 0) return;

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
        NotifyStateChanged();
    }

    public void ClearDungeonDps()
    {
        CurrentDungeonPlayers.Clear();
        NotifyStateChanged();
    }

    // --- POZOSTAŁE METODY POMOCNICZE ---
    private void TryMigrateLegacyJson()
    {
        string legacyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rotmg_data.json");
        if (!File.Exists(legacyPath)) return;

        try
        {
            string json = File.ReadAllText(legacyPath);
            var legacyData = JsonSerializer.Deserialize<TrackerSaveData>(json);
            if (legacyData != null)
            {
                Data = legacyData;
                if (Data.ActiveCharacters.Count == 0 && Data.CurrentCharacter != null)
                {
                    Data.ActiveCharacters.Add(Data.CurrentCharacter);
                    Data.ActiveCharacterId = Data.CurrentCharacter.Id;
                }
            }
        }
        catch { }
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
        if (Data.ActiveCharacters.Any(c => c.Id == characterId))
        {
            Data.ActiveCharacterId = characterId;
            SaveAsync();
        }
    }

    public void AddNewCharacter(string name, RotmgClass rotmgClass, bool isSeasonal, int rotmgCharId = 0)
    {
        var newChar = CreateFreshCharacter(name, rotmgClass, isSeasonal, rotmgCharId);
        Data.ActiveCharacters.Add(newChar);
        Data.ActiveCharacterId = newChar.Id;
        SaveAsync();
    }

    public void DeleteActiveCharacter(Guid characterId)
    {
        if (Data.ActiveCharacters.Count <= 1) return;
        Data.ActiveCharacters.RemoveAll(c => c.Id == characterId);
        if (Data.ActiveCharacterId == characterId)
            Data.ActiveCharacterId = Data.ActiveCharacters[0].Id;

        SaveAsync();
    }

    public void ChangeDropCount(DropType type, int delta)
    {
        var current = ActiveCharacter;
        current.Drops[type] = Math.Max(0, current.Drops.GetValueOrDefault(type) + delta);
        SaveAsync();
    }

    public void ToggleSeasonal()
    {
        ActiveCharacter.IsSeasonal = !ActiveCharacter.IsSeasonal;
        SaveAsync();
    }

    public void RemoveGraveyardEntry(Guid id)
    {
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

    public void SaveAsync()
    {
        _ = Task.Run(SaveToSqlite);
    }

    private void SaveToSqlite()
    {
        lock (_dbLock)
        {
            using var db = new TrackerDbContext(_dbFilePath);
            db.Database.EnsureCreated();

            db.Characters.RemoveRange(db.Characters);
            db.Quests.RemoveRange(db.Quests);
            db.Openings.RemoveRange(db.Openings);
            db.LootHistory.RemoveRange(db.LootHistory);
            db.DungeonRuns.RemoveRange(db.DungeonRuns);
            db.WishlistTargets.RemoveRange(db.WishlistTargets);
            db.Settings.RemoveRange(db.Settings);

            db.Characters.AddRange(Data.ActiveCharacters);
            db.Characters.AddRange(Data.Graveyard);
            db.Quests.AddRange(Data.ActiveQuests);
            db.Openings.AddRange(Data.Openings);
            db.LootHistory.AddRange(Data.LootHistory);
            db.DungeonRuns.AddRange(Data.DungeonRuns);
            db.WishlistTargets.AddRange(Data.Wishlist);

            db.Settings.Add(new AppSetting { Key = "ActiveCharacterId", Value = Data.ActiveCharacterId.ToString() });
            db.Settings.Add(new AppSetting { Key = "LinkedAccountName", Value = LinkedAccountName });

            db.SaveChanges();
        }
    }
}