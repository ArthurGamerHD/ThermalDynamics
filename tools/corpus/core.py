"""The core corpus: a walk of the population that finishes in an hour instead of eleven.

A full walk of the 8,144-ship corpus costs 5.1 hours in air and about 11 in the paired cap
arms. That price is not spread evenly and it is not spread the way
anyone guesses: measured on `out/air-2026-08-28`, the **11.5 % of ships over 10,000 blocks carry
79 % of the cost**, and the 88.5 % under it carry 21 %. So the corpus is nearly all cheap ships and
the walk is nearly all expensive ones.

**The obvious cut is the wrong one, and it is wrong by a measured 82 %.** Dropping every ship over
10,000 blocks gives a walk of 63 minutes — and every one of the 712 runs over the visit allowance
lives in exactly those ships, so `G6`'s cost half reads *zero runs over* and its p99 falls 82 %
below the population's. A size-truncated corpus is not a cheaper corpus, it is a corpus with the
finding deleted.

**What works instead is a stratified sample read with weights.** Every ship is kept or dropped by a
rule on its block count, a kept ship carries the number of ships it stands for, and every figure is
taken over those counts (`weighted_percentile`). The cheap strata are kept **whole**, because they
cost almost nothing — every ship under 2,000 blocks is 63.9 % of the corpus and 4.5 % of the walk —
and the giants are sampled hard, because that is where the money is.

**The rare events need a take-all stratum of their own.** Twenty runs in the whole population go
critical in air and they come from **eight ships**, which cost 2.3 % of the walk between them. Any
sampling fraction that makes the giants affordable loses all eight, and `G1`'s share goes to zero —
not because the population changed but because the event is rarer than the sample. So the eight are
named and always walked.

Usage: core.py <reference-dataset> [--out <selection.txt>] [--weights <weights.csv>]
       core.py --score <core-dataset> [--weights <weights.csv>]

`--score` is the **reader for a walk of the core corpus**, and it is the only one. A core dataset
read by `verdict.py` is read without weights, which reports a population two thirds of whose giants
are missing -- so `verdict.py` names such a dataset and sends the reader here rather than printing
figures that look like population figures. One weighted implementation, not a weighted mode bolted
onto an unweighted report (`P5`).

The reference dataset supplies three things and is named in the output rather than assumed: the
block count that sorts every ship into a stratum, the eight ships the take-all rule is about, and
the population figures the fidelity table at the bottom scores the selection against.

**What the core corpus can and cannot answer is measured, not asserted** — the table this prints
is the selection scored against the very dataset it was drawn from, which is the strongest check
available without walking anything (`P4`). Read it before quoting a core-corpus figure as a
population one.
"""
import collections
import csv
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import scoring

STRATA = [
    (2000, 1),        # 63.9 % of the corpus and 4.5 % of the walk: keeping it whole is free
    (5000, 10),
    (10000, 8),
    (15000, 20),
    (22000, 20),
    (32000, 20),
    (45000, 20),
    (65000, 20),
    (10 ** 9, 20),
]

AIR_MINUTES = 303.6
CAP_OVER_AIR = 2.19




# read operation.
def read(directory):
    """Every outcome row of a walk, keyed by the ship it belongs to."""
    path = os.path.join(directory, "outcomes.csv")
    if not os.path.exists(path):
        raise SystemExit(path + " does not exist, so there is nothing to draw a selection from")

    rows = list(csv.DictReader(open(path)))

    arms, shipped = scoring.split_arms(rows)
    if arms:
        print("note: this is a paired walk carrying arms %s; scoring the %d rows of the arm that"
              " ships and leaving %d to cap.py"
              % (", ".join(a or "(absent)" for a in arms), len(shipped), len(rows) - len(shipped)))
        rows = shipped

    ships = collections.defaultdict(list)
    for row in rows:
        row["_work"] = scoring.step_work(
            scoring.number(row, "substep_cost"), scoring.number(row, "substeps_demanded"))
        row["_critical"] = 1.0 if scoring.number(row, "over_critical") else 0.0
        ships[row["workshop_id"]].append(row)
    return rows, ships


# paths operation.
def paths(directory):
    """Where each ship's blueprint is, from the walk's own record of what it finished."""
    found = {}
    record = os.path.join(directory, "done-air.txt")
    if not os.path.exists(record):
        for name in os.listdir(directory):
            if name.startswith("done-") and name.endswith(".txt"):
                record = os.path.join(directory, name)
                break
    if not os.path.exists(record):
        return found
    for line in open(record):
        line = line.strip()
        if not line or "/content/244850/" not in line:
            continue
        found[line.split("/content/244850/")[1].split("/")[0]] = line
    return found


# cost operation.
def cost(rows, blocks):
    """What one ship costs a walk, in the unit the whole-walk timings were validated against.

    Walk cost is the *assembly's* work over the run -- run seconds times substeps times
    `links + 4 x nodes` summed over the ship's scenarios -- and it is deliberately not
    `step_work`. `step_work` is the most expensive **grid**, because `G6`'s allowance is per grid;
    the walk simulates every grid, so the two quantities differ on a multi-grid hull and only one
    of them predicts wall time. Cumulative cost on this definition tracks the air walk's own
    measured minutes to within about five points across the corpus.
    """
    total = 0.0
    for row in rows:
        seconds = scoring.number(row, "run_seconds") or 0.0
        substeps = scoring.number(row, "substeps_demanded") or 0.0
        links = scoring.number(row, "links") or 0.0
        total += seconds * substeps * (links + 4.0 * blocks)
    return total


# select operation.
def select(ships):
    """The core corpus, as `{ship: (weight, rule)}`.

    Take-all first, then a systematic one-in-N through each stratum's remainder. Systematic rather
    than random for two reasons: it is reproducible without carrying a seed, and sorting the
    stratum by block count before striding it spreads the draw across the band instead of leaving
    that to chance.
    """
    watch = sorted(w for w, rows in ships.items() if any(r["_critical"] for r in rows))

    chosen = {}
    for ship in watch:
        chosen[ship] = (1.0, "critical in the reference walk")

    banded = collections.defaultdict(list)
    for ship, rows in ships.items():
        if ship in chosen:
            continue
        blocks = scoring.number(rows[0], "blocks") or 0.0
        for index, (bound, _) in enumerate(STRATA):
            if blocks < bound:
                banded[index].append((blocks, ship))
                break

    for index, (bound, one_in) in enumerate(STRATA):
        members = sorted(banded.get(index, []))
        if not members:
            continue
        keep = max(1, int(round(len(members) / float(one_in))))
        step = len(members) / float(keep)
        picked = []
        for slot in range(keep):
            at = min(len(members) - 1, int(round(slot * step)))
            if members[at][1] not in picked:
                picked.append(members[at][1])
        weight = len(members) / float(len(picked))
        low = 0 if index == 0 else STRATA[index - 1][0]
        rule = "%d-%d blocks, one in %d" % (low, bound, one_in) if bound < 10 ** 9 \
            else "%d+ blocks, one in %d" % (low, one_in)
        for ship in picked:
            chosen[ship] = (weight, rule)

    return chosen, watch


# fidelity operation.
def fidelity(rows, ships, chosen):
    """The selection scored against the population it was drawn from."""
    kept = []
    for ship, (weight, _) in chosen.items():
        for row in ships[ship]:
            kept.append((row, weight))

# population operation.
    def population(pick):
        return [pick(r) for r in rows if pick(r) is not None]

# sample operation.
    def sample(pick):
        return [(pick(r), w) for r, w in kept if pick(r) is not None]

    work = lambda r: r["_work"]
    demand = lambda r: scoring.number(r, "substeps_demanded")

    out = []
    for label, q in (("work p50", 0.5), ("work p95", 0.95), ("work p99", 0.99)):
        out.append((label, scoring.percentile(population(work), q),
                    scoring.weighted_percentile(sample(work), q)))
    out.append(("work max", max(population(work)), max(v for v, _ in sample(work))))
    out.append(("demand p99", scoring.percentile(population(demand), 0.99),
                scoring.weighted_percentile(sample(demand), 0.99)))

    allowance = scoring.SHIPPED_VISIT_ALLOWANCE
    over = sum(1 for r in rows if r["_work"] and r["_work"] > allowance) / float(len(rows))
    over_w = (sum(w for r, w in kept if r["_work"] and r["_work"] > allowance)
              / sum(w for _, w in kept))
    out.append(("over allowance", over, over_w))
    crit = sum(r["_critical"] for r in rows) / float(len(rows))
    crit_w = sum(r["_critical"] * w for r, w in kept) / sum(w for _, w in kept)
    out.append(("critical rate", crit, crit_w))
    return out


WEIGHTS_DEFAULT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "core-corpus.csv")


# load weights operation.
def load_weights(path):
    """The committed selection: what each kept ship stands for, and the rule that kept it."""
    if not os.path.exists(path):
        raise SystemExit(path + " does not exist, so nothing says what a kept ship stands for")
    weights = {}
    for row in csv.DictReader(open(path)):
        weights[row["workshop_id"]] = (float(row["weight"]), row.get("rule", ""))
    return weights


# score operation.
def score(directory, weights):
    """A walk of the core corpus, read with the weights that make it a population estimate."""
    rows, ships = read(directory)
    kept = [(r, weights[r["workshop_id"]][0]) for r in rows if r["workshop_id"] in weights]
    strays = set(r["workshop_id"] for r in rows) - set(weights)
    if not kept:
        raise SystemExit("no ship in " + directory + " is in the selection, so this is not a core walk")

    print("core walk %s" % directory)
    print("  %d of %d rows carry a weight; %d ships walked are not in the selection"
          % (len(kept), len(rows), len(strays)))
    if strays:
        print("  ** those %d ships are dropped: a figure here is over the selection, not over what"
              " was walked **" % len(strays))
    represented = sum(w for _, w in kept)
    print("  the selection stands for %.0f ship-runs of the population" % represented)

    allowance = scoring.SHIPPED_VISIT_ALLOWANCE
    work = [(r["_work"], w) for r, w in kept if r["_work"]]
    demand = [(scoring.number(r, "substeps_demanded"), w) for r, w in kept
              if scoring.number(r, "substeps_demanded") is not None]
    print()
    print("  %-22s %16s" % ("statistic", "core (weighted)"))
    for label, q in (("G6 work p50", 0.5), ("G6 work p95", 0.95), ("G6 work p99", 0.99)):
        print("  %-22s %16.6g" % (label, scoring.weighted_percentile(work, q)))
    print("  %-22s %16.6g" % ("G6 visit allowance", allowance))
    over = sum(w for v, w in work if v > allowance) / represented * 100.0
    print("  %-22s %15.4f %%" % ("G6 runs over allowance", over))
    for label, q in (("G6 demand p50", 0.5), ("G6 demand p95", 0.95), ("G6 demand p99", 0.99)):
        print("  %-22s %16.6g" % (label, scoring.weighted_percentile(demand, q)))
    crit = sum(r["_critical"] * w for r, w in kept) / represented * 100.0
    print("  %-22s %15.4f %%" % ("G1 idle critical share", crit))
    print()
    print("  **Rare statistics are the ones this cannot resolve.** Measured out of sample against")
    print("  two other walks, the central figures land within a few per cent and the rare ones do")
    print("  not: the survey's over-allowance rate is 0.68 % of runs and reads 24 % low. Read a")
    print("  figure here as an estimate with that character, and the whole corpus for a tail.")


# main operation.
def main():
    if "--score" in sys.argv:
        return score(scoring.flag("--score"),
                     load_weights(scoring.flag("--weights", WEIGHTS_DEFAULT)))

    args = scoring.positionals(("--out", "--weights", "--score"))
    if not args:
        raise SystemExit("core.py <reference-dataset> [--out <file>] [--weights <file>]"
                         "  |  core.py --score <core-dataset>")
    directory = args[0]

    rows, ships = read(directory)
    where = paths(directory)
    chosen, watch = select(ships)

    total = sum(cost(rs, scoring.number(rs[0], "blocks") or 0.0) for rs in ships.values())
    kept_cost = sum(cost(ships[s], scoring.number(ships[s][0], "blocks") or 0.0) for s in chosen)
    share = kept_cost / total if total else 0.0

    print("core corpus drawn from %s" % directory)
    print("  %d of %d ships (%.1f %%), %.2f %% of the walk's cost"
          % (len(chosen), len(ships), 100.0 * len(chosen) / len(ships), 100.0 * share))
    print("  a full air walk is %.0f min -> core %.0f min;  cap walk %.0f min -> core %.0f min"
          % (AIR_MINUTES, AIR_MINUTES * share,
             AIR_MINUTES * CAP_OVER_AIR, AIR_MINUTES * CAP_OVER_AIR * share))

    byrule = collections.defaultdict(int)
    for _, (weight, rule) in chosen.items():
        byrule[rule] += 1
    print()
    print("  %-34s %6s %8s" % ("rule", "ships", "weight"))
    for rule in sorted(byrule, key=lambda r: (r != "critical in the reference walk", r)):
        weights = set(round(w, 4) for w, r in chosen.values() if r == rule)
        print("  %-34s %6d %8s" % (rule, byrule[rule],
                                   "/".join("%.2f" % w for w in sorted(weights))))

    print()
    print("  %-16s %14s %16s %9s" % ("statistic", "population", "core (weighted)", "error"))
    for label, truth, estimate in fidelity(rows, ships, chosen):
        error = 100.0 * (estimate - truth) / truth if truth else float("nan")
        print("  %-16s %14.6g %16.6g %+8.1f %%" % (label, truth, estimate, error))

    out = scoring.flag("--out", None)
    if out:
        missing = [s for s in chosen if s not in where]
        if missing:
            raise SystemExit("%d selected ships have no path in %s" % (len(missing), directory))
        with open(out, "w") as handle:
            handle.write("# The core corpus: %d of %d ships, %.2f %% of a full walk's cost.\n"
                         % (len(chosen), len(ships), 100.0 * share))
            handle.write("# Drawn from %s by tools/corpus/core.py, which states the rule that\n"
                         % directory)
            handle.write("# picked every ship and the fidelity of the result (M10, E2).\n")
            handle.write("# Weights are in the .csv beside this file and a figure taken over this\n")
            handle.write("# selection without them is a figure about a different population.\n")
            for ship in sorted(chosen, key=lambda s: where[s]):
                handle.write(where[ship] + "\n")
        print("\nwrote %s" % out)

    weights_out = scoring.flag("--weights", None)
    if weights_out:
        with open(weights_out, "w") as handle:
            handle.write("workshop_id,weight,rule\n")
            for ship in sorted(chosen):
                weight, rule = chosen[ship]
                handle.write('%s,%.6f,"%s"\n' % (ship, weight, rule))
        print("wrote %s" % weights_out)


if __name__ == "__main__":
    main()
