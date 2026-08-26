#!/usr/bin/env python3
"""**How much of the heat a fleet makes rests on a number nobody sourced.**

`Cubes.xml` states a waste fraction per block type, and since [backlog.md](../../docs/backlog.md)
2026-08-23 each one says where it came from: a conversion in `ReferenceEfficiencies`, a field the
game's own definition states, a note that nothing reads it, or an admitted invention. Counting those says
how much of the *file* is sourced. It does not say how much of the *heat* is, and those are very
different numbers, because one block type carries most of a fleet's waste on its own.

This reads a census `composition.csv` — per ship, per subtype, the watts it wastes at full
electrical load — and reports both.

    python3 tools/corpus/provenance.py out/census-2026-08-21/composition.csv
    python3 tools/corpus/provenance.py out/census-2026-08-21/composition.csv --type OxygenGenerator

The second form asks the other question, and the two disagree by two orders of magnitude on the
same type. The first is a ratio of aggregates and says how much of *a fleet's* heat rests on an
unsourced number; the second is an aggregate of ratios over the ships that carry the type, and says
how much of *their* heat does. The oxygen generator is 0.38 % of the first and a median 48 % of
the second, and only the second is a figure about the player who built one (`E6`).

Two things it states rather than hides. The population is whatever the census walked and the basis
is **full electrical load with every jump drive charging**, which is a bound rather than a duty
cycle (`E3`); and a census taken before a fraction moved measured the old one, so the drives are
restated as a second column with the two values named here rather than silently rescaled.

The note grammar is parsed here and in `AuthoredWasteTests`, which is two readers of one format
(`D3`). `test_provenance.py` pins the four counts against the ones that test pins, so the two
cannot drift apart quietly.
"""
import csv
import hashlib
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

# The same, by type rather than by subtype, for the fraction that moved on 2026-08-25. An oxygen
# generator wasted 0.6 when every census on disk was taken and wastes 0.4 now, and the reason is in
# balance.md: at 0.6 two of the six vanilla generators cannot be built.
TYPE_WAS = {"OxygenGenerator": 0.6}
TYPE_NOW = {"OxygenGenerator": 0.4}

CLASSES = ["sourced", "derived", "unreachable", "invented"]


def restatement(subtype, type_id):
    """What to multiply a census row's watts by to read it at the fractions that ship today.

    A census measures the file it was taken against. Rescaling silently would make the number
    untraceable to the run it came from, so every restatement is a named pair above and this is the
    one place they are applied (`P1`).
    """
    now = DRIVE_NOW.get(subtype)
    if now:
        return now / DRIVE_WAS
    if type_id in TYPE_NOW:
        return TYPE_NOW[type_id] / TYPE_WAS[type_id]
    return 1.0


def measured_current_definitions(composition):
    """Whether the census beside `composition` was taken against the `Cubes.xml` on disk now.

    **A restatement is only right for a dataset that predates the change**, and applying one to a
    census that already measured the new value discounts it twice — which is what happened the day
    `A13`'s census was re-taken with the oxygen generator already at 0.40. The dataset says which
    file it saw, in the `provenance.txt` `CorpusRecord` writes beside it, so this is a lookup
    rather than a judgement (`P1`).

    Returns None when there is no provenance to read, which is every dataset taken before that file
    was written — and for those the restatement is right, because they all predate the changes.
    """
    directory = os.path.dirname(os.path.abspath(composition))
    path = os.path.join(directory, "provenance.txt")
    if not os.path.exists(path):
        return None

    recorded = None
    with open(path, encoding="utf-8") as handle:
        for line in handle:
            if line.startswith("Cubes.xml "):
                recorded = line.split()[1]

    if recorded is None:
        return None

    cubes = os.path.join(repo_root(), "Data", "Cubes.xml")
    if not os.path.exists(cubes):
        return None

    digest = hashlib.sha256()
    with open(cubes, "rb") as handle:
        for block in iter(lambda: handle.read(65536), b""):
            digest.update(block)

    return digest.hexdigest()[:16] == recorded


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


def heat(composition, table, restate=True):
    """Watts of full-load waste by provenance, as measured and restated.

    `restate` is False for a census taken against the definitions that ship now — restating one of
    those applies a correction twice.
    """
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

            scale = restatement(row["subtype"], row["type_id"]) if restate else 1.0
            restated[provenance] += watts * scale
            if provenance == "invented":
                unsourced[row["type_id"]] += watts * scale

    return rows, measured, restated, unsourced


def per_ship(composition, type_id):
    """Each ship's own share of full-load waste carried by one type, for the ships that carry it.

    **The fleet share and this are different questions and they answer very differently.** The
    fleet share is a ratio of aggregates — one type's watts over every ship's watts — so it is
    dominated by whatever the largest ships carry, and a charging jump drive is three quarters of
    that. This is an aggregate of ratios, asked only of the ships that carry the type at all, and
    it is what says whether a fraction matters to the player who built one (`E6`).
    """
    ship_watts, type_watts, instances = {}, {}, 0
    with open(composition, newline="", encoding="utf-8") as handle:
        for row in csv.DictReader(handle):
            ship = (row["ship"], row["workshop_id"])
            watts = float(row["waste_full_w"] or 0)
            ship_watts[ship] = ship_watts.get(ship, 0.0) + watts
            if row["type_id"] == type_id:
                type_watts[ship] = type_watts.get(ship, 0.0) + watts
                instances += int(row["count"] or 0)

    shares = sorted(type_watts[ship] / ship_watts[ship]
                    for ship in type_watts if ship_watts[ship] > 0)
    return len(ship_watts), instances, shares


def percentile(sorted_values, fraction):
    """The value at `fraction` through an already-sorted list, or 0 for an empty one."""
    if not sorted_values:
        return 0.0
    return sorted_values[min(len(sorted_values) - 1, int(fraction * len(sorted_values)))]


def main(argv):
    if len(argv) == 4 and argv[2] == "--type":
        ships, instances, shares = per_ship(argv[1], argv[3])
        if not shares:
            print(f"no ship in {argv[1]} carries a {argv[3]}")
            return 1

        print(f"{argv[3]} over {argv[1]}")
        print(f"  {len(shares):,} of {ships:,} ships carry one"
              f" ({len(shares) / ships * 100:.1f} %), {instances:,} instances")
        print("  its share of that ship's own full-load waste:")
        for name, point in (("median", 0.5), ("p75", 0.75), ("p90", 0.9), ("p99", 0.99)):
            print(f"    {name:>6} {percentile(shares, point) * 100:6.1f} %")
        print(f"    {'max':>6} {shares[-1] * 100:6.1f} %")
        print()
        print("basis: full electrical load, every jump drive charging, no thrust, and the drives"
              " at the fraction the census measured rather than restated — a restatement raises"
              " the ships that carry one and lowers this share further.")
        return 0

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

    current = measured_current_definitions(argv[1])
    rows, measured, restated, unsourced = heat(argv[1], table, restate=current is not True)
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
    if current is True:
        print("basis: full electrical load, every jump drive charging, no thrust. This census"
              " records the Cubes.xml on disk now, so nothing is restated and the two columns are"
              " the same figure.")
    else:
        seen = "records an older Cubes.xml" if current is False else "records no provenance"
        print("basis: full electrical load, every jump drive charging, no thrust. The dataset"
              f" {seen}, so restated puts the four drives at their derived fractions rather than"
              f" the {DRIVE_WAS} a census taken before 2026-08-23 used, and the oxygen generator"
              " at the 0.4 decided on 2026-08-25 rather than the 0.6 it measured.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
