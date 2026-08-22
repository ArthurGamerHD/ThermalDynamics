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

> The rules argued here are stated canonically in [rules.md](rules.md): `E3` `E5` `E10` `E11` `D6`,
> and the principles P3 and P14 they follow from.

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
   place to put the reactor — that visibly changes the outcome. That is `G3`, and it is the
   criterion the corpus least clearly passes.

**It is a framework as well as a mod.** Everything the simulation knows is readable and everything
it does is drivable from another mod, through a delegate table passed by mod message. The intent is
that a second mod can make heat mean something without forking this one — see
[api.md](api.md).

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

**Checked by** `C5` (`CoreIsolationTests`). The suite is 1,529 tests, 33 deterministic scenarios and
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
| **G3** | **Cooling works.** Fitting radiators or a loop moves the outcome. | **Answered, and the answer is uncomfortable.** Bolting fits nearly everywhere and makes more ships worse than better; plumbing works and fits on 15% of warm hulls. |
| **G4** | **Design decides, not size.** Outcome follows what a builder controls. | **Holds, strongly.** Peak correlates +0.89 with worst local W/m² under load against +0.49 with block count. |
| **G5** | **No death spiral.** A ship past critical that throttles to idle returns below critical in bounded time. | **Holds as written.** Its stated reason does not — see below. |
| **G6** | **Affordable across the population**, at p95/p99 rather than at the mean. | **Holds.** Corpus p99 substep demand is 6.02 in vacuum against 64 granted; the worst measured atmospheric case is 36.7 at p95. |

**G6 is not a balance criterion and is on the list on purpose.** The population's stiffness tail is
what decides lumping, multirate stepping and the substep cap, and no synthetic ladder can show its
shape.

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

**The stated target is a single grid of 10⁶ blocks that ticks inside a frame budget**, in either
game. Nothing in the design for it is built — see [scale-design.md](scale-design.md), and see
[the voids below](#where-intent-is-absent-or-ill-defined) for whether that target is meant literally.

**A long run is designed for its own death** (P13): capped, resumable, streaming, and counting what
it did not measure. This is intent about the harness rather than the mod, and it exists because an
uncapped corpus sweep has taken a machine down.

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

* **Not a per-block radiative transfer model.** Emissivity doubles as absorptivity; a block cannot
  be shiny to the sun and black to space.
* **Not a build-state simulator.** A block at 10% construction carries its full thermal properties.
  The machinery to change that exists and the difference would be invisible next to the heat a
  block's neighbours carry.
* **Not a thermal camera.** Mods get no shader and no frame buffer, so seeing temperature is the
  terminal readout, the cockpit summary, the crosshair readout and the x-ray overlay.
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
| **"The radiator is a block you plumb"** against the retrofit measurement, where a plumbed ring fits on **15%** of warm hulls and bolting — which fits nearly everywhere — makes more ships worse than better. | **Both are correct and they describe different questions.** The physics is settled: a sink face carries 1,000 W/K against a bolt joint's 167. What is open is whether cooling should be *fittable to a finished hull* or designed in from the start. That is an undeclared intent — see below. |
| **"Light: a grid's cost is one pass over its links per substep"** against the measured per-node cost of the environment pass. | **The measurement wins and the goal is unchanged.** The step budget already counts `links + 4 × nodes`. The README's wording predates the measurement and understates what a substep does. |
| **`MaxSubstepsPerBlock 6` recommended in the field** against the shipped default of `0` (off). | **The shipped default stands, and the recommendation is weaker than it was.** Re-measured at the shipped `Frequency 8`, a cap of 6 buys 1.6× rather than 3.4× — the original table was taken at `Frequency 4` and read as if it were the default. Still a clear win, no longer a dramatic one, and open as [backlog](backlog.md) C3. |
| **The census hull as "a worst case" against "what a ship does".** It makes 12.1 kW a block against a real median of 335 W — the 96th percentile. | **Undecided, and recorded as such in the code.** `TheCensusHullMakesFarMoreHeatThanARealShip` pins the figure and fails if it changes quietly. It reaches every temperature figure and no stiffness figure. |

---

## Where intent is absent or ill-defined

Each of these is a question the code has already answered by default. **An undeclared intent is
still an intent** — it is just one nobody chose, and it is decided by whoever touches the file next.
They are ordered by how much would have to change if the answer went the other way.

### 1. Is cooling something you fit, or something you design in?

**Nothing states this, and it decides what the mod's own blocks are for.** The retrofit lab measured
both fits over 427 real ships: bolting fits on 281 of 332 warm hulls and has a median effect of
**−0.11%**; plumbing has a median of **+1.35%** and fits on **49**. A finished ship does not leave a
ring of empty cells around the thing that gets hot.

Three answers are available and they are genuinely different mods: cooling is designed in and a
finished hull cannot receive it; cooling is retrofittable and the blocks need a form that fits a
built ship; or bolting is made to work, which means confronting that a block against a face is a
face that stopped radiating.

**Until this is answered, `G3` cannot be scored** — it currently reads as passed-and-failed at once.

### 2. Is 10⁶ blocks a real target or a stress bound?

The scale design differs by which it is: wake storms, sleep thresholds and chunk sizes are all tuned
against an intended maximum. Recorded as [backlog](backlog.md) G5 and **worth deciding before
building**, because the machinery is expensive and none of it exists yet.

### 3. Should a coolant loop cost power?

The pump's switch and speed slider now reach the loop, and the loop's cached flow refreshes when
either moves — so the switch works. **`MaxPowerWatts` is never assigned, so a pump draws nothing.**
A loop is therefore free to run once built, where a heat pump pays for every watt it lifts.

That asymmetry may be right — a pump is a circulator, not a refrigerator — but it is unstated, and
it is a balance decision rather than a fix.

### 4. What is a player entitled to find when they come back?

Nothing states what happens to a ship nobody is watching. This is not hypothetical: three identical
capital ships took 81% of one world's thermal budget with at most one player aboard, and a per-grid
rate tier driven by observation would take two thirds of that away.

The unanswered half is what a coarse-stepped grid owes the player who returns. A derelict that
catches up in one long step reaches the right steady state and a different transient, which is
correct for a derelict. **For a ship the player left with a reactor overheating, it is the difference
between finding it destroyed and finding it fine.**

### 5. Which world does the mod balance for?

Every balance figure is measured in **vacuum** — all five corpus scenarios — while air costs about
six times what vacuum costs and is where the substep budget is actually spent. The atmospheric
evidence is 49 hulls against 8,132.

Relatedly, the servers this mod is played on commonly run a 300 m/s speed limit against vanilla's
100, which multiplies the friction term by 27 and the cooling term by 1.37. **Whether the mod is
balanced for vanilla or for the servers it is played on is unstated.**

### 6. What is a "warning" supposed to look like?

`G5` and the purpose statement both rest on a player reacting to a warning, and **nothing defines
one**. Critical temperature is a threshold with damage on the far side; there is no stated
intent for a band below it, how a player is told, or how much time the telling is meant to buy.
The measured 8.9 s median makes this the load-bearing gap behind the first row of the conflicts
table.

### 7. Is a definition a description of a material, or a balance dial?

The repository answers both ways, deliberately, and does not say where the line is. A block's
`Conductivity` and `SpecificHeat` are **real material figures** — type the number a materials table
gives. The waste-heat fractions are explicitly **balance figures and not efficiencies**. A coolant
loop's `Conductivity` is still a 0…1 quality against a reference.

Each individual call is recorded. **What is missing is the rule** that decides the next one, which
matters because it is the question a third-party mod author faces first.

### 8. What does this mod owe a multiplayer client?

Damage and settings are server-authoritative and replicated. Temperatures are not: every client
integrates its own simulation from the same inputs, nothing reconciles them, and a client joining
mid-session starts from saved temperatures. The divergence is described as cosmetic — **which is a
judgement nobody has written down as intent**, and it stops being true the moment a readout is used
to make a decision the server will not agree with.

---

## Change log

| Date | Change |
| --- | --- |
| 2026-08-22 | Created, by gathering the statements of intent scattered across the README's design goals, `rules.md`'s judgement rules, the balance criteria, the deliberate limits, the profile ladder and the developer's standing instructions, and reconciling each against what the code does. Seven conflicts are resolved against the implementation; eight areas are recorded as having no stated intent at all. Two figures were corrected on the way: the median time to critical at idle is 104.5 s rather than the 112 s the register's prose carried, and the damage-timing finding it belonged to — dropped in an earlier merge — is restored to [balance.md](balance.md#damage-arrives-too-fast-to-be-played-around). |
