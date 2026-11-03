namespace RotmgTrackerV1.Core.Models;

public class DungeonMarkQuest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DungeonName { get; set; } = string.Empty;
    public int CurrentMarks { get; set; } = 0;
    public int TargetMarks { get; set; } = 4;
    
    public bool IsCompleted => CurrentMarks >= TargetMarks;
}