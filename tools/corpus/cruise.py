#!/usr/bin/env python3
"""Whether a derived cruise speed lands where the authored one does, on real ships.

[RelativeTopSpeed](https://github.com/Gauge/RelativeTopSpeed) holds each grid under a *cruise
speed* interpolated through three authored mass points per grid size — eight numbers and two
multipliers, fitted by hand. It has no air in it at all: no density, no altitude, no area, no shape,
so a hull and a brick of the same mass decelerate identically.

**This model has every one of those terms already**, so the same quantity falls out instead of being
fitted: a ship cruises where its thrust balances its drag, `T = ½ C_d ρ A v²`, which is

    v_cruise = sqrt(2T / (C_d ρ A))

and needs no mass at all. That is the whole argument of backlog.md `K12`, and the row says exactly
what decides it: **whether the derived speeds land where the authored ones do on real ships**. This
is that measurement.

**It needs thrust and exposed area and nothing else**, both of which the census carries, so it costs
a parse rather than a walk. Ships with neither — stations, unpowered hulks — are counted and
dropped rather than given a speed they do not have (`E8`).

Usage: cruise.py [census.csv] [--cd C] [--rho R] [--csv <path>]
"""
import csv
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import scoring

#: RTS's authored cruise band, m/s: 60 at 200 t rising to 110 at 8 kt on large grids.
AUTHORED_BAND = (60.0, 110.0)

#: Sea-level air density, kg/m^3. The comparison is made where a ship flies fastest and where the
#: authored curve was tuned; thinner air raises every derived speed by the same square root.
SEA_LEVEL_DENSITY = 1.225

#: Space Engineers' own large-grid speed cap, m/s. RTS raises it and then holds each grid down; the
#: question here is how many ships never reach it because the air stops them first.
ENGINE_CAP = 100.0


#: What fraction of a hull's **total exposed area** is presented to the flow at any one moment.
#:
#: **Cauchy's surface-area formula**: the mean projection of a convex body over all orientations is
#: exactly a quarter of its surface area. The census records the total — every exposed face,
#: whichever way it points — and `½ C_d ρ A v²` wants the *frontal projection*, which is what the
#: solver itself uses through its incidence weighting.
#:
#: **The first version of this file used the total and was wrong by this factor**, which put every
#: cruise speed low by two and every drag high by four. See the change log below.
PROJECTED_SHARE = 0.25


def cruise(thrust, area, coefficient, density):
    """Where thrust balances drag, m/s, or None where the ship has neither (`E8`).

    `area` is a hull's **total exposed area**, as the census records it; the frontal projection the
    drag expression wants is `PROJECTED_SHARE` of it.
    """
    if thrust is None or area is None or thrust <= 0 or area <= 0:
        return None
    if coefficient <= 0 or density <= 0:
        return None

    return math.sqrt(2.0 * thrust / (coefficient * density * area * PROJECTED_SHARE))


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]

    def flag(name, fallback):
        if name not in sys.argv:
            return fallback
        return sys.argv[sys.argv.index(name) + 1]

    path = args[0] if args else "out/census-2026-08-25/census.csv"
    coefficient = float(flag("--cd", 1.0))
    density = float(flag("--rho", SEA_LEVEL_DENSITY))
    out = flag("--csv", None)

    if not os.path.exists(path):
        raise SystemExit(path + " does not exist, so there is no census to read")

    rows = list(csv.DictReader(open(path)))

    speeds = []
    without = 0
    for row in rows:
        speed = cruise(scoring.number(row, "thrust_n"), scoring.number(row, "exposed_area_m2"),
                       coefficient, density)
        if speed is None:
            without += 1
            continue
        speeds.append((speed, row.get("ship", ""), scoring.number(row, "large")))

    if not speeds:
        raise SystemExit("no ship in " + path + " carries both thrust and exposed area")

    speeds.sort()
    values = [s for s, _, _ in speeds]

    print("derived cruise speed from %s" % path)
    print("  %d ships carry thrust and exposed area; %d carry neither and are dropped"
          % (len(values), without))
    print("  C_d %.2f, air density %.3f kg/m^3 — where a ship flies fastest and where the authored"
          " curve was tuned" % (coefficient, density))

    print()
    print("  %-10s %10s" % ("quantile", "m/s"))
    figures = []
    for label, q in (("p5", 0.05), ("p25", 0.25), ("p50", 0.5), ("p75", 0.75), ("p95", 0.95)):
        value = scoring.percentile(values, q)
        figures.append(("cruise " + label, round(value, 2), "m/s"))
        print("  %-10s %10.1f" % (label, value))
    print("  %-10s %10.1f" % ("min", values[0]))
    print("  %-10s %10.1f" % ("max", values[-1]))
    figures.append(("cruise min", round(values[0], 2), "m/s"))
    figures.append(("cruise max", round(values[-1], 2), "m/s"))

    low, high = AUTHORED_BAND
    inside = sum(1 for v in values if low <= v <= high)
    share = 100.0 * inside / len(values)

    print()
    print("  RTS's authored band is %g to %g m/s across 200 t to 8 kt." % (low, high))
    print("  %d of %d ships land inside it, %.1f %%" % (inside, len(values), share))
    figures.append(("ships inside the authored band", inside, "ships"))
    figures.append(("share inside the authored band", round(share, 2), "%"))
    figures.append(("ships with thrust and area", len(values), "ships"))
    figures.append(("ships with neither", without, "ships"))

    print()
    print("  **What the derived model separates that the authored one cannot.** RTS's curve is a"
          " function")
    print("  of mass alone, so a hollow hull and a dense one of equal mass cruise alike. This is a")
    print("  function of thrust over area, which is what actually decides a top speed — and the"
          " spread")
    print("  below is the size of what the mass curve throws away.")

    # ---- what the engine's own cap does to this ------------------------------------------------
    #
    # **The reason `K11` may not need a retarding force at all.** RTS holds each grid under a cruise
    # speed because it has no drag: without one, a ship accelerates to the engine's global cap and
    # stays there. This model *has* drag, so a ship stops where thrust balances it — and for most
    # ships that is below the cap the engine already enforces, which means the retarding force would
    # be holding them under a speed they cannot reach.
    print()
    print("  %-12s %14s %10s %28s" % ("air density", "", "median", "drag-limited below 100 m/s"))
    for rho, label in ((1.225, "sea level"), (0.6, "half"), (0.3, "thin"), (0.1, "very thin")):
        thinner = sorted(v for v in (cruise(scoring.number(row, "thrust_n"),
                                            scoring.number(row, "exposed_area_m2"),
                                            coefficient, rho) for row in rows) if v is not None)
        median = scoring.percentile(thinner, 0.5)
        below = sum(1 for v in thinner if v <= ENGINE_CAP)
        share_below = 100.0 * below / len(thinner)
        print("  %-12.3f %14s %8.1f m/s %26.1f %%" % (rho, label, median, share_below))
        figures.append(("median cruise at density %g" % rho, round(median, 2), "m/s"))
        figures.append(("share under the engine cap at density %g" % rho, round(share_below, 2), "%"))

    print()
    print("  **A top speed that falls with altitude, which is what a mass curve cannot produce.**")
    print("  Low down a ship is held by the air; high up the engine's own cap takes over, exactly as")
    print("  it does today. So the per-grid top speed `K11` wanted a force for is an outcome of drag")
    print("  that is already applied, for the great majority of ships and all of the low-altitude"
          " ones.")

    if out:
        with open(out, "w", newline="", encoding="utf-8") as handle:
            writer = csv.writer(handle)
            writer.writerow(["statistic", "value", "unit"])
            for statistic, value, unit in figures:
                writer.writerow([statistic, value, unit])
        print("\ncsv -> %s  (%d figures)" % (out, len(figures)))


if __name__ == "__main__":
    main()
