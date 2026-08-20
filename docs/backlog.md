# Backlog

Every open item across this repository's documents, condensed to one line each with a pointer to
where it is argued in full. Nothing here is new analysis — it is an index of decisions and work
that are already written down and not yet done, so a session can start from a list rather than a
re-read.

Status vocabulary: **defect** — the code is wrong; **decision** — nothing is wrong until someone
chooses; **work** — agreed, designed, unbuilt; **gap** — untested or unmeasured.

## A. Correctness defects, open

| # | Item | Where |
| --- | --- | --- |
| ~~A1~~ | ~~A face bolted to a non-sealing block counts as buried.~~ **Done** — the mount rejection is gone; the sealing test already covered every joint that should bury a face. | [known-issues.md](known-issues.md) |
| ~~A2~~ | ~~`RefreshBlock` rebuilds the whole conduction graph.~~ **Done** — it rebuilt nothing, leaving stale conductances, and charged a full remap. Now O(degree) relink, remap only when sealing moved. Build state remains deliberately unhooked. | [known-issues.md](known-issues.md) |
| ~~A3~~ | *(was: removal rebuilds the whole graph — already fixed before this pass. `RemoveNodeIncremental` walks the intrusive chains; the "still open" paragraph was stale.)* | — |
| A4 | ~~`SweepRoomPressure` unbudgeted.~~ **Measured and instrumented**; the vent fallback no longer fires for rooms the game does not seal, and a world without pressurisation skips it entirely. Measured at 0.054 % of real time on twelve compartments, so the rota is deliberately deferred until a station dump justifies it. | [known-issues.md](known-issues.md) |
| ~~A5~~ | ~~The step budget counts link visits but not node visits.~~ **Done** — measured, then renamed to `MaxElementVisitsPerStep` and counting `links + 4·nodes`. The unit change alone recalibrates the default to the 18 ms a step its documentation always claimed. | [element-cost.md](element-cost.md) |
| ~~A6~~ | ~~Convection reports 50 W/(m²·K) at zero air density.~~ **Done** — a reporting fault exactly as first diagnosed. The blend is applied at the transfer; only the reported figure was pre-blend. Telemetry now reports `EffectiveConvectionCoefficient`. | [known-issues.md](known-issues.md) |
| A7 | *(was `RecentlyRemoved` unbounded — already fixed: capped at 4096 in `ThermalGrid`.)* | — |
| A8 | *(was a variant group naming a subtype that does not exist — already fixed in `BlockVarientGroups.sbc`.)* | — |
| A9 | Solar occlusion by planets and asteroids is per grid, with no self-shadowing from a per-block implementation that exists commented out (M6). Currently listed as a deliberate limit; the code says otherwise. | [bugs-and-performance.md](bugs-and-performance.md#m6-solar-occlusion-is-all-or-nothing-per-grid--medium) |

## B. Player-facing gaps and decisions

| # | Item | Where |
| --- | --- | --- |
| ~~B1~~ | ~~Whole-grid heat venting in watts.~~ **Done** — `vented` and `made` accumulated on the hot path, shown as a pair in the cockpit panel (warned when made exceeds vented), in the telemetry report, and through `GetGridHeatBalance` in the mod API. No measurable cost. | [telemetry.md](telemetry.md) |
| B2 | The coolant pump's terminal on/off switch does nothing: loops circulate unpowered and with the pump off. Three options — make it stop the loop (a balance change), hide the switch, or document it as deliberate. | open decision |
| B3 | The settings menu cannot change anything from a multiplayer client; only the four presentation switches work. Blocked on B4. | [known-issues.md](known-issues.md) |
| B4 | No network replication. `SENetworkAPI` is initialised on channel `30323` with nothing registered; clients re-simulate and are never reconciled. Cosmetic divergence, but real, and a mid-session joiner starts from saved temperatures. | [known-issues.md](known-issues.md) |
| B5 | Radiators cannot be inline loop segments — no coolant ports of their own, so a loop cannot run through one. Adding ports needs the port geometry checked against the 1×5×2 model. | [known-issues.md](known-issues.md) |
| B6 | `blocks.md` should say much more loudly that the radiator is a block you **plumb**, not one you bolt: a bolt joint carries 167 W/K against a sink face's 1,000 W/K, and panels bolted to a saturated block make it *hotter*. | [balance.md](balance.md#the-finding) |

## C. Balance and tuning

| # | Item | Where |
| --- | --- | --- |
| C1 | **Reactors generate no waste heat.** `Cubes.xml` sets `ProducerWasteEnergy` 0, and a reactor delivers power through the source component, so every reactor in the game reports 0 W. | [balance.md](balance.md#open-items) |
| C2 | The `Cubes.xml` retune implied by the C1/M3/M4 fixes: radiators conduct at half their old rate, fast flight now heats the leading face, overheating destroys blocks 4× slower at `Frequency 4`. | [bugs-and-performance.md](bugs-and-performance.md#suggested-order-of-work) |
| C3 | Ship `MaxSubstepsPerBlock 6` / `MaxSubsteps 6`. Measured in the field: floored blocks 9.0 % → 1.03 %, drift 2.89 K → 1.07 K, for 1.63 % → 2.17 % of real time. The shipped defaults are still `MaxSubstepsPerBlock 0` / `MaxSubsteps 16`. | [field-tuning.md](field-tuning.md) |
| C4 | `Catalog` masses in the harness are up to 4× off the shipped definitions (Battery 1040 kg against 3,845; Thruster 10,000 against 43,200; Radiator 900 against 600). Every scenario temperature is quoted off them. | [balance.md](balance.md#open-items) |
| C5 | The underground core gradient sits behind a 2 km sea-level deadzone, deeper than SE's voxels reach, so every reachable depth reads a flat `UndergroundTemperature`. Whether the default should be a few hundred metres is a balance question. | [known-issues.md](known-issues.md), [planet-climate.md](planet-climate.md#open) |
| C6 | `AmbientLagSeconds` is 45 absolute seconds against a day that is not: it attenuates a four-minute day to 46 % and does nothing to a two-hour one. Wants expressing as a share of `MySectorWeatherComponent.RotationInterval`, if that type is reachable under the whitelist. | [planet-climate.md](planet-climate.md#open) |
| C7 | The 4 K/km lapse rate and the ground and convection offset tables are opinions chosen to look like Earth, not fitted. The honest fix is probably that ground offsets shrink as the lapse rate grows. | [planet-climate.md](planet-climate.md#open) |
| C8 | Scenario run lengths are fixed to the shipped clock, so 67 of 136 profile cells are still climbing when a run ends. Run lengths would have to scale with `HeatTimeScale`. | [profiles.md](profiles.md#known-limits-of-this-suite) |

## D. Performance and scale

| # | Item | Where |
| --- | --- | --- |
| D1 | A solver step is atomic and 104 ms at a million blocks — the largest single thing landing in one tick, and near the memory-bandwidth floor. Only activity tracking, chunking and multirate make it smaller; all designed, none built. | [scale-design.md](scale-design.md), [load-and-hitching.md](load-and-hitching.md#what-is-still-open) |
| D2 | The room map floods the bounding volume — 14× the block count on a hull — and takes 7,237 ticks (twenty minutes) to converge at a million blocks. Wants the host gas system, with a coarse flood as fallback. | [model-redesign.md](model-redesign.md) §4 |
| D3 | World load is 11 s at a million blocks in one call. Acceptable behind a loading screen; a blueprint pasted mid-session takes the same path. | [load-and-hitching.md](load-and-hitching.md#what-is-still-open) |
| D4 | The first step of a grid's life is 209 ms against a 24 ms median at half a million blocks — first touch of every flat array. A warm-up rather than a stutter, and still the largest number in the distribution. | [known-issues.md](known-issues.md) |
| D5 | Lumping (merge a stiff node into its dominant neighbour at topology time) — exact where the substep cap is approximate, and removes links as well as stiffness. Worth a few per cent on SE1 fleets; the design work is unlumping incrementally on change. | [stiffness.md](stiffness.md#2-lumping--the-same-idea-done-properly) |
| D6 | Multirate stepping, and implicit integration as the endgame. Both currently lose to the per-block cap on an SE1 fleet; both are the answer when the stiff set is not a thin tail, i.e. SE2. | [stiffness.md](stiffness.md#3-multirate-stepping--when-the-tail-is-not-thin) |

## E. Memory

Local changes, no design work behind them, ~20 % of a grid's footprint between them.

| # | Item | Saving |
| --- | --- | --- |
| E1 | Drop the solver's `nodesByKey` dictionary; `BlockInstance` carries the node index. | ~36 B/block |
| E2 | Pack the per-node face data — `int[6]` counts into one packed int, weights derived, sun-lit array only when self-shadowing is on. | ~70 B/block |
| E3 | Rooms as one cell array with per-room ranges instead of `List<HashSet>` plus a per-cell dictionary. | ~20 MB at 126k |
| E4 | Move the node diagnostics out of `ThermalNode` into a side array allocated when diagnostics are on. | ~24 B/block |
| E5 | Fold `blockSlots` into `blocksByKey` as one dictionary to a small struct. | ~30 B/block |

See [memory.md](memory.md#what-is-worth-doing-next).

## F. Testing and measurement gaps

| # | Item | Where |
| --- | --- | --- |
| F1 | The adapter under `Game/` is barely covered: the vent sweep, the terminal readout and the mod API's delegate table have no automated tests. The API's *shape* is checkable without a session. | [known-issues.md](known-issues.md#testing-gaps) |
| F2 | The heat pump's `MyResourceSinkComponent`, attached in code during `Init`, cannot be exercised offline — whether the grid's resource distributor picks it up is unverified. | [known-issues.md](known-issues.md) |
| F3 | Nothing in the climate model has been measured in game. The `depth_m`, `weather`, `weather_ambient_k`, `convection_coeff` and `game_temperature` columns exist so the next dump can check it. | [planet-climate.md](planet-climate.md#open) |
| F4 | No latitude spread and no weather in the field data: three sites spanning 7°–41°, one weather event in 851 s, no snow, sand or fog. A polar grid and `/weather SnowHeavy` would settle both. | [planet-climate.md](planet-climate.md#open) |
| F5 | The cost of terrain occlusion has never been measured in game; the `solar occlusion` timing in a telemetry report is where it would show. | [known-issues.md](known-issues.md) |
| F6 | A run confirming the new decorative-block definitions: uncapped demand is *calculated* to fall from 21.4 to ~5.6 substeps, not measured. | [field-tuning.md](field-tuning.md#done-the-lights-now-have-definitions) |

## G. SE2 and architecture

| # | Item | Where |
| --- | --- | --- |
| G1 | Block storage is still per cell — `GridModel.blocksByCell`, `SurfaceMap.states`, `BlockInstance.Cells`. The geometry and integrator are already box-based (`Se2LatticeTests` pins it); the *indexing* is the one thing between this model and an SE2 grid, where a 5 m block spans 8,000 cells. | [model-redesign.md](model-redesign.md) §2, [memory.md](memory.md) §8 |
| G2 | The room map must come off the block lattice for SE2: a thousand times the cells makes even a one-bit set gigabytes. | [memory.md](memory.md) §9 |
| G3 | Struct-of-arrays conversion of the remaining node state, mechanical once G1 lands. | [model-redesign.md](model-redesign.md) §6 |
| G4 | Five open SE2 questions answerable only in game: the shared lattice pitch, whether all eight block sizes coexist, the modder size cap, whether SE2 exposes rooms, and what `"CellSize": 64` means. | [se2-research.md](se2-research.md#7-open-questions-to-settle-in-game) |
| G5 | Whether a 10⁶-block grid is a real target or a stress bound — the scale design differs, and it is worth deciding before building. Wake storms, sleep thresholds and chunk size in metres hang off it. | [scale-design.md](scale-design.md#9-risks-and-open-questions) |

## H. Repository hygiene

| # | Item |
| --- | --- |
| H1 | Three fully-merged branches remain: `balance`, `comment-cleanup`, `coolant-flow-model`. None carries a commit master lacks. |
