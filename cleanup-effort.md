# Cleanup effort

What a pass over the code found, what was changed, and — more usefully — what was found and
**deliberately not** changed, with the reason. A reduction nobody records is a reduction the next
reader proposes again.

> The rules argued here are stated canonically in [docs/rules.md](docs/rules.md): `E5` `D5` `M7` `P4`.

## What was changed

| # | Change | Why it was safe |
| --- | --- | --- |
| 1 | **One preamble for the API's grid accessors.** Five accessors in `ThermalApi` opened with the same three-line resolve — null grid, null game logic, no `ThermalGrid` — so `GridOf(IMyCubeGrid)` now sits beside the `GridOf(IMySlimBlock)` that already existed. | A pure extraction: the same three checks in the same order. Five copies is five chances to check two of the three, and the failure would be an exception crossing the API boundary into another mod. |
| 2 | **`LiveGrids` cannot be null, and three callers checked anyway.** `Live` is a `static readonly` list only added to and removed from. Three callers guarded against null and six did not, which left a reader unable to tell whether the six were a latent `NullReferenceException` or the three were dead code. | They were dead code. The contract is now stated on the property, and it names the case that *is* real: the list can be empty, which is a different check and is kept. |
| 3 | *Nothing changed* — the vendored `TryGet*` surface, [below](#nine-unused-tryget-methods-in-definitionextensionsapics). | A pass that skips a number leaves a reader wondering which iteration went missing. |
| 4 | **The per-face frame, computed once instead of four times.** Counting a block's exposure, auditing it and building a room map each walked the six faces of a block's bounds and each worked out the same six numbers to do it. `BoxGeometry.Span` returns them. | Measured, because it is the surfaces hot path — see below. 52 bit-identity tests pass unchanged, and `FaceSpanTests` is written against the arithmetic rather than against the extraction, so it would have caught a copy that had drifted before it was one function. |
| 5 | **The live cap judgement quoted a figure the re-take superseded.** The paragraph deciding `MaxSubstepsPerBlock` stays 0 is current reasoning, not a historical record, and it cited the 0.282 K measured through the reader `A13` is about. | The decision does not move — it rests on *who pays*, and the who has not changed — so the note says the margin moved rather than silently swapping the number. A decision whose stated evidence changed and whose conclusion did not is worth showing, not tidying. |
| 6 | **Two rigs the harness already owned, built by hand three times each.** `HeatPumpTests` built the same conducting rig three times, and a pumpless ring was derived cell by cell in `Scenarios.LoopFaults` and twice more in `CoolantFaultTests`. Now `ConductingRig` and `PipeFitter.BuildPumplessRing`. | Three copies of a geometry derivation is three chances for one to lay a corner where a straight belongs — and a mis-oriented pipe still builds a ring, so a fault test would fail for the wrong reason and pass. 2,141 tests green. |
| 7 | **Twelve copies of `number`, already drifted into three answers.** Seven returned `None` for an absent or unparseable cell, three took `default=0.0`, and `censusdiff.py` returned `0.0` outright. | The last was **a live defect**: `censusdiff` sums a column over the ships two censuses share, so a column one census does not carry read nought on every ship and printed as a total — *before 0, after 12,345* reads as a column that grew. Comparing censuses across builds is the whole purpose of that tool. |
| 8 | **Row identity, stated in three places.** `key_of` in two tools and `KEY` in a third. Now `scoring.ROW_KEY` and `scoring.key_of`. | Both halves of that identity have failed once: keyed by id alone the fourteen ships sharing `workshop_id` 0 through a workshop collection were one ship; keyed without the arm, `verdict.py` called half a paired walk's rows duplicates and scored `G6` on whichever arm came first. The three tools that read a cell with a default keep it, at the call site, where a reader can see the choice. |
| 9 | **Two constants that were one concept declared twice.** `LargeGridCellMetres` / `SmallGridCellMetres` sat in `Core/Definitions/LoopThermalProperties` **and** privately in `Definitions/ThermalLoopDefinition`; `ThermalDamage` — the damage type every heat kill is attributed to — was declared privately in both `ThermalGrid` and `ThermalCharacters`. | Two declarations of a damage type are two types that happen to spell the same word today. A block cooked by its own heat and a character cooked by the air are the same cause, and anything filtering on the type sees one or the other depending on which declaration it matched. Renaming one is a silent split: both still compile, both still damage. |
| 10 | **Finishing what iteration 8 started.** `key_of` was still declared in `panel.py` and `typical.py`, and `core.py` and `pack.py` still had their own cell parse. All four now use `scoring`. | A half-done consolidation is worse than none: it leaves two conventions and a reader cannot tell which is current. Verified by reproduction rather than by tests alone — `panel.csv`, `typical.csv`, `core-corpus.csv` and both walk summaries come back byte for byte. |
| 11 | **The census's tool list said it mirrored the load model's, and it had drifted.** `CorpusCensus.IsToolType` opened with *mirrors `ShipLoad`'s own list, which is private to it* — and the privacy is the cause: commit `458fad9` took the jump drive off `ShipLoad.IsTool` with an argued reason, and the mirror kept it. `ShipLoad.IsTool` is public now and the census reads it. | The drift was live: `tool_n` counted as a tool a block the load model runs as a drive, so the census and the simulation disagreed about what a tool is. No published claim rests on `tool_n` — nothing in `scoring.py`, `verdict.py` or any page reads it; its one consumer is a repack — so the correction changes future censuses only, and `censusdiff.py` will report the drop on the ships that mount a drive rather than letting it pass as growth. Suite 2,237 green (2026-09-04, commit `fe3817c`; baseline 2,237 at `358797b`). |
| 12 | **Two byte-identical statements of where the game is installed.** `BalanceTests.GameContentPath` duplicated `GameBlocks.ContentPath`, and the harness copy's own summary *justified* the pair by pointing at the test copy — a reason that runs one way only, since the tests already reference the harness. The tests read `GameBlocks.ContentPath` now. | A pure deletion of one of two identical bodies. The surviving path is exercised for real on this machine, where the game is installed and `TheVanillaComponentListsStillMatchTheInstalledGame` walks it to `Cubes.xml`. Suite 2,237 green (2026-09-04, commit `89eed86`). |
| 13 | **The fastest-of-N stopwatch, stated once.** `FleetParallelLab.Time` and `StaggerLab.Time` were identical bodies — the `M4` discipline written twice, so two duration labs could quietly come to measure the same statistic two different ways. `LabTiming.FastestOf` is the one statement; each lab keeps its own `Repeats`, which is part of its design the way a parse default belongs at its call site (iteration 8's reasoning, applied to a repeat count). | A pure extraction, bit-identical code path; the two labs' wall-clock tests are opt-out of parallelism already (`O4`) and unchanged. Suite 2,237 green (2026-09-04, commit `c344f1d`). |

### Measuring cleanup 4, and why one reading was not enough

**The first paired reading said the refactor was 1.9 % slower and it was noise.** Best-of-N moved
5.474 → 5.579 ms while the median moved the other way, −0.3 %, which is the signature of a
difference smaller than the run's own spread.

Three paired rounds, each running both branches inside one hold:

| round | master | cleanup 4 | Δ |
| --- | ---: | ---: | ---: |
| 1 | 5.518 ms | 5.498 ms | −0.020 |
| 2 | 5.591 ms | 5.578 ms | −0.013 |
| 3 | 5.617 ms | 5.611 ms | −0.006 |

**The refactor is at worst neutral: it is faster in all three pairs, by a quarter of a per cent.**
What the table shows more usefully is `master` drifting **upward across rounds** — 5.518 to 5.617,
1.8 %, on unchanged code — which is larger than the effect being measured and is why the two
branches have to be read inside one round rather than across two runs. A before-and-after taken an
hour apart on this machine measures the hour.


## What was found and not changed

### The cleanup broke the mod build and 2,141 tests did not notice

Iteration 9 pointed `ThermalLoopDefinition` at `Core`'s `LoopThermalProperties` without adding the
`using`. `Generic.csproj` — the mod itself — stopped compiling, and the full suite stayed green
through the merge.

**The test projects compile `Core/**/*.cs` and a named list of adapter files.** `Game/`,
`Definitions/`, the session and the HUD compile only in the mod project, which nothing in a test run
builds. So `dotnet build tests/Thermodynamics.Tests` followed by `dotnet test --no-build` is green
over a mod that does not exist.

**`dotnet build tests/Thermodynamics.slnx` catches it in four errors**, and that command was already
the documented one — the mistake was building the project by name, which this repository's own notes
recommend for a *different* reason (an incremental root build leaves a stale test DLL behind). Two
pieces of correct advice that compose into a hole.

[development.md](docs/development.md) now says so where the build commands are, beside the
malformed-project case it is a sibling of: there the project leaves the build, here it was never in
it.

### Constants that share a value and not a meaning

A sweep for constants declared with the same type and literal in more than one file returns about
thirty groups, and **almost all of them are coincidence**: `MassSweepInterval` is 8 and so is
`Longitudes`; `SaveFlushFrames` is 60 and so is `MaxRoomsReported`. Merging those would couple
things whose only relationship is that nobody has yet had a reason to change one of them.

Two near-misses are worth naming because they look like the real thing and are not:

* **`SinkGroup = "Utility"`** in `ThermalCoolantPumpBlock` and `ThermalHeatPumpBlock`. Two block
  types that each declare which power group they draw from. They agree today; a future block that
  drew from another group would be a change to one of them, not a divergence.
* **`SpecificHeatId = "SpecificHeat"`** in `ThermalCellDefinition` and `ThermalLoopDefinition`. Two
  readers of two *different* definition groups that happen to name a field the same way. Renaming a
  cell's field would not imply renaming a loop's.

**The test that separates them from the real duplicates is whether changing one implies changing the
other.** For `ThermalDamage` it does — there is one answer to *what killed this* — so it is one
declaration now. For these two it does not.

### Nine unused `TryGet*` methods in `DefinitionExtensionsAPI.cs`

`TryGetString`, `TryGetInt`, `TryGetLong`, `TryGetFloat`, `TryGetColor`, `TryGetVector2I`,
`TryGetVector2D`, `TryGetVector3I` and `TryGetVector3D` are called from nowhere in this repository;
only `TryGetBool` and `TryGetDouble` are, from the three typed readers in `Definitions/`.

**Not removed, because the file is vendored.**
[architecture.md](docs/architecture.md) files it with `NetworkAPI/` and `RichHudFramework/` as
*third-party API clients — do not edit; replace wholesale when upstream updates*. Trimming a
vendored client to the subset this mod happens to use converts every future update from a copy into
a merge, and buys 456 lines that cost nothing at runtime: they are never called, so they are never
JITted.

**This is the rule rather than the exception for the three vendored trees**, and the reason is worth
stating once: dead code inside a vendored client is not this repository's dead code. It is
somebody else's live code that this repository does not happen to call.

---

## Change log

| Date | Change |
| --- | --- |
| 2026-09-04 | Second pass opened, ten iterations. Iteration 11 merged: the census's tool list, which claimed to mirror `ShipLoad`'s and had drifted over the jump drive, dissolved into the one public list. Suite figures in this pass are dated and stamped with the commit they were taken at, per the standing requirement that a test result is transitory. |
| 2026-08-29 | Opened. Two changes merged — one preamble for the API's grid accessors, and the three dead null checks against a `LiveGrids` that cannot be null — and one finding recorded rather than acted on: the nine unused `TryGet*` methods in the vendored `DefinitionExtensionsAPI.cs`, which stay because trimming a vendored client to the subset this mod happens to call turns every future update from a copy into a merge. |
