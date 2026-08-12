# Known issues, gaps and dead code

Observations from reading the current tree. Nothing here has been reproduced in game — these
are defects and gaps visible in the source, listed so they are not rediscovered from scratch.

> Several of these are now **confirmed by tests** in the isolated simulation environment, along
> with model-level and performance findings that only became visible once the code could be run
> outside the game. See [bugs-and-performance.md](bugs-and-performance.md) for the tested list
> and a suggested order of work.
>
> **Status.** Entries below that name `ThermalCell`, `ThermalGrid*` or `MyFreeList` describe
> files that no longer exist: the live mod runs `Core/` behind the adapter in `Game/`. They are
> kept for the history of what went wrong and why, not as a description of the current tree.
> Two of them are closed by that change on their own: `Settings.Load()` is now called from
> `Session.Init`, and the per-cell surface logging is gone with the mapper that emitted it.

## Defects

### ~~Aerodynamic friction never heats anything~~ — fixed

[ThermalCell.cs:443](../Data/Scripts/Thermodynamics/ThermalCell.cs#L443)

`DeltaFriction` was added to `DeltaTemperature`, which the conduction sum then reassigned before
it was used, so friction heating was inert and `FrictionAtSpeedsAbove` had no gameplay effect.
`Temperature` now takes `DeltaRadiation + DeltaFriction`, and the dead accumulation is gone.

### ~~`RemoveNeighbor` can throw~~ — fixed

[ThermalCell.cs:409](../Data/Scripts/Thermodynamics/ThermalCell.cs#L409)

The second guard tested `i` rather than `j`, so an asymmetric neighbour list called
`RemoveAt(-1)` and threw `ArgumentOutOfRangeException` instead of skipping. The guard now tests
`j`.

### ~~Door state handlers are never unsubscribed~~ — fixed

[ThermalGridMapper.cs:111](../Data/Scripts/Thermodynamics/ThermalGridMapper.cs#L111)

`-=` on a freshly-allocated lambda removed nothing, and the handler called the method that
subscribes it, so the handler count doubled on every door cycle. `ThermalGrid` now keeps the
handler in `DoorStateHandlers`, keyed by the door's `EntityId`: it subscribes once per door and
detaches the stored delegate on removal.

### Block variant group references a subtype that does not exist

[BlockVarientGroups.sbc](../Data/CubeBlocks/BlockVarientGroups.sbc)

The large-grid coolant group lists `Gauge_LG_CoolantPipe_Straight_Sink`, but the definition is
`Gauge_LG_CoolantPipe_Straight_SingleSink` — the small-grid group uses the correct name. The
large-grid single-sink straight pipe is therefore missing from its variant group.

### `RecentlyRemoved` grows without bound

[ThermalGrid.cs:24](../Data/Scripts/Thermodynamics/ThermalGrid.cs#L24)

Every removed block's temperature is recorded so that a grid split can restore it. Entries are
only removed when a split actually claims one, so on a long-lived grid the dictionary grows for
the lifetime of the world.

### Saving perturbs the running simulation

[ThermalGridStorage.cs:46](../Data/Scripts/Thermodynamics/ThermalGridStorage.cs#L46)

`Pack()` truncates each temperature to `short` and writes the truncated value back onto the live
cell (`c.Temperature = t;`), so every autosave snaps the whole grid to whole Kelvin. Both
`Pack` and `PackLoops` carry a `TODO` acknowledging this.

### Loop temperatures are saved by list index

[ThermalGridStorage.cs:82](../Data/Scripts/Thermodynamics/ThermalGridStorage.cs#L82)

`PackLoops` keys on the loop's position in `ThermalLoops`, which is rebuilt by the crawler on
load in whatever order blocks are added. Two loops on the same grid can swap temperatures
across a reload. The index is also written as a single byte, capping a grid at 256 loops.

### Coolant loops have no duplicate detection

[ThermalGridLoop.cs:68](../Data/Scripts/Thermodynamics/ThermalGridLoop.cs#L68)

`OnAddDoCoolantCheck` runs a fresh crawl for every pipe placed and appends any closed ring it
finds, without checking whether those cells already belong to a registered loop. Conversely,
`OnRemoveDoCoolantCheck` deletes only the first loop containing the removed cell.

### ~~Surface-state logging is on in the hot path~~ — fixed

[ThermalGridMapper.cs:321](../Data/Scripts/Thermodynamics/ThermalGridMapper.cs#L321)

`CalculateBlockSurfaceStates` wrote one log line per *cell* per block placement, and
`CreateRoom` one per room per crawl, both unguarded by any debug setting. Both are now commented
out alongside the other debug logging in that file.

## Unfinished systems

| Area | State |
| --- | --- |
| **Config file** | `Settings.Load()` / `Save()` are complete but never called; defaults are always used. See [configuration.md](configuration.md). |
| **Multiplayer** | `SENetworkAPI` is initialised with channel `30323` but no commands or `NetSync` properties are registered. Nothing is replicated; damage is applied with `sync: false` on each machine independently. |
| **Heat pump** | `Gauge_LG_HeatPump` / `Gauge_SG_HeatPump` have models, icons, definitions and thermal properties, but no C# implementation. Both also log a `MOD_ERROR` at load: they are `TypeId` `CubeBlock` yet list a `Computer` component, so the game reports that they can be owned but their ownership cannot be changed from a terminal. Resolve it when the block is implemented — most likely by moving them to `TerminalBlock`, which changes their `Id` and so drops any already placed in a save. |
| **Room air temperature** | The mapper detects sealed rooms (`Rooms[2+]`), but only `Rooms[0]` (external) is consumed. Interior air has no temperature of its own. |
| **Per-block solar shadowing** | An entire implementation exists, fully commented out, in [ThermalGridSolar.cs](../Data/Scripts/Thermodynamics/ThermalGridSolar.cs). Occlusion today is per grid, all-or-nothing. |
| **Underground core heating** | `PlanetDefinition.CoreTemperature` and `SealevelDeadzone` are parsed but unused; `PrepareEnvironmentTemprature` ends with `//TODO: implement underground core temparatures`. |
| **Landing gear conduction** | Written and commented out in [ThermalCell.cs:130](../Data/Scripts/Thermodynamics/ThermalCell.cs#L130), with a note that the intended API is broken. Pistons and rotors do conduct. |
| **Grid-wide heat generation total** | `GridHeatGeneration` / `CurrentGridHeatGeneration` are commented out in `ThermalGrid` and `ThermalCell`. |

## Dead and inert code

| Item | Note |
| --- | --- |
| `ThermalRadiationNode` | Referenced only by a commented-out field in `ThermalGrid`. |
| `MyFreeList<T>` | Superseded by `ThermalCellArray` in the same file; unreferenced. |
| `ThermalCell.DeltaConvection` | Never assigned. The debug HUD prints it, so `dC` always reads `0.000`. |
| `Tools.DirectionToIndex` / `IndexToDirection` / `IndexToDirectionI` | Unused, and they use an axis-sign ordering that does **not** match `ThermalGrid.Directions` (index 0 is `+X`, not `Forward`). Mixing them with mapper code would silently corrupt face indices. |
| `Tools.FindTouchingSurfaceArea` | Superseded by `ThermalCell.FindSurfaceArea`, which uses mount points. |
| `Tools.IsSolarOccluded` | Superseded by the inline planet test in `PrepareSolarEnvironment`. |
| `Tools.KelvinToFahrenheit(String)` | No caller; the HUD is Celsius-only. |
| `ThermalCellArray.Compact` / `GetByIndex` | No callers. Removed cells leave permanent `null` holes in the array. |
| `Settings.DebugTextureColors` | Compile-time `const`, unused. |
| `_gridPool` loop in `PrepareSolarEnvironment` | [ThermalGridEnvironment.cs:195](../Data/Scripts/Thermodynamics/ThermalGridEnvironment.cs#L195) — collects connected grids and then loops over them doing nothing but `continue`. Presumably meant to skip occlusion by physically-connected grids. |

## Behaviour worth knowing

* **Damage rate scales with `Frequency`.** `HandleCriticalTemperature` runs on every cell
  update, so doubling `Frequency` doubles damage per real second at the same overtemperature.
* **`Flatten` assumes a 1024-unit stride.** Blocks further than ±512 cells from origin on the X
  or Y axis will collide in the position→index map.
* **Exposed face counts saturate at 31 per direction**, because of the 5-bit packing in
  `ThermalCell`. Only relevant for very large single blocks.
* **With `EnableSolarHeat` off, day/night stops working.** `FrameSolarDirection` is only set
  inside `PrepareSolarEnvironment`, so it stays zero, and the day/night dot product in
  `PrepareEnvironmentTemprature` always evaluates to the midpoint between `NightTemperature`
  and `DayTemperature`.
* **`FrameSolarOccluded` is grid-wide.** One asteroid clipping the sun line shades the entire
  ship, including faces nowhere near it.
