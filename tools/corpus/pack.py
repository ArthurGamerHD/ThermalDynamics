#!/usr/bin/env python3
"""Packs outcomes.csv into the compact columnar JSON the report reads.

One array per row rather than one object, and names held once in side tables: the corpus is
forty thousand rows, and an object-per-row payload is four times the size for the same numbers.
Peak and median are kept to two decimals because the criteria compare against them directly and
rounding to one moved a ship across the 400 K boundary.

Usage: pack.py <data-dir>
"""
import csv
import json
import os
import sys

DATA = sys.argv[1] if len(sys.argv) > 1 else "."


def number(row, key, default=None):
    try:
        return float(row[key])
    except (TypeError, ValueError, KeyError):
        return default


with open(os.path.join(DATA, "outcomes.csv")) as handle:
    rows = list(csv.DictReader(handle))

scenarios = sorted(set(r["scenario"] for r in rows))
ships = sorted(set(r["ship"] for r in rows))
blocks = sorted(set(r["hottest_block"] for r in rows))
si = {s: i for i, s in enumerate(scenarios)}
shi = {s: i for i, s in enumerate(ships)}
bi = {b: i for i, b in enumerate(blocks)}

packed = [[
    shi[r["ship"]], si[r["scenario"]], int(number(r, "blocks", 0)),
    round(number(r, "peak_k", 0), 2), round(number(r, "median_k", 0), 2),
    int(number(r, "over_critical", 0) or 0), round((number(r, "over_share", 0) or 0) * 100, 3),
    int(number(r, "seconds_to_critical", -1) or -1),
    round(number(r, "substeps_demanded", 0), 2), round(number(r, "substeps_granted", 0), 1),
    int(number(r, "generation_w", 0) or 0), bi[r["hottest_block"]],
    int(number(r, "seconds_to_settle", -1) or -1),
    int(number(r, "seconds_to_first_loss", -1) or -1),
] for r in rows]

workshop = {}
for r in rows:
    workshop.setdefault(r["ship"], r["workshop_id"])

# Two different counts, and the page has to state both because it groups by the first.
#
# `ships` is distinct *names*, which is what a row is keyed on and therefore what every statistic
# on the page is computed over. The run simulated more hulls than that: 8,132 distinct name-and-id
# pairs in the 2026-08-21 corpus against 8,054 names, so eighty-eight re-uploads and near-duplicates
# merge into a neighbour. The header used to say 8,142 — the ships the sweep was handed — beside
# statistics computed over 8,054 groups, and nothing on the page said the two were different.
identities = len(set((r["ship"], r["workshop_id"]) for r in rows))

payload = {
    "scenarios": scenarios,
    "ships": ships,
    "identities": identities,
    "workshop": [workshop[s] for s in ships],
    "blocks": blocks,
    "cols": ["ship", "scen", "blocks", "peak", "median", "overN", "overPct", "tcrit",
             "demand", "granted", "genW", "hot", "settle"],
    "rows": packed,
}

path = os.path.join(DATA, "corpus.json")
with open(path, "w") as handle:
    json.dump(payload, handle, separators=(",", ":"))
print(f"{len(packed):,} rows -> {path} ({os.path.getsize(path) / 1e6:.2f} MB)")
