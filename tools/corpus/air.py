#!/usr/bin/env python3
"""What the candidate retune costs in air, which is the environment `G6` is decided in.

`pairs.py` scores `G8` — the significance window — and prints a cost column taken in vacuum, where
substeps are cheap: corpus p99 is 6.02 against 64 granted. That column cannot say whether a retune
is affordable, because the responsiveness budget is spent on convection rather than on heat. This
reads the dataset `PairSweep.EveryCandidateCellIsPricedInAir` writes and scores the same cells where
the budget actually goes.

**The finding this exists to make checkable is that the two environments move in opposite
directions.** Substep demand is conductance over capacity. `HeatTimeScale` divides every heat
capacity and nothing else, so lowering it divides *every* stiffness term at once; multiplying
conductivity restores the conduction term alone. A hull in vacuum is conduction-limited and gets
dearer by conductivity x clock / 225. A hull in air is convection-limited, that term is not
restored, and the same cell gets cheaper. Both are printed per cell so the claim is a pair of
measured columns rather than an argument.

**Read against the cap, not against the control.** `G6` is satisfied when p99 demand is inside what
the shipped caps grant, and a cell can be dearer than the shipped world and still fit. The ratio is
printed because it says which way a cell moved; the cap is what decides it.

Usage: air.py [data-dir]
"""
import csv
import os
import statistics
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import scoring

DATA = sys.argv[1] if len(sys.argv) > 1 else "out/air-2026-08-23"

SHIPPED_CLOCK = 225.0

# What MaxSubsteps grants at the shipped defaults. A demand above this is a step integrated at the
# ceiling with both overshoot clamps live: an approximation rather than a slow step, and one whose
# price stiffness.md, What refusing the demand costs, has measured.
GRANTED = 64.0

# The scenarios, in the order they are read. vacuum-shadow ties this pass to the vacuum dataset;
# the other three are the convection fit's two anchors and its held-out point.
ORDER = ["vacuum-shadow", "surface-hot-noon", "storm-parked", "reentry"]

# Forced convection at the speed each scenario flies, h = 1 + 0.1*sqrt(v) over still air, as
# balance.md fits it. Used only to project the 300 m/s case the servers this mod is played on run,
# which no scenario in this pass measures.
COEFFICIENT = {"surface-hot-noon": 1.0, "storm-parked": 2.0, "reentry": 2.4142}
PROJECT_AT = 2.7321


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


# One definition, in scoring.py, shared with verdict.py — this is the one that survived, and every
# panel figure this repository publishes was computed with it.
percentile = scoring.percentile


def cell_name(conductivity, clock):
    return f"x{conductivity:g} / {clock:g}"


def main():
    rows = load("air")
    if not rows:
        print(f"no air.csv under {DATA}")
        return 1

    demand = {}
    for row in rows:
        value = number(row, "substeps_demanded")
        if value is None:
            continue
        cell = (float(row["conductivity"]), float(row["clock"]))
        demand.setdefault((cell, row["scenario"]), {})[row["ship"]] = value

    cells = sorted({cell for cell, _ in demand}, key=lambda c: (c[0], -c[1]))
    shipped = (1.0, SHIPPED_CLOCK)
    scenarios = [s for s in ORDER if any(sc == s for _, sc in demand)]

    ships = max((len(v) for v in demand.values()), default=0)
    print(f"Air cost of the cells that satisfy G8 — {DATA}, {ships} hulls, "
          f"{len(cells)} cells, {len(scenarios)} scenarios")
    print()

    # ---- the two directions, side by side ----------------------------------------------------
    print(f"{'cell':>12} {'scenario':>18} {'p50':>8} {'p95':>8} {'p99':>8} {'max':>8}"
          f" {'vs shipped':>11} {'of cap':>8} {'over cap':>9}")

    worst = {}
    for cell in cells:
        for scenario in scenarios:
            mine = demand.get((cell, scenario))
            if not mine:
                continue

            base = demand.get((shipped, scenario), {})
            paired = [mine[s] / base[s] for s in mine if base.get(s)]

            values = list(mine.values())
            p99 = percentile(values, 0.99)
            ratio = statistics.median(paired) if paired else None

            worst[cell] = max(worst.get(cell, 0.0), p99 or 0.0)

            over = sum(1 for v in values if v > GRANTED)

            print(f"{cell_name(*cell):>12} {scenario:>18} "
                  f"{percentile(values, 0.5):8.2f} {percentile(values, 0.95):8.2f} "
                  f"{p99:8.2f} {max(values):8.2f} "
                  + (f"{ratio:10.2f}x" if ratio is not None else f"{'—':>11}")
                  + f" {100.0 * p99 / GRANTED:7.0f}%"
                  + f" {over:4d}/{len(values):<4d}")
        print()

    # ---- G6, per cell ------------------------------------------------------------------------
    print(f"G6: p99 substep demand inside the {GRANTED:.0f} the shipped caps grant, "
          "in the worst environment measured")
    print()
    print(f"{'cell':>12} {'worst p99':>10} {'of cap':>8} {'G6':>5}"
          f" {'proj p50':>9} {'proj p95':>9} {'of cap':>8} {'G6':>5}")

    for cell in cells:
        p99 = worst.get(cell)
        if not p99:
            continue

        middle = project(demand, cell, 0.5)
        projected = project(demand, cell, 0.95)
        line = (f"{cell_name(*cell):>12} {p99:10.2f} {100.0 * p99 / GRANTED:7.0f}% "
                f"{'PASS' if p99 <= GRANTED else 'FAIL':>5}")

        if projected is None or middle is None:
            print(line + f" {'—':>9} {'—':>9} {'—':>8} {'—':>5}")
        else:
            print(line + f" {middle:9.2f} {projected:9.2f} "
                  f"{100.0 * projected / GRANTED:7.0f}% "
                  f"{'PASS' if projected <= GRANTED else 'FAIL':>5}")

    print()
    print("The 300 m/s columns are a projection, not a measurement: demand is linear in the")
    print("convection coefficient, fitted here on this cell's own three atmospheric scenarios and")
    print("evaluated at h = 2.73. balance.md validates the same fit against a held-out point.")
    print()
    print("'over cap' counts hulls demanding more substeps than the caps grant. That is an")
    print("approximation rather than a slow step - the step is integrated at the ceiling with both")
    print("overshoot clamps live - and it is worth about 0.03 K on the hottest block at the 1.15x")
    print("these hulls sit at. See stiffness.md, What refusing the demand costs.")
    return 0


def project(demand, cell, quantile):
    """Demand at 300 m/s, at one quantile, from this cell's own three atmospheric points.

    Fitted per cell rather than scaled from the shipped fit, because whether the *slope* survives a
    retune is exactly what is in question: the intercept is the conduction-limited part and the
    slope is the convective part, and the retune moves the two in opposite directions.
    """
    points = []
    for scenario, h in COEFFICIENT.items():
        mine = demand.get((cell, scenario))
        if not mine:
            continue
        points.append((h, percentile(list(mine.values()), quantile)))

    if len(points) < 2:
        return None

    n = len(points)
    mean_h = sum(h for h, _ in points) / n
    mean_y = sum(y for _, y in points) / n
    denominator = sum((h - mean_h) ** 2 for h, _ in points)
    if denominator == 0:
        return None

    slope = sum((h - mean_h) * (y - mean_y) for h, y in points) / denominator
    return (mean_y - slope * mean_h) + slope * PROJECT_AT


if __name__ == "__main__":
    sys.exit(main())
