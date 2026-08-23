#!/usr/bin/env python3
"""What the conversion to real conductances did to the ships people fly.

Reads the dataset `ConductanceRetestWalk` writes and prints, per scenario, what each arm of the
conversion moved on the retest set. The walk records the shipped world and four counterfactual
worlds read off `Data/Cubes.xml` as it stood before the conversion (`4f6b44a^`), so every column
here is a shipped-minus-counterfactual difference on the same forty hulls.

**The arms are read against the shipped world, not against each other.** Conduction is a network
and two stiffened families in series do not add, so the composite is not the sum of the arms and
this prints all five rather than inviting the subtraction.

**Reach is printed first and it is not decoration.** An arm that reaches no block on this set
reports "no change" in exactly the shape of an arm that reached every block and changed nothing,
and one of the four reaches nothing here by construction — the corpus filters admit vanilla ships,
so none of the forty carries a coolant pipe or a radiator.

Usage: retest.py [data-dir]
"""
import csv
import os
import statistics
import sys

DATA = sys.argv[1] if len(sys.argv) > 1 else "out/retest-2026-08-23"

CONTROL = "shipped"

# The key that identifies one measurement: a ship in a scenario. The world is the axis being
# compared, so it is not part of the key.
KEY = ("ship", "workshop_id", "scenario")


def number(row, key):
    try:
        return float(row[key])
    except (TypeError, ValueError, KeyError):
        return None


def load(name):
    path = os.path.join(DATA, name + ".csv")
    if not os.path.exists(path):
        return []
    with open(path) as handle:
        return list(csv.DictReader(handle))


def median(values):
    return statistics.median(values) if values else float("nan")


def main():
    rows = load("retest")
    reach = load("reach")

    if not rows:
        print(f"no retest.csv in {DATA}. Run:")
        print("  THERMAL_CORPUS_TESTS=1 THERMAL_CORPUS_DATA=<dir> \\")
        print('      dotnet test --filter "FullyQualifiedName~ConductanceRetestWalk"')
        return 1

    worlds = []
    for row in rows:
        if row["world"] not in worlds:
            worlds.append(row["world"])

    ships = {(r["ship"], r["workshop_id"]) for r in rows}
    scenarios = []
    for row in rows:
        if row["scenario"] not in scenarios:
            scenarios.append(row["scenario"])

    print(f"{DATA}: {len(rows)} rows, {len(ships)} ships, {len(scenarios)} scenarios, "
          f"{len(worlds)} worlds")
    print()

    # ---- reach -----------------------------------------------------------------------------
    print("What each arm could reach")
    print()
    print(f"{'arm':<22} {'blocks retuned':>15} {'of blocks':>11} {'share':>8} {'ships':>7}")

    blocks_by_world = {}
    for row in reach:
        world = row["world"]
        got = blocks_by_world.setdefault(world, [0, 0, 0])
        got[0] += int(row["retuned_blocks"])
        got[1] += int(row["blocks"])
        got[2] += 1 if int(row["retuned_blocks"]) > 0 else 0

    for world in worlds:
        if world == CONTROL:
            continue
        retuned, blocks, touched = blocks_by_world.get(world, (0, 0, 0))
        share = f"{retuned / blocks:.1%}" if blocks else "-"
        print(f"{world:<22} {retuned:>15,} {blocks:>11,} {share:>8} {touched:>7}")

    print()
    print("An arm on zero blocks measures nothing, and every figure it prints below is a control")
    print("against itself. That is the expected result for the mod's own blocks on a vanilla corpus.")
    print()

    # ---- the effect ------------------------------------------------------------------------
    indexed = {}
    for row in rows:
        indexed[(row["world"],) + tuple(row[k] for k in KEY)] = row

    for column, label, unit in (
        ("peak_k", "peak temperature", "K"),
        ("over_critical", "blocks over critical", ""),
        ("seconds_to_critical", "seconds to first block over critical", "s"),
    ):
        print(f"Shipped minus pre-conversion, {label}")
        print()
        header = f"{'scenario':<18}" + "".join(f"{w:>22}" for w in worlds if w != CONTROL)
        print(header)

        for scenario in scenarios:
            line = f"{scenario:<18}"

            for world in worlds:
                if world == CONTROL:
                    continue

                deltas = []
                for ship in ships:
                    key = (ship[0], ship[1], scenario)
                    control = indexed.get((CONTROL,) + key)
                    other = indexed.get((world,) + key)
                    if control is None or other is None:
                        continue

                    a = number(control, column)
                    b = number(other, column)
                    if a is None or b is None:
                        continue

                    # A run that never crossed reports -1, which is not a duration and must not be
                    # averaged with ones that are.
                    if column == "seconds_to_critical" and (a < 0 or b < 0):
                        continue

                    deltas.append(a - b)

                if not deltas:
                    line += f"{'-':>22}"
                else:
                    moved = sum(1 for d in deltas if abs(d) > 1e-3)
                    line += f"{median(deltas):>+14.2f}{unit} {moved:>3}/{len(deltas)}"

            print(line)

        print()

    print("Each cell is the median over the ships the scenario could be read from, and the count")
    print("beside it is how many of them moved at all. Positive means the shipped world is the")
    print("hotter, later or more damaged one.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
