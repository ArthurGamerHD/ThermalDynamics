#!/usr/bin/env python3
"""Does the drag a coefficient implies leave a ship able to fly? The drag milestone's criterion, scored.

**The criterion that decided `DragCoefficient` had no scorer.** It was computed once by hand when
the drag milestone moved the default from 1 to 0.5, and the two figures it turned on — 14.06 % and
3.13 % of hulls with drag beating their own thrust, 55.7 m/s and 78.8 m/s at the worst percentile —
live in prose in balance-lab.md and nowhere else. So re-scoring it after any change to the drag
model meant rebuilding the arithmetic from the paragraph that describes it, which is the shape of
`P5`: one definition and no consumer. This is the consumer.

**The registered criterion, unchanged** (balance-lab.md, *what it did*):

* Drag at 100 m/s beats a ship's own thrust on **no more than 5 %** of hulls.
* The **p1** hull still reaches **at least 60 m/s** at full thrust.

**Scored over hulls that can lift themselves**, which is a reading of the criterion rather than a
subset chosen because it passes: the words are *must also still be able to fly*, and the worst
ceilings in the whole census are stations with one or two thrusters — thrust-to-weight of 0.001 to
0.017 g. A hull that cannot lift itself is not a ship drag broke; it never flew.

Usage: dragfit.py [census.csv] [--cd C] [--sweep] [--no-shape] [--rho R] [--csv <path>]
"""

import csv
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cruise
import scoring

#: Surface gravity, m/s^2. A hull lifts itself when its thrust exceeds its weight here.
GRAVITY = 9.81

#: The speed the criterion is asked at: the engine's own large-grid cap, so *drag exceeds thrust at
#: 100 m/s* is exactly *this mod took away a speed the game gave you*.
CRITERION_SPEED = 100.0

#: No more than this share of self-lifting hulls may have drag beating thrust at CRITERION_SPEED.
MAX_OVERPOWERED_SHARE = 5.0

#: The p1 hull must still reach at least this, m/s — two thirds of the engine cap, which is where a
#: player notices they are slow rather than notices they are stuck.
MIN_P1_CEILING = 60.0


def drag_newtons(area, coefficient, density, speed, shape):
    """`1/2 C_d rho A v^2` on the frontal projection, with the hull's shape factor applied."""
    return (0.5 * coefficient * density
            * area * cruise.PROJECTED_SHARE * shape * speed * speed)


def score(rows, coefficient, density, shaped):
    """`(share overpowered, p1 ceiling, population)` over the hulls that can lift themselves."""
    overpowered = 0
    ceilings = []

    for row in rows:
        thrust = scoring.number(row, "thrust_n")
        area = scoring.number(row, "exposed_area_m2")
        mass = scoring.number(row, "mass_kg")

        if not thrust or not area or not mass:
            continue
        if thrust <= 0 or area <= 0 or mass <= 0:
            continue

        # Can it lift itself? Anything below one gravity never flew, and its ceiling describes a
        # station rather than a ship drag broke.
        if thrust / (mass * GRAVITY) < 1.0:
            continue

        shape = cruise.shape_of(row, shaped)

        if drag_newtons(area, coefficient, density, CRITERION_SPEED, shape) > thrust:
            overpowered += 1

        ceiling = cruise.cruise(thrust, area, coefficient, density, shape)
        if ceiling is not None:
            ceilings.append(ceiling)

    if not ceilings:
        return None, None, 0

    ceilings.sort()
    return (100.0 * overpowered / len(ceilings),
            scoring.percentile(ceilings, 0.01),
            len(ceilings))


def verdict(share, p1):
    """Whether both halves of the registered criterion hold."""
    return share <= MAX_OVERPOWERED_SHARE and p1 >= MIN_P1_CEILING


def main():
    args = scoring.positionals(("--cd", "--rho", "--csv"))

    path = args[0] if args else "out/census-2026-08-31/census.csv"
    density = float(scoring.flag("--rho", cruise.SEA_LEVEL_DENSITY))
    shaped = "--no-shape" not in sys.argv
    out = scoring.flag("--csv", None)

    if not os.path.exists(path):
        raise SystemExit(path + " does not exist, so there is no census to read")

    rows = list(csv.DictReader(open(path)))

    print("the drag milestone's criterion from %s" % path)
    print("  hulls that can lift themselves, at %g m/s in %.3f kg/m^3"
          % (CRITERION_SPEED, density))
    print("  registered: drag beats thrust on no more than %g %%, and the p1 hull still makes"
          " at least %g m/s" % (MAX_OVERPOWERED_SHARE, MIN_P1_CEILING))
    print("  hull shape: %s" % ("applied" if shaped else "ignored (--no-shape)"))
    print()

    coefficients = ([float(scoring.flag("--cd", 0.5))] if "--sweep" not in sys.argv
                    else [0.5, 0.75, 1.0, 1.25, 1.5, 1.54, 1.75, 2.0])

    print("  %6s %14s %14s %8s  %s" % ("C_d", "beats thrust", "p1 ceiling", "hulls", "verdict"))

    figures = []
    for coefficient in coefficients:
        share, p1, population = score(rows, coefficient, density, shaped)
        if share is None:
            raise SystemExit("no hull in " + path + " carries thrust, area and mass")

        ok = verdict(share, p1)
        print("  %6.2f %13.2f %% %11.1f m/s %8d  %s"
              % (coefficient, share, p1, population, "pass" if ok else "FAIL"))

        figures.append(("beats thrust at C_d %g" % coefficient, round(share, 2), "%"))
        figures.append(("p1 ceiling at C_d %g" % coefficient, round(p1, 2), "m/s"))
        figures.append(("verdict at C_d %g" % coefficient, "pass" if ok else "fail", ""))

    figures.append(("self-lifting hulls", population, "ships"))
    figures.append(("hull shape applied", 1 if shaped else 0, ""))

    if out:
        scoring.write_summary(out, figures)
        print()
        print("csv -> " + out)


if __name__ == "__main__":
    main()
