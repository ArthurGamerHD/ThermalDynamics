"""**Two walks that saw the same ship should have written the same row.**

`verdict.py --baseline` compares two datasets' *summaries*, which is the right instrument for
asking whether a population moved. This is the other question: whether two runs that overlap
produced the same numbers on the ships they share — a reproduction rather than a comparison (`E7`).

It exists because a walk is restarted rather than resumed whenever the code moved underneath it
(`M1`), and a restart's overlap with what the old run finished is a free reproduction check. On
2026-08-25 the cap walk's first 552 rows matched the abandoned 2026-08-24 partial **exactly**, on
every column, which turned *three commits touched the harness and none was checked* from an
assumption into a measurement.

**A row is keyed by ship, workshop id, scenario and arm**, because a paired walk writes two rows per
ship and scenario and they differ only in the arm. Rows only one side has are reported as a count,
not dropped silently: a walk that overlapped in nothing would otherwise print a clean reproduction.

    python3 tools/corpus/reproduce.py out/cap-2026-08-24 out/cap-2026-08-25

The rules argued here are stated canonically in [rules.md](../../docs/rules.md): `E7` `E8` `M1`.
"""
import argparse
import csv
import os
import sys

COLUMNS = [
    "peak_k", "mean_k", "median_k", "p95_k", "min_k",
    "substeps_demanded", "substeps_granted", "substep_cost", "run_seconds",
    "links", "blocks", "grids", "floored",
    "vented_w", "made_w", "radiation_w", "convection_w", "solar_w", "generation_w",
    "seconds_to_settle", "seconds_to_critical", "over_critical",
]


# rows operation.
def rows(directory, arm=None):
    """Every outcome row of a dataset, keyed by ship, workshop id, scenario and arm.

    `arm` keeps only the rows of a paired walk whose `cap` matches, which is what makes a paired
    dataset comparable with an unpaired one — `CorpusCapWalk`'s control arm *is* `CorpusAirWalk`
    repeated, and reproducing that is the point of running the two.
    """
    path = os.path.join(directory, "outcomes.csv")
    if not os.path.exists(path):
        print(f"no outcomes at {path}")
        sys.exit(1)

    keyed = {}
    with open(path, newline="", encoding="utf-8") as handle:
        for row in csv.DictReader(handle):
            if arm is not None and "cap" in row and normalise(row["cap"]) != normalise(arm):
                continue

            keyed[key(row)] = row

    return keyed


# normalise operation.
def normalise(cap):
    """`0` and a missing column are the same arm: the cap off, which is what ships."""
    return "" if cap in (None, "", "0") else cap


# key operation.
def key(row):
    """A row's identity, with the arm normalised so an unpaired walk keys like a control arm."""
    return (row.get("ship", ""), row.get("workshop_id", ""),
            row.get("scenario", ""), normalise(row.get("cap", "")))


# difference operation.
def difference(before, after, column):
    """Relative difference in one column, or None where either side does not carry it."""
    try:
        a = float(before[column])
        b = float(after[column])
    except (KeyError, TypeError, ValueError):
        return None

    if a == b:
        return 0.0

    return abs(a - b) / max(abs(a), 1e-9)


# compare operation.
def compare(before, after, tolerance):
    """`(shared, mismatches, worst)` over the rows both datasets carry."""
    shared = sorted(set(before) & set(after))
    mismatches = []
    worst = {}

    for entry in shared:
        for column in COLUMNS:
            relative = difference(before[entry], after[entry], column)
            if relative is None:
                continue

            if column not in worst or relative > worst[column][0]:
                worst[column] = (relative, entry, before[entry][column], after[entry][column])

            if relative > tolerance:
                mismatches.append((entry, column,
                                   before[entry][column], after[entry][column], relative))

    return shared, mismatches, worst


# main operation.
def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("before")
    parser.add_argument("after")
    parser.add_argument("--tolerance", type=float, default=1e-6,
                        help="relative difference that counts as a mismatch (default 1e-6)")
    parser.add_argument("--show", type=int, default=10, help="mismatches to print")
    parser.add_argument("--arm", default="0",
                        help="which arm of a paired walk to compare (default 0, the cap off)")
    args = parser.parse_args()

    before = rows(args.before, args.arm)
    after = rows(args.after, args.arm)

    shared, mismatches, worst = compare(before, after, args.tolerance)

    print(f"{args.before}: {len(before)} rows")
    print(f"{args.after}: {len(after)} rows")
    print(f"shared: {len(shared)}; only in the first: {len(set(before) - set(after))}; "
          f"only in the second: {len(set(after) - set(before))}")

    if not shared:
        print("nothing in common, so this compared nothing")
        return 1

    if not mismatches:
        biggest = max(worst.items(), key=lambda item: item[1][0]) if worst else None
        print(f"reproduced: {len(shared)} rows agree on {len(worst)} columns"
              + (f", worst relative difference {biggest[1][0]:.2e} in {biggest[0]}"
                 if biggest else ""))
        return 0

    print(f"{len(mismatches)} cells differ by more than {args.tolerance:g}:")
    mismatches.sort(key=lambda item: -item[4])

    for entry, column, a, b, relative in mismatches[:args.show]:
        ship, _, scenario, cap = entry
        print(f"  {ship} / {scenario} / cap {cap}: {column} {a} -> {b} ({relative:.2e})")

    if len(mismatches) > args.show:
        print(f"  … and {len(mismatches) - args.show} more")

    return 1


if __name__ == "__main__":
    sys.exit(main())
