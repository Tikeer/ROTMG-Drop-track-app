using RotmgTrackerV1.Core.Enums;

namespace RotmgTrackerV1.Core.Models;

public class CharacterSession
{
    public Guid Id { get; set; } =  Guid.NewGuid();
    public string Name { get; set; } = "Seasonal #1";
    public RotmgClass Class { get; set; } = RotmgClass.Wizard;
    public bool IsSeasonal { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? DiedAt  { get; set; }

    public Dictionary<DropType, int> Drops { get; set; } = new();

    public int TotalDrops => Drops.Values.Sum();
}