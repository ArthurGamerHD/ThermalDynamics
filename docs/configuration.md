# Configuration

Settings live in `ThermodynamicsConfig.cfg` in world storage, written with defaults the first time
a world loads and regenerated when `Version` does not match.

Every setting can also be changed while the world is running. A change is written into the settings
object every grid already holds and picked up on the next step: heat capacities are rescaled,
coolant loops and room air are rebuilt, and each mechanism reads its own switch. Nothing is written
to disk unless asked.

## Runtime control

| Command | Effect |
| --- | --- |
| `/thermal status` | Collection state, sample stride, live grids, block models, bridges. |
| `/thermal settings` | Every setting and its current value. |
| `/thermal set <name> <value>` | Changes one setting for this session. Switches take `on`/`off` or `1`/`0`. |
| `/thermal save` | Writes the current values to the config file. |
| `/thermal overlay` | Cycles the block overlay. Same as Ctrl+Shift+=. |
| `/thermal menu` | Opens the settings menu. Same as Ctrl+Shift+S. |
| `/thermal telemetry on` / `off` | Starts and stops data collection. |
| `/thermal stride <n>` | Telemetry sample stride. |
| `/thermal dump` | Writes a telemetry report without closing the world. |

Settings are server side; `set` from a client is refused. The same names are reachable from other
mods — see [api.md](api.md#settings).

## The settings menu

**Ctrl+Shift+S** opens it, as does `/thermal menu`. It is built on the [Rich HUD
Framework](https://github.com/ZachHembree/RichHudFramework.Client) and needs the **Rich HUD Master**
mod (`1965654081`) to be enabled; without it the keystroke says so and the chat commands remain the
way in.

**Save to config file** and **Reset everything to defaults** sit at the top, one of each for the
whole file. Below them are titled rows — heat transfer, solar, solar occlusion, ship systems, solver,
environment, display — each holding its settings in two columns. Every value in the config file has a
control and carries that setting's description; switches are checkboxes, numbers are sliders with a
range chosen for what is worth dragging to, and a setting that picks between behaviours is a named
dropdown.

Two columns because that is what the page is wide enough for: the framework's tiles are a fixed
300x250, so a third column would have to be scrolled to sideways.

Changes apply to the running session as you make them. Nothing is written to the config file until
Save, so a session can be experimented with and abandoned by not pressing it — and Reset puts the
values back without saving either.

The menu is generated from the same name list the chat commands and the mod API use, so a setting
added to the config file appears in it without anyone maintaining a second list. A setting the
menu's layout table does not describe still gets a control, under **Other**.

On a multiplayer client the simulation controls are visible but disabled, for the same reason
`set` is refused there: the config is world state and belongs to the server. The four presentation
switches stay editable, because they only change what that client draws.

## Mechanisms

Each switch removes exactly its own mechanism and its own cost.

| Setting | Default | Effect |
| --- | --- | --- |
| `EnableEnvironment` | `true` | Master switch for radiation and convection. |
| `EnableConduction` | `true` | Heat flow between touching blocks. |
| `EnableRadiation` | `true` | Radiative exchange with the ambient sky. |
| `EnableConvection` | `true` | Convective exchange with the surrounding air. |
| `SolarOcclusionPlanets` | `true` | A planet may shadow the grid: night, and a world's shadow from orbit. Analytic — an angle against the planet's radius, no raycast — so it is nearly free. |
| `SolarOcclusionTerrain` | `true` | The planet's own ground may shadow the grid: the mountain to the east at sunrise, the canyon wall, the cliff a base is parked against. Ground-height lookups along the sun ray, ten of them, and only for grids within 15 km of mean radius. |
| `SolarTerrainRange` | 4000 m | How far along the sun ray the terrain walk looks. Near ground is what shadows you — the cliff two hundred metres off — and far ground almost never does, so this is short by design. |
| `SolarOcclusionVoxels` | `true` | Asteroids and other voxels may shadow the grid. Costs a physics raycast per candidate voxel per sample. |
| `SolarGridShadows` | `full` (2) | How much work another grid's shadow is worth. `0` none: other grids never shadow this one. `1` basic: one ray toward the sun per sample, and anything in the way dims the whole grid — the original behaviour. `2` full: the shadow lands on the faces it actually covers, for a walk through the occluder's blocks per face of this grid, on the shadow pass rather than per step. Full needs `SolarSelfShadowing`, whose pass it rides on. |
| `SolarOcclusionSamples` | 1 | Points across the grid tested for shadow, 1..9. One is a single ray from the middle: the whole ship is lit or dark together, and flips the moment its centre crosses a shadow. More points spread through the hull turn that step into a ramp, at the cost of one full query each. |
| `SolarSelfShadowing` | `true` | A grid shadows itself: a face standing behind the ship's own structure takes no sunlight. Costs a walk from each cell toward the sun each time the sun moves more than 2°, spread over ticks in slices, and nothing between those. Turn it off for the cheap model, which lights any exposed face pointing at the sun. |
| `EnableSolarHeat` | `true` | Solar gain and the sun occlusion raycast. |
| `EnableHeatSources` | `true` | Gain from point sources registered by other mods. |
| `EnableWasteHeat` | `true` | Heat from power production, power draw and thrust. |
| `EnablePlanets` | `true` | Planetary climate. Off means ambient is always `VacuumTemperature`. |
| `EnableFriction` | `true` | Aerodynamic heating at speed in atmosphere. |
| `EnableDamage` | `true` | Damage above a block's critical temperature. |
| `EnableCoolantLoops` | `true` | Coolant loop heat transport. |
| `EnableRoomAir` | `true` | Sealed rooms hold an air mass that couples their surfaces. |
| `EnableHeatPumps` | `true` | Heat pumps move heat against a gradient for an electrical cost. Off makes them ordinary blocks. |

## Solver

`MaxLinkVisitsPerStep` is the one to reach for when a very large grid stutters, and it is worth
understanding before changing it.

A step's cost is not its length: it is the number of substeps it takes times the number of links
on the grid. The substep count is set by the stiffest node, which moves as the grid heats — so a
large ship produced steps costing 15 ms most of the time and 70 ms occasionally, with nothing a
player could see or avoid changing between them.

When a step would exceed the budget, the step is made **shorter** rather than its substeps
coarser. Coarsening substeps would take steps too large for the grid's stiffness and lean on
`ClampConductionOvershoot` to stay bounded, which loses accuracy. Shortening the step advances
less simulated time at exactly the same accuracy: heat moves more slowly, and nothing else about
it changes.

So this setting buys **smoothness with simulation rate**. Lower it and a large grid takes smaller,
more even steps and its heat evolves more slowly; raise it or set it to zero and it runs at full
rate with the spikes back. Grids below roughly a hundred thousand blocks never reach the default
and are unaffected either way. The telemetry report says what rate each grid is actually keeping.

### Trading simulation speed for heat transfer

A natural idea, and worth knowing what it does before reaching for it: halve `SimulationSpeed` and
double `HeatTimeScale`, so half as many steps run each second but heat moves twice as fast in
each, and a ship still cools at the same rate a player watching it would see.

**The pace half is exactly true.** `HeatTimeScale` divides every heat capacity, which is precisely
equivalent to running the clock faster, so any pairing with the same
`SimulationSpeed x HeatTimeScale` produces the same temperatures against the wall clock —
measured at 0.02 % apart across quarter speed, half speed and double speed
(`PaceEquivalenceTests`).

**The saving half mostly is not.** A step costs its substeps, and the substeps a grid needs are
proportional to the step length times the stiffness — which is what `HeatTimeScale` is. Buying
half the steps with twice the stiffness leaves the substeps roughly where they were. Measured on a
hull, quartering the speed and quadrupling the transfer took the substep count from 72 to 60.

What saving there is comes from somewhere else: **fewer, longer steps**, which amortise the passes
that run once per step whatever its length — mirroring node state, estimating the substep count,
publishing the result. That is worth having, and there is a simpler way to ask for it.

| speed | heatScale | freq | steps | substeps | ms / real s | cooled |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1.00 | 225 | 4 | 80 | 240 | 75.6 | 46.37 K |
| 0.50 | 450 | 4 | 40 | 200 | 58.7 | 46.44 K |
| 0.25 | 900 | 4 | 20 | 200 | 57.0 | 46.56 K |
| **1.00** | **225** | **1** | 20 | 200 | **57.5** | **46.56 K** |
| 1.00 | 225 | 16 | 320 | 320 | 106.2 | 46.30 K |

*127,000 blocks, 20 real seconds, work budget off so the effect is not hidden. `bench pace`.*

**Lowering `Frequency` alone reaches the same place** — the fourth row is the third row's cost and
the third row's answer, with `SimulationSpeed` and `HeatTimeScale` left alone. It is one knob
instead of two, it does not move the world's clock, and it leaves `SimulationSpeed` meaning what a
player expects. Going the other way costs: `Frequency 16` is nearly twice `Frequency 1` for a
result 0.5 % different.

Two things to watch when lowering it. A longer step needs more substeps, so a stiff grid can reach
`MaxSubsteps` and start clamping — the report's **steps clamped by substep cap** is where that
shows, and it should stay at zero. And on a grid large enough for `MaxLinkVisitsPerStep` to bind,
that budget is already shortening steps and is the constraint that matters; lowering `Frequency`
will not move it much.

| Setting | Default | Effect |
| --- | --- | --- |
| `Frequency` | 4 | Solver steps per simulated second. The integration step is `1/Frequency`. Lowering it is the cheapest way to cut cost — see above. |
| `SimulationSpeed` | 1 | Simulated seconds per real second, applied by running more steps rather than longer ones. Linear in CPU. |
| `HeatTimeScale` | 225 | How much faster than real physics heat moves. Divides every heat capacity. |
| `MaxLinkVisitsPerStep` | 1000000 | Most link visits one step may make — substeps times links — before the step is shortened to fit. 0 removes the bound. See below. |
| `ClampConductionOvershoot` | `true` | Caps each exchange at the energy that equalises the pair. Off reproduces the original unbounded solver. |
| `DamageIsPerSecond` | `true` | Overheat damage per second of simulated time. Off applies it per step, which makes damage scale with `Frequency`. |

## Environment

| Setting | Default | Effect |
| --- | --- | --- |
| `ClimateGroundInfluence` | 1.0 | How much the ground a grid is parked on shifts the air above it, 0..1. At 1, snow is about 14 K colder than the planet's own figure with a flatter day, and sand about 8 K warmer with nearly twice the swing. At 0 the ground is ignored. |
| `ClimateWeatherInfluence` | 1.0 | How much the weather standing over a grid changes the air around it, 0..1. At 1 the game's own authored figures apply in full — a heavy snowstorm about 18 K colder with a tenth of the sun and twice the wind, a sandstorm 12 K warmer. At 0 the weather affects nothing but the wind, which is what it did before. |
| `VacuumTemperature` | 2.7 K | Ambient in space, and the floor for planetary ambient. |
| `SolarEnergy` | 1000 W/m² | Solar irradiance above the atmosphere. |
| `FrictionAtSpeedsAbove` | 50 m/s | Relative airspeed at which aerodynamic heating starts. |
| `FrictionScale` | 0.001 | Coefficient on the v³ friction term. |
| `RoomConvectionCoefficient` | 8 W/(m²·K) | Coupling between a room's air and the surfaces facing it. Lower than the planetary figure because room air is still. |
| `RoomAirDensity` | 1.225 kg/m³ | Air density in a fully pressurised room. |
| `SolarOcclusionInterval` | 12 | Solver steps between solar occlusion raycasts. The raycast is the most expensive thing a grid does and the sun moves slowly. |

## Heat pumps

The two ratings a heat pump has — how much heat it can move and how much electricity it can draw —
belong to the block and live in
[ThermalHeatPumpShapes](../Data/Scripts/Thermodynamics/Game/ThermalHeatPumpShapes.cs). What is
tunable here is the physics between them, which is the same for every pump in the world.

| Setting | Default | Effect |
| --- | --- | --- |
| `HeatPumpCarnotFraction` | 0.4 | How much of the Carnot limit a pump achieves, 0..1. A real domestic heat pump manages about 0.4; 1 would be a thermodynamically perfect machine. This one number is the whole balance of the block. |
| `HeatPumpMaxCoefficient` | 8 | Ceiling on the coefficient of performance. Carnot's figure runs to infinity as the two sides converge, and a real machine is limited by its compressor long before that. |

Raising the fraction does not change what a pump can do — the shape of the cost curve is Carnot's
and is not negotiable — only how far up it the block sits. Lowering the ceiling makes pumps
predictable near equilibrium at the cost of making cheap, small-gap cooling less rewarding.

## Presentation

All client side and all off by default.

| Setting | Default | Draws |
| --- | --- | --- |
| `DebugTextOnScreen` | `false` | Crosshair readout: temperature, per-mechanism watts, block constants, environment, grid totals, room classification, raw surface bits. Switching it on also makes the solver record per-mechanism watts, which is not free. |
| `DebugSolarRaycast` | `false` | Draws the sun ray from each grid, white when lit and red when occluded. |
| `DebugWindRaycast` | `false` | Draws the relative wind vector. |
| `RoomOverlayMinKelvin` | 253.15 K | Bottom of the room view's colour span, −20 °C. |
| `RoomOverlayMaxKelvin` | 323.15 K | Top of the room view's colour span, 50 °C. |
| `DebugBlockOverlay` | 0 | Which view the block overlay opens a session on: 0 off, 1 temperature, 2 solar watts, 3 exposed faces, 4 friction watts, 5 rooms. |

### The block overlay

**Ctrl+Shift+=** cycles it: off → temperature → solar watts → exposed faces → friction watts → rooms
→ off.
`/thermal overlay` does the same from chat. A mod cannot add a rebindable control, so the chord is
fixed; it is ignored while the chat box or a menu is open.

Every block of the grid you are controlling and the grid you are looking at is drawn as a translucent
box coloured by the selected value, using the same ramp as the HUD and the terminal. The whole grid,
at any distance: a debug view that faded out at some radius would read as a cold far end rather than
as an undrawn one. Only those two grids are picked up, which is what keeps the cost bounded. The boxes are drawn *through* the hull — a reactor buried mid-ship is visible from
outside, which is the point of a debug view and the reason this was a poor thermal camera.

While a view is up, a readout panel sits at the top left with the figures behind the picture: what
the grid's coldest, mean and hottest blocks are in the temperature view, how many watts of sunlight
it is taking and whether the sun is occluded in the solar view, its exposed face count and area,
its speed against the friction threshold, or a table of rooms with their cell counts, air
temperatures and seal state. It follows whatever grid the overlay is drawing and disappears with it.
The panel needs Rich HUD Master, the same as the settings menu; the overlay itself does not.

### What self-shadowing costs

Measured by the `shadow-cost` scenario, which reports both halves because they are different kinds
of work:

| | cheap model | self-shadowed |
| --- | --- | --- |
| Per tick, 8000-block grid, sun still | 0.010 ms | 0.010 ms |
| One full pass, 8000-block solid cube | — | 2400 air cells, 2.4 ms |
| One full pass, 4440-block hull with decks | — | 7848 air cells, 4.3 ms |

Between passes it costs nothing measurable: one extra multiply per face inside the solar term. The
pass is the cost, and it is spread over ticks in slices of `SunShadowBudget` (2048 cells) — about
1.1 ms per slice on both grids above, landing on the grid's ten-frame tick.

A pass runs when the sun has moved 2° in the grid's own frame. On a planet that is tens of seconds
of play. In space it is the *ship's* rotation that moves the sun, so a grid spinning fast rebuilds
continuously — a sustained ~1 ms per tick on a mid-size ship rather than an occasional one. If that
ever matters, `SolarSelfShadowing` off is the answer, and it is free.

### External shadow

`SolarOcclusionInterval` decides how often any of it is re-tested; the three switches decide what is
tested at all, and each is priced differently:

| Occluder | How it is tested | Cost |
| --- | --- | --- |
| Planet | angle against the planet's radius | arithmetic, no ray |
| Terrain | ground height sampled along the sun ray | ten height lookups, near a surface only |
| Voxel | physics raycast against the asteroid | one raycast per candidate |
| Grid | one ray, or a walk per face | `SolarGridShadows`: none, one block ray per candidate, or one walk per face |

At `full`, other grids are dropped from the whole-grid ray and answered per face instead: counting
them twice would shade an entire ship for a shadow across one corner. The
neighbours are gathered on the occlusion interval, and a new shadow pass only starts when one of
them has actually moved — more than a cell, or turned more than about two degrees. Two ships docked
together never move relative to each other and cost nothing after the first pass.

The planet and terrain tests answer different halves of the same question. The planet's is the ball:
is the sun below the horizon of a smooth world. Terrain's is everything the ball ignores, which is
what a player on the ground can see — a base in a canyon stays cold for an hour after the ball says
dawn. The walk runs only when the ball says the sun is up, so night costs nothing extra.

Shadow is now a fraction rather than a flag: `SolarOcclusionSamples` points are cast from inside the
hull, and solar gain is scaled by the share that reached the sun. At the default of one sample that
share is 0 or 1 and behaves exactly as before. Raise it and a kilometre-long ship crossing a
terminator dims over the crossing instead of switching off when its centre passes.

The three solar settings stack as a choice of cost. `EnableSolarHeat` off is free and models no
sunlight at all. On with `SolarSelfShadowing` off is the cheap model: a face is lit whenever it
points at the sun. On with both is the accurate one: the grid shadows itself, for one pass over its
cells whenever the sun moves.

**Solar watts** does not draw boxes. Sunlight lands on a face, not on a block, so it draws the
grid's skin — one quad per exposed face — shaded by that face's own irradiance: the sun's energy
scaled by how square the face is to it, and zero when the mechanism is off or the grid is in shadow.
A wall dark because it turned away from the sun then looks plainly different from a wall dark
because the ship is eclipsed. Faces pointing away from the camera are dropped, so what you see is
the near skin rather than both sides at once. The panel still reports the watts.

**Solar watts** and **friction watts** are the per-mechanism figures the solver normally does not
bother to write down. Selecting either makes it record them for as long as that view is up, the same
way the crosshair readout does, and stop when you cycle past. Without that they would draw every
grid uniformly at zero, which reads as "no solar heating" rather than as "not measured".

**Rooms** is the one view that draws cells rather than blocks: a box in every cell the mapper put in
a room, **coloured by that room's air temperature** on a tighter span than blocks use
(`RoomOverlayMinKelvin`/`RoomOverlayMaxKelvin`, −20 °C to 50 °C by default), because room air lives
in a narrow band where the block ramp makes every compartment the same shade. Which room is which
rides on the wireframe instead — a colour per room, faint when vented — so identity never costs the
temperature its clarity.

A room holding no air is drawn as an empty outline: there is no temperature to show, and an
unpressurised compartment is usually the thing being hunted for. It is also how you find out whether
two compartments you think are separate came back as one room, and where the leak is when a room you
think is sealed reads as vented.

Everything about it is client side and per frame: nothing is written to the grid, nothing
replicates, and switching it off leaves no trace. It replaces the four `Debug*BlockColors` modes,
which called `MyCubeGrid.ColorBlocks` and showed heat by permanently overwriting every player's
paint. `DebugBlockOverlay` is a server-side setting like the rest of the file, so it only sets what
a session starts on — the keybind is how each client drives it.

## Telemetry

| Setting | Default | Effect |
| --- | --- | --- |
| `EnableTelemetry` | `false` | Session-long data collection. See [telemetry.md](telemetry.md). |
| `TelemetrySampleStride` | 4 | Fraction of each grid's blocks sampled per step for the per-definition statistics — `1/n`. Every block is still seen once per `n` steps. |

## Time and pace

Three settings affect pace and they do different things:

```
StepSeconds    = 1 / Frequency              simulated seconds in one solver step
StepsPerSecond = Frequency × SimulationSpeed
Capacity       = SpecificHeat × Mass / HeatTimeScale
```

* **`Frequency` is accuracy** — how finely a simulated second is integrated.
* **`SimulationSpeed` is how fast simulated time runs.** Honest, and linear in CPU.
* **`HeatTimeScale` is how fast heat moves within a simulated second.** Free in CPU terms, and the
  reason the definitions can carry real material values without a hull taking hours to cool.
  Dividing every capacity by *k* is exactly running thermal time at *k*×: every rate scales
  together, so equilibrium temperatures, the balance between mechanisms and the ratios between
  block types are all unchanged. Only the clock moves.

Total acceleration over real physics is `SimulationSpeed × HeatTimeScale`. The shipped 225 is what
makes steel's real 450 J/(kg·K) behave the way the old flat value of 2 did.

`HeatTimeScale` costs stability margin rather than time: higher values make the system stiffer, so
the solver takes more substeps. When a grid is stiff enough to hit the substep cap the telemetry
report says so.

Block tuning — conductivity, specific heat, critical temperatures — is not in this file. It lives in
the definition XML; see [definitions.md](definitions.md).
