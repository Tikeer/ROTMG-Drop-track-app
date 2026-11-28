using RotmgTrackerV1.Core.Models;

namespace RotmgTrackerV1.Core.Services;

public sealed record EarnedFameBonus(string Name, int FlatBonus, double PercentBonus);

public sealed record DeathFameEstimate(
    int BaseFame,
    int MaxingFlatBonus,
    double MaxingPercentBonus,
    int DungeonFlatBonus,
    double DungeonPercentBonus,
    int TotalPercentFame,
    int EstimatedDeathFame,
    int CompletedUniqueDungeons,
    List<EarnedFameBonus> EarnedCollections)
{
    public int TotalBonus => Math.Max(0, EstimatedDeathFame - BaseFame);
    public double TotalPercent => MaxingPercentBonus + DungeonPercentBonus;
}

/// <summary>
/// Estimates minimum Death Fame using RotMG's official client fame bonus table
/// (Maxed Stat bonuses + all 76 single-dungeon completion tiers + all 13 Dungeon Collection bonuses).
/// Because passive sniffer does not track enemy kills / tiles / teleports / quests, the result is a
/// lower-bound estimate ("Est. Death Fame") based on Base Fame + X/8 + tracked DungeonRuns.
/// </summary>
public static class DeathFameCalculator
{
    private readonly record struct DungeonTierValues(int C1, int C10, int C20, int C40, int C100);
    private sealed record DungeonCollectionRule(string Name, int Flat, double Percent, string[] RequiredDungeons);

    // Official RotMG client-fame single-dungeon milestones:
    // Scout (1), Explorer (10), Adventurer (20), Conqueror (40), Master (every 100, repeatable)
    private static readonly Dictionary<string, DungeonTierValues> SingleDungeons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Abyss of Demons"] = new(40, 80, 150, 300, 800),
        ["Advanced Kogbold Steamworks"] = new(400, 800, 1600, 3200, 8000),
        ["Ancient Ruins"] = new(40, 80, 150, 300, 800),
        ["Battle for the Nexus"] = new(150, 300, 600, 1200, 3000),
        ["Beachzone"] = new(5, 10, 20, 40, 100),
        ["Belladonna's Garden"] = new(150, 300, 600, 1200, 3000),
        ["Candyland Hunting Grounds"] = new(60, 120, 250, 500, 1200),
        ["Cave of A Thousand Treasures"] = new(60, 120, 250, 500, 1200),
        ["Cnidarian Reef"] = new(100, 200, 400, 800, 2000),
        ["Crystal Cavern"] = new(300, 600, 1200, 2400, 6000),
        ["Cultist Hideout"] = new(350, 700, 1500, 3000, 7000),
        ["Cursed Library"] = new(50, 100, 200, 400, 1000),
        ["Davy Jones' Locker"] = new(100, 200, 400, 800, 2000),
        ["Deadwater Docks"] = new(100, 200, 400, 800, 2000),
        ["Forax"] = new(125, 250, 500, 1000, 2500),
        ["Forbidden Jungle"] = new(4, 8, 15, 30, 80),
        ["Forest Maze"] = new(2, 5, 10, 20, 60),
        ["Fungal Cavern"] = new(250, 500, 1000, 2000, 5000),
        ["Haunted Cemetery"] = new(60, 120, 250, 500, 1200),
        ["Heroic Undead Lair"] = new(150, 300, 600, 1200, 3000),
        ["Hidden Interregnum"] = new(5, 10, 20, 40, 100),
        ["High Tech Terror"] = new(125, 250, 500, 1000, 2500),
        ["Ice Citadel"] = new(125, 250, 500, 1000, 2500),
        ["Ice Tomb"] = new(150, 300, 600, 1200, 3000),
        ["Infernal Abyss of Demons"] = new(150, 300, 600, 1200, 3000),
        ["Katalund"] = new(125, 250, 500, 1000, 2500),
        ["Kogbold Steamworks"] = new(400, 800, 1600, 3200, 8000),
        ["Lair of Draconis"] = new(175, 350, 700, 1400, 3500),
        ["Lair of Shaitan"] = new(100, 200, 400, 800, 2000),
        ["Lost Halls"] = new(300, 600, 1200, 2400, 6000),
        ["Mad Lab"] = new(50, 100, 200, 400, 1000),
        ["Magic Woods"] = new(35, 70, 140, 280, 700),
        ["Malogia"] = new(125, 250, 500, 1000, 2500),
        ["Manor of the Immortals"] = new(40, 80, 150, 300, 800),
        ["Moonlight Village"] = new(300, 600, 1200, 2400, 6000),
        ["Mountain Temple"] = new(150, 300, 600, 1200, 3000),
        ["Neo Forax"] = new(200, 400, 800, 1600, 4000),
        ["Neo Katalund"] = new(200, 400, 800, 1600, 4000),
        ["Neo Malogia"] = new(200, 400, 800, 1600, 4000),
        ["Neo Untaris"] = new(200, 400, 800, 1600, 4000),
        ["Ocean Trench"] = new(175, 350, 700, 1400, 3500),
        ["Oryx Pandemonium Decaract"] = new(150, 300, 600, 1200, 3000),
        ["Oryx's Castle"] = new(100, 200, 400, 800, 2000),
        ["Oryx's Chamber"] = new(125, 250, 500, 1000, 2500),
        ["Oryx's Sanctuary"] = new(500, 1000, 2000, 4000, 10000),
        ["Parasite Chambers"] = new(150, 300, 600, 1200, 3000),
        ["Pirate Cave"] = new(2, 5, 10, 20, 60),
        ["Plagued Nest"] = new(200, 400, 800, 1600, 4000),
        ["Puppet Master's Encore"] = new(100, 200, 400, 800, 2000),
        ["Puppet Master's Theatre"] = new(40, 80, 150, 300, 800),
        ["Queen Bunny Chamber"] = new(5, 10, 20, 40, 100),
        ["Rainbow Road"] = new(5, 10, 20, 40, 100),
        ["Santa Workshop"] = new(5, 10, 20, 40, 100),
        ["Secluded Thicket"] = new(125, 250, 500, 1000, 2500),
        ["Snake Pit"] = new(25, 50, 100, 200, 500),
        ["Spectral Penitentiary"] = new(300, 600, 1200, 2400, 6000),
        ["Spider Den"] = new(5, 10, 20, 40, 100),
        ["Sprite World"] = new(25, 50, 100, 200, 500),
        ["Stromwell's Rift I"] = new(150, 300, 600, 1200, 3000),
        ["Stromwell's Rift II"] = new(150, 300, 600, 1200, 3000),
        ["Stromwell's Rift III"] = new(150, 300, 600, 1200, 3000),
        ["Sulfurous Wetlands"] = new(100, 200, 400, 800, 2000),
        ["The Crawling Depths"] = new(100, 200, 400, 800, 2000),
        ["The Hive"] = new(4, 8, 15, 30, 80),
        ["The Machine"] = new(100, 200, 400, 800, 2000),
        ["The Nest"] = new(200, 400, 800, 1600, 4000),
        ["The Shatters"] = new(500, 1000, 2000, 4000, 10000),
        ["The Tavern"] = new(150, 300, 600, 1200, 3000),
        ["The Third Dimension"] = new(150, 300, 600, 1200, 3000),
        ["The Void"] = new(400, 800, 1600, 3200, 8000),
        ["Tomb of the Ancients"] = new(200, 400, 800, 1600, 4000),
        ["Toxic Sewers"] = new(40, 80, 150, 300, 800),
        ["Undead Lair"] = new(35, 70, 140, 280, 700),
        ["Untaris"] = new(125, 250, 500, 1000, 2500),
        ["Wine Cellar"] = new(200, 400, 800, 1600, 4000),
        ["Woodland Labyrinth"] = new(100, 200, 400, 800, 2000)
    };

    private static readonly Dictionary<string, string> DungeonAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Santa's Workshop"] = "Santa Workshop",
        ["Mad God Mayhem"] = "Oryx Pandemonium Decaract",
        ["Ice Cave"] = "Ice Citadel",
        ["Advanced Nest"] = "Plagued Nest",
        ["Advanced Steamworks"] = "Advanced Kogbold Steamworks"
    };

    private static readonly DungeonCollectionRule[] CollectionRules =
    [
        new("First Steps", 100, 2.5,
            ["Pirate Cave", "Forest Maze", "Forbidden Jungle", "Spider Den", "The Hive"]),
        new("King of the Mountains", 1000, 5.0,
            ["Sprite World", "Snake Pit", "Abyss of Demons", "Toxic Sewers", "Mad Lab", "Magic Woods",
             "Puppet Master's Theatre", "Haunted Cemetery", "Cursed Library", "Ancient Ruins",
             "Sulfurous Wetlands", "Spectral Penitentiary"]),
        new("Epic Battles", 2000, 7.5,
            ["Deadwater Docks", "Woodland Labyrinth", "The Crawling Depths", "The Nest", "Secluded Thicket"]),
        new("Far Out", 2000, 5.0,
            ["Malogia", "Untaris", "Forax", "Katalund"]),
        new("Farther Out", 3000, 7.5,
            ["Neo Malogia", "Neo Untaris", "Neo Forax", "Neo Katalund"]),
        new("Enemy of the Court", 3000, 7.5,
            ["Lair of Shaitan", "Puppet Master's Encore", "Cnidarian Reef", "Secluded Thicket", "High Tech Terror"]),
        new("Tunnel Rat", 3000, 7.5,
            ["Pirate Cave", "Forbidden Jungle", "Spider Den", "Snake Pit", "Undead Lair", "Abyss of Demons",
             "Manor of the Immortals", "Ocean Trench", "Tomb of the Ancients", "Oryx's Castle",
             "Oryx's Chamber", "Wine Cellar"]),
        new("Explosive Journey", 3000, 7.5,
            ["Davy Jones' Locker", "Mad Lab", "Candyland Hunting Grounds", "Haunted Cemetery",
             "Cave of A Thousand Treasures", "Lair of Draconis", "Deadwater Docks", "Woodland Labyrinth",
             "The Crawling Depths", "The Shatters", "Lair of Shaitan", "Puppet Master's Theatre", "Ice Citadel"]),
        new("Conqueror of the Realm", 4000, 10.0,
            ["Davy Jones' Locker", "Ice Citadel", "Lair of Draconis", "Mountain Temple", "The Third Dimension",
             "Ocean Trench", "Tomb of the Ancients", "The Shatters", "The Nest", "Fungal Cavern",
             "Crystal Cavern", "Lost Halls", "Kogbold Steamworks"]),
        new("Travel of the Decade", 5000, 10.0,
            ["Puppet Master's Encore", "The Hive", "Toxic Sewers", "Mountain Temple", "The Third Dimension",
             "The Nest", "Lost Halls", "Cultist Hideout", "The Void", "Cnidarian Reef", "Parasite Chambers",
             "Magic Woods", "Secluded Thicket", "Cursed Library", "Oryx's Sanctuary", "Ancient Ruins",
             "High Tech Terror", "Sulfurous Wetlands", "Spectral Penitentiary"]),
        new("Hero of the Nexus", 5000, 12.5,
            ["Pirate Cave", "Forest Maze", "Spider Den", "Snake Pit", "Forbidden Jungle", "The Hive",
             "Ancient Ruins", "Magic Woods", "Sprite World", "Candyland Hunting Grounds",
             "Cave of A Thousand Treasures", "Undead Lair", "Abyss of Demons", "Manor of the Immortals",
             "Puppet Master's Theatre", "Toxic Sewers", "Cursed Library", "Haunted Cemetery", "Mad Lab",
             "Parasite Chambers", "Davy Jones' Locker", "Mountain Temple", "The Third Dimension",
             "Lair of Draconis", "Deadwater Docks", "Woodland Labyrinth", "The Crawling Depths",
             "Ocean Trench", "Ice Citadel", "Tomb of the Ancients", "Fungal Cavern", "Crystal Cavern",
             "The Nest", "The Shatters", "Lost Halls", "Cultist Hideout", "The Void", "Sulfurous Wetlands",
             "Kogbold Steamworks", "Oryx's Castle", "Lair of Shaitan", "Puppet Master's Encore",
             "Cnidarian Reef", "Secluded Thicket", "High Tech Terror", "Oryx's Chamber", "Wine Cellar",
             "Oryx's Sanctuary", "Spectral Penitentiary"]),
        new("Season's Beatins", 5000, 12.5,
            ["Belladonna's Garden", "Ice Tomb", "Oryx Pandemonium Decaract", "Battle for the Nexus",
             "Santa Workshop", "The Machine", "Malogia", "Untaris", "Forax", "Katalund",
             "Rainbow Road", "Beachzone", "Neo Malogia", "Neo Untaris", "Neo Forax", "Neo Katalund"]),
        new("Realm of the Mad God", 10000, 25.0,
            ["Pirate Cave", "Forest Maze", "Spider Den", "Snake Pit", "Forbidden Jungle", "The Hive",
             "Ancient Ruins", "Magic Woods", "Sprite World", "Candyland Hunting Grounds",
             "Cave of A Thousand Treasures", "Undead Lair", "Abyss of Demons", "Manor of the Immortals",
             "Puppet Master's Theatre", "Toxic Sewers", "Cursed Library", "Haunted Cemetery", "Mad Lab",
             "Parasite Chambers", "Davy Jones' Locker", "Mountain Temple", "The Third Dimension",
             "Lair of Draconis", "Deadwater Docks", "Woodland Labyrinth", "The Crawling Depths",
             "Ocean Trench", "Ice Citadel", "Tomb of the Ancients", "Fungal Cavern", "Crystal Cavern",
             "The Nest", "The Shatters", "Lost Halls", "Cultist Hideout", "The Void", "Sulfurous Wetlands",
             "Kogbold Steamworks", "Oryx's Castle", "Lair of Shaitan", "Puppet Master's Encore",
             "Cnidarian Reef", "Secluded Thicket", "High Tech Terror", "Oryx's Chamber", "Wine Cellar",
             "Oryx's Sanctuary", "Belladonna's Garden", "Ice Tomb", "Oryx Pandemonium Decaract",
             "Battle for the Nexus", "Santa Workshop", "The Machine", "Malogia", "Untaris", "Forax",
             "Katalund", "Rainbow Road", "Beachzone", "Spectral Penitentiary"])
    ];

    public static DeathFameEstimate Estimate(CharacterSession character, IReadOnlyList<DungeonRunRecord> allRuns)
    {
        int baseFame = Math.Max(0, character.BaseFame);
        int maxed = Math.Clamp(character.MaxedStats, 0, 8);

        // Maxing bonuses: 6 rainbow stats (+100 / +2.5% each), Life & Mana (+200 / +5% each). 8/8 = +1000 / +25%.
        int rainbowMaxed = Math.Min(maxed, 6);
        int lifeManaMaxed = Math.Max(0, maxed - 6);
        int maxingFlat = rainbowMaxed * 100 + lifeManaMaxed * 200;
        double maxingPct = rainbowMaxed * 2.5 + lifeManaMaxed * 5.0;

        // Count completions per canonical dungeon name for this character
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var run in allRuns)
        {
            if (run.CharacterId != character.Id || string.IsNullOrWhiteSpace(run.DungeonName))
                continue;

            string canonical = NormalizeDungeonName(run.DungeonName);
            counts[canonical] = counts.GetValueOrDefault(canonical) + 1;
        }

        int dungeonFlat = 0;
        foreach (var (dName, c) in counts)
        {
            if (!SingleDungeons.TryGetValue(dName, out var tv))
                continue;

            if (c >= 1) dungeonFlat += tv.C1;
            if (c >= 10) dungeonFlat += tv.C10;
            if (c >= 20) dungeonFlat += tv.C20;
            if (c >= 40) dungeonFlat += tv.C40;
            if (c >= 100) dungeonFlat += (c / 100) * tv.C100;
        }

        double dungeonPct = 0;
        var earnedCollections = new List<EarnedFameBonus>();
        foreach (var rule in CollectionRules)
        {
            bool allCompleted = true;
            foreach (var req in rule.RequiredDungeons)
            {
                if (!counts.TryGetValue(req, out int c) || c <= 0)
                {
                    allCompleted = false;
                    break;
                }
            }

            if (allCompleted)
            {
                dungeonFlat += rule.Flat;
                dungeonPct += rule.Percent;
                earnedCollections.Add(new EarnedFameBonus(rule.Name, rule.Flat, rule.Percent));
            }
        }

        double totalPct = maxingPct + dungeonPct;
        int pctFame = (int)Math.Floor(baseFame * (totalPct / 100.0));
        int estTotal = baseFame + maxingFlat + dungeonFlat + pctFame;

        return new DeathFameEstimate(
            baseFame,
            maxingFlat,
            maxingPct,
            dungeonFlat,
            dungeonPct,
            pctFame,
            estTotal,
            counts.Count,
            earnedCollections);
    }

    private static string NormalizeDungeonName(string raw)
    {
        string clean = raw.Trim().Replace('’', '\'');
        return DungeonAliases.TryGetValue(clean, out var alias) ? alias : clean;
    }
}
