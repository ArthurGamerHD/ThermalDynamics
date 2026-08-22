# Corpus tools

What to do with the CSVs a corpus sweep leaves behind. Both tools read a data directory holding
`outcomes.csv` and `ships.csv` — the run of 2026-08-21 is in `out/corpus-2026-08-21`.

```
python3 tools/corpus/verdict.py out/corpus-2026-08-21     # the criteria, on the terminal
./tools/corpus/build-report.sh out/corpus-2026-08-21      # the interactive page
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

**Read the peak columns with the censoring in mind.** The harness never destroys an overheating
block, so anything above critical kept generating for the rest of the clock. See the deliberate
limit in [known-issues.md](../../docs/known-issues.md).
