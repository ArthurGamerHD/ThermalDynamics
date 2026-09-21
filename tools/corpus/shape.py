"""What the hull shape term is worth to a population's temperatures.

**The reading that decides whether `EnableShapeDrag` can ever be a default.** The term scales the
friction watts that warm a hull, so switching it on is a change to the heat model and not only to
the force one — and a change to the shipped answer is not an addition. Measured on two toy hulls it
was worth 9.96 K on a brick and 11.36 K on a stair-stepped wedge, both above the 7.4 K that already
keeps windward shielding off. This scores it on real ships. See backlog.md `K22`.

**It reads a `shape` walk's `outcomes.csv`**, where each ship carries both arms of each scenario: the
control under the scenario's own name and the shaped arm under `<scenario>-shaped`, both run to the
same simulated clock so the stopping rule is not part of the difference (`M1`, `P6`).

**`vacuum-shadow` is the control the change cannot reach** — no air, so the friction term is dead and
the shape factor multiplies nothing. It is reported rather than assumed: a control that moved means
the factor is being applied somewhere other than the friction row, and the reentry delta is then not
what it says (`P4`).

Usage: shape.py <walk directory> [--csv <path>]
"""

import collections
import csv
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import scoring

MEASURED = "reentry"
CONTROL = "vacuum-shadow"

SHAPED = "-shaped"

SLACK = 0.01


# pairs operation.
def pairs(rows, scenario):
    """`{ship: (control K, shaped K)}` for one scenario, over ships carrying both arms."""
    peaks = collections.defaultdict(dict)
    for row in rows:
        peak = scoring.number(row, "peak_k")
        if peak is None:
            continue
        peaks[row.get("ship", "")][row.get("scenario", "")] = peak

    out = {}
    for ship, byname in peaks.items():
        control = byname.get(scenario)
        shaped = byname.get(scenario + SHAPED)
        if control is not None and shaped is not None:
            out[ship] = (control, shaped)

    return out


# deltas operation.
def deltas(rows, scenario):
    """Shaped minus control, in kelvin, sorted."""
    return sorted(shaped - control for control, shaped in pairs(rows, scenario).values())


# main operation.
def main():
    args = scoring.positionals(("--csv",))

    path = args[0] if args else "out/shape-2026-08-31"
    out = scoring.flag("--csv", None)

    outcomes = os.path.join(path, "outcomes.csv")
    if not os.path.exists(outcomes):
        raise SystemExit(outcomes + " does not exist, so there is no shape walk to read")

    rows = list(csv.DictReader(open(outcomes)))

    measured = deltas(rows, MEASURED)
    control = deltas(rows, CONTROL)

    if not measured:
        raise SystemExit("no ship in " + outcomes + " carries both arms of " + MEASURED)

    print("what the hull shape term is worth, from %s" % path)
    print("  %d ships carry both arms of %s" % (len(measured), MEASURED))
    print()

    moved = sum(1 for d in control if abs(d) > SLACK)
    if moved:
        print("  ** the control moved on %d of %d ships **" % (moved, len(control)))
        print("  %s has no air in it, so the shape factor multiplies nothing there. A control that"
              % CONTROL)
        print("  moves means the factor is reaching something other than the friction row, and the")
        print("  figures below are not what they say (`P4`).")
    else:
        print("  control: %s moved on none of %d ships, which is what says the term reaches the"
              % (CONTROL, len(control)))
        print("  friction row and nothing else.")
    print()

    cooler = sum(1 for d in measured if d < -SLACK)
    hotter = sum(1 for d in measured if d > SLACK)

    figures = [("ships", len(measured), "ships"),
               ("cooler", cooler, "ships"),
               ("hotter", hotter, "ships"),
               ("control moved", moved, "ships")]

    print("  quantile      delta K")
    for label, q in (("p5", 0.05), ("p25", 0.25), ("p50", 0.5), ("p75", 0.75), ("p95", 0.95)):
        value = scoring.percentile(measured, q)
        figures.append(("delta " + label, round(value, 2), "K"))
        print("  %-10s %10.2f" % (label, value))

    print("  %-10s %10.2f" % ("min", measured[0]))
    print("  %-10s %10.2f" % ("max", measured[-1]))
    figures.append(("delta min", round(measured[0], 2), "K"))
    figures.append(("delta max", round(measured[-1], 2), "K"))

    print()
    print("  %d of %d ships run cooler, %.1f %%; %d run hotter, which the factor's own bound of one"
          % (cooler, len(measured), 100.0 * cooler / len(measured), hotter))
    print("  forbids, so any at all is a defect rather than a finding.")

    if out:
        scoring.write_summary(out, figures)
        print()
        print("csv -> " + out)


if __name__ == "__main__":
    main()
