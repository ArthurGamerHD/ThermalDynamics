#!/usr/bin/env python3
"""**How much of the heat a fleet makes rests on a number nobody sourced.**

`Cubes.xml` states a waste fraction per block type, and since [backlog.md](../../docs/backlog.md)
`C21` each one says where it came from: a conversion in `ReferenceEfficiencies`, a field the game's
own definition states, a note that nothing reads it, or an admitted invention. Counting those says
how much of the *file* is sourced. It does not say how much of the *heat* is, and those are very
different numbers, because one block type carries most of a fleet's waste on its own.

This reads a census `composition.csv` — per ship, per subtype, the watts it wastes at full
electrical load — and reports both.

    python3 tools/corpus/provenance.py out/census-2026-08-21/composition.csv

Two things it states rather than hides. The population is whatever the census walked and the basis
is **full electrical load with every jump drive charging**, which is a bound rather than a duty
cycle (`E3`); and a census taken before a fraction moved measured the old one, so the drives are
restated as a second column with the two values named here rather than silently rescaled.

The note grammar is parsed here and in `AuthoredWasteTests`, which is two readers of one format
(`D3`). `test_provenance.py` pins the four counts against the ones that test pins, so the two
cannot drift apart quietly.
"""
import csv
import os
import re
import sys
from collections import Counter

# What the four jump drives wasted when a census was taken before 2026-08-23, and what they waste
# now that it is derived from the PowerEfficiency each states. Named rather than folded into a
# ratio so a reader can see which census a restatement applies to.
DRIVE_WAS = 0.15
DRIVE_NOW = {
    "LargeJumpDrive": 0.2,
    "LargeJumpDriveReskin": 0.2,
    "LargePrototechJumpDrive": 0.1,
    "SmallPrototechJumpDrive": 0.1,
}

CLASSES = ["sourced", "derived", "unreachable", "invented"]


def classify(note):
    """The provenance a comment claims, or None where it claims nothing."""
    text = " ".join(line.strip() for line in (note or "").split("\n") if line.strip())
    lowered = text.lower()
    if lowered.startswith("waste:"):
        return "sourced"
    if lowered.startswith("derived:"):
        return "derived"
    if lowered.startswith("no producer"):
        return "unreachable"
    if lowered.startswith("invented"):
        return "invented"
    return None


def repo_root():
    return os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


def authored(path=None):
    """(TypeId, SubtypeId, property) -> provenance, for every waste fraction in `Cubes.xml`."""
    path = path or os.path.join(repo_root(), "Data", "Cubes.xml")
    text = open(path, encoding="utf-8").read()

    found = {}
    for block in re.finditer(
        r"<TypeId>([^<]*)</TypeId>\s*<SubtypeId>([^<]*)</SubtypeId>(.*?)</Definition>", text, re.S
    ):
        type_id, subtype, body = block.group(1).strip(), block.group(2).strip(), block.group(3)
        for element in re.finditer(
            r"(?:<!--((?:(?!-->).)*)-->\s*)?<Decimal Name=\"((?:Producer|Consumer)WasteEnergy)\"",
            body,
            re.S,
        ):
            found[(type_id, subtype, element.group(2))] = classify(element.group(1))
    return found


def counts(table):
    """How many fractions of each provenance the file holds."""
    tally = Counter()
    for provenance in table.values():
        tally[provenance or "none"] += 1
    return tally


def class_by_type(table):
    """TypeId -> the provenance of the fraction its waste heat actually comes through.

    A block that generates power is charged on its producer fraction and everything else on its
    consumer one, so the rule is: take the producer fraction unless it is the one that says nothing
    reads it. That is what makes a hydrogen engine sourced here — its 0.6 names a combustion engine
    — while its consumer fraction is the environment fallback and never carries anything.
    """
    producer, consumer = {}, {}
    for (type_id, subtype, prop), provenance in table.items():
        if subtype != "DefaultThermodynamics":
            continue
        (producer if prop == "ProducerWasteEnergy" else consumer)[type_id] = provenance

    by_type = {}
    for type_id in set(producer) | set(consumer):
        side = producer.get(type_id)
        by_type[type_id] = consumer.get(type_id) if side in (None, "unreachable") else side
    return by_type


def heat(composition, table):
    """Watts of full-load waste by provenance, as measured and restated."""
    by_type = class_by_type(table)
    fallback = by_type.get("EnvironmentDefinition") or "invented"

    measured, restated, unsourced = Counter(), Counter(), Counter()
    rows = 0
    with open(composition, newline="", encoding="utf-8") as handle:
        for row in csv.DictReader(handle):
            watts = float(row["waste_full_w"] or 0)
            provenance = by_type.get(row["type_id"], fallback)
            rows += 1
            measured[provenance] += watts

            now = DRIVE_NOW.get(row["subtype"])
            restated[provenance] += watts * (now / DRIVE_WAS) if now else watts
            if provenance == "invented":
                unsourced[row["type_id"]] += watts * (now / DRIVE_WAS) if now else watts

    return rows, measured, restated, unsourced


def main(argv):
    if len(argv) != 2:
        print(__doc__)
        return 2

    table = authored()
    tally = counts(table)
    total = sum(tally.values())

    print(f"{total} waste fractions authored in Data/Cubes.xml")
    for name in CLASSES + ["none"]:
        if tally[name]:
            print(f"  {name:>12} {tally[name]:4d}  {tally[name] / total * 100:5.1f} %")

    rows, measured, restated, unsourced = heat(argv[1], table)
    measured_total = sum(measured.values()) or 1.0
    restated_total = sum(restated.values()) or 1.0

    print()
    print(f"full-load waste heat over {rows:,} block rows of {argv[1]}")
    print(f"{'':>12} {'as measured':>12} {'restated':>12}")
    for name in CLASSES + ["none"]:
        if not measured[name] and not restated[name]:
            continue
        print(f"  {name:>12} {measured[name] / measured_total * 100:10.2f} % "
              f"{restated[name] / restated_total * 100:10.2f} %")

    print()
    print("where the invented heat is, restated")
    for type_id, watts in unsourced.most_common(8):
        print(f"  {type_id:>22} {watts / restated_total * 100:6.2f} %")

    print()
    print("basis: full electrical load, every jump drive charging, no thrust. Restated puts the"
          f" four drives at their derived fractions rather than the {DRIVE_WAS} a census taken"
          " before 2026-08-23 used.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
