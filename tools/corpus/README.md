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

**Read the peak columns with the censoring in mind.** The harness never destroys an overheating
block, so anything above critical kept generating for the rest of the clock. See the deliberate
limit in [known-issues.md](../../docs/known-issues.md).

---

## Change log

| Date | Change |
| --- | --- |
| 2026-08-22 | Added this change log. |
| 2026-08-22 | Made every recorded corpus figure read by something, and closed the pass. |
| 2026-08-21 | Opened the page against the 2026-08-21 datasets: what each script reads, what it prints, and why the panel's every pick names its own rule. |
