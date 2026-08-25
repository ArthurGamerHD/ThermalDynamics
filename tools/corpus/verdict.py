#!/usr/bin/env python3
"""The verdict function docs/balance-lab.md says does not exist.

Stage 0 of the balance process writes six criteria down before the data is collected, "so a run
that fails them is a finding rather than an excuse to move a threshold." Nothing in the repo
evaluates them: the scenario names in Battery.cs reference G1/G2/G5 in prose, and the numbers are
read off padded text tables by eye.

This reads the corpus dataset and computes the ones the data can answer. G3 needs a cooled
comparison and G4 needs exposure per watt, neither of which is in this dataset yet; they are
reported as not-yet-answerable rather than quietly skipped.

Usage: verdict.py [data-dir]
"""
import csv
import os
import statistics
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import scoring
from scoring import oversubscription_note

DATA = sys.argv[1] if len(sys.argv) > 1 else "out/corpus-2026-08-21"

# Steel gives up around here; a block over its critical temperature is taking damage.
WARM_KELVIN = 400.0


def number(row, key):
    try:
        return float(row[key])
    except (TypeError, ValueError):
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
    """
    path = os.path.join(DATA, name + ".csv")
    if not os.path.exists(path):
        return []
    with open(path) as handle:
        rows = list(csv.DictReader(handle))

    key = KEY + extra_key
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


def percentiles(values):
    if not values:
        return {}
    values = sorted(values)

    def at(q):
        return values[min(len(values) - 1, int(q * len(values)))]

    return {"min": values[0], "p50": at(0.5), "p95": at(0.95), "p99": at(0.99), "max": values[-1]}


outcomes = load("outcomes", "scenario")
ships = load("ships")

by_scenario = {}
for row in outcomes:
    by_scenario.setdefault(row["scenario"], []).append(row)

print(f"corpus dataset: {len(outcomes):,} outcome rows, {len(ships):,} ship rows")
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
    mark = "HOLDS" if ok is True else ("FAILS" if ok is False else "  ?  ")
    print(f"\n[{mark}] {tag}  {claim}")
    print(f"        fails when: {fails_when}")
    print(f"        measured:   {detail}")


# ---- G1: idle is safe -------------------------------------------------------------------
idle = by_scenario.get("idle", []) + by_scenario.get("vacuum-shadow", [])
if idle:
    critical = sum(1 for r in idle if number(r, "over_critical") and number(r, "over_critical") > 0)
    share = pct(critical, len(idle))
    verdict("G1", "Idle is safe.", share <= 1.0,
            f"{critical} of {len(idle)} idle runs had a block over critical ({share:.2f} %)",
            "more than ~1 % of the corpus goes critical at idle")

# ---- G2: load bites ---------------------------------------------------------------------
loaded = by_scenario.get("full-electrical", [])
if loaded:
    warm = sum(1 for r in loaded
               if number(r, "peak_k") is not None and number(r, "peak_k") >= WARM_KELVIN)
    share = pct(warm, len(loaded))
    verdict("G2", "Load bites.", share >= 20.0,
            f"{warm} of {len(loaded)} reached {WARM_KELVIN:.0f} K under full electrical load "
            f"({share:.1f} %)",
            "fewer than ~20 % ever get warm")

# ---- G5: no death spiral ----------------------------------------------------------------
recovery = by_scenario.get("recovery", [])
if recovery:
    returned = sum(1 for r in recovery
                   if number(r, "over_critical") == 0)
    verdict("G5", "No death spiral.", pct(returned, len(recovery)) > 95.0,
            f"{returned} of {len(recovery)} recovered below critical after throttling to idle",
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
    cap = max(granted) if granted else 0
    # The cost half of the same criterion, in the solver's own unit rather than in milliseconds:
    # a step's element visits against what `MaxElementVisitsPerStep` grants one. See
    # balance-lab.md, G6's cost half, which was written down before this was scored (`E11`).
    work = []
    for row in outcomes:
        cost = scoring.step_work(
            number(row, "blocks"), number(row, "joints"), number(row, "substeps_granted"))
        if cost is not None:
            work.append(cost)

    allowance = scoring.SHIPPED_VISIT_ALLOWANCE
    cost_p = percentiles(work) if work else {}
    cost_ok = bool(cost_p) and cost_p["p99"] <= allowance
    over = sum(1 for w in work if not scoring.keeps_up(w))

    demand_ok = p["p99"] <= cap

    verdict("G6", "Affordable across the population.", demand_ok and cost_ok,
            f"substep demand p50 {p['p50']:.1f}, p95 {p['p95']:.1f}, p99 {p['p99']:.1f}, "
            f"max {p['max']:.1f}; highest granted {cap:.0f}" + oversubscription_note(p["p99"], cap),
            "p99 substep demand exceeds what the shipped caps grant, or p99 step work exceeds "
            "the shipped element-visit allowance")

    if cost_p:
        print(f"        step work:  p50 {cost_p['p50']:,.0f}, p95 {cost_p['p95']:,.0f}, "
              f"p99 {cost_p['p99']:,.0f}, max {cost_p['max']:,.0f} element visits against "
              f"{allowance:,.0f} granted"
              + (f"; {over:,} of {len(work):,} runs are past it — those grids run slower than "
                 f"real time" if over else "; every run fits"))
    else:
        print("        step work:  not derivable from this dataset — it needs blocks, joints and "
              "substeps_granted, and one of them is missing")

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

