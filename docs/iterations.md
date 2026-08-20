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
[`sim/benchmarks/performance.csv`](../sim/benchmarks/performance.csv), so any figure in the report
can be recovered for any row here by reading that file at the row's commit.

## The suite

| # | date | commit | tests | duration | note |
| ---: | --- | --- | ---: | ---: | --- |
| 0 | 2026-08-20 | `b0a8496` | 1,017 | 45 s | the state this session started from |
| 1 | 2026-08-20 | `57d1807` | 1,020 | 45 s | +3, `FixedSourceRowTests` |
| 2 | 2026-08-20 | `78d736f` | 1,020 | 45 s | no test added or removed; five suites moved onto one fixture |

## The solver

Headline figures from `bench report --size 32000 --max 125000`, on a 32,800-block census hull in
flight unless the row says otherwise.

| # | commit | calibration | noise | ladder 8k | ladder 32k | every feature on | convection isolated | env pass, ns/node |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 0 | `b0a8496` | 89.3 ms | 0.065 ms | 1.211 ms | 3.834 ms | 3.831 ms | 1.902 ms | 2.29 |
| 1 | `57d1807` | 86.2 ms | 0.014 ms | 1.129 ms | 3.603 ms | 3.595 ms | 1.661 ms | 2.11 |
| 2 | `78d736f` | — | — | — | — | — | — | — |

Row 2 changed no shipped code, so its solver figures are row 1's.

## Recording a row

```bash
cd sim
dotnet test                                                                   # tests, duration
dotnet run --project Thermodynamics.Sim -- bench report --size 32000 --max 125000 --csv benchmarks
dotnet run --project Thermodynamics.Sim -- bench elements                     # ns per node
```

Take it on a quiet machine and check the noise row before committing. A row whose noise column is
several times its neighbours' should be re-taken rather than explained.
