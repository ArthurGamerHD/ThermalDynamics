#!/usr/bin/env python3
"""Choose a retest set: ships that are ordinary, complete and cheap to re-simulate.

This is not `panel.py`, and the difference is the whole point of having both. **The panel is
extremes** — the most buried hull, the least surface per kilowatt, the most heat from jump drives
— because a dial sweep needs the largest lever it can find, and a change that moves nothing at the
extremes moves nothing anywhere. **This set is the middle**, because a retest asks a different
question: *did a change break the ships people actually fly?* A regression judged only on outliers
is a regression judged on hulls nobody built on purpose.

Membership is six tests, each measurable and each stated:

1. **It is a ship, not a fragment.** At least one thruster and at least one thing that makes or
   stores power. A hull with neither is a station, a prop or an unfinished build, and a third of
   the corpus is one of those.
2. **It makes real heat.** 100 kW of waste at full load, the same floor `panel.py` argues for and
   for the same reason: a hull with no heat cannot answer a question about heat, and a rounding
   error in the denominator makes an unpowered station look perfectly cooled.
3. **It has an inside.** At least one airtight room. Convection, room air and the suit are half of
   what the model does, and a ship with no sealed volume exercises none of it.
4. **It is affordable.** 30,000 blocks, the same ceiling and the same arithmetic: a retest that
   costs an afternoon is a retest that stops being run.
5. **It was measurable in every scenario.** All five outcomes present, or its rows are holes.
6. **It is typical.** Ranked against the population on the four quantities the census showed decide
   an outcome — burial, surface per kilowatt, capacity per watt and watts per block — and scored by
   its *worst* percentile distance from the median across the four. A ship near the middle of three
   and at the edge of the fourth is not typical, and taking the worst rather than the mean is what
   says so.

The set is drawn per grid size and across the size range, so a retest is not accidentally all
frigates. Every pick carries its own score, so the choice can be audited rather than trusted.

Usage: typical.py <census.csv> <outcomes.csv> [out.csv] [--count N]
"""
import csv
import os
import sys

args = [a for a in sys.argv[1:] if not a.startswith("--")]
CENSUS = args[0] if len(args) > 0 else "out/census-2026-08-21/census.csv"
OUTCOMES = args[1] if len(args) > 1 else "out/corpus-2026-08-21/outcomes.csv"
TARGET = args[2] if len(args) > 2 else "tools/corpus/typical.csv"

COUNT = 40
for a in sys.argv[1:]:
    if a.startswith("--count"):
        COUNT = int(a.split("=", 1)[1]) if "=" in a else COUNT
    if a.startswith("--count="):
        COUNT = int(a.split("=", 1)[1])

SHIPS = os.path.join(os.path.dirname(OUTCOMES), "ships.csv")
REQUIRED = {"idle", "vacuum-sunlit", "full-electrical", "burn-forward", "recovery"}

POWER_FLOOR = 100000.0
BLOCK_CEILING = 30000

# The four the census found decide an outcome. Named here rather than inline so the score and the
# report cannot describe different sets.
AXES = ["buried_share", "exposure_m2_per_kw", "capacity_j_per_k_per_w", "w_per_block"]


def number(row, key, default=0.0):
    try:
        return float(row[key])
    except (TypeError, ValueError, KeyError):
        return default


def key_of(row):
    return (row["ship"], row["workshop_id"])


census = {key_of(r): r for r in csv.DictReader(open(CENSUS))}

paths = {}
if os.path.exists(SHIPS):
    for r in csv.DictReader(open(SHIPS)):
        paths[key_of(r)] = r.get("path", "")
else:
    print(f"warning: {SHIPS} not found — the set will carry no blueprint paths")

outcomes = {}
for r in csv.DictReader(open(OUTCOMES)):
    outcomes.setdefault(key_of(r), {})[r["scenario"]] = r


def complete(k):
    return REQUIRED.issubset(outcomes.get(k, {}).keys())


def w_per_block(k):
    return number(census[k], "waste_full_w") / max(1.0, number(census[k], "blocks"))


def eligible(k):
    c = census[k]
    return (number(c, "thruster_n") >= 1
            and (number(c, "producer_n") + number(c, "store_n")) >= 1
            and number(c, "rooms") >= 1
            and number(c, "waste_full_w") >= POWER_FLOOR
            and number(c, "blocks") <= BLOCK_CEILING
            and complete(k))


pool = [k for k in census if eligible(k)]
if not pool:
    print("no ship in this dataset passes the six tests")
    sys.exit(1)


def value(k, axis):
    return w_per_block(k) if axis == "w_per_block" else number(census[k], axis)


# Percentile rank on each axis, computed over the eligible pool rather than the whole corpus: the
# question is whether a hull is ordinary *among ships*, and the pool is already what that means.
ranks = {}
for axis in AXES:
    ordered = sorted(pool, key=lambda k: value(k, axis))
    last = max(1, len(ordered) - 1)
    for i, k in enumerate(ordered):
        ranks.setdefault(k, {})[axis] = i / last


def score(k):
    """Worst distance from the median across the four axes. Zero is perfectly ordinary."""
    return max(abs(ranks[k][axis] - 0.5) for axis in AXES)


# Drawn per grid size and across the size range, so the set is not accidentally all frigates. The
# bands are quartiles of block count within each grid size, which is a property of the pool rather
# than a threshold anybody chose.
rows = []
for large in (1, 0):
    side = [k for k in pool if number(census[k], "large") == large]
    if not side:
        continue

    side.sort(key=lambda k: number(census[k], "blocks"))
    want = COUNT // 2
    bands = 4
    per_band = max(1, want // bands)

    for b in range(bands):
        lo = (len(side) * b) // bands
        hi = (len(side) * (b + 1)) // bands
        band = sorted(side[lo:hi], key=score)

        for k in band[:per_band]:
            c = census[k]
            rows.append({
                "ship": k[0],
                "workshop_id": k[1],
                "path": paths.get(k, ""),
                "large": int(number(c, "large")),
                "size_band": b + 1,
                "typicality": round(score(k), 4),
                "blocks": int(number(c, "blocks")),
                "rooms": int(number(c, "rooms")),
                "thruster_n": int(number(c, "thruster_n")),
                "producer_n": int(number(c, "producer_n")),
                "store_n": int(number(c, "store_n")),
                "waste_full_w": number(c, "waste_full_w"),
                "w_per_block": round(w_per_block(k), 1),
                "buried_share": number(c, "buried_share"),
                "exposure_m2_per_kw": number(c, "exposure_m2_per_kw"),
                "capacity_j_per_k_per_w": number(c, "capacity_j_per_k_per_w"),
                "load_peak_k": number(outcomes[k]["full-electrical"], "peak_k"),
                "burn_peak_k": number(outcomes[k]["burn-forward"], "peak_k"),
            })

rows.sort(key=lambda r: (-r["large"], r["size_band"], r["typicality"]))

columns = list(rows[0].keys())
os.makedirs(os.path.dirname(TARGET) or ".", exist_ok=True)
with open(TARGET, "w", newline="") as f:
    w = csv.DictWriter(f, fieldnames=columns)
    w.writeheader()
    w.writerows(rows)

# Every figure printed comes from the data rather than from what anybody remembers it to be.
print(f"{len(census):,} ships in the census, {len(pool):,} pass the six tests, "
      f"{len(rows)} chosen")
print(f"written to {TARGET}")
print()
print(f"  {'ship':44}{'blocks':>8}{'band':>6}{'typ':>7}{'load K':>9}{'burn K':>9}")
for r in rows:
    print(f"  {r['ship'][:44]:44}{r['blocks']:>8,}{r['size_band']:>6}"
          f"{r['typicality']:>7.3f}{r['load_peak_k']:>9,.0f}{r['burn_peak_k']:>9,.0f}")
