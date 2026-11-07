namespace RotmgTrackerV1.Core.Models;

public class DungeonMarkQuest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string QuestTitle { get; set; } = "Daily Tinkerer Quest";
    public string DungeonName { get; set; } = string.Empty;

    public int MarkItemId { get; set; }
    public string MarkName { get; set; } = string.Empty;
    public int DungeonPortalObjectType { get; set; }

    public string BossName { get; set; } = "Dungeon Boss";
    public string RealmBiomeSource { get; set; } = "Realm / Godlands";
    public string RewardName { get; set; } = "Quest Chest";
    public int RewardItemId { get; set; }

    public int CurrentMarks { get; set; }
    public int TargetMarks { get; set; } = 3;
    public bool IsAutoTracked { get; set; } = true;
    public bool IsCompleted => CurrentMarks >= TargetMarks;
}