# Corpus tools

What to do with the CSVs a corpus sweep leaves behind. Everything here reads a data directory; the
run of 2026-08-21 is in `out/corpus-2026-08-21`, its census in `out/census-2026-08-21` and its dial
sweep in `out/knobs-2026-08-21`. None of those directories is committed — they are gigabytes — so
each of these prints what it found and renders without the parts that are absent.

> The rules argued here are stated canonically in [rules.md](../../docs/rules.md): `E5` `M10`.

```
python3 tools/corpus/verdict.py out/corpus-2026-08-21     # the criteria, on the terminal
./tools/corpus/build-report.sh out/corpus-2026-08-21      # the survey as one page
python3 tools/corpus/panel.py out/census-2026-08-21/census.csv \
                             out/corpus-2026-08-21/outcomes.csv   # rebuild the standing panel
./tools/corpus/build-bench.sh out/corpus-2026-08-21 \
                             out/census-2026-08-21 out/knobs-2026-08-21   # every dataset, one page
```

## The three walks over the whole population

`CorpusSurvey` runs five scenarios and every one of them is vacuum. `CorpusAirWalk` runs the four
[`PairLab.AirScenarios`](../../tests/Thermodynamics.Harness/PairLab.cs) — `vacuum-shadow` as the
anchor, then `surface-hot-noon`, `storm-parked` and `reentry` — because both halves of `G6` are
decided in air and the survey has never seen any (`F11`). `CorpusCapWalk` runs those same four
**twice**, with `MaxSubstepsPerBlock` off and at 6, which is `C3` and `G6`'s cost half as one
experiment. They are separate walks with separate resume records and separate data directories, so a
figure quoted from one is not silently a figure from a run of the other (`M1`).

```bash
THERMAL_CORPUS_TESTS=1 THERMAL_CORPUS_DATA=$PWD/out/air-2026-08-24 \
    THERMAL_CORPUS_PROGRESS=$PWD/out/air-2026-08-24/progress.txt \
    systemd-run --user --scope -p MemoryMax=24G -p MemorySwapMax=0 --quiet \
    dotnet test -c Release --no-build tests/Thermodynamics.Tests \
        --filter "FullyQualifiedName~CorpusAirWalk"

THERMAL_CORPUS_TESTS=1 THERMAL_CORPUS_DATA=$PWD/out/cap-2026-08-24 \
    THERMAL_CORPUS_PROGRESS=$PWD/out/cap-2026-08-24/progress.txt \
    systemd-run --user --scope -p MemoryMax=24G -p MemorySwapMax=0 --quiet \
    dotnet test -c Release --no-build tests/Thermodynamics.Tests \
        --filter "FullyQualifiedName~CorpusCapWalk"
```

**A paired walk costs about twice an unpaired one, and not because it runs twice.** Every run in
`CorpusAirWalk` stops at equilibrium, and in air that is usually two chunks of an 1,800-second
scenario; the capped arm cannot stop there, because it has to stop where its own control stopped
(`M1`). `THERMAL_CORPUS_SHIPS=40` walks a stride sample instead of the population, which is how to
see that a walk works before committing the hours — the sample for this one was 320 runs in 77 s.

**`THERMAL_CORPUS_DATA` must be absolute.** The test host's working directory is the test project's
output directory, not the repository, so a relative path writes the dataset somewhere nobody will
look for it. Everything else on this page applies unchanged: no hang timeout, one walk at a time, a
memory cap, and relaunch to resume.

---

## A figure a page quotes has a source in the tree

The datasets are gigabytes and are not committed, so until 2026-08-24 every corpus number in the
documentation was a number with no source anybody could check it against — and one of them drifted
by two orders of magnitude before it was compared back (`F14`: *twenty-four sealed blocks on one
ship* against a dataset saying 1,184 across 331).

`verdict.py --csv <path>` writes the figures it prints as `statistic,value,unit`. That file is
kilobytes and is committed:
[`summary-2026-08-21.csv`](summary-2026-08-21.csv) is the vacuum survey.

```bash
python3 tools/corpus/verdict.py out/corpus-2026-08-21 --csv tools/corpus/summary-2026-08-21.csv
python3 tools/corpus/verdict.py out/air-2026-08-24 --baseline tools/corpus/summary-2026-08-21.csv
```

`--baseline` prints what moved, which is how one walk is read against another — the same corpus in
vacuum and in air. It reports **every statistic on either side**, so one the other dataset does not
carry shows as an em dash rather than being dropped: a scenario present in one walk and absent from
the other is the finding, not a gap to be joined away.

**Read `dataset ships` first.** The corpus is sorted largest-first, so a walk stopped early is not
a small sample of the population — it is the wrong end of one, and every percentile it reports is a
percentile of the biggest ships in the corpus. A partial dataset carries exactly the columns a
finished one does. The count is taken from the outcomes rather than from `ships.csv`, which a walk
may legitimately not write.

---

`verdict.py` evaluates the criteria written down in [balance-lab.md](../../docs/balance-lab.md)
before the data was collected, so a run that fails them is a finding rather than an excuse to move a
threshold. It reports G1, G2, G5 and G6, says plainly that G3 and G4 are not answerable from this
dataset rather than skipping them, and then prints the three things the criteria do not cover: how
much of the population sits in the censored tail, how fast damage arrives, and which block types
drive it.

`build-report.sh` packs the same data with `pack.py` and assembles `shell.html` + `body.html` +
`script.html` into one self-contained page. The page defaults to the per-block critical temperatures
the run actually used; moving its threshold slider switches it to a single global threshold and says
so, because that is a proxy for re-tuning and not a re-simulation.

`panel.py` chooses the standing panel of ships the dial sweep runs on, because re-simulating
8,142 ships for every value of every dial is hours a dial. **Every ship is picked by a named rule
and the rule is written into `panel.csv` beside it**, so the panel can be audited and rebuilt
rather than trusted. `KnobSweep` reads that file, and reads each ship's blueprint path out of
`ships.csv` — without a path the sweep walks and fully parses all 9,981 corpus blueprints to find
fifty ships, with every worker opening quarter-gigabyte files at once, which is how earlier runs
died.

`build-bench.sh` is the whole-corpus counterpart to `build-report.sh`: it packs the survey, the
census, the composition and the dial sweep with `pack-bench.py` and assembles `bench-shell.html` +
`bench-body.html` + `bench-script.html` into one page. The sweep is optional and the page says
`pending` where it is absent, so the bench exists before the sweep finishes.

**Every figure a page states about its dataset comes from that dataset.** The bench's header
counts were written into the HTML by hand until the panel grew from 36 ships to 50 and the page
went on saying 36. A reader who catches one wrong count stops believing the right ones.

## The five ways a full sweep dies

A full sweep is about eight hours of compute. Five things went wrong on the run of 2026-08-21, all
of them avoidable, and all five cost time rather than data. Recorded here because a run that dies at
hour seven is the most expensive mistake this repository can make.

**Never set `--blame-hang-timeout` on a corpus test.** It killed a healthy run at exactly eight
hours. The survey is one test that legitimately runs longer than any timeout worth setting, and
VSTest cannot tell slow from deadlocked.

**Filter to the survey alone** — `--filter "FullyQualifiedName~CorpusSurvey"`. A broader filter pulls
in `SunlightPanelWalk`, which is 200 ships × 7 runs on a fixed 1,800 s clock, and under
`maxParallelThreads: 1` that blocks the deliverable for hours.

**Do not let the walks run concurrently.** Four walks × 31 workers is 124 threads on 32 cores, and it
measured **17× slower** than sequential — hidden behind a 93 % CPU reading, because the cores were
busy thrashing cache rather than working. `xunit.runner.json` is set to `maxParallelThreads: 1`;
leave it, and see [backlog.md](../../docs/backlog.md) F8 for the narrower form that would let the
rest of the suite run in parallel again.

**Watch what a `pkill` pattern matches.** `pkill -f "filter CorpusSurvey"` matched the relaunch that
had just started, killing the recovery along with the corpse. Use a pattern that cannot match the new
invocation.

**Cap the memory.** An uncapped run has taken the machine down with it; wrap it in
`systemd-run --scope -p MemoryMax=…` so the run dies instead of the session.

**Relaunch to resume; there is nothing to pass.** A walk records each blueprint in
`done-<walk>.txt` in the data directory once it has finished with it, and reads that record on the
next start, so a killed run picks up exactly where it stopped. Deleting the file starts the walk
over. This turned a lost eight hours into a seventeen-minute finish, and it replaced a skip counted
in files by hand: files were counted as they were handed to a batch while rows were written as each
ship finished, so a resume both re-emitted the interrupted batch and lost the ships it had not
reached. The 2026-08-21 dataset carries 50 duplicate rows from that; nothing collected since can.

**A run that has no record can be given one.** The resume reads `done-<walk>.txt`, and a dataset
collected before that file existed does not have one — but `ships.csv` carries each ship's blueprint
path, so the record can be rebuilt from it:

```bash
python3 -c "import csv,sys;[print(r['path']) for r in csv.DictReader(open(sys.argv[1]))]" \
    out/corpus-2026-08-22/ships.csv | sort -u > out/corpus-2026-08-22/done-survey.txt
```

One caveat, and it is the reason this is a recovery rather than the normal path: a blueprint holding
several ships that was interrupted part way through appears in `ships.csv` and would be skipped with
ships still to do. That is one file of nine thousand, against a whole run.

**Read progress in bytes, not files.** The corpus is sorted largest-first, so file 500 of 9,981 is
5 % of the files and 50 % of the work. `THERMAL_CORPUS_PROGRESS` reports both.

The environment variables a run takes: `THERMAL_CORPUS_TESTS`, `THERMAL_CORPUS_DATA`,
`THERMAL_CORPUS_PROGRESS`, `THERMAL_CORPUS_SHIPS` and `THERMAL_CORPUS_MAX_MB`. Off is spellable in
`THERMAL_CORPUS_TESTS` every way anyone reaches for — empty, `0`, `no`, `off`, `false` — which is
the standing rule [rules.md](../../docs/rules.md) `C8`, and was not true until `CorpusGuardTests`
existed to say so.

---

## What is in the corpus directory, and what is beside it

Pruned 2026-08-23, from 68 GB in 33,773 files to 32 GB in 8,144 blueprints. **Every blueprint that
has ever produced a ship is still there**, and the three directories say what each holds:

| Directory | Holds | Why |
| --- | --- | ---: |
| `corpus` | 8,144 `bp.sbc` | Every blueprint known to yield a usable ship. 32 GB |
| `corpus-barren` | 1,840 `bp.sbc` | Parsed and yields nothing: 1,742 rejected as modded, 53 under the 25-block floor, 3 the parser cannot read. 12 GB |
| `corpus-oversized` | 8 `bp.sbc` | Over `THERMAL_CORPUS_MAX_MB`, so a walk has never read them and a machine with room can. 4.5 GB |

**20.6 GB of it was the download's leavings and is deleted rather than moved**: 7,205
`.sbcB1`–`.sbcB5` autosave backups, 3,904 `_legacy.bin` archives, 6,209 thumbnails, 185 `.sbcPB`
script backups and a handful of image-editor files. Nothing has ever read any of them, and the
`.bin` were checked to be redundant rather than assumed to be — see below.

**The barren set is moved rather than deleted, and the reason is a rule this repository already
has.** Pruning them from disk would bake today's two filters — `IsVanilla` and the 25-block floor —
into the population, and [backlog.md](../../docs/backlog.md) `F14` records that the floor has never
been varied to see whether it moves a population figure. Moving costs a rename to undo; deleting
costs a re-download.

**And the inference was checked rather than trusted, which is the whole reason to keep this
paragraph.** The barren set was first identified by subtracting the paths in `ships.csv` from the
blueprints on disk — an inference from two runs, one of which predates the per-blueprint resume
record and could therefore have *lost* ships it never reached. Parsing the 1,852 said so: **12 were
usable ships**, and they are back in the corpus. `dotnet run --project tests/Thermodynamics.Sim --
corpus --list --path <dir>` is what names them, because a scan that answers *twelve* and cannot say
which twelve is an answer that has to be taken on trust.

**Three of the 3,904 legacy archives had never been unpacked, and nothing said so.** A blueprint
published before Steam's current UGC system arrives as a zip holding `bp.sbc` and a thumbnail;
three of them have lost the leading characters of their entry names — `p.sbc`, `.sbc`,
`humb.png` — and `Unpack`'s exact test for `bp.sbc` skipped all three silently. It matches the
extension now (`Blueprints.IsLegacyBlueprintEntry`, checked by `CorpusArchiveTests`), and two of
the three recovered blueprints are usable ships that had never been in any run.

---

## The retest set

`panel.py` picks extremes and `typical.py` picks the middle, and they answer different questions.

```
python3 tools/corpus/typical.py out/census-2026-08-21/census.csv \
                               out/corpus-2026-08-21/outcomes.csv
```

A dial sweep needs the largest lever it can find, so the panel carries the most buried hull, the
least surface per kilowatt and the hulls where one block type is most of the heat. **A retest asks
whether a change broke the ships people actually fly**, and a regression judged only on outliers is
a regression judged on hulls nobody built on purpose. `typical.py` writes `typical.csv`: 40 ships
that each carry thrust, power, an airtight room and 100 kW of waste at full load, that fit the same
30,000-block budget, and that sit nearest the population median on the four quantities the census
found decide an outcome — scored on their *worst* axis rather than their mean, because a hull median
on three and extreme on the fourth is not typical. Drawn across four size bands per grid size, so a
retest is not accidentally all frigates. 3,816 of 8,141 ships pass the six tests.

`ConductanceRetestWalk` is what reads it. The walk runs the set through seven scenarios in the
shipped world and in four counterfactual ones, each restoring part of `Data/Cubes.xml` as it stood
before conductance became real W/(m·K), and `retest.py` reads what comes back.

```
THERMAL_CORPUS_TESTS=1 THERMAL_CORPUS_DATA=$PWD/out/retest-2026-08-23 \
    dotnet test --filter "FullyQualifiedName~ConductanceRetestWalk"
python3 tools/corpus/retest.py out/retest-2026-08-23
```

Two hours on a 32-core machine, streamed per ship with a `done-retest.txt` beside the data, so
relaunching resumes and there is nothing to pass. It writes `reach.csv` before it runs anything —
how many blocks each arm actually retunes — because an arm that reaches nothing reports *no change*
in exactly the shape of an arm that reached everything and changed nothing, and one of the four
reaches nothing here by construction: the corpus filters admit vanilla ships, so not one of the
forty carries a coolant pipe or a radiator. Those two are measured on a rig instead —
`dotnet run --project Thermodynamics.Sim -- conductance`.

`PairSweep` reads the same set for a different question. `G8` — the significance window — is a claim
about two dials interacting, and every sweep before it moves one; this runs a grid of conduction
against the clock and `pairs.py` scores `G8` on every cell beside the criteria a cell must not break
to be usable.

```
THERMAL_CORPUS_TESTS=1 THERMAL_CORPUS_DATA=$PWD/out/pairs-2026-08-23 \
    dotnet test --filter "FullyQualifiedName~EveryPairOfConductionAndClockGetsAMeasuredCell"
python3 tools/corpus/pairs.py out/pairs-2026-08-23
```

**And the load pass, which is the third question the class answers.** It runs three load cases. `full-electrical`
charges every jump drive for the whole run and `full-electrical-charged` never charges one; drives
are 71.3 % of the corpus's full-load waste heat, so those two bracket what a loaded ship makes.
**`jump-charge` is the one `G8` is scored on**: a drive fills in 421.9 s — 3 MWh at 32 MW with 0.8
efficiency, every figure off the game's own definition — so the scenario charges for exactly that
and then holds, which is the event rather than a bound on it. Conduction and the clock are
both transport; `EveryLoadAndClockPairGetsAMeasuredCell` moves how much heat a ship makes against
the clock instead, and `load.py` scores the same criteria. Ten minutes on the retest set. It imports
`pairs.py`'s thresholds and censoring rather than restating them, because two scorers that drift
apart disagree about the same criterion silently.

```
THERMAL_CORPUS_TESTS=1 THERMAL_CORPUS_DATA=$PWD/out/load-2026-08-23 \
    dotnet test --filter "FullyQualifiedName~EveryLoadAndClockPairGetsAMeasuredCell"
python3 tools/corpus/load.py out/load-2026-08-23
```

**The same class carries the air pass, and it is a different question with a different filter.**
`G6` is a cost criterion and the grid above is three vacuum scenarios, where substeps are cheap;
the budget is spent on convection. `EveryCandidateCellIsPricedInAir` runs the shipped pair and the
four cells that satisfy `G8` through four atmospheric scenarios, and `air.py` scores `G6` on each.
Two minutes on the retest set, five on the panel — air runs are short because the scenarios are.

```
THERMAL_CORPUS_TESTS=1 THERMAL_CORPUS_DATA=$PWD/out/air-2026-08-23 \
    dotnet test --filter "FullyQualifiedName~EveryCandidateCellIsPricedInAir"
python3 tools/corpus/air.py out/air-2026-08-23
```

It was run on both populations on purpose. The two answer within 0.3 % of each other, which is what
says the `G6` breach it found is a property of the configuration rather than of a sample — the one
check available while [backlog.md](../../docs/backlog.md) `F11` stands and the corpus itself has
never seen air. Point `THERMAL_PANEL` at `tools/corpus/panel.csv` for the second population.

Under two hours for the first sixteen cells, resumable the same way, and the grid grew to
twenty-five in two searches guided by what the earlier cells measured. **Not a full grid**: the two
edges are the single-dial curves, so the interaction is testable as *is the interior what the edges
predict* rather than assumed; the interior is the equal-cost diagonal — substep demand goes as
conductivity × clock — plus the pair [balance.md](../../docs/balance.md) projected and the four
cells bracketing it; and the two corners are where the measured curves said a cell satisfying `G8`
would have to be. **Run length scales with the clock**, capped at twice `G8`'s own bound, because
a fixed ceiling reports a still-climbing hull as settled at the ceiling and a censored settling time
cannot fail a criterion about settling.

**A hull that never crossed critical is censored above, not dropped.** The first reading of this
grid took the crossing median over the hulls that crossed and reported two cells at conductivity ×8
as satisfying `G8` — where 27 of 40 hulls never reach critical at all. `pairs.py` now orders a
non-crosser past every crosser, exactly as it already ordered a non-settler past the recovery bound,
so a cell where fewer than half ever cross prints `censored` and has no median (`E9`).

**`provenance.py` weights a census by where its heat's numbers came from.** Every waste fraction in
`Cubes.xml` states a provenance ([definitions.md](../../docs/definitions.md#every-waste-fraction-says-where-it-came-from-and-most-of-them-say-invented)),
and counting those says how much of the *file* is sourced rather than how much of the *heat* is. It
reads a census `composition.csv` — per ship, per subtype, watts wasted at full electrical load — and
prints both, plus which block types carry the invented share.

```
python3 tools/corpus/provenance.py out/census-2026-08-21/composition.csv
```

Seconds, on a census that already exists. A block that generates power is weighted by its producer
fraction and everything else by its consumer one, which is the difference between a hydrogen engine
reading as sourced and as invented — six per cent of the corpus's waste heat.

```
python3 -m unittest discover -s tools/corpus -p 'test_*.py'
```

`test_scoring.py` pins that rule and the two thresholds `G8` is scored at, and `test_provenance.py`
pins the four provenance counts against the ones `AuthoredWasteTests` pins, so the two readers of
one grammar cannot drift apart quietly (`D3`). Changing either fails a check rather than moving a
number nobody is watching. They are the only checks over the scorers and are not part of the
`dotnet test` suite; run them when a scorer changes.

---

**Read the peak columns with the censoring in mind.** The harness never destroys an overheating
block, so anything above critical kept generating for the rest of the clock. See the deliberate
limit in [known-issues.md](../../docs/known-issues.md).

---

## Change log

| Date | Change |
| --- | --- |
| 2026-08-24 | **`CorpusCapWalk`: the same four air scenarios, run twice, with `MaxSubstepsPerBlock` off and at 6.** `C3` and `G6`'s failing cost half are one question — the cap is the only lever that lowers a step's work — and `C3` was undecided only because its cost had been measured on one hull. **Both arms run to the same simulated clock**, which is why it is a walk rather than a join onto `F11`'s dataset: `Battery.Run` stops at equilibrium, in air that is usually 120 s of 1,800, and the effect being measured is a hundredth of a kelvin against a stopping tolerance of a quarter of one. `Battery.RunForSeconds` takes the control's elapsed clock; `outcomes.csv` gains `run_seconds` and `cap` so a reader can see where a run stopped and which arm it is. |
| 2026-08-24 | **`scoring.step_work` takes the walk's `substep_cost` column, and the two committed summaries have lost their `G6 work` rows.** The unit is `links + 4 × nodes` and no walk before today recorded a link count; the scorer was handed `joints`, which counts rotors and pistons *between grids*, so the expression evaluated to `4 × nodes` and every step-work figure published from either dataset is the node half alone — 1.51× low on a 2,000-block census hull. `outcomes.csv` carries `links` and `substep_cost` now, the second being `ThermalSimulation.SubstepCost` for the assembly's worst grid, since the allowance is per grid. Neither existing dataset can be rescored, so both summaries were regenerated and their work rows are simply gone: a statistic that vanishes between two runs is what `--baseline` reports as a finding, which is the right reading here (`P2`). `G6` prints as one half unmeasured rather than as a failure, and the per-scenario table keeps its demand columns with an em dash in the work ones. |
| 2026-08-24 | `scoring.censored_median` is the one definition of the statistic `G8` is scored on, and `pairs.py` and `load.py` both call it — two copies of it had already drifted at the even-length boundary. Added `joint_median_stability`, the bootstrap that says how often a resampled fleet still satisfies a two-halved criterion, drawn once so the two medians stay paired; `load.py` prints it as a `holds` column. It is what decided `C12`: two cells with the same median were 37 % and 76 % likely to hold. |
| 2026-08-24 | **`THERMAL_CORPUS_DATA` is resolved against the test host's working directory, not the repository root.** A relative path writes a whole dataset into `.build/…/bin/Release/net9.0/out/` and the run passes, having recorded nothing where anyone will look for it. Pass an absolute path. |
| 2026-08-23 | Gave the load pass `jump-charge`, the case `G8` is scored on: the drives charge for the 421.9 s their own definition implies, finish, and the ship holds. `Battery.Scenario` gained `Then` and `ThenAfterSeconds` for it, which also generalises the name-matched special case `recovery` had been carried by since it was written. |
| 2026-08-23 | Gave the load pass its second load case, `full-electrical-charged`, which is what answers `F13`: with the drives full nothing in either of `C12`'s routes has a median crossing at all. The dial that makes it possible found a defect on the way — the jump drive was filed as a *tool*, so `State.Consumers` never reached it — and the change is neutral on 1,794 rows of the previous dataset. |
| 2026-08-23 | Added the load pass and `load.py`: the load against the clock, which is the dial that is not transport. Three grids share one run loop, one row format and one resume-record-per-pass now; the row format gained a `waste` column, and a cell that does not move the load keeps the name the two earlier grids wrote so their records still match the cells they were taken on. |
| 2026-08-23 | Added the air pass and `air.py`: the same cells `pairs.py` scores for `G8`, priced in the environment `G6` is decided in. It found the shipped configuration over the substep cap at 200 m/s and every atmospheric figure in [balance.md](../../docs/balance.md) exactly half, taken before `Frequency` went 8 to 4 ([backlog.md](../../docs/backlog.md) `C19`). The two sweeps share one run loop and one row format; what differs is which cells and which scenarios, and each keeps its own resume record so one cannot mark a ship done for the other. |
| 2026-08-23 | Scored `G8` and found it satisfied at conductivity ×4 with `HeatTimeScale` 80–120. Fixed the defect that had to be fixed first: `pairs.py` took the crossing median over the hulls that crossed, so conductivity ×8 — where 27 of 40 hulls never reach critical — read as the grid's best cell. Non-crossers are censored above now, `test_scoring.py` pins it, and `verdict.py`'s time-to-critical table says in words that its quantiles are over the ships that reached. |
| 2026-08-23 | Pruned the corpus directory from 68 GB to 32 GB and wrote down [what is in it](#what-is-in-the-corpus-directory-and-what-is-beside-it). 20.6 GB of leavings deleted, 1,840 barren blueprints and 8 oversized ones moved aside rather than deleted because `F14` still has the 25-block floor untested. The inference that a blueprint is barren was checked by parsing all 1,852 — 12 were usable ships and are back — which is also what added `corpus --list`. Fixed `Unpack`, which had silently skipped three legacy archives whose entry names are truncated. |
| 2026-08-23 | Added `PairSweep` and `pairs.py`: conduction against the clock, sixteen cells, scoring `G8` ([backlog.md](../../docs/backlog.md) `C12`). The three sweeps' CSV reading, blueprint resolution and resume record are one `ShipSet` now rather than three copies. |
| 2026-08-23 | Wired the retest set to something: `ConductanceRetestWalk` runs it against the world before conductance became real units, and `retest.py` reads the result against G1, G2 and G5 as `verdict.py` already computes them ([backlog.md](../../docs/backlog.md) `C2`). |
| 2026-08-23 | `verdict.py` prices a `G6` breach beside the verdict — the marker stays where it was written (`E11`), and a reader sees whether a failure is 0.03 K or a hull integrated wrong. The arithmetic moved to `scoring.py` so a test can call it without running a report. `air.py`'s footer no longer says a refused step floors a block's capacity; that mechanism ships off. |
| 2026-08-23 | Added `provenance.py` and `test_provenance.py`: how much of a fleet's waste heat rests on a fraction nobody sourced. [backlog.md](../../docs/backlog.md) `C21`. |
| 2026-08-23 | Added `typical.py` and [the retest set](#the-retest-set): the panel picks extremes for a dial sweep, this picks the middle for a regression. |
| 2026-08-22 | Split the crossing from the loss everywhere the pages had run them together. The survey report's headline said a ship *loses its first block* after nine seconds where it meant *crosses critical*, its table's `First loss` column was the crossing and its `Blocks lost` column was blocks over critical, and the bench page repeated all three. `seconds_to_first_loss` is packed and shown beside the crossing, and reads as absent on every dataset collected before it existed. |
| 2026-08-22 | The resume is a record rather than a count. A walk writes `done-<walk>.txt` as it finishes each blueprint and reads it on the next start, so relaunching is the whole procedure and `THERMAL_CORPUS_SKIP` is gone ([backlog.md](../../docs/backlog.md) `H2`). Said that off is now spellable in `THERMAL_CORPUS_TESTS` every way anyone reaches for (`H3`). |
| 2026-08-22 | Wrote down [the five ways a full sweep dies](#the-five-ways-a-full-sweep-dies), which [balance.md](../../docs/balance.md) had been pointing at [backlog.md](../../docs/backlog.md) for and which no page in the tree carried — it had survived only as a note kept outside the repository, which is the failure [rules.md](../../docs/rules.md) exists to prevent. |
| 2026-08-22 | Added this change log. |
| 2026-08-22 | Made every recorded corpus figure read by something, and closed the pass. |
| 2026-08-21 | Opened the page against the 2026-08-21 datasets: what each script reads, what it prints, and why the panel's every pick names its own rule. |
