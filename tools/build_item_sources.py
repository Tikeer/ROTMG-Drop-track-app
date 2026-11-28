#!/usr/bin/env python3
"""
Builds RotmgTracker.Core/Data/item_sources.json mapping dungeon names -> collectible item names.

Sources combined (in priority order):
1. Curated RotMG Exalt dungeon drop tables (exact dungeon names matching GameDataService portals).
2. Local RotMG Exalt resources.assets XML (<Labels>REALMWHITE</Labels> -> "Realm of the Mad God",
   plus dungeon-specific TextureFile hints like specPenObjects8x8, moonlightVillageObjects8x8, etc.).
3. Optional locally saved RealmEye wiki HTML pages (--realmeye-html-dir <folder>).
4. At runtime, ItemSourceService also overlays actual drops from SQLite LootHistory (decision D5).

Usage:
    python3 tools/build_item_sources.py
    python3 tools/build_item_sources.py --realmeye-html-dir ./saved_realmeye_pages
"""

import argparse
import json
import os
import re
import sys
from collections import defaultdict
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_OUTPUT = REPO_ROOT / "RotmgTracker.Core" / "Data" / "item_sources.json"

# Curated core drop tables for RotMG Exalt dungeons (exact portal DungeonName keys).
# Shiny variants automatically inherit their base item's source in ItemSourceService.
CURATED_DUNGEON_ITEMS: dict[str, list[str]] = {
    "Oryx's Sanctuary": [
        "Divinity", "Oryx's Escutcheon", "Gladiator Guard", "Exalted God's Horn",
        "Lumiaire", "Ballistic Star", "Vesture of Duality", "Genesis Spell",
        "Enforcer", "Shattered Crag", "Centaur's Shielding", "Battalion Banner",
        "Avarice", "Turncoat Cape", "Monocle", "Gambler's Fate",
        "Superior", "Diplomatic Robe", "Cranium", "Chaotic Scripture",
        "Coronation", "Shield of Flowing Clarity",
    ],
    "The Shatters": [
        "Valour", "Polarity Poison", "Mantle of the Monarchy", "Warmonger",
        "Vestige of the Forgotten", "Sentinel's Sidearm", "Bracer of the Guardian",
        "Twilight Gemstone", "The Forgotten Crown", "Chrysalis of Eternity",
        "Dusky Catalyst", "Noble Mandolin", "Vestment of Abandoned Shadows",
        "Robe of the Mad Scientist", "Peacekeeper", "Arcana", "Royal Guard's Cuirass",
        "B.O.W.", "Vanguard's Visage", "Primal Arcana", "Mace of the Depths",
    ],
    "Moonlight Village": [
        "Taiko Drums", "Kagenohikari", "Makakoyumi", "Tome of the Tarnished Gods",
        "Sakura Wakizashi", "Amaterasu", "Kogbold Cower Shield", "Miko's Robe",
        "Kitsune's Tail", "Sake", "Scroll of the Unseen", "Maid's Dress",
        "Mikado's Robe", "Umi's Fan", "Concentrated Soul Fire",
    ],
    "Kogbold Steamworks": [
        "Laser Pointer", "Kogbold Multitool", "Kogbold Enhancement Core",
        "Overclocking Amulet", "Steam Pipe", "Brain Cube",
        "Kogbold Turret", "Chain Dagger", "Serrated Edge",
    ],
    "Advanced Kogbold Steamworks": [
        "Kogbold Enhancement Core", "Overclocking Amulet", "Laser Pointer", "Kogbold Multitool",
    ],
    "Lost Halls": [
        "Sword of the Colossus", "Marble Seal", "Breastplate of New Life", "Magical Lodestone",
        "Bow of the Void", "Quiver of the Shadows", "Armor of Nil", "Sourstone Ring",
    ],
    "The Void": [
        "Bow of the Void", "Quiver of the Shadows", "Armor of Nil", "Sourstone Ring",
        "Omnipotence Ring", "Vial of Pure Darkness",
    ],
    "Cultist Hideout": [
        "Staff of Unholy Sacrifice", "Skull of Corrupted Souls", "Ritual Robe",
        "Bloodshed Ring", "Vial of Pure Darkness",
    ],
    "Spectral Penitentiary": [
        "Vial of Soul Extract", "Soul's Guidance", "Soulcursed Scythe",
        "Specter's Cloak", "Penitent's Robe", "Warden's Seal", "Chains of the Damned",
        "Executioner's Helmet", "Spectral Ring of Horrors",
    ],
    "Fungal Cavern": [
        "Tome of the Mushroom Tribes", "Mace of the Celestial Forest", "Sporeous Scepter",
        "Cave Dweller Trap", "Fungal Breastplate", "Crystal Shield",
    ],
    "Crystal Cavern": [
        "Crystallized Fang's Venom", "Crystallized Mist", "Star of Enlightenment",
        "Crystal Kunai", "Fractured Gemstone Wakizashi", "Cloak of Refraction",
        "Echoes Prism", "Crystal Shield",
    ],
    "The Nest": [
        "Queen's Stinger", "Hivemaster Helm", "Beehemoth Armor",
        "Blue Beehemoth Quiver", "Red Beehemoth Quiver", "Yellow Beehemoth Quiver",
        "Honey Scepter", "Orb of Sweet Demise", "Nectar Crossfire",
        "Hivemind Circlet", "Bumblebee's Coat",
    ],
    "Plagued Nest": [
        "Queen's Stinger", "Hivemaster Helm", "Beehemoth Armor",
    ],
    "Sulfurous Wetlands": [
        "Bogwood Crook", "Mire-Mire", "Blighted Quiver", "Swamp Dweller's Hide",
        "Toadskin Armor", "Sulfur-Tipped Spear", "Toxic Toad",
    ],
    "High Tech Terror": [
        "F.E.R.A.L.", "H.A.L.O.", "S.T.A.F.F.", "C.O.R.E.",
        "H.E.L.M.", "G.E.N.E.S.I.S.", "Overcharged Jacket", "B.R.A.I.N.",
    ],
    "Secluded Thicket": [
        "Tenna's Bow", "Tideturner Trident", "Spell of the Darkest", "Robe of the Tlatoani",
        "Bramble Bow", "Ebony-Hilted Sword", "Tezcacoatl's Tail", "Scepter of Devastation",
    ],
    "Parasite Chambers": [
        "Recurrent Resurgence", "Scepter of Devastation", "Tome of Pain",
        "Garment of the Beast", "Hivemind Mace", "Spell of Unstable Magic",
        "Abomination's Wrath", "Ravenous Ring",
    ],
    "Tomb of the Ancients": [
        "Tome of Holy Protection", "Ring of the Pyramid", "Ring of the Nile",
        "Ring of the Sphinx", "Shendyt of Geb", "Book of Geb", "Wand of Geb", "Geb's Ring of Wisdom",
    ],
    "Ocean Trench": [
        "Coral Bow", "Coral Silk Armor", "Coral Venom Trap", "Coral Ring",
    ],
    "Lair of Draconis": [
        "Celestial Blade", "Midnight Star", "Fire Dragon Battle Armor",
        "Water Dragon Silk Robe", "Leaf Dragon Hide Armor", "Sullen Blade",
        "Helm of Draconic Dominance", "Indomptable", "Zaarvox's Heart",
    ],
    "Mountain Temple": [
        "Wand of the Fallen", "Kageboshi", "Orb of Aether",
        "Robe of the Eastern Winds", "Kazekiri", "Ryu's Blade",
        "Daichi's Robe", "Shaitan's Skull",
    ],
    "Lair of Shaitan": [
        "Skull of Endless Torment", "Waki of Crossing Fires", "Wakizashi of Crossing Fires",
    ],
    "The Third Dimension": [
        "Cubic Frame", "Prism of Dire Instability", "Seal of Cubic Conundra",
        "Cloak of Cubic Enigma", "Tesseract Goddess's Ring", "3D Cube",
    ],
    "Woodland Labyrinth": [
        "Leaf Bow", "Wakizashi of Eastern Winds", "Bergenia Bow",
        "Huntsman's Volley", "Crystalline Quiver", "Woodland Robe",
    ],
    "Deadwater Docks": [
        "Pirate King's Cutlass", "Corsair Ring", "Cursed Spire Spell",
        "Captain's Ring", "Swashbuckler's Armor", "Hand Cannon",
    ],
    "The Crawling Depths": [
        "Doku No Ken", "Arachna's Poison", "Venomous Fang", "Spider's Eye Ring",
    ],
    "Davy Jones' Locker": [
        "Spirit Dagger", "Ghostly Prism", "Spectral Cloth Armor", "Captain's Ring",
        "Etherite Dagger", "Mantle of Skuld", "Ghastly Drape", "Spectral Ring of Horrors",
    ],
    "Ice Cave": [
        "Staff of Esben", "Skullish Remains of Esben", "Esben's Shaman Attire",
        "Esben's Wedding Ring", "Frostbite", "Frimarra",
    ],
    "Cnidarian Reef": [
        "Cnidaria Rod", "Sea Slurp Home",
    ],
    "Puppet Master's Encore": [
        "Prism of Dancing Swords", "Thousand Shot", "Jester's Coat",
    ],
    "Haunted Cemetery": [
        "Plague Poison", "Resurrected Warrior's Armor", "Amulet of Dispersion",
        "Soulcursed Scythe", "Etherite Dagger",
    ],
    "Mad Lab": [
        "Conducting Wand", "Scepter of Fulmination", "Robe of the Mad Scientist",
        "Experimental Ring", "Horrific Creation's Ring",
    ],
    "Abyss of Demons": [
        "Demon Blade", "Dblade", "Obsidian Dagger", "Fire Sword",
        "Flamecaller", "Demon Lord's Skull", "Volcanic Sheath",
    ],
    "Toxic Sewers": [
        "Void Blade", "Murky Toxin", "Sludge Armor", "Blighted Quiver",
    ],
    "Cursed Library": [
        "Scholar's Seal", "Corruption Cutter", "Tome of Moral Support",
        "Necronomicon", "Robe of the Ancient Intellect",
    ],
    "Manor of the Immortals": [
        "St. Abraham's Wand", "Tome of Purification", "Chasuble of Holy Light",
        "Ring of Divine Faith", "Bone Dagger",
    ],
    "Puppet Master's Theatre": [
        "Harlequin Armor", "Prism of Dancing Swords", "Puppet Master's Skin",
    ],
    "Undead Lair": [
        "Doom Bow", "Bow of the Morning Star", "Edictum Praetoris", "Memento Mori",
        "Toga Picta", "Interregnum",
    ],
    "Candyland Hunting Grounds": [
        "Pixie-Enchanted Sword", "Seal of the Enchanted Forest", "Fairy Plate",
        "Ring of Pure Wishes", "Candy-Coated Armor", "Ring Pop",
    ],
    "Ancient Ruins": [
        "Amber Encased Mace", "Sandstone Seal", "Ancient Stone Sword",
        "Sandsman's Ring", "Desert Cloak",
    ],
    "Magic Woods": [
        "Staff of the Crystal Serpent", "Fountain Spirit's Staff", "Woodland Tunic",
        "Orb of the Magic Woods", "Fairy Ring",
    ],
    "Snake Pit": [
        "Bulwark", "Wand of the Bulwark", "Snake Skin Shield", "Snake Skin Armor",
        "Snake Eye Ring",
    ],
    "Sprite World": [
        "Staff of Extreme Prejudice", "Cloak of the Planewalker", "Sprite Wand",
        "Mace of the Sprite",
    ],
    "The Hive": [
        "Orb of Sweet Demise", "Honey Scepter", "Honeycomb",
    ],
    "Forbidden Jungle": [
        "Staff of the Crystal Serpent", "Cracked Crystal Skull", "Robe of the Tlatoani",
        "Crystal Bone Ring",
    ],
    "Spider Den": [
        "Poison Fang Dagger", "Spider's Eye Ring",
    ],
    "Pirate Cave": [
        "Corsair Ring", "Pirate's Rum",
    ],
    "Forest Maze": [
        "Bramble Bow", "Forest Tunic",
    ],
    "The Tavern": [
        "Tavern's Tankard", "Brawler's Brew", "Mug of Ale",
    ],
    "The Machine": [
        "Cheater Light Armor", "Cheater Heavy Armor", "Cheater Robe",
        "Mr. Mango", "Admin Sword", "Vanity",
    ],
    "Oryx's Castle": [
        "Ancient Stone Sword", "Shield of Ogmur",
    ],
    "Oryx's Chamber": [
        "Oryx's Greatsword", "Oryx's Armor", "Ring of Decades",
    ],
    "Wine Cellar": [
        "Acclaim", "Sword of Acclaim", "Cosmic Whole", "Foul", "Hydra",
        "Acropolis", "Gsorc", "Colossus Shield", "Elvish Quiver",
    ],
    "Realm of the Mad God": [
        "Dirk of Cronus", "Helm of the Juggernaut", "Shield of Ogmur",
        "Orb of Conflict", "Seal of Blasphemous Prayer", "Quiver of Thunder",
        "Ray Katana", "Tablet of the King's Avatar", "Cloak of Bloody Surprises",
        "Crystallised Fang's Venom", "Kageboshi", "Bloody Cloak",
        "Runes of Knowledge", "Evolutionary Record",
    ],
}

# TextureFile prefixes in resources.assets that map unambiguously to a dungeon
TEXTURE_FILE_TO_DUNGEON: dict[str, str] = {
    "lostHallsObjects8x8": "Lost Halls",
    "oryxSanctuaryObjects8x8": "Oryx's Sanctuary",
    "moonlightVillageObjects8x8": "Moonlight Village",
    "specPenObjects8x8": "Spectral Penitentiary",
    "epicHiveObjects8x8": "The Nest",
    "theMachineObjects8x8": "The Machine",
    "oceanTrenchObjects8x8": "Ocean Trench",
    "ancientRuinsObjects8x8": "Ancient Ruins",
    "abyssOfDemonsObjects8x8": "Abyss of Demons",
    "fungalCavernObjects8x8": "Fungal Cavern",
    "crystalCavernObjects8x8": "Crystal Cavern",
    "sulfurousWetlandsObjects8x8": "Sulfurous Wetlands",
    "secludedThicketObjects8x8": "Secluded Thicket",
    "parasiteChambersObjects8x8": "Parasite Chambers",
    "highTechTerrorObjects8x8": "High Tech Terror",
    "cursedLibraryObjects8x8": "Cursed Library",
    "magicWoodsObjects8x8": "Magic Woods",
    "mountainTempleObjects8x8": "Mountain Temple",
    "thirdDimensionObjects8x8": "The Third Dimension",
    "buffedBunnyObjects8x8": "Queen Bunny Chamber",
    "shattersObjects8x8": "The Shatters",
    "shattersNewObjects8x8": "The Shatters",
    "kogboldSteamworksObjects8x8": "Kogbold Steamworks",
    "steamworksObjects8x8": "Kogbold Steamworks",
}


def find_resources_assets() -> Path | None:
    candidates = [
        Path.home() / ".local/share/RealmOfTheMadGod/Production/RotMGExalt.app/Contents/Resources/Data/resources.assets",
        Path(os.environ.get("LOCALAPPDATA", "")) / "RealmOfTheMadGod/Production/RotMG Exalt_Data/resources.assets",
    ]
    for p in candidates:
        if p.is_file():
            return p
    return None


def find_item_catalog() -> Path | None:
    candidates = [
        Path.home() / "Library/Application Support/RotmgTracker/item_catalog_v8.json",
        Path(os.environ.get("APPDATA", "")) / "RotmgTracker/item_catalog_v8.json",
    ]
    for p in candidates:
        if p.is_file():
            return p
    return None


def extract_realmwhite_from_assets(assets_path: Path, catalog_by_id: dict[int, dict]) -> list[str]:
    raw = assets_path.read_bytes()
    found: set[str] = set()
    # Match <Object ... type="0x..."> ... REALMWHITE ... </Object>
    for m in re.finditer(rb'<Object[^>]*type="(0x[0-9a-fA-F]+|\d+)"[^>]*>(.*?)</Object>', raw, re.S):
        if b"REALMWHITE" not in m.group(2).upper():
            continue
        tid = int(m.group(1), 0)
        it = catalog_by_id.get(tid)
        if it and it.get("Tier", -1) <= 0:
            nm = it["Name"]
            if nm.lower().startswith("shiny "):
                nm = nm[6:].strip()
            found.add(nm)
    return sorted(found)


def parse_saved_realmeye_html(html_dir: Path, catalog_names: set[str]) -> dict[str, set[str]]:
    result: dict[str, set[str]] = defaultdict(set)
    if not html_dir.is_dir():
        return result
    for html_file in sorted(html_dir.glob("*.html")):
        txt = html_file.read_text(encoding="utf-8", errors="replace")
        m_h1 = re.search(r"<h1[^>]*>(.*?)</h1>", txt, re.S | re.I)
        if not m_h1:
            continue
        dungeon_name = re.sub(r"<[^>]+>", "", m_h1.group(1)).strip()
        if not dungeon_name or dungeon_name == "Just a moment...":
            continue
        for item_name in catalog_names:
            if len(item_name) >= 5 and item_name in txt:
                result[dungeon_name].add(item_name)
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate item_sources.json for RotMG Tracker.")
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--realmeye-html-dir", type=Path, default=None)
    args = parser.parse_args()

    dungeon_map: dict[str, set[str]] = defaultdict(set)
    for d, items in CURATED_DUNGEON_ITEMS.items():
        for nm in items:
            dungeon_map[d].add(nm)

    cat_path = find_item_catalog()
    catalog_by_id: dict[int, dict] = {}
    catalog_names: set[str] = set()
    if cat_path:
        cat_items = json.loads(cat_path.read_text(encoding="utf-8"))
        for it in cat_items:
            catalog_by_id[it["ItemId"]] = it
            if it.get("Tier", -1) <= 0:
                nm = it["Name"]
                base = nm[6:].strip() if nm.lower().startswith("shiny ") else nm
                catalog_names.add(base)
                tex = it.get("TextureFile", "")
                if tex in TEXTURE_FILE_TO_DUNGEON:
                    dungeon_map[TEXTURE_FILE_TO_DUNGEON[tex]].add(base)

    assets_path = find_resources_assets()
    if assets_path and catalog_by_id:
        rw_items = extract_realmwhite_from_assets(assets_path, catalog_by_id)
        for nm in rw_items:
            dungeon_map["Realm of the Mad God"].add(nm)
        print(f"Extracted {len(rw_items)} REALMWHITE items from resources.assets")

    if args.realmeye_html_dir and catalog_names:
        rm_map = parse_saved_realmeye_html(args.realmeye_html_dir, catalog_names)
        for d, items in rm_map.items():
            dungeon_map[d].update(items)
        print(f"Merged {sum(len(v) for v in rm_map.values())} entries from saved RealmEye HTML")

    # Normalize output sorted cleanly
    dungeons_out = {
        d: sorted(items, key=str.lower)
        for d, items in sorted(dungeon_map.items(), key=lambda kv: kv[0].lower())
        if items
    }

    payload = {
        "_info": "Mapping of DungeonName -> collectible base item names. Shiny variants automatically inherit their base item source; runtime LootHistory drops overlay additional mappings.",
        "version": 1,
        "dungeons": dungeons_out,
    }

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    total_pairs = sum(len(v) for v in dungeons_out.values())
    unique_items = len({it.lower() for v in dungeons_out.values() for it in v})
    print(f"Wrote {args.output} ({len(dungeons_out)} dungeons, {unique_items} unique base items, {total_pairs} mappings)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
