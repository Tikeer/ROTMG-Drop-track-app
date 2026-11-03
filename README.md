# RotMG Drop & Session Tracker

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)
![C#](https://img.shields.io/badge/C%23-13-239120?style=flat-square&logo=csharp)
![UI](https://img.shields.io/badge/UI-Photino.Blazor-5C2D91?style=flat-square&logo=blazor)
![Database](https://img.shields.io/badge/Database-SQLite%20%2B%20EF%20Core-003B57?style=flat-square&logo=sqlite)
![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20macOS%20%7C%20Linux-2ea44f?style=flat-square)

A lightweight, cross-platform desktop utility designed for **Realm of the Mad God (RotMG)** players to track character loot drops, daily dungeon mark quests, fallen characters, and end-of-season chest openings without relying on manual text notes.

Built with a decoupled architecture in **C# (.NET 10)** using **Photino.Blazor** and **SQLite** to deliver a fast, hardware-accelerated native desktop experience across Windows, macOS, and Linux while maintaining a shared domain layer ready for future expansion.

<img width="905" height="618" alt="RotMG Tracker Dashboard" src="https://github.com/user-attachments/assets/20423096-2381-4508-81a5-3aaa0a9b80ca" />

## Key Features

- **Multi-Character & Class Tracking:** Create and switch between multiple active characters across all 18 RotMG classes with *Seasonal (SSNL)* and *Non-Seasonal* badges.
- **Loot Counter:** Track *Regular Whites*, *Legendary Whites*, *STs*, *Blueprints*, *Tops*, *Divine Items*, *Shinies*, and *Shiny Divines* with instant background persistence.
- **Automated Graveyard Archiving:** Marking a character as fallen (`RIP`) automatically archives its final loot statistics, class, and timestamp into a dedicated Graveyard view.
- **Dungeon Mark To-Do List:** Track required dungeon marks (e.g., `3x Nest`, `4x Davy Jones`) for daily Tinkerer quests with animated progress bars and completion indicators.
- **Chest Opening Analyzer:** Record loot across multiple chest categories (*Standard*, *Mighty*, *Void*, *Royal*, *Other*), track total chests opened, and calculate White/ST ratios in real time.
- **SQLite Local Database:** Powered by **Entity Framework Core & SQLite** stored safely in the OS application data directory, featuring automatic migration from legacy `rotmg_data.json` files.

## Architecture & Tech Stack

The solution follows **Separation of Concerns (SoC)** to keep domain logic and data persistence completely independent of the UI rendering layer:

```text
RotmgTrackerV1/
├── RotmgTrackerV1.Core/       # C# Class Library (Domain Models, Enums, EF Core SQLite Context, State Services)
└── RotmgTrackerV1.Desktop/    # Cross-Platform Photino.Blazor Host (Native OS Window + Razor/CSS UI)           
```
- **Backend / Domain (`Core`):** C#, .NET 10, Entity Framework Core (SQLite), LINQ. Zero UI dependencies—reusable across desktop and web targets.
- **Frontend (`Desktop`):** Photino.Blazor leveraging native OS webviews (WebView2 on Windows, WKWebView on macOS, WebKitGTK on Linux) for low memory usage, native HiDPI scaling, and smooth 144Hz rendering.

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Supported OS: Windows 10/11, macOS (Apple Silicon / Intel), or Linux
- IDE: JetBrains Rider or Visual Studio 2022+

### Build & Run

1. Clone the repository:
   ```bash
   git clone [https://github.com/Tikeer/RotmgTrackerV1.git](https://github.com/Tikeer/RotmgTrackerV1.git)
   cd RotmgTrackerV1
    ```
2. Build and run the desktop application:
    ```bash
    dotnet run --project RotmgTrackerV1.Desktop
    ```

### Building Standalone Executables (Release)
You can compile self-contained binaries (no .NET installation required on the target machine) for any platform using the following commands:
- Windows (x64 single-file .exe):
```bash
dotnet publish RotmgTrackerV1.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish/windows
```
-macOS (Apple Silicon arm64):
```bash
dotnet publish RotmgTrackerV1.Desktop -c Release -r osx-arm64 --self-contained true -o ./publish/macos
```
Linux (x64):
```bash
dotnet publish RotmgTrackerV1.Desktop -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o ./publish/linux
```
Roadmap

- [x] Core domain models & automatic persistence

- [x] Dark-mode dashboard UI with collapsible navigation sidebar

- [x] Multi-character tracking with RotMG class selection & Graveyard view

- [x] Migration to cross-platform Photino.Blazor & SQLite database

- [ ] v2.0 Core: Passive network packet sniffer (SharpPcap / Npcap / libpcap)

- [ ] Automatic account/character detection in Nexus & automated Loot History log

- [ ] Live Dungeon Damage (DPS) meter with player contribution percentages

- [ ] Item Collection log & Season countdown timer