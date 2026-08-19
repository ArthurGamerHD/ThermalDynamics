# Known issues and limits

Current as of the review that added room air, thresholds, point heat sources and the mod API. Split into defects, unfinished work, and limits that are deliberate.

## Fixed, worth remembering

**Every block this mod ships ran on the default thermal properties, in game only.** All eighteen
of the mod's own entries in [Cubes.xml](../Data/Cubes.xml) declared themselves under
`<TypeId>CubeBlocks</TypeId>`. There is no such object builder type — the blocks are `CubeBlock`
(pipes, radiators) and `UpgradeModule` (pumps, heat pumps) — so Definition Extensions matched none
of them and `ThermalCellDefinition.GetDefinition` took its fallback path to
`DefaultThermodynamics` for all eighteen. The whole file was inert for the mod's own blocks while
the three vanilla entries beside it, authored under `Thrust` and `Reactor`, worked; a live dump
showed `LargeBlockSmallHydrogenThrustReskin` with its authored emissivity 0.15 and critical 1050 K
on the same page as `Gauge_LG_Radiator` reporting 0.125 and 900 K.

What it cost, per block:

| Block | Authored | Ran as |
| --- | --- | --- |
| Radiator | emissivity 0.35, area ×1.25, specific heat 900 | 0.125, ×1, 450 |
| Heat pump | waste 0 / 0 | 0.05 / 0.05 |
| Coolant pipes | conductivity 1 | 0.6 |

The radiator is the block that mattered: emissivity and the area multiplier *are* the block, so it
was shedding at 28 % of the authored rate with none of the area bonus and radiating no better than
the armour around it. The heat pump was worse than wrong — the solver already puts every watt it
draws into the hot side, so a 0.05 consumer fraction on a pump drawing its full 20 kW invented
1 kW of heat a second time. The live dump reported exactly that: `heat generation W 1,000.00`
against `power consumed W 20,000` on a block whose definition asks for zero.

**No test could see it.** The solver suite builds its own `BlockThermalProperties` in code and was
right about every equation; the numbers it was handed in game came from a file nobody parsed
offline. `ShippedDefinitionTests` now reads the shipped XML and cross-checks each entry's `TypeId`
against the block's own `.sbc`, that every shipped block has an entry at all, that the radiator
beats the default entry on both properties it exists for, and that the heat pump's waste
fractions are zero. The radiator assertions compare against the default entry rather than against
literals, because silently *becoming* the default is the failure being guarded.

**The substep mass floor was computed from its own previous answer, and its diagnostic read zero
whenever it was working.** `ApplyThermalMassFloor` raises a light block's *integration* capacity so
it stops demanding more substeps than `MaxSubstepsPerBlock` allows. It compared each node against
the mirrored row `nodeThermalMass[i]` — but `SyncNodeState` only refreshes a row whose node is
dirty, so on a settled grid the row still held the floor this same pass wrote last step. Two
consequences, one cosmetic and one not:

* `FlooredNodes`, reported as **blocks raised by cap**, counted only the nodes a pass *moved*. After
  the first step there were none, so it read `0` on every step while the floor was doing all of its
  work. A live dump reported `blocks raised by cap 0 / 0 / 0` on the same page as its own projection
  that the configured cap of 3 raises 804 blocks on that ship.
* Because the pass could only ever raise, the floor became a high-water mark: it was re-raised
  against its own previous output rather than sized from the block. A 20 kg fitting on heavy armour
  settled at **1.94 substeps demanded against a cap of 3** — damped harder than the cap ever asked
  for, which is wasted accuracy in the other direction.

Both come from the same line. The floor is now sized from `nodes[i].ThermalMass`, as the coolant
loop and room air passes immediately below it always were, and `FlooredNodes` counts the nodes
standing above their real capacity rather than the ones one pass happened to move.

**The behavioural half of this is smaller than it looks, and worth stating so nobody re-derives an
alarm from it.** A node's stability rate is conduction plus radiation, and on a real ship
conduction dominates — the live dump attributes 100 % of its substep demand to conduction.
Conduction is temperature-independent, so the stale row usually held the same floor the block
deserved. Measured on a 16 kg fitting in light armour, a grid that had been at 1200 K and one that
never left 400 K integrated **bit-identically** from the same state. The ratchet is real, is fixed,
and was inert wherever conduction sets the floor.

`SubstepFloorTests` gains `TheFlooredCountHoldsForAsLongAsTheFloorDoes` and
`AFlooredGridDemandsExactlyItsCapAndNotLess`; both were confirmed to fail against the previous
arithmetic. The second reads the raw demand through `NodeSubstepDemand`, which divides by the
block's real capacity, so the floor is not being asked to confirm its own work.

**Never assign `NeedsUpdate` from a game logic component that asked for entity updates.**
`[MyEntityComponentDescriptor(typeof(MyObjectBuilder_CubeGrid), true)]` makes the component's
`NeedsUpdate` property the *grid entity's* update flags. Assigning to it clears whatever the grid
set for itself, and `MyCubeGrid` re-arms `EACH_FRAME` only when its scheduled-update queue goes from
empty to non-empty — so clearing it once stops that queue being drained for the rest of the session.
The visible symptom was ship control: `MyGroupControlSystem` recalculates the controlling cockpit
from that queue, so sitting down gave "Someone else is using this ship!" forever. Use `|=`, and stop
work with a flag of your own rather than by taking the entity's updates away.

## Unfinished

**A mechanism tested only through the door nobody uses is untested.** Room air was correct
everywhere it was exercised and inert everywhere it ran. Every test and the mod API set a room's
pressure through `ThermalSimulation.SetRoomPressure`, which rebuilds the room's links to the blocks
bounding it, seeds air appearing for the first time from the temperature of those walls, and
recomputes the conductance totals. The game's own sweep assigned `room.Pressure` directly and called
`RefreshThermalMass` by hand — the only caller in the codebase that did — so in a live world a
pressurised room got its air mass, **no links at all**, and whatever temperature the last rebuild
left behind, which for a ship in vacuum is 2.7 K. The whole suite passed over a feature that did nothing in game.
The sweep now goes through the solver, and `RoomAirCouplingTests` pins the two properties that
differed. The room dump reports a `links` column so air with mass and no coupling says so.

**A vent reported to one room and it was not always the right one.** `ReadVents` stopped at the
first room found on the first cell of the vent, so a vent in a bulkhead between two compartments
gave one of them air and the other nothing — decided by the order the six faces are indexed in.
Measured on a ship with two vents in the same bulkhead: an eight-cell space took the air and the
thirty-cell cabin, with both vents on it and the game reporting it sealed and 99% full, ran at zero
pressure. A vent now reports to every room it touches. That over-reports where a vent serves only
one side and the game exposes no way to ask which room a vent is on; it is bounded by each room
still being tested against `IsRoomAtPositionAirtight` on its own.

**A vent can only speak for the room it stands in, and this model's rooms are smaller than the
game's.** Pressurisation read the air vents and gave air to the compartments a vent physically
touched. The game's rooms are the whole connected volume — its sealing test is finer than a cell and
splits nothing where this splits often — so a cabin joined through an open doorway to a vented one
is full in the game and was in hard vacuum here. Measured: twelve mapped rooms on one ship, the game
holding air in nine of them, two with a vent against them, seven left empty. The level now comes
from `IMyCubeGrid.GasSystem.GetOxygenRoomForCubeGridPosition` per room, which answers for every
compartment whether or not anything is bolted to it; the vents are the fallback when the gas system
cannot be read.

**Sealed is not full, and a diagnostic that confuses the two is worse than none.** The first
version of the dry-room flag asked `IsRoomAtPositionAirtight` and `IMyAirVent.IsPressurized`, both
of which answer *is this room sealed*, and treated the answer as *should this room have air*. On a
ship in vacuum every sealed cupboard nobody had piped air into came back airtight and empty —
correct in both models — so eight of twelve compartments were flagged as faults and painted magenta
in the overlay, burying the one that mattered. The test is now the game's own oxygen level, read
per room from `IMyCubeGrid.GasSystem`, and lives in `RoomPressure.Disagrees` with tests on it
rather than inline in a struct property.

**This model and the game can disagree about what is sealed, and losing that argument is silent.**
Sealing here comes from each definition's pressurisation table read cell by cell; the game's test
knows the real shape of a sloped block. Where they differ the flood fill walks in from outside, the
compartment stops existing, and because pressurisation is only ever asked about rooms the map
already found, nothing notices. **Measured, and on the ship that prompted it the map was right** —
12 compartments found, zero held only by the game, 11 of 12 agreeing with
`IsRoomAtPositionAirtight`. The comparison is now run every dump and reported per compartment, so
the next disagreement is a number rather than a guess. See
[surface-mapping.md](surface-mapping.md#where-the-audit-was-not-enough).

**The ambient lag is in absolute seconds and a day is not.** `AmbientLagSeconds` is 45 seconds of
play. Against a four-minute sun rotation that attenuates the day-night swing to 46% of its intended
size; against the default two-hour rotation it does almost nothing. It is physically a fraction of a
day. `MySectorWeatherComponent.RotationInterval` is the sun's period and would let it be expressed
that way, if that type proves reachable under the mod whitelist — see
[engine-api-notes.md](engine-api-notes.md). Until then it is a per-planet figure in
[Planets.xml](../Data/Planets.xml) that a short-day world has to know to change.

**The underground core gradient is out of reach in ordinary play.** Below `SealevelDeadzone` the
rock warms toward `CoreTemperature`, and the shipped deadzone is 2 km below sea level — deeper than
SE's voxels go. The model is right and the tuning lever is documented, but as shipped, every
reachable depth reads a flat `UndergroundTemperature`. Whether the default deadzone should be a few
hundred metres instead is an open balance question, not a code one.

**Radiators cannot be inline loop segments.** A radiator sheds heat when a pipe's sink face is
pressed against it, which works and is what the `radiator` scenario measures. It has no coolant
ports of its own, so a loop cannot run *through* one. The block is 1×5×2 with mount points only on
its top and bottom, so adding ports needs the port geometry checked against the model.

**No network replication.** `SENetworkAPI` is initialised on channel `30323` and nothing is
registered on it. Clients run their own simulation from the same inputs and reach the same answers,
but nothing reconciles them: a client that joins mid-session starts from saved temperatures, and
divergence is never corrected. Damage and settings are server authoritative, so the divergence is
cosmetic, but it is real.

**The settings menu cannot change anything from a multiplayer client.** The Rich HUD menu edits the
same server-side config the chat commands do, so on a client every simulation control is disabled
and only the four presentation switches work. Making them editable means replicating settings, and
nothing is registered on the network channel yet — see the replication entry above.

**The heat pump's electrical hookup is only checkable in game.** The simulation half is under test
offline. The half that makes it cost anything — a `MyResourceSinkComponent` attached in code during
`Init`, because an upgrade module has no definition field for one — cannot be exercised without a
session, so whether the grid's resource distributor picks the sink up is unverified.

**The heat pump changed block type.** It was a `CubeBlock` and is now an `UpgradeModule`, because
only a functional block has a terminal to switch it from. A grid saved with the old block loses it
on load. Nothing was lost by doing it: the old block had no behaviour at all.

## Fixed, worth remembering

**Config defaults belong on the fields, not in a factory method.** A world's config file has no
element for a setting added after that file was written, and the XML reader leaves such fields at
`default(T)` — so every setting added since a world was first loaded ran as `false` or `0` in that
world, silently. A test world reported `SolarOcclusionPlanets False`, `SolarTerrainRange 0` and a
room overlay span of one kelvin while its owner had changed none of them. Bumping the file version
would only have papered over it, and thrown away real customisation each time. The defaults now
live on the field declarations, where a reader that finds nothing leaves them alone.

**A scale applied to a lagged value compounds against the lag.** Ambient chased its target with a
45-second first-order lag and was then multiplied by an air-density factor — but the *scaled* value
was what the next step chased from, so the factor reapplied every step. The steady state is
`f·k / (1 − f + f·k)` with `k = 1 − e^(−dt/τ)`, which at `f = 0.977`, `dt = 1/6 s` and `τ = 45 s` is
14% of the intended temperature rather than 98%. A snowfield 5.6 km up reported 36 K for an entire
session while two sea-level sites nearby were correct to a tenth of a kelvin, because their density
rounded `f` to 1.0000 and hid it. Everything that decides a temperature now produces a *target*, and
the lag is applied to that target exactly once, last. Pinned by
`ThinAirDoesNotCompoundAgainstTheLag`.

**A lag needs to know it has no history.** The same ambient started each session at the
`VacuumTemperature` its state was seeded with and took three minutes of play to reach the real
climate, dragging every block on every grid with it — one measured grid fell from 257 K to 103 K in
nineteen seconds. `ClimateModel.Follow` guarded against this with `if (current <= 0f) return
target`, which never fired, because 2.7 is not zero. A guard against an uninitialised value has to
test whether the value was initialised, not whether it looks unreasonable.

**The game has no wind field, and its wind speed is a rating.** `MyPlanet.GetWindSpeed` returns the
planet definition's maximum wind scaled by air density — a constant per altitude, the same at every
latitude and longitude, with no direction. It is what wind turbines are balanced against. Used as a
wind it read 80 m/s over a parked ship on an earthlike world, which tripped friction heating and
doubled convection; the mod now treats it as a ceiling and supplies its own field. Nothing in the
API exposes a real local wind, so the field is invented rather than read.

**Two shape caches on the block-placement path were unguarded.** `ThermalBlockCatalog` locks its
model dictionary and deliberately builds outside that lock, so as not to serialise the worker
threads the game pastes grids on — and what it builds calls into `ThermalCoolantShapes.Get` and
`ThermalHeatPumpShapes.Get`, both of which wrote to a plain `Dictionary` with no lock at all. A
field dump logged three `NullReferenceException`s out of `Dictionary.Insert` in the first tenth of
a second of a world load, which is what two threads writing one bucket looks like from the far
side; every block of those types silently failed to become a node. Both caches now take a lock on
the same discipline as the catalogue: probe under the lock, build outside it, publish under it.

## Suspected defects

**A face bolted to a block that does not seal was counted as buried — fixed.** Exposure rejected
any cell face where two mount surfaces met, regardless of what the neighbour was. The ordering is
what made that wrong: the sealing test runs first, so every joint against a block that seals was
already gone, and the mount test could only ever reach faces bolted to something that does *not*
seal — a grating, a catwalk, a ladder. The room mapper calls the cell beyond one of those
external, because air floods through it, and exposure threw the face away anyway. A hull panel
with a catwalk bolted flat against it therefore lost 100% of its radiation and solar gain while
the mod's own room map said it was outdoors.

The mount test is gone. A face against an open lattice now radiates and takes sunlight, and the
joint conducts as it always did — both are true of a real catwalk. `FaceExposure.Mounted` survives
as a *subset* of `Exposed` rather than a rejection, so the surface dump's `mounted` column now
answers how much of a ship conducts and radiates through the same face; the audit's rejection
counts sum to the cell count without it. `AFaceAgainstAnOpenLatticeRadiatesAndIsCountedAsBolted`
replaces the characterisation test, and
`ASealingNeighbourStillBuriesTheFaceWhateverItsMounts` pins the ordering argument the fix rests
on — a solid hull cannot be opened up by this change.

**The step budget counts link visits but not node visits.** `MaxLinkVisitsPerStep` bounds a step
by substeps times links, and the environment pass is per node per substep — radiation, convection,
solar with six face weights each — which the budget cannot see. A grid with few links per node
therefore gets a more generous budget than one with many, for the same real cost. Measured on a
field grid with 2.14 links per node: 991,000 budgeted link visits cost 85–150 ms against the
~17 ms the link count alone predicts. The unit should be links plus nodes, which also means the
default wants recalibrating against a game runtime rather than against the harness's .NET 9.

**A grid holds about 1.8 KB a block, against a design budget of ~110 bytes a node**
([scale-design.md §6](scale-design.md#6-data-structures)). Measured at 126,731 blocks: 213 MB
retained, 278 MB peak. Half of the retained figure is indexed by *bounding volume* rather than by
block, so a hull pays for the empty space it encloses. The earlier figure quoted here — 2.5 GB at a
million blocks — was `GetTotalMemory` without a collection and counted garbage; the peak was real,
the retained figure was not. [memory.md](memory.md) has the breakdown and six local changes that
roughly halve it, none of which help SE2, where the problem is that three structures are indexed
per cell rather than per block.

**Block storage is still per cell, which is what stands between the model and SE2.** The geometry,
the conduction graph and the integrator all work from integer AABBs and cost the same whatever a
block's volume — `Se2LatticeTests` pins that. `GridModel.blocksByCell`, `SurfaceMap.states` and
`BlockInstance.Cells` do not: they are one entry per occupied cell, so a 5 m block on SE2's 0.25 m
lattice would cost 16,000 dictionary entries and an 8,000-element array. See
[model-redesign.md §2](model-redesign.md).

**The room map floods the bounding volume, which a hull fills about a fifteenth of.** It is
budgeted, so the cost is ticks rather than a stall — but at a million blocks it is 7,000 ticks to
converge, which is twenty minutes on a stale map. Bounded and wrong is better than unbounded and
wrong; it is still wrong. See [model-redesign.md §4](model-redesign.md).

**A block whose mounting changed kept stale conduction links — fixed, and it was the opposite of
what this entry used to claim.** `RefreshBlock` did not rebuild the conduction graph. It did not
touch it: it refreshed the surface bits and set the topology flag, and the flag's handler only
rebuilds links when a full rebuild is already due or nodes are queued for their first link, which
a refresh sets neither of. Contact area is the product of both ends' mount fractions, so every
link touching a refreshed block went on carrying a conductance derived from geometry the block no
longer had, for the rest of the session. Meanwhile the *expensive* half of the flag — a full room
flood fill — was charged on every call.

Both halves are now proportional to what changed. `ThermalSolver.RefreshBlockLinks` drops the
node's links through the same intrusive chains removal walks and requeues it for the incremental
link build a placed block takes, which costs the node's degree. The remap is asked for only when
the block's *structural* sealing bits actually moved, or when a door's live bits moved and the
mapper has no portal for it; a change to mounting alone re-resolves venting and recounts that one
block's exposed faces. The dirty flag is split accordingly — `MarkTopologyDirty` for anything that
can move a wall, `MarkLayoutDirty` for anything that cannot.

`BlockRefreshTests` pins it. `ReorientingABlockRebuildsTheJointsItsMountsDecide` and its inverse
turn a block whose mounts are on two faces only and check the joint appears and disappears; both
were confirmed to fail against the previous arithmetic, as was
`RefreshingABlockCostsItsOwnDegreeRatherThanTheGrid`, which holds the repair to one node's links
rather than the grid's.

**`SweepRoomPressure` is per room per cadence, unbudgeted** — measured, instrumented, and cheaper
than it was. It makes two game API calls per compartment every eight steps, bounded by compartment
count rather than block count. Every other whole-grid pass in the mod is a rota or a budgeted
slice; this is the one that is not.

**What a field dump says about it** (TestWorld1, 2026-08-19 20:44, 118.5 s, six grids, 7,976
blocks): the sweep was *unattributed* — the cost table splits a grid's update into topology, room
mapping, exposure, solver and solar occlusion, and the sweep runs in `AfterSteps`, outside all of
them. Backing it out of the totals leaves **64 ms in 118.5 s, 0.054 % of real time**, and that
remainder also holds the mass sweep, overheat damage, threshold crossings, the heat-pump publish
and the hottest-node scan. On twelve compartments the sweep is a fraction of a fraction.

**The vent fallback was the part that mattered, and it ran every sweep.** `ReadVents` walks every
vent on the grid, and it fired whenever any single compartment went unanswered by the gas system.
The dump shows compartment 7 reporting `seal no` with no gas reading at all — a room this model
finds and the game does not seal — so the fallback ran on every sweep of the session. That is the
normal state of most ships, since this model's rooms are finer than the game's.

It cannot change an answer for such a room: `RoomPressure.Level` empties anything the game does
not seal, whatever a vent reports. The sweep now asks `RoomPressure.NeedsVentFallback` first, which
is true only for a room that could hold air and that nothing has answered for, so an unsealed
compartment no longer buys a walk over the grid's vents. A world with oxygen or pressurisation
disabled now skips the gas system and the vents entirely rather than reading both and discarding
the result.

The rota is **deliberately not built**. The measurement says it is a station-scale risk with no
evidence behind it, and the sweep is now timed as `of which room pressure` with counters for
compartments visited, game calls made, vent scans and vents walked — so the next dump from a large
station answers the question with a number rather than an argument.

**The first step of a grid's life is several times an ordinary one** — 209 ms against a 24 ms
median at half a million blocks — from first touch of every flat array and the first fill of every
mirrored row. It happens once, immediately after a world load that took six seconds, so it is a
warm-up rather than a stutter. It is still the largest number in the distribution.

## Deliberate limits

**Planet and asteroid shadow is per grid; only other grids shade individual faces.** A grid's own
shadow is per face (`SolarSelfShadowing`) and so is another grid's (`SolarGridShadows = full`), but a
planet's or an asteroid's dims the whole grid by the share of sampled rays that were blocked
(`SolarOcclusionSamples`). A capital ship crossing a terminator therefore ramps rather than
switching, but never carries a shadow edge across its own hull. Per-block would need the ray count to
scale with block count, which is a different order of cost from what is there.

**A room with no air vent holds no air.** The game exposes a room's oxygen level through vents and
nowhere else, so a sealed compartment that was never piped is indistinguishable from one that cannot
be measured, and is treated as empty. It costs that compartment the heat capacity of its air; the
walls still conduct and radiate as they should.

**Point sources are not occluded.** A registered heat source heats through walls and through other
ships. Occlusion is left to the host, which can simply not register a source it knows is hidden.

**Pressurisation is only known through air vents.** The game exposes room pressure nowhere else a
mod can reach. A sealed compartment with no vent holds no air as far as this mod is concerned.

**A room that changes shape loses its air temperature.** Rooms are matched across rebuilds by their
lowest cell. Building inside a compartment gives it a fresh air mass at the temperature of its
walls. The same key carries air across a save, so a compartment rebuilt while the world was closed
comes back at the temperature of its walls rather than the one it was saved at.

**There is no thermal view.** A heat overlay was built and removed: mods get no shader, no
post-process and no frame buffer, so the only way to recolour the world is to blank it and redraw
every body as a billboard. That gives coarse terrain, discs for asteroids, and nothing at all for
anything the mod does not draw. Seeing temperature is the terminal readout, the cockpit summary, the
crosshair readout and the x-ray block overlay instead — the overlay keeps the part of that work that
was worth keeping, since a debug view *wants* to see through the hull.

**Build state does not change a block's thermal properties.** A block at 10% construction has the
same mass, heat capacity, conductivity and mounting as a finished one, and nothing notifies the
simulation when a block finishes building. This is a deliberate simplification rather than an
omission: a partially-built block is a transient a player watches for seconds, the thermal
difference would be invisible next to the heat its neighbours carry, and tracking it would mean a
per-block event on the construction path plus a rule for what a half-built block conducts. The
machinery to support it exists — `RefreshBlock` handles a geometry change correctly and cheaply —
so this can be revisited by hooking build state to it, and nothing else would need to change.

**Emissivity is used as absorptivity.** The grey-body assumption. A block cannot be made shiny to
the sun and black to space.

**`Conductivity` is a 0..1 quality value, not W/(m·K).** It is multiplied by a reference 200 W/(m·K)
to get real units. Definitions describe a block's material by specific heat, which is real, and its
conduction by a quality, which is not.

**Mount coverage is combined as independent fractions.** Where both sides of a joint are only partly
mounted, the bolted area is `coverage_a × coverage_b`. The model records coverage per face, not per
cell, so it cannot know whether two partial mounts line up.

**The cost of terrain occlusion has never been measured in game.** It is ten height lookups per
sample per interval for grids near a surface, which should be well under the voxel raycast it sits
beside, but no dump has confirmed it. The `solar occlusion` timing in a telemetry report is where it
would show.

## Testing gaps

The test suite covers the model thoroughly and the adapter barely — `HostAdapterTests` exercises
what can be reached without a session, and the rest of `Game/` is only exercised in the game. In
particular the vent sweep, the terminal readout and the mod API's delegate table have no automated
coverage. The API's *shape* is checkable without a session and is
worth pinning down.

The load tests in `sim/Thermodynamics.Tests/LoadTests.cs` close part of that gap for cost rather
than for correctness, and they assert work counters rather than milliseconds so they hold on any
machine. What they cannot reach is the adapter: the mass sweep asks the game for a block's mass,
and a harness has no game block to ask, so the rota that bounds it is covered only by arithmetic
tests on its slice function.

`docs/bugs-and-performance.md` records findings from earlier stress work at eight thousand blocks;
entries there that are still open are the performance ones, not the correctness ones.
[load-and-hitching.md](load-and-hitching.md) is the same exercise repeated at a million, and
supersedes its performance section.
