#!/usr/bin/env python3
"""Which test classes cost more than the fast lane's threshold, and which of them say so.

**The rule this checks is stated in tests/README.md and has rotted twice.** A class costing more
than about two seconds of the suite carries `[Trait("speed", "slow")]`; the fast lane is everything
else. On 2026-08-24 the lane measured **3 m 45 s** against a documented fifteen seconds, and after
nineteen classes were tagged it rotted again inside two days — 37 s on 2026-08-26, with one class
35 s of it and ten more past the threshold untagged.

**Nothing checked it, and that is the whole reason.** The page says so plainly: the honest check is a
class's own measured cost, which a test inside that class cannot read. It can be read from *outside*
though, which is what this is — the manual refresh ("take the durations, tag what crossed two
seconds") as one command that exits non-zero when the lane has drifted.

    dotnet test tests/Thermodynamics.Tests --logger "trx;LogFileName=lanes.trx" \
        --results-directory out/lanes
    python3 tools/lanes/lanes.py out/lanes/lanes.trx

**It reports both directions.** A class over the threshold without the trait is what makes the fast
lane slow. A class *under* it carrying the trait is the other drift: it costs the fast lane nothing
to run and is being skipped anyway, so the lane covers less than it could for no saving.
"""

import collections
import glob
import os
import re
import sys
import xml.etree.ElementTree as ElementTree

#: Seconds of suite time above which a class belongs in the slow lane. The rule's own number.
THRESHOLD = 2.0

#: Below this a class carrying the trait is being skipped for no saving. Deliberately well under
#: THRESHOLD rather than equal to it, so a class hovering either side of the rule is not reported as
#: drift every time the machine breathes.
CHEAP = 0.5

NS = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"


def seconds(duration):
    """A trx `hh:mm:ss.fffffff` as seconds, or 0 where the runner recorded none."""
    if not duration:
        return 0.0

    parts = duration.split(":")
    if len(parts) != 3:
        return 0.0

    return int(parts[0]) * 3600 + int(parts[1]) * 60 + float(parts[2])


def costs(path):
    """`{class: seconds}` summed over every test the run recorded."""
    tree = ElementTree.parse(path)

    byId = {}
    for test in tree.iter(NS + "UnitTest"):
        method = test.find(NS + "TestMethod")
        if method is None:
            continue

        name = method.get("className") or ""
        byId[test.get("id")] = name.split(".")[-1]

    total = collections.Counter()
    for result in tree.iter(NS + "UnitTestResult"):
        name = byId.get(result.get("testId"))
        if name:
            total[name] += seconds(result.get("duration"))

    return total


def tagged(source):
    """Every class carrying `[Trait("speed", "slow")]`, from the test sources.

    The trait sits in the attribute block above the class and other attributes may sit between the
    two, so the block is taken whole rather than matched adjacently — a `[Collection]` between the
    trait and the class is common and would otherwise read as untagged.
    """
    found = set()

    for path in glob.glob(os.path.join(source, "*.cs")):
        with open(path, encoding="utf-8", errors="ignore") as handle:
            text = handle.read()

        for match in re.finditer(r"((?:\[[^\]]*\]\s*)+)public\s+(?:sealed\s+)?class\s+(\w+)", text):
            if '[Trait("speed", "slow")]' in match.group(1).replace('"speed","slow"', '"speed", "slow"'):
                found.add(match.group(2))

    return found


def drift(total, slow):
    """`(over threshold and untagged, under CHEAP and tagged)`, each sorted by cost."""
    heavy = sorted(((n, s) for n, s in total.items() if s >= THRESHOLD and n not in slow),
                   key=lambda pair: -pair[1])
    idle = sorted(((n, total.get(n, 0.0)) for n in slow if total.get(n, 0.0) < CHEAP),
                  key=lambda pair: pair[1])
    return heavy, idle


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    path = args[0] if args else "out/lanes/lanes.trx"
    source = args[1] if len(args) > 1 else "tests/Thermodynamics.Tests"

    if not os.path.exists(path):
        raise SystemExit(path + " does not exist — run the suite with a trx logger first")

    total = costs(path)
    if not total:
        raise SystemExit(path + " recorded no test durations, so this judged nothing")

    slow = tagged(source)
    heavy, idle = drift(total, slow)

    print("the two lanes, from %s" % path)
    print("  %d classes, %.1f s of test time; %d carry the slow trait"
          % (len(total), sum(total.values()), len(slow)))
    print("  a class over %.1f s belongs in the slow lane (tests/README.md)" % THRESHOLD)
    print()

    print("  the ten most expensive")
    for name, cost in sorted(total.items(), key=lambda pair: -pair[1])[:10]:
        print("    %-44s %8.2f s  %s" % (name, cost, "slow" if name in slow else "fast"))

    if heavy:
        print()
        print("  ** %d over the threshold and untagged — the fast lane is paying for these **"
              % len(heavy))
        for name, cost in heavy:
            print("    %-44s %8.2f s" % (name, cost))

    if idle:
        print()
        print("  %d tagged slow and costing under %.1f s: skipped by the fast lane for no saving"
              % (len(idle), CHEAP))
        for name, cost in idle:
            print("    %-44s %8.2f s" % (name, cost))

    if not heavy and not idle:
        print()
        print("  the lane holds: nothing over the threshold is untagged, and nothing tagged is free")

    # Non-zero only for the direction that makes the lane slow. An over-tagged class costs coverage
    # rather than time, and failing on it would make this refuse to pass on a machine that ran fast.
    return 1 if heavy else 0


if __name__ == "__main__":
    sys.exit(main())
