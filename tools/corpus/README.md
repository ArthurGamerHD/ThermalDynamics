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

---

**Read the peak columns with the censoring in mind.** The harness never destroys an overheating
block, so anything above critical kept generating for the rest of the clock. See the deliberate
limit in [known-issues.md](../../docs/known-issues.md).

---

## Change log

| Date | Change |
| --- | --- |
| 2026-08-23 | Pruned the corpus directory from 68 GB to 32 GB and wrote down [what is in it](#what-is-in-the-corpus-directory-and-what-is-beside-it). 20.6 GB of leavings deleted, 1,840 barren blueprints and 8 oversized ones moved aside rather than deleted because `F14` still has the 25-block floor untested. The inference that a blueprint is barren was checked by parsing all 1,852 — 12 were usable ships and are back — which is also what added `corpus --list`. Fixed `Unpack`, which had silently skipped three legacy archives whose entry names are truncated. |
| 2026-08-23 | Wired the retest set to something: `ConductanceRetestWalk` runs it against the world before conductance became real units, and `retest.py` reads the result against G1, G2 and G5 as `verdict.py` already computes them ([backlog.md](../../docs/backlog.md) `C2`). |
| 2026-08-23 | Added `typical.py` and [the retest set](#the-retest-set): the panel picks extremes for a dial sweep, this picks the middle for a regression. |
| 2026-08-22 | Split the crossing from the loss everywhere the pages had run them together. The survey report's headline said a ship *loses its first block* after nine seconds where it meant *crosses critical*, its table's `First loss` column was the crossing and its `Blocks lost` column was blocks over critical, and the bench page repeated all three. `seconds_to_first_loss` is packed and shown beside the crossing, and reads as absent on every dataset collected before it existed. |
| 2026-08-22 | The resume is a record rather than a count. A walk writes `done-<walk>.txt` as it finishes each blueprint and reads it on the next start, so relaunching is the whole procedure and `THERMAL_CORPUS_SKIP` is gone ([backlog.md](../../docs/backlog.md) `H2`). Said that off is now spellable in `THERMAL_CORPUS_TESTS` every way anyone reaches for (`H3`). |
| 2026-08-22 | Wrote down [the five ways a full sweep dies](#the-five-ways-a-full-sweep-dies), which [balance.md](../../docs/balance.md) had been pointing at [backlog.md](../../docs/backlog.md) for and which no page in the tree carried — it had survived only as a note kept outside the repository, which is the failure [rules.md](../../docs/rules.md) exists to prevent. |
| 2026-08-22 | Added this change log. |
| 2026-08-22 | Made every recorded corpus figure read by something, and closed the pass. |
| 2026-08-21 | Opened the page against the 2026-08-21 datasets: what each script reads, what it prints, and why the panel's every pick names its own rule. |
