#!/usr/bin/env python3
"""What the conversion to real conductances did to the ships people fly.

Reads the dataset `ConductanceRetestWalk` writes and prints, per scenario, what each arm of the
conversion moved on the retest set. The walk records the shipped world and four counterfactual
worlds read off `Data/Cubes.xml` as it stood before the conversion (`4f6b44a^`), so every column
here is a shipped-minus-counterfactual difference on the same forty hulls.

**The arms are read against the shipped world, not against each other.** Conduction is a network
and two stiffened families in series do not add, so the composite is not the sum of the arms and
this prints all five rather than inviting the subtraction.

**The criteria are the ones that already existed.** A retest that invents a threshold after
seeing its numbers has fitted the threshold to the data (`E1`), so what this reports per world is
G1, G2 and G5 exactly as `verdict.py` computes them — idle is safe, load bites, no death spiral —
on forty ships instead of eight thousand. A world that flips one of them is the finding; a world
that moves temperatures and flips none is a different and smaller finding, and this prints both.

**Reach is printed first and it is not decoration.** An arm that reaches no block on this set
reports "no change" in exactly the shape of an arm that reached every block and changed nothing,
and one of the four reaches nothing here by construction — the corpus filters admit vanilla ships,
so none of the forty carries a coolant pipe or a radiator.

Usage: retest.py [data-dir]
"""
import os
import statistics
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import scoring

DATA = sys.argv[1] if len(sys.argv) > 1 else "out/retest-2026-08-23"

CONTROL = "shipped"

# The key that identifies one measurement: a ship in a scenario. The world is the axis being
# compared, so it is not part of the key.
KEY = scoring.ROW_KEY + ("scenario",)




def load(name):
    return scoring.load(DATA, name)


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

    # Worlds come from the reach file where there is one: it is written before any scenario runs,
    # so an interrupted walk still knows how many arms it was going to have. Reading them off the
    # results instead makes a run that died after one world look like a run with one world (`E4`).
    worlds = []
    for row in reach + rows:
        if row["world"] not in worlds:
            worlds.append(row["world"])

    # The population is the set the walk was given, which the reach file records before any
    # scenario runs. Taking it from the results instead shrinks the denominator to whatever
    # finished, and a partial run then reports itself as complete.
    ships = {(r["ship"], r["workshop_id"]) for r in (reach or rows)}
    scenarios = []
    for row in rows:
        if row["scenario"] not in scenarios:
            scenarios.append(row["scenario"])

    print(f"{DATA}: {len(rows)} rows, {len(ships)} ships, {len(scenarios)} scenarios, "
          f"{len(worlds)} worlds")

    # **A partial walk says so.** Every figure below is a median over whatever landed, and a walk
    # killed halfway through a world has measured the ships it happened to reach — which on this set
    # is not a random half of it (`E4`).
    per_world = {w: len({(r["ship"], r["workshop_id"]) for r in rows if r["world"] == w})
                 for w in worlds}
    short = [w for w in worlds if per_world.get(w, 0) < len(ships)]
    if short:
        print()
        print("PARTIAL — these arms have not finished, and nothing below is a whole result:")
        for world in short:
            print(f"    {world}: {per_world.get(world, 0)} of {len(ships)} ships")
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

    # ---- censoring -------------------------------------------------------------------------
    # **The lab never destroys an overheating block** (`E9`), so anything past critical kept
    # generating for the rest of the clock and a peak above about 1,500 K says *ran away* and
    # nothing finer. Differences taken between two censored runs are differences between two
    # harness artefacts, so the share is printed before any of them.
    # **Censoring begins at a block's own rating, not at 1,500 K.** From the moment anything is
    # past critical it keeps generating undamped for the rest of the clock, so the peak is a
    # harness artefact from that point on. Reading it off the magnitude instead said 9 % of these
    # runs were censored when 31 % of them were, and 9 % against 85 % in `burn-forward` — which is
    # how the +34.7 K that [backlog.md](../../docs/backlog.md) `C2` was deciding on came to be read
    # as a temperature. The 1,500 K line is kept below it as the stronger flag: past there a peak
    # says *ran away* and nothing finer.
    censored = {}
    for row in rows:
        peak = scoring.number(row, "peak_k")
        if peak is None:
            continue
        got = censored.setdefault((row["world"], row["scenario"]), [0, 0, 0])
        got[2] += 1
        if scoring.peak_is_censored(scoring.number(row, "over_critical"), peak):
            got[0] += 1
        if scoring.peak_ran_away(peak):
            got[1] += 1

    print("Runs with a block past its own rating, which is where the peak stops being a")
    print(f"temperature, and runs past {scoring.RAN_AWAY_KELVIN:.0f} K, which is where it stops"
          " being anything")
    print()
    print(f"    {'world':<22}{'scenario':<18}{'over rating':>14}"
          f"{'past ' + str(int(scoring.RAN_AWAY_KELVIN)) + ' K':>14}")
    for world in worlds:
        for scenario in scenarios:
            over, ran, total = censored.get((world, scenario), (0, 0, 0))
            if not total:
                continue
            print(f"    {world:<22}{scenario:<18}"
                  f"{over:>6} of {total:<4} {ran:>6} of {total:<4}")
    print()
    print("**A peak difference taken on a censored row is a difference between two harness")
    print("artefacts** (`E9`). Where the left column is most of the population, read the crossing")
    print("and the counts below and not the peak: those are decided before anything diverges.")
    print()

    # ---- the effect ------------------------------------------------------------------------
    indexed = {}
    for row in rows:
        indexed[(row["world"],) + tuple(row[k] for k in KEY)] = row

    for column, label, unit in (
        ("peak_k", "peak temperature", "K"),
        ("over_critical", "blocks over critical", ""),
        ("seconds_to_critical", "seconds to first block over critical", "s"),
        # The crossing is not the loss, and the loss is what costs a player something
        # (balance.md, How long a block has after it crosses). Both are here because a change
        # that moves one without the other says which half it touched.
        ("seconds_to_first_loss", "seconds to the first block lost", "s"),
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

                    a = scoring.number(control, column)
                    b = scoring.number(other, column)
                    if a is None or b is None:
                        continue

                    # A run that never crossed reports -1, which is not a duration and must not be
                    # averaged with ones that are.
                    if column.startswith("seconds_to") and (a < 0 or b < 0):
                        continue

                    deltas.append(a - b)

                if not deltas:
                    line += f"{'-':>22}"
                else:
                    # **The median is not enough for a count.** Blocks over critical is zero for
                    # most ships in most scenarios, so its median is zero however many hulls the
                    # change pushed over the line; the split says which way the ones that moved
                    # went, and it is the half that carries the finding.
                    up = sum(1 for d in deltas if d > 1e-3)
                    down = sum(1 for d in deltas if d < -1e-3)
                    cell = f"{median(deltas):+.2f}{unit} {up}up {down}dn"
                    line += f"{cell:>22}"

            print(line)

        print()

    # ---- the criteria, as they already stand ------------------------------------------------
    # Copied in shape from verdict.py rather than reimagined: same scenarios, same thresholds, same
    # direction. Forty ships is not the population the thresholds were written for, so what matters
    # here is the difference between worlds rather than the absolute verdict.
    print("G1, G2 and G5 per world — the criteria as verdict.py computes them, on this set")
    print()
    print(f"{'world':<22}{'G1 critical at idle':>22}{'G2 warm under load':>22}{'G5 recovered':>18}")

    for world in worlds:
        mine = [r for r in rows if r["world"] == world]
        idle = [r for r in mine if r["scenario"] == "idle"]
        loaded = [r for r in mine if r["scenario"] == "full-electrical"]
        recovery = [r for r in mine if r["scenario"] == "recovery"]

        def share(subset, test):
            if not subset:
                return "-"
            hit = sum(1 for r in subset if test(r))
            return f"{hit}/{len(subset)} {hit / len(subset):.0%}"

        g1 = share(idle, lambda r: (scoring.number(r, "over_critical") or 0) > 0)
        g2 = share(loaded, lambda r: (scoring.number(r, "peak_k") or 0) >= 400.0)
        g5 = share(recovery, lambda r: scoring.number(r, "over_critical") == 0)
        print(f"{world:<22}{g1:>22}{g2:>22}{g5:>18}")

    print()
    print("G1 fails above ~1 % critical at idle, G2 below ~20 % reaching 400 K under load, and G5")
    print("below ~95 % recovering. A world that moves a temperature without moving one of these has")
    print("moved something a player does not meet.")
    print()

    print("Each cell is the median over the ships the scenario could be read from, then how many")
    print("of them the shipped world is higher on (+) and lower on (-). Positive means the shipped")
    print("world is the hotter, later or more damaged one.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
