#!/usr/bin/env python3
"""What one dial does to the standing panel, against what the shipped value does.

`KnobSweep` runs every dial in `KnobLab` over the panel at several levels and writes one row per
ship, scenario and level. This reads that back as *what moved*, which is the question a dial is
swept to answer and which the raw rows do not state.

**A dial is judged against its own shipped level and nothing else.** Every row carries `shipped`,
so the comparison is a ship at level `x` against *the same ship* at the level that ships, in the
same scenario — not against the population, and not against another dial. That is the same paired
shape `cap.py` and `floor.py` use, and for the same reason: a delta between two different ships is a
statement about the ships.

**Only the ships a dial can reach are counted.** A knob with `OnlyType` moves nothing on a hull that
carries none of that type, and folding those in reports the *reach* as though it were the effect —
in the direction that makes every dial look harmless. The report prints both and reads the band
against the reached one, which is what `floor.py` learned the same week.

Usage: knob.py <knobs-dir> [--knob NAME] [--csv <path>]
"""
import collections
import csv
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import provenance as provenance_lib
import scoring


def load(directory):
    path = os.path.join(directory, "knobs.csv")
    if not os.path.exists(path):
        raise SystemExit(path + " does not exist, so there is no sweep to read")

    with open(path, newline="", encoding="utf-8") as handle:
        return list(csv.DictReader(handle))


def pairs(rows, knob):
    """`(level, [(shipped peak, level peak, ship)])` for one dial, keyed by ship and scenario.

    A row without its shipped partner is dropped and counted: an interrupted sweep leaves exactly
    that, and comparing against a default would be comparing against nothing (`E4`).
    """
    mine = [r for r in rows if r.get("knob") == knob]
    if not mine:
        raise SystemExit("no rows for " + knob + " — the sweep may not have reached it")

    by_key = collections.defaultdict(dict)
    for row in mine:
        key = (row.get("ship"), row.get("workshop_id"), row.get("scenario"))
        by_key[key][row.get("level")] = row

    shipped = None
    for row in mine:
        if row.get("level") == row.get("shipped"):
            shipped = row.get("level")
            break

    if shipped is None:
        raise SystemExit(knob + " has no row at its shipped level, so nothing says what it moved from")

    out = collections.defaultdict(list)
    orphans = 0
    for key, levels in by_key.items():
        if shipped not in levels:
            orphans += len(levels)
            continue

        base = scoring.number(levels[shipped], "peak_k")
        if base is None:
            continue

        for level, row in levels.items():
            if level == shipped:
                continue

            peak = scoring.number(row, "peak_k")
            if peak is None:
                continue

            out[level].append((base, peak, key[0]))

    return shipped, out, orphans


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]

    def flag(name, fallback=None):
        if name not in sys.argv:
            return fallback
        at = sys.argv.index(name)
        return sys.argv[at + 1] if at + 1 < len(sys.argv) else fallback

    flagged = set()
    for name in ("--knob", "--csv"):
        value = flag(name)
        if value:
            flagged.add(value)
    args = [a for a in args if a not in flagged]

    directory = args[0] if args else "out/knobs-2026-08-30"
    rows = load(directory)

    wanted = flag("--knob")
    knobs = [wanted] if wanted else sorted(set(r.get("knob") for r in rows if r.get("knob")))

    figures = []
    for statistic, value, unit in provenance_lib.summary_rows(directory):
        figures.append((statistic, value, unit))

    print("knob sweep %s: %s rows over %s dials"
          % (directory, format(len(rows), ","), len(set(r.get("knob") for r in rows))))

    for knob in knobs:
        shipped, levels, orphans = pairs(rows, knob)
        print("\n%s — against its shipped level %s" % (knob, shipped))
        if orphans:
            print("  %s rows had no shipped partner and are dropped" % format(orphans, ","))

        print("  %-10s %8s %8s %12s %12s %12s"
              % ("level", "cells", "reached", "dpeak p50", "dpeak p95", "dpeak max"))

        for level in sorted(levels, key=lambda v: float(v)):
            entries = levels[level]
            deltas = [abs(peak - base) for base, peak, _ in entries]
            reached = [d for d in deltas if d > 1e-4]

            # **Read against the reached cells.** A dial with an `OnlyType` moves nothing on a hull
            # that carries none of that type, and those nulls would drown the effect in its reach.
            band = reached if reached else deltas
            p = scoring.percentiles(band)
            print("  %-10s %8s %8s %12.4f %12.4f %12.4f"
                  % (level, format(len(deltas), ","), format(len(reached), ","),
                     p["p50"], p["p95"], p["max"]))

            figures.append(("%s level %s cells" % (knob, level), len(deltas), "cells"))
            figures.append(("%s level %s reached" % (knob, level), len(reached), "cells"))
            for label in ("p50", "p95", "max"):
                figures.append(("%s level %s dpeak %s" % (knob, level, label),
                                round(p[label], 4), "K"))

        print("  reached means the dial moved that cell's peak at all; the band is read against it")

    out = flag("--csv")
    if out:
        with open(out, "w", newline="", encoding="utf-8") as handle:
            writer = csv.writer(handle)
            writer.writerow(["statistic", "value", "unit"])
            for statistic, value, unit in figures:
                writer.writerow([statistic, value, unit])
        print("\ncsv -> %s  (%d figures)" % (out, len(figures)))


if __name__ == "__main__":
    main()
