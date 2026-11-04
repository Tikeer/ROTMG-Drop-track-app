using RotmgTrackerV1.Core.Enums;

namespace RotmgTrackerV1.Core.Models;

public class DungeonRunRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CharacterId { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public RotmgClass CharacterClass { get; set; } = RotmgClass.Wizard;

    public string DungeonName { get; set; } = string.Empty;
    public DungeonDifficultyTier DifficultyTier { get; set; } = DungeonDifficultyTier.Tier2_Godlands;
    public string ServerName { get; set; } = "EUWest";

    public bool IsSeasonal { get; set; } = true;
    public string SeasonTag { get; set; } = "S1";
    public DateTime CompletedAt { get; set; } = DateTime.Now;
}