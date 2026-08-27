# Performance work

The record of the performance passes over this repository: what each iteration measured, what it
proposed, what it changed, and what the change was worth on a stated machine. It is the log of the
*work*; the instrument the work is measured with is [benchmarks.md](benchmarks.md), and the figures
quoted here are taken from that report unless a row says otherwise. This page does not restate what
a step costs or how the report is read — it says what was done about it and how each claim was
checked.

> The rules argued here are stated canonically in [rules.md](rules.md): `M4` `M5` `M6` `M7`
> `D7` `D8` `W5`, and the principles P1, P4 and P6 they follow from.

| Looking for | Go to |
| --- | --- |
| The report itself, and how to read a comparison | [benchmarks.md](benchmarks.md) |
| What a grid costs as it grows, and what makes it stutter | [load-and-hitching.md](load-and-hitching.md) |
| Why a handful of light fittings sets a capital ship's cost | [stiffness.md](stiffness.md) |
| Where the model is going at a million blocks | [scale-design.md](scale-design.md) |
| Open performance rows | [backlog.md](backlog.md), section D |

## How a pass is run

Every iteration takes the same seven steps, and the order is the point: the measurement comes
before the idea, and the oracle before the change.

1. **Measure.** `bench report` at the pass's own starting commit and at its tip, minutes apart, on
   one machine, inside `heavy run` (`M7`, `W5`). Where the report cannot see the thing in question,
   a lab is written first and the report is extended to carry the figure.
2. **Identify.** A candidate is a place the numbers accuse, and the counts are a place to look
   rather than a verdict (`D7`).
3. **Branch.** One branch per iteration, off the working branch's tip, merged back with a merge
   commit once the change has passed every check below.
4. **Validate in a lab.** The candidate is measured on one hull with the change flipped between
   timed blocks, because two hulls are two allocations with two cache colourings.
5. **Implement only if it pays and holds.** A change is kept when its saving is outside the noise
   floor (`M5`) and it breaks neither the three invariants (`C6`) nor a stated intent.
6. **Pin it.** An optimisation is asserted bit-identical to the code it replaced on a fixture that
   proves it exercised something (`D8`), and a defect it found is pinned so it cannot return.
7. **Clean up.** The code and the pages it replaced go; what stays describes the present.

**The machine.** Every figure on this page was taken on the repository's 32-core development
machine, which is shared with three other projects (`W5`). It was not idle on 2026-08-26: two
editor language servers held a core each for the whole day and the swap was full, so absolute
milliseconds on this page are about 2.7× the committed baseline's on the same hardware. The
comparisons here are taken minutes apart in one held window, which is what makes the ratios
readable when the absolutes are not.

## The iterations

| # | Date | Subject | Verdict | Where |
| ---: | --- | --- | --- | --- |
| 1 | 2026-08-26 | The harness measured unoptimised code | **kept** — every configuration now compiles optimised | [Iteration 1](#iteration-1--the-harness-measured-unoptimised-code) |
| 2 | 2026-08-26 | The ladder's `build` column measured the hull generator | *in progress* | [Iteration 2](#iteration-2--the-ladders-build-column-measured-the-hull-generator) |

## Iteration 1 — the harness measured unoptimised code

**What was found.** Nothing in `tests/` set `<Optimize>`, and every command this repository runs —
`dotnet run --project Thermodynamics.Sim`, `dotnet test` — builds the Debug configuration. A Debug
assembly carries `DebuggableAttribute(DisableOptimizations)`, and the JIT honours it: it compiles
minimal-optimisation code with no register allocation across statements, no bounds-check
elimination and no inlining. **Every millisecond this repository had ever published was taken on
code the game never runs.**

**What it was worth, measured.** The same commit, the same machine, the same held window, four
minutes apart; the committed report at `--size 32000 --max 125000`, fastest of three (`M4`).

| figure | unoptimised | optimised | ratio |
| --- | ---: | ---: | ---: |
| ladder 32,000, step | 18.33 ms | 5.21 ms | **0.28** |
| ladder 125,000, step | 58.27 ms | 18.36 ms | 0.32 |
| features, everything on | 17.90 ms | 5.22 ms | 0.29 |
| features, everything off | 0.959 ms | 0.248 ms | **0.26** |
| step shape, fixed per step (fit) | 2.07 ms | 0.84 ms | 0.41 |
| step shape, per substep (fit) | 0.602 ms | 0.163 ms | 0.27 |
| step shape, prologue and estimate | 0.718 ms | 0.169 ms | **0.23** |
| step shape, write-back | 0.261 ms | 0.071 ms | 0.27 |
| step shape, row fill, clamp live | 1.485 ms | 0.300 ms | **0.20** |
| overshoot clamp, resolved, always clamped | 30.24 ms | 11.49 ms | 0.38 |
| diagnostics, cost of being measured | 5.04 ms | 0.65 ms | **0.13** |
| fleets, 100 grids, whole fleet | 53.50 ms | 14.66 ms | 0.27 |
| noise, spread of five identical runs | 0.353 ms | 0.092 ms | 0.26 |

**The ratio is not a constant, and that is the finding.** A uniform 3.5× would have left every
comparison this repository ever made intact, since a benchmark exists for ratios. It is not
uniform: the row fill is 5× and the cost of diagnostics 7.7×, where the clamped conduction loop is
2.6×. So the published *shares* were wrong too — the row fill was quoted as a fifth of a step and
the diagnostics as a 15 % surcharge, and both were mostly the price of unoptimised code around a
field store. Any figure on [benchmarks.md](benchmarks.md) that is a *share* of a step, and any
optimisation whose case rested on hoisting a load out of a loop, was measured on an instrument
that exaggerated exactly that kind of work.

**What changed.** `tests/Directory.Build.props` sets `<Optimize>true</Optimize>` for every project
and every configuration. `dotnet run` and `dotnet test` still build Debug; Debug is now optimised.
Pinned by `OptimisedBuildTests`, which reads `DebuggableAttribute` off the built core, harness and
test assemblies and fails if any of them says the optimiser is off — a line dropped from a props
file fails nothing else.

**What it is not.** It is not the game. The game compiles `Data/Scripts` with its own compiler and
runs it on .NET Framework 4.8; the harness is .NET 9. The 6–8× gap between a field dump and the
harness recorded on [load-and-hitching.md](load-and-hitching.md) was measured against the
unoptimised harness and is now larger, not smaller — the harness got faster and the game did not.
Absolute nanoseconds still do not transfer, which [benchmarks.md](benchmarks.md#what-it-measures)
already says; what transfers is the shape of a curve, and the shapes are now taken on an optimising
JIT, which is what the game has.

**The fast lane** ran 1,662 cases green on the optimised build before the change was committed.

## Iteration 2 — the ladder's `build` column measured the hull generator

*In progress.* `PerformanceReport.Build` starts its stopwatch before `PlaceCensus`, which since
`C26` bolts every block by trying all 24 orientations against six neighbours through a dictionary
and an `Enum.GetValues` iterator per block. Measured on a 32,800-block hull, optimised: the
generator is **1.0 s** and the simulation's own build — surfaces, links, loops, rooms, exposure —
is **0.10 s**. The ladder's `build` column reported the sum under a name that reads as the second.

---

## Change log

| Date | Change |
| --- | --- |
| 2026-08-26 | Opened, with the first two iterations of the 2026-08-26 pass: the harness had measured unoptimised code for its whole life, and the ladder's `build` column had been measuring the census generator since `C26`. |
