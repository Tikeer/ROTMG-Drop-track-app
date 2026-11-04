using RotmgTrackerV1.Core.Enums;

namespace RotmgTrackerV1.Core.Models;

public class CharacterSession
{
    // Unikalny klucz główny w bazie (character_uuid)
    public Guid Id { get; set; } = Guid.NewGuid();

    // Wewnętrzne ID postaci z pakietu LOAD w RotMG (rozróżnia np. 3 Archerów na koncie)
    public int RotmgCharId { get; set; }

    // Numer slotu w obrębie danej klasy (np. Archer #1, Archer #2)
    public int ClassSlotNumber { get; set; } = 1;

    // Własny alias gracza (np. "PPE Archer", "O3 Rusher")
    public string Name { get; set; } = "Main Character";
    public RotmgClass Class { get; set; } = RotmgClass.Wizard;
    public int SkinId { get; set; }

    public bool IsSeasonal { get; set; } = true;
    public string SeasonTag { get; set; } = "S1";
    public bool IsAlive { get; set; } = true;

    // Statystyki w formacie RealmEye (X/8 oraz Fame)
    public int MaxedStats { get; set; } = 0; // 0..8 (np. 8/8)
    public int BaseFame { get; set; } = 0;
    public int TotalFame { get; set; } = 0;  // Death Fame / Total Fame z bonusami

    // 4 sloty założonego ekwipunku: [Weapon, Ability, Armor, Ring]
    public int[] EquipmentIds { get; set; } = new int[4];

    // Metadane czasu życia i śmierci (Graveyard)
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? DiedAt { get; set; }
    public string? DeathServer { get; set; }
    public string? DeathDungeon { get; set; }
    public string? KilledBy { get; set; }

    // Słownik synchronizowany dynamicznie z tabelą LootHistory
    public Dictionary<DropType, int> Drops { get; set; } = new();

    public int TotalDrops => Drops.Values.Sum();

    // Sformatowana nazwa do wyświetlenia na wystawie (np. "Archer #1 (PPE) - 8/8")
    public string FormattedTitle =>
        string.IsNullOrWhiteSpace(Name) || Name == Class.ToString()
            ? $"{Class} #{ClassSlotNumber} ({MaxedStats}/8)"
            : $"{Class} #{ClassSlotNumber} - {Name} ({MaxedStats}/8)";
}