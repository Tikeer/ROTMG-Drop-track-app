using RotmgTrackerV1.Core.Enums;

namespace RotmgTrackerV1.Core.Models;

public class WishlistTarget
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string TargetDungeonName { get; set; } = string.Empty;

    public ItemRarity MinimumRarity { get; set; } = ItemRarity.Common;
    public bool RequireShiny { get; set; } = false;
    public string DesiredEnchantments { get; set; } = string.Empty;

    // Szacowana szansa na drop (np. 0.01 = 1%)
    public double EstimatedDropRate { get; set; } = 0.01;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }
    public bool IsCompleted { get; set; } = false;
}