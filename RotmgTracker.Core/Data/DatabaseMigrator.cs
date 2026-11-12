using Microsoft.EntityFrameworkCore;

namespace RotmgTrackerV1.Core.Data;

/// <summary>
/// Jedyne miejsce, które zmienia schemat bazy. Zastępuje ręczne ALTER-y.
/// - nowa baza: Migrate() tworzy wszystko,
/// - baza sprzed migracji (są tabele, brak __EFMigrationsHistory): kopia zapasowa,
///   sprawdzenie schematu i oznaczenie InitialCreate jako już zastosowanej (baseline),
/// - baza z historią: kopia zapasowa tylko gdy czekają migracje, potem Migrate().
/// </summary>
public static class DatabaseMigrator
{
    private const int BackupsToKeep = 5;

    // Najnowsze kolumny dodawane dawniej ręcznymi ALTER-ami. Jeśli ich brak,
    // baza jest zbyt stara na baseline i trzeba ją odtworzyć (np. z rotmg_data.json).
    private static readonly (string Table, string Column)[] LegacyMarkers =
    [
        ("Characters", "LegacyDrops"),
        ("LootHistory", "RarityScore"),
        ("Quests", "IsAutoTracked"),
        ("DungeonRuns", "CompletedAt"),
        ("CombatSessions", "Participants"),
        ("WishlistTargets", "IsCompleted")
    ];

    public static void Initialize(string dbFilePath)
    {
        using var db = new TrackerDbContext(dbFilePath);

        bool hasHistory = TableExists(db, "__EFMigrationsHistory");
        bool isLegacyDb = !hasHistory && TableExists(db, "Characters");

        if (isLegacyDb)
        {
            VerifyLegacySchema(db, dbFilePath);
            CreateBackup(dbFilePath, "pre-baseline");
            MarkInitialMigrationAsApplied(db);
        }
        else if (hasHistory && db.Database.GetPendingMigrations().Any())
        {
            CreateBackup(dbFilePath, "pre-migrate");
        }

        db.Database.Migrate();
    }

    private static bool TableExists(TrackerDbContext db, string table)
    {
        return db.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = {0}", table)
            .AsEnumerable()
            .First() > 0;
    }

    private static bool ColumnExists(TrackerDbContext db, string table, string column)
    {
        return db.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM pragma_table_info({0}) WHERE name = {1}", table, column)
            .AsEnumerable()
            .First() > 0;
    }

    private static void VerifyLegacySchema(TrackerDbContext db, string dbFilePath)
    {
        foreach (var (table, column) in LegacyMarkers)
        {
            if (!ColumnExists(db, table, column))
                throw new InvalidOperationException(
                    $"Baza '{dbFilePath}' ma przestarzały schemat (brak {table}.{column}). " +
                    "Nie wykonuję migracji, żeby jej nie uszkodzić. Plik bazy nie został zmieniony.");
        }
    }

    private static void MarkInitialMigrationAsApplied(TrackerDbContext db)
    {
        string? initial = db.Database.GetMigrations().FirstOrDefault();
        if (initial == null)
            throw new InvalidOperationException(
                "Brak migracji InitialCreate w projekcie Core. Wygeneruj ją: dotnet ef migrations add InitialCreate.");

        db.Database.ExecuteSqlRaw(
            "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" (" +
            "\"MigrationId\" TEXT NOT NULL CONSTRAINT \"PK___EFMigrationsHistory\" PRIMARY KEY, " +
            "\"ProductVersion\" TEXT NOT NULL);");

        db.Database.ExecuteSqlRaw(
            "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({0}, {1});",
            initial, "10.0.14");
    }

    private static void CreateBackup(string dbFilePath, string reason)
    {
        if (!File.Exists(dbFilePath) || new FileInfo(dbFilePath).Length == 0)
            return;

        string dir = Path.GetDirectoryName(dbFilePath)!;
        string name = Path.GetFileName(dbFilePath);
        string backupPath = Path.Combine(dir, $"{name}.{reason}-{DateTime.Now:yyyyMMdd-HHmmss}.bak");
        File.Copy(dbFilePath, backupPath, overwrite: false);

        var obsolete = new DirectoryInfo(dir)
            .GetFiles($"{name}.*.bak")
            .OrderByDescending(f => f.CreationTimeUtc)
            .Skip(BackupsToKeep);

        foreach (var file in obsolete)
        {
            try { file.Delete(); }
            catch (IOException) { }
        }
    }
}