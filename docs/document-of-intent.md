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
> `D7` `C9` `C10` `R14`, and the principles P3, P7, P10 and P14 they follow from.

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

**Consequential includes the player.** Heat that only ever damages blocks stops at the airlock, and
a burning compartment that a person can stand in is the mod saying the temperature does not really
matter. The suit is what closes that — a machine that holds its occupant and can be beaten, rather
than a threshold — and it is described in [configuration.md](configuration.md#the-suit).

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

This is `R14`, and the check on it catches the failure the length limit prevents: a comment that
argues a topic is maintained against a subject it does not sit beside, and one whose subject is
deleted stays behind and describes whatever is below it.

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

Legibility is the first of the three commitments above, and it has three layers. **The first two are
built; the third is not.**

### Instruments — built

The terminal panel per block, the cockpit summary, the crosshair readout, the x-ray block overlay
and the room view. These answer a question a player already knows to ask.

**The intent for all of them is that they belong to the game's HUD rather than sit on top of it.**
The readouts are drawn through Rich HUD so they can adopt the game's own placement, scaling and
styling, and the goal is that a player reads a temperature the same way they read a power figure —
without a mod-shaped panel announcing itself. Anything that looks bolted on has failed this even if
every number in it is right.

### Natural feedback — built

**A player should learn their ship is overheating without looking at an instrument.** This is the
layer that makes heat a felt resource rather than a readout. It is two channels.

**The glow is the last hundred kelvin before a block fails.** Nothing until a block is within 100 K
of its own critical temperature, then a straight ramp to full at that temperature and above. The
window is narrow on purpose: a glow means *this block is about to go*, not *this block is warm*, so
it stays dark through every temperature a ship runs at in ordinary play and comes on only at the
end. Because the band is a fixed hundred kelvin rather than a share of the rating, the same distance
from failure looks the same on a decorative block and on a large thruster — and it is a longer
warning on a block that heats slowly than on one that heats fast, which is the right way round.

**The colour is physics and stays absolute**: the Planckian locus, computed from Planck's law
against the CIE 1931 observer. A block glowing at 500 K is deep red and one at 2,000 K is orange,
whatever either is rated for. So brightness says how close to failing and colour says how hot it
actually got, and a decorative block dying dull red beside a thruster dying orange reads correctly.

> **The brightness is deliberately not incandescence, and the measurement is why.** A real solid
> emits no visible light below the Draper point at 798 K, and the shipped definitions put **26 % of
> block types below that** — keyed to the physics, a quarter of the game would fail with no visual
> warning at all, and a decorative block rated 583 K would go from dark to destroyed with nothing in
> between. [Game mod first](#the-governing-prior-game-mod-first) settles it: take the physical form
> where the difference cannot be perceived, and here the difference is the whole point of the
> channel. The colour keeps the physics, because nothing is lost by being right about it.

**The glow is drawn, because the emissive path could not carry it.** `UpdateEmissiveParts` writes to
a named material in the block's model, and of the 1,992 base cube models the game ships **418 hold
one — of 479 armour models, four**; where the material exists at all it is a status lamp rather than
a skin. So the glow is an additive quad over each *exposed* face of a hot block, coloured by the
locus and brightened by the ramp, with one dynamic light per grid at the glow-weighted centre of its
failing blocks so the heat falls on what is around it. The emissive write is kept beside it, because
a lamp going red on a hot reactor is right. Exposed faces only, which is both the cheap answer and
the correct one: a face buried in a hull cannot be seen.

**The cue is the same warning in sound.** A short sound about three seconds before a block crosses
its critical temperature and a distinct one as it crosses, heard only by the player at the controls,
because they are the only one who can act. It reaches what the glow cannot — a block behind another,
a block off screen, a player looking the other way — which is a different set from the one it used
to cover, now that a block with no emissive material glows like any other.

Both channels are properties of the object rather than marks on the screen, which is what makes them
read as physics instead of as UI.

**The three-second lead is a prediction, and a straight line is the wrong one.** A block warming
toward an equilibrium slows down as it approaches, so projecting its current rate forward crosses
thresholds the block never reaches — every hot-running reactor in the game would be warned about,
once, for as long as it was warming up, and a cue that cries wolf is worse than no cue. `HeatWarning`
reads the equilibrium off the rate instead: heat flow out of a block grows with the gap between it
and everything around it, so the rate decays by a fixed factor each interval and two rates one
interval apart give both the time constant and the temperature the block is heading for. **A block
heading somewhere below its rating is not warned about at all**, which is the whole difference. A
block whose rate is *rising* has no equilibrium to read, so the straight line answers there — it
under-estimates a runaway, which warns early, and early is the safe direction to be wrong in.

**It runs when nothing is wrong.** Every other presentation feature is off unless something reads
it. This one has to be watching in order to warn, which is the tension resolved in the
[conflicts table](#where-the-goals-and-the-code-disagree): the reader is the block's own state, and
the cost on a ship where nothing is hot is **one comparison for the whole grid** — the hottest
block, which the observation pass already caches, against the lower of where the coolest block would
start glowing and where it would start being watched. Both come off that block's own rating, so the
floor follows what the ship is made of.

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

**Checked by** `C5` (`CoreIsolationTests`). The suite is 1,760 tests, 33 deterministic scenarios and
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
| **G6** | **Affordable across the population**, at p95/p99 rather than at the mean. | **Fails in air, and had only been scored in vacuum.** Corpus p99 substep demand is 6.02 in vacuum against 64 granted; at 200 m/s in thick air the panel's p99 is **73.4**, fourteen of forty-nine hulls are over the cap, and at 300 m/s the *median* ship is. The atmospheric figure this row used to carry, 36.7, was taken at `Frequency` 8 and is exactly half. See [balance.md](balance.md#air-is-where-the-substep-budget-goes-and-the-shipped-pair-does-not-fit-it) and [backlog.md](backlog.md) `C19`. |

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
  disintegrate on arrival. This is `G7`, and **it holds**.

So the balance target is a floor as well as a ceiling. `G2` asks that load bites; this asks that it
does not bite an unmodified ship that is simply *there*. The two are not in tension at idle, and
they are the two ends of the same dial under load.

> **All 705 prefabs the game ships have now been simulated.** Idle, in the environment each
> category spawns into, for five simulated minutes: **461,428 blocks across 705 ships, and not one
> of them crosses its critical temperature, let alone loses a block.** Run `-- prefabs`.
>
> **And the same 705 flown hard lose 616.** That is the control rather than the criterion — a floor
> that can only ever pass has not been tested — and the distance between the two numbers is the
> whole of what this mod is: a ship that arrives is safe, and a ship that is worked is not.
>
> Two things the walk cannot see, which are part of the result. **1,503 blocks are subtypes the
> installed game no longer defines** — coloured legacy armour, every one of them in
> `LegacyContent` — so they are not simulated. And one prefab is stored gzipped, which the parser
> now un-gzips; before that it was one ship the floor was never measured on.

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

**Built.** A coolant pump draws through a `MyResourceSinkComponent` attached in code during `Init`,
because an upgrade module has no definition field for one — the same shape `ThermalHeatPumpBlock`
already used. The heat comes free with the cost: `ThermalBlock` subscribes to whatever sink an
entity carries and turns its draw into `PowerConsumedWatts`, which the waste-heat model converts
through the block's own fraction. That fraction is **1** for a pump, and the reason is physics
rather than balance: a circulator does no work that leaves the system, so its shaft power dissipates
as friction in the coolant it is pushing and its motor losses stay in the block.

**The rating is derived, not chosen.** A large-grid ring moves 200 kg/s of coolant — four
two-and-a-half-metre parcels a second at fifty kilograms each — and pushing that against two bar of
head at seventy per cent efficiency is `ṁ ΔP / (ρ η)` = 57 kW. Fifty is the rating; the small-grid
block is a fifth, on the same reasoning the heat pump's rating uses.

**The asymmetry with the heat pump is now a stated design rather than an accident.** The ring
carries 680 kW for every kelvin of difference around it, so a circulator costs well under a per cent
of what it moves, where a heat pump pays a third. Both blocks exist because those two prices are
different, and that could not be said while one of them was free.

An under-supplied pump circulates proportionally slower rather than stopping, so a ship whose
reactors are failing loses its cooling gradually rather than all at once.

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
[configuration.md](configuration.md#where-the-settings-surface-is-going). A player asking how
fast coolant moves is asking a balance question, and it should not matter to them which file the
answer lives in.

### The pace the mod is meant to be played at

`HeatTimeScale` is the clock and 1 is real. **A world runs the clock at 225** — with full
integration accuracy, since real thermal time is physically honest and far too slow to play.
Dividing every heat capacity by *k* is exactly running thermal time at *k*×, so equilibrium
temperatures and every ratio between mechanisms are untouched; only the clock moves.

**There is one configuration, and it is the most faithful one the model has.** Presets were tried and
removed: a preset makes a block's behaviour depend on which one a world happens to run, and it puts
an approximation in front of a player who did not ask for one. What a world may still do is move an
individual setting. See [configuration.md](configuration.md), and [realism.md](realism.md) for how
far the model is from physics whatever the settings say.

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

* **Not a per-block radiative transfer model.** A face radiates to the sky or to nothing: two hot
  blocks facing each other across a gap do not see each other, and no view factors exist. A surface
  *can* now be shiny to the sun and black to space — `SolarAbsorptivity` is separate from
  `Emissivity` — but each is one constant rather than a curve against wavelength.
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
| **`G5`'s rationale against the measured damage timing.** `G5` is *"a player must be able to react to a warning"*. Of ships that cross critical under full electrical load the median crosses at **8.9 s**, and at p10 at 3.5 s. | **Resolved: the goal holds, and the conflict was a mis-measurement.** The crossing is the moment the damage rate leaves *zero*, not the moment a block is lost — the first block goes at a median **37.0 s**, and with the cue's three-second lead the median pilot has **40 s** from the first warning. `G5` holds as written and its rationale holds too. See [balance.md](balance.md#how-long-a-block-has-after-it-crosses). |
| **The 2–5 minute significance window against `HeatTimeScale` 225.** The balance target asks for the most significant thermal event to land in a 2–5 minute window. **No block in the game lands in it** at the shipped pair, and none can be made to at 225 by the clock alone: of the 72 block types that cannot cool themselves, 0 fall in 120–300 s. | **The shipped clock still wins, and the conflict has moved from a price to a trade between two goals.** Conductivity ×4 with `HeatTimeScale` 80–120 reaches the window — crossing 124–186 s, recovery inside the hour, `G1`, `G2` and `G5` kept — and costs *less* than what ships in air, where the budget is actually spent. **Built and measured 2026-08-23, and it is not the price that stops it.** At ×4 a coolant sink stops out-performing the best surface dial, bolting a radiator starts working, a 300 MW reactor buried in armour settles inside its rating, and the stiffest block on the census hull stops responding to air. That is `G3`'s lever and this page's *there must be a lever* going away to buy `G8`'s timing, and the sweep that chose ×4 never scored `G3`. **What has not been tried is a dial that is not transport** — waste heat and the critical temperatures move the crossing without touching the conduction pace at all. [backlog.md](backlog.md) `C12`. |
| **`G2` "the criterion the current build most likely fails"** against the corpus, where G2 passes at 65.5%. | **The corpus is correct; the prediction was written before it.** The prediction stands in [balance-lab.md](balance-lab.md) as a criterion's original wording, which is right — a criterion is not edited once the data arrives (`E11`). Read the status from [balance.md](balance.md), not from the prediction. |
| **"The radiator is a block you plumb"** against the retrofit measurement, where a plumbed ring fits on **15%** of warm hulls and bolting — which fits nearly everywhere — makes more ships worse than better. | **Resolved: cooling is designed in.** A hull laid out with heat in mind is the ship the mod is for, so a 15% retrofit rate is not the failure it looks like — it is the measurement of how many finished hulls happen to have room. What the answer does *not* license is a balance that breaks unmodified ships, which is the floor stated in [Cooling is designed in](#cooling-is-designed-in--and-a-vanilla-ship-still-has-to-survive). `G3` should be scored against designed-in cooling, not against retrofits. |
| **"Light: a grid's cost is one pass over its links per substep"** against the measured per-node cost of the environment pass. | **The measurement wins and the goal is unchanged.** The step budget already counts `links + 4 × nodes`. The README's wording predates the measurement and understates what a substep does. |
| **`MaxSubstepsPerBlock 6` recommended in the field** against the shipped default of `0` (off). | **The shipped default stands, and the recommendation is weaker than it was.** Re-measured at the shipped `Frequency 8`, a cap of 6 buys 1.6× rather than 3.4× — the original table was taken at `Frequency 4` and read as if it were the default. Still a clear win, no longer a dramatic one, and open as [backlog](backlog.md) C3. |
| **Natural feedback against the Light goal.** The README says *"every readout, diagnostic and overlay is off unless something is reading it"*. [Natural feedback](#natural-feedback--built) is the first presentation feature meant to be **on** by default — a player who has not opened anything is exactly who it is for. | **Both stand, and the resolution is a definition rather than a compromise.** Feedback that only runs when a block is near its limit *is* "something reading it" — the reader is the block's own state, not a player with a panel open. What it must not do is cost anything on a ship where nothing is hot, which makes the trigger a threshold test on a ratio the solver already computes, and `C7` still applies: it needs its own switch like every other mechanism. **Built, and the cost is one comparison per grid** — the hottest block against the lower of the glow's floor and the coolest block's watch point — with `HeatGlow` and `HeatWarningSound` as the two switches. |
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

The shape is decided and built, and the projection is no longer the problem it was: a block heading
somewhere below its rating is not warned about, because the forecast reads the equilibrium off the
rate rather than extrapolating a straight line. **What is still open is the sound**, which is
currently two of the game's own destruction cues standing in for a lead chirp and an event chirp,
and which wants hearing in a cockpit rather than deciding on paper. **Three seconds is a defensible
lead now that the window is measured**: the median ship loses its first block 37 s after the load
and 24 s after the crossing, so the cue is early rather than late. [backlog](backlog.md) `F15`.

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

### 6. Where the visual ramp starts — settled

**A hundred kelvin below the block's own critical temperature, and full at it.** The *probably* in
"the threshold temperature should probably dictate much of what that means" is gone: the threshold
dictates the brightness entirely, as a fixed band rather than a share of it. The colour is the half
that stayed physical. See [Natural feedback](#natural-feedback--built).

---

## Change log

| Date | Change |
| --- | --- |
| 2026-08-23 | **The significance window is reachable and reaching it that way costs *there must be a lever*.** The retune was built and run against the suite: at conductivity ×4 the coolant sink, the bolt joint's disadvantage, the buried reactor and the atmosphere all stop mattering the way this page says they must. So the conflict between `G8` and the shipped clock is now a conflict between `G8` and `G3`, and it is recorded here as one rather than resolved on the developer's behalf. |
| 2026-08-23 | **`G6` moved from *holds* to *fails in air*, and the significance window's price with it.** The criterion had only ever been scored in vacuum, where corpus p99 demand is 6.02 against the 64 the caps grant; at 200 m/s in thick air the panel's p99 is 73.4, fourteen of forty-nine hulls are over the cap, and at 300 m/s the median ship is. The atmospheric figure this page carried, 36.7 at p95, was taken at `Frequency` 8 and is exactly half. Read where the budget is actually spent, all four cells that satisfy `G8` cost *less* than what ships, so the retune resolves the goal conflict rather than trading against it. |
| 2026-08-23 | The significance window is no longer a goal the code cannot reach. Measured by the paired sweep: conductivity ×4 with `HeatTimeScale` 80–120 lands the median crossing at 124–186 s and still returns the hull inside the hour, for 1.36–2.04× the shipped substep demand. The disagreement stays on the page because the mod does not ship it — what changed is that it is now a price rather than an impossibility. |
| 2026-08-23 | Resolved the `G5` conflict by measuring the event it is about. The crossing is where the damage rate leaves *zero*; the first block is lost at a median **37.0 s** under full electrical load, a median **24.2 s** after the crossing, so with the cue's lead the median pilot has 40 s. `G5` and its rationale both hold. |
| 2026-08-23 | The glow is drawn rather than written to an emissive material, after a session found no glowing blocks. Measured: 418 of 1,992 base cube models hold an emissive material and four of 479 armour models do, and where it exists it is a status lamp. Armour now glows, which changes what the audio cue is for. |
| 2026-08-23 | Settled where the glow's brightness comes from, superseding the three entries below it. It is the **last hundred kelvin before a block's own critical temperature**: nothing below that, full at it and above, a straight line between. A fixed band rather than a share, so the same distance from failure looks the same on every block, and narrow enough that a glow means a block is about to go rather than that it is warm. The colour is unchanged and stays absolute, so brightness says how close to failing and colour says how hot. |
| 2026-08-23 | Reverted the glow to incandescence: both brightness and colour are functions of temperature alone, and the rating-keyed ramp of the entry below is withdrawn. The reason is that it says what the game already says — the engine puts damage effects on a damaged block, and a player works out how close a block is to its limit from playing — while costing the property the channel exists for, that the same temperature looks the same on every block. The 26 % measurement stands as the stated limit rather than as an argument: a quarter of block types fail before they glow, which is what the audio cue is for. |
| 2026-08-23 | The glow is keyed to each block's own rating after all, and the entry below it is superseded. Nothing glows at comfortable temperatures; brightness is the share of the way to critical, cubed, and is full at critical and above. The measurement that argued for the absolute form is unchanged and is now the argument *for* this one: 26 % of block types are rated below the Draper point, so a physical glow would leave a quarter of the game going from dark to destroyed with nothing in between. The colour stays absolute and stays physical, so brightness reads as danger and colour reads as temperature. |
| 2026-08-23 | Natural feedback is built ([backlog.md](backlog.md) `B25`), and building it withdrew a statement on this page. The visual ramp is **not** keyed to each block's critical temperature: it is incandescence, absolute, from the Draper point at 798 K up the Planckian locus, because block ratings run from 500 K to 1,522 K and a relative ramp shows the same colour at 400 K and at 1,200 K. The consequence is stated rather than hidden — 26 % of block types fail before they glow — and it is the reason the audio cue is keyed to the rating instead. The three-second lead now reads a block's equilibrium off its own rate, so a block levelling off short of its rating is never warned about; undecided items 2 and 6 move with it. |
| 2026-08-22 | A coolant loop costs power and makes heat doing it ([backlog.md](backlog.md) `C13`). The rating is derived from the loop the model already describes — 200 kg/s against two bar of head — and the asymmetry with the heat pump is now a stated design: a circulator costs well under a per cent of what it moves against a heat pump's third. |
| 2026-08-22 | The compatibility floor is measured. All 705 prefabs the game ships, idle, in the environment each category spawns into: 461,428 blocks and not one crossing critical. The same 705 flown hard lose 616, which is what makes the first number a measurement rather than a formality ([backlog.md](backlog.md) `C10`, criterion `G7`). |
| 2026-08-22 | Said that *consequential* includes the player. Heat that only damages blocks stops at the airlock, and a burning compartment somebody can stand in is the mod contradicting its own purpose ([backlog.md](backlog.md) `B10`, closed). |
| 2026-08-22 | Stated that the comment standard here is `R14`, and that the check on it catches the failure the length limit prevents. Twenty-four comments in the tree described a member that was no longer there. |
| 2026-08-22 | Recorded what a code comment is for: it names a definition or an especially complex chunk of code, stands on its own without context from another file, and stays inside two lines — anything longer is a documentation entry in the wrong file. |
| 2026-08-22 | Recorded what the README is for: a workshop-pasteable front page for players, with one section for modders building on the framework, and deliberately not an explanation of how the mod works inside. Recorded the conflict this creates with the three developer-facing sections the page carries today. |
| 2026-08-22 | Renamed from `document_of_intent.md` to match the kebab-case every other page uses. **Answered all eight areas that had no stated intent**, which are now stated in the body: cooling is designed in but a vanilla ship still has to survive; the scale target is 10⁶ blocks at `SimulationSpeed` 1.0 with ~250,000 as the realistic figure and uncapped stress bounds; a coolant loop costs power and makes waste heat, because a pump is a motor; an unattended grid gets the same simulation as an attended one, with the saving available as a switch; the mod is balanced for vanilla play in the knowledge that worlds get modded; the warning is a cockpit-only lead cue with a linear visual ramp keyed to each block's threshold; every thermal property is a dial; and a client is owed the minimum traffic that keeps the world coherent, with drift tolerated. Added the principle those answers share — **fidelity is the default and a saving is a switch** — which is what P14 leaves undecided once a difference *is* perceptible. The voids section is replaced by the six smaller questions the answers left open. |
| 2026-08-22 | Recorded four intents stated by the developer after this page was first written. **A thermal camera is wanted** — it had been recorded here and in [known-issues.md](known-issues.md) as a deliberate limit on the grounds that mods get no shader, which is a statement about difficulty rather than about intent. Added [How a player perceives heat](#how-a-player-perceives-heat), covering the three layers of it: instruments that belong to the game's HUD rather than sit on top of it, natural feedback through subtle audio and in-world visuals, and thermal vision as an open problem with no known route. Added the threading target to [Performance intent](#performance-intent): use as much of the CPU as possible while taking as little of the game thread as possible. Void 6 is rewritten — the form of a warning is now stated, and its timing is what remains undefined. |
| 2026-08-22 | Created, by gathering the statements of intent scattered across the README's design goals, `rules.md`'s judgement rules, the balance criteria, the deliberate limits, the profile ladder and the developer's standing instructions, and reconciling each against what the code does. Seven conflicts are resolved against the implementation; eight areas are recorded as having no stated intent at all. Two figures were corrected on the way: the median time to critical at idle is 104.5 s rather than the 112 s the register's prose carried, and the damage-timing finding it belonged to — dropped in an earlier merge — is restored to [balance.md](balance.md#how-fast-a-ship-crosses-critical). |
