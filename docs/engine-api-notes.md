# Engine API notes

A survey of the Space Engineers assemblies in
`~/Steam/SteamLibrary/steamapps/common/SpaceEngineers/Bin64`, looking for things this mod either
reimplements by hand or could be taking advantage of.

Every API quoted here was **compile-verified**: a probe file using all of it builds at
`LangVersion 6` / `net48` against the real DLLs, so the namespaces, accessibility and signatures
are correct as written. Where behaviour rather than existence is in question, that is called out.

---

## Build corrections

The mod now compiles against the real assemblies with **zero warnings**. Two things were wrong in
`Generic.csproj`:

| Problem | Fix |
| --- | --- |
| `VRage.Native.dll` is an **unmanaged** PE — referencing it produced `MSB3246: PE image does not have metadata`. | Reference removed. |
| The project targeted `net472`, but `VRage.Platform.Windows` (and its RestSharp dependency) target **.NET Framework 4.8**, so they never resolved. | `TargetFramework` raised to `net48`. |

Reference points:

* Game assemblies live in `Bin64`; 84 DLLs, of which the managed ones are `VRage*`, `Sandbox*`
  and `SpaceEngineers*`.
* XML documentation ships for `Sandbox.Common`, `Sandbox.Game`, `SpaceEngineers.Game`,
  `VRage`, `VRage.Game`, `VRage.Library`, `VRage.Math` and `VRage.Scripting` — worth pointing
  your IDE at, it gives IntelliSense docs for the whole modding surface.
* `Pulsar/` alongside `Bin64` is a third-party loader, not part of the game's own assemblies.
  Build against `Bin64`.
* Mod scripts are compiled by `VRage.Scripting.MyScriptCompiler` against a whitelist with three
  targets (`MyWhitelistTarget.Ingame`, `.ModApi`, `.Both`). Everything below is `ModApi`
  surface — it is reachable from `Data/Scripts`, unlike the in-game programmable block sandbox.

---

## 1. The game already computes airtight rooms — **highest value**

`ThermalGridMapper` is 725 lines that classify every cell as external, sealed structure, or an
interior room, restarting the whole flood fill on every block change. The engine already does
this for pressurisation, incrementally, and exposes it on the mod API:

```csharp
IMyGridGasSystem gas = grid.GasSystem;              // VRage.Game.ModAPI.IMyCubeGrid

List<IMyOxygenRoom> rooms = new List<IMyOxygenRoom>();
gas.GetRooms(rooms);

Vector3I cell = block.Position;
IMyOxygenRoom room = gas.GetOxygenRoomForCubeGridPosition(ref cell);   // null outside any room

bool sealed_    = room.IsAirtight;
float oxygen    = room.OxygenLevel(grid.GridSize);
float outside   = room.EnvironmentOxygen;
HashSetReader<Vector3I> cells = room.Blocks;

gas.OnProcessingDataComplete += RefreshExposure;    // same signal as SurfaceCheckComplete
bool busy = gas.IsProcessingData;
gas.ForcePressurize = true;
```

The engine's own airtightness test is public too — the logic
`CalculateBlockSurfaceStates` reimplements, including every door special case:

```csharp
bool airtight = MyGridGasSystem.IsAirtightBlock(block, cell, normal);   // Sandbox.Game.GameSystems
```

What this could replace: the surface bit flags, the external flood fill, the room detection, and
the per-block `BeginCrawl` restart that is finding **P1** in
[bugs-and-performance.md](bugs-and-performance.md). `GetOxygenRoomForCubeGridPosition` is an O(1)
lookup where the mod currently maintains its own dictionary and queue machinery.

It also unlocks something the mod cannot currently model at all: `OxygenLevel` and
`EnvironmentOxygen` per room mean **interior air can have a temperature and convect**, instead of
sealed interior faces simply dropping out of the exposed count.

**Verify in game before committing to it:**

* Whether room data is populated when `EnableOxygenPressurization` is off in world settings, and
  exactly what `ForcePressurize` does about that. There must be a fallback — the extracted
  `RoomMapper` in `Core/` is that fallback, and it is already tested.
* Whether "not in any room" reliably means "open to space" for the exposed-surface rule. The
  gas system cares about pressure, the thermal model cares about line of sight to vacuum; they
  agree in the normal case but may differ for open lattice blocks.

## 2. Parallelism is available to mods

`ThermalGridSimulation.ProcessCellsSequentially` carries the comment *"parallel processing not
available in Space Engineers"*. That is not correct:

```csharp
MyAPIGateway.Parallel.For(0, nodeCount, i => { /* ... */ });
MyAPIGateway.Parallel.Start(work, completionCallback);
MyAPIGateway.Utilities.InvokeOnGameThread(action, "Thermodynamics", 0, 1);
```

`VRage.Game.ModAPI.IMyParallelTask` offers `For`, `ForEach`, `Do` and `Start`, backed by
`ParallelTasks` in `VRage.Library`.

This matters because of how the rewritten solver works: it accumulates watts from
start-of-step temperatures and applies them together, so the pass has no order dependence and is
safe to split across threads. Reads must not race with the game mutating the grid, so the natural
split is *solve in parallel, apply on the game thread* via `InvokeOnGameThread`.

Worth measuring before adopting — 8 000 blocks currently solve in 0.128 ms/step, which may
already be below the threshold where thread hand-off pays for itself.

## 3. Planets already describe their own climate

[Data/Planets.xml](../Data/Planets.xml) defines only the `DefaultThermodynamics` fallback, so
**every planet in the game is treated as Earthlike** — 283–294 K, `SolarDecay` 0.5. The vanilla
planet definitions already say otherwise:

| Planet | `DefaultSurfaceTemperature` | `SolarRadiationProtectionFactor` | `Atmosphere.Density` | `MaxWindSpeed` |
| --- | --- | --- | --- | --- |
| EarthLike | *(unset)* | 1.8 | 1.0 | 80 |
| Alien | *(unset)* | 1.8 | 1.2 | 80 |
| Mars | *(unset)* | 0.2 | 1.0 | 80 |
| Europa | ExtremeFreeze | 0 | *(none)* | 30 |
| Triton | ExtremeFreeze | 1.8 | 1.0 | 80 |
| Pertam | Hot | 0.75 | 1 | 80 |

All reachable at runtime:

```csharp
MyPlanetGeneratorDefinition generator = planet.Generator;      // Sandbox.Definitions

MyTemperatureLevel level   = generator.DefaultSurfaceTemperature;   // ExtremeFreeze .. ExtremeHot
float solarProtection      = generator.SolarRadiationProtectionFactor;
float surfaceGravity       = generator.SurfaceGravity;

MyPlanetAtmosphere air     = generator.Atmosphere;
float density              = air.Density;
float maxWind              = air.MaxWindSpeed;
float oxygenDensity        = air.OxygenDensity;
bool breathable            = air.Breathable;
float limitAltitude        = air.LimitAltitude;

double altitude            = planet.GetHeightFromSurface(position);   // for the unfinished
float localDensity         = planet.GetAirDensity(position);          // underground/core model
float windSpeed            = planet.GetWindSpeed(position);
float localOxygen          = planet.GetOxygenForPosition(position);
```

The obvious move: derive `PlanetThermalProperties` from the planet's own definition, and let a
`ThermalPlanetProperties` ModExtensions group override it. Pertam runs hot and Europa freezes
without anyone authoring a line of XML, and modded planets get sensible defaults for free.

`SolarRadiationProtectionFactor` is the engine's version of the mod's `SolarDecay`, and
`GetHeightFromSurface` is what the `//TODO: implement underground core temparatures` needs.

## 4. Block mass and build state can be tracked

`ThermalCell.PrecalculateVariables` reads `Block.Mass` once at construction and never again, so a
half-built or heavily damaged block keeps the thermal mass of a complete one.

```csharp
float mass       = block.Mass;
float buildRatio = block.BuildLevelRatio;   // 0..1 while welding
float integrity  = block.Integrity;
float maxIntegrity = block.MaxIntegrity;
float damageRatio  = block.DamageRatio;
bool destroyed     = block.IsDestroyed;
```

with the event to hang it on:

```csharp
grid.OnBlockIntegrityChanged += slim => node.RefreshThermalMass();
```

`ThermalNode.RefreshThermalMass()` in `Core/` already exists for exactly this; it just needs
wiring.

## 5. Damage should go through the damage system properly

The mod calls `Block.DoDamage(amount, hash, sync: false)`, so every machine computes and applies
its own damage independently. The full signature supports server-authoritative damage:

```csharp
block.DoDamage(amount, MyStringHash.GetOrCompute("Thermal"), sync: true, hitInfo: null, attackerId: 0L);
```

and the session exposes handlers so *other* mods can see, scale or veto thermal damage:

```csharp
IMyDamageSystem system = MyAPIGateway.Session.DamageSystem;
system.RegisterBeforeDamageHandler(0, (object target, ref MyDamageInformation info) => {
    if (info.Type == MyStringHash.GetOrCompute("Thermal")) info.Amount *= 0.5f;
});
system.RegisterAfterDamageHandler(0, (target, info) => { });
system.RegisterDestroyHandler(0, (target, info) => { });
```

Registering a documented `Thermal` damage type is what lets shield and armour mods interoperate
with this one instead of fighting it.

## 6. Grid events that coalesce

The mod restarts its room crawl from `OnBlockAdded` / `OnBlockRemoved`, i.e. once per block. The
engine offers a settled-state event:

```csharp
MyCubeGrid.OnBlocksChangeFinishedGlobally += (a, b) => { };   // note: static, fires for all grids
grid.OnConnectionChangeCompleted += (g, link) => { };          // better than piston/rotor hooks
grid.OnGridBlockDamaged += (slim, damage, hit, attacker) => { };
```

`OnBlocksChangeFinishedGlobally` being static means one subscription for the whole session, with
the affected grids passed as arguments — a natural place to trigger one coalesced rebuild per
grid per batch of changes.

## 7. Cheaper update cadence

`ThermalGrid.Init` requests `MyEntityUpdateEnum.EACH_FRAME` for every grid, so every grid runs
scheduling logic 60 times a second to perform 4 solver steps. The enum has coarser options:

```csharp
NeedsUpdate = MyEntityUpdateEnum.EACH_10TH_FRAME;    // 6 Hz, enough for Frequency = 4
// also: EACH_100TH_FRAME, EACH_FRAME_AFTER, SIMULATE, BEFORE_NEXT_FRAME
```

At the default `Frequency = 4` a grid needs servicing 4 times a second; `EACH_10TH_FRAME` gives 6.
That is a 10× cut in per-grid dispatch overhead for free, and it composes with the
`SimulationScheduler` in `Core/`, which already accumulates fractional step credit and therefore
does not care how often it is polled.

## 8. Terminal readout without a HUD dependency

The mod requires Rich HUD Master for all of its on-screen text. Per-block information has a
first-party route:

```csharp
terminalBlock.AppendingCustomInfo += (b, sb) => sb.Append("Temperature: 412 C\n");
terminalBlock.RefreshCustomInfo();
```

That puts temperature in the terminal detail panel for any functional block, with no dependency —
which is why the terminal readout works in a world with no HUD framework at all. It is also the only
route that takes a paragraph: `IMyTerminalControlTextbox` is a one-line editable field and clips
anything longer, whatever its content. The framework is
still the right tool for the always-on cockpit overlay, but the per-block readout does not need it.

`MyAPIGateway.TerminalControls` is also available if you want a per-block thermal limit slider or
a "vent coolant" action.

## 9. The Definition Extensions dependency is optional

Mods can register their own definition types, so the thermal properties could be read from the
mod's own SBCs without a third-party mod in the load order:

* `VRage.Game.Definitions.MyDefinitionTypeAttribute`
* `VRage.ObjectBuilders.MyObjectBuilderDefinitionAttribute`

The ModExtensions approach has a real advantage though — *other* mods can annotate their blocks
without depending on this one — so this is a trade, not a straight win. Worth noting that a hard
dependency currently means a missing Definition Extensions install throws inside
`ThermalCellDefinition.GetDefinition` rather than degrading.

## 10. Richer weather data

**Partly taken up.** The mod now uses `MyVisualScriptLogicProvider.GetWeather(position)`, which
returns the effect's subtype name and is on the same whitelisted class as the intensity call it
already used. That is enough to distinguish a dust storm from rain, and
[WeatherResponse](../Data/Scripts/Thermodynamics/Core/Definitions/WeatherResponse.cs) turns the name
into a temperature offset, a solar multiplier, a wind multiplier and a convective multiplier.

The response table's figures are **transcribed** from Keen's `WeatherEffects.sbc`, not read from it.
The definition type is there —

```csharp
Sandbox.Definitions.MyWeatherEffectDefinition   // Sandbox.Game.dll
  .WindOutputModifier / .SolarOutputModifier / .TemperatureModifier / .OxygenLevelModifier
```

— and reading it through `MyDefinitionManager` would pick up modded weathers' own authored numbers
instead of falling back on a word match. It was not done because a whitelist rejection is a
*compile* failure at world load rather than a catchable exception, so there is no way to try it and
degrade gracefully. Worth confirming in a throwaway mod before adopting.

`MySectorWeatherComponent` has more still, including the game's own resolved multipliers and the
sun's rotation period:

```csharp
MySectorWeatherComponent.GetSolarMultiplier(position);        // instance
MySectorWeatherComponent.GetTemperatureMultiplier(position);  // instance
MySectorWeatherComponent.GetWindMultiplier(position);         // instance
MySectorWeatherComponent.RotationInterval                     // the sun's period
```

All instance members on a session component in `Sandbox.Game.SessionComponents`, with the same
whitelist question. `RotationInterval` is the interesting one: it would let `AmbientLagSeconds` be
expressed as a share of a day rather than as absolute seconds, which is the outstanding defect in
[planet-climate.md](planet-climate.md#open).

---

## Things that are not there

* **No engine-side temperature.** `MyTemperatureLevel` is a five-value classification for
  planet definitions, not a simulation. Nothing in the game tracks block temperature; this mod
  remains the source of truth.
* **No generic per-block emissive helper on the mod API.** `SetEmissiveState*` exists on concrete
  `MyCubeBlock` subclasses in `Sandbox.Game`, not on `IMyCubeBlock`. Making hot blocks glow
  instead of repainting them needs either the concrete types or a billboard overlay like the
  extinguisher tool and the block overlay already draw. `ColorBlocks` is no longer used anywhere
  in the mod: it overwrote players' paint permanently.
* **`MySessionComponentBase` has no 10th/100th-frame hooks** — only
  `UpdateBeforeSimulation`, `Simulate`, `UpdateAfterSimulation` and `Draw`. The coarser cadences
  are entity-component only, via `MyEntityUpdateEnum`.

---

## Re-running the survey

The survey used a throwaway reflection tool over `Bin64` built on
`System.Reflection.MetadataLoadContext`, plus a probe file compiled against the real DLLs. To
repeat it, point `MetadataLoadContext` at every DLL in `Bin64`, load the `VRage*` / `Sandbox*` /
`SpaceEngineers*` assemblies, and enumerate. The XML documentation files next to those DLLs cover
most of the same ground if you would rather read than reflect.

The single most useful habit: before hand-rolling a subsystem, grep the ModAPI namespaces
(`VRage.Game.ModAPI`, `Sandbox.ModAPI`) for it. The room mapper is 725 lines of code that the
engine was already computing.

## Entity updates are NOT staggered across frames — measured

`ThermalGrid` polls on `UpdateBeforeSimulation10`, and the question is whether every grid in the
world lands on the same frame.

**They do.** A field run of a 203-grid save measured it directly: of 13,915 frames, **1,392 did
any thermal work at all** — one in ten, exactly — and every one of the worst frames reports **183
grids** on it. Those frames averaged 117 ms with a worst of 611 ms, and two in three exceeded a
60 fps frame. See the frame cost section of [telemetry.md](telemetry.md#frame-cost-and-hitching)
for where those numbers come from.

> **An earlier version of this section said the opposite**, on the strength of reading
> `MyDistributedTypeUpdater<MyEntity>(10)` in `VRage.Library`: it computes
> `m_step = ceil(Count / UpdateInterval)` and its enumerator walks `[m_updateIndex, m_updateIndex +
> m_step)`, which is a stagger. Whatever that machinery does in this build, the observable
> behaviour of `UpdateBeforeSimulation10` on grid entities is that they all fire together, and the
> measurement is what counts. The reading was plausible and wrong, and it was nearly the basis for
> deciding no fix was needed.

The mod therefore paces its own grids: see `ThermalGridScheduler`. Every grid ticks on every
frame, and each does its share of the solver step it is part way through — a fifteenth of it at
`Frequency 4`. Bucketing was the first answer and was not enough: it spreads the grids across the
cycle but not the work inside any one of them, so a single large grid on its own frame stayed a
stutter. Spreading the step itself subsumes it, and the buckets are gone.

## `MyCubeGrid` clears `EACH_FRAME` from its own update flags

The obvious way to pace a component per frame is to ask the entity for `EACH_FRAME` and gate on a
phase inside the callback. On a cube grid that does not work:

```csharp
base.NeedsUpdate &= ~MyEntityUpdateEnum.EACH_FRAME;      // MyCubeGrid, on its own schedule
```

`MyCubeGrid` drives its scheduled work off `EACH_FRAME` and **clears the flag when its queue
empties**, re-arming it only when the queue goes from empty to non-empty. A mod hanging its
cadence on that flag stops running, silently, whenever the grid has nothing of its own to do.

This is the same property recorded under "never assign `NeedsUpdate`" in
[known-issues.md](known-issues.md) — that entry is about not *clearing* the grid's flags, and this
is the other half: not *depending* on them either. Anything that must happen on a schedule the mod
controls belongs on the session component's per-frame call, which nothing else edits.

