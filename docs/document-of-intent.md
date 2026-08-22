# Document of intent

What this mod is for, what it is trying to be, and the standing goals every decision is measured
against. Each statement here was already made somewhere — in the README's design goals, in a
criterion written down before a corpus run, in a block's own page, in a code comment, or in a
decision recorded and never revisited — and that was the problem: an intent stated once in the
document its author happened to be writing is an intent the next reader finds by accident.

**This page states intent; it does not argue it.** Where a goal is argued at length, this page names
it and points there — the same relationship [rules.md](rules.md) has with the pages that argue its
rules. Where two statements of intent conflict, this page says which the code currently follows.
Where there is no intent at all, this page says that too, because an undeclared intent is decided by
whoever touches the file next.

> The rules argued here are stated canonically in [rules.md](rules.md): `E3` `E5` `E10` `E11` `D6`
> `D7` `C9` `C10`, and the principles P3, P7, P10 and P14 they follow from.

| Looking for | Go to |
| --- | --- |
| The rules a change is measured against | [rules.md](rules.md) |
| Open work, one line each | [backlog.md](backlog.md) |
| What the simulation does now | [thermal-model.md](thermal-model.md) |
| The evidence behind the balance goals | [balance.md](balance.md), [balance-lab.md](balance-lab.md) |

---

## The purpose

**Heat is a resource a player reasons about.** Every block has a temperature, that temperature is
consequential, and a player who is overheating can find out why and do something about it.

Three commitments follow, and they are the test for whether a feature belongs:

1. **It has to be legible.** A player must be able to ask "is this ship able to cool itself at all"
   and get an answer. That question is why the grid heat balance exists — `made` against `vented`,
   in watts, on the cockpit panel and through the API.
2. **It has to be caused.** The outcome must follow from what a builder chose, not from how big the
   ship is. That is `G4` below, and it is the criterion the corpus most clearly passes.
3. **It has to be answerable.** There must be a lever — a radiator, a loop, a heat pump, a different
   place to put the reactor — that visibly changes the outcome. That is `G3`, and the lever that
   works is plumbing rather than bolting.

**It is a framework as well as a mod.** Everything the simulation knows is readable and everything
it does is drivable from another mod, through a delegate table passed by mod message. The intent is
that a second mod can make heat mean something without forking this one — see
[api.md](api.md).

---

## What the README is for

**The [README](../README.md) is the mod's front page for players, not the repository's front page
for developers.** It is written to be copied whole into the Steam workshop description or handed
over as a guide, and to be read end to end by someone who has never opened a source file — so it
explains what the mod does, what it adds, and how to configure it, in that order and in plain
language.

It carries one technical section on purpose: **instructions for modders who want to build on this
framework.** That is the API and what a second mod can do with it, written for someone who has
decided to write code against this one. Both audiences are served by the same page because both
arrive at the same place — a workshop item — and neither should have to be told to go and read a
repository.

**What it is not.** It does not explain how the mod works inside. No solver, no substeps, no
architecture, no measurements, and no defence of a design decision. Everything of that kind lives on
the pages this one sits among, reached through the README's
[documentation index](../README.md#documentation) rather than summarised in the README itself: a
paragraph about the integrator is a paragraph a workshop reader skips and a developer would rather
read in full elsewhere.

**It therefore does not behave the way a repository README does.** The conventions in
[development.md](development.md#documentation-conventions) hold — it states its scope, describes the
present, and keeps a change log — but the audience test overrides the developer one wherever they
disagree: a section earns its place by being something a player or a modder needs, not by being what
a repository is conventionally expected to publish.

---

## What a code comment is for

**A comment names a definition; it does not teach a topic.** Its job is to tell a reader what a
thing is for in as little space as possible, and nothing beyond that: the explanation of *why* the
thing works the way it does belongs on the documentation page that covers it, where it can be
argued at length and found by someone who is not already looking at that line.

So a comment is worth writing in two places — on a definition, and over a chunk of code that is
genuinely complex or abstract. Code that is neither is read faster without one.

Three tests, in the developer's words:

* **It must stand on its own.** A comment that expects the reader to hold context living in another
  file is a bad comment. Either it says enough to be understood where it sits, or it should be a
  pointer to the page that does.
* **Two lines is the limit.** A comment longer than that is a documentation entry that ended up in
  the wrong file. Move the explanation to the page it belongs on and leave the definition with the
  one line that names it.
* **It describes, it does not lecture.** Detail is what the documentation files are for; this page
  and the ones it sits among exist so that no comment has to carry an argument.

---

## How a player perceives heat

Legibility is the first of the three commitments above, and it has three layers. **Only the first is
built.**

### Instruments — built

The terminal panel per block, the cockpit summary, the crosshair readout, the x-ray block overlay
and the room view. These answer a question a player already knows to ask.

**The intent for all of them is that they belong to the game's HUD rather than sit on top of it.**
The readouts are drawn through Rich HUD so they can adopt the game's own placement, scaling and
styling, and the goal is that a player reads a temperature the same way they read a power figure —
without a mod-shaped panel announcing itself. Anything that looks bolted on has failed this even if
every number in it is right.

### Natural feedback — not built

**A player should learn their ship is overheating without looking at an instrument.** This is the
layer that makes heat a felt resource rather than a readout, and none of it exists: there is no
sound emitter and no particle or emissive code anywhere in the mod.

**Audio is for the player in the cockpit, and it is a warning rather than an ambience.** Only a
player at the controls hears it — they are the one who can act. The shape is a short cue with a lead
on it and a distinct one at the event: *a chirp about three seconds before a block crosses its
threshold, a double chirp as it crosses.* That is an illustration of the timing rather than the
final sound.

**Visuals are a ramp, not a state.** A block at its critical temperature glows fully and distorts the
air around it in atmosphere; both fall away **linearly** as it cools and are gone entirely at
ordinary temperatures. The block's own threshold temperature sets the top of that ramp, which is what
keeps it honest across block types — a block that runs hot by design does not spend its life looking
like it is failing.

Both channels are properties of the object rather than marks on the screen, which is what makes them
read as physics instead of as UI.

**Two implementation consequences follow from the three-second lead, and neither is free:**

* **It is a prediction, so it needs a rate.** `ThermalNode.LastDeltaTemperature` is a per-step
  delta, so a rate exists. Extrapolating it linearly will over-warn: a block approaching equilibrium
  slows down as it gets there, so a linear projection crosses a threshold that the block never
  reaches. A cue that cries wolf is worse than no cue, so the projection has to account for the
  approach, or the lead has to be defined some other way.
* **It runs when nothing is wrong.** Every other presentation feature is off unless something reads
  it. This one has to be watching in order to warn, which is the tension resolved in the
  [conflicts table](#where-the-goals-and-the-code-disagree): the reader is the block's own state, and
  the cost on a ship where nothing is hot must be a threshold test and nothing more.

### Thermal vision — wanted, method unknown

**A thermal camera view is an aspiration, not a rejected idea.** An earlier heat overlay was built
and removed, and the limit recorded from it — *mods get no shader, no post-process and no frame
buffer* — is true and is the obstacle rather than the answer. The x-ray block overlay keeps the part
of that work worth keeping, since a debug view *wants* to see through a hull, but it is a debug view
and not the thing.

**The route is genuinely unknown and is expected to be indirect.** Candidates worth investigating,
none of them straightforward: whether any shader or material parameter is reachable from a mod at
all; billboards or transparent materials, which is how the extinguisher overlay already draws;
particle effects as a rendering surface; and per-block emissive, which is the one path the engine
definitely exposes and which overlaps with the glow described above.

This is the one place where the [governing prior](#the-governing-prior-game-mod-first) does not
settle the question. The prior says take the cheap form where the difference cannot be perceived —
here the difference is *entirely* perception, so the cheap form is not obviously the right one.

---

## The governing prior: game mod first

> *Where a simplification costs nothing a player can perceive and avoids real expense, take the
> shortcut, and say plainly when the shortcut would be visible.*

This is the prior that decides what gets built. The mod exists to make heat a real resource, not to
be a finite element solver: **fidelity no player can observe is cost** — in frame time, in code
paths, and in the number of things that can go wrong — bought with nothing.

It is deliberately not a rule, because it is a disposition rather than something a diff can violate.
Its operative half *is* a rule: `D6` — take the shortcut, and record it as a deliberate limit in
[known-issues.md](known-issues.md#deliberate-limits), with the price written down, so nobody
rediscovers it as a bug.

**Realism wins where the difference is visible.** The reactor waste fraction is the worked example
and is worth reading as the pattern: a real plant's efficiency applied to a 300 MW 3×3×3 block
destroys every large reactor in every world, so the fraction is chosen so the *consequences* land
where they should. Applying a real number to a fictional rating compounds the fiction rather than
correcting it. See [balance.md](balance.md#reactor-waste-heat).

### Fidelity is the default; a saving is a switch

The prior above decides what gets *built*. This decides what gets **shipped on**:

> *The simulation mirrors real behaviour as closely as it can. Where a cost-saving approximation is
> worth having, it is a switchable feature rather than the default.*

The two are consistent because P14 is about differences a player **cannot perceive** — where the
difference is invisible the shortcut simply is the model, and there is nothing to switch. Where the
difference *is* perceptible, the cheap form does not become the default by being cheap; it becomes
an option, and the player or the server admin chooses it.

This is what decides the unattended-ship question. A per-grid rate tier driven by observation would
take two thirds of a world's thermal budget away, and it changes what a player finds when they
return: a ship left with a reactor overheating comes back destroyed or fine depending on how coarsely
it was stepped. That is perceptible, so **full simulation is the default and the rate tier is a
switch** — see [What an unattended grid gets](#what-an-unattended-grid-gets).

---

## The four design goals

Stated on the [README](../README.md) and refined here, each with what currently holds it up.

### Light — a mechanism nobody uses costs nothing

Every readout, diagnostic and overlay is off unless something is reading it. Telemetry defaults to
off and costs a handful of static bool reads when it is. Nothing allocates on the stepping path.

**Checked by** `C4` (`bench report`), and by the feature switches under `C7`.

> **The README's formulation is out of date and the intent behind it is not.** It reads *"a grid's
> cost is one pass over its links per substep, and nothing else"*. A substep also runs the
> environment pass, which is per **node** — measured at 3 to 8 links' worth of work at the sizes
> where it matters, which is why the step budget counts `links + 4 × nodes`. The goal is that a
> substep is one pass over the grid and no more; the link-only wording predates the measurement. See
> [benchmarks.md](benchmarks.md#what-a-substep-costs).

### Isolated — every mechanism has its own switch

Switching one mechanism off removes exactly its own cost, takes effect on the next step, and needs
no reload. Absent and empty mean the same thing.

**Checked by** `C7` (`FeatureToggleTests`) and `C8`.

### Tested — the simulation is a library the game happens to call

The core speaks no game type, so it builds and runs outside the session in seconds. That is what
makes it testable, profilable, drivable by another mod, and portable to another engine — the four
are one property, not four.

**Checked by** `C5` (`CoreIsolationTests`). The suite is 1,532 tests, 33 deterministic scenarios and
a load benchmark that reaches a million blocks in one grid.

### Open — the API is part of the contract

The delegate table is the mod's interface to every other mod, and it is a dictionary of strings to
delegates, so a caller finds a wrong name out at run time in someone else's session.
[api.md](api.md) is therefore part of the contract rather than a description of it.

**Checked by** `R9` (`EveryModApiEntryIsDocumented`).

---

## What correctness means

**The solver's three invariants are the definition of correctness; everything else is tuning.**

| Invariant | Meaning |
| --- | --- |
| **Order independence** | Iteration order cannot change the result. |
| **Energy conservation** | Every internal exchange is applied equally and oppositely. |
| **Boundedness** | No pairwise exchange can carry a pair past their shared equilibrium. |

This is principle P10, and it is what lets everything else be argued about freely: a change that
holds all three is a tuning question, and a change that breaks one is a defect whatever it buys.
See [thermal-model.md](thermal-model.md#the-three-invariants).

**How claims are made is itself intent.** The evidence discipline — a figure carries its scope, a
blind spot is part of the result, a criterion is fixed before the data, nothing is its own oracle —
is stated in full in [rules.md](rules.md) and is not restated here. Two standing instructions from
the developer sit alongside it:

* **Validate on real grids.** Do not trust in-game numbers as the basis for a claim. A figure a
  running session reported cannot be re-examined — the ships are gone and the world is gone. Build
  the lab and measure the workshop corpus instead, where every input is visible and the run repeats.
  A field dump is one sample; say that it is one.
* **Measure before fixing.** More than one thing on this project has looked guilty from the counts
  alone and been innocent. The room map is the standing example: it was suspected of losing
  compartments, and when the comparison was finally run it was right about twelve of twelve.

---

## Balance goals

Six criteria, written down before any data was collected so that a run which fails them is a finding
rather than an excuse to move a threshold (`E1`, `E11`). They are argued in
[balance-lab.md](balance-lab.md#0-define-good-balance-before-collecting-anything) and measured in
[balance.md](balance.md).

| # | Goal | Status against 8,132 corpus ships |
| --- | --- | --- |
| **G1** | **Idle is safe.** A ship at rest in the environment it was built for does not overheat. | **Holds.** 0.22% go critical at idle, against a ~1% gate. |
| **G2** | **Load bites.** Under sustained full power a meaningful share of uncooled ships reach a warning state. | **Holds.** 65.5% of ships carrying no jump drive reach 400 K under full electrical load, against a ~20% gate. |
| **G3** | **Cooling works.** Fitting radiators or a loop moves the outcome. | **Answered.** Plumbing works — a sink face carries 1,000 W/K against a bolt joint's 167 — and bolting makes more ships worse than better. It fits on 15% of *finished* hulls, which is a fact about retrofits rather than about the mechanic, now that cooling is stated as designed in. |
| **G4** | **Design decides, not size.** Outcome follows what a builder controls. | **Holds, strongly.** Peak correlates +0.89 with worst local W/m² under load against +0.49 with block count. |
| **G5** | **No death spiral.** A ship past critical that throttles to idle returns below critical in bounded time. | **Holds as written.** Its stated reason does not — see below. |
| **G6** | **Affordable across the population**, at p95/p99 rather than at the mean. | **Holds.** Corpus p99 substep demand is 6.02 in vacuum against 64 granted; the worst measured atmospheric case is 36.7 at p95. |

**G6 is not a balance criterion and is on the list on purpose.** The population's stiffness tail is
what decides lumping, multirate stepping and the substep cap, and no synthetic ladder can show its
shape.

### Cooling is designed in — and a vanilla ship still has to survive

**The intent is that players design cooling in.** A hull that was laid out with heat in mind is the
ship the mod is for, and that is why the sink face carries 1,000 W/K against a bolt joint's 167.

**It must not follow from that that a vanilla design is unusable.** This game has been out a long
time, and two cases are load-bearing:

* **A player adds this mod to an existing world and loads it.** Their fleet must not begin falling
  apart. This is the case `G1` protects, and it holds: 0.22% of the corpus goes critical at idle.
* **The game spawns a vanilla prefab.** Cargo ships, drones, encounters and respawn ships must not
  disintegrate on arrival. **This has never been measured**, and it is measurable — see below.

So the balance target is a floor as well as a ceiling. `G2` asks that load bites; this asks that it
does not bite an unmodified ship that is simply *there*. The two are not in tension at idle, and
they are the two ends of the same dial under load.

> **The prefabs are sitting in the game install and nothing has ever run them.** There are **705**
> prefab files under `Content/Data/Prefabs`, including 46 planetary encounters, 41 unknown signals,
> 38 global encounters, 32 random encounters, 23 cargo ships, 47 drones and the 2 respawn ships.
> They hold `<CubeGrids><CubeGrid>` exactly as a workshop blueprint does; the corpus parser keys on
> `Descendants("ShipBlueprint")`, so reading them is a one-element change to a parser that already
> exists rather than new machinery. Tracked as [backlog](backlog.md) C10.

### Who it is balanced for

**Vanilla play, in the knowledge that worlds and blocks get modded.** The mod has to be a jack of
all trades, because who runs it and in what situation is unknowable: the intent is to **minimise its
potential to blow up in a player's face while not being a drag on performance**, and those two
together are the whole balance problem.

The long-run intent is regimented tests covering every scenario a grid can find itself in, collecting
every figure that could bear on balance. That is what the lab and the corpus are for, and it is why
`G6` — a cost criterion — sits on a list of balance criteria.

**The evidence is not there yet, and the gap is one-sided.** All five corpus scenarios are in vacuum;
air costs about six times what vacuum costs and is where the substep budget is actually spent, and
the atmospheric evidence is 49 hulls against 8,132. Relatedly, the servers this mod is played on
commonly run a 300 m/s speed limit against vanilla's 100, which multiplies the friction term by 27
and the cooling term by 1.37.

### A cooling system costs power, and makes heat doing it

**A pump is a motor.** It draws electricity and it makes waste heat like any other motor, and being
part of a coolant loop does not exempt it. This mirrors the physics, which is the default under
[fidelity](#fidelity-is-the-default-a-saving-is-a-switch).

**Not built.** The pump's switch and speed slider reach the loop and the ring's flow refreshes when
either moves, but `MaxPowerWatts` is never assigned, so a pump draws nothing and heats nothing — a
loop is free to run once built, where a heat pump pays for every watt it lifts. The symmetric
implementation exists to copy: `ThermalHeatPumpBlock` attaches a `MyResourceSinkComponent` in code
during `Init`, because an upgrade module has no definition field for one. Tracked as
[backlog](backlog.md) B2.

### Every thermal property is a dial

**A dial is anything that changes how heat moves through the system**, and a block's thermal
properties are all dials — conductivity, specific heat, emissivity, exposed-surface multiplier,
critical temperature, the waste fractions. Balancing the mod means turning them until the population
behaves; there is no separate class of "physical constants" that is off limits.

**The convention is where they start, not whether they may move.** A block's `Conductivity` and
`SpecificHeat` are written as the figure a materials table gives, because a definition that reads as
a description of the material is one a third-party author can write without asking anyone. The waste
fractions are chosen for their consequences and say so. Both are dials; they differ in what a
sensible starting value looks like.

This is why the settings and the definition files are converging into one surface — see
[configuration.md](configuration.md#the-settings-surface-and-where-it-is-going). A player asking how
fast coolant moves is asking a balance question, and it should not matter to them which file the
answer lives in.

### The pace the mod is meant to be played at

`HeatTimeScale` is the clock and 1 is real. **A fresh world runs `responsive`** — full integration
accuracy with the clock at 225 — because real thermal time is physically honest and far too slow to
play. Dividing every heat capacity by *k* is exactly running thermal time at *k*×, so equilibrium
temperatures and every ratio between mechanisms are untouched; only the clock moves.

The five profiles are a ladder on two axes rather than five unrelated tunings: how faithfully the
simulation is integrated, and how fast heat is made to move. See [profiles.md](profiles.md).

---

## Performance intent

**A grid's steady cost is proportional to the grid, and a change to it is proportional to the
change.** Placing a block costs that block's degree, not the graph; the room map is budgeted and
resumable; a step is spread across the frames of its window rather than landing whole on one.

### The scale target

**A single grid of 10⁶ blocks running at `SimulationSpeed` 1.0**, in either game. Stress bounds above
that are uncapped — the ladder is allowed to go wherever it goes — but a million blocks at real pace
is the figure the design is for.

**The realistic figure is around 250,000.** Servers may reach a million; most will not. That matters
because the two numbers are answered by different work: 250k is a tuning problem and a million is a
structural one.

Measured against the current ladder, on hulls built from the block census:

| Blocks | Full step | Tick | Substeps granted of 12 demanded | Against a 16.7 ms frame |
| ---: | ---: | ---: | ---: | --- |
| 126,731 | 22.29 ms | **2.43 ms** | 1 | comfortable |
| 505,566 | 67.06 ms | **13.18 ms** | 1 | inside |
| 1,000,294 | 118.20 ms | **25.87 ms** | 1 | **1.55× over** |

**The realistic target is already met and the stated one is not, on two counts.** At a million blocks
the tick is over a 60 fps frame, *and* the step is being shortened by `MaxElementVisitsPerStep` to
1 substep against the 12 the grid's stiffness asks for — so simulated time is not advancing at 1.0
either. Closing it means not touching every node every step: activity tracking, chunking and
multirate stepping, all designed in [scale-design.md](scale-design.md) and none of it built.

### Use the machine, and stay off the game thread

**The mod should use as much of the CPU as it can while taking as little of the main thread as
possible.** The two halves are one goal: the game's simulation stability is set by what happens on
its own thread, so work moved off it buys frame-time stability *and* lets this mod do more.

This is a target, not a description. **Nothing in the mod is threaded today** — every grid solves on
the game thread — and the design has been kept ready for it rather than built:

* **The solver is already safe to split.** Order independence is one of the three invariants: every
  exchange reads start-of-step temperatures and writes into an accumulator, so no node sees
  another's new value and iteration order cannot change the result. That property was kept for
  correctness and pays for parallelism for free.
* **The engine allows it.** `MyAPIGateway.Parallel` offers `For`, `ForEach`, `Do` and `Start`,
  backed by `ParallelTasks`. See [engine-notes.md](engine-notes.md#parallelism-is-available-to-mods).
* **The shape is decided by what may race.** Reads must not race with the game mutating a grid, so
  the natural split is *solve in parallel, apply on the game thread* through `InvokeOnGameThread`.

**Where the payoff is depends on size, and the measurements point in opposite directions.** Eight
thousand blocks solve in 0.128 ms a step, which may already be under the cost of a thread hand-off;
a 242-grid fleet spent 25.9% of real time in the solver, and a million-block step is 118 ms and
atomic. Per-grid parallelism across many grids and per-grid splitting of one huge grid are different
changes, and the fleet figure argues for the first before the second.

Two things must survive it, and both are already rules: the three invariants (`C6`), and that
nothing allocates on the stepping path (`C4`).

### What an unattended grid gets

**The same simulation as an attended one.** A grid nobody is looking at is stepped like any other,
because what a player finds when they return is exactly the kind of difference they can perceive —
and [fidelity is the default](#fidelity-is-the-default-a-saving-is-a-switch).

The saving is real and is worth having as an option: three identical capital ships took 81% of one
world's thermal budget with at most one player aboard, and a per-grid rate tier driven by distance,
by whether anything is reading the grid, or by whether anything on it is *doing* anything would take
two thirds of that away. The model already permits it — steps are energy-conserving and
order-independent, so a grid can advance coarsely and catch up in one long step.

**It is a switch, not a default**, and what it gives up is stated: a coarse-stepped grid reaches the
right steady state and a different transient. For a derelict that is correct. For a ship the player
left with a reactor overheating it is the difference between finding it destroyed and finding it
fine.

**A long run is designed for its own death** (P13): capped, resumable, streaming, and counting what
it did not measure. This is intent about the harness rather than the mod, and it exists because an
uncapped corpus sweep has taken a machine down.

---

## What the mod owes a multiplayer client

**As little traffic as possible, and deviation is acceptable to a point.** Keeping every block's
temperature in sync across the network would degrade the thing the mod is trying to protect, so the
server sends the minimum that keeps the world coherent and lets the rest drift.

| | Where it lives |
| --- | --- |
| **Damage** | Server. It propagates damage and it is authoritative over what a block loses. |
| **Settings and pump controls** | Server, replicated. 44 of the 49 serialized fields reach every client. |
| **Temperatures** | Each machine's own simulation, from the same inputs. Not reconciled. |

**Drift is the design, not a defect.** The original shape was to seed a client's temperatures once
at load, let them diverge, and re-sync a grid in full when it was worth doing. That is probably not
the final answer, but the implication holds: **transmit as little as possible, and tolerate deviation
up to a point.**

**What is not yet decided is where that point is.** Nothing states how far a client may drift before
it must be corrected, or what triggers a re-sync — and the answer matters because divergence stops
being cosmetic the moment a player makes a decision from a readout the server will not honour. A
client watching a block sit below critical while the server destroys it is the case to design
against.

---

## Portability intent

**One simulation model serves Space Engineers 1 and 2 — the model, not a shared binary.** The two
games get separate builds and separate adapters, and the line between model and adapter is the point
of the architecture: everything the game supplies crosses one of three boundaries — block layout, an
environment sample, and results out — and nothing else.

The remaining distance is storage rather than mathematics. The geometry, the conduction graph and
the integrator already work from integer AABBs and cost the same whatever a block's volume;
`GridModel.blocksByCell`, `SurfaceMap.states` and `BlockInstance.Cells` are still one entry per
occupied cell, which is what a 0.25 m lattice cannot afford. See
[scale-design.md](scale-design.md#cell-centric--boundary-centric).

---

## What this mod deliberately is not

Recorded so nobody rediscovers a decision as a bug. Each carries its price in
[known-issues.md](known-issues.md#deliberate-limits).

**A thermal camera is not on this list.** It was, on the grounds that mods get no shader and no
frame buffer. That is a statement about difficulty, not about intent — it is wanted. See
[Thermal vision](#thermal-vision--wanted-method-unknown).

* **Not a per-block radiative transfer model.** Emissivity doubles as absorptivity; a block cannot
  be shiny to the sun and black to space.
* **Not a build-state simulator.** A block at 10% construction carries its full thermal properties.
  The machinery to change that exists and the difference would be invisible next to the heat a
  block's neighbours carry.
* **Not a destruction model, in the lab.** The harness never removes an overheating block, so every
  peak above critical describes the harness rather than the mod. Crossing times are unaffected.
* **Not authoritative over the game's own systems.** Room pressure is the game's answer, not this
  model's, and none of the three sources that can empty a room may insist on air — only refuse it.

---

## Where the goals and the code disagree

Each row is a conflict between two statements this repository makes, or between a statement and what
ships. **The resolution column says which the implementation currently follows**, which is evidence
about intent rather than a decision on the developer's behalf.

| Conflict | Resolution |
| --- | --- |
| **`G5`'s rationale against the measured damage timing.** `G5` is *"a player must be able to react to a warning"*. Of ships that cross critical under full electrical load the median crosses at **8.9 s**, and at p10 at 3.5 s. | **The measurement is correct and the goal's rationale is not met.** `G5` as literally written — bounded recovery — holds; the reason given for it does not. This is the balance decision the corpus most clearly asks for, and it is not a defect. See [balance.md](balance.md#damage-arrives-too-fast-to-be-played-around). |
| **The 2–5 minute significance window against `HeatTimeScale` 225.** The balance target asks for the most significant thermal event to land in a 2–5 minute window. **No block in the game lands in it**, and none can be made to at 225: of the 72 block types that cannot cool themselves, 0 fall in 120–300 s. | **The shipped clock wins, and the window is currently unreachable.** The dial that reaches it is `HeatTimeScale` ≈ 11, which pushes a hull's settling time to eight hours. Raising conduction and lowering the clock together is the projected route and **has never been measured** — every knob row moves one dial. |
| **`G2` "the criterion the current build most likely fails"** against the corpus, where G2 passes at 65.5%. | **The corpus is correct; the prediction was written before it.** The prediction stands in [balance-lab.md](balance-lab.md) as a criterion's original wording, which is right — a criterion is not edited once the data arrives (`E11`). Read the status from [balance.md](balance.md), not from the prediction. |
| **"The radiator is a block you plumb"** against the retrofit measurement, where a plumbed ring fits on **15%** of warm hulls and bolting — which fits nearly everywhere — makes more ships worse than better. | **Resolved: cooling is designed in.** A hull laid out with heat in mind is the ship the mod is for, so a 15% retrofit rate is not the failure it looks like — it is the measurement of how many finished hulls happen to have room. What the answer does *not* license is a balance that breaks unmodified ships, which is the floor stated in [Cooling is designed in](#cooling-is-designed-in--and-a-vanilla-ship-still-has-to-survive). `G3` should be scored against designed-in cooling, not against retrofits. |
| **"Light: a grid's cost is one pass over its links per substep"** against the measured per-node cost of the environment pass. | **The measurement wins and the goal is unchanged.** The step budget already counts `links + 4 × nodes`. The README's wording predates the measurement and understates what a substep does. |
| **`MaxSubstepsPerBlock 6` recommended in the field** against the shipped default of `0` (off). | **The shipped default stands, and the recommendation is weaker than it was.** Re-measured at the shipped `Frequency 8`, a cap of 6 buys 1.6× rather than 3.4× — the original table was taken at `Frequency 4` and read as if it were the default. Still a clear win, no longer a dramatic one, and open as [backlog](backlog.md) C3. |
| **Natural feedback against the Light goal.** The README says *"every readout, diagnostic and overlay is off unless something is reading it"*. [Natural feedback](#natural-feedback--not-built) is the first presentation feature meant to be **on** by default — a player who has not opened anything is exactly who it is for. | **Both stand, and the resolution is a definition rather than a compromise.** Feedback that only runs when a block is near its limit *is* "something reading it" — the reader is the block's own state, not a player with a panel open. What it must not do is cost anything on a ship where nothing is hot, which makes the trigger a threshold test on a ratio the solver already computes, and `C7` still applies: it needs its own switch like every other mechanism. |
| **The README's stated audience against what it currently carries.** The README is meant to be pasteable into the workshop and readable by a non-technical player, with one section for modders. It currently also carries a repository layout tree, a building-and-testing section and the documentation index — three sections written for somebody who has cloned the repository. | **The intent is newer than the page, and the page has not been changed to match it.** Nothing is wrong with the content; it is in the wrong place for the audience the page is for. Moving the layout and the build instructions under [development.md](development.md) and reducing the documentation index to one link is the change this asks for, and it is not made here. |
| **The census hull as "a worst case" against "what a ship does".** It makes 12.1 kW a block against a real median of 335 W — the 96th percentile. | **Undecided, and recorded as such in the code.** `TheCensusHullMakesFarMoreHeatThanARealShip` pins the figure and fails if it changes quietly. It reaches every temperature figure and no stiffness figure. |

---

## What is still undecided

The eight areas this page opened with as having no stated intent are now answered, and are stated
above rather than here. What follows is what those answers left open — smaller questions, but each
one still decided by whoever touches the file next.

### 1. How a thermal camera could be built

The intent is stated and the route is not known. Mods get no shader, no post-process and no frame
buffer; the candidates are per-block emissive (the one path the engine definitely exposes),
transparent materials, particle effects as a rendering surface, and whether any material parameter
is reachable at all. **This is a research task before it is a design task.**
[backlog](backlog.md) B24.

### 2. The warning cue itself

The *shape* is decided — a lead cue and an event cue, cockpit only, with a linear visual ramp keyed
to each block's threshold. The cue itself is an open question, and so is the lead: three seconds is
an illustration, and it collides with the fact that a linear projection over-warns on a block that
is levelling off. **This wants prototyping in game rather than deciding on paper.**
[backlog](backlog.md) B25.

### 3. How far a client may drift

"Tolerate deviation to a point" does not say where the point is, what measures it, or what a re-sync
costs when it fires. See [What the mod owes a multiplayer client](#what-the-mod-owes-a-multiplayer-client).

### 4. Whether the compatibility floor becomes a scored criterion

*A vanilla prefab must survive being spawned* is stated intent and is the right shape for a
criterion: it is falsifiable, it is measurable against 705 prefab files that already exist, and it
can fail. It is not currently one of the six, and adding a criterion after the data exists is exactly
what `E11` forbids doing quietly. **If it is to be scored it should be written down before the
prefabs are first run**, not after.

### 5. What "as much of the CPU as possible" means concretely

The direction is unambiguous and the target is not: how many threads, whether the mod may saturate a
machine a server is sharing with other mods, and whether parallelism is per grid or within one grid.
The two measurements point opposite ways — 0.128 ms for 8,000 blocks may be under the cost of a
hand-off, while a 242-grid fleet spent 25.9% of real time in the solver — which argues for across
grids before within one. [backlog](backlog.md) D19.

### 6. Where the visual ramp starts

"The threshold temperature should probably dictate much of what that means" is the stated
expectation, with the *probably* intact. Because radiated power rises as the fourth power, a band
defined as a fraction of critical is not a band defined as a number of seconds, and the two give
very different warnings on very different blocks.

---

## Change log

| Date | Change |
| --- | --- |
| 2026-08-22 | Recorded what a code comment is for: it names a definition or an especially complex chunk of code, stands on its own without context from another file, and stays inside two lines — anything longer is a documentation entry in the wrong file. |
| 2026-08-22 | Recorded what the README is for: a workshop-pasteable front page for players, with one section for modders building on the framework, and deliberately not an explanation of how the mod works inside. Recorded the conflict this creates with the three developer-facing sections the page carries today. |
| 2026-08-22 | Renamed from `document_of_intent.md` to match the kebab-case every other page uses. **Answered all eight areas that had no stated intent**, which are now stated in the body: cooling is designed in but a vanilla ship still has to survive; the scale target is 10⁶ blocks at `SimulationSpeed` 1.0 with ~250,000 as the realistic figure and uncapped stress bounds; a coolant loop costs power and makes waste heat, because a pump is a motor; an unattended grid gets the same simulation as an attended one, with the saving available as a switch; the mod is balanced for vanilla play in the knowledge that worlds get modded; the warning is a cockpit-only lead cue with a linear visual ramp keyed to each block's threshold; every thermal property is a dial; and a client is owed the minimum traffic that keeps the world coherent, with drift tolerated. Added the principle those answers share — **fidelity is the default and a saving is a switch** — which is what P14 leaves undecided once a difference *is* perceptible. The voids section is replaced by the six smaller questions the answers left open. |
| 2026-08-22 | Recorded four intents stated by the developer after this page was first written. **A thermal camera is wanted** — it had been recorded here and in [known-issues.md](known-issues.md) as a deliberate limit on the grounds that mods get no shader, which is a statement about difficulty rather than about intent. Added [How a player perceives heat](#how-a-player-perceives-heat), covering the three layers of it: instruments that belong to the game's HUD rather than sit on top of it, natural feedback through subtle audio and in-world visuals, and thermal vision as an open problem with no known route. Added the threading target to [Performance intent](#performance-intent): use as much of the CPU as possible while taking as little of the game thread as possible. Void 6 is rewritten — the form of a warning is now stated, and its timing is what remains undefined. |
| 2026-08-22 | Created, by gathering the statements of intent scattered across the README's design goals, `rules.md`'s judgement rules, the balance criteria, the deliberate limits, the profile ladder and the developer's standing instructions, and reconciling each against what the code does. Seven conflicts are resolved against the implementation; eight areas are recorded as having no stated intent at all. Two figures were corrected on the way: the median time to critical at idle is 104.5 s rather than the 112 s the register's prose carried, and the damage-timing finding it belonged to — dropped in an earlier merge — is restored to [balance.md](balance.md#damage-arrives-too-fast-to-be-played-around). |
