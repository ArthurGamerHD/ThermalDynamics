#!/usr/bin/env python3
"""Conduction against the clock: does the pair reach the significance window, and at what cost.

Reads the dataset `PairSweep` writes and scores `G8` on every cell — under sustained full
electrical load the median crossing falls in 120–300 s, and at idle the median hull settles inside
an hour — alongside the criteria a cell must not break to be usable: `G1`, `G2`, `G5` and the
substep demand `G6` is about.

**The interaction is the point, so it is reported as one.** Substep demand goes as conductivity ×
clock and the projection that opened this run multiplied two single-dial curves together. This
prints, per cell, the crossing the two edges predict for it and the crossing it actually had, so
the composition rule is a measurement rather than an assumption.

**A cell that satisfies G8 is not yet a recommendation.** It has to satisfy it while keeping G1,
G2 and G5 and while costing no more than the shipped pair, and the table prints all of those beside
each other rather than ranking on one.

**The crossing median is over the hulls that were loaded, not over the hulls that crossed.** A hull
that never reaches critical is censored above, not absent: it is ordered past every hull that did
cross, exactly as a hull that never settles is counted past the recovery bound. So a cell where
fewer than half the hulls ever cross has no median at all and cannot satisfy `G8` — which is the
whole of the difference between reading conduction x8 as a win and reading it as a blind spot
(`E9`).

Usage: pairs.py [data-dir]
"""
import csv
import os
import statistics
import sys

import scoring

DATA = sys.argv[1] if len(sys.argv) > 1 else "out/pairs-2026-08-23"

# The clock of this sweep's control cell, which is what shipped when the sweep was designed.
#
# **It is the baseline and not the shipped configuration.** `C24` shipped `ConductionScale` 9.6 and
# `HeatTimeScale` 90 on 2026-08-24 — conductivity x4 at a clock of 90 in this sweep's coordinates,
# which is a cell of the grid rather than the control. Every "control" here means the cell the grid
# was laid out around; `air.py` carries the same correction.
BASELINE_CLOCK = 225.0

# G8, as balance-lab.md wrote it down before this run — and its settling half as corrected before
# this dataset was scored, which is the recovery time rather than an idle settling time. Idle in
# vacuum shadow has no equilibrium, so the figure there reads its own 120 s floor at low clock.
WINDOW = (120.0, 300.0)
RECOVERY_BOUND = 3600.0

# The floor Battery.SettleSeconds can return: the scan starts at the second sample. A column of
# these is a blind spot, and the idle column is printed only to show it.
SETTLE_FLOOR = 120.0

# The thresholds G1, G2 and G5 are already scored at, copied in shape from verdict.py rather than
# reinvented: same scenarios, same numbers, same direction.
WARM_KELVIN = 400.0
G1_MAX_SHARE = 1.0
G2_MIN_SHARE = 20.0
G5_MIN_SHARE = 95.0

# A hull that never crossed critical, ordered past every hull that did.
INFINITE = float("inf")

# Anything past here is "ran away" and not a temperature — the lab never destroys an overheating
# block (E9).
CENSORED = 1500.0


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
    return statistics.median(values) if values else None


def cell_key(row):
    return (float(row["conductivity"]), float(row["clock"]))


def crossings(rows):
    """Seconds to the first crossing for every loaded hull, censored above where there was none.

    **A run that never crossed is not absent from the population, it is past the end of it.** It
    reports -1 and is returned as infinity, so it orders past every hull that did cross and the
    median it lands in is the median of the hulls that were *loaded* rather than of the hulls that
    *crossed*. Dropping it instead answers a different question — how fast do the hulls that
    overheat overheat — and answers it in the shape of the one `G8` asks.
    """
    out = []
    for row in rows:
        value = number(row, "seconds_to_critical")
        out.append(value if value is not None and value >= 0 else INFINITE)
    return out


def crossing_median(rows):
    """The population median crossing, or None where more than half the hulls never crossed.

    Returns `(median, crossed, loaded)`. The median is None when it falls in the censored tail,
    because the only honest reading of *the 50th percentile is past the end of the run* is that
    there is no median here — not that it is large, and not that it is the median of the third of
    the population that did cross.
    """
    values = crossings(rows)
    crossed = sum(1 for v in values if v != INFINITE)

    if not values:
        return None, 0, 0

    # One definition of this statistic, in `scoring.py` beside its tests, because the bootstrap
    # that says how *stable* a median is has to take the same median the verdict does (`P5`).
    middle = scoring.censored_median(values)
    return (None if middle == INFINITE else middle), crossed, len(values)


def where(cells, per_cell, cell_crossing):
    """What the measured curves say about where a cell satisfying `G8` would have to be.

    This is a search inside a fixed criterion, not a criterion fitted to the data (`E1`, `E11`).
    Two curves decide it and both come out of this dataset:

    * the crossing goes as one over the clock, so `crossing x clock` is a constant per
      conductivity, and that constant is what a conductivity *is* for this purpose;
    * recovery is set by the clock and barely moves with conduction, so the bound on it is a
      bound on the clock alone.

    Printed as two bands per conductivity rather than as a solved answer, because the intersection
    is the thing to look at and an intersection printed as a single pair hides how wide it is.
    """
    print()
    print("Where a satisfying cell would have to be, from the curves above")
    print()

    constants = []
    censored_rungs = []
    for conductivity in sorted({c for c, _ in cells}):
        products = []
        for c, h in cells:
            if c != conductivity:
                continue
            value = cell_crossing((c, h))
            if value is not None:
                products.append(value * h)

        if not products:
            censored_rungs.append(conductivity)
            continue

        constant = statistics.median(products)
        spread = (max(products) - min(products)) / constant if len(products) > 1 else 0.0
        constants.append((conductivity, constant, len(products), spread))

    print(f"{'conductivity':>12} {'cross x clock':>14} {'cells':>6} {'spread':>7}"
          f" {'clock for the window':>22}")

    for conductivity, constant, count, spread in constants:
        low = constant / WINDOW[1]
        high = constant / WINDOW[0]
        print(f"{conductivity:>12g} {constant:>14.0f} {count:>6} {spread:>6.1%}"
              f" {f'{low:.0f} - {high:.0f}':>22}")

    for conductivity in censored_rungs:
        print(f"{conductivity:>12g} {'censored':>14} {'-':>6} {'-':>7} {'no cell has a median':>22}")

    # The recovery bound, read off the measured clocks rather than fitted: the two clocks the
    # bound falls between, over every cell that has one, so the reading carries its own resolution.
    below = []
    above = []
    for key in cells:
        recovery = [r for r in per_cell[key] if r["scenario"] == "recovery"]
        values = []
        for row in recovery:
            value = number(row, "seconds_to_settle")
            ceiling = number(row, "ceiling_s") or 0.0
            values.append(value if value is not None and value >= 0
                          else max(ceiling * 2, RECOVERY_BOUND * 10))
        back = median(values)
        if back is None:
            continue
        (below if back <= RECOVERY_BOUND else above).append((key[1], back))

    if below and above:
        slowest = min(below)
        fastest = max(above)
        print()
        print(f"    recovery is under {RECOVERY_BOUND:.0f} s at clock {slowest[0]:g} "
              f"({slowest[1]:.0f} s) and over it at clock {fastest[0]:g} ({fastest[1]:.0f} s),")
        print(f"    so the clock has to be at least somewhere between them.")

    print()
    print("    A cell satisfies G8 only where its conductivity's window band and the recovery")
    print("    bound overlap. Rows whose band lies entirely below that clock cannot, at any clock.")


def main():
    rows = load("pairs")
    if not rows:
        print(f"no pairs.csv in {DATA}. Run:")
        print("  THERMAL_CORPUS_TESTS=1 THERMAL_CORPUS_DATA=<dir> \\")
        print('      dotnet test --filter "FullyQualifiedName~PairSweep"')
        return 1

    cells = []
    for row in rows:
        key = cell_key(row)
        if key not in cells:
            cells.append(key)

    ships = {(r["ship"], r["workshop_id"]) for r in rows}
    scenarios = sorted({r["scenario"] for r in rows})
    expected = len(ships) * len(scenarios)

    print(f"{DATA}: {len(rows)} rows, {len(cells)} cells, {len(ships)} ships, "
          f"{len(scenarios)} scenarios")

    per_cell = {}
    for key in cells:
        per_cell[key] = [r for r in rows if cell_key(r) == key]

    short = [k for k in cells if len(per_cell[k]) < expected]
    if short:
        print()
        print("PARTIAL — these cells have not finished, and nothing below is a whole result:")
        for key in short:
            print(f"    conductivity x{key[0]:g}, clock {key[1]:g}: "
                  f"{len(per_cell[key])} of {expected} runs")

    control = (1.0, BASELINE_CLOCK)
    if control not in per_cell:
        print()
        print("no shipped cell in this dataset, so nothing has a baseline")
        return 1

    # ---- the edges, which are what the interaction is read against -------------------------
    def cell_crossing(key):
        loaded = [r for r in per_cell.get(key, []) if r["scenario"] == "full-electrical"]
        return crossing_median(loaded)[0]

    base = cell_crossing(control)

    print()
    print("G8 and what a cell costs to get there")
    print()
    print(f"{'conductivity':>12} {'clock':>6} {'cross p50':>10} {'crossed':>8}"
          f" {'recover p50':>12}"
          f" {'G8':>4} {'G1':>6} {'G2':>6} {'G5':>6} {'substeps':>9} {'cost':>6}"
          f" {'idle-floor':>11}")

    control_demand = median([number(r, "substeps_demanded") or 0.0
                             for r in per_cell[control]])

    winners = []

    for key in cells:
        mine = per_cell[key]
        loaded = [r for r in mine if r["scenario"] == "full-electrical"]
        idle = [r for r in mine if r["scenario"] == "idle"]
        recovery = [r for r in mine if r["scenario"] == "recovery"]

        cross, crossed, loaded_count = crossing_median(loaded)

        # **Recovery is where the settling half is scored**, because the hull is driven somewhere
        # and back, so the figure is a real duration. A run that never settled reports -1 and
        # counts as past the bound rather than as missing — its ceiling is already past it.
        returns = []
        for row in recovery:
            value = number(row, "seconds_to_settle")
            ceiling = number(row, "ceiling_s") or 0.0
            returns.append(value if value is not None and value >= 0
                           else max(ceiling * 2, RECOVERY_BOUND * 10))

        back = median(returns)

        # The idle column, printed only so the blind spot stays visible: at low clock a hull has
        # barely moved and reports the floor, which is why this is not what G8 is scored on.
        idle_settles = [number(r, "seconds_to_settle") or -1.0 for r in idle]
        on_floor = sum(1 for v in idle_settles if 0 <= v <= SETTLE_FLOOR)

        g1_hits = sum(1 for r in idle if (number(r, "over_critical") or 0) > 0)
        g1 = 100.0 * g1_hits / len(idle) if idle else None

        g2_hits = sum(1 for r in loaded if (number(r, "peak_k") or 0) >= WARM_KELVIN)
        g2 = 100.0 * g2_hits / len(loaded) if loaded else None

        g5_hits = sum(1 for r in recovery if number(r, "over_critical") == 0)
        g5 = 100.0 * g5_hits / len(recovery) if recovery else None

        demand = median([number(r, "substeps_demanded") or 0.0 for r in mine])

        in_window = cross is not None and WINDOW[0] <= cross <= WINDOW[1]
        comes_back = back is not None and back <= RECOVERY_BOUND
        g8 = in_window and comes_back

        cost = demand / control_demand if control_demand else float("nan")

        print(f"{key[0]:>12g} {key[1]:>6g}"
              f" {('censored' if cross is None else f'{cross:.1f}'):>10}"
              f" {crossed:>4}/{loaded_count:<3}"
              f" {('-' if back is None else f'{back:.0f}'):>12}"
              f" {('YES' if g8 else ('win' if in_window else '-')):>4}"
              f" {('-' if g1 is None else f'{g1:.0f}%'):>6}"
              f" {('-' if g2 is None else f'{g2:.0f}%'):>6}"
              f" {('-' if g5 is None else f'{g5:.0f}%'):>6}"
              f" {demand:>9.2f} {cost:>5.2f}x"
              f" {on_floor:>7}/{len(idle):<3}")

        if g8:
            keeps = ((g1 is None or g1 <= G1_MAX_SHARE)
                     and (g2 is None or g2 >= G2_MIN_SHARE)
                     and (g5 is None or g5 >= G5_MIN_SHARE))
            winners.append((key, cross, back, demand, cost, keeps))

    print()
    print(f"G8 holds when the crossing p50 is in {WINDOW[0]:.0f}-{WINDOW[1]:.0f} s AND the recovery")
    print(f"p50 is under {RECOVERY_BOUND:.0f} s. 'win' means the window alone. cost is substep demand")
    print("against the shipped pair, which is what G6 is about.")
    print()
    print("crossed is how many of the loaded hulls ever reached critical. The median is over all of")
    print("them, with the rest censored above, so a cell where fewer than half ever cross reads")
    print("'censored' and cannot satisfy G8 — it has no median, rather than a large one (E9).")
    print()
    print("idle-floor is how many hulls reported the 120 s floor of the settling rule at idle — a")
    print("hull that has not begun reading as one that has finished. It is printed rather than")
    print("scored, and it is why the settling half is taken from recovery (SettleReadingTests).")

    print()
    if winners:
        print("Cells that satisfy G8, and whether they keep the criteria a cell must not break")
        print()
        print(f"{'conductivity':>12} {'clock':>6} {'cross p50':>10} {'recover p50':>12}"
              f" {'cost':>6}  {'keeps G1 G2 G5':>14}")
        for key, cross, back, _demand, cost, keeps in winners:
            print(f"{key[0]:>12g} {key[1]:>6g} {cross:>10.1f} {back:>12.0f}"
              f" {cost:>5.2f}x  {('yes' if keeps else 'NO'):>14}")
        print()
        print("A cell that satisfies G8 and breaks G1, G2 or G5 is not a route. Cost is what G6 is")
        print("about and is a price rather than a bound — the shipped caps decide whether it is one.")
    else:
        print("No cell in this grid satisfies G8.")

    # ---- does the interaction compose? ------------------------------------------------------
    print()
    print("Does the pair compose? Predicted from the two edges against measured")
    print()
    print(f"{'conductivity':>12} {'clock':>6} {'predicted':>10} {'measured':>10} {'ratio':>7}")

    no_median = 0
    no_edge = 0

    for key in cells:
        c, h = key
        if c == 1.0 or h == BASELINE_CLOCK:
            continue

        # An edge that was never run is a different absence from an edge that ran and had no
        # median, and collapsing the two would report a gap in the grid as a censored measurement.
        if (1.0, h) not in per_cell:
            no_edge += 1
            continue

        edge_c = cell_crossing((c, BASELINE_CLOCK))
        edge_h = cell_crossing((1.0, h))
        measured = cell_crossing(key)

        if base is None or edge_c is None or edge_h is None or measured is None:
            no_median += 1
            continue

        # Both edges are ratios against the same control, so the multiplicative prediction is
        # base x (edge_c/base) x (edge_h/base).
        predicted = edge_c * edge_h / base
        print(f"{c:>12g} {h:>6g} {predicted:>10.1f} {measured:>10.1f}"
              f" {measured / predicted:>6.2f}x")

    print()
    print("A ratio of 1.00 means the two dials multiply. The projection that opened this run")
    print("assumed they do; that assumption is what these rows test.")

    if no_median:
        print()
        print(f"{no_median} interior cells are missing from that table because the cell or one of its")
        print("edges has no median — every one of them is at a conductivity where most hulls never")
        print("cross. The composition rule is measured where a crossing exists and untested where")
        print("it does not.")

    if no_edge:
        print()
        print(f"{no_edge} more are missing because their clock has no cell on the conductivity x1 edge,")
        print("so there is nothing to predict them from. They are the corner cells, which were")
        print("chosen from the constants below rather than from a pair of edges.")

    where(cells, per_cell, cell_crossing)

    # ---- censoring ---------------------------------------------------------------------------
    censored = sum(1 for r in rows
                   if (number(r, "peak_k") or 0) >= CENSORED)
    print()
    print(f"{censored} of {len(rows)} runs ({censored / len(rows):.1%}) end past {CENSORED:.0f} K,")
    print("which says 'ran away' and not a temperature. Crossing times are decided long before")
    print("that and are unaffected; peaks on those runs describe the harness (E9).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
