using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RotmgTrackerV1.Core.Enums;
using RotmgTrackerV1.Core.Models;

namespace RotmgTrackerV1.Core.Data;

public class AppSetting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class TrackerDbContext : DbContext
{
    private readonly string _dbPath;

    public DbSet<CharacterSession> Characters => Set<CharacterSession>();
    public DbSet<DungeonMarkQuest> Quests => Set<DungeonMarkQuest>();
    public DbSet<OpeningSession> Openings => Set<OpeningSession>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();

    public TrackerDbContext(string dbPath)
    {
        _dbPath = dbPath;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={_dbPath}");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var jsonOptions = new JsonSerializerOptions();

        modelBuilder.Entity<AppSetting>().HasKey(s => s.Key);
        
        modelBuilder.Entity<CharacterSession>()
            .Property(c => c.Drops)
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<Dictionary<DropType, int>>(v, jsonOptions) ?? new());
        
        modelBuilder.Entity<OpeningSession>()
            .Property(o => o.Chests)
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<Dictionary<ChestType, ChestDropStats>>(v, jsonOptions) ?? new());
    }
}