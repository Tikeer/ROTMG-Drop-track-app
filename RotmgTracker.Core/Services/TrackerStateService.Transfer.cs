using System.Text.Json;
using RotmgTrackerV1.Core.Data;
using RotmgTrackerV1.Core.Models;

namespace RotmgTrackerV1.Core.Services;

// Zabezpieczenie danych: kopie zapasowe oraz eksport/import historii między komputerami (Mac <-> Windows)
public partial class TrackerStateService
{
    private static readonly JsonSerializerOptions TransferJson = new() { WriteIndented = true };

    public string AppDataFolder => Path.GetDirectoryName(_dbFilePath) ?? string.Empty;
    public string BackupFolder => DatabaseBackup.GetBackupDirectory(_dbFilePath);
    public string ExportFolder => Path.Combine(AppDataFolder, "exports");
    public string ImportFolder => Path.Combine(AppDataFolder, "import");

    public IReadOnlyList<FileInfo> GetBackupFiles() => DatabaseBackup.ListBackups(_dbFilePath);

    // Zapisuje stan z pamięci do bazy i robi z niej kopię; zwraca ścieżkę kopii
    public string CreateBackupNow()
    {
        SaveToSqlite();
        return DatabaseBackup.CreateManualBackup(_dbFilePath, "manual");
    }

    // Eksport całej historii (dropy, ukończenia, walki DPS, postacie) do pliku JSON w folderze exports
    public string ExportHistory()
    {
        string json;

        {
            using var guard = LockState();

            var file = new HistoryExportFile
            {
                ExportedAt = DateTime.Now,
                MachineName = Environment.MachineName,
                AccountName = LinkedAccountName,
                Characters = Data.ActiveCharacters.Concat(Data.Graveyard).Select(ExportedCharacter.From).ToList(),
                Loot = Data.LootHistory.ToList(),
                DungeonRuns = Data.DungeonRuns.ToList(),
                CombatSessions = Data.CombatHistory.ToList()
            };

            json = JsonSerializer.Serialize(file, TransferJson);
        }

        Directory.CreateDirectory(ExportFolder);

        string machine = string.Concat(Environment.MachineName.Where(char.IsLetterOrDigit));
        string path = Path.Combine(ExportFolder, $"rotmg_history_{machine}_{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.WriteAllText(path, json);
        return path;
    }

    // Importuje wszystkie pliki *.json z folderu import (duplikaty pomijane), zrobiwszy kopię bazy przed zmianą
    public HistoryImportResult ImportHistoryFromFolder()
    {
        var result = new HistoryImportResult();

        Directory.CreateDirectory(ImportFolder);
        var files = Directory.GetFiles(ImportFolder, "*.json").OrderBy(f => f, StringComparer.Ordinal).ToList();

        if (files.Count == 0)
        {
            result.Errors.Add($"No .json files in the import folder: {ImportFolder}");
            return result;
        }

        try
        {
            SaveToSqlite();
            DatabaseBackup.CreateManualBackup(_dbFilePath, "pre-import");
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Could not back up the database before importing - import aborted ({ex.Message})");
            return result;
        }

        string doneFolder = Path.Combine(ImportFolder, "done");

        foreach (var path in files)
        {
            try
            {
                var file = JsonSerializer.Deserialize<HistoryExportFile>(File.ReadAllText(path), TransferJson);

                if (file == null || file.Format != "rotmg-tracker-history" || file.Version != 1)
                {
                    result.Errors.Add($"{Path.GetFileName(path)}: not a RotMG Tracker history file (wrong format/version)");
                    continue;
                }

                if (!ApplyHistoryFile(file, result, Path.GetFileName(path)))
                    continue;

                Directory.CreateDirectory(doneFolder);
                File.Move(path, Path.Combine(doneFolder, $"{DateTime.Now:yyyyMMdd-HHmmss}_{Path.GetFileName(path)}"));
                result.FilesImported++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        if (result.FilesImported > 0)
        {
            using var guard = LockState();

            Data.LootHistory = Data.LootHistory.OrderByDescending(l => l.DroppedAt).ToList();
            Data.DungeonRuns = Data.DungeonRuns.OrderByDescending(r => r.CompletedAt).ToList();
            Data.CombatHistory = Data.CombatHistory.OrderByDescending(c => c.EndedAt).ToList();

            RecalculateAllRarityScores();
            foreach (var ch in Data.ActiveCharacters.Concat(Data.Graveyard))
                RecalculateCharacterDrops(ch);

            SaveAsync();
            NotifyStateChanged();
        }

        return result;
    }

    // Scala jeden plik historii ze stanem; zwraca false, gdy plik odrzucono (np. inne konto)
    private bool ApplyHistoryFile(HistoryExportFile file, HistoryImportResult result, string fileName)
    {
        using var guard = LockState();

        if (!string.IsNullOrWhiteSpace(file.AccountName) &&
            !string.IsNullOrWhiteSpace(LinkedAccountName) &&
            !string.Equals(file.AccountName, LinkedAccountName, StringComparison.OrdinalIgnoreCase))
        {
            result.Errors.Add($"{fileName}: file is from account '{file.AccountName}', but the app is linked to '{LinkedAccountName}' - skipped");
            return false;
        }

        // Postacie, do których odwołuje się jakikolwiek rekord w pliku
        var referencedCharacters = file.Loot.Select(l => l.CharacterId)
            .Concat(file.DungeonRuns.Select(r => r.CharacterId))
            .Concat(file.CombatSessions.Select(c => c.CharacterId))
            .ToHashSet();

        // Id postaci z pliku -> Id postaci w tej bazie (Id są losowe i różne na każdym komputerze)
        var characterMap = new Dictionary<Guid, Guid>();
        foreach (var ec in file.Characters)
        {
            // Pusty placeholder bez ID z gry i bez żadnych rekordów - nie tworzymy duplikatu
            if (ec.RotmgCharId <= 0 && !referencedCharacters.Contains(ec.Id))
                continue;

            characterMap[ec.Id] = ResolveImportedCharacter(ec, result).Id;
        }

        // Duplikaty: to samo Id albo ten sam item o tej samej chwili (np. drop wpisany ręcznie na obu komputerach)
        var lootIds = Data.LootHistory.Select(l => l.Id).ToHashSet();
        var lootKeys = Data.LootHistory.Select(l => (l.ItemId, l.DroppedAt.Ticks)).ToHashSet();

        foreach (var drop in file.Loot)
        {
            if (lootIds.Contains(drop.Id) || lootKeys.Contains((drop.ItemId, drop.DroppedAt.Ticks)))
            {
                result.LootSkipped++;
                continue;
            }

            if (characterMap.TryGetValue(drop.CharacterId, out var localCharacterId))
                drop.CharacterId = localCharacterId;

            Data.LootHistory.Add(drop);
            lootIds.Add(drop.Id);
            lootKeys.Add((drop.ItemId, drop.DroppedAt.Ticks));
            result.LootAdded++;
        }

        var runIds = Data.DungeonRuns.Select(r => r.Id).ToHashSet();
        var runKeys = Data.DungeonRuns.Select(r => (r.CharacterId, r.DungeonName, r.CompletedAt.Ticks)).ToHashSet();

        foreach (var run in file.DungeonRuns)
        {
            if (characterMap.TryGetValue(run.CharacterId, out var localCharacterId))
                run.CharacterId = localCharacterId;

            if (runIds.Contains(run.Id) || runKeys.Contains((run.CharacterId, run.DungeonName, run.CompletedAt.Ticks)))
            {
                result.RunsSkipped++;
                continue;
            }

            Data.DungeonRuns.Add(run);
            runIds.Add(run.Id);
            runKeys.Add((run.CharacterId, run.DungeonName, run.CompletedAt.Ticks));
            result.RunsAdded++;
        }

        var combatIds = Data.CombatHistory.Select(c => c.Id).ToHashSet();
        foreach (var session in file.CombatSessions)
        {
            if (combatIds.Contains(session.Id))
                continue;

            if (characterMap.TryGetValue(session.CharacterId, out var localCharacterId))
                session.CharacterId = localCharacterId;

            Data.CombatHistory.Add(session);
            combatIds.Add(session.Id);
            result.CombatAdded++;
        }

        return true;
    }

    // Dopasowanie postaci z pliku do postaci w bazie: Id -> ID postaci z gry -> (klasa, sezon, nazwa); inaczej nowa postać
    private CharacterSession ResolveImportedCharacter(ExportedCharacter ec, HistoryImportResult result)
    {
        var all = Data.ActiveCharacters.Concat(Data.Graveyard).ToList();

        var match = all.FirstOrDefault(c => c.Id == ec.Id);

        if (match == null && ec.RotmgCharId > 0)
            match = all.FirstOrDefault(c => c.RotmgCharId == ec.RotmgCharId);

        if (match == null)
        {
            var candidates = all.Where(c =>
                c.Class == ec.Class &&
                c.IsSeasonal == ec.IsSeasonal &&
                (c.RotmgCharId == 0 || ec.RotmgCharId == 0) &&
                string.Equals(c.Name, ec.Name, StringComparison.OrdinalIgnoreCase)).ToList();

            if (candidates.Count == 1)
                match = candidates[0];
        }

        if (match != null)
        {
            if (match.RotmgCharId == 0 && ec.RotmgCharId > 0)
                match.RotmgCharId = ec.RotmgCharId;

            return match;
        }

        var created = CreateFreshCharacter(ec.Name, ec.Class, ec.IsSeasonal, ec.RotmgCharId);
        created.SkinId = ec.SkinId;
        created.MaxedStats = ec.MaxedStats;
        created.BaseFame = ec.BaseFame;
        created.TotalFame = ec.TotalFame;

        if (ec.IsAlive)
        {
            Data.ActiveCharacters.Add(created);
        }
        else
        {
            created.IsAlive = false;
            created.DiedAt = ec.DiedAt ?? DateTime.Now;
            created.DeathServer = ec.DeathServer;
            created.DeathDungeon = ec.DeathDungeon;
            created.KilledBy = ec.KilledBy;
            Data.Graveyard.Add(created);
        }

        result.CharactersCreated++;
        return created;
    }
}
