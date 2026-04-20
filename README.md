# ShipCleaner

**Automatically sorts your ship's scrap and equipment with a single hotkey press.**

Tired of wading through a pile of loot after every run? ShipCleaner organizes everything on the ship into a clean, value-sorted grid — so you always know where your most valuable scrap is, and your tools are always where you left them.

---

## Features

- **One (configurable) hotkey sorts everything** — Press `,` (comma) by default to sort the entire ship instantly. Rebind it anytime from the controls menu.
- **Scrap sorted by value** — Most valuable items are placed first so you can find them at a glance.
- **Tools sorted into the Storage Closet** — Flashlights, keys, weapons, consumables, and the shovel each go to their own shelf.
- **Two-handed items get their own zone** — Large scrap (engines, axles, etc.) is placed near the ship doors, separate from one-handed scrap.
- **Same-type items stack** — Duplicate scrap occupies the same grid cell with a slight random rotation so you can tell them apart.
- **Dynamic furniture avoidance** — Sorted items will not be placed on top of or inside your purchased furniture, regardless of where you've moved it.
- **Closet-relative shelf layout** — Tools and equipment will always find their way into the Storage Closet regardless of where it has been moved on the ship.
- **Special item handling** — Yield signs and Stop signs get their own dedicated column out of the way. Certain crew-use items are intentionally left in place and not sorted. Most items from other mods should be compatible automatically.
- **Host-only** — Only the host needs to press the hotkey. Clients will see the sorted layout automatically via normal game sync.

---

## Installation

Install via [r2modman](https://thunderstore.io/c/lethal-company/p/ebkr/r2modman/) or the Thunderstore app. All dependencies are handled automatically.

**Manual install:** Drop `ShipCleaner.dll` into `BepInEx/plugins/ShipCleaner/`.

---

## Usage

1. Load into a game as **host**.
2. Press `,` (comma) to sort the ship.
3. All scrap and tools will be arranged automatically.

The hotkey can be rebound in-game via the controls menu (requires [LethalCompanyInputUtils](https://thunderstore.io/c/lethal-company/p/Rune580/LethalCompanyInputUtils/)).

---

## Configuration

ShipCleaner can be configured externally by config file, or in-game with [LethalConfig](https://thunderstore.io/c/lethal-company/p/AinaVT/LethalConfig/) (optional).

**Sorting Mode** — Scrap, Tools & Equipment / Scrap Only / Tools & Equipment Only.
Controls what gets sorted when the hotkey is pressed.

**Storage Closet Usage** — Enabled / Disabled.
When disabled, tools are placed on the floor along the closet's default wall instead of inside it. Useful for players who prefer floor-only organisation.

---

## Scrap Grid Layout

```
  Ship Doors Side
  |______==______|
  |              |
  |   2H Zone    |
  |--------------|
  |              |
  |              |
  |   1H Zone    |
  |              |
  |______________|
  Ship Controls Side
```

---

## Known Limitations

- **Host only** — Clients cannot trigger the sort. The host's sorted layout syncs to all players automatically.
- **Modded items** — Unrecognised modded tools will sort as scrap unless their name contains a known keyword.
---

## Credits

Developed by [Yakka_Productions](https://thunderstore.io/c/lethal-company/p/Yakka_Productions/) — [GitHub](https://github.com/Yakka-2k/ShipCleaner)
Built with [BepInEx](https://github.com/BepInEx/BepInEx) and [LethalCompanyInputUtils](https://thunderstore.io/c/lethal-company/p/Rune580/LethalCompanyInputUtils/).

Inspired by mods such as:
- [ShipSort](https://thunderstore.io/c/lethal-company/p/baer1/ShipSort/) by baer1 — ([GitHub](https://github.com/baer1/ShipSort))
- [QuickSort](https://thunderstore.io/c/lethal-company/p/asta/QuickSort/) by asta — ([GitHub](https://github.com/P-Asta/lc-quicksort))
- [ShipMaid](https://thunderstore.io/c/lethal-company/p/bozzobrain/ShipMaid/) by bozzobrain — ([GitHub](https://github.com/bozzobrain/LethalCompanyShipMaid))

---

## Changelog

### 1.0.0
- Initial release.
