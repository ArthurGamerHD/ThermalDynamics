#!/usr/bin/env python3
"""The verdict function docs/balance-lab.md says does not exist.

Stage 0 of the balance process writes six criteria down before the data is collected, "so a run
that fails them is a finding rather than an excuse to move a threshold." Nothing in the repo
evaluates them: the scenario names in Battery.cs reference G1/G2/G5 in prose, and the numbers are
read off padded text tables by eye.

This reads the corpus dataset and computes the ones the data can answer. G3 needs a cooled
comparison and G4 needs exposure per watt, neither of which is in this dataset yet; they are
reported as not-yet-answerable rather than quietly skipped.

Usage: verdict.py [data-dir] [--csv <path>] [--baseline <path>]

`--csv` writes the figures this prints as `statistic,value,unit` rows. **That is what makes a
population figure quotable**: the datasets are gigabytes and are not committed, so every number a
documentation page takes from one has until now been a number with no source in the tree, and one
of them drifted by two orders of magnitude before anyone compared it back (`F14`). A summary is
kilobytes, is committed, and diffs.

`--baseline` reads such a file and prints what moved, which is how one dataset is read against
another — the vacuum survey against the same corpus in air, for instance (`F11`).

**A partial dataset is named as partial before anything else is printed.** `E4` — *a corpus run is
quoted whole, or quoted with the words "partial" and the count attached* — had nothing checking it:
a walk killed at hour one has every column a finished one has, and the corpus is walked
largest-first, so a partial read is not a small population but the wrong end of one. At 500 of 8,142
ships the load criterion read 95 % against a true 75 %. This counts the blueprints the corpus holds
and compares, and where the corpus is not on the machine it says the population is **unknown**
rather than assuming the dataset is whole (`P2`). `--population <n>` states it instead, for a
machine with no corpus.
"""
import csv
import os
import statistics
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import provenance as provenance_lib
import scoring
from scoring import oversubscription_note

ARGS = [a for a in sys.argv[1:] if not a.startswith("--")]
DATA = ARGS[0] if ARGS else "out/corpus-2026-08-21"

def flag(name, fallback=None):
    if name not in sys.argv:
        return fallback
    at = sys.argv.index(name)
    return sys.argv[at + 1] if at + 1 < len(sys.argv) else fallback


CSV_OUT = flag("--csv", "summary.csv") if "--csv" in sys.argv else None
BASELINE = flag("--baseline")

# Every figure worth quoting, in the order it was produced.
FIGURES = []


def record(statistic, value, unit=""):
    """Keeps a figure so it can be written out, and returns it so a caller can print it too."""
    FIGURES.append((statistic, value, unit))
    return value

# Steel gives up around here; a block over its critical temperature is taking damage.
WARM_KELVIN = 400.0


def number(row, key):
    """A column as a float, or None where the dataset does not carry it.

    **A missing column reads the same as an empty cell** (`C8`). `KeyError` is in the list because
    a walk that predates a column has no such key at all, and the first caller to ask an old
    dataset for a new column crashed the report rather than reporting the column as unmeasured.
    """
    try:
        return float(row[key])
    except (KeyError, TypeError, ValueError):
        return None


# The key that identifies a ship. A workshop id alone is not enough: fourteen corpus blueprints
# sit outside a numbered workshop folder and all of them get id 0.
KEY = ("ship", "workshop_id")


def load(name, *extra_key):
    """One row per ship, or per ship and scenario — duplicates dropped, and counted.

    **Kept for the datasets that need it.** A resume used to be a skip counted in files while the
    writing was done per ship, so the ships between the last flush and the kill were written twice;
    the 2026-08-21 dataset carries ten of them, fifty rows. The resume is now a record of finished
    blueprints rather than a count and nothing collected since can carry them, but a population
    statistic that silently double-weights part of its population is the exact failure this whole
    lab exists to prevent — so the duplicates still come out, and the count is still printed, which
    is also how a reader learns which kind of dataset they are holding.

    **A paired walk is not a walk with duplicates in it**, and reading one as the other is the
    failure this function exists to prevent, arriving through the front door. `CorpusCapWalk` writes
    two rows per ship and scenario that differ only in the `cap` column, so a key without `cap`
    calls half of them duplicates and silently drops an entire arm — on a dry run of the 2026-08-25
    dataset it reported "912 duplicate rows" and scored `G6` on whichever arm happened to be first.
    The arm is part of a row's identity wherever the dataset carries one.
    """
    path = os.path.join(DATA, name + ".csv")
    if not os.path.exists(path):
        return []
    with open(path) as handle:
        rows = list(csv.DictReader(handle))

    arms, shipped = scoring.split_arms(rows)
    if arms:
        print(f"note: {name}.csv is a paired walk carrying arms {', '.join(arms)}; "
              f"scoring the {len(shipped):,} rows of the arm that ships and leaving "
              f"{len(rows) - len(shipped):,} to cap.py")
        rows = shipped

    key = KEY + extra_key
    if rows and "cap" in rows[0]:
        key = key + ("cap",)

    if not rows or any(k not in rows[0] for k in key):
        return rows

    seen = set()
    unique = []
    for row in rows:
        identity = tuple(row[k] for k in key)
        if identity in seen:
            continue
        seen.add(identity)
        unique.append(row)

    dropped = len(rows) - len(unique)
    if dropped:
        print(f"note: {name}.csv held {dropped:,} duplicate rows "
              f"(a run resumed by a skip counted in files); they are excluded")

    return unique


def pct(part, whole):
    return 0.0 if not whole else 100.0 * part / whole


# One definition, in scoring.py, shared with air.py. See scoring.percentile for why there was more
# than one and which survived.
percentiles = scoring.percentiles


outcomes = load("outcomes", "scenario")
ships = load("ships")

by_scenario = {}
for row in outcomes:
    by_scenario.setdefault(row["scenario"], []).append(row)

# **The ship count comes from the outcomes, not from ships.csv.** A walk that records outcomes and
# not ships is a legitimate shape — `CorpusAirWalk` deliberately writes no ships.csv, because a
# hull's structure belongs to the hull rather than to the world it is run in — and reading the
# count off the file that happens to be absent reports a population of nought. It is also the
# figure that says a dataset is *partial*: a walk killed at hour one has every column a finished
# one has, over the ships it reached, and the corpus is sorted largest-first, so a partial read is
# not a small population but the wrong end of one (`P13`, and the standing warning that partial
# corpus results mislead badly).
walked = len(set((r.get("ship"), r.get("workshop_id")) for r in outcomes))

print(f"corpus dataset: {len(outcomes):,} outcome rows over {walked:,} ships, "
      f"{len(ships):,} ship rows")

# ---- whole, or partial and said so (`E4`) -------------------------------------------------------
population = scoring.corpus_population(flag("--population"))
share = scoring.walked_share(walked, population)

if population is None:
    print("  population UNKNOWN — the corpus is not on this machine and --population was not given,"
          " so whether this dataset is whole cannot be established here")
    record("dataset population", "", "blueprints")
elif share is not None and share < scoring.WHOLE_ENOUGH:
    print(f"  *** PARTIAL: {walked:,} of {population:,} blueprints, {share * 100:.1f} % ***")
    print("      The corpus is walked largest-first, so this is the wrong end of a population"
          " rather than a small one. Every figure below is over the ships reached (`E4`).")
    record("dataset population", population, "blueprints")
    record("dataset partial", 1)
else:
    print(f"  whole: {walked:,} of {population:,} blueprints")
    record("dataset population", population, "blueprints")
    record("dataset partial", 0)
record("dataset outcome rows", len(outcomes))
record("dataset ships", walked)
record("dataset ships.csv rows", len(ships))
record("dataset scenarios", len(set(r.get("scenario") for r in outcomes)))

# **What build the dataset was collected on**, printed and recorded so a figure quoted from a
# summary carries the world it was measured in. Datasets collected before 2026-08-25 have no such
# file, and *absent* is said rather than assumed: the 2026-08-24 air walk turned out to have been
# collected one minute after a definition change, and nothing in its output said so.
provenance = os.path.join(DATA, "provenance.txt")
if os.path.exists(provenance):
    seen = {}
    with open(provenance, encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if not line:
                continue

            print(f"  {line}")

            # A comment is context for a reader and not a statistic; recording it would put prose
            # in the summary's value column.
            if line.startswith("#"):
                continue

            name, _, value = line.partition(" ")
            seen.setdefault(name, [])
            if value not in seen[name]:
                seen[name].append(value)

    # **A resumed walk appends a block per slice, so a key can appear several times — and recording
    # each in turn leaves the summary claiming the last.** That is the committed artefact every
    # quoted figure is checked against, so it has to carry the split rather than the last value:
    # the 2026-08-25 survey ran in six slices across two `Cubes.xml` and two `Loops.xml`, and this
    # file would have said it was measured against one of each.
    for name in seen:
        record("provenance " + name, seen[name][-1])
        if len(seen[name]) > 1:
            record("provenance " + name + " versions", len(seen[name]))
            record("provenance " + name + " all", " ".join(seen[name]))

    # **Which definition files this dataset spans is asked of `provenance.py` rather than worked
    # out again here.** The two readers of this format had already drifted once — that module read
    # only the last `Cubes.xml` line and reported a split dataset as one — and a second
    # implementation of the same question is how that happens (`D3`). The block above still counts
    # every key, because the summary has to carry the commits and walk times as well.
    split = sorted(provenance_lib.spans_several_definitions(DATA))
    if split:
        print()
        for name in split:
            versions = provenance_lib.definition_hashes(DATA, name)
            print(f"  SPANS {len(versions)} VERSIONS of {name}: {', '.join(versions)}")
        print("  This dataset was walked in slices and the definitions moved under it. Every figure"
              " below is over both.")
else:
    print("  provenance: not recorded — this dataset predates 2026-08-25")
    record("provenance", "absent")

# **The population figures the documentation quotes.** Sealed blocks are here because a page said
# twenty-four of them on one ship where the dataset says 1,184 across 331 (`F14`); the share it
# also quoted was right, which is how it survived.
_blocks = [number(r, "blocks") for r in ships]
_blocks = sorted(b for b in _blocks if b is not None)
if _blocks:
    record("population blocks", int(sum(_blocks)))
    record("population blocks p50", int(_blocks[len(_blocks) // 2]))
    record("population blocks p99", int(_blocks[min(len(_blocks) - 1, int(0.99 * len(_blocks)))]))

_sealed = [number(r, "sealed_blocks") for r in ships]
_sealed = [v for v in _sealed if v is not None]
if _sealed and _blocks:
    record("sealed blocks", int(sum(_sealed)))
    record("sealed ships", sum(1 for v in _sealed if v > 0))
    record("sealed block share", round(100.0 * sum(_sealed) / sum(_blocks), 5), "%")
print(f"scenarios: {', '.join(sorted(by_scenario))}\n")

print("peak temperature by scenario")
print(f"  {'scenario':18}{'ships':>7}{'min':>8}{'p50':>8}{'p95':>9}{'p99':>9}{'max':>10}{'over crit':>11}")
for name in sorted(by_scenario):
    rows = by_scenario[name]
    peaks = [number(r, "peak_k") for r in rows]
    peaks = [p for p in peaks if p is not None]
    if not peaks:
        continue
    p = percentiles(peaks)
    over = sum(1 for r in rows if number(r, "over_critical") and number(r, "over_critical") > 0)
    print(f"  {name:18}{len(rows):>7,}{p['min']:>8.0f}{p['p50']:>8.0f}{p['p95']:>9.0f}"
          f"{p['p99']:>9.0f}{p['max']:>10.0f}{pct(over, len(rows)):>10.1f}%")

print("\n" + "=" * 78)
print("CRITERIA")
print("=" * 78)


def verdict(tag, claim, ok, detail, fails_when):
    record(tag + " verdict", "holds" if ok is True else ("fails" if ok is False else "unanswered"))
    mark = "HOLDS" if ok is True else ("FAILS" if ok is False else "  ?  ")
    print(f"\n[{mark}] {tag}  {claim}")
    print(f"        fails when: {fails_when}")
    print(f"        measured:   {detail}")


# ---- G1: idle is safe -------------------------------------------------------------------
idle = by_scenario.get("idle", []) + by_scenario.get("vacuum-shadow", [])
if idle:
    critical = sum(1 for r in idle if number(r, "over_critical") and number(r, "over_critical") > 0)
    share = pct(critical, len(idle))
    record("G1 idle critical share", round(share, 4), "%")
    verdict("G1", "Idle is safe.",
            share <= 1.0 if scoring.resolves(len(idle), 1.0) else None,
            f"{critical} of {len(idle)} idle runs had a block over critical ({share:.2f} %)"
            if scoring.resolves(len(idle), 1.0) else scoring.too_coarse(len(idle), 1.0, "idle runs"),
            "more than ~1 % of the corpus goes critical at idle")
else:
    verdict("G1", "Idle is safe.", None,
            "neither idle nor vacuum-shadow is in this dataset",
            "more than ~1 % of the corpus goes critical at idle")

# ---- G2: load bites ---------------------------------------------------------------------
loaded = by_scenario.get("full-electrical", [])
if loaded:
    warm = sum(1 for r in loaded
               if number(r, "peak_k") is not None and number(r, "peak_k") >= WARM_KELVIN)
    share = pct(warm, len(loaded))
    record("G2 warm share", round(share, 3), "%")
    verdict("G2", "Load bites.",
            share >= 20.0 if scoring.resolves(len(loaded), 20.0) else None,
            f"{warm} of {len(loaded)} reached {WARM_KELVIN:.0f} K under full electrical load "
            f"({share:.1f} %)"
            if scoring.resolves(len(loaded), 20.0) else scoring.too_coarse(len(loaded), 20.0, "loaded runs"),
            "fewer than ~20 % ever get warm")
else:
    verdict("G2", "Load bites.", None,
            "the full-electrical scenario is not in this dataset — it is a load case, and a walk "
            "of idle environments cannot answer it",
            "fewer than ~20 % ever get warm")

# ---- G5: no death spiral ----------------------------------------------------------------
recovery = by_scenario.get("recovery", [])
if recovery:
    returned = sum(1 for r in recovery
                   if number(r, "over_critical") == 0)
    record("G5 recovered share", round(pct(returned, len(recovery)), 3), "%")
    verdict("G5", "No death spiral.",
            pct(returned, len(recovery)) > 95.0 if scoring.resolves(len(recovery), 5.0) else None,
            f"{returned} of {len(recovery)} recovered below critical after throttling to idle"
            if scoring.resolves(len(recovery), 5.0) else scoring.too_coarse(len(recovery), 5.0, "recovery runs"),
            "recovery time unbounded, or damage continues after the load stops")
else:
    verdict("G5", "No death spiral.", None,
            "the recovery scenario is not in this dataset — no walk runs it over the corpus",
            "recovery time unbounded, or damage continues after the load stops")

# ---- G6: affordable across the population ------------------------------------------------
demand = [number(r, "substeps_demanded") for r in outcomes]
demand = [d for d in demand if d is not None]
if demand:
    p = percentiles(demand)
    granted = [number(r, "substeps_granted") for r in outcomes]
    granted = [g for g in granted if g is not None]

    # The shipped `MaxSubsteps`, not the largest count this dataset happened to be granted. See
    # scoring.SHIPPED_SUBSTEP_CAP for why the second is not a cap at all.
    cap = scoring.SHIPPED_SUBSTEP_CAP
    highest = max(granted) if granted else 0
    # The cost half of the same criterion, in the solver's own unit rather than in milliseconds:
    # a step's element visits against what `MaxElementVisitsPerStep` grants one. See
    # balance-lab.md, G6's cost half, which was written down before this was scored (`E11`).
    work = []
    for row in outcomes:
        # Demanded rather than granted: the allowance decides by comparing the demand against what
        # it can afford, so a granted count is the answer rather than the question.
        substeps = number(row, "substeps_demanded")
        if substeps is None:
            substeps = number(row, "substeps_granted")
        cost = scoring.step_work(number(row, "substep_cost"), substeps)
        if cost is not None:
            work.append(cost)

    allowance = scoring.SHIPPED_VISIT_ALLOWANCE
    cost_p = percentiles(work) if work else {}
    cost_ok = bool(cost_p) and cost_p["p99"] <= allowance
    over = sum(1 for w in work if not scoring.keeps_up(w))

    record("G6 demand p50", round(p["p50"], 3), "substeps")
    record("G6 demand p95", round(p["p95"], 3), "substeps")
    record("G6 demand p99", round(p["p99"], 3), "substeps")
    record("G6 demand max", round(p["max"], 3), "substeps")
    record("G6 substep cap", int(cap), "substeps")
    if cost_p:
        record("G6 work p50", int(cost_p["p50"]), "element visits")
        record("G6 work p95", int(cost_p["p95"]), "element visits")
        record("G6 work p99", int(cost_p["p99"]), "element visits")
        record("G6 work max", int(cost_p["max"]), "element visits")
        record("G6 visit allowance", int(allowance), "element visits")
        record("G6 runs over the allowance", over)

    demand_ok = p["p99"] <= cap

    # **A half that was not measured is not a half that failed** (`P2`, `E8`). A dataset with no
    # `substep_cost` column can score the demand and nothing else, and a criterion with one half
    # unmeasured has no verdict — reporting it as a failure would read as evidence against the
    # configuration when it is evidence about the walk.
    outcome = (demand_ok and cost_ok) if cost_p else None

    verdict("G6", "Affordable across the population.", outcome,
            f"substep demand p50 {p['p50']:.1f}, p95 {p['p95']:.1f}, p99 {p['p99']:.1f}, "
            f"max {p['max']:.1f}; against {cap:.0f} granted, highest reached {highest:.0f}"
            + oversubscription_note(p["p99"], cap)
            + ("" if cost_p else "; the cost half is unmeasured on this dataset, so the criterion "
                                 "has no verdict"),
            "p99 substep demand exceeds what the shipped caps grant, or p99 step work exceeds "
            "the shipped element-visit allowance")

    if cost_p:
        print(f"        step work:  p50 {cost_p['p50']:,.0f}, p95 {cost_p['p95']:,.0f}, "
              f"p99 {cost_p['p99']:,.0f}, max {cost_p['max']:,.0f} element visits "
              f"(links + {scoring.NODE_COST_IN_LINKS:.0f} x nodes) against "
              f"{allowance:,.0f} granted"
              + (f"; {over:,} of {len(work):,} runs are past it — those grids run slower than "
                 f"real time" if over else "; every run fits"))

    # **And the same two statistics per scenario, because both are properties of the world.**
    # The criterion is one figure over the dataset and stays that way (`E11`); this is the
    # breakdown that says which environment produced it. A dataset of one scenario prints one row
    # and repeats the verdict above, which is honest rather than redundant: it is what says the
    # figure is that world's and not a population's.
    if len(by_scenario) > 1:
        # **The demand columns survive a dataset with no cost column.** One half of the criterion
        # being unmeasured is not a reason to withhold the other; an em dash in the work columns
        # says which one it is (`P2`).
        print(f"\n        {'scenario':18}{'runs':>8}{'demand p50':>12}{'demand p99':>12}"
              f"{'work p50':>12}{'work p99':>12}{'over':>7}")
        for name in sorted(by_scenario):
            rows = by_scenario[name]
            demands = [d for d in (number(r, "substeps_demanded") for r in rows) if d is not None]
            works = []
            for row in rows:
                substeps = number(row, "substeps_demanded")
                if substeps is None:
                    substeps = number(row, "substeps_granted")
                cost = scoring.step_work(number(row, "substep_cost"), substeps)
                if cost is not None:
                    works.append(cost)
            if not demands:
                continue
            d = percentiles(demands)

            # Per scenario rather than pooled, because `Census.Corpus` states the corpus in
            # vacuum and in air separately and a pooled figure answers neither.
            record(f"G6 {name} demand p10", round(d["p10"], 3), "substeps")
            record(f"G6 {name} demand p50", round(d["p50"], 3), "substeps")
            record(f"G6 {name} demand p90", round(d["p90"], 3), "substeps")
            record(f"G6 {name} demand p99", round(d["p99"], 3), "substeps")
            record(f"G6 {name} demand max", round(d["max"], 3), "substeps")

            if not works:
                print(f"        {name:18}{len(rows):>8,}{d['p50']:>12.1f}{d['p99']:>12.1f}"
                      f"{scoring.ABSENT:>12}{scoring.ABSENT:>12}{scoring.ABSENT:>7}")
                continue

            w = percentiles(works)
            past = sum(1 for value in works if not scoring.keeps_up(value))
            record(f"G6 {name} work p99", int(w["p99"]), "element visits")
            record(f"G6 {name} runs over the allowance", past)
            print(f"        {name:18}{len(rows):>8,}{d['p50']:>12.1f}{d['p99']:>12.1f}"
                  f"{w['p50']:>12,.0f}{w['p99']:>12,.0f}{past:>7,}")
    if not cost_p:
        # **Not a zero, and not the old formula.** Every walk taken before 2026-08-24 carries
        # `blocks` and `joints` but no `substep_cost`, and `joints` is the count of mechanical
        # joints between grids rather than of thermal links — so scoring those datasets at all
        # would reproduce the node-half-only figure this column exists to replace (`E8`, `P2`).
        print("        step work:  not derivable from this dataset — it needs the `substep_cost` "
              "column, which walks before 2026-08-24 do not carry. What stood here was scored "
              "with `joints` for the link count and is the node half alone; re-walk to score it.")

# ---- G3 / G4: not answerable from this dataset -------------------------------------------
verdict("G3", "Cooling works.", None,
        "needs a cooled comparison — no corpus ship carries a radiator or a loop, because the "
        "corpus filter rejects any ship with a non-vanilla block. Needs the retrofit pass.",
        "median delta-peak from a standard cooling fit is small")

verdict("G4", "Design decides, not size.", None,
        "needs exposed area per watt per ship; ships.csv carries blocks and nodes but not "
        "exposure or installed power yet. Needs the screening pass.",
        "rank correlation with block count exceeds that with exposure")

# ---- exceptional ships --------------------------------------------------------------------
print("\n" + "=" * 78)
print("EXCEPTIONAL SHIPS — candidates for a standing panel")
print("=" * 78)


def extremes(scenario, key, label, count=5, reverse=True):
    rows = [r for r in by_scenario.get(scenario, []) if number(r, key) is not None]
    if not rows:
        return
    rows.sort(key=lambda r: number(r, key), reverse=reverse)
    print(f"\n{label} ({scenario})")
    for r in rows[:count]:
        print(f"  {number(r, key):12,.0f}  {int(number(r, 'blocks') or 0):>7,} blocks  "
              f"{r['ship'][:44]}")


extremes("full-electrical", "peak_k", "hottest under full electrical load")
extremes("full-electrical", "hotspot_k", "worst hot spot (peak minus mean)")
extremes("idle", "peak_k", "hottest at idle — these should not exist")
extremes("full-electrical", "substeps_demanded", "most expensive to solve")

# ---- censoring: what the peak statistics above cannot mean --------------------------------
print("\n" + "=" * 78)
print("CENSORING — the harness never destroys a block")
print("=" * 78)
print("""
  The solver raises an overheat event past critical, but DoDamage lives in the game layer, which
  no harness runs. An overheating block is never removed: it keeps generating and keeps climbing
  for the rest of the clock. Every peak above critical means "this block dies" and nothing more,
  so the p95/p99/max columns above describe the harness, not the mod.
""".rstrip())

tail = [r for r in outcomes if (number(r, "peak_k") or 0) > 900.0]
far = [r for r in outcomes if (number(r, "peak_k") or 0) > 10000.0]
runaway_ships = set(r["ship"] for r in outcomes if (number(r, "peak_k") or 0) > 2000.0)
print(f"\n  rows past 900 K (steel service limit): {len(tail):>6,}  {pct(len(tail), len(outcomes)):5.2f} %")
print(f"  rows past 10,000 K — plainly unphysical: {len(far):>6,}  {pct(len(far), len(outcomes)):5.2f} %")
print(f"  ships with at least one runaway scenario: {len(runaway_ships):,} of "
      f"{len(set(r['ship'] for r in outcomes)):,}")

# ---- how fast damage arrives -------------------------------------------------------------
# **The crossing and the loss are two events, and this section used to print one under the other's
# name.** The solver damages an overheating block by (T - critical) x OverheatDamagePerKelvin per
# simulated second, so at the crossing the damage rate is exactly zero. The crossing is when a
# warning could fire; the loss is when the player is out a block. Both are decided before the run
# ends, so censoring reaches neither.
print("\n" + "=" * 78)
print("TIME TO CRITICAL — when a warning could fire, not when anything is lost")
print("=" * 78)
print("\n  The quantiles are over the ships that reached critical, which is the share in the")
print("  first column and not the population. A median here is 'how fast do the ships that")
print("  overheat overheat', not 'when does a ship overheat' — pairs.py scores G8 on the second")
print("  and they are different numbers wherever the share is not one (E9).")
print(f"\n  {'scenario':18}{'reach critical':>16}{'p10':>8}{'median':>9}{'p90':>8}")
for name in sorted(by_scenario):
    rows_ = by_scenario[name]
    hit = sorted(v for v in (number(r, "seconds_to_critical") for r in rows_)
                 if v is not None and v >= 0)
    if not hit:
        print(f"  {name:18}{'none':>16}")
        continue
    q = percentiles(hit)
    print(f"  {name:18}{pct(len(hit), len(rows_)):>15.1f}%{hit[len(hit)//10]:>8.0f}s"
          f"{q['p50']:>8.0f}s{hit[min(len(hit)-1, (9*len(hit))//10)]:>7.0f}s")

print("\n  recovery inherits its crossings from the burn it starts in — that column is not new damage;")
# Computed rather than quoted. This line carried "7,899 of 8,142", which were the figures of the
# 2026-08-21 run written in by hand — in a script whose whole purpose is that a run's numbers come
# from the run and not from what anybody remembers them to be.
if recovery:
    print(f"  whether those ships come back is G5 above, and {returned:,} of {len(recovery):,} do.")
else:
    print("  whether those ships come back is G5 above, which this dataset cannot answer.")

print("\n" + "=" * 78)
print("TIME TO FIRST LOSS — when a block's hit points run out")
print("=" * 78)
if not any("seconds_to_first_loss" in r for r in outcomes):
    print("\n  not in this dataset: seconds_to_first_loss was added after it was collected.")
else:
    print(f"\n  {'scenario':18}{'lose a block':>16}{'p10':>8}{'median':>9}{'p90':>8}"
          f"{'after crossing':>17}")
    for name in sorted(by_scenario):
        rows_ = by_scenario[name]
        lost = sorted(v for v in (number(r, "seconds_to_first_loss") for r in rows_)
                      if v is not None and v >= 0)
        if not lost:
            print(f"  {name:18}{'none':>16}")
            continue
        # Paired per ship before the difference is taken (`E6`): the gap between two percentiles of
        # two populations is not a percentile of the gap.
        gaps = sorted(number(r, "seconds_to_first_loss") - number(r, "seconds_to_critical")
                      for r in rows_
                      if (number(r, "seconds_to_first_loss") or -1) >= 0
                      and (number(r, "seconds_to_critical") or -1) >= 0)
        q = percentiles(lost)
        gap = f"{percentiles(gaps)['p50']:>16.0f}s" if gaps else f"{'—':>17}"
        print(f"  {name:18}{pct(len(lost), len(rows_)):>15.1f}%{lost[len(lost)//10]:>8.0f}s"
              f"{q['p50']:>8.0f}s{lost[min(len(lost)-1, (9*len(lost))//10)]:>7.0f}s{gap}")
    print("\n  The last column is the median ship's own crossing-to-loss gap, paired before it is")
    print("  differenced — it is the span a warning has to be useful in.")
# ---- which blocks drive the tail ----------------------------------------------------------
print("\n" + "=" * 78)
print("WHICH BLOCKS RUN AWAY — the tunable surface")
print("=" * 78)
hot_all = {}
hot_run = {}
for r in outcomes:
    name = r.get("hottest_block") or "?"
    hot_all[name] = hot_all.get(name, 0) + 1
    if (number(r, "peak_k") or 0) > 2000.0:
        hot_run[name] = hot_run.get(name, 0) + 1

total_run = sum(hot_run.values())
print(f"\n  {'block':44}{'runaway':>9}{'share':>8}{'of all':>8}")
for name, count in sorted(hot_run.items(), key=lambda kv: -kv[1])[:10]:
    print(f"  {name[:42]:44}{count:>9,}{pct(count, total_run):>7.1f}%"
          f"{pct(hot_all.get(name, 0), len(outcomes)):>7.1f}%")
print("\n  A type far larger in the runaway column than in the last one is over-producing,")
print("  under-massed, or too weakly coupled to its neighbours — a dozen definitions, not the solver.")



# ---- the summary --------------------------------------------------------------------------

if CSV_OUT:
    with open(CSV_OUT, "w", newline="") as handle:
        writer = csv.writer(handle)
        writer.writerow(["statistic", "value", "unit"])
        for statistic, value, unit in FIGURES:
            writer.writerow([statistic, value, unit])
    print(f"\ncsv -> {CSV_OUT}  ({len(FIGURES)} figures)")

if BASELINE:
    if not os.path.exists(BASELINE):
        print(f"\nbaseline not found: {BASELINE}")
    else:
        with open(BASELINE) as handle:
            before = {r["statistic"]: r["value"] for r in csv.DictReader(handle)}

        now = dict((statistic, value) for statistic, value, _ in FIGURES)

        print("\n" + "=" * 78)
        print(f"AGAINST {BASELINE}")
        print("=" * 78)
        print(f"\n  {'statistic':38}{'baseline':>16}{'now':>16}  {'change':>9}")

        for statistic, was, became, change in scoring.compare(before, now):
            print(f"  {statistic[:36]:38}{str(was):>16}{str(became):>16}  {change:>9}")
