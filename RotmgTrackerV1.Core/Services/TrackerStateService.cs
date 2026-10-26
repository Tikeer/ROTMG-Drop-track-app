using System.Data;
using System.Text.Json;
using RotmgTrackerV1.Core.Models;
using RotmgTrackerV1.Core.Enums;

namespace RotmgTrackerV1.Core.Services;

public class TrackerSaveData
{
    public CharacterSession CurrentCharacter { get; set; } = new();
    public List<CharacterSession> Graveyard { get; set; } = new();
    public List<DungeonMarkQuest> ActiveQuests { get; set; } = new();
    public List<OpeningSession> Openings { get; set; } = new();
}

public class TrackerStateService
{
    private readonly string _saveFilePath;
    public TrackerSaveData Data { get; private set; } = new();

    public TrackerStateService()
    {
        string appFolder = AppDomain.CurrentDomain.BaseDirectory;
        _saveFilePath = Path.Combine(appFolder, "rotmg_data.json");
        Load();
    }

    public void ChangeDropCount(DropType type, int delta)
    {
        if (!Data.CurrentCharacter.Drops.ContainsKey(type))
            Data.CurrentCharacter.Drops[type] = 0;

        Data.CurrentCharacter.Drops[type] = Math.Max(0, Data.CurrentCharacter.Drops[type] + delta);
        Save();
    }

    public void ToggleSeasonal()
    {
        Data.CurrentCharacter.IsSeasonal = !Data.CurrentCharacter.IsSeasonal;
        Save();
    }

    public void ArchiveAndResetCharacter()
    {
        Data.CurrentCharacter.DiedAt =  DateTime.Now;
        Data.Graveyard.Add(Data.CurrentCharacter);
        
        bool wasSeasonal = Data.CurrentCharacter.IsSeasonal;
        Data.CurrentCharacter = new CharacterSession { IsSeasonal = wasSeasonal };
        Save();
    }

    public void AddQuest(string dunegonName, int targetMarks)
    {
        if (string.IsNullOrWhiteSpace(dunegonName)) return;

        Data.ActiveQuests.Add(new DungeonMarkQuest
        {
            DungeonName = dunegonName.Trim(),
            TargetMarks = Math.Max(1, targetMarks)
        });
        Save();
    }

    public void ChangeQuestMark(Guid questId, int delta)
    {
        var quest = Data.ActiveQuests.FirstOrDefault(q => q.Id == questId);
        if (quest == null) return;
        
        quest.CurrentMarks = Math.Max(0, quest.CurrentMarks + delta);
        Save();
    }

    public void RemoveQuest(Guid questId)
    {
        Data.ActiveQuests.RemoveAll(q => q.Id == questId);
        Save();
    }

    public void AddOpeningSession(string title)
    {
        Data.Openings.Insert(0, new OpeningSession
        {
            Title = string.IsNullOrWhiteSpace(title) ? $"{Data.Openings.Count + 1} opening" : title
        });
        Save();
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

        Save();
    }

    public void Save()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(Data, options);
        File.WriteAllText(_saveFilePath, json);
    }

    public void Load()
    {
        if (!File.Exists(_saveFilePath))
        {
            InitializeWithNotebookData();
            Save();
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

    private void InitializeWithNotebookData()
    {
        Data.CurrentCharacter.IsSeasonal = true;
        Data.CurrentCharacter.Drops[DropType.RegularWhite] = 0;
        Data.CurrentCharacter.Drops[DropType.LegendaryWhite] = 0;
        Data.CurrentCharacter.Drops[DropType.ST] = 0;
        
        Data.ActiveQuests.Add(new DungeonMarkQuest { DungeonName = "Nest", TargetMarks = 3 });
        Data.ActiveQuests.Add(new DungeonMarkQuest { DungeonName = "Davy", TargetMarks = 4 });
    }
}