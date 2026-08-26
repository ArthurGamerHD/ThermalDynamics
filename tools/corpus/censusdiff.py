#!/usr/bin/env python3
"""**What changed between two censuses, and whether the predictions written before the run hold.**

`reproduce.py` asks whether two walks that saw the same ship wrote the same row — a check that
nothing moved. This asks the opposite question, for the case where something was *meant* to move: a
definition changed, or the blueprint reader was fixed, and the census was re-taken. It reports the
size of the move per ship and per block type, and it scores it against a rule given on the command
line rather than against a threshold typed in here.

    python3 tools/corpus/censusdiff.py out/census-2026-08-21 out/census-2026-08-25

Written for [backlog.md](../../docs/backlog.md) `A13`, where the reader built eleven kinds of
vanilla block as armour and every dataset on disk was taken through it. The predictions for that
re-take are registered in [balance-lab.md](../../docs/balance-lab.md) — a rise of 8 to 10 % in
full-load waste, 60 to 75 % of ships touched, an unchanged ship count, and the geometry columns
moving where the heat does not — and `--expect` scores them here so the verdict is computed rather
than read off by eye (`E5`).

**Ships only one side has are reported, never dropped.** A re-take that lost half the population
would otherwise print a small, clean delta over the half it kept, which is the exact shape of a
figure that looks like evidence and is not (`E8`).

The rules argued here are stated canonically in [rules.md](../../docs/rules.md): `E5` `E7` `E8`
`P1`.
"""
import argparse
import csv
import os
import sys

# Columns worth comparing, and what each one answers. Everything else in a census row is an
# identifier, a name, or a figure derived from these.
HEAT = ["waste_idle_w", "waste_full_w", "waste_burn_w", "installed_power_w", "consumer_draw_w"]

GEOMETRY = ["blocks", "grids", "joints", "rooms", "exposed_blocks", "buried_blocks",
            "exposed_area_m2", "thermal_mass_j_per_k", "sealed_blocks"]

COLUMNS = HEAT + GEOMETRY


def key_of(row):
    return (row["ship"], row["workshop_id"])


def number(row, column):
    try:
        return float(row[column])
    except (TypeError, ValueError, KeyError):
        return 0.0


def read(directory, name):
    """One census file as a dict keyed by ship, or an empty dict when it is not there."""
    path = os.path.join(directory, name)
    if not os.path.exists(path):
        return {}, path

    with open(path, newline="", encoding="utf-8") as handle:
        return {key_of(r): r for r in csv.DictReader(handle)}, path


def composition(directory):
    """Watts of full-load waste by type id, and how many ships carry each."""
    path = os.path.join(directory, "composition.csv")
    if not os.path.exists(path):
        return {}, {}

    watts, ships = {}, {}
    with open(path, newline="", encoding="utf-8") as handle:
        for row in csv.DictReader(handle):
            type_id = row["type_id"]
            watts[type_id] = watts.get(type_id, 0.0) + number(row, "waste_full_w")
            ships.setdefault(type_id, set()).add(key_of(row))

    return watts, {t: len(s) for t, s in ships.items()}


def share(before, after):
    """`after / before - 1` as a percentage, or None where there is nothing to divide by."""
    if before == 0:
        return None
    return (after / before - 1.0) * 100.0


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("before")
    parser.add_argument("after")
    parser.add_argument("--expect", action="append", default=[], metavar="NAME=LOW:HIGH",
                        help="score a named quantity against a band, e.g. waste_full_w=8:10")
    parser.add_argument("--types", type=int, default=12,
                        help="how many block types to list, by the watts they moved")
    args = parser.parse_args(argv)

    before, before_path = read(args.before, "census.csv")
    after, after_path = read(args.after, "census.csv")

    if not before or not after:
        print(f"no census to read: {before_path} has {len(before):,} ships,"
              f" {after_path} has {len(after):,}")
        return 2

    shared = sorted(set(before) & set(after))
    only_before = sorted(set(before) - set(after))
    only_after = sorted(set(after) - set(before))

    print(f"{args.before} -> {args.after}")
    print(f"  {len(before):,} ships before, {len(after):,} after,"
          f" {len(shared):,} in both")
    print(f"  {len(only_before):,} only in the first, {len(only_after):,} only in the second")

    if not shared:
        print("  nothing is in both, so nothing below would be a comparison")
        return 1

    print()
    print("over the ships both censuses hold:")
    print(f"  {'column':<24} {'before':>18} {'after':>18} {'move':>9}  ships moved")

    moves = {}
    for column in COLUMNS:
        total_before = sum(number(before[k], column) for k in shared)
        total_after = sum(number(after[k], column) for k in shared)
        moved = sum(1 for k in shared
                    if number(before[k], column) != number(after[k], column))

        percent = share(total_before, total_after)
        moves[column] = (percent, moved)

        shown = "     —" if percent is None else f"{percent:8.2f} %"
        print(f"  {column:<24} {total_before:18,.0f} {total_after:18,.0f} {shown}"
              f"  {moved:,} ({moved / len(shared) * 100:.1f} %)")

    watts_before, ships_before = composition(args.before)
    watts_after, ships_after = composition(args.after)

    if watts_before or watts_after:
        print()
        print("full-load waste by block type, largest move first:")
        print(f"  {'type':<32} {'before W':>16} {'after W':>16}  carriers")

        types = set(watts_before) | set(watts_after)
        ranked = sorted(types,
                        key=lambda t: abs(watts_after.get(t, 0.0) - watts_before.get(t, 0.0)),
                        reverse=True)

        for type_id in ranked[:args.types]:
            b = watts_before.get(type_id, 0.0)
            a = watts_after.get(type_id, 0.0)
            if b == 0.0 and a == 0.0:
                continue
            print(f"  {type_id:<32} {b:16,.0f} {a:16,.0f}"
                  f"  {ships_before.get(type_id, 0):,} -> {ships_after.get(type_id, 0):,}")

    failed = 0
    if args.expect:
        print()
        print("registered predictions, scored:")

        for expectation in args.expect:
            name, _, band = expectation.partition("=")
            low, _, high = band.partition(":")

            try:
                low, high = float(low), float(high)
            except ValueError:
                print(f"  {name:<24} cannot read the band '{band}'")
                failed += 1
                continue

            if name == "ships":
                actual = share(len(before), len(after))
            elif name in moves:
                actual = moves[name][0]
            else:
                print(f"  {name:<24} is not a column this compares")
                failed += 1
                continue

            if actual is None:
                print(f"  {name:<24} has nothing to divide by, so it is not scored")
                failed += 1
                continue

            holds = low <= actual <= high
            if not holds:
                failed += 1
            print(f"  {name:<24} {actual:8.2f} %  against {low} to {high}"
                  f"  {'holds' if holds else 'FAILS'}")

    print()
    print("basis: both censuses are a build of every ship rather than a simulation, so these are"
          " the terms a definition decides and not a temperature.")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
