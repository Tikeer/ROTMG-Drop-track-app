# RotMG Drop & Session Tracker

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)
![C#](https://img.shields.io/badge/C%23-13-239120?style=flat-square&logo=csharp)
![Blazor Hybrid](https://img.shields.io/badge/UI-WPF%20%2B%20Blazor%20Hybrid-5C2D91?style=flat-square&logo=blazor)
![Platform](https://img.shields.io/badge/Platform-Windows-0078D6?style=flat-square&logo=windows)

A lightweight, modern desktop utility designed for **Realm of the Mad God (RotMG)** players to track character loot drops, daily dungeon mark quests, and end-of-season chest openings without relying on manual text notes.

Built with a decoupled architecture in **C# (.NET 10)** to support a native Windows desktop experience today while maintaining a shared domain layer ready for a full web application expansion.

---

## Key Features (v1 Desktop)

* **Seasonal & Non-Seasonal Loot Counter:** Track *Regular Whites*, *Legendary Whites*, *STs*, *Blueprints*, *Tops*, *Divine Items*, and *Shinies* with quick increment/decrement controls.
* **Automated Graveyard Archiving:** Resetting a character upon death automatically archives the session's final stats and timestamp into a local Graveyard history before starting a fresh run.
* **Dungeon Mark To-Do List:** Track required dungeon marks (e.g., `3x Nest`, `4x Davy Jones`) for daily Tinkerer quests with visual completion indicators.
* **Chest Opening Analyzer:** Record loot across multiple chest categories (*Standard*, *Mighty*, *Void*, *Royal*, *Other*) with real-time calculation of total White/ST ratios.
* **Instant Local Persistence:** Every change is automatically serialized to local storage (`rotmg_data.json`), eliminating manual saving.

---

## Architecture & Tech Stack

The solution is structured using **Separation of Concerns (SoC)** to ensure business logic is completely independent of the rendering layer:

```text
RotmgTrackerV1/
├── RotmgTrackerV1.Core/       # Pure C# Class Library (Domain Models, Enums, State & Storage Services)
└── RotmgTrackerV1.Desktop/    # WPF Host with BlazorWebView (Modern HTML/CSS/Razor UI)
```
- **Backend / Domain (`Core`):** C#, .NET 10, LINQ, `System.Text.Json`. Zero UI dependencies—100% reusable for future Web API or Blazor WebAssembly targets.
- **Frontend (`Desktop`):** WPF hosting Blazor Hybrid (`BlazorWebView`),  enabling responsive, dark-themed web styling inside a fast native Windows executable.

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Windows 10/11 (with Microsoft Edge WebView2 Runtime, pre-installed on modern Windows)
- IDE: JetBrains Rider or Visual Studio 2022+

### Build & Run

1. Clone the repository:
   ```bash
   git clone [https://github.com/Tikeer/RotmgTrackerV1.git](https://github.com/Tikeer/RotmgTrackerV1.git)
   cd RotmgTrackerV1
    ```
2. Build and run the desktop project:
    ```bash
    dotnet run --project RotmgTrackerV1.Desktop
    ```

Roadmap

- [x] Core domain models & automatic JSON persistence

- [x] Dark-mode dashboard UI inspired by modern RotMG community tools

- [x] Dedicated Graveyard history view & character lifetime statistics

- [ ] Season countdown timer & item collection log

- [ ] v2 Expansion: Standalone Web Application sharing RotmgTrackerV1.Core