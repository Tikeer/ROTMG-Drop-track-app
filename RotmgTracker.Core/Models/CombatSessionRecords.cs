using RotmgTrackerV1.Core.Enums;

namespace RotmgTrackerV1.Core.Models;

public class CombatSessionRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DungeonName { get; set; } = string.Empty;
    public string BossName { get; set; } = string.Empty;
    public int BossObjectType { get; set; }
    public double GraveDifficulty { get; set; } = 3.0;

    public Guid CharacterId { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public RotmgClass CharacterClass { get; set; } = RotmgClass.Wizard;

    public int TotalBossDamage { get; set; }
    public int MyDamage { get; set; }
    public double MySharePercent => TotalBossDamage > 0 ? Math.Round((double)MyDamage / TotalBossDamage * 100.0, 1) : 0.0;

    public List<PlayerDpsEntry> Participants { get; set; } = new();
    public DateTime EndedAt { get; set; } = DateTime.Now;
}