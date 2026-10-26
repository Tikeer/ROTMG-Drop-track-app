using RotmgTrackerV1.Core.Enums;

namespace RotmgTrackerV1.Core.Models;

public class CharacterSession
{
    public Guid Id { get; set; } =  Guid.NewGuid();
    public string Name { get; set; } = "Main Character";
    public bool IsSeasonal { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? DiedAt  { get; set; }

    public Dictionary<DropType, int> Drops { get; set; } = new()
    {
        { DropType.RegularWhite, 0 },
        { DropType.LegendaryWhite, 0 },
        { DropType.ST, 0 },
        { DropType.BPs, 0 },
        { DropType.Tops, 0 },
        { DropType.DivineGoodItem, 0 },
        { DropType.Shiny, 0 },
        { DropType.Shivine, 0 }
    };
}