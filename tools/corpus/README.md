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

**Resume with `THERMAL_CORPUS_SKIP=<n>` rather than starting again.** Corpus order is deterministic —
sorted largest-first — and rows are written per ship, so a killed run resumes by skipping what is
already done. This turned a lost eight hours into a seventeen-minute finish. Note `H2` in
[backlog.md](../../docs/backlog.md): the skip counts *files* while the writing is per *ship*, which
is why an interrupted batch re-emits and the shipped dataset carries 50 duplicate rows.

**Read progress in bytes, not files.** The corpus is sorted largest-first, so file 500 of 9,981 is
5 % of the files and 50 % of the work. `THERMAL_CORPUS_PROGRESS` reports both.

The environment variables a run takes: `THERMAL_CORPUS_TESTS`, `THERMAL_CORPUS_DATA`,
`THERMAL_CORPUS_PROGRESS`, `THERMAL_CORPUS_SKIP`, `THERMAL_CORPUS_SHIPS` and
`THERMAL_CORPUS_MAX_MB`. **`THERMAL_CORPUS_TESTS=` with an empty value opts *in*** rather than out,
which is `H3` on the backlog and the standing rule [rules.md](../../docs/rules.md) `C8`.

---

**Read the peak columns with the censoring in mind.** The harness never destroys an overheating
block, so anything above critical kept generating for the rest of the clock. See the deliberate
limit in [known-issues.md](../../docs/known-issues.md).

---

## Change log

| Date | Change |
| --- | --- |
| 2026-08-22 | Wrote down [the five ways a full sweep dies](#the-five-ways-a-full-sweep-dies), which [balance.md](../../docs/balance.md) had been pointing at [backlog.md](../../docs/backlog.md) for and which no page in the tree carried — it had survived only as a note kept outside the repository, which is the failure [rules.md](../../docs/rules.md) exists to prevent. |
| 2026-08-22 | Added this change log. |
| 2026-08-22 | Made every recorded corpus figure read by something, and closed the pass. |
| 2026-08-21 | Opened the page against the 2026-08-21 datasets: what each script reads, what it prints, and why the panel's every pick names its own rule. |
