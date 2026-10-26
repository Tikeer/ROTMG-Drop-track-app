using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using RotmgTrackerV1.Core.Models;
using RotmgTrackerV1.Core.Enums;

namespace RotmgTrackerV1.Core.Services;

public class TrackerSaveData
{
    public CharacterSession? CurrentCharacter { get; set; } = new();
    public List<CharacterSession> ActiveCharacters { get; set; } = new();
    public Guid ActiveCharacterId { get; set; }
    public List<CharacterSession> Graveyard { get; set; } = new();
    public List<DungeonMarkQuest> ActiveQuests { get; set; } = new();
    public List<OpeningSession> Openings { get; set; } = new();
}

public class TrackerStateService
{
    private readonly string _saveFilePath;
    private readonly object _saveLock = new();
    public TrackerSaveData Data { get; private set; } = new();

    [JsonIgnore]
    public CharacterSession ActiveCharacter
    {
        get
        {
            var found = Data.ActiveCharacters.FirstOrDefault(c => c.Id == Data.ActiveCharacterId);
            if (found != null) return found;

            if (Data.ActiveCharacters.Count == 0)
            {
                var defaultChar = CreateFreshCharacter("Main Character", RotmgClass.Wizard, true);
                Data.ActiveCharacters.Add(defaultChar);
            }
            
            Data.ActiveCharacterId = Data.ActiveCharacters[0].Id;
            return Data.ActiveCharacters[0];
        }
    }
    public TrackerStateService()
    {
        string appFolder = AppDomain.CurrentDomain.BaseDirectory;
        _saveFilePath = Path.Combine(appFolder, "rotmg_data.json");
        Load();
        MigrateAndEnsureDrops();
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
        {
            ch.Drops[type] = 0;
        }

        return ch;
    }

    private void MigrateAndEnsureDrops()
    {
        if (Data.ActiveCharacters.Count == 0 && Data.CurrentCharacter != null)
        {
            Data.ActiveCharacters.Add(Data.CurrentCharacter);
            Data.ActiveCharacterId = Data.CurrentCharacter.Id;
            Data.CurrentCharacter = null;
        }

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
        Data.ActiveCharacters.RemoveAll(ch => ch.Id == characterId);
        if (Data.ActiveCharacterId == characterId)
        {
            Data.ActiveCharacterId = Data.ActiveCharacters[0].Id;
        }
        SaveAsync();
    }

    public void ChangeDropCount(DropType type, int delta)
    {
        var current = ActiveCharacter;
        if (!current.Drops.ContainsKey(type))
            current.Drops[type] = 0;

        current.Drops[type] = Math.Max(0, current.Drops[type] + delta);
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
        deadChar.DiedAt =  DateTime.Now;
        Data.Graveyard.Insert(0, deadChar);
        
        Data.ActiveCharacters.Remove(deadChar);

        if (Data.ActiveCharacters.Count == 0)
        {
            var replacement = CreateFreshCharacter($"{deadChar.Class}",deadChar.Class,deadChar.IsSeasonal);
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

    public void AddQuest(string dunegonName, int targetMarks)
    {
        if (string.IsNullOrWhiteSpace(dunegonName)) return;

        Data.ActiveQuests.Add(new DungeonMarkQuest
        {
            DungeonName = dunegonName.Trim(),
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
            Title = string.IsNullOrWhiteSpace(title) ? $"{Data.Openings.Count + 1} opening" : title
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

    public void SaveAsync()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(Data, options);
        _ = Task.Run(() =>
        {
            lock (_saveLock)
            {
                File.WriteAllText(_saveFilePath, json);
            }
        });
    }

    public void Load()
    {
        if (!File.Exists(_saveFilePath))
        {
            SaveAsync();
            return;
        }

        try
        {
            string json = File.ReadAllText(_saveFilePath);
            Data = JsonSerializer.Deserialize<TrackerSaveData>(json) ?? new TrackerSaveData();
        }
        catch
        {
            Data = new TrackerSaveData();
        }
    }
}