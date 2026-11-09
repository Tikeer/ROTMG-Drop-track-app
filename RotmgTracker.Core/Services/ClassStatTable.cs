using RotmgTrackerV1.Core.Enums;

namespace RotmgTrackerV1.Core.Services;

/// <summary>Osiem statystyk postaci w kolejnosci uzywanej przez RealmEye (X/8).</summary>
public readonly record struct StatSet(int Hp, int Mp, int Att, int Def, int Spd, int Dex, int Vit, int Wis)
{
    public static StatSet operator -(StatSet a, StatSet b) =>
        new(a.Hp - b.Hp, a.Mp - b.Mp, a.Att - b.Att, a.Def - b.Def, a.Spd - b.Spd, a.Dex - b.Dex, a.Vit - b.Vit, a.Wis - b.Wis);
}

/// <summary>
/// Maksymalne statystyki bazowe klas (kolumny *_max z class_stats.csv wyciagnietego z resources.assets).
/// Wartosc "max" z XML, bez bonusow z ekwipunku i exaltacji.
/// Zrodlo prawdy ma byc docelowo GameDataService (skan plikow gry); ta tabela jest wartoscia domyslna.
/// </summary>
public static class ClassStatTable
{
    private static readonly Dictionary<RotmgClass, StatSet> Maxes = new()
    {
        //                          HP   MP  ATT DEF SPD DEX VIT WIS
        { RotmgClass.Rogue,        new(750, 300, 55, 25, 65, 75, 40, 50) },
        { RotmgClass.Archer,       new(750, 300, 75, 25, 55, 50, 40, 50) },
        { RotmgClass.Wizard,       new(700, 400, 60, 25, 50, 75, 40, 60) },
        { RotmgClass.Priest,       new(700, 400, 65, 25, 55, 60, 40, 75) },
        { RotmgClass.Warrior,      new(800, 300, 75, 25, 50, 50, 75, 50) },
        { RotmgClass.Knight,       new(800, 300, 50, 40, 50, 50, 75, 50) },
        { RotmgClass.Paladin,      new(800, 300, 55, 30, 55, 55, 60, 75) },
        { RotmgClass.Assassin,     new(750, 350, 65, 25, 65, 75, 40, 60) },
        { RotmgClass.Necromancer,  new(700, 400, 75, 25, 50, 60, 40, 75) },
        { RotmgClass.Huntress,     new(750, 350, 65, 25, 50, 60, 40, 60) },
        { RotmgClass.Mystic,       new(700, 400, 65, 25, 60, 65, 40, 75) },
        { RotmgClass.Trickster,    new(750, 300, 65, 25, 75, 75, 40, 60) },
        { RotmgClass.Sorcerer,     new(700, 400, 70, 25, 60, 60, 75, 60) },
        { RotmgClass.Ninja,        new(800, 300, 70, 25, 60, 70, 60, 70) },
        { RotmgClass.Samurai,      new(800, 300, 75, 30, 55, 55, 60, 60) },
        { RotmgClass.Bard,         new(750, 400, 55, 25, 55, 70, 45, 75) },
        { RotmgClass.Summoner,     new(700, 400, 60, 25, 60, 75, 40, 75) },
        { RotmgClass.Kensei,       new(800, 300, 65, 25, 60, 65, 60, 50) },
        { RotmgClass.Druid,        new(750, 350, 60, 25, 60, 65, 40, 75) },
    };

    public static bool TryGetMax(RotmgClass playerClass, out StatSet max) =>
        Maxes.TryGetValue(playerClass, out max);

    /// <summary>
    /// Liczy X/8: ile statystyk bazowych (wartosc - boost) osiaga maksimum klasy.
    /// Porownanie ">=" jest celowe: nadwyzka (np. po exaltacji) nadal liczy sie jako maks.
    /// </summary>
    public static int CountMaxed(RotmgClass playerClass, StatSet baseStats)
    {
        if (!TryGetMax(playerClass, out var m)) return 0;

        int n = 0;
        if (baseStats.Hp  >= m.Hp)  n++;
        if (baseStats.Mp  >= m.Mp)  n++;
        if (baseStats.Att >= m.Att) n++;
        if (baseStats.Def >= m.Def) n++;
        if (baseStats.Spd >= m.Spd) n++;
        if (baseStats.Dex >= m.Dex) n++;
        if (baseStats.Vit >= m.Vit) n++;
        if (baseStats.Wis >= m.Wis) n++;
        return n;
    }
}