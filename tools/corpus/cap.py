#!/usr/bin/env python3
"""What a per-block substep cap buys and costs, over the whole population.

Reads the paired dataset `CorpusCapWalk` writes — every ship through the four air scenarios twice,
with `MaxSubstepsPerBlock` off and at 6 — and scores the four predictions registered in
[balance-lab.md](../../docs/balance-lab.md#what-a-per-block-cap-does-to-the-population-written-before-it-is-measured)
before the walk existed, against the falsifiers written down with them.

**Why one report rather than two.** `C3` asks whether the cap should be a default and `G6`'s cost
half fails in air; they are the same question, because the cap is the only lever that lowers a
step's *work* rather than moving it into the frame. So the benefit is scored against the shipped
allowance in `verdict.py`'s own arithmetic, and the cost against the two calibration points this
repository already has for what imperceptible means.

**Every pair is one ship, one scenario, one clock.** The control arm stops at equilibrium and hands
its elapsed seconds to the capped arm, so a delta here is the cap and nothing else (`M1`, `P6`). A
row without its partner is counted and dropped rather than compared against a default.

Usage: cap.py [data-dir]
"""
import collections
import csv
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import scoring

DATA = sys.argv[1] if len(sys.argv) > 1 else "out/cap-2026-08-24"

# The order the scenarios are printed in: the anchor first, then the three with air in them, in
# rising wind. The same order `air.py` uses, so two reports of one experiment read alike.
ORDER = ["vacuum-shadow", "surface-hot-noon", "storm-parked", "reentry"]

# The arm the walk ran, and the value `C3` is about. Read from the data rather than assumed: a
# dataset walked at another cap must not be scored as though it were this one.
OFF = 0


def number(row, key):
    """A column as a float, or None where the dataset does not carry it (`C8`)."""
    try:
        return float(row[key])
    except (KeyError, TypeError, ValueError):
        return None


def load(path):
    if not os.path.exists(path):
        print(f"no dataset at {path}")
        sys.exit(1)

    with open(path, newline="", encoding="utf-8") as handle:
        return list(csv.DictReader(handle))


def pair(rows):
    """Rows grouped into `(control, capped)` by ship, workshop id and scenario.

    Returns the pairs, the caps seen, and the count of rows that had no partner — which is reported
    rather than dropped in silence, because an interrupted walk leaves exactly that (`E4`, `P2`).
    """
    byKey = collections.defaultdict(dict)
    caps = set()

    for row in rows:
        cap = number(row, "cap")
        if cap is None:
            continue

        caps.add(int(cap))
        key = (row.get("ship"), row.get("workshop_id"), row.get("scenario"))
        byKey[key][int(cap)] = row

    capped_at = sorted(c for c in caps if c != OFF)
    pairs = []
    orphans = 0

    for key, arms in byKey.items():
        if OFF not in arms or len(arms) < 2:
            orphans += len(arms)
            continue

        for cap in capped_at:
            if cap in arms:
                pairs.append((key, arms[OFF], arms[cap]))
            else:
                orphans += 1

    return pairs, capped_at, orphans


def work(row):
    substeps = number(row, "substeps_demanded")
    if substeps is None:
        substeps = number(row, "substeps_granted")
    return scoring.step_work(number(row, "substep_cost"), substeps)


def verdict(label, holds, detail):
    mark = "HOLDS" if holds else ("  ?  " if holds is None else "FAILS")
    print(f"\n[{mark:^5}] {label}")
    print(f"        {detail}")


def main():
    rows = load(os.path.join(DATA, "outcomes.csv"))
    pairs, capped_at, orphans = pair(rows)

    ships = len(set((k[0], k[1]) for k, _, _ in pairs))
    print(f"paired dataset: {len(rows):,} rows, {len(pairs):,} pairs over {ships:,} ships")
    print(f"caps walked: off and {', '.join(str(c) for c in capped_at) or '(none)'}")

    if orphans:
        print(f"rows with no partner: {orphans:,} — an unfinished walk, and they are dropped")

    if not pairs:
        print("nothing to score")
        return

    cap = capped_at[-1]

    # ---- the identity ------------------------------------------------------------------------
    # The predicted benefit was arithmetic that substituted `min(demand, cap)` into an earlier
    # walk's rows. The walk asserts this per ship; this is the population statement of it, so a
    # reader of the report is told the premise held rather than having to trust that it did.
    broken = 0
    worst = 0.0
    for _, control, capped in pairs:
        a = number(control, "substeps_demanded")
        b = number(capped, "substeps_demanded")
        if a is None or b is None:
            continue

        expected = min(a, cap)
        off_by = abs(b - expected)
        if off_by > worst:
            worst = off_by
        if off_by > max(0.01, expected * 0.01):
            broken += 1

    verdict(f"the identity: a capped demand is min(demand, {cap})", broken == 0,
            f"{broken:,} of {len(pairs):,} pairs differ by more than 1 %; the worst is "
            f"{worst:.4f} substeps")

    # ---- the benefit -------------------------------------------------------------------------
    allowance = scoring.SHIPPED_VISIT_ALLOWANCE
    columns = {}
    for name, index in (("off", 1), (f"cap {cap}", 2)):
        values = [w for w in (work(p[index]) for p in pairs) if w is not None]
        columns[name] = (scoring.percentiles(values) if values else {}, values)

    print(f"\n        {'arm':10}{'work p50':>14}{'work p95':>14}{'work p99':>14}"
          f"{'work max':>16}{'over':>9}")
    for name, (p, values) in columns.items():
        if not p:
            print(f"        {name:10}{'—':>14}")
            continue
        over = sum(1 for v in values if not scoring.keeps_up(v))
        print(f"        {name:10}{p['p50']:>14,.0f}{p['p95']:>14,.0f}{p['p99']:>14,.0f}"
              f"{p['max']:>16,.0f}{over:>9,}")

    capped_p, capped_values = columns[f"cap {cap}"]
    control_p, control_values = columns["off"]

    if capped_p:
        past = sum(1 for v in capped_values if not scoring.keeps_up(v))
        verdict(f"the benefit: G6's cost half under a cap of {cap}",
                capped_p["p99"] <= allowance,
                f"work p99 {capped_p['p99']:,.0f} against {allowance:,.0f} granted, from "
                f"{control_p['p99']:,.0f} uncapped — {past:,} of {len(capped_values):,} runs still "
                f"past it")

    # ---- the cost ----------------------------------------------------------------------------
    deltas = []
    by_scenario = collections.defaultdict(list)
    for key, control, capped in pairs:
        delta = scoring.delta_peak(number(control, "peak_k"), number(capped, "peak_k"))
        if delta is None:
            continue
        deltas.append((delta, key))
        by_scenario[key[2]].append(delta)

    if deltas:
        values = [d for d, _ in deltas]
        p = scoring.percentiles(values)
        decision = scoring.cap_decision(p["p99"])

        print(f"\n        {'scenario':18}{'pairs':>9}{'dpeak p50':>12}{'dpeak p95':>12}"
              f"{'dpeak p99':>12}{'dpeak max':>12}")
        for name in ORDER + sorted(set(by_scenario) - set(ORDER)):
            if name not in by_scenario:
                continue
            q = scoring.percentiles(by_scenario[name])
            print(f"        {name:18}{len(by_scenario[name]):>9,}{q['p50']:>12.4f}"
                  f"{q['p95']:>12.4f}{q['p99']:>12.4f}{q['max']:>12.4f}")

        print(f"        {'every scenario':18}{len(values):>9,}{p['p50']:>12.4f}"
              f"{p['p95']:>12.4f}{p['p99']:>12.4f}{p['max']:>12.4f}")

        over_accepted = sum(1 for v in values if v > scoring.CAP_ACCEPTED_KELVIN)
        over_refused = sum(1 for v in values if v > scoring.CAP_REFUSED_KELVIN)

        verdict(f"the cost: what a cap of {cap} moves a peak by", None,
                f"p99 {p['p99']:.4f} K, max {p['max']:.4f} K; {over_accepted:,} of {len(values):,} "
                f"pairs move more than the {scoring.CAP_ACCEPTED_KELVIN} K this mod already "
                f"accepts and {over_refused:,} more than the {scoring.CAP_REFUSED_KELVIN} K it "
                f"refuses")

        print(f"\n        the rule, fixed before the walk: at or under "
              f"{scoring.CAP_ACCEPTED_KELVIN} K it ships, at or over "
              f"{scoring.CAP_REFUSED_KELVIN} K it stays a switch")
        print(f"        p99 is {p['p99']:.4f} K  ->  {decision}")

        # The worst pairs by name, because a tail that is one ship is a different finding from a
        # tail that is a thousand, and the ships themselves are what a reader would go and look at.
        deltas.sort(reverse=True)
        print(f"\n        the ten furthest-apart pairs")
        for delta, key in deltas[:10]:
            print(f"        {delta:>10.4f} K  {key[0][:44]:44} {key[2]}")

    # ---- the reach ---------------------------------------------------------------------------
    floored = 0
    blocks = 0
    control_floored = 0
    for _, control, capped in pairs:
        f = number(capped, "floored")
        b = number(capped, "blocks")
        if f is None or b is None:
            continue
        floored += f
        blocks += b
        control_floored += number(control, "floored") or 0

    if blocks:
        share = 100.0 * floored / blocks
        verdict(f"the reach: what a cap of {cap} holds back", None,
                f"{floored:,.0f} of {blocks:,.0f} node-runs, {share:.2f} % — and the control "
                f"floored {control_floored:,.0f}, which must be nought or it is not a control")


if __name__ == "__main__":
    main()
