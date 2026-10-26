using RotmgTrackerV1.Core.Enums;

namespace RotmgTrackerV1.Core.Models;

public class ChestDropStats
{
    public int Whites { get; set; } = 0;
    public int STs {get; set; } = 0;
}

public class OpeningSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "New Opening";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int TotalChests { get; set; } = 0;
    public bool IsExpanded { get; set; } = true;

    public Dictionary<ChestType, ChestDropStats> Chests { get; set; } = new()
    {
        { ChestType.Standard, new ChestDropStats() },
        { ChestType.Mighty, new ChestDropStats() },
        { ChestType.Citadel, new ChestDropStats() },
        { ChestType.Crystalized, new ChestDropStats() },
        { ChestType.Cultish, new ChestDropStats() },
        { ChestType.Honeyed, new ChestDropStats() },
        { ChestType.Marble, new ChestDropStats() },
        { ChestType.Moonlit, new ChestDropStats() },
        { ChestType.Royal, new ChestDropStats() },
        { ChestType.Spectral, new ChestDropStats() },
        { ChestType.Steampunk, new ChestDropStats() },
        { ChestType.Void, new ChestDropStats() }
    };

    public int TotalWhites => Chests.Values.Sum(c => c.Whites);
    public int TotalSTs => Chests.Values.Sum(c => c.STs);
}