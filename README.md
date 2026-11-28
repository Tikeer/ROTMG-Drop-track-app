# RotMG Drop, Dungeon & DPS Tracker (v2.0)

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)
![C#](https://img.shields.io/badge/C%23-13-239120?style=flat-square&logo=csharp)
![UI](https://img.shields.io/badge/UI-Photino.Blazor-5C2D91?style=flat-square&logo=blazor)
![Database](https://img.shields.io/badge/Database-SQLite%20%2B%20EF%20Core-003B57?style=flat-square&logo=sqlite)
![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20macOS%20%7C%20Linux-2ea44f?style=flat-square)

A cross-platform desktop tracker for **Realm of the Mad God (RotMG Exalt)** powered entirely by a **passive read-only TCP network sniffer** and local game asset extraction — zero manual data entry, zero client modification, zero server interference.

Built in **C# (.NET 10)** with **Photino.Blazor** (native OS WebView2 / WKWebView / WebKitGTK) and **SQLite + EF Core Migrations**.

---

## Key Features

### Passive Network Sniffer (TCP:2050)
- **Zero-touch tracking:** Passively reads game traffic via `SharpPcap` (`Npcap` on Windows, `libpcap` / `ChmodBPF` on macOS) with an ordered TCP reassembly buffer (`TcpReorderBuffer`) and RC4 stream decryption.
- **Automatic Account & Character Sync:** Detects all **19 RotMG classes**, character `charId`, skin, `X/8` maxed stats, Base Fame, Exaltations (stats 105–112), Realm Gold (stat 35), Rank Stars (stat 30), Seasonal status (stat 24), and currently equipped gear.
- **Rarity & Enchantments (`stat 80`):** Decodes item `UNIQUE_DATA` blobs to extract item rarity (**Common → Divine**, 0–4 enchantment slots) and exact enchantment names/descriptions both for loot drops and equipped gear.
- **Boss Kills, Dungeon Completions & Live DPS:** Confirms boss kills by correlating boss removal/kill flags with nearby loot bags, automatically records dungeon completions upon exiting the instance, and logs party damage contribution per fight.
- **Configurable Bag Filter:** Toggle which physical bag colors (White, Orange/ST, Red/Tops, Gold/BP, Blue, Cyan, Egg, Purple) are recorded to Loot History without affecting boss kill or Tinkerer mark tracking.

### Trophy Hall & Item Collection
- **Dungeon-Grouped Trophy Hall:** Organizes collectibles across 59 dungeons by **Boss / Miniboss** and **ST Sets**, plus a dedicated **Tiered Equipment** hall ordered cleanly (**Weapons → Abilities → Armors → Rings**, split by tier).
- **Rich Hover Tooltips & Rarity Matrix:** Displays item sprite, tier (`UT`/`ST`/`T14`), Feed Power, Fame Bonus, drop sources with boss sprites, and a 5-tier **Rarities Collected** tracker.
- **Local Sprite Atlas (Zero Copyrighted Assets in Git):** Extracts item, boss, portal, bag, skin, and UI sprites directly from your local RotMG Exalt `resources.assets` into a compact packed PNG atlas served via a custom `rotmg-sprites://` scheme.

### Profile, Live Feed, Graveyard & Data Safety
- **Live Feed & Unified Loot History:** Clickable drop-category filters (Regular White, Legendary White, Realm White, ST, UT, Gold Bag, Tops, Shiny), bag grouping, rarity badges, and 100-row paginated history.
- **Character Profile:** Equipped gear with live rarity & enchantment tooltips, **Dream Set** target tracker (with empirical progress from tracked runs & drops), **Top Drops** (scored by `RarityScoreEngine` + customizable `item_value.json`), **Best Bags**, and **Est. Death Fame** (using the official client fame bonus table for `X/8` maxing + 76 single-dungeon milestones + 13 dungeon collections).
- **Daily Quests & Graveyard:** Collapsible Tinkerer mark tracker taskbar and automated Graveyard archiving on character death (`DEATH` packet) with killer/dungeon sprites and estimated death fame.
- **Backup & Sync:** Automated SQLite `VACUUM INTO` backups and JSON history export/import with deduplication for syncing records between Windows and macOS.

---

## Architecture

```text
ROTMG_Loot_Tracker_APP/
├── RotmgTracker.Core/        # Domain models, EF Core SQLite DB & migrations, GameDataService,
│                             # SpriteAtlasService, ItemSourceService, RarityScoreEngine, DeathFameCalculator
├── RotmgTracker.Sniffer/     # Passive SharpPcap listener, TcpReorderBuffer, RC4, packet & stat 80 parsers
├── RotmgTracker.Desktop/     # Photino.Blazor host, Razor tab panels, custom dark UI & CSS
├── scripts/                  # One-click single-file Windows (.exe + Desktop shortcut) & macOS (.app) builders
└── tools/                    # Offline dataset builder for Data/item_sources.json
```

---

## Prerequisites

- **[.NET 10 SDK](https://dotnet.microsoft.com/download)** (only needed to build from source)
- **Windows:** [Npcap](https://npcap.com/) installed with **"Install Npcap in WinPcap API-compatible Mode"** checked
- **macOS:** ChmodBPF permissions for non-root packet capture:
  ```bash
  brew install --cask wireshark-chmodbpf
  ```
- **RotMG Exalt** installed locally (used once to extract item/boss/enchantment catalogs and build the local sprite atlas)

---

## One-Click Release Build

Instead of digging through hundreds of `.dll` files, use the included release scripts to produce a single clickable app:

### Windows (`RotmgTracker.exe` + Desktop Shortcut)
Run in PowerShell from the repository root (with the app closed):
```powershell
.\scripts\build-windows.ps1
```
This creates a clean single-file executable at `.\publish\win-x64\RotmgTracker.exe` (alongside only `wwwroot\` and `Data\`) and automatically adds a **`RotMG Tracker`** shortcut on your Desktop. Pass `-NoShortcut` to skip the shortcut.

### macOS (`RotMG Tracker.app`)
Run in Terminal from the repository root:
```bash
chmod +x ./scripts/build-macos.sh
./scripts/build-macos.sh
```
This builds `./publish/RotMG Tracker.app`, which you can double-click directly in Finder or drag into `/Applications`.

### Development Run
```bash
dotnet run --project RotmgTracker.Desktop
```

---

## Data Storage

All user data stays local in your OS application data directory (never committed to git):
- **Windows:** `%APPDATA%\RotmgTracker\`
- **macOS:** `~/Library/Application Support/RotmgTracker/`

Includes `rotmg_tracker.db` (SQLite), `ui_settings.json`, `item_value.json` (optional per-item score multipliers), `sprites/` (local sprite atlas cache), `backups/`, `exports/`, and `import/`.

---

## Roadmap

- [x] **v1.0** — Core domain models, multi-character tracking, SQLite persistence, dark Photino.Blazor UI
- [x] **v2.0** — Passive TCP:2050/RC4 sniffer with TCP reordering, 19-class & account detection, Rarity & Enchantments (`stat 80`), Boss Kill / Dungeon Completion / Live DPS, Trophy Hall & Tiered Equipment Collection, local `resources.assets` sprite atlas, Profile (Dream Set, Top Drops, Best Bags, Est. Death Fame), Graveyard, Backup & Sync, one-click Windows/macOS builds
- [ ] **v2.1+** — Own-character DPS estimation (from weapon shots), Vault packet (`117`) parsing, Enchantment & Pet Fame calculators
