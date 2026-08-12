# Telemetry

A data collection module for the live mod. It observes the running simulation and writes a
report when the world closes.

It exists to answer the questions in [bugs-and-performance.md](bugs-and-performance.md) with
measurements from real play rather than from a synthetic benchmark: what temperature blocks
actually sit at, which definitions are mistuned, how often the room mapper restarts, and what
the mod costs per frame on a ship-shaped grid.

Nothing in it changes simulation behaviour. Every hook is a no-op when collection is off, and
every hook swallows its own exceptions.

## Output

Three files land in the world's storage folder
(`%AppData%/SpaceEngineers/Saves/<world>/Storage/<mod>/`), stamped with the session start time:

| File | Contents |
| --- | --- |
| `Thermodynamics_Telemetry_<stamp>.log` | the full report |
| `Thermodynamics_BlockTypes_<stamp>.csv` | one row per block definition |
| `Thermodynamics_Grids_<stamp>.csv` | one row per grid |

A summary line always goes to `SpaceEngineers.log`. If world storage cannot be written — which
is the failure mode most likely during shutdown — the whole report goes to the game log instead,
so a run is never lost silently.

Type `/thermaldump` in chat to write a report without closing the world. Repeated dumps are
safe; the "final state" sections are rebuilt each time rather than appended to.

## What is collected

**Session** — world name and path, online mode, server/dedicated/multiplayer, real and in-game
elapsed time, frame count, and the full `Settings` snapshot that produced the numbers.

**Per grid** (kept for the life of the grid, and retained after it is destroyed) — cell count,
block count, conduction links, sealed rooms, exterior cells, surface entries, coolant loops,
`RecentlyRemoved` size and mapper queue depths, all as min/mean/max over the session; block
add/remove/ignore counts, splits, merges, door state changes, surface recalcs, crawl restarts,
mapper passes and completions, coolant crawls, saves and loads with payload sizes; simulation
steps, cell updates, quota, cells per frame, critical block counts, damage events and total
damage, peak temperature with the block that reached it, and the final temperature histogram;
ambient temperature, air density, wind speed, convection coefficient, effective solar energy,
grid speed, the fraction of time the sun was occluded, the fraction of time in atmosphere, and
which planets were visited.

**Per block definition** (aggregated across every grid) — the thermal definition in force,
placed/removed/live/peak counts, mass, neighbour count, exposed surfaces and area, summed `kA`,
and min/mean/max/sd for temperature, conduction ΔT, radiation ΔT, friction ΔT, heat generation,
power produced and consumed, thrust draw and solar intensity; critical updates and heat damage
dealt; and both a sampled and a final temperature histogram.

**Cost** — call count, total, mean, worst and a distribution for grid simulation, the room
mapper, surface state calculation, environment and solar preparation, the coolant crawler, save
and load. Rows marked "of which" are nested inside grid simulation and are not double counted in
the total.

**Anomalies** — NaN, infinite and implausibly high temperatures, temperatures clamped to zero
from a positive value (the signature of an unstable step, P8), and any exception caught inside
the module. Each is recorded once per kind with a count and its first and last example.

## Cost of collecting

The per-cell hook does a few increments, one comparison against the running peak, and a branch
on a countdown; the wide statistics run on one update in `TelemetrySampleStride` (default 4).
Per-grid structure sampling runs once per *simulation step*, not once per frame, and the one
O(cells) walk in it — the conduction link count — runs once in sixteen of those.

Memory is bounded by construction. There are no sample buffers and no per-cell history: every
figure is a streaming count, sum, sum of squares, min and max, or a fixed-bucket histogram. The
dictionaries are bounded by the number of block definitions (4096), grids that have existed
(2048) and anomaly kinds (64); overruns are counted and reported rather than allowed to grow.

## Tests

Most of the module reads `Sandbox.*` and `VRage.Game` types, which cannot load in [`sim/`](../sim).
Its decision logic does not, and lives in three files that reference nothing but `System`:

| File | What it holds |
| --- | --- |
| `TelemetryStats.cs` | `RunningStat`, `Histogram`, `TimingStat` |
| `TelemetryAnomalies.cs` | anomaly classification, the sampling gate |
| `TelemetryFormat.cs` | report and CSV formatting |

`Thermodynamics.Tests` links those three directly — the same files the game compiles, not a
copy — and covers them in `TelemetryStatsTests.cs`, `TelemetryAnomalyTests.cs` and
`TelemetryFormatTests.cs`. 72 tests, about 50 ms.

What they pin down:

- **Accumulators.** Mean, population standard deviation and min/max against hand-checked values;
  an empty stat reporting zero rather than its `float.MaxValue` seeds; NaN and both infinities
  refused rather than poisoning every derived figure; merge equalling a single combined pass.
- **Histograms.** Edges as exclusive upper bounds, the overflow bucket, NaN dropped but infinity
  bucketed, `Clear` (which is what lets a mid-session dump rebuild rather than accumulate), and
  percentages taken over the total with empty buckets skipped.
- **Timings.** That `Begin` resets the shared `Stopwatch` — without it every "max ms" in the
  report would climb for the life of the session — and that a row lines up with its header.
- **Anomalies.** Each kind fires on exactly its own condition, ordinary play temperatures fire
  nothing, a cold block that stays cold is not a clamp, and no two kinds share a name.
- **The sampling gate.** Exactly one call in N, evenly spaced, first call always admitted, and a
  zero or negative stride falling back to sampling everything rather than nothing.
- **Formatting.** Numbers, integers and bucket labels are invariant even with the thread pinned
  to `de-DE`, where a comma decimal separator would silently shift every CSV column; non-finite
  values become empty fields rather than `NaN`; player-supplied grid names are quoted and
  embedded quotes doubled; truncation never exceeds its column.

What they do not cover is the wiring — which game fields each hook reads, and whether the report
is written successfully during shutdown. That is the same boundary the rest of `sim/` accepts,
and it can only be checked by loading a world.

## Settings

```xml
<EnableTelemetry>true</EnableTelemetry>
<TelemetrySampleStride>4</TelemetrySampleStride>
```

Note that `Settings.Load()` still has no callers (**C10**), so these take their default values
until the config file is wired up.
