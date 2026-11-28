#!/usr/bin/env python3
"""
Builds RotmgTracker.Core/Data/item_sources.json (v4) from:
  1. TrollOutt/rotmg-tools RealmEye archive (realmeye-enrichment.json + realmeye-data.json),
     providing exact Dungeon -> Boss / Miniboss / Dungeon Minions -> Items, ST Sets with exact
     enemy/boss sources, and Realm Event Bosses / Biomes.
  2. Official DECA ORG_* and REALMWHITE labels from local resources.assets as a fallback for
     brand-new items not yet linked on RealmEye.

Usage:
    python3 tools/build_item_sources.py
    python3 tools/build_item_sources.py --fetch   # re-downloads latest JSONs from GitHub into /tmp
"""

import argparse
import json
import os
import re
import sys
import urllib.request
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_OUTPUT = REPO_ROOT / "RotmgTracker.Core" / "Data" / "item_sources.json"

ENRICHMENT_URL = "https://raw.githubusercontent.com/TrollOutt/rotmg-tools/main/data/Index/realmeye-enrichment.json"
REALMEYE_DATA_URL = "https://raw.githubusercontent.com/TrollOutt/rotmg-tools/main/web/realmeye-data.json"
DEFAULT_ENRICHMENT_CACHE = Path("/tmp/rt_enrichment.json")
DEFAULT_REALMEYE_DATA_CACHE = Path("/tmp/rt_realmeye_data.json")

# Official DECA XML <Labels> ORG_* tags -> canonical DungeonName (fallback only)
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
    "ORG_ICECAVE": "Ice Citadel",
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

# Portals to skip from RealmEye (retired / temporary / heroic / advanced duplicates)
EXCLUDED_PORTAL_SUBSTRINGS = (
    "(original)",
    "(old)",
    "legacy ",
    "heroic ",
    "halloween ",
    "infernal ",
    "advanced kogbold",
    "plagued nest",
    "stromwell's rift",
    "trials of cronus",
    "white snake invasion",
    "oryxmania",
    "diagram dungeon",
    "tutorial",
    "candy miner",
    "admin arena",
    "stoneborn memory",
    "memory of the infected",
    "time chamber",
    "oryx's kitchen",
    "santa's workshop",
    "rainbow road",
    "queen bunny chamber",
    "neo forax",
    "neo katalund",
    "neo malogia",
    "neo untaris",
)

# Normalize RealmEye portal names to match GameDataService PortalMetadata.DungeonName
PORTAL_ALIASES: dict[str, str] = {
    "Ice Cave": "Ice Citadel",
    "Oryx's Chamber": "Oryx's Castle",
    "Wine Cellar": "Oryx's Castle",
}

# Normalize RealmEye enemy display names to exact GameDataService.ResolveEnemyByName names for sprites
BOSS_NAME_ALIASES: dict[str, str] = {
    "oryx-the-mad-god-3": "Oryx the Mad God 3",
    "oryx-the-mad-god-2": "Oryx the Mad God 2",
    "oryx-the-mad-god": "Oryx the Mad God",
    "lord-of-the-lost-lands": "Lord of the Lost Lands",
    "hermit-god": "Hermit God",
    "grand-sphinx": "Grand Sphinx",
    "cube-god": "Cube God",
    "skull-shrine": "Skull Shrine",
    "pentaract": "Pentaract Tower",
    "ghost-ship": "Ghost Ship",
    "rock-dragon": "Rock Dragon",
    "crystal-prisoner": "Crystal Prisoner",
    "avatar-of-the-forgotten-king": "Avatar of the Forgotten King",
    "the-bridge-sentinel": "The Bridge Sentinel",
    "twilight-archmage": "Twilight Archmage",
    "the-forgotten-king": "The Forgotten King",
    "archbishop-leucoryx": "Archbishop Leucoryx",
    "chancellor-dammah": "Chancellor Dammah",
    "chief-beisa": "Chief Beisa",
    "treasurer-gemsbok": "Treasurer Gemsbok",
    "marble-colossus": "Marble Colossus",
    "void-entity": "Void Entity",
    "malus": "Malus",
    "soulwarden-murcian": "Soulwarden Murcian",
    "doctor-lobotomik": "Doctor Lobotomik",
    "overseer-oculon": "Overseer Oculon",
    "groundskeeper-gretch": "Groundskeeper Gretch",
    "griefkeeper-zole": "Griefkeeper Zole",
    "factory-control-core": "Factory Control Core",
    "dancer-miko": "Dancer Miko",
    "sage-genji": "Sage Genji",
    "drummer-kaguya": "Drummer Kaguya",
    "kitsune-umi": "Kitsune Umi",
    "Pentaract": "Pentaract Tower",
    "Archdemon Malphas": "Malphas, Gilded Forgemaster",
    "Septavius the Ghost God": "Septavius, Master of the Undead",
    "Esben the Unwilling": "Esben the Neurotic",
    "Oryx the Mad God#Oryx the Mad God 3": "Oryx the Mad God 3",
    "Oryx the Mad God#Oryx the Mad God 2": "Oryx the Mad God 2",
}

# Moonlight Village boss fallback (Makakoyumi/Kagenohikari/Tezutsu Hanabi pages lack dropped_by in enrichment)
MOONLIGHT_BOSS_OVERRIDES: dict[str, str] = {
    "Taiko Drums": "Dancer Miko",
    "Flowering Kimono": "Dancer Miko",
    "Mischief": "Dancer Miko",
    "Makakoyumi": "Sage Genji",
    "+Makakoyumi": "Sage Genji",
    "Sage's Wakibiki": "Sage Genji",
    "Vigor": "Sage Genji",
    "Tezutsu Hanabi": "Drummer Kaguya",
    "+Tezutsu Hanabi": "Drummer Kaguya",
    "Ethereal Happi": "Drummer Kaguya",
    "Fortitude": "Drummer Kaguya",
    "Kagenohikari": "Kitsune Umi",
    "+Kagenohikari": "Kitsune Umi",
    "Elegant Parasol": "Kitsune Umi",
    "Rain Maker": "Kitsune Umi",
}

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
    "Hidden Interregnum": "The Syndicate",
    "High Tech Terror": "F.E.R.A.L.",
    "Ice Citadel": "Esben the Neurotic",
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
        "Dungeon Minions",
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
    "Oryx's Castle": ["Stone Guardian", "Janus the Doorwarden", "Oryx the Mad God", "Oryx the Mad God 2"],
    "Deadwater Docks": ["Calamity Crab", "Jon Bilgewater the Pirate King"],
    "The Nest": ["The Beekeeper", "Killer Bee Queen"],
    "Kogbold Steamworks": ["Kogbold Flying Machine", "Factory Control Core", "Dungeon Minions"],
}

# Ignore global/temporary chests & universal event bosses so they don't hijack dungeon items
IGNORED_DROP_SOURCES = (
    "quest-chest",
    "quest chest",
    "mystery",
    "reward-ticket",
    "event-chest",
    "event chest",
    "the-crucible",
    "the-tinkerer",
    "the-keyper",
    "the keyper",
    "biff",
    "leprechaun",
    "oryxmas",
    "santa",
    "interregnum-treasure-chest",
    "heroic ",
    "retro ",
    "voided ",
)


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


def to_slug(name: str) -> str:
    return re.sub(r"[^a-z0-9]+", "-", name.lower()).strip("-")


def normalize_portal(raw_name: str) -> str | None:
    clean = raw_name.split("#")[0].strip()
    low = clean.lower()
    if any(sub in low for sub in EXCLUDED_PORTAL_SUBSTRINGS):
        return None
    return PORTAL_ALIASES.get(clean, clean)


def normalize_boss_name(raw: str) -> str:
    if raw in BOSS_NAME_ALIASES:
        return BOSS_NAME_ALIASES[raw]
    no_prefix = raw[6:] if raw.startswith("enemy:") else raw
    if no_prefix in BOSS_NAME_ALIASES:
        return BOSS_NAME_ALIASES[no_prefix]
    clean = no_prefix.split("#")[0].strip()
    if clean in BOSS_NAME_ALIASES:
        return BOSS_NAME_ALIASES[clean]
    if clean.lower() in BOSS_NAME_ALIASES:
        return BOSS_NAME_ALIASES[clean.lower()]
    return clean


def is_ignored_source(raw: str) -> bool:
    low = raw.lower()
    return any(tok in low for tok in IGNORED_DROP_SOURCES)


def ensure_cached(path: Path, url: str, force: bool) -> Path:
    if force or not path.is_file():
        print(f"Downloading {url} -> {path} ...")
        urllib.request.urlretrieve(url, path)
    return path


def load_org_fallbacks(assets_path: Path, cat_by_id: dict[int, dict]) -> dict[str, set[str]]:
    raw = assets_path.read_bytes()
    org_by_base: dict[str, set[str]] = defaultdict(set)
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
            for lbl in labels:
                d = ORG_LABEL_TO_DUNGEON.get(lbl)
                if d:
                    org_by_base[base_name.lower()].add(d)
    return org_by_base


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate item_sources.json v4 from TrollOutt/rotmg-tools RealmEye archive + resources.assets.")
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--enrichment", type=Path, default=DEFAULT_ENRICHMENT_CACHE)
    parser.add_argument("--realmeye-data", type=Path, default=DEFAULT_REALMEYE_DATA_CACHE)
    parser.add_argument("--fetch", action="store_true", help="Re-download latest rotmg-tools JSONs from GitHub")
    args = parser.parse_args()

    enr_path = ensure_cached(args.enrichment, ENRICHMENT_URL, args.fetch)
    rd_path = ensure_cached(args.realmeye_data, REALMEYE_DATA_URL, args.fetch)
    cat_path = find_item_catalog()
    assets_path = find_resources_assets()
    if not cat_path:
        print("ERROR: Could not locate item_catalog_v9.json / v8.json", file=sys.stderr)
        return 1

    enr = json.loads(enr_path.read_text(encoding="utf-8"))["records"]
    rd = json.loads(rd_path.read_text(encoding="utf-8"))
    cat_items = json.loads(cat_path.read_text(encoding="utf-8"))

    # Collectible non-tiered non-BP items (we include Legacy/Reskin base names too so if toggled on, they map properly!)
    cat_by_id = {it["ItemId"]: it for it in cat_items if it.get("Tier", -1) < 0 and it.get("Quality") != 5}
    base_items: dict[str, tuple[str, bool, str]] = {}  # lower -> (displayBaseName, isSt, setName)
    for it in cat_by_id.values():
        bname = strip_shiny(it["Name"])
        low = bname.lower()
        is_st = it.get("Quality") == 3
        sname = (it.get("SetName") or "").strip()
        if low not in base_items:
            base_items[low] = (bname, is_st, sname)

    org_fallbacks = load_org_fallbacks(assets_path, cat_by_id) if assets_path else {}

    # 1. Index canonical dungeons -> bosses, minibosses/troom, minions, and dungeon_drop_interest
    boss_to_dungeons: dict[str, set[str]] = defaultdict(set)      # boss_name_low -> {dungeon}
    minion_to_dungeons: dict[str, set[str]] = defaultdict(set)    # enemy_name_low -> {dungeon}
    portal_drop_interest: dict[str, set[str]] = defaultdict(set)  # item_low or slug -> {dungeon}

    for k, v in enr.items():
        if not k.startswith("portal:"):
            continue
        raw_p = k[7:]
        if raw_p.endswith((" Enemies", " Guide")):
            continue
        dungeon = normalize_portal(raw_p)
        if not dungeon:
            continue

        for r in v.get("relations", []):
            rt = r.get("type")
            to = r.get("to", "")
            slug = r.get("toRealmEye", "")
            if rt in ("dungeon_boss", "dungeon_miniboss", "dungeon_treasure_boss"):
                if to.startswith("enemy:"):
                    bname = normalize_boss_name(to)
                    boss_to_dungeons[bname.lower()].add(dungeon)
                if slug:
                    boss_to_dungeons[normalize_boss_name(slug).lower()].add(dungeon)
                    boss_to_dungeons[slug.lower()].add(dungeon)
            elif rt in ("dungeon_enemy", "dungeon_minion", "dungeon_boss_minion"):
                if to.startswith("enemy:"):
                    ename = normalize_boss_name(to)
                    minion_to_dungeons[ename.lower()].add(dungeon)
                if slug:
                    minion_to_dungeons[slug.lower()].add(dungeon)
            elif rt == "dungeon_drop_interest":
                if to.startswith("item:"):
                    portal_drop_interest[to[5:].split("#")[0].strip().lower()].add(dungeon)
                if slug:
                    portal_drop_interest["slug:" + slug.lower()].add(dungeon)

    # Ensure well-known multi-boss dungeon bosses are explicitly bound to their dungeon
    for d, blist in BOSS_ORDER.items():
        for b in blist:
            if b != "Dungeon Minions":
                boss_to_dungeons[b.lower()].add(d)
    for d, b in DEFAULT_DUNGEON_BOSS.items():
        if d != "Realm of the Mad God":
            boss_to_dungeons[b.lower()].add(d)

    # 2. Index Realm biomes & creatures from realmeye-data.json
    biome_names: dict[str, str] = {}
    for b in (rd["biomes"].values() if isinstance(rd["biomes"], dict) else rd["biomes"]):
        bid = b.get("id", "")
        btitle = b.get("name") or b.get("slug", bid).replace("-", " ").title()
        biome_names[bid] = btitle

    realm_creature_role: dict[str, tuple[str, str]] = {}  # creature_low -> (displayName, role_or_biome)
    realm_drop_sources: dict[str, list[tuple[str, str]]] = defaultdict(list)  # item_low -> [(creatureName, role_or_biome)]
    creatures_iter = rd["creatures"].values() if isinstance(rd["creatures"], dict) else rd["creatures"]
    for c in creatures_iter:
        cname = normalize_boss_name(c.get("name", "").replace("’", "'"))
        groups = c.get("groups") or []
        role = groups[0].get("role", "regular") if groups else "regular"
        biome_id = groups[0].get("biome", "") if groups else ""
        biome_label = biome_names.get(biome_id, biome_id.replace("-", " ").title())
        realm_creature_role[cname.lower()] = (cname, role if role != "regular" else f"Biome: {biome_label}")
        for dr in (c.get("detail") or {}).get("drops") or []:
            dname = (dr.get("name") or "").strip().replace("’", "'")
            if dname:
                realm_drop_sources[dname.lower()].append((cname, role, biome_label))

    # 3. Index item enrichment records by lowercase base item name
    enr_items_by_low: dict[str, list[dict]] = defaultdict(list)
    for k, v in enr.items():
        if k.startswith("item:"):
            low = k[5:].split("#")[0].strip().lower()
            enr_items_by_low[low].append(v)

    # Output accumulators:
    # dungeon -> boss -> set of UT items
    ut_map: dict[str, dict[str, set[str]]] = defaultdict(lambda: defaultdict(set))
    # dungeon -> item_base_name -> boss_name (for ST items)
    st_map: dict[str, dict[str, str]] = defaultdict(dict)

    # Also build ST Set -> resolved dungeon from items that HAVE RealmEye sources,
    # so any missing piece of the same 4-piece ST set inherits its set's dungeon!
    resolved_st_set_dungeon: dict[str, tuple[str, str]] = {}
    unresolved_st_items: list[tuple[str, str]] = []  # (bname, setName)

    def resolve_item(low: str, bname: str, is_st: bool, set_name: str) -> bool:
        slug = to_slug(bname)
        enr_recs = enr_items_by_low.get(low, [])

        # Collect clean dropped_by enemies/portals from RealmEye item page
        re_enemies: list[str] = []
        re_portals: list[str] = []
        for rv in enr_recs:
            for r in rv.get("relations", []):
                if r.get("type") != "dropped_by":
                    continue
                to = r.get("to", "")
                rslug = r.get("toRealmEye", "")
                if is_ignored_source(to) or is_ignored_source(rslug):
                    continue
                if to.startswith("enemy:"):
                    re_enemies.append(normalize_boss_name(to))
                elif to.startswith("portal:"):
                    np = normalize_portal(to[7:])
                    if np:
                        re_portals.append(np)
                elif rslug:
                    re_enemies.append(normalize_boss_name(rslug))

        # Deduplicate preserving order
        re_enemies = list(dict.fromkeys(re_enemies))
        interest_dungeons = portal_drop_interest.get(low, set()) | portal_drop_interest.get("slug:" + slug, set())

        # Special handling for Eternal Phoenix drops (Realm encounter, NOT dungeon bosses!)
        if any(e.lower() == "eternal phoenix" for e in re_enemies):
            if is_st:
                st_map["Realm of the Mad God"][bname] = "Eternal Phoenix"
            else:
                ut_map["Realm of the Mad God"]["Eternal Phoenix"].add(bname)
            return True

        # Step 1: Match dropped_by enemy -> known dungeon boss/miniboss/troom
        boss_matches: list[tuple[str, str]] = []
        for enemy in re_enemies:
            dset = boss_to_dungeons.get(enemy.lower(), set())
            if not dset:
                continue
            # Prefer dungeon also listed in portal_drop_interest if multiple
            preferred = dset & interest_dungeons if (dset & interest_dungeons) else dset
            for d in sorted(preferred):
                boss_matches.append((d, enemy))

        if boss_matches:
            # Prefer a single primary dungeon if all enemies are in the same dungeon, or add per dungeon
            seen_d = set()
            for d, boss in boss_matches:
                if is_st:
                    if bname not in st_map[d]:
                        st_map[d][bname] = boss
                    if set_name and set_name not in resolved_st_set_dungeon:
                        resolved_st_set_dungeon[set_name] = (d, boss)
                else:
                    ut_map[d][boss].add(bname)
                seen_d.add(d)
            return True

        # Step 2: Match dropped_by enemy -> dungeon minions (clear drops like Fungal Breastplate / Shatters armors / Molten Mantle)
        minion_matches: list[tuple[str, str]] = []
        for enemy in re_enemies:
            dset = minion_to_dungeons.get(enemy.lower(), set())
            if not dset:
                continue
            preferred = dset & interest_dungeons if (dset & interest_dungeons) else dset
            for d in sorted(preferred):
                minion_matches.append((d, enemy))

        if minion_matches:
            for d, enemy in minion_matches:
                if is_st:
                    if bname not in st_map[d]:
                        st_map[d][bname] = enemy
                    if set_name and set_name not in resolved_st_set_dungeon:
                        resolved_st_set_dungeon[set_name] = (d, enemy)
                else:
                    ut_map[d]["Dungeon Minions"].add(bname)
            return True

        # Step 3: Realm Event Bosses & Biomes from realmeye-data.json + dropped_by Realm encounters
        rd_srcs = realm_drop_sources.get(low, [])
        if rd_srcs:
            # Prefer encounter/hero/beacon boss over regular biome mobs
            encounters = [c for c, role, _ in rd_srcs if role != "regular"]
            if encounters:
                boss = encounters[0]
            else:
                biomes = [bl for _, _, bl in rd_srcs if bl]
                boss = f"{biomes[0]} (Biome)" if biomes else rd_srcs[0][0]
            if is_st:
                st_map["Realm of the Mad God"][bname] = boss
                if set_name and set_name not in resolved_st_set_dungeon:
                    resolved_st_set_dungeon[set_name] = ("Realm of the Mad God", boss)
            else:
                ut_map["Realm of the Mad God"][boss].add(bname)
            return True

        # Check if dropped_by enemy is a Realm creature (even if not in rd_srcs)
        for enemy in re_enemies:
            if enemy.lower() in realm_creature_role:
                cname, role_or_biome = realm_creature_role[enemy.lower()]
                target_boss = cname if not role_or_biome.startswith("Biome:") else f"{role_or_biome[7:].strip()} (Biome)"
                if is_st:
                    st_map["Realm of the Mad God"][bname] = target_boss
                else:
                    ut_map["Realm of the Mad God"][target_boss].add(bname)
                return True

        # Step 4: Portal drop_interest or dropped_by portal
        cand_dungeons = set(re_portals) | set(interest_dungeons)
        if cand_dungeons:
            for d in sorted(cand_dungeons):
                boss = MOONLIGHT_BOSS_OVERRIDES.get(bname) if d == "Moonlight Village" else None
                boss = boss or DEFAULT_DUNGEON_BOSS.get(d, d)
                if is_st:
                    st_map[d][bname] = boss
                    if set_name and set_name not in resolved_st_set_dungeon:
                        resolved_st_set_dungeon[set_name] = (d, boss)
                else:
                    ut_map[d][boss].add(bname)
            return True

        # Step 5: Fallback to official DECA ORG_* / REALMWHITE labels from resources.assets
        org_d = org_fallbacks.get(low, set())
        if org_d:
            for d in sorted(org_d):
                boss = MOONLIGHT_BOSS_OVERRIDES.get(bname) if d == "Moonlight Village" else None
                boss = boss or DEFAULT_DUNGEON_BOSS.get(d, d)
                if is_st:
                    st_map[d][bname] = boss
                    if set_name and set_name not in resolved_st_set_dungeon:
                        resolved_st_set_dungeon[set_name] = (d, boss)
                else:
                    ut_map[d][boss].add(bname)
            return True

        return False

    # First pass: resolve all items
    for low, (bname, is_st, set_name) in sorted(base_items.items()):
        ok = resolve_item(low, bname, is_st, set_name)
        if not ok and is_st and set_name:
            unresolved_st_items.append((bname, set_name))

    # Second pass: any ST piece whose sibling pieces in the same ST set resolved to a dungeon inherits it
    for bname, set_name in unresolved_st_items:
        if set_name in resolved_st_set_dungeon:
            d, boss = resolved_st_set_dungeon[set_name]
            st_map[d][bname] = boss

    # Build final sorted JSON output (v4)
    all_dungeons = sorted(set(ut_map.keys()) | set(st_map.keys()), key=str.lower)
    dungeons_out: dict[str, dict] = {}
    unique_items: set[str] = set()
    total_boss_groups = 0
    total_st = 0

    for d in all_dungeons:
        bdict = ut_map.get(d, {})
        order = BOSS_ORDER.get(d, [])
        order_idx = {b.lower(): idx for idx, b in enumerate(order)}

        sorted_bosses = sorted(
            bdict.items(),
            key=lambda kv: (
                1 if kv[0] == "Dungeon Minions" else 0,
                1 if "(Biome)" in kv[0] else 0,
                order_idx.get(kv[0].lower(), 500),
                kv[0].lower(),
            ),
        )

        boss_entries = []
        for boss_name, items in sorted_bosses:
            sorted_items = sorted(items, key=str.lower)
            if sorted_items:
                boss_entries.append({"boss": boss_name, "items": sorted_items})
                for it in sorted_items:
                    unique_items.add(it.lower())
        total_boss_groups += len(boss_entries)

        st_entries = [
            {"item": item_name, "boss": boss_name}
            for item_name, boss_name in sorted(st_map.get(d, {}).items(), key=lambda kv: kv[0].lower())
        ]
        for se in st_entries:
            unique_items.add(se["item"].lower())
        total_st += len(st_entries)

        if boss_entries or st_entries:
            dungeons_out[d] = {
                "bosses": boss_entries,
                "st": st_entries,
            }

    payload = {
        "_info": "Generated from TrollOutt/rotmg-tools RealmEye archive (realmeye-enrichment.json + realmeye-data.json) with DECA resources.assets ORG_* fallback.",
        "version": 4,
        "dungeons": dungeons_out,
    }

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"Wrote {args.output}: {len(dungeons_out)} dungeons, {total_boss_groups} boss groups, {total_st} ST mappings, {len(unique_items)} unique base items")
    for check_d in ("Oryx's Sanctuary", "The Shatters", "Moonlight Village", "Spectral Penitentiary", "Lost Halls", "The Void", "The Nest", "Hidden Interregnum", "Kogbold Steamworks", "Abyss of Demons"):
        entry = dungeons_out.get(check_d, {})
        b_summary = [(b["boss"], len(b["items"])) for b in entry.get("bosses", [])]
        print(f"  [{check_d}] bosses={b_summary}, st={len(entry.get('st', []))}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
