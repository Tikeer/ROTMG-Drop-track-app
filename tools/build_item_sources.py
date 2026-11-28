#!/usr/bin/env python3
"""
Builds RotmgTracker.Core/Data/item_sources.json (v3) directly from DECA's official ORG_*, REALMWHITE,
and ST setName metadata inside RotMG Exalt's resources.assets XML, grouped by Dungeon -> Boss -> Items
plus Dungeon -> ST Items.

Usage:
    python3 tools/build_item_sources.py
"""

import argparse
import json
import os
import re
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_OUTPUT = REPO_ROOT / "RotmgTracker.Core" / "Data" / "item_sources.json"

# Official DECA XML <Labels> ORG_* tags -> exact PortalMetadata.DungeonName in GameDataService
ORG_LABEL_TO_DUNGEON: dict[str, str] = {
    "ORG_OSANC": "Oryx's Sanctuary",
    "ORG_OSANC_S": "Oryx's Sanctuary",
    "ORG_SHTT": "The Shatters",
    "ORG_SHTT_S": "The Shatters",
    "ORG_MOONLIGHT": "Moonlight Village",
    "ORG_MOONLIGHT_CORE": "Moonlight Village",
    "ORG_UMI": "Moonlight Village",
    "ORG_KOGBOLD": "Kogbold Steamworks",
    "ORG_KOGBOLD_S": "Kogbold Steamworks",
    "ORG_KOGBOLD_CORE": "Kogbold Steamworks",
    "ORG_KOGBOLD_CORE_SHINY": "Kogbold Steamworks",
    "ORG_SPECTRAL": "Spectral Penitentiary",
    "ORG_SPECTRAL_CORE": "Spectral Penitentiary",
    "ORG_SPECTRAL_CORE_SHINY": "Spectral Penitentiary",
    "ORG_LH": "Lost Halls",
    "ORG_VOID": "The Void",
    "ORG_CULT": "Cultist Hideout",
    "ORG_FUNGAL": "Fungal Cavern",
    "ORG_CRYSTAL": "Crystal Cavern",
    "ORG_CRYSTAL_S": "Crystal Cavern",
    "ORG_NEST": "The Nest",
    "ORG_WETLANDS": "Sulfurous Wetlands",
    "ORG_HTT": "High Tech Terror",
    "ORG_THICKET": "Secluded Thicket",
    "ORG_TOMB": "Tomb of the Ancients",
    "ORG_LOD": "Lair of Draconis",
    "ORG_MTEMPLE": "Mountain Temple",
    "ORG_SHAITAN": "Lair of Shaitan",
    "ORG_3D": "The Third Dimension",
    "ORG_TAVERN": "The Tavern",
    "ORG_PARASITE": "Parasite Chambers",
    "ORG_WLAB": "Woodland Labyrinth",
    "ORG_DDOCKS": "Deadwater Docks",
    "ORG_CDEPTHS": "The Crawling Depths",
    "ORG_ENCORE": "Puppet Master's Encore",
    "ORG_REEF": "Cnidarian Reef",
    "ORG_DAVY": "Davy Jones' Locker",
    "ORG_OTRENCH": "Ocean Trench",
    "ORG_ICECAVE": "Ice Cave",
    "ORG_CEM": "Haunted Cemetery",
    "ORG_ABYSS": "Abyss of Demons",
    "ORG_SEWER": "Toxic Sewers",
    "ORG_LAB": "Mad Lab",
    "ORG_LIBRARY": "Cursed Library",
    "ORG_MANOR": "Manor of the Immortals",
    "ORG_PUPPET": "Puppet Master's Theatre",
    "ORG_CLAND": "Candyland Hunting Grounds",
    "ORG_UDL": "Undead Lair",
    "ORG_RUINS": "Ancient Ruins",
    "ORG_MWOODS": "Magic Woods",
    "ORG_SPIT": "Snake Pit",
    "ORG_SPRITE": "Sprite World",
    "ORG_HIVE": "The Hive",
    "ORG_JUNGLE": "Forbidden Jungle",
    "ORG_SPIDER": "Spider Den",
    "ORG_FMAZE": "Forest Maze",
    "ORG_PCAVE": "Pirate Cave",
    "ORG_CASTLE": "Oryx's Castle",
    "REALMWHITE": "Realm of the Mad God",
    "ORG_SETPIECE": "Realm of the Mad God",
}

# Exact enemy DisplayName / IdName matching GameDataService.ResolveEnemyByName for sprite lookup
DEFAULT_DUNGEON_BOSS: dict[str, str] = {
    "Abyss of Demons": "Malphas, Gilded Forgemaster",
    "Ancient Ruins": "Sandstone Titan",
    "Candyland Hunting Grounds": "Swoll Fairy",
    "Cnidarian Reef": "Royal Cnidarian",
    "Crystal Cavern": "Crystal Entity",
    "Cultist Hideout": "Malus",
    "Cursed Library": "Avalon the Archivist",
    "Davy Jones' Locker": "Davy Jones",
    "Deadwater Docks": "Jon Bilgewater the Pirate King",
    "Forbidden Jungle": "Mixcoatl the Masked God",
    "Forest Maze": "Mama Megamoth",
    "Fungal Cavern": "Crystal Worm Mother",
    "Haunted Cemetery": "Ghost of Skuld",
    "High Tech Terror": "F.E.R.A.L.",
    "Ice Cave": "Esben the Neurotic",
    "Kogbold Steamworks": "Factory Control Core",
    "Lair of Draconis": "Ivory Wyvern",
    "Lair of Shaitan": "Shaitan the Advisor",
    "Lost Halls": "Marble Colossus",
    "Mad Lab": "Dr Terrible",
    "Magic Woods": "Fountain Spirit",
    "Manor of the Immortals": "Lord Ruthven",
    "Moonlight Village": "Dancer Miko",
    "Mountain Temple": "Daichi the Fallen",
    "Ocean Trench": "Thessal the Mermaid Goddess",
    "Oryx's Castle": "Oryx the Mad God",
    "Oryx's Sanctuary": "Oryx the Mad God 3",
    "Parasite Chambers": "Nightmare Colony",
    "Pirate Cave": "Dreadstump the Pirate King",
    "Puppet Master's Encore": "The Puppet Master",
    "Puppet Master's Theatre": "The Puppet Master",
    "Realm of the Mad God": "Realm Encounters & Biomes",
    "Secluded Thicket": "Xolotl the Lightning God",
    "Snake Pit": "Stheno the Snake Queen",
    "Spectral Penitentiary": "Soulwarden Murcian",
    "Spider Den": "Arachna the Spider Queen",
    "Sprite World": "Limon the Sprite Goddess",
    "Sulfurous Wetlands": "Heart of the Wetlands",
    "The Crawling Depths": "Son of Arachna",
    "The Hive": "Queen Bee",
    "The Nest": "Killer Bee Queen",
    "The Shatters": "The Forgotten King",
    "The Tavern": "Bradley the Barkeep",
    "The Third Dimension": "Tesseract Goddess",
    "The Void": "Void Entity",
    "Tomb of the Ancients": "Geb",
    "Toxic Sewers": "Gulpord the Slime God",
    "Undead Lair": "Septavius, Master of the Undead",
    "Woodland Labyrinth": "Murderous Megamoth",
}

# Canonical boss ordering per multi-boss dungeon
BOSS_ORDER: dict[str, list[str]] = {
    "Oryx's Sanctuary": [
        "Archbishop Leucoryx",
        "Chancellor Dammah",
        "Chief Beisa",
        "Treasurer Gemsbok",
        "Oryx the Mad God 3",
    ],
    "The Shatters": [
        "The Bridge Sentinel",
        "Twilight Archmage",
        "The Forgotten King",
    ],
    "Moonlight Village": [
        "Dancer Miko",
        "Sage Genji",
        "Drummer Kaguya",
        "Kitsune Umi",
    ],
    "Spectral Penitentiary": [
        "Doctor Lobotomik",
        "Overseer Oculon",
        "Groundskeeper Gretch",
        "Griefkeeper Zole",
        "Soulwarden Murcian",
    ],
    "Lair of Draconis": [
        "Pyyr the Crimson Dragon",
        "Nikao the Azure Dragon",
        "Limoz the Viridian Dragon",
        "Feargus the Obsidian Dragon",
        "Ivory Wyvern",
    ],
    "Tomb of the Ancients": ["Bes", "Nut", "Geb"],
    "Mad Lab": ["Dr Terrible", "Horrific Creation"],
    "Oryx's Castle": ["Stone Guardian", "Janus the Doorwarden", "Oryx the Mad God"],
    "Deadwater Docks": ["Calamity Crab", "Jon Bilgewater the Pirate King"],
    "Realm of the Mad God": [
        "Lord of the Lost Lands",
        "Hermit God",
        "Grand Sphinx",
        "Cube God",
        "Skull Shrine",
        "Pentaract Tower",
        "Avatar of the Forgotten King",
        "Rock Dragon",
        "Ghost Ship",
        "Crystal Prisoner",
        "Realm Encounters & Biomes",
    ],
}

# Exact (Dungeon, ItemBaseName) -> BossName overrides for multi-boss dungeons & Realm event bosses
ITEM_TO_BOSS: dict[tuple[str, str], str] = {
    # Oryx's Sanctuary
    ("Oryx's Sanctuary", "Lumiaire"): "Archbishop Leucoryx",
    ("Oryx's Sanctuary", "Chaotic Scripture"): "Archbishop Leucoryx",
    ("Oryx's Sanctuary", "Vesture of Duality"): "Archbishop Leucoryx",
    ("Oryx's Sanctuary", "Divine Coronation"): "Archbishop Leucoryx",
    ("Oryx's Sanctuary", "Superior"): "Chancellor Dammah",
    ("Oryx's Sanctuary", "Genesis Spell"): "Chancellor Dammah",
    ("Oryx's Sanctuary", "Diplomatic Robe"): "Chancellor Dammah",
    ("Oryx's Sanctuary", "Chancellor's Cranium"): "Chancellor Dammah",
    ("Oryx's Sanctuary", "Enforcer"): "Chief Beisa",
    ("Oryx's Sanctuary", "Battalion Banner"): "Chief Beisa",
    ("Oryx's Sanctuary", "Centaur's Shielding"): "Chief Beisa",
    ("Oryx's Sanctuary", "Excubicur"): "Chief Beisa",
    ("Oryx's Sanctuary", "Avarice"): "Treasurer Gemsbok",
    ("Oryx's Sanctuary", "Gambler's Fate"): "Treasurer Gemsbok",
    ("Oryx's Sanctuary", "Turncoat Cape"): "Treasurer Gemsbok",
    ("Oryx's Sanctuary", "Collector's Monocle"): "Treasurer Gemsbok",
    ("Oryx's Sanctuary", "Divinity"): "Oryx the Mad God 3",
    ("Oryx's Sanctuary", "Oryx's Escutcheon"): "Oryx the Mad God 3",
    ("Oryx's Sanctuary", "Gladiator Guard"): "Oryx the Mad God 3",
    ("Oryx's Sanctuary", "Exalted God's Horn"): "Oryx the Mad God 3",
    ("Oryx's Sanctuary", "Ballistic Star"): "Oryx the Mad God 3",
    ("Oryx's Sanctuary", "Eternal Echo"): "Oryx the Mad God 3",
    # The Shatters
    ("The Shatters", "Sentinel's Sidearm"): "The Bridge Sentinel",
    ("The Shatters", "Vanguard's Visage"): "The Bridge Sentinel",
    ("The Shatters", "Royal Guard's Cuirass"): "The Bridge Sentinel",
    ("The Shatters", "Bracer of the Guardian"): "The Bridge Sentinel",
    ("The Shatters", "Mantle of the Monarchy"): "The Bridge Sentinel",
    ("The Shatters", "Valor"): "Twilight Archmage",
    ("The Shatters", "Primal Arcana"): "Twilight Archmage",
    ("The Shatters", "Vest of Abandoned Shadows"): "Twilight Archmage",
    ("The Shatters", "The Twilight Gemstone"): "Twilight Archmage",
    ("The Shatters", "Polarity Poison"): "Twilight Archmage",
    ("The Shatters", "Dusky Catalyst"): "Twilight Archmage",
    ("The Shatters", "Warmonger"): "The Forgotten King",
    ("The Shatters", "Peacekeeper"): "The Forgotten King",
    ("The Shatters", "Noble Mandolin"): "The Forgotten King",
    ("The Shatters", "Crest of the Rising Sun"): "The Forgotten King",
    ("The Shatters", "Sovereign Cinder-Blade"): "The Forgotten King",
    ("The Shatters", "The Forgotten Crown"): "The Forgotten King",
    ("The Shatters", "The Golden Forgotten Crown"): "The Forgotten King",
    ("The Shatters", "Ice Crown"): "The Forgotten King",
    # Moonlight Village
    ("Moonlight Village", "Taiko Drums"): "Dancer Miko",
    ("Moonlight Village", "Flowering Kimono"): "Dancer Miko",
    ("Moonlight Village", "Mischief"): "Dancer Miko",
    ("Moonlight Village", "Makakoyumi"): "Sage Genji",
    ("Moonlight Village", "+Makakoyumi"): "Sage Genji",
    ("Moonlight Village", "Sage's Wakibiki"): "Sage Genji",
    ("Moonlight Village", "Vigor"): "Sage Genji",
    ("Moonlight Village", "Tezutsu Hanabi"): "Drummer Kaguya",
    ("Moonlight Village", "+Tezutsu Hanabi"): "Drummer Kaguya",
    ("Moonlight Village", "Ethereal Happi"): "Drummer Kaguya",
    ("Moonlight Village", "Fortitude"): "Drummer Kaguya",
    ("Moonlight Village", "Kagenohikari"): "Kitsune Umi",
    ("Moonlight Village", "Elegant Parasol"): "Kitsune Umi",
    ("Moonlight Village", "Rain Maker"): "Kitsune Umi",
    # Spectral Penitentiary
    ("Spectral Penitentiary", "Sinister Syringes"): "Doctor Lobotomik",
    ("Spectral Penitentiary", "Cackling Straitjacket"): "Doctor Lobotomik",
    ("Spectral Penitentiary", "Blood Ritual Tools"): "Doctor Lobotomik",
    ("Spectral Penitentiary", "Command Cornea"): "Overseer Oculon",
    ("Spectral Penitentiary", "Ocular Entrapment"): "Overseer Oculon",
    ("Spectral Penitentiary", "Overwhelming Axehead"): "Groundskeeper Gretch",
    ("Spectral Penitentiary", "Wretched Rags"): "Groundskeeper Gretch",
    ("Spectral Penitentiary", "Tools of the Tarnished"): "Groundskeeper Gretch",
    ("Spectral Penitentiary", "Motivational Megaphone"): "Griefkeeper Zole",
    ("Spectral Penitentiary", "Pandemic Poison"): "Griefkeeper Zole",
    ("Spectral Penitentiary", "Damnation"): "Soulwarden Murcian",
    ("Spectral Penitentiary", "Deliverance"): "Soulwarden Murcian",
    ("Spectral Penitentiary", "Boundless Vessel"): "Soulwarden Murcian",
    ("Spectral Penitentiary", "Amulet of Restoration"): "Soulwarden Murcian",
    # Lair of Draconis
    ("Lair of Draconis", "Fire Dragon Battle Armor"): "Pyyr the Crimson Dragon",
    ("Lair of Draconis", "Water Dragon Silk Robe"): "Nikao the Azure Dragon",
    ("Lair of Draconis", "Leaf Dragon Hide Armor"): "Limoz the Viridian Dragon",
    ("Lair of Draconis", "Midnight Star"): "Feargus the Obsidian Dragon",
    ("Lair of Draconis", "Arashi"): "Feargus the Obsidian Dragon",
    ("Lair of Draconis", "Celestial Blade"): "Ivory Wyvern",
    ("Lair of Draconis", "Snowflake Blade"): "Ivory Wyvern",
    ("Lair of Draconis", "Draconic Insignia"): "Ivory Wyvern",
    ("Lair of Draconis", "Forbidden Artifact"): "Ivory Wyvern",
    ("Lair of Draconis", "Vision of Draconis"): "Ivory Wyvern",
    # Tomb of the Ancients
    ("Tomb of the Ancients", "Ring of the Pyramid"): "Bes",
    ("Tomb of the Ancients", "Tome of Holy Protection"): "Bes",
    ("Tomb of the Ancients", "Tome of Frigid Protection"): "Bes",
    ("Tomb of the Ancients", "Ring of the Nile"): "Nut",
    ("Tomb of the Ancients", "Frimarra"): "Nut",
    ("Tomb of the Ancients", "Ring of the Sphinx"): "Geb",
    ("Tomb of the Ancients", "Pharaoh's Requiem"): "Geb",
    ("Tomb of the Ancients", "Ring of the Northern Light"): "Geb",
    # Mad Lab
    ("Mad Lab", "Conducting Wand"): "Dr Terrible",
    ("Mad Lab", "Eggstatic Conductor"): "Dr Terrible",
    ("Mad Lab", "Robe of the Mad Scientist"): "Dr Terrible",
    ("Mad Lab", "Experimental Ring"): "Dr Terrible",
    ("Mad Lab", "Scepter of Fulmination"): "Horrific Creation",
    ("Mad Lab", "Storm Caller"): "Horrific Creation",
    # Oryx's Castle
    ("Oryx's Castle", "Ancient Stone Sword"): "Stone Guardian",
    ("Oryx's Castle", "Spiteful Scutum"): "Janus the Doorwarden",
    ("Oryx's Castle", "Esoteric Buckler"): "Janus the Doorwarden",
    ("Oryx's Castle", "Cloak of the Mad God"): "Oryx the Mad God",
    ("Oryx's Castle", "Tome of Holy Furor"): "Oryx the Mad God",
    ("Oryx's Castle", "Tome of the Dragon"): "Oryx the Mad God",
    # Deadwater Docks
    ("Deadwater Docks", "Wavecrest Concertina"): "Calamity Crab",
    ("Deadwater Docks", "Ghost Ship's Bell"): "Calamity Crab",
    # Realm of the Mad God classic Event Bosses
    ("Realm of the Mad God", "Shield of Ogmur"): "Lord of the Lost Lands",
    ("Realm of the Mad God", "Shield of Jawgmur"): "Lord of the Lost Lands",
    ("Realm of the Mad God", "Helm of the Juggernaut"): "Hermit God",
    ("Realm of the Mad God", "Helm of the Juggernaut - Chaos"): "Hermit God",
    ("Realm of the Mad God", "Helm of the Juggernaut - Jade"): "Hermit God",
    ("Realm of the Mad God", "Helm of the Juggernaut - Midas"): "Hermit God",
    ("Realm of the Mad God", "Helm of the Jack-o'-naut"): "Hermit God",
    ("Realm of the Mad God", "Dirk of Cronus"): "Cube God",
    ("Realm of the Mad God", "Phantasm Dirk"): "Cube God",
    ("Realm of the Mad God", "Orb of Conflict"): "Skull Shrine",
    ("Realm of the Mad God", "Orb of Conflict - Chaos"): "Skull Shrine",
    ("Realm of the Mad God", "Orb of Terror"): "Skull Shrine",
    ("Realm of the Mad God", "Seal of Blasphemous Prayer"): "Pentaract Tower",
    ("Realm of the Mad God", "Tablet of the King's Avatar"): "Avatar of the Forgotten King",
    ("Realm of the Mad God", "Ray Katana"): "Rock Dragon",
    ("Realm of the Mad God", "Trap of the Vile Spirit"): "Ghost Ship",
    ("Realm of the Mad God", "Trap of the Blood Spirit"): "Ghost Ship",
    ("Realm of the Mad God", "Crystal Sword"): "Crystal Prisoner",
    ("Realm of the Mad God", "Crystal Wand"): "Crystal Prisoner",
}

# Official ST Set name (XML setName attribute) -> DungeonName where the set drops
ST_SET_TO_DUNGEON: dict[str, str] = {
    "Magmatic Mystic Set": "Abyss of Demons",
    "Earthen Bulwark Summoner Set": "Ancient Ruins",
    "Swoll Paladin Set": "Candyland Hunting Grounds",
    "Legacy Swoll Paladin Set": "Candyland Hunting Grounds",
    "Slurp Scion Wizard Set": "Cnidarian Reef",
    "Crystal Kunoichi Set": "Crystal Cavern",
    "Hollow King Necromancer Set": "Cultist Hideout",
    "Archexorcist Priest Set": "Cursed Library",
    "Spectral Sentry Rogue Set": "Davy Jones' Locker",
    "Pirate King Warrior Set": "Deadwater Docks",
    "Shaman Necromancer Set": "Forbidden Jungle",
    "Skuld 2 The ReGhostening Set": "Haunted Cemetery",
    "Legacy Skuld 2 The ReGhostening Set": "Haunted Cemetery",
    "Mechanical Raptor Druid Set": "High Tech Terror",
    "Nordic Knight Set": "Ice Cave",
    "Kogbold Pyrotechnician Wizard Set": "Kogbold Steamworks",
    "Kogbold Turretsmith Archer Set": "Kogbold Steamworks",
    "Dragon Tamer Set": "Lair of Draconis",
    "Fiery Kensei Set": "Lair of Shaitan",
    "Lost Golem Set": "Lost Halls",
    "Corrupted Paladin": "Lost Halls",
    "Horrific Sorcerer Set": "Mad Lab",
    "Scientific Summoner Set": "Mad Lab",
    "Horticultural Huntress Set": "Magic Woods",
    "Vampire Rogue Set": "Manor of the Immortals",
    "Raijin Disciple Set": "Moonlight Village",
    "Akuma Slayer Set": "Mountain Temple",
    "Onmyoji Ninja Set": "Mountain Temple",
    "Tidal Wave Kensei Set": "Ocean Trench",
    "Oryx Awesome Set": "Oryx's Castle",
    "Oryx's Battle Attires": "Oryx's Sanctuary",
    "Flesh Collector Set": "Parasite Chambers",
    "Court Magician Sorcerer Set": "Puppet Master's Theatre",
    "Storm Shaman Set": "Secluded Thicket",
    "Spellbound Soulthief Set": "Spectral Penitentiary",
    "Extra Dimensional Druid Set": "Sprite World",
    "Swamp Hermit Druid Set": "Sulfurous Wetlands",
    "Swarming Huntress Set": "The Nest",
    "Twilight Archmage Set": "The Shatters",
    "Legacy Twilight Archmage Set": "The Shatters",
    "Cuboid Necromancer Set": "The Third Dimension",
    "Void Doctor Priest Set": "The Void",
    "Priest of Geb Set": "Tomb of the Ancients",
    "Acidified Assassin Set": "Toxic Sewers",
    "Alchemist Assassin Set": "Toxic Sewers",
    "Phantom Archer Set": "Undead Lair",
    "Phytomancer Summoner Set": "Woodland Labyrinth",
    # Realm / World ST Sets
    "Phylactery Mystic Set": "Realm of the Mad God",
    "Mad God's Messenger Set": "Realm of the Mad God",
    "Plague Doctor Priest Set": "Realm of the Mad God",
    "Storm Caller Bard Set": "Realm of the Mad God",
    "Angelic Bard Set": "Realm of the Mad God",
    "Daring Discoverer Samurai Set": "Realm of the Mad God",
    "Treasure Hunter Archer Set": "Realm of the Mad God",
    "Timekeeper Mystic": "Realm of the Mad God",
    "Ancestral Shaman Set": "Realm of the Mad God",
    "Astral Arcanist Set": "Realm of the Mad God",
    "Golden Archer Set": "Realm of the Mad God",
    "Daeva Trickster": "Realm of the Mad God",
    "2-Bit Mystic Set": "Realm of the Mad God",
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
    for ver in ("item_catalog_v9.json", "item_catalog_v8.json"):
        candidates = [
            Path.home() / "Library/Application Support/RotmgTracker" / ver,
            Path(os.environ.get("APPDATA", "")) / "RotmgTracker" / ver,
        ]
        for p in candidates:
            if p.is_file():
                return p
    return None


def strip_shiny(name: str) -> str:
    s = name.strip()
    if s.lower().startswith("shiny "):
        return s[6:].strip()
    return s


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate item_sources.json v3 (Dungeon -> Boss -> Items + ST Sets) from resources.assets.")
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    args = parser.parse_args()

    assets_path = find_resources_assets()
    cat_path = find_item_catalog()
    if not assets_path or not cat_path:
        print("ERROR: Could not locate resources.assets or item_catalog_v*.json", file=sys.stderr)
        return 1

    cat_items = json.loads(cat_path.read_text(encoding="utf-8"))
    # Collectible items only: non-tiered (Tier < 0), exclude Blueprints (Quality == 5)
    cat_by_id = {it["ItemId"]: it for it in cat_items if it.get("Tier", -1) < 0 and it.get("Quality") != 5}

    raw = assets_path.read_bytes()
    # dungeon -> set of base UT/White item names
    ut_by_dungeon: dict[str, set[str]] = defaultdict(set)
    # dungeon -> set of base ST item names
    st_by_dungeon: dict[str, set[str]] = defaultdict(set)
    mapped_by_obj: set[str] = set()
    fp_candidates: dict[str, set[str]] = defaultdict(set)

    pos = 0
    while True:
        s = raw.find(b"<Objects>", pos)
        if s < 0:
            break
        e = raw.find(b"</Objects>", s)
        if e < 0:
            break
        chunk = raw[s : e + 10].decode("utf-8", "replace")
        pos = e + 10
        if len(chunk) > 25_000_000:
            continue
        chunk = re.sub(r"&(?!(?:amp|lt|gt|apos|quot|#\d+|#x[0-9a-fA-F]+);)", "&amp;", chunk)
        try:
            root = ET.fromstring(chunk)
        except Exception:
            continue

        for obj in root.findall("Object"):
            t = obj.attrib.get("type")
            if not t:
                continue
            tid = int(t, 0)
            it = cat_by_id.get(tid)
            if not it:
                continue

            base_name = strip_shiny(it["Name"])
            labels = [x.strip().upper() for x in (obj.findtext("Labels") or "").split(",") if x.strip()]
            set_name = (obj.attrib.get("setName") or "").strip()
            is_st = it.get("Quality") == 3 or "ST" in labels or "TAB_ST" in labels or bool(set_name)

            # 1. ST Set -> Dungeon mapping
            if set_name and set_name in ST_SET_TO_DUNGEON:
                st_by_dungeon[ST_SET_TO_DUNGEON[set_name]].add(base_name)
                mapped_by_obj.add(base_name.lower())
                continue

            # 2. Exact per-object <Labels> (ORG_* / REALMWHITE)
            for lbl in labels:
                dungeon = ORG_LABEL_TO_DUNGEON.get(lbl)
                if dungeon:
                    if is_st:
                        st_by_dungeon[dungeon].add(base_name)
                    else:
                        ut_by_dungeon[dungeon].add(base_name)
                    mapped_by_obj.add(base_name.lower())

        # 3. Single-dungeon ForgeProperties fallback
        for fp in root.findall("ForgeProperties"):
            t = fp.attrib.get("type")
            if not t:
                continue
            tid = int(t, 0)
            it = cat_by_id.get(tid)
            if not it:
                continue
            base_name = strip_shiny(it["Name"])
            for r in fp.findall("RequireDismantleWithLabel"):
                lbl = (r.text or "").strip().upper()
                if "," in lbl:
                    continue
                dungeon = ORG_LABEL_TO_DUNGEON.get(lbl)
                if dungeon:
                    fp_candidates[base_name].add(dungeon)

    for base_name, dungeons in fp_candidates.items():
        if base_name.lower() not in mapped_by_obj:
            for d in dungeons:
                ut_by_dungeon[d].add(base_name)

    all_dungeons = sorted(set(ut_by_dungeon.keys()) | set(st_by_dungeon.keys()), key=str.lower)
    dungeons_out: dict[str, dict] = {}
    unique_items: set[str] = set()
    total_boss_groups = 0
    total_st = 0

    for d in all_dungeons:
        boss_buckets: dict[str, list[str]] = defaultdict(list)
        default_boss = DEFAULT_DUNGEON_BOSS.get(d, d)

        for item_name in sorted(ut_by_dungeon.get(d, set()), key=str.lower):
            boss = ITEM_TO_BOSS.get((d, item_name), default_boss)
            boss_buckets[boss].append(item_name)
            unique_items.add(item_name.lower())

        # Sort boss buckets by canonical BOSS_ORDER, then alphabetically
        order = BOSS_ORDER.get(d, [])
        order_idx = {b: idx for idx, b in enumerate(order)}
        sorted_bosses = sorted(
            boss_buckets.items(),
            key=lambda kv: (order_idx.get(kv[0], 999), kv[0].lower()),
        )

        boss_entries = [{"boss": b, "items": items} for b, items in sorted_bosses if items]
        total_boss_groups += len(boss_entries)

        st_items = sorted(st_by_dungeon.get(d, set()), key=str.lower)
        for s_it in st_items:
            unique_items.add(s_it.lower())
        total_st += len(st_items)

        dungeons_out[d] = {
            "bosses": boss_entries,
            "st": st_items,
        }

    payload = {
        "_info": "Generated from official DECA ORG_*, REALMWHITE, and ST setName XML metadata in resources.assets. Grouped by Dungeon -> Boss -> Items + ST Sets.",
        "version": 3,
        "dungeons": dungeons_out,
    }

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"Wrote {args.output}: {len(dungeons_out)} dungeons, {total_boss_groups} boss groups, {total_st} ST mappings, {len(unique_items)} unique base items")
    for check_d in ("Oryx's Sanctuary", "The Shatters", "Moonlight Village", "Spectral Penitentiary", "Lost Halls", "The Void"):
        entry = dungeons_out.get(check_d, {})
        b_summary = [(b["boss"], len(b["items"])) for b in entry.get("bosses", [])]
        print(f"  [{check_d}] bosses={b_summary}, st={len(entry.get('st', []))}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
