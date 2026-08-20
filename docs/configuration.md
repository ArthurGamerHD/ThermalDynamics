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
| `/thermal set <name> <value>` | Changes one setting for this session. Switches take `on`/`off` or `1`/`0`. On a multiplayer client this asks the server, which answers whether it was allowed. |
| `/thermal save` | Writes the current values to the config file. |
| `/thermal sync` | Digest of every replicated setting, to compare a client against the server by eye. Run it on both; the strings must match. |
| `/thermal sync fetch` | Client only: asks the server for the settings again. |
| `/thermal overlay` | Cycles the block overlay. Same as Ctrl+Shift+=. |
| `/thermal menu` | Opens the settings menu. Same as Ctrl+Shift+S. |
| `/thermal telemetry on` / `off` | Starts and stops data collection. |
| `/thermal stride <n>` | Telemetry sample stride. |
| `/thermal dump` | Writes a telemetry report without closing the world. |

Settings are world state. A client at space master or above may change them — the request goes to
the server, which decides — and the four presentation switches belong to the client outright. The
same names are reachable from other mods, see [api.md](api.md#settings).

## The settings menu

**Ctrl+Shift+S** opens it, as does `/thermal menu`.

The menu is a tree rather than one scroll: **Overview**, **Status** and **Debug** at the root, then
four folders.

| Folder | Pages |
| --- | --- |
| **Solver** | Cost limits, Pace |
| **Heat transfer** | Ambient, Conduction, Radiation, Convection, Solar, Occlusion |
| **Ship systems** | Coolant loops, Heat pumps, Room air, Waste heat, Friction, Overheat damage, Point sources |
| **World** | Climate, Underground |

Sixty-eight settings on a single scroll is a list to be searched by eye, and an administrator
usually arrives wanting one part of it. **One system to a page, with its own switch at the top.**
Four switches used to share a "Mechanisms" page because they were all switches, which is filing by
part of speech: switching convection off belongs above the convection dials, where you can see what
it governs.

A page whose system still keeps most of its numbers in a definition file says so, rather than
looking broken with one switch on it. A setting named on no page still gets a control, on a final
**Other** page — a setting added to the config and forgotten here is reachable rather than
invisible.

Three of the framework's habits shape what the pages can say, and all three were learned by looking
at the menu in game rather than by reading the API:

* **A label is one centred line and clips at both ends rather than wrapping.** Every label here
  stays inside about twenty characters, and anything longer than that — the full text of a warning,
  the list of what has been changed — lives on the Status page, which is a text page and does wrap.
* **A page name clips in the rail at about seventeen characters.** Page names are short for that
  reason, not for taste.
* **A loose page added after a folder draws against the folder's row.** Overview, Status and Debug
  are therefore added before the folders.

**Some settings are typed, not dragged.** A slider offers about two hundred distinguishable
positions, which suits a fraction between 0 and 1 and suits nothing else this mod has. The step
budget spans four million, so one position is ten thousand element visits; the friction scale spans
a hundredth, so every position shows the same number. Seven settings therefore get a field to type
a value into — the step budget, terrain range, solar energy, heat time scale, vacuum temperature,
the friction threshold and the friction scale — and the rest keep their sliders.

The range in a typed field's tooltip is what the slider *would* have spanned, not a limit. A typed
value goes through the same clamp as `/thermal set` and the mod API, so a step budget of nine
million is yours to try. Anything unreadable puts the setting's own value back rather than guessing.

**Overview** answers the two questions a wall of sliders cannot — which profile this world matches,
worked out by comparing the nine values a profile sets, and how many settings differ from the
shipped defaults, each of them dotted in front of its label on its own page. It flags a conflict in
three words; **Status** spells it out, lists every changed setting with the shipped value beside it,
and carries the settings digest for comparing against the server.

Three pages carry live figures read from the running grids rather than from the settings that
produced them: **Cost limits** shows substeps granted against substeps asked for and how many blocks
the cap floored, **Pace** shows the hottest block and what the world is venting against what it
makes, and **Debug** shows which overlay is up and whether telemetry is recording.

It also carries Save, Reset and the five profiles as buttons — profiles were previously reachable
only from chat, so the menu could show a world tuned by one without ever mentioning they existed. It is built on the [Rich HUD
Framework](https://github.com/ZachHembree/RichHudFramework.Client) and needs the **Rich HUD Master**
mod (`1965654081`) to be enabled; without it the keystroke says so and the chat commands remain the
way in.

**Settings save themselves.** Every change is written to the config file about a second
later — a menu that asks you to confirm what you already did is asking you to do it twice,
and a setting that reverts on reload because a button was missed is worse than either.
There is no reset button either: a profile sets every world setting, so applying one is how
you start over.

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

`MaxElementVisitsPerStep` is the one to reach for when a very large grid stutters, and it is worth
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

### `Frequency` is not the cost dial it looks like

Worth understanding before tuning a busy world, because it works on some grids and not at all on
others.

A step is subdivided twice. `Frequency` cuts a simulated second into steps, and then the solver
cuts each step into as many **substeps** as it needs to stay numerically stable — enough that no
block's step is longer than half its own thermal time constant. Substeps advance no extra
simulated time; they are passes over the same interval, bought so the integration does not
diverge. A substep is what actually costs: one walk over every node and every link.

```
substeps a step needs = StepSeconds × max over blocks of (ΣG / C) / safety
                      = (1 / Frequency) × r_max / 0.5
```

and what you pay per real second is that times the number of steps:

```
substeps per real second = (Frequency × SimulationSpeed) × (1 / Frequency) × r_max / 0.5
                         = SimulationSpeed × r_max / 0.5
```

**`Frequency` cancels.** Doubling it halves what each step needs and runs twice as many. What sets
the bill is how much simulated time you asked for and how stiff the stiffest block is.

That holds while the estimate is above one substep. It stops holding at the two ends, and the ends
are where most grids live:

* **A soft grid** needs a fraction of a substep, and the solver still charges a whole one because
  it cannot run less. There `Frequency` *is* the cost, one pass per step, linear. Halving it halves
  the bill. This is a smaller population than it sounds: in the field dump only 37 of 189 stepping
  grids sat at one substep, and between them they held **182 cells** — they are debris, not ships.
  A 790-cell corvette needed five.
* **A grid at `MaxSubsteps`** is being refused what it asked for, so raising `Frequency` shortens
  the step until the estimate fits again. That is a real accuracy gain, and it costs.

Practically: **lowering `Frequency` saves on debris and does nothing on ships.** Measured across
the 189 stepping grids of the field dump, by the substeps each asked for:

| substeps | grids | cells between them |
| ---: | ---: | ---: |
| 1 | 37 | 182 |
| 2–4 | 77 | 2,021 |
| 5–6 | 72 | 46,605 |
| 11 | 3 | 126,157 |

Eighty per cent of grids, and better than ninety-nine per cent of the blocks, are above one
substep — which is the regime where `Frequency` cancels out of the bill entirely. That is what
`MaxSubstepsPerBlock` is for.

### `MaxSubstepsPerBlock`

A step is divided into as many substeps as the **stiffest** block on the grid needs, and every
other block pays for all of them. On a real ship that stiffest block is almost never anything
interesting. Measured on a 42,051-block capital ship: forty-three sixteen-kilogram light fittings
asked for twenty-eight substeps, the five hundred kilogram armour around them asked for one, and
the ship ran at 35 % of real time to pay for the lights.

Physically, a 32 J/K fitting bolted to armour reaches the armour's temperature in about eighteen
milliseconds. At a quarter-second step it is not an independent temperature at all — it is a
reading off the block it is bolted to. This setting says so: any block that would demand more than
N substeps has its heat capacity raised to the least that keeps it inside N. "Demand" counts
everything the stability estimate does — conduction to neighbours, coolant loops, room air, and
the linearised radiation and convection with the sky — so the cap means what it says: set it to
one and the grid takes one substep.

What it costs is **that block's own transient, and only that block's**. It warms and cools more
slowly than a sixteen kilogram object would. It ends up at the same temperature, because where
something settles is decided by the watts cancelling and has nothing to do with heat capacity, so
the difference decays as the grid settles rather than accumulating. Everything the block is bolted
to is untouched, and the block's real heat capacity is still what the terminal, the overlay and
the mod API report.

Measured on a synthetic ship carrying the same proportion of fittings, over fifty simulated
seconds with temperatures spread across 500 K:

| `MaxSubstepsPerBlock` | substeps | speed | blocks affected of 43,232 | worst error |
| ---: | ---: | ---: | ---: | ---: |
| off | 23.0 | 1.0x | 0 | — |
| 16 | 16 | 1.4x | 172 (0.4 %) | 0.03 K |
| 8 | 8 | 2.6x | 461 (1.1 %) | 0.14 K |
| 6 | 6 | 3.4x | 1,450 (3.4 %) | 0.22 K |
| **4** | 4 | **4.7x** | 3,648 (8.4 %) | 0.36 K |
| 3 | 3 | 6.1x | 5,219 (12.1 %) | 0.59 K |
| 2 | 2 | 7.1x | 9,283 (21.5 %) | 1.50 K |
| 1 | 1 | **11.0x** | 14,138 (32.7 %) | 5.00 K |

**The value is chosen by how many blocks it reaches, not by the error.** The error barely moves
between 16 and 2 and stays far below anything a player can see; what changes suddenly is the
population. Above the knee the cap is a handful of fittings; below it, it is re-massing ordinary
armour. On a real ship the knee sits somewhere in 2–4 — a telemetry dump reports the exact figure
for *your* world, per grid, for every candidate cap.

**A field dump found the uncapped configuration is already approximating.** A 1,293-block ship
with its thrusters lit asked for 21.35 substeps against a `MaxSubsteps` of 16, and every one of
its 1,867 steps was clamped — the overshoot clamps carrying the difference, which is bounded but
not accurate. Setting `MaxSubstepsPerBlock` to 16 there costs nothing and stops the clamping
outright, because it makes the demand fit the ceiling rather than leaning on a clamp to survive
being refused. Setting it to 8 stops the clamping *and* runs at 2.6 times the rate.

Hence a rule worth remembering: **while `MaxSubstepsPerBlock <= MaxSubsteps` the overshoot clamps
never engage**, and every step is genuinely short enough for the grid it is integrating.

It is off by default because it is an approximation, and a mod that models heat should not make
one on a player's behalf without being asked. On a world with large ships in it, turning it on is
the single largest thing that can be done for frame time — and unlike `MaxElementVisitsPerStep`, it
buys the throughput back rather than trading it away: a ship that stops needing more substeps than
the visit budget allows stops being throttled at all.

Reproduce the table with `dotnet run --project Thermodynamics.Sim -- bench floor --size 42000`.

**It does not have to be guessed.** A telemetry dump taken with the cap off reports, per grid and
for the world, exactly what each cap would do to the substep count and how many blocks it would
raise — see [telemetry.md](telemetry.md#substeps). The projection is the same arithmetic the
setting uses, and `SubstepFloorTests` asserts the two agree, so one baseline dump answers the
question for that world without running the experiment.

## Changing settings from a client

Every setting except the four presentation switches is world state, owned by the server. A client
at **space master** or above can change one anyway: the settings menu and `/thermal set` send the
change to the server as a request, the server checks the asker's promote level and applies it, and
the result comes back as a chat line. An accepted change then replicates to everyone as part of the
ordinary settings sync, so the value moving is its own confirmation.

A player below that level is refused, and told so — silence would be indistinguishable from a lost
packet.

**The request travels on its own channel, not the one the rest of the mod uses.** SENetworkAPI
registers the game's non-secure message handler, where the sender's id is a field the *sender*
wrote; its own documentation says not to gate admin actions on it. `SettingsRequests` uses
`RegisterSecureMessageHandler`, where the transport supplies the sender and a from-the-server flag
that a client cannot forge. That is the whole reason for the separate channel.

## The definition files, from the menu

`Loops.xml` and `Planets.xml` were settings nobody could reach in game: "how fast does coolant
move" is a settings question whose answer lived in a file the settings menu had never mentioned.
Their nineteen values are world settings now — saved, replicated, reachable from `/thermal set` and
the mod API, and laid out on the **Coolant loops**, **Climate** and **Underground** pages.

They behave as an override rather than a copy. A value still equal to what a fresh install ships
leaves the definition alone, so an untouched world reads whatever `Loops.xml`, `Planets.xml` and
the active profile's overlay say. Move one and it wins from then on, for that world.

`Cubes.xml` is not here: its properties are per block subtype, which a flat setting cannot express.
A profile's overlay reaches those, and editing them per world is the next step.

## Checking a client has the server's settings

Only the server reads the config file. A client is sent the world's settings when it joins and
whenever one changes, and until that arrived it would be simulating the shipped defaults — the same
physics inputs producing different temperatures on the two machines, with nothing on screen to say
so.

`/thermal sync` prints a digest over the 44 replicated settings, plus the five figures most likely
to differ. Run it on the server and on a client: **the digests must match**. If they do not,
`/thermal sync fetch` asks again, and running the first command a second time says whether that
worked.

The four presentation switches — the debug text, the two raycast overlays and the block overlay —
are deliberately outside the digest. A client owns what is drawn on its own screen, so those are
allowed to differ and a server does not overwrite them.

## Profiles

Five presets, laid out as a graphics menu lays them out: a ladder on two axes, where the
simulation is integrated and how fast heat is made to move. `/thermal profile` lists them,
`/thermal profile arcade` applies one live, and a profile sets **every** world setting — so
applying one is also how you start over. A fresh world runs `responsive`.

| Profile | Freq | HeatTimeScale | MaxSubsteps | Blocks crossed | Work/s | ms/s | For |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| `simulation` | 8 | **1** | 64 | 0.0 | 3,192 | 0.58 | Real time, real physics. The reference, not a way to play. |
| `optimized` | 4 | **1** | 6 | 0.0 | 1,596 | 0.13 | Real time with the cost dials tuned. |
| `simlite` | 4 | **1** | 3 | 0.0 | 1,596 | 0.04 | Real time, knowingly approximate. |
| `responsive` | 8 | 225 | 64 | 0.1 | 3,192 | 0.07 | **The default.** Simulation with the clock run fast. |
| `arcade` | 4 | 225 | 6 | 0.1 | 1,596 | 0.04 | Responsive's pace at optimized's price. |

*Measured by `bench profiles --seconds 8`: blocks crossed along a held-hot 200-block run, work as
element visits per real second, and the solver's own milliseconds per simulated second.*

**Read the first three rows' zero honestly.** It is not a rounding artefact: at `HeatTimeScale` 1 a
ship changes temperature at the rate a ship does, and eight seconds of play moves heat across no
blocks at all. The same run with the environment on leaves a hot spot 768 K above its hull on those
profiles and 10 K above it on the two that run the clock fast. That is the whole difference between
the reference and a way to play.

**`HeatTimeScale` is also the stiffness dial**, because it divides every heat capacity: substep
demand on a 150-block hull is 0.00 at scale 1, 0.90 at 225 and 14.40 at 3,600. Real time is the
cheapest thing to integrate, which is why the accurate profiles are not the expensive ones — on
this ladder accuracy costs patience, and pace costs frames.

See [profiles.md](profiles.md) for the ladder in full, including the definition overlay each
profile brings with it.

> The Cost column tracks `Frequency` exactly, and that is a property of what it was measured on
> rather than of `Frequency`. Both figures come from a 200-block conduction run where every node
> has at most two neighbours and the substep estimate sits at or below one — the regime where a
> step costs one pass whatever its length. On a stiff grid the estimate is far above one and the
> column would be flat in `Frequency` instead. See
> [`Frequency` is not the cost dial it looks like](#frequency-is-not-the-cost-dial-it-looks-like).

**The shipped default is the worst point on this table**, and that is worth saying plainly: it is
outrun by every other profile including the cheapest one. It spends its budget on accuracy — a low
`HeatTimeScale` with substeps to spare — and accuracy is not what most of the settings above are
for. It stays the default because a simulation mod that quietly stops being a simulation is a
worse surprise than a slow one, but a world that wants heat to *do* something should not be on it.

### Where the extremes lie

Two relationships bound everything, and neither can be tuned around.

**Heat spreads as the square root of the arithmetic you spend on it.** Diffusion is a square-root
process: a front crosses blocks at a rate proportional to `sqrt(substeps per second)`, while cost
is proportional to substeps per second outright. Measured, holding everything else: 1 substep/s
gave 0.5 blocks/s, 4 gave 1.0, 8 gave 1.4, 16 gave 2.0 — square root to two figures. **Doubling
how responsive a world feels costs four times as much.**

**But where you spend the substeps changes the exchange rate by about three times.** There are two
ways to move more heat per second. The accurate one raises `HeatTimeScale` and grants the extra
substeps its stiffness demands. The approximate one raises `HeatTimeScale` *and refuses* the
substeps with `MaxSubsteps`, letting the overshoot clamps decide how much crosses — which is the
most a substep can carry, by definition. Measured: the clamped route delivered 0.125 blocks/s per
substep/s against 0.045 for the accurate one. If the shape of the curve between two temperatures
does not matter to your world, do not pay for it.

**The far end is a wall, not a slope.** Past roughly `HeatTimeScale / Frequency = 4000` the clamps
are carrying the entire step and blocks start being driven to the ambient floor. `arcade` sits at
3,333 deliberately. `Validate()` warns above 4,000, and the settings menu will show it.

### Designing your own

* **`Frequency` sets responsiveness and cost together — but only while `MaxSubsteps` is 1**, or
  the grid is soft enough that the estimate never rises above one substep. That is the regime
  every profile above was tuned in. On a grid stiff enough to ask for real substeps it cancels out
  of the cost entirely; reach for `MaxSubstepsPerBlock` there instead.
* **`HeatTimeScale` sets how much a substep carries.** Raise it until the clamps engage; past that
  it buys nothing, because the clamp is already moving all it can. `arcade` at 20,000 and the same
  profile at 1,000,000 reach identically far.
* **`MaxSubsteps` chooses accuracy or speed.** High means the estimate is always granted and
  nothing clamps. `1` means every step is deliberately too long and the clamps carry it.
* **Keep `HeatTimeScale / Frequency` under 4000.** This is the safety rail. Everything else is
  taste.
* **`EnableRoomAir` and `SolarSelfShadowing` are the two mechanisms that cost most** for what a
  player notices; `minimal` turns both off.

Both clamps must stay on for any of this. `ClampConductionOvershoot` and
`ClampEnvironmentOvershoot` are what make a deliberately-too-long step bounded instead of
divergent — with them off, `arcade` reaches 10^22 K in twenty seconds.

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
shows, and it should stay at zero. And on a grid large enough for `MaxElementVisitsPerStep` to bind,
that budget is already shortening steps and is the constraint that matters; lowering `Frequency`
will not move it much.

| Setting | Default | Effect |
| --- | --- | --- |
| `Frequency` | 8 | Solver steps per simulated second. The integration step is `1/Frequency`. Whether lowering it cuts cost depends on the grid — see below. |
| `SimulationSpeed` | 1 | Simulated seconds per real second, applied by running more steps rather than longer ones. Linear in CPU. |
| `HeatTimeScale` | 225 | How much faster than real physics heat moves. Divides every heat capacity. |
| `MaxElementVisitsPerStep` | 1000000 | Most element visits one step may make — substeps times its links plus four times its nodes — before the step is shortened to fit. 0 removes the bound. See below. |
| `MaxSubstepsPerBlock` | 0 (off) | Most substeps any single block may demand of the whole grid before it is treated as heavier than it is. The cheapest large win there is on a real ship. See below. |
| `MaxSubsteps` | 64 | Most substeps one step may be cut into, whatever the grid asks for. A grid refused here integrates a step too long for its stiffest block, and the overshoot clamps carry the difference. |
| `ClampConductionOvershoot` | `true` | Caps each exchange at the energy that equalises the pair. Off reproduces the original unbounded solver. Skipped, at no change to the result, on any step short enough that no element can overshoot — see [benchmarks.md](benchmarks.md#the-overshoot-clamp-ab). |
| `ClampEnvironmentOvershoot` | `true` | The same for radiation and convection: neither may carry a block past ambient in one substep. This is what bounds a profile whose step is deliberately far too long. |
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

## Coolant loops

These ship in [`Loops.xml`](../Data/Loops.xml), which the settings menu never showed — so "how fast
does coolant move" was a settings question whose answer lived in a file nobody could reach from the
game. They are world settings: saved, replicated and editable like any other.

**A value left at its shipped figure does not override the definition.** The file, and whatever a
profile's overlay does to it, still decides. Move one and it wins from then on, across every loop
definition in the world.

| Setting | Default | Effect |
| --- | --- | --- |
| `LoopCoolantMassPerPipe` | 50 kg | Coolant carried by one pipe block. More capacity for the same coupling: a heavier ring takes longer to saturate and longer to shed. |
| `LoopSpecificHeat` | 3400 J/(kg·K) | The coolant's specific heat. Water-glycol is about 3,400. |
| `LoopConductivity` | 1.0 | How well the fluid conducts into the pipe carrying it, 0..1. |
| `LoopPipeContactMultiplier` | 1.0 | Scales the coupling between the fluid and its own pipe. |
| `LoopSinkContactMultiplier` | 1.0 | Scales the coupling through a sink face into whatever is mounted against it. This is the dial that decides whether plumbing beats bolting. |
| `LoopLargeGridFlowRate` | 10 m/s | How fast coolant moves on a large grid with one pump at full speed. Flow costs no substeps — carrying the fluid is a rotation of which parcel sits in which pipe, exact at any speed — so this is free to be set for feel. |
| `LoopSmallGridFlowRate` | 10 m/s | The same for a small grid. Split from the large-grid figure because it is a balance dial rather than a constant. |
| `LoopStagnantTransferFraction` | 1.0 | What a stopped ring still carries between neighbouring parcels, 0..1. 0 makes a pump failure total. |

## Planet climate

These ship in [`Planets.xml`](../Data/Planets.xml) and follow the same rule: one entry ships, so
these address it, and a world with several authored planet types still reads them from the file
until a value is moved off its shipped figure. See [planet-climate.md](planet-climate.md) for the
model they parameterise.

| Setting | Default | Effect |
| --- | --- | --- |
| `PlanetDayTemperature` | 294.261 K | Equatorial daytime air. |
| `PlanetNightTemperature` | 283.15 K | Equatorial night air. |
| `PlanetPoleTemperatureDrop` | 40 K | Span from equator to pole, interpolated on cos(latitude). |
| `PlanetAmbientLapseRate` | 4 K/km | How fast the air cools with altitude. |
| `PlanetAmbientLagSeconds` | 45 s | First-order lag on the ambient target, which is what makes the day peak after noon. Absolute seconds against a day that is not — see [planet-climate.md](planet-climate.md#open). |
| `PlanetConvectionCoefficient` | 50 W/(m²·K) | Convective coupling in full atmosphere, scaled down with air density and up with wind. |
| `PlanetSolarDecay` | 0.5 | How fast sunlight is attenuated through the atmosphere. |
| `PlanetUndergroundTemperature` | 280 K | The rock's own temperature below the damping depth and above the deadzone. |
| `PlanetUndergroundDampingDepth` | 20 m | Depth over which the day's swing, the weather and the ground table all damp out. Below it, ambient is simply `PlanetUndergroundTemperature`. |
| `PlanetSealevelDeadzone` | 2000 m | Depth below which the rock starts warming toward the core. Deeper than SE's voxels reach, so every reachable depth currently reads flat. |
| `PlanetCoreTemperature` | 3000 K | Temperature at the planet's centre, approached linearly from the deadzone. |

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

## Wind

The game's own wind figure is `MaxWindSpeed × airDensity` — one number per planet, scaled linearly
by altitude, identical at the pole and the equator, with no direction and no time of day. These
settings drive the model that replaces it. See [wind-model.md](wind-model.md) for what each one is
and where it comes from.

| Setting | Default | Effect |
| --- | --- | --- |
| `WindRoughnessLength` | 0.03 m | Roughness length z0 — about a tenth of the height of whatever covers the ground. 0.0002 open water, 0.03 grassland, 0.1 scattered obstacles, 0.5 forest. Sets how steeply wind strengthens with height near the surface. |
| `WindGradientHeight` | 600 m | Height at which wind stops strengthening: the top of the boundary layer. Above it the profile is flat and the air density takes it down from there. |
| `WindDiurnalAmplitude` | 0.35 | How far the daily cycle swings wind either side of its mean. Surface wind peaks in the afternoon; wind above the crossover peaks before dawn. 0 disables the cycle. |
| `WindDiurnalCrossover` | 80 m | Height at which the daily cycle vanishes. Below it the surface cycle, above it the nocturnal jet, fully reversed by twice this height. |
| `WindTerrainInfluence` | 1 | How much the shape of the ground affects wind: speed-up over rises, shelter behind ridges, steering along valleys. 0 leaves the wind ignorant of terrain. |
| `WindTerrainRadius` | 300 m | How far out the land around a point is read. The scale of landform the wind is allowed to notice. |
| `WindSlopeStrength` | 1 | Slope winds, 0..1: air running **up** a mountain by day and draining back **down** it at night. A thermal flow the ground makes rather than something it does to an existing wind, so it blows on a still day — and a real wind overruns it. Costs about 46 ns a sample, because the terrain it needs is already read. |

## Presentation

All client side, and all off by default but one: the wind indicator, which is the only entry here
that is a readout for playing rather than a diagnostic for debugging.

| Setting | Default | Draws |
| --- | --- | --- |
| `DebugTextOnScreen` | `false` | Crosshair readout: temperature, per-mechanism watts, block constants, environment, grid totals, room classification, raw surface bits. Switching it on also makes the solver record per-mechanism watts, which is not free. |
| `DebugSolarRaycast` | `false` | Draws the sun ray from each grid, white when lit and red when occluded. |
| `DebugWindRaycast` | `false` | Draws the relative wind each grid is flying through, as a line from the grid scaled by its speed: green in still air, red once the grid is over `FrictionAtSpeedsAbove` and the leading face is heating. |
| `DebugWindOverlay` | 0 | Which view the wind map opens a session on: 0 off, 1 the lattice around you, 2 the whole planet. |
| `DebugWindIndicator` | `true` | The wind needle and speed under the crosshair. |
| `RoomOverlayMinKelvin` | 253.15 K | Bottom of the room view's colour span, −20 °C. |
| `RoomOverlayMaxKelvin` | 323.15 K | Top of the room view's colour span, 50 °C. |
| `DebugBlockOverlay` | 0 | Which view the block overlay opens a session on: 0 off, 1 temperature, 2 solar watts, 3 exposed faces, 4 friction watts, 5 rooms. |

### The wind map

**Ctrl+Shift+W** cycles it: off → local → planet → off. `/thermal wind` does the same from chat.

The game has no wind field — `MyPlanet.GetWindSpeed` returns the planet definition's maximum scaled
by air density, the same figure at the pole and the equator — so this mod invents one, and until
this view there was no way to look at it. See [planet-climate.md](planet-climate.md#wind) for what
the field is; this is how you see it.

**Local** drapes arrows over the ground itself, out to five kilometres in every direction — a disc of
about thirteen hundred, 250 m apart, each projected onto the terrain under it and lifted ten metres
clear. Five kilometres is chosen against the field's own 900 m variation scale: it takes several
turnovers of that variation to read as a pattern rather than as one gust, and a lattice small enough
to fit on a landing pad shows a single value repeated.

Because the arrows lie on the terrain, **terrain hides them**. From standing height most of a five
kilometre field is below the horizon or behind a hill — that is the field being drawn honestly, and
it is why this view is worth gaining some altitude for. Look down on a valley from a few hundred
metres up and the whole disc is visible at once.

The lattice is anchored to the world rather than to you, snapped to a whole number of 250 m steps, so
it stays put as you walk instead of sliding along underfoot. It is rebuilt when you cross into the
next cell, and the rebuild is spread over frames — about nine of them — because the terrain lookup
per arrow is the one expensive call in the view. The arrows already up stay up until the replacement
is complete, so a resample is invisible rather than a blink.

**Planet** draws arrows over the whole globe on a latitude and longitude lattice, sized against the
planet's radius and floating above its highest terrain. This is the view for the circulation itself:
easterly trades either side of the equator, westerlies in the middle latitudes, easterly again at the
poles. Fly out far enough to see a hemisphere. Arrows on the far side are dropped rather than drawn
through the planet, since nothing occludes transparent geometry.

An arrow points **where the wind blows**, is longer and redder the harder it blows, and is scaled
against the storm end of the ramp rather than against the planet's ceiling — calm air is about a
tenth of that ceiling, so scaling against it would draw every ordinary day as a field of stubs.

Arrow *width* is held on the screen rather than in the world, between a floor and the arrow's own
length. A single lattice spans two orders of magnitude of distance — the arrow at your feet and the
one five kilometres away are the same arrow — and a fixed width in metres would draw the near one as
a slab and lose the far one entirely.

Two things it does not do. **Weather is sampled once, where you are standing, and applied to every
arrow**: asking per arrow costs a string allocation and a lookup for each of several hundred points
every resample, and on the globe view it would be reading one storm's weather at points thousands of
kilometres away regardless. And the lattice is resampled every twenty frames rather than every frame,
so an arrow can be a third of a second out of date — which is far finer than anything in the field
can actually move.

### The wind indicator

A needle under the crosshair, with the speed beneath it, whenever there is wind where you are.

Screen up is the way you are facing, so the needle points where the wind is pushing you: straight up
is a tailwind, straight down is a wind in your face, and a needle on its side is the crosswind that
carries a ship off its line. This is the opposite of the meteorological convention, where a wind is
named for where it comes from — the question here is which way you are being pushed, not what to call
the weather.

**In a cockpit it shows the wind the ship is flying through, not the wind over the ground.** Those
are the same parked and quite different at speed, and the relative one is what the solver heats the
hull with. On foot there is no grid to ask, so the field's own wind is used. It draws nothing in
space, nothing in still air, and nothing while a menu or the chat box is open.

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
| `TelemetryPlanetProbes` | 0 | Solver steps between planet-wide probe sweeps, or 0 for none. A sweep reads the **wind and the climate** at 72 fixed points — every latitude from −80° to +80° including the equator, eight longitudes each — at five heights, whether or not anything is standing there. Writes `Thermodynamics_PlanetProbes_*.csv`. 360 is a sweep a minute at the shipped clock. |
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
