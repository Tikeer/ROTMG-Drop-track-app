using RotmgTrackerV1.Core.Enums;

namespace RotmgTrackerV1.Core.Models;

public class PlayerDpsEntry
{
    public int ObjectId { get; set; }
    public string PlayerName { get; set; } = "Unknown";
    public RotmgClass Class { get; set; } = RotmgClass.Wizard;
    public int TotalDamage { get; set; }
    public int HitsCount { get; set; }
    public bool IsMe { get; set; }
}