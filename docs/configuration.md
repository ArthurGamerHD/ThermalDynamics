# Configuration

Settings live in `ThermodynamicsConfig.cfg` in world storage, written with defaults the first time
a world loads and regenerated when `Version` does not match.

> The rules argued here are stated canonically in [rules.md](rules.md): `R8` `C7` `C8`.

| Looking for | Go to |
| --- | --- |
| Per-block, per-planet and per-loop properties | [definitions.md](definitions.md) |
| How far the model is from physics | [realism.md](realism.md) |
| What each setting is scaling | [thermal-model.md](thermal-model.md), [environment.md](environment.md) |
| Driving settings from another mod | [api.md](api.md#settings) |

Every setting can also be changed while the world is running. A change is written into the settings
object every grid already holds and picked up on the next step: heat capacities are rescaled,
coolant loops and room air are rebuilt, and each mechanism reads its own switch. Nothing is written
to disk unless asked.

## Runtime control

| Command | Effect |
| --- | --- |
| `/thermal status` | Collection state, sample stride, live grids, block models, bridges, and how many validation problems have been reported. |
| `/thermal problems` | Every distinct problem the definition and settings validators found this session, once each. The same lines are in the game log. |
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

**Overview** answers the question a wall of sliders cannot — how many settings differ from the
shipped defaults, each of them dotted in front of its label on its own page. It flags a conflict in
three words; **Status** spells it out, lists every changed setting with the shipped value beside it,
and carries the settings digest for comparing against the server.

Three pages carry live figures read from the running grids rather than from the settings that
produced them: **Cost limits** shows substeps granted against substeps asked for and how many blocks
the cap floored, **Pace** shows the hottest block and what the world is venting against what it
makes, and **Debug** shows which overlay is up and whether telemetry is recording.

Overview also carries the menu's one bulk action, a **Defaults** button that returns every world
setting to the value a fresh install ships. The four presentation switches are left alone: what is
drawn on a player's own screen is theirs. The menu is built on the
[Rich HUD Framework](https://github.com/ZachHembree/RichHudFramework.Client) and needs the
**Rich HUD Master** mod (`1965654081`) to be enabled; without it the keystroke says so and the chat
commands remain the way in.

**Settings save themselves, and there is no per-control Reset.** A change applies to the running
session as you make it and reaches the config file about a second later, so a value you can see on
screen is the value the world has and the value it will still have after a reload. A menu that asks
you to confirm what you already did is asking you to do it twice, and a setting that reverts on
reload because a button was missed is worse than either. Starting over is the one case that needs an
action of its own, which is what Defaults is.

Two columns because that is what the page is wide enough for: the framework's tiles are a fixed
300x250, so a third column would have to be scrolled to sideways.

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
| `EnableSuitDamage` | `true` | Heat can hurt a player, not only a block. Off leaves the suit unsimulated and the pass does nothing. |

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

**Measured, not just derived.** `dotnet run --project Thermodynamics.Sim -- frequency` sweeps a
289-block grid with a real stiffness spread, `MaxSubsteps` high enough that the estimate is always
granted, warmed up and best-of-three:

| Freq | Substeps/step | Substeps/s | Demanded | ms/step | **ms/sim second** | Settled K |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 14.00 | 14.0 | 13.4 | 0.0375 | **0.038** | 945.3 |
| 2 | 7.00 | 14.0 | 6.7 | 0.0193 | 0.039 | 945.3 |
| 3 | 5.00 | 15.0 | 4.5 | 0.0140 | 0.042 | 945.3 |
| 4 | 4.00 | 16.0 | 3.3 | 0.0111 | 0.044 | 945.3 |
| 6 | 3.00 | 18.0 | 2.2 | 0.0091 | 0.055 | 945.3 |
| 8 | 2.00 | 16.0 | 1.7 | 0.0065 | 0.052 | 945.3 |
| 12 | 2.00 | 24.0 | 1.1 | 0.0065 | 0.078 | 945.3 |
| 16 | 1.00 | 16.0 | 0.8 | 0.0040 | 0.064 | 945.3 |
| 24 | 1.00 | 24.0 | 0.6 | 0.0041 | 0.099 | 945.3 |
| 32 | 1.00 | 32.0 | 0.4 | 0.0039 | 0.126 | 945.3 |
| 60 | 1.00 | 60.0 | 0.2 | 0.0039 | 0.234 | 945.3 |

`substeps/s` sits at 14–16 from Frequency 1 to 8: that column is the grid's stiffness rather than a
setting. Past that it gets *worse*, because **substeps are an integer**. At Frequency 12 the estimate
asks for 1.1 and pays 2 — 24 substeps a second against a true demand of 13 — while at Frequency 16
it asks for 0.8, pays 1, and drops back to 16. **The curve is not monotonic**, and every frequency
whose demand lands just above an integer pays for a whole substep it does not need.

**It is not a propagation dial either.** Time for the source to reach 90% of its total rise is 18 s
at every frequency from 1 to 60, and the settled temperature is 945.3 K on all eleven rows. A
sixty-fold change in step rate moves neither the transient nor the equilibrium measurably — which is
what substepping is for: the integrated transfer over a second is the same however the second is
chopped up.

**The exception is when substeps are refused.** With `MaxSubsteps` at 1 the step is deliberately too
long and the overshoot clamp decides how much crosses — the most a substep can carry, by definition.
There each step moves a fixed maximum and more steps a second really does move more heat. That is
propagation bought by being wrong, and the shipped `MaxSubsteps` of 64 is what keeps this world out
of it.

**So what should it be?** Not 1, despite the table: `Frequency` is also how often damage lands, how
often the HUD moves, and how quickly a change is felt. What the sweep rules out is the idea that
raising it buys performance. **The shipped value is 4** — a quarter-second step, which is the basis
every substep figure in this documentation is quoted on, and four times a second of responsiveness.

Two things depend on it and move with it. Demand *per step* is proportional to step length, so this
rig asks 3.3 substeps at `Frequency 4` and about 6.7 at `Frequency 2` — enough to clip a small
`MaxSubsteps`, though not the shipped 64. And a step is spread across the frames of its own window,
so **halving `Frequency` halves the per-frame cost of a given step budget**; that is why
`MaxElementVisitsPerStep` is 2,000,000 rather than the 1,000,000 it carried at `Frequency` 8.

### `MaxSubstepsPerBlock`

A step is divided into as many substeps as the **stiffest** block on the grid needs, and every
other block pays for all of them. On a real ship that stiffest block is almost never anything
interesting: on a 42,051-block capital ship, forty-three sixteen-kilogram light fittings asked for
twenty-eight substeps while the five hundred kilogram armour around them asked for one, and the ship
ran at 35 % of real time to pay for the lights.

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

**A cap is a statement about a node's time constant against the step, not a number of substeps**, so
it cannot be quoted without a `Frequency`: a cap of N at `Frequency` 8 reaches the same blocks as a
cap of 2N at `Frequency` 4. **The value is chosen by how many blocks it reaches, not by the error** —
the error stays far below anything a player can see across the whole useful range, while the
population the cap reaches moves suddenly once it stops being a handful of fittings and starts being
ordinary armour. [stiffness.md](stiffness.md#1-a-per-block-substep-cap--the-one-that-is-built)
carries the sweep at both shipped step lengths, driven and diffusing, and the population argument
that puts the knee in 2–4.

Hence a rule worth remembering: **while `MaxSubstepsPerBlock <= MaxSubsteps` the overshoot clamps
never engage**, and every step is genuinely short enough for the grid it is integrating. Raising one
without the other starts refusing steps instead.

It is off by default because it is an approximation, and a mod that models heat should not make
one on a player's behalf without being asked. On a world with large ships in it, turning it on is
the single largest thing that can be done for frame time — and unlike `MaxElementVisitsPerStep`, it
buys the throughput back rather than trading it away: a ship that stops needing more substeps than
the visit budget allows stops being throttled at all.

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
leaves the definition alone, so an untouched world reads whatever `Loops.xml` and `Planets.xml`
say. Move one and it wins from then on, for that world.

`Cubes.xml` is not here: its properties are per block subtype, which a flat setting cannot
express. Edit the file itself.

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

## Replicating temperatures

Settings replicate. **Temperatures now do too**, and until this existed they did not: a client
re-simulated from its own inputs, and a client joining mid-session started from whatever the world
was last *saved* at. The model is dissipative, so a client converges on its own — but it spends 150
to 305 s on the wrong side of a block's critical temperature while it does, against a damage event
that runs a median 37 s from the load to the first block lost. A client could show **safe** for the
entire lifetime of the event that destroyed the block, several times over.

**The protocol is the whole hull once, then the blocks near failing.** When a client has finished
building a grid it asks the server to state it; the server answers with every block's temperature,
in as many messages as the hull needs. After that the server states only the blocks inside the
warning band — the same last 100 K the glow already draws — every `TemperatureSyncInterval`
seconds, and says nothing at all about a hull with nothing near failing.

Both halves are load-bearing, and that is measured rather than assumed. Correcting the band alone
leaves a client misreading for 145 s of a 560 s run *whatever the interval*, because a corrected
block conducts to neighbours that are still stale; the hull once and then the band takes it to
nothing, for one packet — 94 KB on a 9,430-block hull — and 513 bytes a second after it. Replicating
every block continuously does the same job for thirty times the bandwidth. The evidence is in
[known-issues.md](known-issues.md#the-protocol-is-measured-and-the-near-critical-tail-alone-is-not-it).

| Setting | Default | Effect |
| --- | --- | --- |
| `EnableTemperatureSync` | `true` | Whether the server states block temperatures to its clients at all. Off is what the mod did before this existed: every client guessing, and able to show a block safe for the whole time it is burning. |
| `TemperatureSyncInterval` | 5 s | Seconds between band updates once a client has the hull. The whole hull is stated once whatever this says. Sixty is enough to keep a client right about a *stale join*, which decays; five is chosen for a client whose **inputs** are wrong, which does not decay and re-diverges between updates. |

**The cost is the size of the emergency.** A quiet ship sends nothing — an empty band is not
transmitted. A burning one sends ten bytes per block near failing per interval per client in range,
which on the hottest hull in the corpus is about 6 KB/s while it burns. Blocks outside a client's
sync distance are never sent, because there is no grid on that machine to correct.

**It travels on its own secure channel**, two above the shared one, for the reason
[Changing settings from a client](#changing-settings-from-a-client) gives: the engine's secure
handler supplies a sender the transport verified and a from-the-server flag a client cannot forge.
A client must not be able to write temperatures onto another client's simulation, and the server
must not serve a hull to a player id somebody else named. A snapshot asked for twice inside five
seconds is refused, so a client asking in a loop cannot make the server transmit a hull per frame.

`/thermal sync` prints what this has sent, asked for and applied on the machine it is run on. Run it
on both: registration and addressing cannot be tested outside a session, and the pair of counters is
what says which half is not moving.

**What is still wrong after it.** The correction overwrites this machine's guess with the server's
answer, so it fixes a client that started from the wrong state. It does not fix a client whose
*inputs* are wrong — block power arriving late, a dropped solver backlog, settings that never landed
— because those re-diverge as soon as the packet is applied. Against the measured combined case the
correction more than halves the standing error and does not remove it. Those are tracked separately;
see [backlog.md](backlog.md) `F17` to `F23`.

## What the dials trade against each other

There is one configuration and no presets, so tuning a world is moving individual settings — and two
relationships bound what any move can buy. Neither can be tuned around.

**Heat spreads as the square root of the arithmetic you spend on it.** Diffusion is a square-root
process: a front crosses blocks at a rate proportional to `sqrt(substeps per second)`, while cost is
proportional to substeps per second outright. Measured, holding everything else: 1 substep/s gave
0.5 blocks/s, 4 gave 1.0, 8 gave 1.4, 16 gave 2.0 — square root to two figures. **Doubling how
responsive a world feels costs four times as much.**

**But where you spend the substeps changes the exchange rate by about three times.** There are two
ways to move more heat per second. The accurate one raises `HeatTimeScale` and grants the extra
substeps its stiffness demands. The approximate one raises `HeatTimeScale` *and refuses* the substeps
with `MaxSubsteps`, letting the overshoot clamps decide how much crosses — which is the most a
substep can carry, by definition. Measured: the clamped route delivered 0.125 blocks/s per substep/s
against 0.045 for the accurate one. **The shipped configuration takes the accurate route almost
everywhere**, which is what `MaxSubsteps 64` is for — but not quite everywhere: in thick air at
200 m/s about a fifth of a real population demands 73.4 and is refused. What that refusal costs has
been measured and it is **0.028 K** on the hottest block over 600 simulated seconds, because 1.15×
over-subscribed is the free end of that trade. See
[stiffness.md](stiffness.md#what-refusing-the-demand-costs).

**The far end is a wall, not a slope.** Past roughly `HeatTimeScale / Frequency = 4000` the clamps
are carrying the entire step and blocks start being driven to the ambient floor. The shipped ratio is
56. `Validate()` warns above 4,000, and the settings menu shows it.

So, knob by knob:

* **`Frequency` sets responsiveness and cost together — but only while `MaxSubsteps` is 1**, or the
  grid is soft enough that the estimate never rises above one substep. On a grid stiff enough to ask
  for real substeps it cancels out of the cost entirely; reach for `MaxSubstepsPerBlock` there
  instead. See [above](#frequency-is-not-the-cost-dial-it-looks-like).
* **`HeatTimeScale` sets how much a substep carries.** Raise it until the clamps engage; past that it
  buys nothing, because the clamp is already moving all it can.
* **`MaxSubsteps` chooses accuracy or speed.** High means the estimate is always granted and nothing
  clamps, which is what ships. `1` means every step is deliberately too long and the clamps carry it.
* **Keep `HeatTimeScale / Frequency` under 4000.** This is the safety rail. Everything else is taste.
* **`EnableRoomAir` and `SolarSelfShadowing` are the two mechanisms that cost most** for what a player
  notices, and both ship on.

Both clamps must stay on for any of this. `ClampConductionOvershoot` and `ClampEnvironmentOvershoot`
are what make a deliberately-too-long step bounded instead of divergent — with them off, a fast clock
at one substep reaches 10^22 K in twenty seconds.

**Starting over is one action.** The settings menu's Overview carries a **Defaults** button that
returns every world setting to the value a fresh install ships, leaving the four presentation
switches alone. There is no per-control reset and no Save button, because a change applies as it is
made and reaches the config file a second later.

## Trading simulation speed for heat transfer

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
| `Frequency` | 4 | Solver steps per simulated second. The integration step is `1/Frequency`, so 4 is a quarter-second step — the basis every substep figure in this documentation is quoted on. Whether lowering it cuts cost depends on the grid — see below. |
| `SimulationSpeed` | 1 | Simulated seconds per real second, applied by running more steps rather than longer ones. Linear in CPU. |
| `HeatTimeScale` | 225 | How much faster than real physics heat moves. Divides every heat capacity. |
| `MaxElementVisitsPerStep` | 2000000 | Most element visits one step may make — substeps times its links plus four times its nodes — before the step is shortened to fit. 0 removes the bound. **Moves with `Frequency`**: a step is spread across the frames of its window, so this figure and the step rate together set the per-frame cost. See below. |
| `MaxSubstepsPerBlock` | 0 (off) | Most substeps any single block may demand of the whole grid before it is treated as heavier than it is. The cheapest large win there is on a real ship. See below. |
| `MaxSubsteps` | 64 | Most substeps one step may be cut into, whatever the grid asks for. A grid refused here integrates a step too long for its stiffest block, and the overshoot clamps carry the difference. |
| `ClampConductionOvershoot` | `true` | Caps each exchange at the energy that equalises the pair. Off reproduces the original unbounded solver. Skipped, at no change to the result, on any step short enough that no element can overshoot — see [benchmarks.md](benchmarks.md#the-overshoot-clamp-ab). |
| `ClampEnvironmentOvershoot` | `true` | The same for radiation and convection: neither may carry a block past ambient in one substep. This is what bounds a step that is deliberately far too long. |
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

**A value left at its shipped figure does not override the definition.** The file still decides.
Move one and it wins from then on, across every loop definition in the world.

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
until a value is moved off its shipped figure. See [environment.md](environment.md) for the
model they parameterise.

| Setting | Default | Effect |
| --- | --- | --- |
| `PlanetDayTemperature` | 294.261 K | Equatorial daytime air. |
| `PlanetNightTemperature` | 283.15 K | Equatorial night air. |
| `PlanetPoleTemperatureDrop` | 40 K | Span from equator to pole, interpolated on cos(latitude). |
| `PlanetAmbientLapseRate` | 4 K/km | How fast the air cools with altitude. |
| `PlanetAmbientLagSeconds` | 45 s | First-order lag on the ambient target, which is what makes the day peak after noon. **The fallback**: the lag is a share of this world's own day wherever the day has been measured, and this is what runs until it has been. See [The lag is a share of the day](environment.md#the-lag-is-a-share-of-the-day). |
| `PlanetConvectionCoefficient` | 50 W/(m²·K) | Convective coupling in full atmosphere, scaled down with air density and up with wind. |
| `PlanetUndergroundConvectionCoefficient` | 2 W/(m²·K) | The same for a grid buried in rock, which is a far worse heat sink than moving air: `2k/D` for rock at 2.5 W/(m·K) over a 2.5 m block. Crossed over the first five metres of burial, and neither wind nor weather multiplies it. |
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

## The suit

A player in a burning compartment used to be the one place heat stopped being consequential. These
settings are the suit that decides otherwise, and they describe a machine rather than a threshold:
the occupant is held at body temperature by a cooler, so a hot room is survivable while the cooler
keeps up and lethal past the point where it does not.

| Setting | Default | Effect |
| --- | --- | --- |
| `SuitConductance` | 2.5 W/K | How well the room reaches the occupant through a sealed suit. With the rating below, this is what sets the hottest room a player can stand in indefinitely. |
| `SuitHeatCapacity` | 240,000 J/K | The occupant and the suit together — about eighty kilograms of mostly water. Divided by `HeatTimeScale` like every block's, so a player heats on the same clock as the ship. It is the whole of how long a dash through a hot room can be. |
| `SuitCoolingWatts` | 500 W | Heat the suit can move, either way. A real EMU's sublimator handles about this. |
| `SuitCriticalTemperature` | 315.15 K | Interior temperature above which the occupant is being hurt. 42 °C, where heat stroke becomes life-threatening, and five degrees above where the suit holds them. |
| `SuitDamagePerKelvin` | 1 | Hit points a second per kelvin of overshoot. The same shape a block is damaged with. |

**The survivable room temperature is derived, not authored.** It is where the rating exactly cancels
what leaks in — `310 + 500/2.5` = **510 K**, about 237 °C — so moving either of the first two moves
it, and `SuitThermal.SurvivableKelvin` is the one place it is computed.

**An open helmet is a second heat path, not a second rule.** Breathing puts the room against the
lung surface, which no suit wall stands in the way of, so the conductance goes up tenfold and the
same cooler is asked to shift ten times as much. There is no separate setting and no separate
threshold: the survivable temperature falls out at `310 + 500/25` = **330 K**, about 57 °C, which is
where breathing hot air stops being merely unpleasant.

**Only room air is read.** A player outside a pressurised compartment the mod has mapped is not
simulated — no planet surface, no vacuum, no open-frame ship. That is the scope of what room air
already carries, and the rest is [backlog](backlog.md) `C17`.

**The cooler is free**, which is a limit rather than a decision: `IMyCharacter` exposes
`SuitEnergyLevel` to read and nothing to write, so no mod can charge a player for running it. What
the mod can see is a flat suit, and a flat suit does not regulate.

## Wind

The game's own wind figure is `MaxWindSpeed × airDensity` — one number per planet, scaled linearly
by altitude, identical at the pole and the equator, with no direction and no time of day. These
settings drive the model that replaces it. See [environment.md](environment.md) for what each one is
and where it comes from.

| Setting | Default | Effect |
| --- | --- | --- |
| `WindRoughnessLength` | 0.03 m | Roughness length z0 — about a tenth of the height of whatever covers the ground. Sets how steeply wind strengthens with height near the surface. **The ground under the grid answers first**: the material table carries a roughness for every surface it classifies — 0.0002 ice, 0.0005 snow, 0.003 sand, 0.03 grass, 0.05 rock, 0.5 forest — and this setting is what unclassified ground gets, which is an airless world, a modded voxel or a grid over no surface at all. |
| `WindGradientHeight` | 600 m | Height at which wind stops strengthening: the top of the boundary layer. Above it the profile is flat and the air density takes it down from there. **Capped by the air there is** — a boundary layer cannot be taller than the atmosphere over the ground under the grid, and several shipped worlds have less than 600 m of it. |
| `WindDiurnalAmplitude` | 0.35 | How far the daily cycle swings wind either side of its mean. Surface wind peaks in the afternoon; wind above the crossover peaks before dawn. 0 disables the cycle. |
| `WindDiurnalCrossover` | 80 m | Height at which the daily cycle vanishes. Below it the surface cycle, above it the nocturnal jet, fully reversed by twice this height. |
| `WindTerrainInfluence` | 1 | How much the shape of the ground affects wind: speed-up over rises, shelter behind ridges, steering along valleys. 0 leaves the wind ignorant of terrain. |
| `WindTerrainRadius` | 300 m | How far out the land around a point is read. The scale of landform the wind is allowed to notice. |
| `WindSlopeStrength` | 1 | Slope winds, 0..1: air running **up** a mountain by day and draining back **down** it at night. A thermal flow the ground makes rather than something it does to an existing wind, so it blows on a still day — and a real wind overruns it. Costs about 46 ns a sample, because the terrain it needs is already read. |

## Presentation

All client side, and all off by default but three: the two natural-feedback switches and the wind
indicator, which are what a player meets while playing rather than diagnostics for debugging.

| Setting | Default | Draws |
| --- | --- | --- |
| `DebugTextOnScreen` | `false` | Crosshair readout: temperature, per-mechanism watts, block constants, environment, grid totals, room classification, raw surface bits. Switching it on also makes the solver record per-mechanism watts, which is not free. |
| `DebugSolarRaycast` | `false` | Draws the sun ray from each grid, white when lit and red when occluded. |
| `DebugWindRaycast` | `false` | Draws the relative wind each grid is flying through, as a line from the grid scaled by its speed: green in still air, red once the grid is over `FrictionAtSpeedsAbove` and the leading face is heating. |
| `DebugWindOverlay` | 0 | Which view the wind map opens a session on: 0 off, 1 the lattice around you, 2 the whole planet. |
| `DebugWindIndicator` | `true` | The wind needle and speed under the crosshair. |
| `HeatGlow` | `true` | Blocks glow over the last 100 K before their own critical temperature, full at it and above. |
| `HeatWarningSound` | `true` | A cue in the cockpit as a block comes up on its own rating and as it crosses it. Heard only by the player at the controls. |
| `RoomOverlayMinKelvin` | 253.15 K | Bottom of the room view's colour span, −20 °C. |
| `RoomOverlayMaxKelvin` | 323.15 K | Top of the room view's colour span, 50 °C. |
| `DebugBlockOverlay` | 0 | Which view the block overlay opens a session on: 0 off, 1 temperature, 2 solar watts, 3 exposed faces, 4 friction watts, 5 rooms. |
| `DebugOverlayMaxBoxes` | 12000 | Boxes the block overlay may draw in one frame. Beyond it the overlay draws the part of the grid nearest the camera. 0 draws nothing. |

### Natural feedback

Two channels, and between them they say how close a block is to failing and how hot it got.

**`HeatGlow` is the last hundred kelvin before a block's own critical temperature.** Nothing below
that, a straight ramp through it, full at critical and above. It is a narrow window on purpose: a
glow means this block is about to go, not that it is warm. The band is a fixed 100 K rather than a
share of the rating, so the same distance from failure looks the same on a decorative block and on a
large thruster. Nothing the game ships glows from the weather — the hottest planet in `Planets.xml`
runs a 390 K day, and the coolest block type is rated 583 K, so its band starts at 483 K.

**The colour is separate and stays physical**: the Planckian locus by absolute temperature, so a
block glowing at 500 K is deep red and one at 2,000 K is orange. Brightness says how close to
failing, colour says how hot it actually got. See
[document-of-intent.md](document-of-intent.md#natural-feedback--built) for why the brightness is not
incandescence.

**`HeatWarningSound` is the same warning in sound**: a cue about three seconds before a block
crosses its rating and a distinct one as it crosses, heard only by the player at the controls. It
reaches what the glow cannot — a block with no emissive material in its model cannot glow whatever
its temperature.

The lead is a forecast rather than a straight line: a block levelling off below its rating is never
cued, however fast it is warming at the moment. A block heating *faster* than it was has no
equilibrium to read, so a straight line answers there, which warns early rather than late.

**With both off, the cost is nothing**, and with both on and nothing hot it is one comparison per
grid per second — the hottest block against the lower of where the coolest block would start glowing
and where it would start being watched.

### The wind map

**Ctrl+Shift+W** cycles it: off → local → planet → off. `/thermal wind` does the same from chat.

The game has no wind field — `MyPlanet.GetWindSpeed` returns the planet definition's maximum scaled
by air density, the same figure at the pole and the equator — so this mod invents one, and until
this view there was no way to look at it. See [environment.md](environment.md#wind) for what
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

**The sample count is the cheapest rung, and it is measurably not the dial worth spending on.**
`SolarOcclusionSamples`, `SolarGridShadows` and `SolarSelfShadowing` compose into a ladder that
nothing names, and two of the three already ship at the top. The third ships at one ray from the
grid's centre, which [backlog.md](backlog.md) `A9` ranked second of everything open on the ground
that a ship flipping between fully lit and fully dark is a difference a player can see. **Measured,
it is the wrong dial**, and `dotnet run --project tests/Thermodynamics.Sim -- occlusion` is the
measurement.

Flying hulls of every size through an Earthlike's terminator at the shipped cadence, averaged over
where the test schedule falls:

| Hull | Surplus sunlight, 1 sample | at 9 samples | Worst error, 1 → 9 | Partial readings a crossing, at 9 |
| --- | ---: | ---: | ---: | ---: |
| 25 m | 1.51 s | 1.54 s | 0.977 → 0.968 | 0.08 |
| 300 m | 1.53 s | 1.50 s | 0.889 → 0.769 | 0.62 |
| 600 m | 1.56 s | 1.53 s | 0.778 → 0.551 | 1.21 |
| 2,500 m | 1.41 s | 1.46 s | 0.551 → 0.333 | 4.92 |

**Nine times the raycasts changes the sunlight a hull absorbs by nothing.** A single ray from the
centre is *unbiased*: it goes on reporting light after the leading end is dark and reports dark
before the trailing end is, and over a crossing the two cancel. What the extra samples fix is the
**worst error** column — how wrong the reading is at a moment, which is what a player sees — and
only on a hull long enough that a test lands while it is still straddling. Below about 600 m it
almost never does.

**The surplus is the interval, and it is exactly half of it.** A test that ran before the crossing
keeps saying *lit* until the next one, and a grid meets every phase of that schedule equally often:

| `SolarOcclusionInterval` | Every | Surplus sunlight a crossing |
| ---: | ---: | ---: |
| 1 step | 0.25 s | 0.10 s |
| 4 steps | 1.00 s | 0.48 s |
| 12 steps *(shipped)* | 3.00 s | 1.53 s |
| 48 steps | 12.00 s | 5.96 s |

So the two dials cost the same thing — raycasts — and per raycast spent **the interval buys strictly
more fidelity than the sample count**. Neither default moves here, because both raise a cost that has
never been measured in a session: `A9` needs `F5` first. What has changed is which dial the answer
is expected to be.

**And the rung above both is worth about a two-hundredth of a kelvin a metre of hull.** Neither dial
reaches the error that comes from applying one answer to a whole ship — the leading end told it is
lit while it is dark and the trailing end the reverse — and resolving the planet's shadow *per face*
is the only thing that would. Read per block instead of per hull, tested every step so the cadence
contributes nothing, that error is:

| Hull | Worst block, geometry alone | at the shipped 12-step cadence |
| --- | ---: | ---: |
| 25 m | 0.17 K | 1.47 K |
| 150 m | 0.73 K | 1.97 K |
| 600 m | 2.77 K | 4.07 K |
| 2,500 m | 11.33 K | 12.54 K |

*One lit face of a 500 kg steel-plate block gains 0.905 K a second of sunlight at the shipped clock,
which is the conversion every figure above rests on.*

**It is linear in length and it is under a kelvin for anything under 200 m**, so on the ships people
build it is a fraction of what the cadence already costs — below 300 m the cadence is the larger half
of the error, and above it the geometry is. That is what decides the top rung: it is a change about
how long ships are rather than about how good the model is, and nothing in the shipped configuration
moves for it. `OcclusionLadderTests` pins the rate and the crossover.

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
replicates, and switching it off leaves no trace.

A box is six transparent quads and twelve lines, so a forty thousand block ship drawn whole asks
the renderer for over seven hundred thousand billboards a frame. Two things bound that. Boxes
outside the camera's view cone are not drawn, which changes nothing on screen. What is left is held
to `DebugOverlayMaxBoxes` by a radius fitted each frame to the count, so a large ship is drawn out
to the distance that fits the budget and a small one is drawn whole. The report's *Block overlay*
section records what it cost and how much was held back. It replaces the four `Debug*BlockColors` modes,
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

---

## Where the settings surface is going

The menu, the config file and the definition files are one surface to a player and three to the
code. Two of the three definition files are settings now — [the loop and planet values](#the-definition-files-from-the-menu)
— and what is left is the third, the config file's own shape, and a Status page worth opening.

**Per-subtype block overrides.** `Cubes.xml` is 114 entries — 96 per-type defaults and 18 per-subtype
— and it is different in kind from the other two: 656 authored values, whose interesting ones belong
to the block a player is looking at rather than to a list they scroll. Properties are cached per definition in `ThermalBlockCatalog`, so
changing one at runtime needs the cache invalidated and every node of that type refreshed. A mod
folder is read-only in a workshop install, so this writes a per-world override layer into world
storage like the other two, covering any subtype rather than a fixed list.

**Status becomes the panel worth opening.** Today it lists what changed, which the Overview already
counts. It should be the mod's own report:

* **Cost**, per stage rather than as one number: topology, room mapping, exposure, solver, room
  pressure, with the same figures the telemetry report carries.
* **Load**, as a projection rather than an instant: substeps demanded against granted, what the
  per-block cap is flooring, and what a change to the caps would cost — the sweep
  [load-and-hitching.md](load-and-hitching.md#in-the-field) does by hand.
* **Faults**: settings that cancel each other, grids running below real time, compartments the game
  seals and this model does not, pumps in a ring that oppose each other, definition entries that
  fell back to the default because their `TypeId` was wrong. **Every one of these has been a real
  defect at least once, and each was found by reading a dump rather than by the mod saying so.**

Its form is still open and wants prototypes rather than a decision on paper. Rich HUD has no chart
control: a sparkline can be drawn as text in a `TextPage`, which is cheap and honest; a real plot
means a custom HUD element, which is a different size of job.

**The config file follows the menu.** The file is flat — every element in one list, in the order
they were added. If the menu is organised by system then the file should be too, because they are
read by the same person for the same reason:

```xml
<Solver>
  <Frequency>8</Frequency>
  <MaxSubsteps>64</MaxSubsteps>
</Solver>
<CoolantLoops enabled="true">
  <LargeGridFlowRate>10</LargeGridFlowRate>
</CoolantLoops>
```

Two things this must not break:

* **A world's existing values.** Defaults live on the fields, so a reader that finds nothing leaves
  them alone — which means a restructure silently resets every tuned world unless the old flat shape
  is read first and migrated. That is the whole risk of this step.
* **The names.** `/thermal set`, the mod API and the sync all address settings by name. Grouping may
  change where a name sits in the file; it must not change the name.

Each step stands alone and each is separately revertible, which is why the file restructure — the
one with a migration risk — is late rather than first.

---

## Change log

| Date | Change |
| --- | --- |
| 2026-08-23 | **Priced `A9`'s unbuilt top rung**, which had been described and never measured: resolving the planet's shadow per face removes an error linear in hull length, about a two-hundredth of a kelvin a metre — 0.73 K on a 150 m hull against the 1.97 K the cadence already costs it, and 11.33 K on a 2,500 m one. Below 300 m the cadence is the larger half. Added the table to [External shadow](#external-shadow). |
| 2026-08-23 | `HeatGlow` is the last 100 K before a block's own critical temperature, superseding the two entries below it. The colour is unchanged and still absolute. |
| 2026-08-23 | `HeatGlow` is back to incandescence — brightness and colour both functions of temperature alone — and the rating-keyed form of the entry below is withdrawn. |
| 2026-08-23 | `HeatGlow` is a block's distance from its own rating rather than an absolute temperature: nothing at comfortable temperatures, full at critical and above. The colour is unchanged and still absolute. |
| 2026-08-23 | Added `HeatGlow` and `HeatWarningSound`, the two natural-feedback switches ([backlog.md](backlog.md) `B25`). Both client side and both on by default, which makes them the first presentation entries here that are on because a player is meant to meet them rather than because they were asked for. |
| 2026-08-22 | `PlanetAmbientLagSeconds` is the fallback rather than the whole answer: the lag is a share of the world's own day wherever the day has been measured ([backlog.md](backlog.md) `C6`). |
| 2026-08-22 | `WindRoughnessLength` is the fallback rather than the whole answer: the ground material under a grid now sets its own roughness, which is the one figure in that table with a published table behind it ([backlog.md](backlog.md) `B16`). |
| 2026-08-22 | Said that `WindGradientHeight` is capped by the atmosphere over the grid's own ground ([backlog.md](backlog.md) `B20`). |
| 2026-08-22 | Added `PlanetUndergroundConvectionCoefficient`. A buried grid exchanged at the coefficient for moving air, which made digging in the best cooling in the game ([backlog.md](backlog.md) `A16`). |
| 2026-08-22 | Added [The suit](#the-suit) and its five settings, plus `EnableSuitDamage`. A player in a burning compartment was the one place heat stopped being consequential ([backlog.md](backlog.md) `B10`). The survivable temperature is derived from the rating and the conductance rather than authored, and the three things the model deliberately does not do are `C16`, `C17` and `C18`. |
| 2026-08-22 | Added `/thermal problems`, and the count of them to `/thermal status`. The two validators the mod carried had never been called from anywhere the game runs, so an emissivity above one or a substep long enough to clamp away a whole step was diagnosed correctly and told to nobody ([backlog.md](backlog.md) `A19`). |
| 2026-08-22 | Said in [External shadow](#external-shadow) that the shipped occlusion default is the cheapest rung of the ladder those three settings compose, and pointed at [backlog.md](backlog.md) `A9` for the ladder and the default it asks for. The settings table is unchanged — it describes what ships, and what ships has not moved. |
| 2026-08-22 | **An existing world keeps the values in its config file.** The file's `Version` is unchanged, because the shape did not change and regenerating it would throw away real customisation — so a world created before this pass still runs at `Frequency` 8 and the old visit budget until someone moves them, and the menu's **Defaults** button is the one action that takes it to the new values. |
| 2026-08-22 | **Removed the five settings profiles.** There is one configuration now, and it is the most faithful one the model has: every mechanism on, no approximation switched on for anybody, `SimulationSpeed` 1 and `Frequency` **4** against the 8 it shipped at. `MaxSubstepsPerBlock` stays 0 and `MaxSubsteps` 64, so nothing is refused the substeps it asks for. `/thermal profile` is gone and the menu's profile buttons are one **Defaults** button, which is what applying a profile was actually being used for. `TheDefaultsAreTheMostFaithfulConfiguration` holds the claim so it cannot quietly stop being true. |
| 2026-08-22 | Moved `MaxElementVisitsPerStep` from 1,000,000 to **2,000,000** with `Frequency`, keeping the per-frame cost identical rather than the per-step one. A step is spread across the frames of its own window, and a frame does `budget × frameSeconds × Frequency` of work, so halving the rate halves what a given budget costs per frame — and leaving the budget alone would have throttled an 8,904-block ship to 73 % of real time where it previously ran at 100 %, which is an approximation nobody asked for. Old value kept visible here (`E11`). |
| 2026-08-22 | The tuning guidance that went to `profiles.md` last pass comes back as [What the dials trade against each other](#what-the-dials-trade-against-each-other), which is settings guidance and now has nowhere else to be. |
| 2026-08-22 | Gave up this page's second account of the profiles to [realism.md](realism.md), which is the page about them: the measured cost ladder, *Where the extremes lie* and *Designing your own* all moved, and with them a duplicate of the `HeatTimeScale` substep-demand table. What stays here is the four settings each preset sets. Replaced `MaxSubstepsPerBlock`'s sweep table with a pointer to [stiffness.md](stiffness.md#1-a-per-block-substep-cap--the-one-that-is-built), which carries the same sweep at both shipped step lengths — the copy here had the same floored-block counts against different speeds and errors, which is a table that had drifted from the one it was taken from. Cut *The settings surface, and where it is going* down to what is left of it: three of its five subsections described the menu this page already documents two screens above. |
| 2026-08-22 | Corrected two statements about `Frequency` that contradicted this page's own reference table: the prose called 4 the shipped value where the table says 8, and read a rig's demand as though it were the shipped configuration. The nested-config example showed a `MaxSubsteps` of 16 rather than the shipped 64. |
| 2026-08-22 | Absorbed `settings-redesign.md`, whose subject is this page's subject, as [The settings surface](#where-the-settings-surface-is-going), with the completed steps restated as what the menu now is rather than as a plan. Took the `Frequency` sweep from `field-tuning.md` into the section that already argued the arithmetic, so the derivation and the measurement sit together. Added the standard header and this log. |
| 2026-08-22 | Brought the loop and planet definitions into the menu as world settings, replicated and reachable from `/thermal set` and the mod API. |
| 2026-08-19 | Documented the twenty-one settings the reference had never listed — the whole `Loop*` and `Planet*` families, `MaxSubsteps` and `ClampEnvironmentOvershoot` — and added `ConfigurationDocTests`, which fails when a setting exists in one place and not the other. Let an admin change world settings from a client, over a secure channel rather than the shared one. |
| 2026-08-18 | Added the five profiles as a ladder on two axes, and fixed the clamp defect that had to be fixed before they were safe. *(The profiles were removed on 2026-08-22; the clamp fix stands — see [realism.md](realism.md).)* |
| 2026-08-17 | Moved the settings menu onto Rich HUD at Ctrl+Shift+S. |
| 2026-08-12 | Opened the reference against `ThermodynamicsConfig.cfg`. |
