# Known issues and limits

What this mod deliberately does not model, what is still open, and the failure patterns worth
carrying forward. Open work is tracked one line each in [backlog.md](backlog.md); this page carries
the argument behind each entry.

> The rules argued here are stated canonically in [rules.md](rules.md): `E2` `E9` `D3` `D5` `D6`,
> and the principle P14 the deliberate limits follow from.

| Looking for | Go to |
| --- | --- |
| Every open item, categorised, one line each | [backlog.md](backlog.md) |
| What the model does now | [thermal-model.md](thermal-model.md) |
| What a grid costs, and what makes it stutter | [benchmarks.md](benchmarks.md), [load-and-hitching.md](load-and-hitching.md) |
| What the corpus survey found about balance | [balance.md](balance.md) |

---

## Deliberate limits

Each of these is a simplification taken on purpose, with the price written down. A limit is not a
defect; a limit nobody wrote down is.

**The lab never destroys a block, so a peak temperature above critical is not a prediction.** The
solver raises an `OverheatEvent` when a node passes its critical temperature, but applying that
damage is `bound.Block.DoDamage` in `ThermalGridSimulation` — the game layer, which no harness runs.
In the lab an overheating block is therefore never removed: it keeps generating, keeps conducting to
neighbours, and keeps climbing for whatever remains of the clock. In game it would be gone in
seconds and would stop producing.

That censors every number drawn from the right tail. The corpus survey reports one ship at
541,648 K and a parked mobile base at 31,151 K; neither is a temperature the mod can reach, because
the block that got there does not survive to be measured. **Read any peak above critical as "this
block dies" and nothing further**, and treat the population's peak statistics — p95, p99, max — as
describing the harness rather than the mod. `over_critical`, `over_share` and `seconds_to_critical`
are unaffected: they are decided at the crossing, before the divergence matters.

`seconds_to_first_loss` is unaffected for the same reason and only for the **first** loss. Up to the
moment a block's hit points run out the harness and the game agree exactly, because nothing has been
removed from either. After it they part company, which is why the column records the first loss and
no count of losses: a second one would be measured on a ship the game would no longer have.

Closing it properly means the harness modelling destruction — removing the node, re-deriving the
graph, and stopping the source — which is a solver-wide change to answer a question the censored
reading already answers.

**Planet and asteroid shadow is per grid; only other grids shade individual faces.** A grid's own
shadow is per face (`SolarSelfShadowing`) and so is another grid's (`SolarGridShadows = full`), but a
planet's or an asteroid's dims the whole grid by the share of sampled rays that were blocked
(`SolarOcclusionSamples`). A capital ship crossing a terminator therefore ramps rather than
switching, but never carries a shadow edge across its own hull.

**This is no longer filed as a limit taken and left.** It is now the top rung of
[backlog](backlog.md) `A9`, which asks for occlusion as an ordered ladder of configurations from a
single centre ray to per-face shadow for every occluder, with the most realistic rung shipped by
default. The cost objection that justified the limit — *per-block would need the ray count to scale
with block count* — survives for terrain and voxels and does **not** hold for the planet, which is
the occluder that matters: `OcclusionMath.IsOccludedBySphere` is a normalise, a dot and an `atan`
with no ray in it, so evaluating it per exposed face is arithmetic on a pass the self-shadow already
walks.

**Point sources are not occluded.** A registered heat source heats through walls and through other
ships. Occlusion is left to the host, which can simply not register a source it knows is hidden.

**A room that changes shape loses its air temperature.** Rooms are matched across rebuilds by their
lowest cell. Building inside a compartment gives it a fresh air mass at the temperature of its
walls. The same key carries air across a save, so a compartment rebuilt while the world was closed
comes back at the temperature of its walls rather than the one it was saved at.

**Build state does not change a block's thermal properties.** A block at 10% construction has the
same mass, heat capacity, conductivity and mounting as a finished one, and nothing notifies the
simulation when a block finishes building. This is a deliberate simplification rather than an
omission: a partially-built block is a transient a player watches for seconds, the thermal
difference would be invisible next to the heat its neighbours carry, and tracking it would mean a
per-block event on the construction path plus a rule for what a half-built block conducts. The
machinery to support it exists — `RefreshBlock` handles a geometry change correctly and cheaply —
so this can be revisited by hooking build state to it, and nothing else would need to change.

**A surface is two constants, not a spectrum.** Emission and absorption are separate numbers now —
`Emissivity` and `SolarAbsorptivity`, the second following the first unless a definition declares it
— so a block *can* be made shiny to the sun and black to space ([backlog](backlog.md) `B27`, closed).
What is still a simplification is that each is one constant: a real selective surface is a curve
against wavelength, and this is that curve reduced to a value in the visible and a value in the
thermal infrared. No shipped block declares the two apart yet.

**A coolant loop's `Conductivity` is a 0…1 quality against a 200 W/(m·K) reference**, where a
block's is the real W/(m·K) figure a materials table gives. The fluid-to-wall path is convective,
and its honest dial is a heat transfer coefficient in W/(m²·K) — a change to the loop equations
rather than to a number. See [definitions.md](definitions.md#conductivity-is-in-real-wmk).

**Mount coverage is combined as independent fractions.** Where both sides of a joint are only partly
mounted, the bolted area is `coverage_a × coverage_b`. The model records coverage per face, not per
cell, so it cannot know whether two partial mounts line up.

**Radiators cannot be inline loop segments.** A radiator sheds heat when a pipe's sink face is
pressed against it, which works and is what the `radiator` scenario measures. It has no coolant
ports of its own, so a loop cannot run *through* one. The block is 1×5×2 with mount points only on
its top and bottom, so adding ports needs the port geometry checked against the model.

**The underground core gradient is out of reach in ordinary play.** Below `SealevelDeadzone` the
rock warms toward `CoreTemperature`, and the shipped deadzone is 2 km below sea level — deeper than
SE's voxels go. The model is right and the tuning lever is documented; as shipped, every reachable
depth reads a flat `UndergroundTemperature`. Whether the default should be a few hundred metres is a
balance question, not a code one.

---

## Open defects

**Temperatures are not reconciled between server and clients.** Clients run their own simulation
from the same inputs and reach the same answers, but nothing reconciles them: a client that joins
mid-session starts from saved temperatures, and divergence is never corrected. Damage and settings
are server authoritative, so nothing a client believes changes what happens to the ship.

**Measured, and it is not as cosmetic as it reads.** `-- drift` runs the same hull twice from states
a stated distance apart and watches the disagreement decay — the model is dissipative, so a client
converges on the server without anybody correcting it, and the question is how fast. On the census
hull in shadow, a client joining sixty simulated seconds behind:

| Hull | Wrong by, at the join | Under 10 K | Under 1 K | Wrong about *critical* |
| --- | ---: | ---: | ---: | ---: |
| 1,500 blocks | 37 K | 75 s | 190 s | **150 s** |
| 8,904 blocks | 112 K | 200 s | 375 s | **305 s** |

Air is much faster than vacuum — 75 s to agree within a kelvin on a planet surface against 190 s in
shadow — because convection is a far stronger path to a shared ambient than radiation is. Staleness
saturates: a state from thirty minutes ago is no worse than one from five, because the hull had
settled by then.

**The last column is the finding.** For two and a half to five minutes a client's readout is on the
wrong side of a block's critical temperature — and [balance.md](balance.md#damage-arrives-too-fast-to-be-played-around)
measures the whole event at a median 8.9 s to the crossing. A client can therefore show *safe* for
the entire lifetime of the event that destroyed the block, which is the readout being wrong about
the one thing it is for.

**And a per-grid correction would not fix it.** The error is not a uniform offset: at the join the
worst block is 105 K out against a mean of 20 K, so a single scalar per grid would correct the
armour and leave the blocks that matter wrong. What has to be replicated is the near-critical tail —
1,700 to 2,000 blocks of 8,904 on this hull, which makes 12.1 kW a block against a real median of
335 W and is therefore an upper bound on how long that tail is. Tracked as [backlog](backlog.md) B4.

Settings and pump controls *are* replicated. `SENetworkAPI` 2.0 runs on channel `30323` with three
properties on it: the world's settings and the two pump throttles.

The settings property is seeded with the loaded settings at construction rather than left at null,
because **a null value is never transmitted** — a property sitting at null answers a joining
client's fetch with silence, and since the server publishes only when a setting *changes*, a world
where nobody touched the config would leave every client on the shipped defaults for the whole
session.

A received value is **copied into the live settings object rather than swapped for it**. A grid
takes its core settings once, at construction — `new ThermalSimulation(Settings.Instance.ToCore(),
Model)` — and notices later changes only through that object's `Revision`, so replacing
`Settings.Instance` would leave every grid already on the client running the settings it was born
with. The copy goes through `Settings.Names()`, the same list the settings menu uses, minus
`Settings.ClientOwned` — the four presentation switches a client owns for itself, since a server has
no business choosing which overlay is on someone else's screen. That leaves 77 of the 85 serialized
fields replicated; of the other eight, seven are the client-owned presentation switches and the
last is the config file's `Version`, which describes the file rather than the world.

**Admin changes from a client travel on their own secure channel, deliberately.** SENetworkAPI
registers the game's non-secure message handler, so every sender id it reports is a field the sender
wrote — a modified client can claim to be anyone, and the API's own documentation says not to gate
admin actions on it. Settings requests use the engine's `RegisterSecureMessageHandler` on a channel
one above the shared one, where the transport supplies the sender identity and a from-the-server
flag that cannot be forged. Replies are ignored unless that flag is set, so a client cannot fake the
server's answer to another client.

**The heat pump's electrical hookup is only checkable in game.** The simulation half is under test
offline. The half that makes it cost anything — a `MyResourceSinkComponent` attached in code during
`Init`, because an upgrade module has no definition field for one — cannot be exercised without a
session, so whether the grid's resource distributor picks the sink up is unverified.

**A grid saved with the old heat pump block loses it on load.** The heat pump was a `CubeBlock` and
is now an `UpgradeModule`, because only a functional block has a terminal to switch it from. Nothing
was lost by doing it: the old block had no behaviour at all.

**`SweepRoomPressure` is per room per cadence, unbudgeted** — measured, instrumented, and cheaper
than it was. It makes two game API calls per compartment every eight steps, bounded by compartment
count rather than block count. Every other whole-grid pass in the mod is a rota or a budgeted slice;
this is the one that is not.

A field dump (six grids, 7,976 blocks, 118.5 s) puts it below the noise: the sweep runs in
`AfterSteps`, outside every row of the cost table, and backing it out of the totals leaves **64 ms
in 118.5 s — 0.054% of real time** for the sweep *plus* the mass sweep, overheat damage, threshold
crossings, the heat-pump publish and the hottest-node scan together. The rota is **deliberately not
built**: the measurement says it is a station-scale risk with no evidence behind it, and the sweep
is now timed as `of which room pressure` with counters for compartments visited, game calls made,
vent scans and vents walked — so the next dump from a large station answers the question with a
number rather than an argument.

**The first step of a grid's life is several times an ordinary one** — 28.3 ms above a 13.2 ms tick
at half a million blocks, and 53 ms above a 26 ms tick at a million — from first touch of every flat
array and the first fill of every mirrored row. It happens once, immediately after a world load that
took four seconds, so it is a warm-up rather than a stutter. It is still the largest number in the
distribution at the top rung, and it grew relative to the tick as the tick shrank: the ticks either
side of it got three to five times cheaper and the first touch did not.

**A grid holds about 1.8 KB a block, against a design budget of ~110 bytes a node.** Measured at
126,731 blocks: 213 MB retained, 278 MB peak. Half of the retained figure is indexed by *bounding
volume* rather than by block, so a hull pays for the empty space it encloses. [memory.md](memory.md)
has the breakdown and the changes that roughly halve it — none of which help SE2, where the problem
is that three structures are indexed per cell rather than per block.

**Block storage is still per cell, which is what stands between the model and SE2.** The geometry,
the conduction graph and the integrator all work from integer AABBs and cost the same whatever a
block's volume — `Se2LatticeTests` pins that. `GridModel.blocksByCell`, `SurfaceMap.states` and
`BlockInstance.Cells` do not: they are one entry per occupied cell, so a 5 m block on SE2's 0.25 m
lattice would cost 16,000 dictionary entries and an 8,000-element array. See
[scale-design.md](scale-design.md#cell-centric--boundary-centric).

**The room map floods the bounding volume, which a hull fills about a fifteenth of.** It is
budgeted, so the cost is ticks rather than a stall — but at a million blocks it is 7,000 ticks to
converge, which is twenty minutes on a stale map. Bounded and wrong is better than unbounded and
wrong; it is still wrong. See [scale-design.md](scale-design.md#room-mapping-is-the-one-that-has-to-change-shape).

**There is no thermal view, and one is wanted.** Mods get no shader, no post-process and no frame
buffer, so the only way to recolour the world is to blank it and redraw every body as a billboard —
which gives coarse terrain, discs for asteroids, and nothing at all for anything the mod does not
draw. An earlier heat overlay was built on that basis and removed; the x-ray block overlay keeps the
part of that work worth keeping, since a debug view *wants* to see through a hull.

**This is an obstacle rather than a decision**, and it was recorded here as a deliberate limit for a
while, which was wrong: the difficulty is real and the intent to have one stands. The routes worth
investigating are in [document-of-intent.md](document-of-intent.md#thermal-vision--wanted-method-unknown),
and none of them is straightforward. Tracked as [backlog](backlog.md) B24.

**Nothing gives a player feedback without an instrument.** There is no sound emitter, no particle
effect and no emissive code anywhere in the mod, so a ship that is overheating looks and sounds
exactly like one that is not until somebody opens a terminal. Against a measured **8.9 s** median
from full electrical load to critical, that is the gap between a warning and a post-mortem. Tracked
as [backlog](backlog.md) B25.

**The cost of terrain occlusion has never been measured in game.** It is ten height lookups per
sample per interval for grids near a surface, which should be well under the voxel raycast it sits
beside, but no dump has confirmed it. The `solar occlusion` timing in a telemetry report is where it
would show.

---

## Failure patterns worth remembering

Each of these is stated as the rule it produced. The defect that produced it is the evidence, and
the date it was found is in the [change log](#change-log). They are grouped by the shape of the
failure rather than by the subsystem, because the shape is what repeats.

### A mechanism can be correct everywhere it is exercised and inert everywhere it runs

**Room air was tested only through the door nobody used.** Every test and the mod API set a room's
pressure through `ThermalSimulation.SetRoomPressure`, which rebuilds the room's links to the blocks
bounding it, seeds air appearing for the first time from the temperature of those walls, and
recomputes the conductance totals. The game's own sweep assigned `room.Pressure` directly and called
`RefreshThermalMass` by hand — the only caller in the codebase that did — so in a live world a
pressurised room got its air mass, **no links at all**, and whatever temperature the last rebuild
left behind, which for a ship in vacuum is 2.7 K. The whole suite passed over a feature that did
nothing in game. The room dump now reports a `links` column, so air with mass and no coupling says
so.

**Every block this mod ships ran on the default thermal properties, in game only.** All eighteen of
the mod's own entries in [Cubes.xml](../Data/Cubes.xml) declared themselves under
`<TypeId>CubeBlocks</TypeId>`. There is no such object builder type — the blocks are `CubeBlock`
(pipes, radiators) and `UpgradeModule` (pumps, heat pumps) — so Definition Extensions matched none of
them and every one took the fallback path to `DefaultThermodynamics`:

| Block | Authored | Ran as |
| --- | --- | --- |
| Radiator | emissivity 0.35, area ×1.25, specific heat 900 | 0.125, ×1, 450 |
| Heat pump | waste 0 / 0 | 0.05 / 0.05 |
| Coolant pipes | conductivity 1 | 0.6 |

The radiator is the block that mattered: emissivity and the area multiplier *are* the block, so it
was shedding at 28% of the authored rate with none of the area bonus. The heat pump was worse than
wrong — the solver already puts every watt it draws into the hot side, so a 0.05 consumer fraction
on a pump drawing its full 20 kW invented 1 kW of heat a second time.

**No test could see it**, because the solver suite builds its own `BlockThermalProperties` in code
and was right about every equation; the numbers it was handed in game came from a file nobody parsed
offline. `ShippedDefinitionTests` now reads the shipped XML and cross-checks each entry's `TypeId`
against the block's own `.sbc`. Its radiator assertions compare against the default entry rather
than against literals, because silently *becoming* the default is the failure being guarded.

**A settings toggle can have a menu entry, a config field, a label and no reader.**
`DebugWindRaycast` sat in the file, in `Names()`, in `ClientOwned` and on the Debug page reading
"Draw wind vector" from the day the wind field was written, and nothing anywhere read it. Switching
it on drew nothing. Found by grepping for *readers* of a setting rather than by using it — which is
the check worth running over the whole settings list, and is now `SettingsWiringTests`.

### One definition read by two parsers drifts, silently, in both directions

Block thermal properties are read twice: `ThermalCellDefinition` asks Definition Extensions for them
in a live session, and `ShippedBlocks` reads the XML directly so the harness can build from what the
mod ships. Neither can be the other — the first needs a session and the second must run without one.

`HeatSourceWatts`, the term for a block that is hot because of what it *is* rather than because of
power crossing it, was only ever known to the offline reader: it has a class of tests, a place in
the properties type, a clamp and a conservation proof, and a mod author declaring a smouldering
wreck would have got 1,234 W in every test in this repository and **0 W in a world**.
`ExcludeFromSimulation` went the other way — the in-game reader has always read it and the offline
one never did, so a block type excluded in `Cubes.xml` would have been simulated by every benchmark,
every corpus run and every scenario, and by nothing in a game.

`BothParsersKnowTheSamePropertyNames` compares the two name lists in both directions. It is textual,
because the in-game reader cannot be linked into the test project — which is the same reason the two
parsers exist, and therefore the reason a check on them has to be.

### A number can be right in the solver and attached to nothing

**Every reactor in the game made no heat, and the whole suite was green over it.** `Cubes.xml` gave
the `Reactor` type `ProducerWasteEnergy` 0 and `ConsumerWasteEnergy` 0.25. A reactor delivers power
through `MyResourceSourceComponent`, so only the producer fraction can ever reach it: the largest
heat source a ship has was inert.

Nothing in the solver was wrong, so no test of the solver could see it — the simulation correctly
integrated a load of zero. Which of the two fractions applies is decided by the game's component
wiring, not by the definition, so an entry can be internally consistent and still be attached to
nothing. The check that catches it asserts on the *type's* producer fraction rather than on any
block's behaviour, because behaviour was never the thing that broke. See
[balance.md](balance.md#reactor-waste-heat).

**`LargePrototechReactor` is the same shape, unfixed.** The game gives it the TypeId
`HydrogenEngine`, so the derivation charges a 400 MW plant a combustion engine's 0.60 waste
fraction: 240 MW of heat out of a 3×2×2 block. It needs a per-subtype override — see
[backlog.md](backlog.md).

### A diagnostic that reports zero while working is worse than no diagnostic

**The substep mass floor was computed from its own previous answer.** `ApplyThermalMassFloor` raises
a light block's *integration* capacity so it stops demanding more substeps than `MaxSubstepsPerBlock`
allows. It compared each node against the mirrored row `nodeThermalMass[i]` — but `SyncNodeState`
only refreshes a row whose node is dirty, so on a settled grid the row still held the floor this
same pass wrote last step. Two consequences:

* `FlooredNodes`, reported as **blocks raised by cap**, counted only the nodes a pass *moved*. After
  the first step there were none, so it read `0` on every step while the floor was doing all of its
  work — on the same page as its own projection that the configured cap of 3 raises 804 blocks.
* Because the pass could only ever raise, the floor became a high-water mark, re-raised against its
  own previous output rather than sized from the block. A 20 kg fitting on heavy armour settled at
  **1.94 substeps demanded against a cap of 3** — wasted accuracy in the other direction.

**The behavioural half is smaller than it looks, and worth stating so nobody re-derives an alarm
from it.** A node's stability rate is conduction plus radiation, and on a real ship conduction
dominates — a live dump attributes 100% of its substep demand to conduction. Conduction is
temperature-independent, so the stale row usually held the same floor the block deserved: a 16 kg
fitting in light armour on a grid that had been at 1200 K and one that never left 400 K integrated
**bit-identically**. The ratchet was real, is fixed, and was inert wherever conduction sets the
floor.

`AFlooredGridDemandsExactlyItsCapAndNotLess` reads the raw demand through `NodeSubstepDemand`, which
divides by the block's real capacity, so the floor is not asked to confirm its own work.

**Convection was reported before the atmosphere blend, not after** — a reporting fault only, and the
first attempt at fixing it was wrong. A field dump showed `convection W/m2K 50.0` beside
`air density 0.0000` at 44 km, which reads as a hull convecting in a vacuum. It was not:
`EnvironmentState.ConvectionCoefficient` is the planet's figure scaled by wind and weather, and the
solver blends it by `AtmosphereFactor` at the point of transfer, because the same factor weights
radiation *down* as it weights convection *up*. Measured on one 200 kW block, the transfer was
correct throughout — convective watts fall 50,000 → 49,401 → 30,562 → 412 → 0 as density falls
1 → 0.25 → 0.01 → 0.0001 → 0, while radiation rises to take over.

Applying the factor at the coefficient would have been a *second* application and squared the blend:
0.47 instead of 0.68 at quarter density. A settled-temperature test written to catch the imagined
defect failed against correct code, which is what exposed the mistake — **thinner air does not make
a block hotter** over most of the range, because thin air is also much colder (294 K at sea level
against 101 K at a twentieth), and a weak coupling to a cold sink beats a strong coupling to a warm
one. The same block settles at 321 K at sea level, 241 K at a twentieth, and 553 K only in true
vacuum. `ASettledTemperatureIsNotMonotonicInAirDensity` pins that so the alarm is not re-derived.

### An invariant documented at three call sites will be missing from the fourth

**A grid welded past its buffer capacity went NaN, whole.** The per-node arrays — temperatures,
mirrored heat capacities, the watts a substep is accumulating — grow when the node count passes
their capacity, and growing reallocates every one of them. They are refilled by `SyncNodeState`,
which runs when a step *begins*. A step already in flight carried on over the zeroed rows, divided
its watts by a heat capacity of zero, and published the result onto every block on the grid.

The trigger is a block placed on a frame the step is not finished with, on a grid with no headroom
left in its buffers. A step spans fifteen frames at the shipped settings, so the window is most of
the time; the buffers grow by a quarter plus sixteen, so a ship being welded crosses the boundary
regularly. `BufferGrowthTests` reproduces it in twenty milliseconds. Three call sites documented
*"a step in flight is abandoned when the grid changes shape"* and the fourth, which changes the
grid's shape most violently, did not.

### Two models of the same thing disagree, and losing the argument is silent

**This model's rooms are pieces of the game's.** Sealing here comes from each definition's
pressurisation table read cell by cell; the game's test knows the real shape of a sloped block. Where
they differ the flood fill walks in from outside, the compartment stops existing, and because
pressurisation is only ever asked about rooms the map already found, nothing notices.

Three separate defects came out of that gap, each measured before it was fixed:

* **A vent could only speak for the room it stands in.** Pressurisation read the air vents and gave
  air to the compartments a vent physically touched. Measured: twelve mapped rooms on one ship, the
  game holding air in nine, two with a vent against them, seven left empty. The level now comes from
  `IMyCubeGrid.GasSystem.GetOxygenRoomForCubeGridPosition` per room, which answers for every
  compartment whether or not anything is bolted to it; the vents are the fallback when the gas
  system cannot be read.
* **A vent reported to one room and it was not always the right one.** `ReadVents` stopped at the
  first room found on the first cell of the vent, so a vent in a bulkhead between two compartments
  gave one of them air and the other nothing — decided by the order the six faces are indexed in.
  Measured on a ship with two vents in the same bulkhead: an eight-cell space took the air and the
  thirty-cell cabin, with both vents on it and the game reporting it sealed and 99% full, ran at
  zero pressure. A vent now reports to every room it touches.
* **Sealed is not full.** The first version of the dry-room flag asked `IsRoomAtPositionAirtight`
  and `IMyAirVent.IsPressurized`, both of which answer *is this room sealed*, and treated the answer
  as *should this room have air*. On a ship in vacuum every sealed cupboard nobody had piped air
  into came back airtight and empty — correct in both models — so eight of twelve compartments were
  flagged as faults and painted magenta, burying the one that mattered.

**When the comparison was finally run, the map was right**: 12 compartments found, zero held only by
the game, 11 of 12 agreeing with `IsRoomAtPositionAirtight`. That is the argument for measuring
before fixing — the sealing test looked guilty from the counts alone and was not. The comparison now
runs every dump and is reported per compartment. See
[thermal-model.md](thermal-model.md#diagnostics).

### A guard has to test what it claims to test

**A lag needs to know it has no history.** Ambient started each session at the `VacuumTemperature`
its state was seeded with and took three minutes of play to reach the real climate, dragging every
block on every grid with it — one measured grid fell from 257 K to 103 K in nineteen seconds.
`ClimateModel.Follow` guarded against this with `if (current <= 0f) return target`, which never
fired, because 2.7 is not zero. A guard against an uninitialised value has to test whether the value
*was* initialised, not whether it looks unreasonable.

**A scale applied to a lagged value compounds against the lag.** Ambient chased its target with a
45-second first-order lag and was then multiplied by an air-density factor — but the *scaled* value
was what the next step chased from, so the factor reapplied every step. A snowfield 5.6 km up
reported 36 K for an entire session while two sea-level sites nearby were correct to a tenth of a
kelvin, because their density rounded the factor to 1.0000 and hid it. Everything that decides a
temperature now produces a *target*, and the lag is applied to that target exactly once, last.
Pinned by `ThinAirDoesNotCompoundAgainstTheLag`. The arithmetic is in
[environment.md](environment.md#ambient-temperature).

**Config defaults belong on the fields, not in a factory method.** A world's config file has no
element for a setting added after that file was written, and the XML reader leaves such fields at
`default(T)` — so every setting added since a world was first loaded ran as `false` or `0` in that
world, silently. A test world reported `SolarOcclusionPlanets False`, `SolarTerrainRange 0` and a
room overlay span of one kelvin while its owner had changed none of them. Bumping the file version
would only have papered over it, and thrown away real customisation each time. The defaults now live
on the field declarations, where a reader that finds nothing leaves them alone.

### Block placement is not a main-thread-only path

The game builds pasted and projected grids on worker threads, so everything reachable from
`ThermalGrid.AddBlock` runs concurrently with itself. Four separate unguarded collections were found
that way, and the field symptom is always the same: `NullReferenceException` out of
`Dictionary.Insert`, and every block of the affected type silently failing to become a node.

**Two shape caches on the block-placement path were unguarded.** `ThermalBlockCatalog` locks its
model dictionary and deliberately builds outside that lock, so as not to serialise the worker
threads the game pastes grids on — and what it builds calls into `ThermalCoolantShapes.Get` and
`ThermalHeatPumpShapes.Get`, both of which wrote to a plain `Dictionary` with no lock at all. A
field dump logged three such exceptions in the first tenth of a second of a world load. Both caches
now take a lock on the same discipline as the catalogue: probe under the lock, build outside it,
publish under it.

The same fact produced the definition catalog's shared scratch list and the shared telemetry
registries, both fixed the same way.

### Assigning `NeedsUpdate` from a grid logic component breaks ship control

`[MyEntityComponentDescriptor(typeof(MyObjectBuilder_CubeGrid), true)]` makes the component's
`NeedsUpdate` property the *grid entity's* update flags. Assigning to it clears whatever the grid set
for itself, and `MyCubeGrid` re-arms `EACH_FRAME` only when its scheduled-update queue goes from
empty to non-empty — so clearing it once stops that queue being drained for the rest of the session.
The visible symptom is ship control: `MyGroupControlSystem` recalculates the controlling cockpit from
that queue, so sitting down gives "Someone else is using this ship!" forever.

**Use `|=`, and stop work with a flag of your own rather than by taking the entity's updates away.**

### A repair has to cost what changed, and no more

**A block whose mounting changed kept stale conduction links** — and it was the opposite of what a
first reading suggested. `RefreshBlock` did not rebuild the conduction graph. It did not touch it: it
refreshed the surface bits and set the topology flag, and the flag's handler only rebuilds links when
a full rebuild is already due or nodes are queued for their first link, which a refresh sets neither
of. Contact area is the product of both ends' mount fractions, so every link touching a refreshed
block went on carrying a conductance derived from geometry the block no longer had, for the rest of
the session. Meanwhile the *expensive* half of the flag — a full room flood fill — was charged on
every call.

Both halves are now proportional to what changed. `ThermalSolver.RefreshBlockLinks` drops the node's
links through the same intrusive chains removal walks and requeues it for the incremental link build
a placed block takes, which costs the node's degree. The remap is asked for only when the block's
*structural* sealing bits actually moved, or when a door's live bits moved and the mapper has no
portal for it. The dirty flag is split accordingly — `MarkTopologyDirty` for anything that can move a
wall, `MarkLayoutDirty` for anything that cannot. `RefreshingABlockCostsItsOwnDegreeRatherThanTheGrid`
holds the repair to one node's links.

### The step budget has to count everything a step does

`MaxElementVisitsPerStep`, formerly `MaxLinkVisitsPerStep`, bounded a step by substeps times links,
while the environment pass is per node per substep — radiation, convection, solar with six face
weights each — which the budget could not see. A grid with few links per node therefore got a more
generous allowance than one with many, for the same real cost.

The weight was measured rather than guessed: a node is worth **2.8 links at four thousand nodes, 3.3
at a hundred thousand and 7.5 at a quarter of a million**, and an exposed face between a tenth and a
half of a link. Per-link cost is flat across a hundredfold size range because links stream; per-node
cost triples because the node state stops fitting in cache. See
[benchmarks.md](benchmarks.md#what-a-substep-costs).

The budget now counts `links + 4 × nodes` and ignores faces, four being the low end of the range over
the sizes where the bound binds at all.

What this retires is the claim that only grids past a hundred thousand blocks reach the default. A
step's cost is size times stiffness, and `TheShippedAllowanceFitsThisGridAndAHalvedOneDoesNot` pins
both halves on one 8,904-node rig: counting links alone, its 20,779 links bought 48 substeps against
a demand of 23, so the budget did nothing at all; counting nodes as well, one substep over that rig
costs 56,395 element visits. **Whether the allowance binds is a question about the step rate rather
than about block count** — the same rig asks 23 substeps at the shipped quarter-second step and about
12 at an eighth-second one, which is why the allowance moved with `Frequency` and why halving it now
throttles this grid. A world whose config predates the rename takes the new default rather than
importing its old number, which would be a value in the wrong unit; the load path logs when it drops
one.

### A face bolted to something that does not seal is still exposed

Exposure rejected any cell face where two mount surfaces met, regardless of what the neighbour was.
The ordering is what made that wrong: the sealing test runs first, so every joint against a block
that seals was already gone, and the mount test could only ever reach faces bolted to something that
does *not* seal — a grating, a catwalk, a ladder. The room mapper calls the cell beyond one of those
external, because air floods through it, and exposure threw the face away anyway. **A hull panel with
a catwalk bolted flat against it lost 100% of its radiation and solar gain while the mod's own room
map said it was outdoors.**

The mount test is gone. `FaceExposure.Mounted` survives as a *subset* of `Exposed` rather than a
rejection, so the surface dump's `mounted` column answers how much of a ship conducts and radiates
through the same face. `ASealingNeighbourStillBuriesTheFaceWhateverItsMounts` pins the ordering
argument the fix rests on — a solid hull cannot be opened up by this change.

---

## Testing gaps

The suite covers the model thoroughly and the adapter barely. `HostAdapterTests` exercises what can
be reached without a session; the rest of `Game/` is exercised only in the game.

| Gap | Why it is hard | What would close it |
| --- | --- | --- |
| The vent sweep, the terminal readout and the mod API's delegate table | need a live session | the API's *shape* is checkable without one and is worth pinning |
| The wind map and the wind indicator | whether an arrow lands where it should on screen is answerable only by looking | the arithmetic under them is in `WindCompass` and pinned by `WindCompassTests`, including the handedness — the half a drawing cannot argue with |
| The mass sweep's rota | the sweep asks the game for a block's mass, and a harness has no game block to ask | covered today only by arithmetic tests on its slice function |

`LoadTests` closes part of the adapter gap for cost rather than for correctness, and asserts work
counters rather than milliseconds so it holds on any machine.

---

## Change log

| Date | Change |
| --- | --- |
| 2026-08-22 | Withdrew the burning-ship divergence. It was never a divergence: run ten times longer the rig is flat to the last digit from 600 s to 6,000 s, its energy balances to a part in ten thousand, and two integrators refused wildly different substep counts land one kelvin apart. The 11,279 K is a converged conduction-limited interior temperature — the hottest block has no exposed face and pushes 2.22 MW out through 1,317 W/K of conduction — and the peak among blocks that can radiate is 2,822 K. The defect it left behind is on [realism.md](realism.md): a divergence column that was a threshold on a temperature, which cannot tell a converged extreme from a diverged one. |
| 2026-08-22 | Measured the client divergence that had been recorded as cosmetic, with `-- drift`. It converges on its own — the model is dissipative — but it is on the wrong side of a block's critical temperature for two and a half to five minutes, against a whole damage event that is a median 8.9 s long, and the error is not a uniform offset so a per-grid correction would not reach it ([backlog.md](backlog.md) `B4`). Corrected the count of replicated settings, which said 44 of 49 against 77 of 85. |
| 2026-08-22 | Said what the destruction limit does and does not reach in the new `seconds_to_first_loss` column: the first loss is exact, and there is deliberately no count of losses after it. |
| 2026-08-22 | Reopened the per-grid shadow limit as designed work. It was recorded as a simplification taken on purpose, which `D6` is satisfied by, but the cost argument behind it treated three occluders as one: the planet's test is analytic and costs no ray, so the per-block objection was never true of the one occluder a player notices. Now [backlog](backlog.md) `A9`. |
| 2026-08-22 | Filed the burning-ship divergence as an open defect. It had been carried on [realism.md](realism.md) as a starved-integrator finding; re-measuring it showed 0% starved, so the explanation is withdrawn and the defect stands with its cause unknown. |
| 2026-08-22 | Repointed the step-budget paragraph at the renamed test and at the shipped rate, which moved from eight steps a second to four when the settings profiles were removed. |
| 2026-08-22 | Moved the thermal view out of the deliberate limits. It was filed there on the grounds that mods get no shader — which is a statement about difficulty, not a simplification taken on purpose, and a limit is only a limit when it is chosen (`D6`). It is an open problem, and the intent to have one is stated in [document-of-intent.md](document-of-intent.md#thermal-vision--wanted-method-unknown). Recorded the absence of any non-instrument feedback beside it. |
| 2026-08-22 | Absorbed `bugs-and-performance.md`, the record of the first extraction pass. Every one of its thirty-two findings is resolved in the current code — including the eleven whose headings carried no *fixed* marker, each re-verified against the source during this pass — so the page survives as the dated entries below and the patterns above rather than as a defect list. Restructured around the shape of each failure rather than its subsystem; promoted the deliberate limits to the top; moved the corpus balance findings to [balance.md](balance.md), which is where the dataset they come from is described. Removed two limits that the per-room gas-system read had already retired ("a room with no air vent holds no air" and "pressurisation is only known through air vents") and corrected a third: block `Conductivity` is real W/(m·K), and it is the *coolant loop's* that is still a 0…1 quality. Merged the two sections both titled "Fixed, worth remembering". |
| 2026-08-21 | Recorded the buffer-growth NaN, the unguarded shape caches on the block-placement path, and the substep mass floor computing from its own previous answer. Recorded the censoring limit that makes every peak above critical a statement about the harness. |
| 2026-08-20 | Recorded that every reactor in the game made no heat, and that all eighteen of the mod's own `Cubes.xml` entries were filed under a TypeId that does not exist. Recorded the step budget counting links but not nodes, and the exposure test that buried a face bolted to an open lattice. |
| 2026-08-19 | Recorded the three room-air defects — links never built through the game's sweep, a vent reporting to one room of two, and a vent speaking only for the room it stands in — and the reporting fault that showed convection before the atmosphere blend. |
| 2026-08-18 | Recorded the ambient scale compounding against the lag, and the lag with no history test. |
| 2026-08-12 | Opened the register against the pre-rewrite implementation: 32 findings across model physics, code and performance, from extracting the simulation into `tests/` and putting it under test. The model defects — conduction ignoring thermal mass, conduction not conserving energy, friction computed and discarded, damage scaling with the update rate, specific heat 250× below physical — are what made a rewrite worth doing rather than a patch. |
