using Microsoft.Data.Sqlite;

namespace RotmgTrackerV1.Core.Data;

/// <summary>
/// Kopie zapasowe bazy SQLite (folder "backups" obok bazy).
/// - dzienna: pierwsza w danym dniu (stan z początku dnia), trzymanych 14,
/// - ostatnia: odświeżana najwyżej raz na godzinę (rotmg_tracker_latest.db),
/// - ręczna / przed importem: rotmg_tracker_{powód}_{data}.db, trzymanych 5 na powód.
/// Snapshot robi VACUUM INTO (spójny także przy trybie WAL i działającej aplikacji).
/// Błąd kopii nigdy nie przerywa pracy aplikacji.
/// </summary>
public static class DatabaseBackup
{
    private const int DailyBackupsToKeep = 14;
    private const int ManualBackupsToKeep = 5;
    private static readonly TimeSpan LatestInterval = TimeSpan.FromHours(1);

    private static readonly object Sync = new();
    private static DateTime _lastDailyDay = DateTime.MinValue;
    private static DateTime _lastLatestAt = DateTime.MinValue;

    public static string GetBackupDirectory(string dbFilePath) =>
        Path.Combine(Path.GetDirectoryName(dbFilePath) ?? string.Empty, "backups");

    // Wołane przy starcie i po każdym zapisie bazy - tanie, gdy nic nie ma do zrobienia
    public static void Run(string dbFilePath)
    {
        lock (Sync)
        {
            try
            {
                if (!HasData(dbFilePath))
                    return;

                string dir = GetBackupDirectory(dbFilePath);
                Directory.CreateDirectory(dir);

                if (_lastDailyDay != DateTime.Today)
                {
                    string daily = Path.Combine(dir, $"rotmg_tracker_{DateTime.Now:yyyyMMdd}.db");
                    if (!File.Exists(daily))
                        CreateSnapshot(dbFilePath, daily);

                    Prune(dir, "rotmg_tracker_????????.db", DailyBackupsToKeep);
                    _lastDailyDay = DateTime.Today;
                }

                if (DateTime.Now - _lastLatestAt >= LatestInterval)
                {
                    CreateSnapshot(dbFilePath, Path.Combine(dir, "rotmg_tracker_latest.db"));
                    _lastLatestAt = DateTime.Now;
                }
            }
            catch
            {
                // Kopia nie może blokować aplikacji; kolejna próba przy następnym zapisie
            }
        }
    }

    // Kopia na żądanie (przycisk) lub przed importem; zwraca ścieżkę pliku
    public static string CreateManualBackup(string dbFilePath, string reason)
    {
        lock (Sync)
        {
            string dir = GetBackupDirectory(dbFilePath);
            Directory.CreateDirectory(dir);

            string path = Path.Combine(dir, $"rotmg_tracker_{reason}_{DateTime.Now:yyyyMMdd-HHmmss}.db");
            CreateSnapshot(dbFilePath, path);
            Prune(dir, $"rotmg_tracker_{reason}_*.db", ManualBackupsToKeep);
            return path;
        }
    }

    public static IReadOnlyList<FileInfo> ListBackups(string dbFilePath)
    {
        string dir = GetBackupDirectory(dbFilePath);
        if (!Directory.Exists(dir))
            return Array.Empty<FileInfo>();

        return new DirectoryInfo(dir)
            .GetFiles("rotmg_tracker_*.db")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ToList();
    }

    private static bool HasData(string dbFilePath) =>
        File.Exists(dbFilePath) && new FileInfo(dbFilePath).Length > 0;

    private static void CreateSnapshot(string dbFilePath, string targetPath)
    {
        if (!HasData(dbFilePath))
            throw new InvalidOperationException("Baza jest pusta lub nie istnieje - kopia nie została utworzona.");

        string tmp = targetPath + ".tmp";
        if (File.Exists(tmp))
            File.Delete(tmp);

        using (var conn = new SqliteConnection($"Data Source={dbFilePath};Mode=ReadOnly;Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "VACUUM INTO $path";
            cmd.Parameters.AddWithValue("$path", tmp);
            cmd.ExecuteNonQuery();
        }

        File.Move(tmp, targetPath, overwrite: true);
    }

    private static void Prune(string dir, string pattern, int keep)
    {
        // Nazwy zawierają datę, więc sortowanie po nazwie = chronologiczne
        var obsolete = Directory.GetFiles(dir, pattern)
            .OrderByDescending(f => f, StringComparer.Ordinal)
            .Skip(keep);

        foreach (var file in obsolete)
        {
            try { File.Delete(file); }
            catch (IOException) { }
        }
    }
}
