# RotMG Drop, Dungeon & DPS Tracker (v2.0)

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)
![C#](https://img.shields.io/badge/C%23-13-239120?style=flat-square&logo=csharp)
![UI](https://img.shields.io/badge/UI-Photino.Blazor-5C2D91?style=flat-square&logo=blazor)
![Database](https://img.shields.io/badge/Database-SQLite%20%2B%20EF%20Core-003B57?style=flat-square&logo=sqlite)
![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20macOS%20%7C%20Linux-2ea44f?style=flat-square)

A lightweight, cross-platform desktop companion app for **Realm of the Mad God (RotMG Exalt)** that automatically tracks your loot drops, item collection, dungeon completions, character stats, boss fights, and graveyard — completely read-only, with zero manual entry and zero modification to the game client.

Built in **C# (.NET 10)** using **Photino.Blazor** and a local **SQLite (EF Core)** database.

---

## Key Features

- **Automatic Loot & Character Tracking:** Passively detects your characters across all 19 RotMG classes along with their fame, maxed stats (`X/8`), exaltations, seasonal status, rank stars, equipped gear, and dropped bags in real time.
- **Rarity & Enchantments:** Automatically recognizes item rarity (**Common → Divine**) and enchantments both on new drops and on your equipped gear.
- **Live Feed & Loot History:** Real-time drop feed with category filters (Regular Whites, Legendary Whites, Realm Whites, STs, UTs, Gold Bags, Tops, Shinies), bag grouping, and fast paginated history.
- **Item Collection — Trophy Hall:** Organizes collectible items by dungeon, boss, and ST sets, plus a dedicated **Tiered Equipment** view (Weapons, Abilities, Armors, Rings) with hover tooltips and per-rarity collection progress.
- **Character Profile & Dream Set:** Displays your active character's gear, customizable **Dream Set** goals, **Top Drops**, **Best Bags**, and **Est. Death Fame** based on maxed stats and tracked dungeon runs.
- **Live DPS & Dungeon Completions:** Automatically logs confirmed dungeon completions and party damage contribution during boss fights.
- **Daily Quests & Graveyard:** Collapsible Tinkerer mark tracker and automatic Graveyard archiving when a character falls.
- **Configurable Tracking & Backup/Sync:** Choose which bag colors are saved to your Loot History, create automatic local database backups, and export/import your history between machines.

---

## Architecture

```text
ROTMG_Loot_Tracker_APP/
├── RotmgTracker.Core/        # Domain models, SQLite/EF Core persistence, catalogs & calculators
├── RotmgTracker.Sniffer/     # Read-only passive network listener
├── RotmgTracker.Desktop/     # Cross-platform Photino.Blazor desktop UI
└── scripts/                  # One-click Windows (.exe + Desktop shortcut) & macOS (.app) build scripts
```

All game icons are loaded locally from your own RotMG installation at runtime — no copyrighted game assets are bundled in this repository.

---

## Prerequisites

- **[.NET 10 SDK](https://dotnet.microsoft.com/download)** (only needed to build from source)
- **Windows:** [Npcap](https://npcap.com/) installed with **"Install Npcap in WinPcap API-compatible Mode"** checked
- **macOS:** Capture permissions enabled via Homebrew:
  ```bash
  brew install --cask wireshark-chmodbpf
  ```
- **RotMG Exalt** installed locally

---

## One-Click Release Build

Use the included scripts to build a clean standalone app without digging through `.dll` files:

### Windows (`RotmgTracker.exe` + Desktop Shortcut)
Run in PowerShell from the repository root (with the app closed):
```powershell
.\scripts\build-windows.ps1
```
Creates a single-file executable at `.\publish\win-x64\RotmgTracker.exe` and automatically places a **`RotMG Tracker`** shortcut on your Desktop. Pass `-NoShortcut` to skip creating the shortcut.

### macOS (`RotMG Tracker.app`)
Run in Terminal from the repository root:
```bash
chmod +x ./scripts/build-macos.sh
./scripts/build-macos.sh
```
Creates `./publish/RotMG Tracker.app`, ready to double-click or drag into `/Applications`.

### Development Run
```bash
dotnet run --project RotmgTracker.Desktop
```

---

## Local Data

All tracking data stays strictly on your machine:
- **Windows:** `%APPDATA%\RotmgTracker\`
- **macOS:** `~/Library/Application Support/RotmgTracker/`

---

## Roadmap

- [x] **v1.0** — Multi-character tracking, SQLite persistence, dark Photino.Blazor UI
- [x] **v2.0** — Automatic passive tracking, 19 classes, rarity & enchantments, Trophy Hall & Tiered Equipment collection, Profile & Dream Set, Live DPS & dungeon completions, Est. Death Fame, Graveyard, Backup & Sync, one-click builds
- [ ] **v2.1+** — Own-character DPS estimation, Enchantment & Pet Fame calculators
