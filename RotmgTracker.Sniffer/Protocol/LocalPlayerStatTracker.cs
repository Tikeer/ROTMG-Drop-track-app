using RotmgTrackerV1.Core.Enums;
using RotmgTrackerV1.Core.Services;

namespace RotmgTrackerV1.Sniffer.Protocol;

/// <summary>
/// Sklada pelny stan statystyk lokalnego gracza z pakietow Update (lista pelna) i NewTick (tylko zmiany),
/// i liczy z niego X/8.
///
/// Potwierdzone na capture (Knight 8/8): wartosc calkowita - boost = statystyka bazowa.
///  HP 965-165=800, MP 365-65=300, ATT 51-1=50, DEF 78-38=40, SPD 51-1=50, VIT 76-1=75, WIS 52-2=50, DEX 51-1=50.
///  Boost (stat 46..53) zawiera ekwipunek ORAZ bonus z exaltacji, wiec exaltacje nie psuja X/8.
///  Przy zmianie ekwipunku baza zostaje ta sama (np. HP 885-85=800).
/// </summary>
public sealed class LocalPlayerStatTracker
{
    // Wartosci calkowite
    private const int TotalHp = 0, TotalMp = 3, TotalAtt = 20, TotalDef = 21, TotalSpd = 22, TotalVit = 26, TotalWis = 27, TotalDex = 28;
    // Boosty (ekwipunek + exaltacja). 51=VIT, 53=DEX zgodnie z kolejnoscia protokolu - na capture oba maja 1, wiec nie rozrozniaja.
    private const int BoostHp = 46, BoostMp = 47, BoostAtt = 48, BoostDef = 49, BoostSpd = 50, BoostVit = 51, BoostWis = 52, BoostDex = 53;

    // Bonusy z exaltacji (stat 105..112). TYLKO PROWIZORYCZNE: pewne sa 106=DEF i 109=WIS,
    // reszta wynika z kolejnosci id i wymaga potwierdzenia (patrz ExaltBonusesText).
    private const int ExaltAtt = 105, ExaltDef = 106, ExaltSpd = 107, ExaltVit = 108, ExaltWis = 109, ExaltDex = 110, ExaltHp = 111, ExaltMp = 112;

    private readonly Dictionary<int, int> _values = new();

    public void Reset() => _values.Clear();

    public void Apply(IEnumerable<StatEntry> stats)
    {
        // W ramce lokalnego gracza stat 0 (MaxHP) wystepuje DWA razy: pierwszy to prawdziwe MaxHP (965),
        // ostatni "0:604" to prawdopodobnie inny stat zle odczytany (brakuje 156 miedzy 155 a 157).
        // Dlatego w obrebie jednej listy liczy sie pierwsze wystapienie, a pozniejsze listy (NewTick) nadpisuja.
        var seenInThisList = new HashSet<int>();
        foreach (var s in stats)
        {
            // Staty tekstowe (nick, guild...) maja StringValue; interesuja nas tylko liczby
            if (!string.IsNullOrEmpty(s.StringValue)) continue;
            if (!seenInThisList.Add(s.StatType)) continue;
            _values[s.StatType] = s.IntValue;
        }
    }

    /// <summary>Zwraca false, dopoki nie znamy wszystkich 16 statystyk (calkowite + boosty).</summary>
    public bool TryGetBaseStats(out StatSet baseStats)
    {
        baseStats = default;
        int[] needed =
        {
            TotalHp, TotalMp, TotalAtt, TotalDef, TotalSpd, TotalDex, TotalVit, TotalWis,
            BoostHp, BoostMp, BoostAtt, BoostDef, BoostSpd, BoostDex, BoostVit, BoostWis
        };
        foreach (var id in needed)
            if (!_values.ContainsKey(id)) return false;

        baseStats = new StatSet(
            _values[TotalHp]  - _values[BoostHp],
            _values[TotalMp]  - _values[BoostMp],
            _values[TotalAtt] - _values[BoostAtt],
            _values[TotalDef] - _values[BoostDef],
            _values[TotalSpd] - _values[BoostSpd],
            _values[TotalDex] - _values[BoostDex],
            _values[TotalVit] - _values[BoostVit],
            _values[TotalWis] - _values[BoostWis]);
        return true;
    }

    public bool TryComputeMaxed(RotmgClass playerClass, out int maxed, out StatSet baseStats)
    {
        maxed = 0;
        if (!TryGetBaseStats(out baseStats)) return false;
        if (!ClassStatTable.TryGetMax(playerClass, out _)) return false;
        maxed = ClassStatTable.CountMaxed(playerClass, baseStats);
        return true;
    }

    /// <summary>Tekst do logu diagnostycznego. Nazwy statow 105/107/108/110/111/112 sa niepotwierdzone.</summary>
    public string ExaltBonusesText()
    {
        int V(int id) => _values.TryGetValue(id, out var v) ? v : -1;
        return $"HP?={V(ExaltHp)} MP?={V(ExaltMp)} ATT?={V(ExaltAtt)} DEF={V(ExaltDef)} SPD?={V(ExaltSpd)} DEX?={V(ExaltDex)} VIT?={V(ExaltVit)} WIS={V(ExaltWis)}";
    }
}