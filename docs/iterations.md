# Iteration log

One row per pass over this repository, so a change over many sessions can be read as a trend
rather than reconstructed from commit messages. Each row records what the suite cost to run and
what the solver cost to step, taken at the end of that pass.

**Read the columns for their shape, not their absolute value.** Suite duration and step figures
both belong to the machine that took them; a row taken elsewhere is not comparable to its
neighbours. The calibration and noise columns are what say whether two rows may be read against
each other at all — a run taken at several times the usual noise moved the smallest figures in
the benchmark report by several per cent.

The benchmark baseline itself is versioned at
[`tests/benchmarks/performance.csv`](../tests/benchmarks/performance.csv), so any figure in the report
can be recovered for any row here by reading that file at the row's commit.

## The suite

| # | date | commit | tests | duration | note |
| ---: | --- | --- | ---: | ---: | --- |
| 0 | 2026-08-20 | `b0a8496` | 1,017 | 45 s | the state this session started from |
| 1 | 2026-08-20 | `57d1807` | 1,020 | 45 s | +3, `FixedSourceRowTests` |
| 2 | 2026-08-20 | `78d736f` | 1,020 | 45 s | no test added or removed; five suites moved onto one fixture |
| 3 | 2026-08-20 | `9aaf3d2` | 1,026 | 44 s | +6: `StepTermsTests`, and a flight case for the bit-identity suite |
| — | | `9aaf3d2..55a6935` | | | **36 commits recorded no row.** The wind model, per-planet climate, block derivation from build components, the blueprint corpus and the balance lab all landed between rows 3 and 4. |
| 4 | 2026-08-21 | `55a6935` | 1,285 | 2 m 11 s | +259 across those 36 commits and this one; this pass added `DumpAuditTests` and the field-dump fixture |
| 5 | 2026-08-20 | `b7ccf75` | 1,358 | 5 m 4 s | +73: world settings, the overlay budget, the descent, the planet-definition merge, `RescanGate`, and the burial audit. Nine slow suites now carry `speed=slow`. |
| 6 | 2026-08-20 | `f411f7d` | 1,359 | **50 s** | one ungated corpus test was 4 m 57 s of every run since the fixture landed; rows 4 and 5 carry it. The `speed!=slow` lane is 14 s. |
| — | | `f411f7d..c18e3e4` | | | **20 commits recorded no row.** The wind burial audit, the per-planet climates, the block heat index, the balance bench and the first full corpus survey landed between rows 6 and 7. |
| 7 | 2026-08-22 | `c18e3e4` | 1,467 | 51 s | where this pass started; +108 across those 20 commits |
| 8 | 2026-08-22 | *(this pass)* | 1,526 | 50 s | +59. A defragmentation pass, and then a validation one: shipped code is **343 lines shorter** with nothing left in it that nothing calls, and the ground truth every benchmark rests on moved from two ships in a vanished session to 8,102 workshop hulls measured in the lab. |

## The solver

Headline figures from `bench report --size 32000 --max 125000`, on a 32,800-block census hull in
flight unless the row says otherwise.

| # | commit | calibration | noise | ladder 8k | ladder 32k | every feature on | convection isolated | env pass, ns/node |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 0 | `b0a8496` | 89.3 ms | 0.065 ms | 1.211 ms | 3.834 ms | 3.831 ms | 1.902 ms | 2.29 |
| 1 | `57d1807` | 86.2 ms | 0.014 ms | 1.129 ms | 3.603 ms | 3.595 ms | 1.661 ms | 2.11 |
| 2 | `78d736f` | — | — | — | — | — | — | — |
| 3 | `9aaf3d2` | 86.2 ms | 0.043 ms | 1.119 ms | 3.544 ms | 3.550 ms | 1.617 ms | 2.11 |
| 4 | `55a6935` | 87.4 ms | 0.038 ms | 1.134 ms | 3.621 ms | 3.613 ms | 1.680 ms | 2.17 |
| 5 | `b7ccf75` | 105.6 ms | 0.206 ms | 1.210 ms | 4.198 ms | 4.282 ms | 1.951 ms | 2.41 |
| 7 | `c18e3e4` | 85.8 ms | 0.057 ms | 1.095 ms | 3.490 ms | 3.496 ms | 1.616 ms | 2.02 |
| 8 | *(this pass)* | 84.0 ms | 0.075 ms | 1.085 ms | 3.461 ms | 3.510 ms | 1.636 ms | 2.02 |

Row 2 changed no shipped code, so its solver figures are row 1's.

**Row 3's noise column is three times row 1's**, and row 1's was the quietest run this machine has
recorded. Differences of a few per cent between those two rows are not readable; the row 3 change
was aimed at the one-substep configurations, which are in the table below rather than above.

**Row 4 is row 3 on a slightly slower machine.** Calibration is up 1.4 % and every column with it,
by 1.4-3.9 %, in the same direction and roughly the same proportion — which is what a machine
looks like, not what a change looks like. The two rows are comparable in the one way that matters:
`substeps demanded` on both ladder rungs is identical to six decimal places, so the census hull's
stiffness has not moved and the columns are measuring the same work. The report names eight
regressions against the committed baseline; all eight are under 0.02 ms absolute, against a noise
floor of 0.038 ms.

Nothing in rows 3 to 4 aimed at the solver. The shipped code this pass touched — the frame cost
tracker, the grid profiler, the two host stages now timed — is telemetry-gated and lives in the
game adapter, which the harness does not compile, so no benchmark here can see it either way.
`tests/benchmarks/performance.csv` is deliberately left at row 3: re-recording a baseline for a
pass that moved no solver code would bake this run's machine state into every future comparison.

**Rows 7 and 8 were taken back to back on one machine, which is the only way this pass's figures
mean anything.** Row 6's baseline is `9aaf3d2`, two hundred commits back, so a diff against the
committed `performance.csv` spans everything since — including the change that stopped filing an
overheat event per substep, which shows as a 95 % fall in event counts and belongs to a commit
before this pass began. So the pass was measured against *its own start* instead: the same report
run at `c18e3e4` and at the tip, twenty minutes apart, on an idle machine.

Against that, **the pass moved nothing.** Six rows are named as regressions and every one is
inside or barely above a noise floor that is itself up 30 % between the two runs: the largest is
`step shape / fixed per step` at 0.047 ms against a 0.075 ms spread. The ladder rows did not move
enough to be named at all. That is the expected result — the code this pass deleted was code
nothing called, and the two structures it changed, a grid's key index and a room's cell list, are
touched when a block is placed and when a mapping pass completes, not inside a substep.

`tests/benchmarks/performance.csv` is deliberately left at row 3, for the reason row 4 gives.

**Row 8's second half changed no code the columns can see and changed what the columns mean.** The
census hull is the instrument every figure above is taken on, and it was held to `Census.Field` —
21.35 and 31.25 substeps, from two ships in two live sessions. `StiffnessLab` asks the same
question of 8,102 real workshop blueprints in four minutes. The field figures survive: they land at
the 63rd and 85th percentile. What they could not show is that the population is **bimodal** — a
light sets the substep count on 45 % of hulls at a median of 28.5 and armour on the rest at 4.8,
with almost nothing between — and that the census hull sits in the trough between them, feeling a
little less of the air than a typical hull because its stiffest block is less exposed. See [stiffness.md](stiffness.md#the-same-question-asked-of-eight-thousand-real-ships).

Where a pass moves something the columns above cannot see, it gets a row here.

| # | figure | before | after |
| ---: | --- | ---: | ---: |
| 3 | `optimized`, per simulated second | 2.100 ms | **1.873 ms** |
| 3 | `simlite`, per simulated second | 2.081 ms | **1.868 ms** |
| 3 | `simulation`, per simulated second | 4.201 ms | **3.916 ms** |
| 3 | substep cap 1, step | 0.668 ms | **0.619 ms** |
| 3 | substep cap 4, step | 1.210 ms | **1.165 ms** |
| 3 | step shape, fixed per step | 0.535 ms | **0.494 ms** |
| 8 | `GridModel` indexes, B/block at 126,731 | 120 | **86** |
| 8 | `RoomMap` retained, B/block at 126,731 | 166 | **128** |
| 8 | `RoomMap` retained, B/block at 505,566 | 309 | **229** |
| 8 | retained total, B/block at 126,731 | 1,095 | **1,023** |

**Row 5 was taken on a loud machine and its solver columns are not readable against row 4.**
Calibration is up 21 % and noise is five times row 4's; a re-take mid-pass moved every column
together. Nothing in the pass touched the per-node path — the burial factor is one branch per
environment solve, once a step per grid — so the 14-19 % across the board is the machine. Re-take
on a quiet one before reading a trend from it.

## Recording a row

**Measure the pass against its own start, not against the committed baseline.** That file is
pinned at row 3, so a diff against it spans every commit since and attributes all of them to the
pass that ran it. Build the pass's starting commit in a worktree, take a report from it into a
scratch directory, then run the tip's report with `--baseline` pointing at that. Twenty minutes
apart on one idle machine is what makes the two comparable, and it is the only way to say a pass
moved nothing.

```bash
cd tests
dotnet test                                                                   # tests, duration
dotnet run --project Thermodynamics.Sim -- bench report --size 32000 --max 125000 --csv benchmarks
dotnet run --project Thermodynamics.Sim -- bench elements                     # ns per node
```

Take it on a quiet machine and check the noise row before committing. A row whose noise column is
several times its neighbours' should be re-taken rather than explained.
