#!/usr/bin/env python3
"""What flooring an over-budget grid buys and costs, on the ships the allowance binds on.

Reads the paired dataset `CorpusFloorWalk` writes — every ship the element-visit allowance binds on,
through the four air scenarios twice, with `FloorBlocksWhenOverBudget` off as it ships and on — and
scores the four predictions registered in
[balance-lab.md](../../docs/balance-lab.md#what-flooring-an-over-budget-grid-does-written-before-it-is-measured)
before the walk existed, against the falsifiers written down with them.

**Why this exists as a tool rather than as an analysis.** The 2026-08-25 walk's figures were read
once and written onto a page, and a figure a page quotes has to have a source in the tree or it is a
number nobody can check (`E5`). `--csv` writes them as `statistic,value,unit` beside the dataset's
provenance, which is what `cap.py` does for `C3` and what this had not.

**Every pair is one ship, one scenario, one clock.** The unfloored arm stops at equilibrium and
hands its elapsed seconds to the floored arm, so a delta here is the floor and nothing else
(`M1`, `P6`). A row without its partner is counted and dropped rather than compared against a
default.

**This is not a population.** It walks the ships the allowance binds on and no others, so nothing
here describes the corpus and none of it describes the ships the mechanism never reaches
(`P1`, `P2`). The report says so on its first line rather than leaving it to the reader.

Usage: floor.py [data-dir] [--csv <path>]
"""
import collections
import csv
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import provenance as provenance_lib
import scoring

ARGS = [a for a in sys.argv[1:] if not a.startswith("--")]
FLAGGED = set()
if "--csv" in sys.argv and sys.argv.index("--csv") + 1 < len(sys.argv):
    FLAGGED.add(sys.argv[sys.argv.index("--csv") + 1])
ARGS = [a for a in ARGS if a not in FLAGGED]

DATA = ARGS[0] if ARGS else "out/floor-2026-08-29"
CSV_OUT = (sys.argv[sys.argv.index("--csv") + 1]
           if "--csv" in sys.argv and sys.argv.index("--csv") + 1 < len(sys.argv) else None)

# The order the scenarios print in: the anchor first, then the three with air in them, in rising
# wind. The same order `air.py` and `cap.py` use, so three reports of one corpus read alike.
ORDER = ["vacuum-shadow", "surface-hot-noon", "storm-parked", "reentry"]

# The scenario with no air in it, which is the walk's control for *air cannot make a hull softer*.
ANCHOR = "vacuum-shadow"

# The arms, as the walk writes them into the `cap` column: 0 is the shipped configuration with the
# floor off, -1 is the floor on. Read from the data rather than assumed.
OFF = 0
ON = -1

FIGURES = []


def record(statistic, value, unit=""):
    FIGURES.append((statistic, value, unit))




def load(path):
    if not os.path.exists(path):
        print("no dataset at " + path)
        sys.exit(1)

    with open(path, newline="", encoding="utf-8") as handle:
        return list(csv.DictReader(handle))


def pair(rows):
    """Rows grouped into `(control, floored)` by ship, workshop id and scenario.

    Returns the pairs and the count of rows that had no partner — reported rather than dropped in
    silence, because an interrupted walk leaves exactly that (`E4`, `P2`).
    """
    by_key = collections.defaultdict(dict)
    for row in rows:
        arm = scoring.number(row, "cap")
        if arm is None:
            continue
        by_key[(row.get("ship"), row.get("workshop_id"), row.get("scenario"))][int(arm)] = row

    pairs = []
    orphans = 0
    for key, arms in by_key.items():
        if OFF in arms and ON in arms:
            pairs.append((key, arms[OFF], arms[ON]))
        else:
            orphans += len(arms)

    return pairs, orphans


def verdict(key, label, holds, detail):
    """One scored row, printed and recorded under a name that does not move (`E5`)."""
    mark = "HOLDS" if holds else ("  ?  " if holds is None else "FAILS")
    record("floor " + key + " verdict",
           "unscored" if holds is None else ("holds" if holds else "fails"))
    print("\n[%s] %s" % (mark.center(5), label))
    print("        " + detail)


def main():
    rows = load(os.path.join(DATA, "outcomes.csv"))
    pairs, orphans = pair(rows)

    ships = len(set((k[0], k[1]) for k, _, _ in pairs))
    print("paired dataset: %s rows, %s pairs over %s ships"
          % (format(len(rows), ","), format(len(pairs), ","), format(ships, ",")))
    print("  ** these are the ships the element-visit allowance binds on and no others, so nothing")
    print("     here is a population figure and none of it describes the ships the mechanism never")
    print("     reaches (`P1`, `P2`) **")

    if orphans:
        print("rows with no partner: %s — an unfinished walk, and they are dropped"
              % format(orphans, ","))

    if not pairs:
        print("nothing to score")
        return

    record("dataset outcome rows", len(rows))
    record("dataset ships", ships)
    record("dataset pairs", len(pairs))
    record("dataset rows without a partner", orphans)
    for statistic, value, unit in provenance_lib.summary_rows(DATA):
        record(statistic, value, unit)

    # ---- the clock ---------------------------------------------------------------------------
    # The floored arm runs to the control's elapsed seconds, so a pair on two clocks is measuring
    # the stopping rule rather than the floor (`M1`, `P6`).
    off_clock = 0
    for _, control, floored in pairs:
        a = scoring.number(control, "run_seconds")
        b = scoring.number(floored, "run_seconds")
        if a is None or b is None or abs(a - b) > 1e-3:
            off_clock += 1

    record("floor pairs on different clocks", off_clock, "pairs")
    verdict("clock", "the clock: no run loses simulated time", off_clock == 0,
            "%s of %s pairs ran their two arms on different clocks"
            % (format(off_clock, ","), format(len(pairs), ",")))

    # ---- the safety -------------------------------------------------------------------------
    # The control must never floor — it has the mechanism off — and the floored arm must never
    # *stiffen* a block, which would be the mechanism working backwards.
    control_floored = 0
    stiffened = 0
    for _, control, floored in pairs:
        control_floored += scoring.number(control, "floored") or 0
        a = scoring.number(control, "substeps_demanded")
        b = scoring.number(floored, "substeps_demanded")
        if a is not None and b is not None and b > a + scoring.FLOOR_SLACK_SUBSTEPS:
            stiffened += 1

    record("floor control floored", control_floored, "node-runs")
    record("floor pairs stiffened", stiffened, "pairs")
    verdict("safety", "the safety: never stiffens, never floors in the control",
            control_floored == 0 and stiffened == 0,
            "the control floored %s node-runs, which must be nought or it is not a control, and "
            "%s pairs came back stiffer than they went in"
            % (format(int(control_floored), ","), format(stiffened, ",")))

    # ---- the reach --------------------------------------------------------------------------
    floored_nodes = 0
    blocks = 0
    engaged = []
    for _, control, floored in pairs:
        f = scoring.number(floored, "floored")
        b = scoring.number(floored, "blocks")
        if f is None or b is None:
            continue

        floored_nodes += f
        blocks += b
        if f > 0:
            engaged.append((_, control, floored))

    share = 100.0 * floored_nodes / blocks if blocks else 0.0
    low, high = scoring.FLOOR_REACH_BAND
    record("floor reach", round(share, 4), "%")
    record("floor cells the floor engages on", len(engaged), "pairs")
    verdict("reach", "the reach: what the floor holds back", low <= share <= high,
            "%s of %s node-runs, %.2f %% against the %g-%g %% predicted; the floor engages on "
            "%s of %s cells"
            % (format(int(floored_nodes), ","), format(int(blocks), ","), share, low, high,
               format(len(engaged), ","), format(len(pairs), ",")))

    # ---- the cost ---------------------------------------------------------------------------
    # **Scored over the cells the floor engaged on**, because a cell it never touched has a delta of
    # nought by construction and folding those in reports the mechanism's reach as though it were
    # its price. Both are printed and the band is read against the engaged one.
    deltas = []
    by_scenario = collections.defaultdict(list)
    everything = []
    for key, control, floored in pairs:
        delta = scoring.delta_peak(scoring.number(control, "peak_k"), scoring.number(floored, "peak_k"))
        if delta is None:
            continue

        everything.append(delta)
        if (scoring.number(floored, "floored") or 0) > 0:
            deltas.append((delta, key))
            by_scenario[key[2]].append(delta)

    if not deltas:
        verdict("cost", "the cost: what the floor moves a peak by", None,
                "the floor engaged on no cell in this dataset, so its price is unmeasured (`E8`)")
    else:
        values = [d for d, _ in deltas]
        p = scoring.percentiles(values)
        decision = scoring.cap_decision(p["p99"])

        print("\n        %-18s%9s%12s%12s%12s%12s"
              % ("scenario", "cells", "dpeak p50", "dpeak p95", "dpeak p99", "dpeak max"))
        for name in ORDER + sorted(set(by_scenario) - set(ORDER)):
            if name not in by_scenario:
                continue
            q = scoring.percentiles(by_scenario[name])
            print("        %-18s%9s%12.4f%12.4f%12.4f%12.4f"
                  % (name, format(len(by_scenario[name]), ","),
                     q["p50"], q["p95"], q["p99"], q["max"]))

        print("        %-18s%9s%12.4f%12.4f%12.4f%12.4f"
              % ("every engaged cell", format(len(values), ","),
                 p["p50"], p["p95"], p["p99"], p["max"]))

        allp = scoring.percentiles(everything)
        print("        %-18s%9s%12.4f%12.4f%12.4f%12.4f"
              % ("every cell walked", format(len(everything), ","),
                 allp["p50"], allp["p95"], allp["p99"], allp["max"]))
        print("        the band is read against the engaged row: a cell the floor never touched has"
              " a delta of nought by construction")

        for label, q in (("p50", "p50"), ("p95", "p95"), ("p99", "p99")):
            record("floor dpeak " + label, round(p[q], 4), "K")
        record("floor dpeak max", round(p["max"], 4), "K")
        record("floor decision", decision)

        over_accepted = sum(1 for v in values if v > scoring.CAP_ACCEPTED_KELVIN)
        over_refused = sum(1 for v in values if v > scoring.CAP_REFUSED_KELVIN)
        record("floor cells over the accepted kelvin", over_accepted, "cells")
        record("floor cells over the refused kelvin", over_refused, "cells")

        verdict("cost", "the cost: what the floor moves a peak by",
                p["p99"] < scoring.FLOOR_COST_P99_KELVIN,
                "p99 %.4f K against the predicted p99 under %g K, and a maximum of %.4f K\n"
                "        %s of %s engaged cells move more than the %g K this mod already accepts "
                "and %s more than the %g K it refuses"
                % (p["p99"], scoring.FLOOR_COST_P99_KELVIN, p["max"],
                   format(over_accepted, ","), format(len(values), ","),
                   scoring.CAP_ACCEPTED_KELVIN, format(over_refused, ","),
                   scoring.CAP_REFUSED_KELVIN))

        print("\n        the rule, fixed before the walk: at or under %g K it ships, at or over "
              "%g K it stays a switch"
              % (scoring.CAP_ACCEPTED_KELVIN, scoring.CAP_REFUSED_KELVIN))
        print("        p99 is %.4f K  ->  %s" % (p["p99"], decision))

        # The worst cells by name, because a tail that is one ship is a different finding from a
        # tail that is a thousand, and the ships are what a reader would go and look at.
        deltas.sort(key=lambda d: d[0], reverse=True)
        print("\n        the ten furthest-apart cells")
        for delta, key in deltas[:10]:
            print("        %10.4f K  %-44s %s" % (delta, key[0][:44], key[2]))

    if CSV_OUT:
        scoring.write_summary(CSV_OUT, FIGURES)
        print("\ncsv -> %s  (%d figures)" % (CSV_OUT, len(FIGURES)))


if __name__ == "__main__":
    main()
