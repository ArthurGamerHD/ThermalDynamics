# Stiffness is the cost

What the 199-grid field dump of 18 August says about where the time goes, and why the next
optimisation is not a faster loop.

Companion to [load-and-hitching.md](load-and-hitching.md), which made the spikes proportional to
what changed, and to [scale-design.md](scale-design.md), which designed the machinery this page
argues is now needed. This page is about the **steady** cost, which is the only thing left.

---

## The dump

`ThermalDynamics_Thermodynamics/Thermodynamics_Telemetry_20260818_222511.log`, a 199-grid save
with three 42,051-block capital ships in it, running in vacuum with every mechanism switched on.

| | |
| --- | --- |
| grids | 199, of which 3 are 42k blocks and 196 average 220 |
| mean frame, all grids | **18.4 ms** |
| frames over a 60 fps budget | **41 %** |
| worst frame | 275 ms |
| solver share of a grid's update | **92 %** |
| the three capital ships' share of all simulation cost | **81 %** |
| their simulation rate | **35.6 %** — 72.7 s of simulated time not advanced |
| their substeps per step | 11.00, sd **0.00**, 0 clamped |

Everything the previous round of work fixed stayed fixed: topology rebuild is 338 ms across the
whole session, room mapping 289 ms, exposure 97 ms. Against 67.7 s of solver. **There is nothing
left to fix that is not arithmetic**, and the arithmetic is being throttled: a substep count with
zero variance is `MaxElementVisitsPerStep` holding it, and a 35 % simulation rate is the price.

So the question is not "why is the solver slow". It is **why does this ship need thirty substeps
for a quarter-second step**.

---

## Why none of the settings that existed could help

A step of `dt` simulated seconds needs `dt · r_max / safety` substeps, where `r_max` is the
largest `ΣG / C` on the grid. A substep costs one pass over the nodes and links. So

```
work per real second  ∝  (simulated seconds per real second) × r_max
```

Both settings that look like they control cost cancel out of that:

* **`Frequency`** halves the step and doubles the number of steps, so it cancels out of the
  expression above entirely. It only bites at the two ends: a grid soft enough to need less than
  one substep is charged a whole one anyway, so there `Frequency` is the cost outright — and a
  grid pinned at `MaxSubsteps` is being refused, so raising it buys accuracy. Neither end is where
  these ships are.
* **`HeatTimeScale`** divides every block's heat capacity — `ThermalMass = specificHeat · mass /
  heatTimeScale` — so halving it halves `r_max` and halves the heat advanced per real second in
  exactly the same proportion. No change.
* **`MaxElementVisitsPerStep`** shortens the step when it will not fit, which is what these ships are
  living on. It buys smoothness, not throughput: the same work per unit of heat time, delivered
  more evenly and therefore more slowly.

That is not a defect in the settings. It is what an explicit integrator is: **you pay for the
stiffest node, on every node, for as long as you want heat to move.** The only ways down are to
lower `r_max`, to stop applying it to everyone, or to stop being explicit. The rest of this page
is those three, and the first of them is now `MaxSubstepsPerBlock`.

---

## The stiffness is a thin tail, and that is the good news

Measured two ways, and they agree.

### The harness could not reproduce it, and that is a finding in itself

`ΣG / C` per node on the standard ship shape the benchmarks use — heavy armour with one grating
fitting in eight:

| percentile | rate 1/s | τ | substeps a 0.25 s step needs |
| --- | ---: | ---: | ---: |
| min | 0.045 | 22 s | — |
| median | 0.18 | 5.5 s | 0.09 |
| p90 | 3.0 | 0.33 s | 1.5 |
| max | 4.5 | 0.22 s | **2.25** |

Two populations, exactly as the design predicted — 87.5 % of the grid at τ ≈ 5.5 s and the
fittings at τ ≈ 0.22 s — and **the whole ship asks for two substeps**. The field ship asks for
thirty-one. The benchmark ship is not stiff enough to have the problem, and every scale figure in
[load-and-hitching.md](load-and-hitching.md) was measured on it.

The reason is one number. The harness's lightest block is a 200 kg grating; the block the field
dump found setting the substep count is a **16 kg** light fitting. Twelve times the mass is twelve
times the heat capacity is a twelfth of the stiffness, and the synthetic ship has nothing on it
that a real ship has.

### The field ship, from its own block table

The same calculation over the block types the dump reports on `UNSC Infinity`, at large-grid
geometry:

| blocks | heat capacity | τ | substeps a 0.25 s step needs |
| ---: | ---: | ---: | ---: |
| 43 × `SmallLight` | 32 J/K | 17.8 ms | **28** |
| 1 × `LargeCameraBlock` | 81 J/K | 45 ms | 15 |
| 138 cumulative | ≥ 120 J/K | 67 ms | 7.5 |
| 225 cumulative | ≥ 232 J/K | 129 ms | 4 |
| 1,576 cumulative | ≥ 400 J/K | 222 ms | 2 |

The dump's own numbers say the ship asks for about 31 substeps for a full step — 11 substeps at
35.6 % of the step. The table says 28 from the lights alone, and the linearised radiation term
accounts for the rest. **The measurement and the arithmetic agree on the culprit.**

> The surface CSV is truncated at 300,000 rows, so this table is built from the subtypes it
> reached. It is a lower bound on the tail: there may be lighter blocks it did not list. It does
> not need to be complete to make the point, because the top of it already explains the whole
> substep count.

So: **forty-three sixteen-kilogram light fittings set the simulation rate of a forty-two-thousand
block capital ship.** Removing the top 225 nodes of 42,051 — half a percent — would take it from
31 substeps to 4.

### Reproducing it synthetically

`bench floor` builds its hull from the measured block [`Census`](../tests/Thermodynamics.Harness/Census.cs)
— the eight-band population of a real ship, read out of a dump's block-type table — and it lands
on **22.97 substeps** in vacuum and 43.9 in flight, against a field range of 21 to 31. Close
enough that the synthetic ship can now be used to answer questions about the real one, which it
could not before. `CensusFidelityTests` holds it there.

> The first version of this reproduced the field by adding a single 16 kg fitting at one in a
> thousand. That got the substep count right and the *distribution* wrong — every cap touched the
> same forty-three blocks, where a real ship has a continuum of armour tips and panels between the
> fittings and the hull. The census replaced it, and the cap tables below moved a long way as a
> result: cap 2 reaches 21 % of a census hull against 2 % of the fitting hull.

---

## The field says it is not a trade at all

A clean single-ship dump of 19 August, `MaxSubstepsPerBlock` off, `MaxSubsteps 16`, a 1,293-block
ship with thrusters running:

```
substeps required   18.75 / 21.34 / 21.35
solver substeps     16.00 / 16.00 / 16.00 (sd 0.00)
steps clamped       1,867  of 1,867
set by              LargeBlockLight_1corner [X:9, Y:-3, Z:-2] (48 J/K), 100 % conduction
```

**Every step of that session was clamped.** The ship asks for 21.35 substeps, `MaxSubsteps` grants
16, and the remainder is absorbed by `ClampConductionOvershoot` — which keeps the answer bounded
but is, by the setting's own documentation, "approximate rather than wrong". The grid was at
100 % simulation rate, so nothing else was binding: this is the shipped default, on an ordinary
ship, approximating on every step and saying so only in a counter nobody reads.

Against that baseline the cap is not a trade:

| | substeps | clamped | cost per simulated second |
| --- | ---: | ---: | ---: |
| as shipped | 16 | **every step** | 100 % |
| `MaxSubstepsPerBlock 16` | 16 | **never** | 100 % |
| `MaxSubstepsPerBlock 8` | 8 | never | **38 %** |
| `MaxSubstepsPerBlock 4` | 4 | never | **19 %** |

Cap 16 costs nothing and removes the clamping, because it makes the demand fit the ceiling instead
of leaning on the clamp to survive being refused. Cap 8 removes the clamping *and* runs at 2.6
times the rate. There is no column in which the uncapped configuration wins.

That gives the pair a useful invariant worth stating plainly:

> **While `MaxSubstepsPerBlock <= MaxSubsteps`, the overshoot clamps never engage.** Every step is
> short enough for the grid it is integrating, which is what the clamps exist to fake.

That invariant is now load-bearing rather than an observation. The conduction clamp is a per-link
branch and two extra reads in the hottest loop in the mod, and it used to run whether or not it
could do anything: 4.17 ms of an 8.52 ms step on a 32,800-block hull. The solver now settles the
same stability test once per step — `h * G > C` for a node, `h * conductance >` the reduced mass
for a link, neither of which reads a temperature — and skips the clamped arithmetic on any step
where the answer is no. A resolved grid halves its step; a refused one pays one comparison to find
out. See [benchmarks.md](benchmarks.md#the-overshoot-clamp-ab).

Sixteen blocks of 1,381 are raised at cap 8. Two, at cap 16.

---

## What to do about it

Four routes, cheapest first. The first is built and measured; the other three are re-ranked
against what it turned out to be worth, which is most of what was on offer. That is the thin tail
paying off: when half a percent of the blocks are the problem, the cheapest thing that reaches
exactly those blocks collects nearly the whole prize, and the general machinery has to justify
itself against a much smaller remainder than it expected to.

### 1. A per-block substep cap — built, measured, off by default

`MaxSubstepsPerBlock` is the setting. Zero, the default, leaves every block's heat capacity alone
and the solver is bit-identical to what it was. Set to N, the mirrored heat capacity of any node
whose conduction would demand more than N substeps is raised to the least that keeps it inside:

```
C_min(node) = ΣG(node) · StepSeconds / (safety · N)
```

What that says physically is that **a node whose time constant is far below the step is not an
independent node**. A 32 J/K light bolted to a 500 kg armour block reaches its temperature in
eighteen milliseconds; at a quarter-second step it is a reading off the block it is bolted to, and
resolving its approach to that reading is arithmetic nobody can observe.

`ΣG` is everything the stability estimate reads: the conduction graph, the coolant loops and room
air a block is bolted to, and the linearised radiation and convection it exchanges with the sky.
It has to be the same rate, or the cap would bound a number nobody is looking at — a block on a
windy planet can be stiffer through convection than through six faces of armour, and a
conduction-only floor would leave it setting the substep count.

Only the solver's mirrored row moves. `ThermalNode.ThermalMass` keeps the block's real capacity,
so the terminal readout, the energy figure and the mod API all still describe the block rather
than the approximation used to integrate it.

**What the cap cannot reach** is room air and the coolant loops themselves, which are elements in
the estimate but are not blocks. On the field ship the stiffest room air asks for about two
substeps against the lights' twenty-eight, so it does not bind — but a ship with one tiny
pressurised cupboard could be different, and the telemetry now reports both so it stops being a
guess.

**What it buys and what it costs**, from `bench floor --size 42000` — a 43,232-node ship with the
field's proportion of light fittings, temperatures spread 250–750 K, error measured against the
uncapped run.

**Read the step length first.** A step of `dt` needs `dt · r_max / safety` substeps, so the demand
— and with it which blocks a given cap reaches and what that cap is worth — is *proportional to
the step length*. There is no single table here; there is one per step length, and the two that
matter are the two the shipped profiles run at.

`simulation` and `responsive` run **Frequency 8**, an eighth-second step:

| cap | substeps | ms | speed | blocks raised of 43,232 | worst error | rms error |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| off | 11.48 | 696 | 1.0x | 0 | — | — |
| 32 | 11.48 | 689 | 1.0x | 0 | 0 | 0 |
| 16 | 11.48 | 680 | 1.0x | 0 | 0 | 0 |
| 8 | 8.00 | 508 | 1.4x | 172 (0.4 %) | 0.056 K | 0.016 K |
| **6** | 6.00 | 436 | **1.6x** | 459 (1.1 %) | 0.113 K | 0.033 K |
| 4 | 4.00 | 327 | **2.1x** | 461 (1.1 %) | 0.225 K | 0.067 K |
| 3 | 3.00 | 285 | 2.4x | 1,450 (3.4 %) | 0.345 K | 0.095 K |
| 2 | 2.00 | 235 | 3.0x | 3,648 (8.4 %) | 0.606 K | 0.152 K |
| 1 | 1.00 | 191 | 3.6x | 9,283 (21.5 %) | 1.887 K | 0.449 K |

`optimized`, `arcade` and `simlite` run **Frequency 4**, a quarter-second step, and ask twice as
much of the integrator:

| cap | substeps | ms | speed | blocks raised of 43,232 | worst error | rms error |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| off | 22.97 | 627 | 1.0x | 0 | — | — |
| 32 | 22.97 | 613 | 1.0x | 0 | 0 | 0 |
| 16 | 16.00 | 452 | 1.4x | 172 (0.4 %) | 0.052 K | 0.015 K |
| 8 | 8.00 | 269 | **2.3x** | 461 (1.1 %) | 0.221 K | 0.066 K |
| **6** | 6.00 | 223 | **2.8x** | 1,450 (3.4 %) | 0.341 K | 0.094 K |
| 4 | 4.00 | 180 | 3.5x | 3,648 (8.4 %) | 0.601 K | 0.150 K |
| 3 | 3.00 | 162 | 3.9x | 5,219 (12.1 %) | 0.887 K | 0.218 K |
| 2 | 2.00 | 139 | 4.5x | 9,283 (21.5 %) | 1.898 K | 0.450 K |
| 1 | 1.00 | 108 | 5.8x | 14,138 (32.7 %) | 5.915 K | 1.518 K |

> **This page used to carry the second table alone, under no step length, and read it as if it
> described the default.** It does not: `Frequency` had moved to 8 and the table had not, so every
> speed-up quoted here was about twice what a `simulation` or `responsive` world would see, and
> the cap of 6 that `optimized` ships was being credited with 3.4x rather than its own 2.8x. The
> floored-block counts are what identified it — they reproduce *exactly*, row for row, one cap
> apart — and `bench floor` now takes `--frequency` and prints the step length above the table so
> the mistake cannot be made silently again.

The two tables are the same measurement at two step lengths, and they line up: a cap of N at
Frequency 8 raises the same blocks as a cap of 2N at Frequency 4, because both leave the same
`C_min = ΣG · StepSeconds / (safety · N)`. **A cap is a statement about a node's time constant
against the step, not a number of substeps**, which is why it cannot be quoted without one.

The substep column lands on the cap exactly, which is the point of the floor being computed from
the same rate the estimate reads. A cap of one means one substep, not "about one".

Three things to read out of that table.

**Down to a cap of four, it touches forty-three blocks in forty-three thousand** and buys five and
a half times the throughput. Below four it starts reaching into the grating and then the armour,
and the error grows with the population rather than with the approximation.

**The error is confined to the blocks it moves.** The worst error anywhere and the worst error on a
floored block are the same number from cap 4 down; at 16 and 8 they differ by about a fiftieth of
a kelvin, which is the floored block's neighbour feeling it warm differently for a moment.

**The error decays.** Over five simulated seconds a cap of 4 is about a kelvin out; over fifty it
is a fifth of one. Heat capacity decides how fast a node gets somewhere, not where it ends up, so the
difference is a transient rather than a bias — `SubstepFloorTests` asserts exactly that, along
with the cap actually bounding the estimate, the untouched nodes staying untouched, and the block
keeping its real capacity for every readout.

**What it means for the field ship.** 31 substeps down to 4 is not only a fifth of the arithmetic:
it is below the 11 substeps `MaxElementVisitsPerStep` was granting, so the budget stops binding and
the grid stops being throttled. The ship runs at **full simulation rate at about a third of what
it now spends running at 35 %** — which is between a threefold and a fifteenfold improvement in
heat moved per millisecond, depending on which of the two you were unhappy about.

### Driven, and the caveat the harness only found at scale

The sweep above diffuses a seeded spread. A ship held at temperature by its own power — which is
what a field ship is — is the case that decides whether a player ever sees it, and it is what
`bench floor --driven` measures: the census share of heat producers, at the census wattage, run
until the hull settles. On a 43,232-block hull over 500 simulated seconds, **at `Frequency 4`** —
the same quarter-second step as the second table above, and not the shipped default:

| cap | speed | blocks raised of 43,232 | peak K | peak error | worst error |
| ---: | ---: | ---: | ---: | ---: | ---: |
| off | 1.0x | 0 | 2,345.5 | — | — |
| 16 | 1.3x | 172 | 2,345.6 | +0.10 K | 0.12 K |
| 8 | 2.4x | 461 | 2,345.6 | +0.06 K | 0.15 K |
| 6 | 3.2x | 1,450 | 2,345.5 | −0.02 K | 0.28 K |
| 4 | 4.7x | 3,648 | 2,344.6 | **−0.93 K** | 1.22 K |
| 3 | 5.6x | 5,219 | 2,343.3 | **−2.20 K** | 2.43 K |
| 2 | 8.0x | 9,283 | 2,337.2 | **−8.33 K** | 9.08 K |
| 1 | 12.3x | 14,138 | 2,310.1 | **−35.4 K** | 38.8 K |

> Re-run after the census landed and reproduced every temperature to three decimal places, which
> is the benchmark saying it is deterministic: the hull, the sources and the seeded state are all
> derived rather than sampled, so two runs differ only by the clock.

At the shipped `Frequency 8`, over the same 500 simulated seconds:

| cap | speed | blocks raised of 43,232 | peak K | peak error | worst error |
| ---: | ---: | ---: | ---: | ---: | ---: |
| off | 1.0x | 0 | 2,345.5 | — | — |
| 32 | 1.0x | 0 | 2,345.5 | 0.000 K | 0 |
| 16 | 1.0x | 0 | 2,345.5 | 0.000 K | 0 |
| 8 | 1.3x | 172 | 2,345.6 | +0.124 K | 0.138 K |
| 6 | 1.6x | 459 | 2,345.6 | +0.137 K | 0.167 K |
| **4** | **2.0x** | 461 | 2,345.6 | +0.085 K | 0.167 K |
| 3 | 2.5x | 1,450 | 2,345.5 | +0.006 K | 0.261 K |
| 2 | 3.1x | 3,648 | 2,344.6 | −0.909 K | 1.200 K |
| 1 | 4.1x | 9,283 | 2,337.2 | −8.305 K | 9.068 K |

**Both tables reach 2,345.5 K uncapped**, which is the two runs agreeing on where the hull settles
and therefore agreeing that they are the same experiment at two step lengths. The peak errors then
line up one cap apart exactly as the diffusing sweep does: Frequency 8 at cap 2 reads −0.909 K
against Frequency 4 at cap 4's −0.93 K, and cap 1 reads −8.305 K against cap 2's −8.33 K.

**So the safe cap at the shipped rate is lower than this page used to say.** The Frequency 4 table
puts the first visible movement at cap 4; at Frequency 8 the peak is unmoved down to **cap 3**, and
cap 4 costs +0.085 K for twice the throughput. The advice "above about 6" was correct for the rate
it was measured at and is conservative by a factor of two for the rate that ships.

> **This table could not be produced at all until the overheat list was fixed.** `bench floor
> --driven --ticks 4000` was OOM-killed at 14 GB, twice: a burning block filed an overheat event
> every *substep*, and the harness's batched path keeps every step's events for a whole run, so
> this sweep held on the order of 440 million records. The solver now accumulates a block's damage
> and files one event a step. Same run, same 12 GB cap, completes. See
> [benchmarks.md](benchmarks.md#a-burning-block-filed-one-overheat-event-per-substep).

**Down to a cap of 6 at `Frequency 4`, and a cap of 3 at `Frequency 8`, the peak is unmoved.**
Below that it is not, and the sign says why: the capped hull is *cooler*, which is what a hull that
has not finished climbing looks like. The cap adds heat capacity, and a hull with more capacity
takes longer to reach the same place — at cap 1 it is 35 K short after five hundred simulated
seconds at the quarter-second step, and 8 K short at the eighth-second one.

That matters more than the number suggests, because **overheat damage is a threshold crossing**.
A hull that will eventually burn but takes twice as long to get there is a different game from one
that burns on schedule. An earlier and smaller measurement — 8,904 blocks settling at 764 K —
showed the peak identical at every cap, and concluded too readily that the equilibrium argument
was safe at any setting. It is safe in the limit; the limit is further away than a test that runs
for five hundred seconds can see.

The honest statement is therefore narrower than the one this page used to make: **the cap does not
change where a hull settles, and above about 6 it does not visibly change when it gets there
either.** Below 6 it delays the approach, in proportion to how much mass it added.

### At a million blocks it helps and is not enough

`bench floor --size 1000000 --ticks 10`, 1,000,294 blocks and 2.1 M links. **Measured on the
single-fitting hull that preceded the census**, so the block counts in the fourth column are far
lower than a census hull would give; the timings and the conclusion are unaffected, because both
are set by the substep count rather than by how many blocks were raised to reach it:

| cap | substeps | ms / step | speedup | blocks raised | peak K |
| ---: | ---: | ---: | ---: | ---: | ---: |
| off | 28.13 | 1,089 | 1.0x | 0 | 719.5 |
| 16 | 16 | 651 | 1.7x | 999 | 719.5 |
| 8 | 8 | 370 | 2.9x | 1,000 | 719.5 |
| 6 | 6 | 279 | 3.9x | 1,000 | 719.5 |
| 4 | 4 | 219 | 5.0x | 1,000 | 719.5 |
| 3 | 3 | 191 | **5.7x** | 1,000 | 719.5 |
| 2 | 2 | 135 | 8.1x | 7,049 | 719.6 |
| 1 | 1 | 105 | **10.4x** | 126,036 | 719.6 |

The same shape as everywhere else — the thousand fittings are the whole tail, and the cliff is
still between 3 and 2 — but **the speedup is smaller than at 42,000 blocks**: 10.4x here against
14.9x there. Once the substeps are gone what is left is the fixed per-step work, and at a million
nodes syncing state, publishing results and walking the stability estimate are no longer a
rounding error. The cap removes the multiplier; it cannot remove the pass.

Put through the measured 7x runtime penalty and spread across the fifteen frames of a step's
window, that is what a single million-block grid would cost a player:

| | per step, harness | per step, in game | per frame, in game |
| --- | ---: | ---: | ---: |
| uncapped | 1,089 ms | ~7.6 s | ~508 ms |
| cap 3 | 191 ms | ~1.3 s | ~89 ms |
| cap 1 | 105 ms | ~0.7 s | ~49 ms |

**A million-block grid is not playable at any cap.** Even at 1, one grid alone is three 60 fps
frames deep. This is exactly what [scale-design.md](scale-design.md) is for: the cap attacks the
substep multiplier and nothing else, and at a million blocks the pass itself is the problem —
which needs activity tracking and chunking, so that a step stops touching every node.

Where the cap does land the ship is at capital-ship scale. The same arithmetic on the
43,232-block hull: 22.4 ms per step uncapped becomes 2.9 ms at cap 3, or about **10.5 ms of a
frame in game becoming 1.4 ms**.

### The value is chosen by population, not by error

The error barely moves between 16 and 2 — 0.035 K to 0.061 K on the peak — so it cannot be what
picks the setting. What moves, and moves suddenly, is **how many blocks the cap reaches**:

| cap | harness ship, 8,904 blocks | field ship, 1,381 blocks | 199-grid world, 174,873 blocks |
| ---: | ---: | ---: | ---: |
| 16 | 8 | 2 (0.1 %) | 480 (0.3 %) |
| 8 | 8 | 16 (1.2 %) | 1,017 (0.6 %) |
| 4 | 8 | 83 (6.0 %) | 6,057 (3.5 %) |
| 3 | **8** | between | between |
| 2 | **201** | 327 (23.7 %) | 49,638 (28.4 %) |
| 1 | 1,121 | 540 (39.1 %) | 72,492 (41.5 %) |

The distribution is bimodal, and that is the whole shape of the decision. There is a thin tail of
genuinely tiny blocks — lights, cameras, panel tips — and then a cliff where **ordinary armour**
begins. Above the cliff the cap is a few fittings and free; below it, the cap is re-massing the
hull a player watches heat move through.

On the harness ship the cliff sits between 3 and 2: cap 3 reaches the same eight blocks that cap
16 does, and costs 7.8x less than uncapped. On a real ship it sits somewhere inside the 2–4 band,
which is why the projection now evaluates 3 and 6 as well — the previous list jumped straight over
the only interesting part of the range.

**Risks worth watching in game, which the harness cannot see.** Overheat damage is a per-node
threshold crossing, and a fattened node crosses later — on a 16 kg fitting bolted to armour that
is the same argument as before, but it is an argument rather than a measurement. And a cap set
aggressively enough to reach ordinary blocks changes how a hull warms, which is the thing the mod
exists to show. Four is the value the measurement supports; one is an arcade setting.

### 2. Lumping — the same idea, done properly

Merge a node into its dominant neighbour at topology time when the pair's coupling time constant is
far below the step. The lump carries the sum of the two heat capacities, the union of their exposed
faces, and the outside links of both; the absorbed block reads the lump's temperature.

Better than the cap in two ways: it is exact rather than approximate — the lump's capacity is the
sum of what it absorbed, so no energy is invented — and it removes the node *and* its links from
every pass rather than only softening its stability demand. Against the cap's five and a half
times, the extra is whatever the node and link count buys: on the field ship the lights are forty
three of forty-two thousand, so it is nothing at all.

That is the honest verdict for SE1: **lumping is the right shape and the cap already collects the
whole prize.** It becomes worth building when the population that needs collapsing is large — a
grating in eight rather than a light in a thousand, or SE2's small blocks — because then the node
count matters as much as the stiffness.

This is [scale-design §3.3](scale-design.md#33-lumping--the-memory-and-precision-win) and
[model-redesign §5(c)](model-redesign.md), which recommended it and could not say how much it was
worth. Now we can: on this fleet, a few per cent over a setting that already exists.

**What it needs designing:** which pairs qualify (mass ratio, coupling constant, both not heat
sources, both not independently exposed to very different environments); how a lumped block reports
its temperature to the terminal and the overlay; how overheat damage is attributed; how a lump
survives save/load and a block being ground off the middle of it. The last is the real work —
unlumping on change has to be as incremental as removal now is.

### 3. Multirate stepping — when the tail is not thin

Give each node, or each chunk, a substep tier and integrate the stiff subset more often than the
bulk. [scale-design §4](scale-design.md#4-multirate-stepping-unifies-two-problems) has the design
and the two rules that keep it conservative.

On *this* fleet it is worth less than the cap and costs far more. The stiff set is forty-three
blocks in forty-three thousand, so multirate's bill is `0.001 × 28 + 0.999 × 1 ≈ 1.03`
substep-equivalents against 28 — arithmetically the best answer on the page — but every one of
those savings is already collected by a floor that is forty lines and cannot desynchronise
anything. Multirate earns its complexity when the stiff set is large enough that flooring it would
change the simulation people came for, which is what SE2's mixed block sizes will produce and this
fleet does not.

### 4. Implicit integration — the endgame

Backward Euler on the sparse conductance matrix, solved with preconditioned conjugate gradient over
CSR adjacency. Unconditionally stable: one step of the whole 0.25 s, no substeps, no
`MaxSubsteps`, no `ClampConductionOvershoot`, no `MaxElementVisitsPerStep` shortening the step, and
`r_max` stops being in the cost model at all. Cost becomes iterations × one pass, and iterations
depend on how well-conditioned and well-preconditioned the system is rather than on the worst
block on the ship.

Against the field ship's 31 substeps, a CG solve that converges in twenty iterations is a third of
the work *and* runs at full rate instead of 35 %. That is roughly what `MaxSubstepsPerBlock 4`
already delivers, for a fraction of the risk — so the case for implicit is no longer this fleet.
It is the grid where the stiffness is *not* a thin tail: a hull of genuinely mixed block sizes,
where there is no small set to floor and the cap would have to fatten half the ship.

It is also the only route that fixes SE2's 400× stiffness ratio without a per-grid judgement call.

**What it costs:** the solver stops being a loop anyone can read. Energy conservation becomes a
property of the formulation plus the residual tolerance rather than of a visibly equal-and-opposite
exchange, and the non-linear terms — radiation's `T⁴`, the overshoot clamps, the coolant loops and
room air — have to be either linearised into the matrix or split out and handled explicitly, which
brings back a stability limit for whatever is left outside. It is a rewrite of
`ThermalSolver`, and it should not be attempted before 1 and 2 have said how much is left to win.

---

## An orthogonal lever: nobody is on two of those ships

Three identical capital ships cost 81 % of the world's thermal budget and at most one of them has a
player on it. Nothing in the mod knows that.

A per-grid rate tier driven by observation — distance to the nearest player, whether anything is
reading the grid's temperatures, whether it is powered — would take two thirds of this world's cost
away without touching the solver. The model already permits it: steps are energy-conserving and
order-independent, so a grid can advance in coarse steps or catch up in one long one when a player
approaches, exactly as it does now after a load.

What it needs is a decision about what a player is entitled to find when they arrive. A ship that
has been idling at one step a minute and then catches up in one 60-second step reaches the right
steady state and a different transient. For a derelict that is right; for a ship the player left
with a reactor overheating, it is the difference between finding it destroyed and finding it fine.
The honest version couples the tier to whether anything on the grid is *doing* anything, not only
to whether anyone is watching.

---

## A number in the report that does not add up

The Cost table and the per-grid records disagree, and it matters because the headline "share of
real time" comes from the smaller one.

| | Cost table | sum of the per-grid rows |
| --- | ---: | ---: |
| grid simulation | 73,454 ms | **119,182 ms** |
| solver | 67,681 ms | **109,812 ms** |
| simulation steps | 48,747 | **82,572** |
| node updates | 46,089,881 | **77,392,060** |

The per-grid rows in the log and the rows in `Thermodynamics_Grids_*.csv` are identical to the
last decimal, so those two views agree with each other. The Cost table claims to be a merge of
exactly those records — `WritePerformance` walks `Telemetry.Grids` and merges each
`SimulationTime` — and comes out at 62 % of their sum. The three capital ships alone are 95,894 ms
of it, more than the merged total.

The session counters are off by the same kind of factor (`FramesObserved` 3,989 against 6,672 calls
to every grid's tick), and the ratios are not identical across counters — 1.62 for milliseconds,
1.69 for steps and node updates — which is the signature of two different windows rather than one
dropped set of records.

Two readings fit, and they differ by a lot. Either the merge is dropping work, in which case 119 s
of simulation in the reported 263 s window is **45 % of real time** rather than the 28 % the Cost
table implies; or the per-grid records cover a longer window than the session clock — the grids
report lifetimes of 429 s against a 263 s session — in which case the share is right and the
per-grid rows cannot be added to it. Both cannot be true, and the report does not say which it is.

It is the first thing to fix, because every other number on this page is read against it — and
reading the code did not settle it. `TimingStat.Merge` is arithmetically correct and now has a
test summing two hundred stats to prove it; the Cost table walks the same list the CSV does; and
between the two sections nothing runs that could tick a grid.

So the report now checks itself instead. A **Consistency** section re-takes the Cost table's own
figures after every other section is written and prints both, along with each record's first and
last session frame, and states the two invariants in a form that can fail loudly:

* the record list must not change while the report is built;
* a grid cannot tick more often than the session is framed, because both happen once each in the
  same `Simulate` call.

The most likely cause it is aimed at is `Telemetry.Reset`, which zeroes the session clock and
clears the registry while every live grid still holds the record it was handed — so a record can
outlive the clock its timestamps were taken from, which is exactly how a grid comes to report a
429-second lifetime inside a 263-second session. `Reset` now detaches those references with the
list. Whether that was the cause is what the next dump will say.

---

## How close the synthetic tests are to a real ship

Four differences, three now measured and one closed.

**Runtime: the game is 6–8x slower per element visit.** Measured by dividing solver milliseconds
by substep passes times elements, the same way at both ends — 3.97 ns on the harness at 8,904
blocks against 24.9 ns on a 1,293-block field ship whose entire working set fits in L2. Cache
pressure explains none of that; it is .NET Framework 4.8 against .NET 9. See
[load-and-hitching.md](load-and-hitching.md#measuring-it).

**Stiffness: the harness had none, and this was the important one.** The synthetic block
catalogue's lightest block was a 200 kg grating, so the benchmark ship asked for 2.25 substeps
where a real ship asks for 21 to 31. Every scale figure in this repository was measured on a hull
an order of magnitude softer than the ships it was meant to describe — the scale ladder's full
step at a million blocks was six times cheaper than it should have been. Closed by the census.
See [load-and-hitching.md](load-and-hitching.md#the-ladder-was-measured-on-the-wrong-ship).

**Population: closed, by measuring it.** The harness's blocks were 3,300 kg armour, 200 kg
gratings and one 16 kg fitting — a gap where a real ship has hundreds of armour corners, tips and
panels between 120 and 240 J/K. That gap mattered because it is what decides how many blocks a cap
reaches: cap 2 raised 2.3 % of the old benchmark hull and 23.7 % of a field ship.

Every benchmark hull is now built from [`Census`](../tests/Thermodynamics.Harness/Census.cs), the
eight-band block population of a real 1,381-block ship read out of a dump's block-type table. The
census hull asks for 23 substeps against a field range of 21 to 31, and its cap curve tracks the
field's closely — cap 8 reaches 1.2 % against the field's 1.2 %, cap 2 reaches 24 % against 23.7 %,
cap 1 reaches 38 % against 39.1 %. `CensusFidelityTests` holds it there: it asserts the substep
demand and the cap curve against constants recorded in `Census.Field`, so the harness cannot drift
away from the reports again without a test saying so.

**Shape and the adapter, both already known.** Bounding-box fill, exposed fraction and diameter
all vary by an order of magnitude with hull shape
([scale-design §10](scale-design.md#10-grid-shape-changes-the-arithmetic)), and the harness has no
game blocks at all, so the mass sweep, the power and thrust events and the room pressure API are
absent from every synthetic figure.

## What was measured and found not to matter

**Diagnostics.** Telemetry switches `CollectDiagnostics` on, which adds a per-node clear, five
field writes per exposed node per substep, and two random-access read-modify-writes per link into
managed `ThermalNode` objects — none of it charged to the step's work estimate. It looked like it
might be a large share of what a field dump measures. It is not: `bench scale --diagnostics`
against the same run without it costs **7 %** at 32,800 blocks on the harness. Worth knowing, worth
keeping in mind for a .NET 4.8 runtime with a colder cache, not worth acting on.

The uncharged part is still a real if small defect — `ClearConductionDiagnostics` walks every node
per substep and `SubstepWork` does not count it, so a diagnostics-on grid paces itself slightly
slow.
