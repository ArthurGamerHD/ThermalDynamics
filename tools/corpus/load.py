#!/usr/bin/env python3
"""The load against the clock: can a dial that is not transport reach the significance window.

Conduction reaches it. `balance.md` measures what that costs — a coolant sink stops out-performing
the best surface dial, bolting starts working, a buried reactor survives, and the stiffest block on
a hull stops responding to air — so the open question is whether a dial that changes how much heat
there is, rather than how it moves, can reach the same window without those.

**The quantity to read is the ratio.** `G8` wants the median crossing inside 120–300 s *and* the
median recovery inside 3,600 s. Both go as one over the clock, so the clock alone cannot change
`recovery / crossing`; it is 129 at the shipped pair against the 30 or less the window needs. A dial
that reaches the window has to move that ratio, and this prints it beside the two medians it comes
from rather than leaving it to be divided out.

**Two load cases, printed as a pair.** `full-electrical` charges every jump drive for the whole run
and no ship does that; `full-electrical-charged` is the same load with the drives full. Drives are
71.3 % of the corpus's full-load waste heat, so the two are bounds on what a loaded ship makes
rather than one being right — and which side of the window each lands on is what says whether the
mod makes too much heat or the scenario asks for too much.

**Read the crossing beside the share.** A hull only crosses if its equilibrium is past critical, and
equilibrium moves with the load — so cutting the load lengthens the crossing *and* empties the set
that has one, which is the censoring that excluded conductivity ×8. Raising it does the opposite and
adds slow crossers to the set, which moves the median later even though every individual hull
crosses sooner. Neither effect can be reasoned about without the share in view.

Every threshold and every censoring rule here is `pairs.py`'s, imported rather than restated: two
scorers that drift apart would disagree about the same criterion silently (`P5`).

Usage: load.py [data-dir]
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import scoring  # noqa: E402

from pairs import (  # noqa: E402
    CENSORED,
    G1_MAX_SHARE,
    G2_MIN_SHARE,
    G5_MIN_SHARE,
    RECOVERY_BOUND,
    WARM_KELVIN,
    WINDOW,
    crossing_median,
    crossings,
    median,
    number,
)

DATA = sys.argv[1] if len(sys.argv) > 1 else "out/load-2026-08-23"

# What each load case is, in one line, because a table of three is unreadable without them.
CASES = {
    "full-electrical": "(every jump drive charging, for the whole run — a bound)",
    "full-electrical-charged": "(the same load, drives full — the other bound)",
    "jump-charge": "(drives charge for 421.9 s, finish, and hold — the event itself)",
}


def load(name):
    import csv

    path = os.path.join(DATA, name + ".csv")
    if not os.path.exists(path):
        return []
    with open(path) as handle:
        return list(csv.DictReader(handle))


def share(rows, predicate):
    if not rows:
        return None
    return 100.0 * sum(1 for r in rows if predicate(r)) / len(rows)


def settles(rows):
    """Seconds to settle for every hull, censored above where it never did."""
    values = []
    for row in rows:
        value = number(row, "seconds_to_settle")
        values.append(value if value is not None and value >= 0 else float("inf"))
    return values


def recovery_median(rows):
    """Seconds to settle after the load stops, with a run that never settled ordered past the end.

    Same censoring as the crossing: a hull still moving at the ceiling is not absent from the
    population, it is past it. `Battery` reports -1 for that.
    """
    values = settles(rows)

    if not values:
        return None, 0, 0

    settled = sum(1 for v in values if v != float("inf"))

    # The same median the crossing takes, from the one place it is defined (`P5`).
    middle = scoring.censored_median(values)
    return (None if middle == float("inf") else middle), settled, len(values)


def main():
    rows = load("load")
    if not rows:
        print(f"no load.csv under {DATA}")
        return 1

    cells = {}
    for row in rows:
        cells.setdefault(row["cell"], []).append(row)

    def axes(name):
        first = cells[name][0]
        return (number(first, "waste") or 1.0, -(number(first, "clock") or 0.0))

    order = sorted(cells, key=axes)
    ships = max(len({r["ship"] for r in v if r["scenario"] == "idle"}) for v in cells.values())

    print(f"The load against the clock — {DATA}, {ships} hulls, {len(cells)} cells")
    print()
    load_cases = [c for c in ("full-electrical", "full-electrical-charged", "jump-charge")
                  if any(r["scenario"] == c for r in rows)]

    for case in load_cases:
        print("load case: " + case + "   " + CASES.get(case, ""))
        print()
        print(f"{'cell':>18} {'waste':>6} {'clock':>6} | {'crossing p50':>12} {'crossed':>9}"
              f" | {'recovery p50':>12} {'settled':>9} | {'ratio':>7} | {'G8':>4} {'holds':>6}"
              f" {'G1':>6} {'G2':>6} {'G5':>6}")

        for name in order:
            mine = cells[name]
            waste = number(mine[0], "waste") or 1.0
            clock = number(mine[0], "clock") or 0.0

            loaded = [r for r in mine if r["scenario"] == case]
            idle = [r for r in mine if r["scenario"] == "idle"]
            recovery = [r for r in mine if r["scenario"] == "recovery"]
            if not loaded:
                continue

            crossing, crossed, of = crossing_median(loaded)
            settle, settled, settle_of = recovery_median(recovery)

            g1 = share(idle, lambda r: (number(r, "over_critical") or 0) > 0)
            g2 = share(loaded, lambda r: (number(r, "peak_k") or 0) >= WARM_KELVIN)
            g5 = share(recovery, lambda r: (number(r, "over_critical") or 0) == 0)

            window = crossing is not None and WINDOW[0] <= crossing <= WINDOW[1]
            recovered = settle is not None and settle <= RECOVERY_BOUND
            g8 = window and recovered

            keeps = (g1 is not None and g1 <= G1_MAX_SHARE
                     and g2 is not None and g2 >= G2_MIN_SHARE
                     and g5 is not None and g5 >= G5_MIN_SHARE)

            ratio = (settle / crossing) if (crossing and settle) else None

            # **How often the same cell still satisfies `G8` on a fleet drawn from the same
            # population.** A censored median has a cliff at half the hulls, and a cell can sit a
            # ship or two clear of it: the crossing share is what decides that, and it is not
            # visible in the median itself. Paired, because `G8` is one criterion with two halves
            # and both are medians over the same hulls.
            by_ship = {r["ship"]: r for r in recovery}
            paired = [r for r in loaded if r["ship"] in by_ship]
            holds = scoring.joint_median_stability([
                (crossings(paired), scoring.in_window(WINDOW[0], WINDOW[1])),
                (settles([by_ship[r["ship"]] for r in paired]), scoring.within(RECOVERY_BOUND)),
            ]) if paired else 0.0

            print(f"{name:>18} {waste:>6g} {clock:>6g} | "
                  + (f"{crossing:10.1f} s" if crossing is not None else f"{'censored':>12}")
                  + f" {crossed:4d}/{of:<4d} | "
                  + (f"{settle:10.1f} s" if settle is not None else f"{'censored':>12}")
                  + f" {settled:4d}/{settle_of:<4d} | "
                  + (f"{ratio:7.1f}" if ratio is not None else f"{'—':>7}")
                  + f" | {('PASS' if g8 else '—'):>4} {100 * holds:5.0f}%"
                  + f" {g1:5.1f}% {g2:5.1f}% {g5:5.1f}%"
                  + ("" if keeps else "   <- breaks a criterion it must keep"))
        print()

    if len(load_cases) > 1:
        print("The recovery column is the same in both tables: recovery runs from a full burn")
        print("rather than from an electrical load, so the drives never charge in it either way.")

    print()
    print(f"G8 wants the crossing p50 inside {WINDOW[0]:.0f}-{WINDOW[1]:.0f} s and the recovery p50")
    print(f"under {RECOVERY_BOUND:.0f} s. Both go as one over the clock, so a cell reaches the window")
    print("only if its ratio column is under about 30. G1 wants at most"
          f" {G1_MAX_SHARE:.0f}% critical at idle,")
    print(f"G2 at least {G2_MIN_SHARE:.0f}% reaching {WARM_KELVIN:.0f} K under load,"
          f" G5 at least {G5_MIN_SHARE:.0f}% recovered.")
    print()
    print("'holds' is how often a fleet resampled from the same population still satisfies G8, over")
    print(f"{scoring.RESAMPLES:,} draws with replacement. Two cells with the same median can be half as")
    print("likely to hold: the censored median has a cliff at half the hulls crossing, and how far a")
    print("cell sits from it is not visible in the median. Read it as a property of the statistic.")
    print()
    print("'crossed' is the count with a crossing at all. A cell where fewer than half the hulls")
    print("cross has no median and cannot satisfy G8 whatever the survivors did (E9) — which is how")
    print("conductivity x8 was excluded, and is the failure mode cutting the load is expected to")
    print("share.")

    censored = sum(1 for r in rows if (number(r, "peak_k") or 0) >= CENSORED)
    print()
    print(f"{censored} of {len(rows)} runs ({censored / len(rows):.1%}) end past {CENSORED:.0f} K,")
    print("which says 'ran away' and not a temperature.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
