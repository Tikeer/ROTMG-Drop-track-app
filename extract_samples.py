#!/usr/bin/env python3
"""
RotMG Tracker - wyciaganie probek XML z plikow gry (tylko ODCZYT).

Uzycie:
    python3 extract_samples.py                 # szuka resources.assets samo
    python3 extract_samples.py /sciezka/do/resources.assets

Wynik: ~/rotmg_samples_out.txt  (wklej mi jego zawartosc albo dolacz plik)
Skrypt nic nie modyfikuje i nie wysyla.
"""
import glob
import os
import re
import sys

OUT = os.path.expanduser("~/rotmg_samples_out.txt")
MAX_SAMPLE = 3500


def find_assets():
    if len(sys.argv) > 1:
        return sys.argv[1]
    pats = [
        "~/.local/share/RealmOfTheMadGod/**/resources.assets",
        "~/Library/Application Support/**/RotMGExalt*/**/resources.assets",
        "/Applications/**/resources.assets",
    ]
    for p in pats:
        hits = glob.glob(os.path.expanduser(p), recursive=True)
        if hits:
            return hits[0]
    return None


def carve_objects(data):
    blocks = []
    pos = 0
    while True:
        s = data.find(b"<Objects", pos)
        if s < 0:
            break
        e = data.find(b"</Objects>", s)
        if e < 0:
            break
        e += len(b"</Objects>")
        if 20 <= e - s <= 25_000_000:
            blocks.append(data[s:e].decode("utf-8", errors="ignore"))
        pos = e
    return blocks


def main():
    path = find_assets()
    if not path or not os.path.exists(path):
        print("Nie znaleziono resources.assets. Podaj sciezke jako argument:")
        print("  python3 extract_samples.py /sciezka/do/resources.assets")
        sys.exit(1)
    print("Czytam:", path)
    with open(path, "rb") as f:
        data = f.read()

    objects = {}
    forges = {}
    obj_re = re.compile(r'<Object\b[^>]*\bid="([^"]+)"[^>]*>.*?</Object>', re.S)
    forge_re = re.compile(r'<ForgeProperties\b[^>]*\bid="([^"]+)"[^>]*>.*?</ForgeProperties>', re.S)
    for block in carve_objects(data):
        for m in obj_re.finditer(block):
            objects.setdefault(m.group(1), m.group(0))
        for m in forge_re.finditer(block):
            forges.setdefault(m.group(1), m.group(0))

    out = []
    w = out.append
    w(f"# source: {path}")
    w(f"# objects={len(objects)} forge_props={len(forges)}")

    w("\n=== REALMWHITE ids ===")
    rw = [i for i, x in objects.items() if "REALMWHITE" in x]
    w(f"count={len(rw)}")
    w(", ".join(sorted(rw)[:200]))

    w("\n=== ogmur ===")
    for i, x in objects.items():
        if "ogmur" in i.lower():
            w(x[:MAX_SAMPLE])
    for i, x in forges.items():
        if "ogmur" in i.lower():
            w(x[:MAX_SAMPLE])

    w("\n=== blueprints / schematics ===")
    bp = [i for i in objects if re.search(r"blueprint|schematic", i, re.I)]
    w(f"count={len(bp)}")
    w(", ".join(sorted(bp)[:150]))
    with_bag = next((i for i in bp if "<BagType>" in objects[i]), None)
    without_bag = next((i for i in bp if "<BagType>" not in objects[i]), None)
    for label, i in (("WITH BagType", with_bag), ("WITHOUT BagType", without_bag)):
        w(f"\n--- {label}: {i} ---")
        if i:
            w(objects[i][:MAX_SAMPLE])

    w("\n=== enemies ===")
    enemies = {i: x for i, x in objects.items() if "<Enemy" in x}
    w(f"total_enemy={len(enemies)}")
    w(f"with_KeepDamageRecord={sum('<KeepDamageRecord' in x for x in enemies.values())}")
    w(f"with_Quest={sum('<Quest' in x for x in enemies.values())}")
    w(f"with_Oryx={sum('<Oryx' in x for x in enemies.values())}")

    terms = ["septavius", "oryx the mad god", "lord of the lost lands", "archdemon malphas"]
    picked = []
    for t in terms:
        for i in enemies:
            if t in i.lower() and i not in picked:
                picked.append(i)
                break
    for i, x in enemies.items():
        if len(picked) >= 4:
            break
        if "<KeepDamageRecord" in x and i not in picked:
            picked.append(i)
    for i in picked[:6]:
        x = re.sub(r"<Projectile\b.*?</Projectile>", "<Projectile.../>", objects[i], flags=re.S)
        w(f"\n--- {i} ---")
        w(x[:MAX_SAMPLE])

    with open(OUT, "w", encoding="utf-8") as f:
        f.write("\n".join(out))
    print("Gotowe ->", OUT, f"({os.path.getsize(OUT)} bajtow)")


if __name__ == "__main__":
    main()
