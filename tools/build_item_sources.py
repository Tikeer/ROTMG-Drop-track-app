#!/usr/bin/env python3
"""
Builds RotmgTracker.Core/Data/item_sources.json directly from DECA's official ORG_* and REALMWHITE
labels embedded inside RotMG Exalt's resources.assets XML (zero guesswork, zero Lost Halls/Void duplicates,
zero Blueprints).

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


def strip_shiny(name: str) -> str:
    s = name.strip()
    if s.lower().startswith("shiny "):
        return s[6:].strip()
    return s


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate item_sources.json from official DECA ORG_* labels in resources.assets.")
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    args = parser.parse_args()

    assets_path = find_resources_assets()
    cat_path = find_item_catalog()
    if not assets_path or not cat_path:
        print("ERROR: Could not locate resources.assets or item_catalog_v8.json", file=sys.stderr)
        return 1

    cat_items = json.loads(cat_path.read_text(encoding="utf-8"))
    # Collectible items only: non-tiered (Tier <= 0), exclude Blueprints (Quality == 5)
    cat_by_id = {it["ItemId"]: it for it in cat_items if it.get("Tier", -1) <= 0 and it.get("Quality") != 5}

    raw = assets_path.read_bytes()
    dungeon_map: dict[str, set[str]] = defaultdict(set)
    # Track items explicitly mapped by <Object><Labels> (more specific than combined ForgeProperties like ORG_LH,ORG_CULT,ORG_VOID)
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

        # 1. Exact per-object <Labels> (e.g. ORG_LH, ORG_VOID, ORG_CULT, ORG_FUNGAL, ORG_CRYSTAL, REALMWHITE)
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
                dungeon = ORG_LABEL_TO_DUNGEON.get(lbl)
                if dungeon:
                    dungeon_map[dungeon].add(base_name)
                    mapped_by_obj.add(base_name.lower())

        # 2. Single-dungeon ForgeProperties (e.g. ORG_KOGBOLD_CORE, ORG_SPECTRAL_CORE, ORG_MOONLIGHT_CORE)
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
                # Skip multi-dungeon combined forge labels like "ORG_LH,ORG_CULT,ORG_VOID"
                if "," in lbl:
                    continue
                dungeon = ORG_LABEL_TO_DUNGEON.get(lbl)
                if dungeon:
                    fp_candidates[base_name].add(dungeon)

    # Only add ForgeProperties fallback for items that didn't already get an exact <Object><Labels> dungeon
    for base_name, dungeons in fp_candidates.items():
        if base_name.lower() not in mapped_by_obj:
            for d in dungeons:
                dungeon_map[d].add(base_name)

    dungeons_out = {
        d: sorted(items, key=str.lower)
        for d, items in sorted(dungeon_map.items(), key=lambda kv: kv[0].lower())
        if items
    }

    payload = {
        "_info": "Generated directly from official DECA ORG_* and REALMWHITE labels in resources.assets XML. Shiny variants inherit their base item source.",
        "version": 2,
        "dungeons": dungeons_out,
    }

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    total_pairs = sum(len(v) for v in dungeons_out.values())
    unique_items = len({it.lower() for v in dungeons_out.values() for it in v})
    print(f"Wrote {args.output} ({len(dungeons_out)} dungeons, {unique_items} unique base items, {total_pairs} mappings)")
    for check_d in ("Lost Halls", "The Void", "Cultist Hideout", "Oryx's Sanctuary", "Moonlight Village", "Realm of the Mad God"):
        print(f"  [{check_d}] ({len(dungeons_out.get(check_d, []))}): {dungeons_out.get(check_d, [])[:8]}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
