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
}

public class TrackerStateService
{
    private readonly string _dbFilePath;
    private readonly object _dbLock = new();

    public TrackerSaveData Data { get; private set; } = new();

    // Dane konta wykrytego w Nexusie
    public string LinkedAccountName { get; private set; } = string.Empty;
    public string? PendingAccountName { get; private set; }
    public RotmgClass PendingAccountClass { get; private set; } = RotmgClass.Wizard;
    public bool PendingAccountSeasonal { get; private set; } = true;

    // Dane Live DPS (tylko w pamięci RAM)
    public string CurrentDungeonName { get; private set; } = "Nexus";
    public Dictionary<int, PlayerDpsEntry> CurrentDungeonPlayers { get; } = new();

    // Zdarzenie odświeżające UI, gdy sniffer odbierze pakiet w tle
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
                var defaultChar = CreateFreshCharacter("Seasonal #1", RotmgClass.Wizard, true);
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

        // Bezpieczne utworzenie tabeli LootHistory, jeśli plik .db istniał już wcześniej
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS LootHistory (
                Id TEXT PRIMARY KEY,
                ItemName TEXT NOT NULL,
                ItemId INTEGER NOT NULL,
                BagType INTEGER NOT NULL,
                DungeonName TEXT NOT NULL,
                CharacterName TEXT NOT NULL,
                CharacterClass INTEGER NOT NULL,
                AccountName TEXT NOT NULL,
                IsSeasonal INTEGER NOT NULL,
                DroppedAt TEXT NOT NULL
            );");

        if (!db.Characters.Any() && !db.Openings.Any())
        {
            TryMigrateLegacyJson();
            EnsureDropsInitialized();
            SaveToSqlite();
            return;
        }

        var allChars = db.Characters.ToList();
        Data.ActiveCharacters = allChars.Where(c => c.DiedAt == null).ToList();
        Data.Graveyard = allChars.Where(c => c.DiedAt != null).OrderByDescending(c => c.DiedAt).ToList();
        Data.ActiveQuests = db.Quests.ToList();
        Data.Openings = db.Openings.OrderByDescending(o => o.CreatedAt).ToList();
        Data.LootHistory = db.LootHistory.OrderByDescending(l => l.DroppedAt).ToList();

        var activeSetting = db.Settings.FirstOrDefault(s => s.Key == "ActiveCharacterId");
        if (activeSetting != null && Guid.TryParse(activeSetting.Value, out Guid parsedId))
            Data.ActiveCharacterId = parsedId;

        var accountSetting = db.Settings.FirstOrDefault(s => s.Key == "LinkedAccountName");
        if (accountSetting != null)
            LinkedAccountName = accountSetting.Value;

        EnsureDropsInitialized();
    }

    // --- 1. WYKRYWANIE KONTA W NEXUSIE ---
    public void HandleNexusPlayerDetected(string accountName, RotmgClass charClass, bool isSeasonal)
    {
        if (string.IsNullOrWhiteSpace(accountName)) return;

        // Jeśli konto jest już powiązane z tym nickiem, automatycznie wybieramy/tworzymy tę postać
        if (string.Equals(LinkedAccountName, accountName, StringComparison.OrdinalIgnoreCase))
        {
            SyncDetectedCharacter(accountName, charClass, isSeasonal);
            NotifyStateChanged();
            return;
        }

        // Jeśli konto nie jest jeszcze powiązane, wyświetlamy zapytanie w UI
        PendingAccountName = accountName;
        PendingAccountClass = charClass;
        PendingAccountSeasonal = isSeasonal;
        NotifyStateChanged();
    }

    public void ConfirmPendingAccount()
    {
        if (string.IsNullOrWhiteSpace(PendingAccountName)) return;

        LinkedAccountName = PendingAccountName;
        SyncDetectedCharacter(PendingAccountName, PendingAccountClass, PendingAccountSeasonal);
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

    private void SyncDetectedCharacter(string accountName, RotmgClass charClass, bool isSeasonal)
    {
        var existing = Data.ActiveCharacters.FirstOrDefault(c => c.Class == charClass && c.IsSeasonal == isSeasonal);
        if (existing != null)
        {
            Data.ActiveCharacterId = existing.Id;
        }
        else
        {
            AddNewCharacter($"{accountName} ({charClass})", charClass, isSeasonal);
        }
    }

    // --- 2. AUTOMATYCZNY LOOT HISTORY ---
    public void RecordLootDrop(string itemName, int itemId, DropType bagType, string? dungeonName = null)
    {
        var current = ActiveCharacter;
        var record = new LootDropRecord
        {
            ItemName = itemName,
            ItemId = itemId,
            BagType = bagType,
            DungeonName = string.IsNullOrWhiteSpace(dungeonName) ? CurrentDungeonName : dungeonName,
            CharacterName = current.Name,
            CharacterClass = current.Class,
            AccountName = LinkedAccountName,
            IsSeasonal = current.IsSeasonal,
            DroppedAt = DateTime.Now
        };

        Data.LootHistory.Insert(0, record);
        current.Drops[bagType] = current.Drops.GetValueOrDefault(bagType) + 1;

        SaveAsync();
        NotifyStateChanged();
    }

    public void RemoveLootRecord(Guid lootId)
    {
        var record = Data.LootHistory.FirstOrDefault(l => l.Id == lootId);
        if (record == null) return;

        Data.LootHistory.Remove(record);
        SaveAsync();
        NotifyStateChanged();
    }

    // --- 3. LIVE DUNGEON DPS (TYLKO RAM) ---
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

    // --- POZOSTAŁA LOGIKA Z V1 ---
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

    private CharacterSession CreateFreshCharacter(string name, RotmgClass rotmgClass, bool isSeasonal)
    {
        var ch = new CharacterSession
        {
            Name = string.IsNullOrWhiteSpace(name) ? rotmgClass.ToString() : name.Trim(),
            Class = rotmgClass,
            IsSeasonal = isSeasonal
        };

        foreach (var type in Enum.GetValues<DropType>())
            ch.Drops[type] = 0;

        return ch;
    }

    private void EnsureDropsInitialized()
    {
        if (Data.ActiveCharacters.Count == 0)
        {
            var first = CreateFreshCharacter("Wizard #1", RotmgClass.Wizard, true);
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

    public void AddNewCharacter(string name, RotmgClass rotmgClass, bool isSeasonal)
    {
        var newChar = CreateFreshCharacter(name, rotmgClass, isSeasonal);
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

    public void ArchiveAndResetCharacter()
    {
        var deadChar = ActiveCharacter;
        deadChar.DiedAt = DateTime.Now;
        Data.Graveyard.Insert(0, deadChar);
        Data.ActiveCharacters.Remove(deadChar);

        if (Data.ActiveCharacters.Count == 0)
        {
            var replacement = CreateFreshCharacter($"{deadChar.Class}", deadChar.Class, deadChar.IsSeasonal);
            Data.ActiveCharacters.Add(replacement);
        }

        Data.ActiveCharacterId = Data.ActiveCharacters[0].Id;
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
            db.Settings.RemoveRange(db.Settings);

            db.Characters.AddRange(Data.ActiveCharacters);
            db.Characters.AddRange(Data.Graveyard);
            db.Quests.AddRange(Data.ActiveQuests);
            db.Openings.AddRange(Data.Openings);
            db.LootHistory.AddRange(Data.LootHistory);

            db.Settings.Add(new AppSetting { Key = "ActiveCharacterId", Value = Data.ActiveCharacterId.ToString() });
            db.Settings.Add(new AppSetting { Key = "LinkedAccountName", Value = LinkedAccountName });

            db.SaveChanges();
        }
    }
}