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

**A walk of the core corpus is read with its weights, and this report says so on its first line.**
The core corpus keeps every small ship and one giant in twenty, so a pair drawn from it stands for
up to twenty pairs of the population; counted once, the report would describe a population that is
mostly small ships, which is not the corpus and is not anything (`P1`, `E2`). The dataset is
recognised by `scoring.is_core_walk` rather than by a flag, because a flag is a thing a reader
forgets and a silent wrong answer is exactly what this whole path exists to prevent.

**Two of the four predictions cannot be scored on a core walk and are reported as unscored rather
than as passing.** A maximum is one observation and cannot be sampled — the core corpus reads 76 %
under the population's `work max` — so the cost prediction's *max under 10 K* half is `?` on a
sampled dataset, and so is any rate below about half a per cent. The percentiles, the medians, the
demand half and the reach share are what a core walk answers (`E8`).

**Every figure this prints has a place in a committed summary.** `--csv <path>` writes them as
`statistic,value,unit` beside the dataset's provenance, which is what makes a number quoted on a
page checkable against the walk that produced it rather than against another page (`E5`). The
statistics carry `weighted` in their names on a sampled walk, so a core-walk figure and a
full-walk figure can never be compared as though they were the same reading.

Usage: cap.py [data-dir] [--csv <path>]
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
for _name in ("--csv",):
    if _name in sys.argv and sys.argv.index(_name) + 1 < len(sys.argv):
        FLAGGED.add(sys.argv[sys.argv.index(_name) + 1])
ARGS = [a for a in ARGS if a not in FLAGGED]

DATA = ARGS[0] if ARGS else "out/cap-2026-08-24"
CSV_OUT = (sys.argv[sys.argv.index("--csv") + 1]
           if "--csv" in sys.argv and sys.argv.index("--csv") + 1 < len(sys.argv) else None)

# Every figure worth quoting, in the order it was produced, as `(statistic, value, unit)`. The
# same shape `verdict.py` writes, so one reader can hold a cap summary and a survey summary
# against each other without knowing which tool wrote which (`P5`, `E5`).
FIGURES = []


def record(statistic, value, unit=""):
    FIGURES.append((statistic, value, unit))

# The order the scenarios are printed in: the anchor first, then the three with air in them, in
# rising wind. The same order `air.py` uses, so two reports of one experiment read alike.
ORDER = ["vacuum-shadow", "surface-hot-noon", "storm-parked", "reentry"]

# The scenario with no air in it. It is the walk's control and is not part of *in air*, which is how
# the reach prediction is worded.
ANCHOR = "vacuum-shadow"

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


def weights(rows):
    """What each walked ship stands for, and whether this dataset is a sample at all.

    Returns `(weight_by_ship, core)`. A full-corpus walk gets every weight at 1 and `core` false,
    which is the unweighted arithmetic this report has always done — one implementation with the
    sample case as its general form rather than a weighted mode bolted on beside it (`P5`).

    **The core case is detected, not declared.** `scoring.is_core_walk` tells a core dataset from a
    full one by what it does *not* contain, so a reader who forgets which walk produced a directory
    still gets the right report. A ship in a core dataset that carries no weight is a stray and is
    counted for the banner rather than given a default of 1: a default here would be the silent
    wrong answer (`E4`).
    """
    walked = set(row.get("workshop_id") for row in rows)
    if not scoring.is_core_walk(walked):
        return {ship: 1.0 for ship in walked}, False

    selection = {}
    with open(scoring.CORE_SELECTION, newline="", encoding="utf-8") as handle:
        for row in csv.DictReader(handle):
            selection[row["workshop_id"]] = float(row["weight"])
    return {ship: selection[ship] for ship in walked if ship in selection}, True


def pair(rows, weight_by_ship):
    """Rows grouped into `(control, capped, weight)` by ship, workshop id and scenario.

    Returns the pairs, the caps seen, and the count of rows that had no partner — which is reported
    rather than dropped in silence, because an interrupted walk leaves exactly that (`E4`, `P2`).
    A row whose ship carries no weight is dropped the same way and counted as a stray.
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
    strays = 0

    for key, arms in byKey.items():
        if key[1] not in weight_by_ship:
            strays += len(arms)
            continue

        if OFF not in arms or len(arms) < 2:
            orphans += len(arms)
            continue

        weight = weight_by_ship[key[1]]
        for cap in capped_at:
            if cap in arms:
                pairs.append((key, arms[OFF], arms[cap], weight))
            else:
                orphans += 1

    return pairs, capped_at, orphans, strays


def work(row):
    substeps = number(row, "substeps_demanded")
    if substeps is None:
        substeps = number(row, "substeps_granted")
    return scoring.step_work(number(row, "substep_cost"), substeps)


def verdict(key, label, holds, detail):
    """One scored row, printed and recorded under a name that does not move.

    `key` is what the summary calls the row and `label` is what a reader is shown; they are
    separate because the label carries the cap the walk ran and a statistic whose name changes
    with the data is a statistic no page can quote (`E5`).

    `None` is *unscored* and prints `?`, which is not the same as a failure and must not be
    recorded as one: a criterion the dataset cannot reach reports nothing rather than nought
    (`E8`). The summary carries the word rather than a boolean for the same reason.
    """
    mark = "HOLDS" if holds else ("  ?  " if holds is None else "FAILS")
    record("C3 " + key + " verdict", "unscored" if holds is None else ("holds" if holds else "fails"))
    print(f"\n[{mark:^5}] {label}")
    print(f"        {detail}")


def main():
    rows = load(os.path.join(DATA, "outcomes.csv"))
    weight_by_ship, core = weights(rows)
    pairs, capped_at, orphans, strays = pair(rows, weight_by_ship)

    ships = len(set((k[0], k[1]) for k, _, _, _ in pairs))
    print(f"paired dataset: {len(rows):,} rows, {len(pairs):,} pairs over {ships:,} ships")
    print(f"caps walked: off and {', '.join(str(c) for c in capped_at) or '(none)'}")

    if orphans:
        print(f"rows with no partner: {orphans:,} — an unfinished walk, and they are dropped")
    if strays:
        print(f"rows on ships outside the selection: {strays:,} — dropped, because nothing says "
              f"what they stand for")

    if not pairs:
        print("nothing to score")
        return

    if core:
        represented = sum(w for _, _, _, w in pairs)
        print(f"\n*** CORE CORPUS: {ships:,} ships drawn by tools/corpus/core.py, standing for "
              f"{represented:,.0f} pairs of the population ***")
        print("    Every figure below is weighted by tools/corpus/core-corpus.csv. The maxima are "
              "the sample's own")
        print("    and are not population figures — a maximum is one observation and cannot be "
              "sampled — and neither")
        print("    is any rate under about half a per cent. Percentiles, medians and shares are "
              "what this answers.")

    # **The summary says which population its figures are about, in the statistic names.** A core
    # walk's rows are `weighted`; a full walk's are not. Two summaries put side by side then
    # cannot be read as the same reading of the same thing, which is the mistake `A13`'s air half
    # made when it scored a prediction against a figure from a walk that carried no link column.
    scale = "weighted " if core else ""
    record("dataset outcome rows", len(rows))
    record("dataset ships", ships)
    record("dataset pairs", len(pairs))
    record("dataset scenarios", len(set(r.get("scenario") for r in rows)))
    record("dataset core corpus", 1 if core else 0)
    if core:
        record("dataset represents pairs", "%.0f" % sum(w for _, _, _, w in pairs))
    record("dataset rows without a partner", orphans)
    record("dataset rows outside the selection", strays)
    for statistic, value, unit in provenance_lib.summary_rows(DATA):
        record(statistic, value, unit)

    cap = capped_at[-1]
    record("C3 cap walked", cap, "substeps")

    # ---- the identity ------------------------------------------------------------------------
    # The predicted benefit was arithmetic that substituted `min(demand, cap)` into an earlier
    # walk's rows. The walk asserts this per ship; this is the population statement of it, so a
    # reader of the report is told the premise held rather than having to trust that it did.
    broken = 0
    worst = 0.0
    for _, control, capped, _ in pairs:
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

    # **Counted unweighted, deliberately.** This is an assertion about the walk — did the capped
    # arm do what a cap does — and every pair the walk ran is one observation of it. Weighting it
    # would report how much of the *population* the check covers, which is a different sentence
    # and not the one the row is making.
    verdict("identity", f"the identity: a capped demand is min(demand, {cap})", broken == 0,
            f"{broken:,} of {len(pairs):,} pairs differ by more than 1 %; the worst is "
            f"{worst:.4f} substeps  (counted over the pairs walked, unweighted: this is a check "
            f"on the walk, not an estimate of the population)")

    # ---- the benefit -------------------------------------------------------------------------
    allowance = scoring.SHIPPED_VISIT_ALLOWANCE
    columns = {}
    for name, index in (("off", 1), (f"cap {cap}", 2)):
        values = [(work(p[index]), p[3]) for p in pairs if work(p[index]) is not None]
        columns[name] = (scoring.weighted_percentiles(values) if values else {}, values)

    print(f"\n        {'arm':10}{'work p50':>14}{'work p95':>14}{'work p99':>14}"
          f"{'work max':>16}{'over':>9}{'over %':>10}")
    for name, (p, values) in columns.items():
        if not p:
            print(f"        {name:10}{'—':>14}")
            continue
        over = sum(1 for v, _ in values if not scoring.keeps_up(v))
        carried = sum(w for _, w in values)
        over_share = 100.0 * sum(w for v, w in values if not scoring.keeps_up(v)) / carried
        print(f"        {name:10}{p['p50']:>14,.0f}{p['p95']:>14,.0f}{p['p99']:>14,.0f}"
              f"{p['max']:>16,.0f}{over:>9,}{over_share:>9.3f} %")
        arm = "off" if name == "off" else "capped"
        for label, q in (("p50", "p50"), ("p95", "p95"), ("p99", "p99")):
            record(f"C3 {arm} {scale}work {label}", round(p[q], 3), "element visits")
        # **The max is the sample's own on a core walk and is named that way rather than dropped.**
        # A reader wants to know the largest thing the walk saw; what they must not do is quote it
        # as the population's, and a statistic called `sample work max` cannot be.
        record(f"C3 {arm} {'sample ' if core else ''}work max", round(p["max"], 3),
               "element visits")
        record(f"C3 {arm} runs over the allowance", over, "runs")
        record(f"C3 {arm} {scale}share over the allowance", round(over_share, 4), "%")
    if core:
        print("        the max column is the sample's own; the counts are of pairs walked and the "
              "share is weighted")

    capped_p, capped_values = columns[f"cap {cap}"]
    control_p, control_values = columns["off"]

    if capped_p:
        past = sum(1 for v, _ in capped_values if not scoring.keeps_up(v))
        low, high = scoring.CAP_BENEFIT_BAND

        # **Two answers, and they are different questions.** Whether `G6`'s cost half passes under
        # the cap is the criterion; whether the walk found what the pre-registration predicted is
        # the prediction, and its falsifier runs in both directions — a p99 *under* 1.5 M would mean
        # the projection that produced 2.5 M was wrong, even though the criterion would pass.
        verdict("benefit", f"the benefit: G6's cost half under a cap of {cap}",
                capped_p["p99"] <= allowance,
                f"work p99 {capped_p['p99']:,.0f} against {allowance:,.0f} granted, from "
                f"{control_p['p99']:,.0f} uncapped — {past:,} of {len(capped_values):,} runs still "
                f"past it")

        verdict("benefit as predicted", "the benefit, as predicted rather than as scored",
                low <= capped_p["p99"] <= high,
                f"work p99 {capped_p['p99']:,.0f} against the {low:,.0f}-{high:,.0f} registered "
                f"before the walk — a figure under the band falsifies the projection as surely as "
                f"one over it")

    # ---- the cost ----------------------------------------------------------------------------
    deltas = []
    by_scenario = collections.defaultdict(list)
    for key, control, capped, weight in pairs:
        delta = scoring.delta_peak(number(control, "peak_k"), number(capped, "peak_k"))
        if delta is None:
            continue
        deltas.append((delta, key, weight))
        by_scenario[key[2]].append((delta, weight))

    # **The same deltas split by whether the control had stopped moving**, which is a different
    # question from whether it *settled*: the settle test is an average over a chunk and a hull
    # drifting at exactly the tolerated rate passes it for ever. Reported beside the registered
    # statistic rather than instead of it — the decision rule was written against the unsplit
    # figure, and re-pointing it after the data is `E11` exactly.
    resting = []
    travelling = []
    for key, control, capped, weight in pairs:
        delta = scoring.delta_peak(number(control, "peak_k"), number(capped, "peak_k"))
        if delta is None:
            continue

        rest = scoring.at_rest(number(control, "peak_rate_k_per_s"))
        if rest is None:
            continue

        (resting if rest else travelling).append((delta, weight))

    if deltas:
        values = [(d, w) for d, _, w in deltas]
        p = scoring.weighted_percentiles(values)
        decision = scoring.cap_decision(p["p99"])
        carried = sum(w for _, w in values)

        print(f"\n        {'scenario':18}{'pairs':>9}{'dpeak p50':>12}{'dpeak p95':>12}"
              f"{'dpeak p99':>12}{'dpeak max':>12}")
        for name in ORDER + sorted(set(by_scenario) - set(ORDER)):
            if name not in by_scenario:
                continue
            q = scoring.weighted_percentiles(by_scenario[name])
            print(f"        {name:18}{len(by_scenario[name]):>9,}{q['p50']:>12.4f}"
                  f"{q['p95']:>12.4f}{q['p99']:>12.4f}{q['max']:>12.4f}")

        print(f"        {'every scenario':18}{len(values):>9,}{p['p50']:>12.4f}"
              f"{p['p95']:>12.4f}{p['p99']:>12.4f}{p['max']:>12.4f}")
        for label, q in (("p50", "p50"), ("p95", "p95"), ("p99", "p99")):
            record(f"C3 {scale}dpeak {label}", round(p[q], 4), "K")
        record(f"C3 {'sample ' if core else ''}dpeak max", round(p["max"], 4), "K")

        def moved_more_than(bound):
            """How many walked pairs moved further than `bound`, and what share of the population.

            Both, because they answer different questions and a full walk's reader has always been
            given the first. On a full walk the two agree by construction; on a core walk the count
            is of the sample and only the share is about the corpus.
            """
            return (sum(1 for v, _ in values if v > bound),
                    100.0 * sum(w for v, w in values if v > bound) / carried)

        accepted_n, accepted_share = moved_more_than(scoring.CAP_ACCEPTED_KELVIN)
        refused_n, refused_share = moved_more_than(scoring.CAP_REFUSED_KELVIN)
        whose = "of the population" if core else "of the pairs"
        record("C3 pairs over the accepted kelvin", accepted_n, "pairs")
        record(f"C3 {scale}share over the accepted kelvin", round(accepted_share, 4), "%")
        record("C3 pairs over the refused kelvin", refused_n, "pairs")
        record(f"C3 {scale}share over the refused kelvin", round(refused_share, 4), "%")
        record("C3 decision", decision)

        # **On a sampled walk the max half of this prediction is unscored rather than passed.**
        # The registered prediction is a p99 under 1 K *and* a max under 10 K; a core walk keeps
        # one giant in twenty, so its largest delta is the largest of the sample and a pass on it
        # would be a claim the dataset cannot support. `None` prints `?`, which is what an
        # unmeasured half reports (`E8`).
        holds = p["p99"] < scoring.CAP_COST_P99_KELVIN
        if core:
            holds = None if holds else False
        else:
            holds = holds and p["max"] < scoring.CAP_COST_MAX_KELVIN

        max_note = ("max {:.4f} K over the sample, which a sampled walk cannot score against the "
                    "predicted {:g} K".format(p["max"], scoring.CAP_COST_MAX_KELVIN) if core
                    else "max {:.4f} K against the predicted max under {:g} K".format(
                        p["max"], scoring.CAP_COST_MAX_KELVIN))

        verdict("cost", f"the cost: what a cap of {cap} moves a peak by", holds,
                f"p99 {p['p99']:.4f} K against the predicted p99 under "
                f"{scoring.CAP_COST_P99_KELVIN:g} K, and {max_note}\n"
                f"        {accepted_n:,} of {len(values):,} walked pairs — {accepted_share:.3f} % "
                f"{whose} — move more than the {scoring.CAP_ACCEPTED_KELVIN} K this mod already "
                f"accepts, and {refused_n:,}, {refused_share:.3f} %, more than the "
                f"{scoring.CAP_REFUSED_KELVIN} K it refuses")

        if resting or travelling:
            print(f"\n        the same deltas, split by whether the control had stopped moving")
            for name, series in (("at rest", resting), ("still travelling", travelling)):
                if not series:
                    print(f"        {name:18}{'—':>9}")
                    continue

                q = scoring.weighted_percentiles(series)
                print(f"        {name:18}{len(series):>9,}{q['p50']:>12.4f}{q['p95']:>12.4f}"
                      f"{q['p99']:>12.4f}{q['max']:>12.4f}")

            print(f"        at rest means the control's own peak was moving slower than "
                  f"{scoring.SETTLE_RATE_KELVIN_PER_SECOND:.5f} K/s when it was read, which is the "
                  f"rate the settle test tolerates")

        print(f"\n        the rule, fixed before the walk: at or under "
              f"{scoring.CAP_ACCEPTED_KELVIN} K it ships, at or over "
              f"{scoring.CAP_REFUSED_KELVIN} K it stays a switch")
        print(f"        p99 is {p['p99']:.4f} K  ->  {decision}")

        # The worst pairs by name, because a tail that is one ship is a different finding from a
        # tail that is a thousand, and the ships themselves are what a reader would go and look at.
        deltas.sort(key=lambda d: d[0], reverse=True)
        print(f"\n        the ten furthest-apart pairs")
        for delta, key, _ in deltas[:10]:
            print(f"        {delta:>10.4f} K  {key[0][:44]:44} {key[2]}")

    # ---- the reach ---------------------------------------------------------------------------
    #
    # **Scored over the air scenarios, because that is how the prediction was written**: *the cap
    # holds back 3-10 % of all blocks in air*. The anchor is vacuum-shadow and the cap is expected
    # to bind least there, so folding it in dilutes the share by a quarter and scores a band nobody
    # registered. Both are printed, and the one the band is read against is the air one.
    floored = 0
    blocks = 0
    air_floored = 0
    air_blocks = 0
    control_floored = 0

    for _, control, capped, weight in pairs:
        f = number(capped, "floored")
        b = number(capped, "blocks")
        if f is None or b is None:
            continue

        floored += f * weight
        blocks += b * weight
        control_floored += (number(control, "floored") or 0) * weight

        if capped.get("scenario") == ANCHOR:
            continue

        air_floored += f * weight
        air_blocks += b * weight

    if blocks:
        share = 100.0 * floored / blocks
        air_share = 100.0 * air_floored / air_blocks if air_blocks else 0.0
        low, high = scoring.CAP_REACH_BAND
        held = None if not air_blocks else (low <= air_share <= high)
        record(f"C3 {scale}reach in air", round(air_share, 4), "%")
        record(f"C3 {scale}reach over every scenario", round(share, 4), "%")
        record("C3 control floored", round(control_floored, 0), "node-runs")

        # A share is the one figure a weighted sample estimates cleanly, so the reach band is
        # scored on a core walk: the node-runs below are weighted counts of the population rather
        # than of the walk, which is why they do not match the row count printed above.
        verdict("reach", f"the reach: what a cap of {cap} holds back", held,
                f"in air, {air_floored:,.0f} of {air_blocks:,.0f} node-runs, {air_share:.2f} % "
                f"against the {low:g}-{high:g} % predicted\n"
                f"        over all four scenarios including the vacuum anchor, "
                f"{floored:,.0f} of {blocks:,.0f}, {share:.2f} %\n"
                f"        the control floored {control_floored:,.0f}, which must be nought or it "
                f"is not a control")


def write_summary(path):
    """The figures as `statistic,value,unit`, which is what makes them quotable (`E5`)."""
    with open(path, "w", newline="", encoding="utf-8") as handle:
        writer = csv.writer(handle)
        writer.writerow(["statistic", "value", "unit"])
        for statistic, value, unit in FIGURES:
            writer.writerow([statistic, value, unit])
    print(f"\ncsv -> {path}  ({len(FIGURES)} figures)")


if __name__ == "__main__":
    main()
    if CSV_OUT:
        write_summary(CSV_OUT)
