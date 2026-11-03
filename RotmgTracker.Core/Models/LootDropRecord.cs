using RotmgTrackerV1.Core.Enums;

namespace RotmgTrackerV1.Core.Models;

public class LootDropRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ItemName { get; set; } = string.Empty;
    public int ItemId { get; set; }
    public DropType BagType { get; set; } = DropType.RegularWhite;
    public string DungeonName { get; set; } = "Unknown Dungeon";
    public string CharacterName { get; set; } = string.Empty;
    public RotmgClass CharacterClass { get; set; } = RotmgClass.Wizard;
    public string AccountName { get; set; } = string.Empty;
    public bool IsSeasonal { get; set; } = true;
    public DateTime DroppedAt { get; set; } = DateTime.Now;
}