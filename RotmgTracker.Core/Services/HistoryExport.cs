using RotmgTrackerV1.Core.Enums;
using RotmgTrackerV1.Core.Models;

namespace RotmgTrackerV1.Core.Services;

// Plik wymiany historii między komputerami (JSON). Wersja formatu rośnie tylko przy zmianach łamiących zgodność.
public class HistoryExportFile
{
    public string Format { get; set; } = "rotmg-tracker-history";
    public int Version { get; set; } = 1;
    public DateTime ExportedAt { get; set; } = DateTime.Now;
    public string MachineName { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;

    public List<ExportedCharacter> Characters { get; set; } = new();
    public List<LootDropRecord> Loot { get; set; } = new();
    public List<DungeonRunRecord> DungeonRuns { get; set; } = new();
    public List<CombatSessionRecord> CombatSessions { get; set; } = new();
}

public class ExportedCharacter
{
    public Guid Id { get; set; }
    public int RotmgCharId { get; set; }
    public RotmgClass Class { get; set; }
    public int ClassSlotNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsSeasonal { get; set; }
    public string SeasonTag { get; set; } = TrackerStateService.DefaultSeasonTag;
    public bool IsAlive { get; set; } = true;
    public int SkinId { get; set; }
    public int MaxedStats { get; set; }
    public int BaseFame { get; set; }
    public int TotalFame { get; set; }
    public DateTime? DiedAt { get; set; }
    public string? DeathServer { get; set; }
    public string? DeathDungeon { get; set; }
    public string? KilledBy { get; set; }

    public static ExportedCharacter From(CharacterSession c) => new()
    {
        Id = c.Id,
        RotmgCharId = c.RotmgCharId,
        Class = c.Class,
        ClassSlotNumber = c.ClassSlotNumber,
        Name = c.Name,
        IsSeasonal = c.IsSeasonal,
        SeasonTag = c.SeasonTag,
        IsAlive = c.IsAlive && c.DiedAt == null,
        SkinId = c.SkinId,
        MaxedStats = c.MaxedStats,
        BaseFame = c.BaseFame,
        TotalFame = c.TotalFame,
        DiedAt = c.DiedAt,
        DeathServer = c.DeathServer,
        DeathDungeon = c.DeathDungeon,
        KilledBy = c.KilledBy
    };
}

public class HistoryImportResult
{
    public int FilesImported { get; set; }
    public int LootAdded { get; set; }
    public int LootSkipped { get; set; }
    public int RunsAdded { get; set; }
    public int RunsSkipped { get; set; }
    public int CombatAdded { get; set; }
    public int CharactersCreated { get; set; }
    public List<string> Errors { get; } = new();

    public string Summary =>
        $"Files imported: {FilesImported} • drops: +{LootAdded} (duplicates skipped: {LootSkipped}) • " +
        $"completions: +{RunsAdded} (skipped: {RunsSkipped}) • DPS fights: +{CombatAdded} • new characters: {CharactersCreated}" +
        (Errors.Count > 0 ? $" • ERRORS: {string.Join(" | ", Errors)}" : string.Empty);
}
