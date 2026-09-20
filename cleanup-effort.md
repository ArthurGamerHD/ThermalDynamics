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
| 6 | **Two rigs the harness already owned, built by hand three times each.** `HeatPumpTests` built the same conducting rig three times, and a pumpless ring was derived cell by cell in `Scenarios.LoopFaults` and twice more in `CoolantFaultTests`. Now `ConductingRig` and `PipeFitter.BuildPumplessRing`. | Three copies of a geometry derivation is three chances for one to lay a corner where a straight belongs — and a mis-oriented pipe still builds a ring, so a fault test would fail for the wrong reason and pass. 2,141 historical test cases green. |
| 7 | **Twelve copies of `number`, already drifted into three answers.** Seven returned `None` for an absent or unparseable cell, three took `default=0.0`, and `censusdiff.py` returned `0.0` outright. | The last was **a live defect**: `censusdiff` sums a column over the ships two censuses share, so a column one census does not carry read nought on every ship and printed as a total — *before 0, after 12,345* reads as a column that grew. Comparing censuses across builds is the whole purpose of that tool. |
| 8 | **Row identity, stated in three places.** `key_of` in two tools and `KEY` in a third. Now `scoring.ROW_KEY` and `scoring.key_of`. | Both halves of that identity have failed once: keyed by id alone the fourteen ships sharing `workshop_id` 0 through a workshop collection were one ship; keyed without the arm, `verdict.py` called half a paired walk's rows duplicates and scored `G6` on whichever arm came first. The three tools that read a cell with a default keep it, at the call site, where a reader can see the choice. |
| 9 | **Two constants that were one concept declared twice.** `LargeGridCellMetres` / `SmallGridCellMetres` sat in `Core/Definitions/LoopThermalProperties` **and** privately in `Definitions/ThermalLoopDefinition`; `ThermalDamage` — the damage type every heat kill is attributed to — was declared privately in both `ThermalGrid` and `ThermalCharacters`. | Two declarations of a damage type are two types that happen to spell the same word today. A block cooked by its own heat and a character cooked by the air are the same cause, and anything filtering on the type sees one or the other depending on which declaration it matched. Renaming one is a silent split: both still compile, both still damage. |
| 10 | **Finishing what iteration 8 started.** `key_of` was still declared in `panel.py` and `typical.py`, and `core.py` and `pack.py` still had their own cell parse. All four now use `scoring`. | A half-done consolidation is worse than none: it leaves two conventions and a reader cannot tell which is current. Verified by reproduction rather than by tests alone — `panel.csv`, `typical.csv`, `core-corpus.csv` and both walk summaries come back byte for byte. |
| 11 | **The census's tool list said it mirrored the load model's, and it had drifted.** `CorpusCensus.IsToolType` opened with *mirrors `ShipLoad`'s own list, which is private to it* — and the privacy is the cause: commit `458fad9` took the jump drive off `ShipLoad.IsTool` with an argued reason, and the mirror kept it. `ShipLoad.IsTool` is public now and the census reads it. | The drift was live: `tool_n` counted as a tool a block the load model runs as a drive, so the census and the simulation disagreed about what a tool is. No published claim rests on `tool_n` — nothing in `scoring.py`, `verdict.py` or any page reads it; its one consumer is a repack — so the correction changes future censuses only, and `censusdiff.py` will report the drop on the ships that mount a drive rather than letting it pass as growth. Suite 2,237 green (2026-09-04, commit `fe3817c`; baseline 2,237 at `358797b`). |
| 12 | **Two byte-identical statements of where the game is installed.** `BalanceTests.GameContentPath` duplicated `GameBlocks.ContentPath`, and the harness copy's own summary *justified* the pair by pointing at the test copy — a reason that runs one way only, since the tests already reference the harness. The tests read `GameBlocks.ContentPath` now. | A pure deletion of one of two identical bodies. The surviving path is exercised for real on this machine, where the game is installed and `TheVanillaComponentListsStillMatchTheInstalledGame` walks it to `Cubes.xml`. Suite 2,237 green (2026-09-04, commit `89eed86`). |
| 13 | **The fastest-of-N stopwatch, stated once.** `FleetParallelLab.Time` and `StaggerLab.Time` were identical bodies — the `M4` discipline written twice, so two duration labs could quietly come to measure the same statistic two different ways. `LabTiming.FastestOf` is the one statement; each lab keeps its own `Repeats`, which is part of its design the way a parse default belongs at its call site (iteration 8's reasoning, applied to a repeat count). | A pure extraction, bit-identical code path; the two labs' wall-clock tests are opt-out of parallelism already (`O4`) and unchanged. Suite 2,237 green (2026-09-04, commit `c344f1d`). |
| 14 | **Iteration 8's wrappers had themselves become three copies.** The default-at-the-call-site decision left `panel.py`, `typical.py` and `pack-bench.py` each restating the wrapper over `scoring.number` — mechanism and shared boilerplate included — around one paragraph of genuinely local justification. `scoring.number_or` holds the mechanism once and takes no default of its own, so the caller still states the choice; each wrapper keeps only its own paragraph. | The contract that made iteration 8 right is preserved and now pinned: `test_number_or_defaults_only_where_number_is_unmeasured` asserts a measured nought comes back as the measurement, never the default. Verified by reproduction as iteration 10 was — `panel.csv`, `typical.csv` and `bench.json` byte-identical on the 2026-08-21 datasets. 199 python tests and C# suite 2,237 green (2026-09-04, commit `c4ca7f8`). |
| 15 | **Two waste-fraction labs, one cache policy stated twice.** `ReactorLab` and `OxygenGeneratorLab` already share their rig (`SoloBlockRig`) and deliberately mirror each other's prose; what they also shared, without saying so, was an identical fraction-keyed results cache — same lock discipline, same compute-outside-the-lock choice. `FractionCache<TRow>` states it once, with the benign second-write-wins race documented. | The two `Row` classes were considered for merging and left apart, by the standing test: a producer's fields and a consumer's differ (`LoadFraction` against a `Draw` enum, rated output against stated draw), so changing one does not imply changing the other. The cache is the opposite case — one copy losing its lock would be a silent fault in an eight-wide suite. The waste-heat tests sweep several fractions, so the cache path is exercised by its consumers. Suite 2,237 green (2026-09-04, commit `7cba2a9`). |
| 16 | **The block under the crosshair, resolved once.** The crosshair readout and the extinguisher HUD each walked camera → 15 m ray → grid → adapter → nudged cell → bound block, and the 15 and the 0.005 were a contract between two copies: had one nudge moved, the two readouts would describe different blocks while the player looks at one. `Crosshair.Resolve` is the one statement, both constants named and argued. | Shipped client code no test reaches — the path needs a session — so the verification is what the rules give it: the `.slnx` build compiles the mod project (`C11`), the helper uses no API the two copies did not already use (`C2` unchanged by construction), and the extraction preserves the calls, constants and early-outs in their original order. Suite 2,237 green (2026-09-04, commit `1fedf38`). |
| 17 | *Nothing changed* — the two voxel walks, [below](#two-voxel-walks-that-share-an-algorithm-and-not-a-definition). | They share Amanatides and Woo and differ in precision, origin and bounding, each for a reason its own comments state; merging would trade a documented pair for a mode flag or a hot-path precision change nothing asked for. Each walk keeps its own oracle. |
| 18 | **Two copies of the analytic sun oracle, and one was the other's parent.** `Reference.Lit` was extracted from `SunShadowMapTests` for the scenario claims, and the class kept its private `ReferenceLit` and `Penetrates` — identical to the constant (`1e-3f`, `1e-9f`, same final condition, verified line by line before deletion). The tests read `Reference.Lit` now. | `E7` asks an oracle to be independent of the *model*, not of other tests — two tests sharing one slow analytic reference is the intended shape, and two copies of an oracle drift as silently as two copies of anything else. The exactness sweep still checks every face of every block against it. Suite 2,237 green (2026-09-04, commit `4e6c709`). |
| 19 | **Cleanup 4, finished.** `SunShadowMap.FaceLitFraction` was a fourth site hand-deriving the per-face frame that `BoxGeometry.Span` extracted from the other three, and it was left behind. It reads `Span` now. | The arithmetic is identical and doubly pinned: `FaceSpanTests` is written against the numbers themselves, and the shadow map's exactness sweep checks every face of every block against the analytic oracle, fractional cases included. No cost claim is made — the call sites are the environment-row fills on shadow-pass completion, colder than the surfaces path where cleanup 4 measured this same transformation shape at worst neutral, and an unmeasured claim is not one (`M5`). Suite 2,237 green (2026-09-04, commit `2ee609d`). |
| 20 | **One claim on a grid group, and the copies' comments had already split.** The drag pass and the top-speed pass each walked leader → physical group → scratch → smallest-entity-id identity → handled set, and the two `Handled` summaries disagreed — one said *keyed by the grid that led them*, which is not what either copy does. `GridGroups.TryClaim` states the mechanism and the id convention once, so a later pass — lift is on the roadmap — cannot pick a different convention and handle every group once per member. | The drift had already reached the prose, which is how the code drift starts. Session-only server path, so the check is `C11`'s: the `.slnx` build compiles the mod project, the helper uses only APIs the two copies already used, and each pass keeps its own scratch and handled set — the systems stay uncoupled. Suite 2,237 green (2026-09-04, commit `87956b2`). |
| 21 | **One loader for a dataset page.** `air.py`, `pairs.py` and `retest.py` carried an identical six-line `load(name)`; `scoring.load` holds it once with the contract stated — an empty list is for a dataset that legitimately lacks a page, and each tool still prints its own guidance when the page it cannot run without is the empty one. `verdict.py` keeps its richer loader on purpose: dropping and counting duplicate rows is a statement about its datasets, not about reading a file. | Verified by reproduction: all three tools' reports byte-identical on the 2026-08-23 datasets, and the loader pinned by a new test. 200 python and 2,237 C# green (2026-09-04, commit `a4cc077`). |
| 22 | **The summary page has one writer.** Seven tools wrote the `statistic,value,unit` page independently — and it is a contract, not a convention: the committed `summary-*.csv` artefacts are this page, `E5` makes figures quotable from it, and `verdict.py --baseline` reads it back. `scoring.write_summary` states it once, encoding pinned so the page does not depend on the machine's locale; each tool keeps its own closing print. | Pinned by a round trip through the `DictReader` that `verdict.py` uses, and verified by reproduction on six of seven tools — byte-identical summaries on the committed datasets. `knob.py` cannot run on either committed knob dataset: both fail its own shipped-level precondition, before and after alike (recorded below). 201 python and 2,237 C# green (2026-09-04, commit `1908294`). |
| 23 | **Five hand copies of a definition that already knows how to copy itself.** `BlockThermalProperties.Clone` (a `MemberwiseClone`) has existed in Core all along; `SoloBlockRig`, `BalanceLab`, `BalanceProfiles` and `MaterialOverrideTests` (twice) hand-listed the fields instead — and every copy had drifted: all five dropped `SolarAbsorptivity` and `HeatSourceWatts`, three also `ExcludeFromSimulation`. All five read `source.Clone()` now, with modified fields set after. | The drop was live in code and latent in results. The one lab that clones the shipped radiator — whose definition authors `SolarAbsorptivity 0.1`, the selective surface — is the sensitivity table, and it runs in deep space precisely so sun cannot confound it, so no published figure carried the loss. Every pinned scenario conclusion holds, and a field added to the definition tomorrow is copied by construction, which no reflection pin could promise better. Suite 2,237 green (2026-09-04, commit `57700e6`). |
| 24 | **The dead-world control, stated once.** Nine test classes declare a `private static ThermalSettings Isolated()`; four of them — the coolant and heat-source rigs — were byte-identical: environment, sun, friction and damage off, under one shared name. `Isolation.DeadWorld` states it once and the four keep their name as a one-liner over it. | The other five declarations are deliberately different — a pinned pace with its own comment, a substep ceiling, waste heat off — and stay beside their experiments, where a reader can see the choice: those are deviations, not copies, by the standing changing-one-implies-changing-the-other test. Suite 2,237 green (2026-09-04, commit `f35712f`). |
| 25 | **The pair grids' prologue, and a check one grid lacked.** `PairSweep`'s three grids each restated the opt-in guard, the ship-set read and the battery index — and had drifted the way `E8` warns: two asserted their scenario names exist in the battery, the load grid did not, while `Run` skips an unknown name silently. `Prologue` is the one statement; the name check lives in `Sweep` now, where no pass can be written without it. | The consolidation *found* the gap rather than merely tidying: a misspelled load scenario would have thinned that dataset without an error. The opted-in walks are hours and are not run for a prologue extraction; the suite exercises the compile and quiet-skip paths. Suite 2,237 green (2026-09-04, commit `9cb9b39`). |
| 26 | **One resolver from scenario names to battery cases.** The air, cap and floor walks each resolved `PairLab.AirScenarios` against the battery in a near-copy differing only in the walk's own name — which is now the argument to `ScenarioIndex.Resolve`. It asserts on a name the battery lacks, for iteration 25's reason. The air walk keeps its `Ceiling` mapping at its own call site: a deviation, not a copy. | Suite 2,237 green (2026-09-04, commit `9f45b97`); the walks' opted-in bodies are unchanged and the quiet-skip path is what the suite runs. |
| 27 | **The doored shell is one fixture.** Four test classes hand-built the same 6×6×6 shell around a 4×4×4 pocket with one slide door at (2, 1, −1) — iteration 6's rig class again: a shell with a misplaced door still builds a room, so a sealing test against the wrong fixture fails for the wrong reason and passes. `RoomFixtures.DooredShell`/`AddDooredShell` state it once, and `DoorCell` is a named constant. `RoomMapSnapshotTests` keeps its second sealed pocket as its own addition. | One process note, kept because the trap is documented and still caught me: the branch's first test run was `--no-build` against a DLL the broken build had left behind, and it passed — void, per the stale-test-build note in development.md. The recorded figure is from the run after a real rebuild. Suite 2,237 green (2026-09-04, commit `7903e56`). |
| 28 | **One snapshot of a grid's temperatures, and one way to put it back.** `SolverAb`, `ClientDriftLab` and `ClientInputLab` each carried an identical `Temperatures(simulation)`; the labs also shared an identical `Restore`. `GridState` holds both, with `Restore`'s contract stated: it writes only `Temperature` because that is the one value a host may write from outside a step — what loading a saved world does. `SolverAb` keeps its public name as the A/B contract and delegates. | The deletion produced `R14`'s defect live: the drift lab's `Restore` summary was left orphaned over `MaxDifference`, caught in the same diff, and the summary now lives on `GridState.Restore` where its subject is. Suite 2,237 green after a real rebuild (2026-09-04, commit `beb3e60`). |
| 29 | **Cleanup 6, finished.** Two hand-derived pumpless rings outlived the consolidation that was built for them — one in `CoolantLoopTests`, and one in `CoolantFaultTests`, the very file iteration 6's record says was consolidated. Both read `PipeFitter.BuildPumplessRing` now, which also validates the ring instead of trusting it. | Iteration 10's words apply verbatim: a half-done consolidation leaves two conventions and a reader cannot tell which is current. The loop and fault tests the rings feed assert on the built ring's behaviour, so a fitter that built a different ring would fail them. Suite 2,237 green (2026-09-04, commit `446cfb7`). |
| 30 | *Nothing changed* — the three shapes examined and left alone, [above](#three-shapes-examined-in-the-third-pass-and-left-alone), and the pass closes. | A call is not a constant pair, a convention is not a definition, and a clean dead-scan is a result worth writing down so the next pass does not redo it. |
| 31 | **The aero overlay's declared mirror had broken where a player can see it.** `AeroOverlay.SumGroup` said it mirrors `ThermalGridDrag.ApplyToGroupOf` *without applying anything* — and the copy did not know the anchored-group veto, so a base standing in wind drew drag and lift arrows the server never applies, against the overlay's own promise of showing the force applied rather than a second opinion. `AeroGroupForces` holds the one summation and the one anchored test; the overlay zeroes its force arrows for an anchored group and names *anchored* in the red gate line, which configuration.md's gate list now includes. | The veto stays the applier's policy, separate from the sum: the drag pass bails on it before touching a thermal adapter (cheaper than the old mid-walk bail), and the overlay still sums the group so its centre-of-mass marker survives. Two accepted deltas stated in the commit: component-wise rather than per-grid float summation order (last-bit, multi-grid groups only), and a redundant flag removed with its equivalence argued. Session-only paths: verified by the `.slnx` build (`C11`), no new API (`C2`), suite 2,237 green after a clean rebuild (2026-09-04, commit `fca3e83`). |
| 32 | **The two clamp lists, and the thousandfold drift between them.** Fourteen fields are clamped by both the world's `Settings` and the solver's `ThermalSettings`; `SuitHeatCapacity`'s floor was `1f` in one and `MinimumThermalMass` — 0.001, a constant about zero-mass blocks — in the other, so the suite tested a floor no world runs (`R8`'s bridge gap, in the clamp direction). One constant now, `ThermalSettings.MinimumSuitHeatCapacity`, referenced by both. `TheTwoClampListsAgreeOnEveryFieldTheyShare` holds the pair together from here on. | The new check reads both clamp bodies as text — the way the rest of `SettingsWiringTests` reads what it cannot link — refuses a scan that matched under ten shared fields (`E8`), and failed on the deliberately reintroduced drift before being believed, on exactly the one field. Suite 2,238 green — one test added (2026-09-04, commit `8531848`). |
| 33 | **One statement of which vents are worth asking.** `ThermalGridRoomDiagnostics` read its vents through three loops that each restated the same filter — live, still on the grid, opening onto these cells. `LiveVentOn` states it once; each reader keeps its own `try/catch`, whose telemetry names the reader. | A liveness rule added to two of three copies would have left the diagnostic disagreeing with itself about which vents exist. Session-only path: `.slnx` build (`C11`), identical checks in the original order, suite 2,238 green (2026-09-04, commit `8fe58bf`). |
| 34 | **One CLI flag read, six declarations, two behaviours.** `core`, `cruise`, `dragfit` and `shape` indexed one past the flag unguarded — a command line ending in `--csv` with no value was a traceback; `verdict` and `knob` bounds-checked and fell back, and `cap` and `floor` carried the checked form inline. `scoring.flag` is the one definition, in the checked form, with an `argv` parameter so its test patches nothing. | The `number` story on the command line: one mechanism, drifted copies, and the drifted form's failure is loud in the wrong way. Demonstrated live — `shape.py` with a dangling `--csv` finishes its report instead of dying. Six tools' summaries byte-identical; 202 python and 2,238 C# green (2026-09-04, commit `cafa47d`). Also observed, pre-existing: `core.py --score` fails on the 2026-08-21 dataset with a `TypeError` from a `None` percentile, byte-identical either side of this change — recorded below with `knob.py`'s. |
| 35 | **One positional parse: eight statements, five behaviours, two latent faults.** Four tools kept the bare not-a-flag comprehension, so `tool.py --csv out.csv` read `out.csv` as its dataset — caught live in iteration 22's own reproduction runs. Four grew value filters in three spellings: `core`'s crashed on a dangling flag, `knob`'s declined to filter an empty value, and all four would drop a positional that equals some flag's value. `scoring.positionals` skips a value flag's token by position; each tool names which of its flags take a value. | Pinned at every fault the copies had among them, and demonstrated live — `cruise.py --csv X` now falls back to its default census instead of reading `X` as one. Six tools' summaries byte-identical; 203 python and 2,238 C# green (2026-09-04, commit `0bcba38`). |
| 36 | **`Clamp01`, stated once instead of ten times.** Ten character-identical private copies (up to the parameter name) across the solver, environment pass, pumps, definitions and overlays. `ThermalMath.Clamp01` in Core is the one statement, with the NaN pass-through named as the contract — a clamp is not a validity check. | The hot-path users carry real verification: the bit-identity suites (`D8`) run over the conduction clamp and environment rows, and the settled-step allocation gate (`C4`) sees a static call that allocates nothing. No deletion orphaned a comment — checked before deleting, per iteration 28's lesson. Suite 2,238 green (2026-09-04, commit `17e8a11`). |
| 37 | **The banned percentile form crept back.** `scoring.percentile` exists because there were two definitions and `values[int(q·n)]` is not a percentile on small samples — its own docstring says so. `provenance.py` declared that form again, in a tool the original consolidation never touched. It reads `scoring.percentile` now. | The delta is the finding: the JumpDrive console report moves in exactly one place, p99 93.2 → 93.1 — the tail, where the index form errs. Nothing published quotes the old figure: the console report is the only consumer, `summary_rows` never used it, and the cap summary reproduces byte-identically. 203 python and 2,238 C# green (2026-09-04, commit `4d190d5`). |
| 38 | **The straggler sweep, and the worse instance of 37's find.** Sweeping every past consolidation for surviving copies of its banned form found `verdict.py`'s own `population blocks p99` — a row the documentation quotes — still computed as `values[int(0.99·n)]`, the exact copy `scoring.percentile`'s docstring says was consolidated away. It reads `scoring.percentile` now; the definition changed in a commit carrying no dataset (`E11`), moving the 2026-08-21 summary in exactly one row: p99 70,141 → 70,095, 0.066 %, definitional, with the reason in a comment where the row is made. The quoted 70,141 stays as the committed record of the run that made the `G5` decision. Also finished iteration 8's row identity: `verdict.KEY` and `retest.KEY` alias `scoring.ROW_KEY`, and `cap.py`/`floor.py` build their arm keys through `scoring.key_of`. | The key consolidations are byte-identical on all three tools' outputs; the p50 lands identically under both definitions on this population. 203 python and 2,238 C# green (2026-09-04, commit `663d108`). |
| 39 | **The pair grid's cell identity, stated once.** `pairs.py` declared `cell_key` and `air.py` inlined the same tuple — the identity joining one pair-walk document's rows to another's, stated twice for two documents read side by side, exactly where a drifted key is silent. `scoring.pair_cell` owns it, with crash-on-missing-column kept and argued: a pair dataset that cannot say which cell a row is from is unreadable, and loudly is the only honest way to be unreadable. | Both tools' reports byte-identical on the 2026-08-23 datasets. 203 python and 2,238 C# green (2026-09-04, commit `d9e7b3e`). |
| 41 | **The straggler sweep, run as the pass-opener it was prescribed to be — five fixes and a live break.** `core.py` read `--score`'s value raw (34's crash form); `typical.py` kept the bare comprehension (35's); `load.py` carried a fourth page loader (21's); `cap.py` and `floor.py` shared an identical load-or-die contract, now `scoring.load_required`. **And `load.py` had been broken since 2026-08-29**: cleanup 7 deleted `pairs.number` while `load.py` imported it, and the tool would not import for six days across four green passes — the python twin of the first pass's recorded C# lesson, because nothing imports the tools. | The fix is proven by reproduction: the repaired tool's report on `out/load-2026-08-23` is byte-identical to the last working version's (`bf647d9^`). `knob.py` keeps its own loader for its own guidance message — the caller's half of `load`'s contract. cap, floor, panel, typical byte-identical. 203 python and 2,238 C# green (2026-09-04, commit `f018f3c`). The gate that closes the import class is iteration 42. |
| 42 | **The import gate.** Nothing imports the tools — several run a report at module level — so the gate parses instead: every tool under `tools/corpus` and `tools/lanes` must compile, and every from-import naming a local module must name something that module binds at top level. Refuses to pass when it matched almost nothing (`E8`); its limit — from-imports, not uses — is stated on it. Proven against the deliberate reintroduction of `load.py`'s exact break before being believed. | Two process notes, both owned in the history: commit `dce7757` landed with the C# suite red, because `EveryToolIsNamedByItsReadme` — the repository's own reachability guard — refused the gate the moment it was added, and the failure was masked in the terminal by a pipeline whose exit status was `tail`'s rather than `dotnet test`'s. The README names the gate now. 204 python and 2,238 C# green (2026-09-04, commit `6f2956d`). |
| 43 | **`test_lanes.py` was run by nothing.** `D2`'s class in the test tooling itself: built, named by its README, and reached by no command — the documented discovery starts at `tools/corpus`, and `discover -s tools` finds zero tests and exits OK, a discovery that judged nothing, passing. The corpus discovery bridges to the lanes suite now, refusing to pass if it shrinks to nothing, proven by a sabotaged lanes assertion the bridge reported by name. `lanes.py`'s bare positional comprehension stays, with the reason: no value flags, so 35's leak cannot occur, and importing corpus `scoring` across tool families couples two independent tools for nothing. | One operational note: proving the bridge left a stale `__pycache__` serving sabotaged bytecode after a same-size restore — the python twin of the stale-DLL note — and the suite stayed red until it was cleared. 205 python green (the bridge adds one and reaches ten more); 2,238 C# green with the exit code read directly (2026-09-04, commit `0b5397a`). |
| 44 | *Nothing changed* — the sweep's tail triaged into [standing exclusions](#the-straggler-sweeps-standing-exclusions), so the next pass's sweep starts from a list instead of re-deriving each verdict. | An oracle keeps its old spelling because the old spelling is what makes it evidence (`D8`); a tool with no value flags cannot leak one; a caller's guidance stays with the caller; and a shared word is not a shared contract. |
| 45 | **One CSV split, and the reader that mangled quoted names is gone.** Three readers split CSV lines; `ShipSet` and `PerformanceReport` honoured the doubled-quote escape the writers produce, and `BlockTriageLab`'s naive quote-toggle silently dropped it — a ship name carrying a quote parsed differently depending on which lab read it. `CsvLine.Split` in the harness is the one statement; all three delegate. | The writer–reader round trip is pinned beside `CorpusRecord.Text` at exactly the field the drifted reader got wrong: a name with a quote and a comma comes back as itself. Suite 2,239 green — one test added (2026-09-04, commit `62a48ec`). |
| 46 | **The labs' column trim (×3) and nearest-rank percentile (×2), each stated once.** `LabText.Trim` and `LabStats.PercentileOfSorted`; five sites delegate. `LabStats` names its definition boundary in its own summary: nearest rank at `fraction·(n−1)`, *not* the interpolated percentile `scoring.py` publishes population figures with — lab consoles on one side, published pages on the other, and a figure that crosses goes through the corpus tooling. | Character-identical bodies consolidated without definitional change. Suite 2,239 green (2026-09-04, commit `c485583`). |
| 47 | **The Sim runner's command line, read one way.** `Program.ValueAfter` ≡ `CorpusFetch.Value` and `Program.HasFlag` ≡ `CorpusFetch.Has` — the C# twin of the six python `flag()` declarations, caught before it drifted rather than after. `Cli.Value`/`Cli.Has` are the statements; the four private names stay as one-line delegates (`ValueAfter` alone has forty-four call sites). | Character-identical, bounds-safe bodies; `SimCommandTests` covers the dispatch that walks them. Suite 2,239 green (2026-09-04, commit `a7d5dc8`). |
| 48 | **The CSV text escape, declared beside the split that honours it.** Six sites stated the always-quote idiom independently — `CorpusRecord.Text`, `PerformanceReport.Quote`, a `BlockHeatIndexTests` helper, and five inline appends across three labs. `CsvLine.Text` sits next to `CsvLine.Split`: the writer and reader of one escaping in one file, `D3`'s cure applied to both halves, with iteration 45's round trip now running through both. | `CorpusFetch.Csv` deliberately stays its own — minimal quoting plus newline scrubbing is that artefact's policy, not this escape. Suite 2,239 green (2026-09-04, commit `adbaeb4`). |
| 49 | **The straggler sweep is a test now — `ConsolidationTests` — and it found a seventh escape on run one.** Each consolidated definition's banned form must appear in its owner (a case whose owner lost the pattern is checking nothing, and fails) and nowhere else, with the standing exclusions as in-place exemptions. First run: it caught `TelemetryFormat.Quote` — a seventh CSV-escape site iteration 48's `tests/`-only grep missed, in shipped code that *cannot* reference the harness — and its own owner-check caught this test's first draft naming a literal the `IntoFaceMetres` constant had replaced. | The pair that cannot be one definition is held by a cross-parser round trip instead: `TelemetryFormatTests` pins that what the shipped writer quotes, `CsvLine.Split` parses back, empty field included (`D3` across a project boundary). `EveryTestClassIsInTheIndex` refused the class until the README filed it. Suite 2,241 green — two tests added (2026-09-04, commit `96ec7d4`). |
| 50 | *The pass closes.* Its opening prescription — run the straggler sweep by hand at the top of every pass — retired itself: the sweep is `ConsolidationTests` and `test_tool_imports.py` now, so the next pass opens by reading their exemption lists instead of re-running greps. | A sweep run by hand is a sweep that is eventually not run; the sixth pass inherits it as two suite members and a standing-exclusions section. |
| 40 | *Nothing changed* — the fourth pass's examined-and-left-alone shapes and the one question only a game session can settle, [above](#shapes-examined-in-the-fourth-pass-and-left-alone), and the pass closes. | An assembly convention is not a pipeline, a shared name is not a shared contract, and an in-game fact is recorded as a question rather than asserted from the lab. |

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

### The cleanup broke the mod build and 2,141 historical test cases did not notice

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

### Two voxel walks that share an algorithm and not a definition

`SunShadowMap.BlockedBySelf` and `VoxelWalk.Blocked` both step a ray through grid cells by
Amanatides and Woo, and a scan for duplicated blocks flags their inner loops as copies. They are
not one thing twice:

* **Different precision for a stated reason.** The self-shadow walk is `float` from an integer air
  cell whose first boundary is always half a cell; the occluder walk is `double` from an arbitrary
  point another grid's transform produced, where the origin can be kilometres from the box and
  `float` steps would land between cells.
* **Different bounding for a stated reason.** The self-shadow walk keeps walking while outside the
  box — its cells are the hull's skin, so most rays *start* outside and may re-enter along a
  flank — where the occluder walk computes box entry and skips ahead, because its origin can have
  kilometres to cross.
* **Both are pinned to an oracle independently** (`E7`): the self-shadow walk against the analytic
  ray-versus-cube reference in `SunShadowTests`, the occluder walk by `GridShadowTests`.

Merging them means either promoting the per-air-cell hot walk to `double` — a cost and a bitwise
behaviour change on the exposure path, judged only through a held-window bench (`M7`) and a
bit-identity pin (`D8`) — or a mode flag that makes one function carry two contracts. The test
this page applies to constants applies to code too: changing one walk does not imply changing the
other, so they stay two. What guards the pair is what already guards them — each walk's own
oracle test fails if its behaviour drifts.

### Three shapes examined in the third pass and left alone

* **The A/B suites' arrange expression.** Four bit-identity suites spell out
  `EnvironmentSolver.Solve(whole.Settings, whole.Planet, Worlds.Ab.MildAtmosphere())`. The sample
  and the solver are each already one definition; what repeats is a call, and a wrapper for a call
  is indirection bought with nothing — there is no constant pair to drift.
* **The overlay `Cycle`/`Set`/`Announce` trio.** `ThermalDebugView`, `WindOverlay` and
  `AeroOverlay` share the pattern over *their own* `Mode` enums, and each `Announce` names its own
  modes. Merging needs generics over enums or an int-mode indirection in shipped C# 6 code; the
  shared thing is a convention, and a convention is kept by reading, not by a type parameter.
* **A dead-scan of the harness's public surface came back clean.** `UncalledCodeTests` covers
  private helpers; a name-frequency sweep over every `public`/`internal` harness method found none
  mentioned only at its declaration (2026-09-04, at commit `2f4c195`). A clean result is a result:
  the labs' reachability guard (`SimCommandTests`) is doing its job on the public side too.

### The straggler sweep's standing exclusions

The fifth pass ran the sweep as its opener (iteration 41) and these are the hits a future sweep
will re-flag and should skip, each with the reason it is not a straggler:

* **`TwoDictionarySurfaceMap`'s hand face frames** (`SurfaceMapPackingTests`). The class *is* the
  old code, kept verbatim as the oracle the packed map is compared against — its own summary says
  so — and `D8` is precisely why an oracle does not get modernised to `BoxGeometry.Span`: the old
  spelling is what makes it evidence.
* **`lanes.py`'s bare positional comprehension.** No flag of that tool takes a value, so
  iteration 35's leak cannot occur, and importing corpus `scoring` across tool families would
  couple two independent tools for nothing.
* **`knob.py`'s own page loader.** Its guidance message names which sweep to run — the caller's
  half of `scoring.load`'s contract, kept where the caller is.
* **`reproduce.py`'s `key` and `compare`.** Same words as `scoring.key_of` and
  `scoring.compare`, different contracts: its key normalises the cap arm so an unpaired walk keys
  like a control (its own documented decision), and its compare is a per-row tolerance check, not
  a summary diff. Same-word near-misses, like the three C# `Percentile`s.

### Shapes examined in the fourth pass and left alone

* **The two report pipelines.** `build-report.sh`/`build-bench.sh` and their six HTML templates
  share an eight-line assembly convention and nothing else — the corpus report and the balance
  bench are different documents by design, and a parameterised assembler would trade two readable
  scripts for one with two modes.
* **Three C# `Percentile`s that share a name and not a contract.** `TimeToLossTests` takes an
  integer per cent, `ShipProfile` a fraction, and `BuildCostLab` computes the inverse — the rank
  of a value. The same-word near-miss the first pass documented for `SinkGroup`: changing one does
  not imply changing the others, and none of their figures crosses into another's document.
* **The two python `median` one-liners.** `pairs.py` answers an empty series with `None` and
  `retest.py` with `nan` — each tool's own absence answer over a mechanism that is already one
  definition (`statistics.median`), which is the `number_or` precedent exactly.
* **The overlay `Cycle` trio and the register pattern** were re-confirmed as conventions, not
  definitions (third pass, entry above).

### An asymmetry only a game session can settle

`Session.UnloadData` unregisters four of the five things `LoadData` registers;
`SettingsSync.Register(this)` has no counterpart, and `SettingsSync.synced` is a static that is
never nulled. Whether that matters turns on whether mod statics survive across world loads in one
game process — if the script assembly is rebuilt per world, the guard `if (synced != null) return`
is only a same-session re-entry check and the asymmetry is harmless; if statics survive, the
second world of a session keeps a `NetSync` bound to a dead session and settings sync goes dark.
The tree's own design assumes per-world statics everywhere (`ThermalGrid.LiveGrids` would leak
grids between worlds otherwise), so harmless is the likely answer — but it is an in-game fact
(`E2` names the class), nothing in the lab can establish it, and it is recorded here as a question
for a session rather than asserted either way.

### Two tools whose committed inputs fail them

Both found while reproducing an iteration's outputs, both pre-existing, and both the same shape:
`D2` one step out — a tool that is built, documented, and runnable against nothing this machine
holds. Whether each is a dataset that predates a column or a tool that outgrew its datasets is a
question about the sweeps, not about a cleanup pass, so they are recorded here rather than fixed
in passing.

* **`core.py --score out/corpus-2026-08-21`** dies with a `TypeError`: `weighted_percentile`
  returns `None` — a column the scorer needs that the dataset does not carry — and the print
  formats it as a number (found 2026-09-04; the traceback is byte-identical either side of
  iteration 34's change).

### `knob.py` cannot run on either committed knob dataset

Found while reproducing iteration 22's writers: `knob.py out/knobs-2026-08-30` exits with
*environment has no row at its shipped level, so nothing says what it moved from*, and
`out/knobs-2026-08-21` the same — its own precondition, failing on every knob dataset this machine
holds, before the iteration's change and after it alike. That is `D2`'s shape one step out: a tool
that is built, documented, and runnable against nothing present. Not investigated further here —
whether the datasets predate the `shipped` column or the environment knob genuinely lacks a
shipped-level row is a question about the knob sweeps, not about this pass — but a tool whose
every committed input fails it is worth a line where somebody will read it.

### The room mapper's two run walks are specialisation, not duplication

`StepExternalRun` and `StepInteriorRun` read as near-copies to a scan, and the interior one's own
summary already answers it: *the same walk … and it has one thing to do that the external one does
not* — a cell it reaches is either air joining the room or sealed structure recorded as the room's
boundary, where the external walk counts cells and stores nothing. These are the loops passes 4
through 10 of the performance effort spent their time in; merging them puts a mode branch in the
per-cell body of the hottest stage the repository has, to remove a duplication whose two halves
already document their difference. Held by `RoomMappingNeverExceedsItsBudgetInOneTick` and the room
map's own comparison tests either way.

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
| 2026-09-04 | **Fifth pass closed: ten iterations, eight merged with fixes, and the sweep that opened it retired itself into the suite.** The opener (41) paid immediately: five stragglers of past consolidations, and a tool — `load.py` — that had been **unimportable since 2026-08-29**, broken by cleanup 7 itself and green through four passes because nothing imports the tools; fixed and proven byte-identical to the last working version's report. The class-closing gates followed: the python import gate (42), the lanes suite bridged into the one discovery command after being run by nothing (43), and the manual sweep promoted to `ConsolidationTests` (49), which found a seventh CSV-escape site on its first run and whose owner-check caught its own first draft. Between them: the sweep's verdicts became standing exclusions (44), one CSV split ended the reader that mangled quoted ship names (45), the labs' trim and nearest-rank percentile (46), the Sim runner's CLI pair (47), and the CSV escape declared beside its split with a cross-project round trip where one definition is structurally impossible (48). The pass opened at 2,238 C# / 203 python green (`3cc4f7c`) and closes at 2,241 C# / 205 python — five checks added. Three guards earned their keep against this pass's own work: `EveryToolIsNamedByItsReadme` and `EveryTestClassIsInTheIndex` each refused a new file until documented, and one commit (`dce7757`) is owned in history as red-for-one-commit behind a pipeline-masked exit code. |
| 2026-09-04 | **Fourth pass closed: ten iterations, nine merged, and the drifts got player-visible.** Merged: the aero overlay's broken mirror of the drag pass — a base in wind drew forces the server never applies — with one summation and the veto named as a gate (31); the two clamp lists' thousandfold `SuitHeatCapacity` disagreement, now one constant with a source-reading agreement test that failed on the deliberately reintroduced drift before being believed (32); one statement of which vents are worth asking (33); one CLI flag read, where four of six copies crashed on a dangling flag (34); one positional parse, whose leak had bitten this record's own iteration 22 (35); `Clamp01` stated once instead of ten times (36); the banned index-form percentile evicted from `provenance.py` (37) and then from `verdict.py`'s own quoted rows, found by sweeping every past consolidation for stragglers — p99 70,141 → 70,095, definitional, changed in a commit carrying no dataset (38); and the pair grid's cell identity (39). Recorded: four near-miss shapes and one asymmetry only a game session can settle (40). The pass opened at 2,237 C# / 201 python green (`b87d5e7`) and closes at 2,238 C# / 203 python — three tests added, pinning the clamp agreement, `scoring.flag` and `scoring.positionals`. The pattern of the pass: consolidations decay — three of this pass's finds were previous passes' consolidations with a surviving or re-created copy — so a straggler sweep belongs at the top of every future pass. |
| 2026-09-04 | **Third pass closed: ten iterations, eight merged, one iteration a set of recorded verdicts, and two prior consolidations finished.** Merged: one dataset-page loader (21), one writer for the `statistic,value,unit` summary page with both sides of its contract pinned (22), one clone for the block definition — where all five hand copies had already dropped fields (23), one dead-world control (24), one prologue for the pair grids — which found a missing scenario check in the load grid (25), one scenario resolver for the walks (26), one doored-shell fixture (27), one grid-state snapshot and restore (28), and cleanup 6 finished at last (29). Recorded rather than acted on: the A/B arrange call, the overlay cycle convention, a clean dead-scan of the harness's public surface (30), and the `knob.py` observation. Two drifts were live in code: the five clone copies (23, latent in results — the one affected lab runs sunless by design) and the load grid's absent scenario check (25). The pass opened at 2,237 C# / 199 python green (`75e48f0`) and closes at 2,237 C# / 201 python green — two python tests added, pinning `scoring.load` and `scoring.write_summary`. Twice during the pass a `--no-build` test run went green against a stale DLL from a broken build; both are recorded void where they happened, which is the stale-test-build note earning its keep. |
| 2026-09-04 | **Second pass closed: ten iterations, eight merged and two recorded as verdicts.** Merged: the census's drifted tool-list mirror (11), the duplicated game-install candidate list (12), the duration labs' shared stopwatch (13), the corpus tools' one default-taking cell read (14), the waste-fraction labs' one cache policy (15), the crosshair resolution (16), the sun oracle its own extraction had left behind (18), cleanup 4 finished in the shadow map (19), and the grid-group claim whose two comments had already split (20). Not changed, with the reasons above: the two voxel walks (17) and the room mapper's two run walks — both share an algorithm and not a definition. Every suite figure in the pass is dated and stamped with its commit; the baseline was 2,237 green at `358797b` and the pass ends 2,237 green, with one python test added (198 → 199). Two live drifts were found and closed by construction rather than described: `tool_n` counted the jump drive as a tool after the load model stopped doing so, and a `Handled` summary described a keying neither copy used. |
| 2026-08-29 | Opened. Two changes merged — one preamble for the API's grid accessors, and the three dead null checks against a `LiveGrids` that cannot be null — and one finding recorded rather than acted on: the nine unused `TryGet*` methods in the vendored `DefinitionExtensionsAPI.cs`, which stay because trimming a vendored client to the subset this mod happens to call turns every future update from a copy into a merge. |
