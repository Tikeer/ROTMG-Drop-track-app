# RotMG Tracker — Technical & Product Specification

## 0. Core Engineering Principles (Strict Rules)
1. **100% Passive & Legal (Priority #1):**
   - Zero memory reading (`ReadProcessMemory`), zero DLL/code injection, zero proxy/MITM.
   - Passive network packet sniffing ONLY (`SharpPcap` / `libpcap` / `Npcap` on `tcp port 2050`).
2. **Zero Hardcoded Game Data:**
   - NEVER hardcode item lists, dungeon lists, mark lists, or boss names in C# dictionaries.
   - All game metadata (Items, Portals, Grave Difficulty `1.0–10.0`, Bosses, Marks, Spritesheet coordinates) MUST be dynamically extracted from the local RotMG Exalt installation (`resources.assets` XML chunks).
3. **Single Source of Truth (SQLite):**
   - `LootHistory` and `DungeonRuns` are the primary source tables.
   - All counters (Character Drops, Class Totals, Seasonal Totals, Collection Progress, Wishlist Attempts) are dynamically aggregated from these tables.

---

## 1. Data Architecture & System Logic

### A. Character Identification & Lifecycle
- **Unique Identification (`CharacterId` + `RotmgCharId`):**
  - Each character has a primary database key (`Id` / `character_uuid`) mapped to the in-game `CharId` from `LOAD` / `CREATE_SUCCESS` packets.
  - Supports multiple identical classes simultaneously:
    - Auto-incremented class slot numbering (e.g., `Archer #1`, `Archer #2`, `Archer #3`).
    - Optional player-defined alias (e.g., `PPE Archer`, `O3 Rusher`).
    - Creation timestamp, equipped `SkinId` (for sprite rendering), and 4 equipped item IDs (`[Weapon, Ability, Armor, Ring]`).
- **Data Immutability on Death:**
  - Character death NEVER deletes character records or associated loot/dungeon history.
  - Upon receiving a `DEATH` packet (or manual RIP), `IsAlive` is set to `false` and the character moves to the **Graveyard**.
  - All historical drops retain the metadata: *"Dropped by: [Class #Slot - Alias (X/8)]"*.
- **Graveyard Format (RealmEye Standard):**
  - Class, Skin sprite, and maxed stats status (`X/8`, e.g., `8/8`, `4/8`).
  - Fame breakdown: **Base Fame** (alive fame) vs **Total Death Fame** (including death bonuses).
  - Exact 4-slot equipment worn at the moment of death (rendered as item sprites).
  - Death timestamp, server name, dungeon/location, and killer entity (`KilledBy`).

### B. Passive Sniffer & Loot Registration
- **Nexus Account Detection:**
  - Automatically detects the local player in the Nexus via `CREATE_SUCCESS` and `UPDATE` packets.
  - Prompts the user once (*"Joined as [Nick] ([Class]). Is this your account?"*) and links the account in SQLite.
- **Anti-Rescan & Anti-Drop Protection:**
  - Tracks `(BagObjectId, SlotIndex, ItemId)` per map instance so walking away and returning to a bag never duplicates a drop.
  - Ignores bags spawned within 2 seconds of an outgoing `INVDROP` packet (items dropped by the player from inventory).
- **Rich Bag & Drop Metadata:**
  - `BagInstanceId`: Groups multiple items dropped inside the same physical bag into a single visual card.
  - `CombatSessionId`: Links the dropped bag to the specific boss fight DPS log (`CombatSessions`).
  - `DroppedAt` timestamp (date, hour, minute, second).
  - `ServerName` (e.g., `EUWest`, `USEast`) and `DungeonName` (with `DungeonPortalObjectType` for portal sprite).
  - `BossName` and `BossObjectType` (matched via `(X, Y)` bag spawn coordinates and boss combat tracking, ready for boss sprite rendering).
  - `CharacterId`, `CharacterSkinId`, `MaxedStats` (`X/8`), and exact `BaseFameAtDrop` a split-second before the drop.
  - `PhysicalBag` (`BagObjectType`: White, Orange, Red, Cyan, Blue, Purple, Brown) vs analytical `DropType` (`LegendaryWhite`, `RegularWhite`, `EventWhite`, `ST`, `UT`, `Shiny`, `Shivine`, `BPs`, `Tops`, `Tiered`).

---

## 2. Progression Modules: Dungeon Counter, Quests & Item Collection

### A. Season Dungeons Completed Counter
- Automatically logs dungeon completions upon boss defeat (`DAMAGE` packet with kill flag on boss entity / completion trigger).
- Breakdown filters:
  - By **Season** (`SeasonTag`).
  - By **Class** and specific **Character Instance** (e.g., `Archer #1` vs `Archer #2`).
  - **All-Time** global totals.
- Direct integration with **Target Wishlist**: calculates exact dungeon runs completed since a wishlist target was created.

### B. Automated Daily Quest Tracker (Tinkerer)
- Tracks required `Mark of ...` items with mark sprites and reward chest sprites.
- Automatically updates progress when marks are detected in loot bags, inventory, or Vault.
- **Interactive Tooltips:** Hovering over a mark displays the target Boss, Dungeon, and the Realm Biome / Event god that drops the dungeon portal.

### C. Item Collection & Checklist
- **Tiered Item Filters:**
  - Toggle checkbox: *"Include Tiered Items"* (default: off/configurable).
  - Granular exclusion filters (e.g., hide weapons/armors below `T12`, hide abilities below `T6`, hide rings below `T5`).
- **Focus Categories:** White Bags, ST Sets, UTs, Event Whites, and Shiny variants.
- **Variant & Duplicate Tracking:**
  - Displays how many times each item (and each rarity/slot roll) has been obtained on the account.
  - Highlights missing items and missing Shiny / Rarity variants.

---

## 3. Rarity Score Engine

Point-based algorithm evaluating item uniqueness for sorting the **Trophy Case**:
1. **Base Item Quality:** `Shivine`, `EventWhite`, `LegendaryWhite`, `DivineGoodItem`, `RegularWhite`, `Shiny`, `UT`, `ST`, `BPs`, `Tops`.
2. **Item Rarity / Enchantment Slots (`0..4`):**
   - `Common` (0 slots): `1.0x`
   - `Uncommon` (1 slot): `1.15x`
   - `Rare` (2 slots): `1.35x`
   - `Legendary` (3 slots): `1.75x`
   - `Divine` (4 slots): `2.50x`
3. **Shiny Modifier:** `3.0x` multiplier for Shiny sprite variants.
4. **In-Game Grave Difficulty Multiplier (`1.0 – 10.0` Graves):**
   - Uses the official in-game dungeon grave difficulty rating extracted from `<Portal>` XML and `MAPINFO` packets (e.g., `10.0/10` Shatters, `9.5/10` Moonlight Village / O3) — never scaled boss HP or item FeedPower.
5. **Non-Punitive Duplicate Stacking (Dupe Factor):**
   - Intrinsic item score never decreases when obtaining multiple endgame items.
   - Identical items stack into a single Trophy Case card (`[xN]`) showing the best rarity roll with a small collection bonus (`+5%` per duplicate, up to `+25%`).

---

## 4. User Interface & Visual Architecture (UI / UX)

### A. Left Navigation Sidebar (with Game Sprites)
1. **Dashboard** (Main Overview)
2. **Showcase & Mini-Profile** (Trophy Case & Top Characters)
3. **Loot History** (Sprite-rich Bag Cards & Filters)
4. **Live Dungeon DPS & Combat History** (Real-time Damage Meter & Boss Fight Logs)
5. **Counters** (Loot & Dungeon Completion Analytics)
6. **Item Collection** (Checklist with Tier/Category Filters)
7. **Target Wishlist** (Goal Planner & Run Counter)
8. **Quests & Chest Openings** (Automated Tinkerer Marks & Chest Ratio Analyzer)
9. **Graveyard** (Fallen Characters in RealmEye Format)
- *Global Header Controls:* Season Switcher, Character Selector, and Passive Sniffer Status.

### B. Visual Overhaul & Sprite-Rich Drop Cards
- Every drop card displays a compact, visual progression flow using extracted game sprites:
  - `[Timestamp & Base Fame]` ➔ `[Physical Bag Sprite]` ➔ `[Character Skin Sprite]` ➔ `[Dungeon Portal Sprite + Graves]` ➔ `[Boss Entity Sprite]` ➔ `[Sprites of All Items in Bag + Rarity Frame]` ➔ `[⚔ View Fight DPS Button]`.
- Hovering over any item in the **Trophy Case** or **Loot History** opens a RotMG-styled tooltip with rarity border, enchantment slots, Shiny badge, character owner (`Archer #1 - 8/8`), dungeon, boss, server, and timestamp.