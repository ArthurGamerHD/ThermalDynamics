#!/usr/bin/env python3
"""Packs every corpus dataset into the one payload the balance bench reads.

Four passes feed it and they are different shapes, so the packing is not uniform:

  survey      one row per ship per scenario, 40,710 rows — shipped whole, because the
              distributions are the point and a summary cannot be re-filtered
  census      one row per ship, 8,141 rows — shipped whole, it is small
  composition one row per ship and heat-making block type, 109,312 rows — **aggregated**,
              because the page asks "where is the corpus's heat made" and "what is this panel
              ship made of", and neither needs all hundred thousand
  knobs       one row per dial, level, ship and scenario — shipped whole when present, and the
              page renders without it so the report exists before the sweep finishes

Rows are arrays rather than objects and names live once in side tables; at these counts an
object-per-row payload is four times the size for the same numbers.

Usage: pack-bench.py <survey-dir> <census-dir> [knobs-dir] [out.json]
"""
import csv
import json
import os
import sys

SURVEY = sys.argv[1] if len(sys.argv) > 1 else "out/corpus-2026-08-21"
CENSUS = sys.argv[2] if len(sys.argv) > 2 else "out/census-2026-08-21"
KNOBS = sys.argv[3] if len(sys.argv) > 3 else "out/knobs-2026-08-21"
TARGET = sys.argv[4] if len(sys.argv) > 4 else "out/bench.json"
PANEL = "tools/corpus/panel.csv"


def rows(path):
    if not os.path.exists(path):
        return []
    with open(path) as handle:
        return list(csv.DictReader(handle))


def number(row, key, default=0.0):
    try:
        return float(row[key])
    except (TypeError, ValueError, KeyError):
        return default


def key_of(row):
    return (row["ship"], row["workshop_id"])


def r2(value, places=2):
    return round(value, places)


survey = rows(os.path.join(SURVEY, "outcomes.csv"))
census = rows(os.path.join(CENSUS, "census.csv"))
composition = rows(os.path.join(CENSUS, "composition.csv"))
knobs = rows(os.path.join(KNOBS, "knobs.csv"))
panel = rows(PANEL)

print(f"survey {len(survey):,}  census {len(census):,}  composition {len(composition):,}  "
      f"knobs {len(knobs):,}  panel {len(panel):,}")

# ---- shared name tables ---------------------------------------------------------------------
# **A ship is a name and a workshop id together, never a name alone.** The corpus holds ships whose
# names collide, so a name-keyed join silently merges them: it reported 8,142 joined rows where the
# honest join is 8,132, and every correlation was computed against a few ships' figures crossed
# with another's. Identity here is the pair; the name is carried separately for display.
scenarios = sorted(set(r["scenario"] for r in survey))
keys = sorted(set(key_of(r) for r in survey) | set(key_of(r) for r in census))
blocks = sorted(set(r["hottest_block"] for r in survey)
                | set(r["subtype"] for r in composition))
si = {s: i for i, s in enumerate(scenarios)}
shi = {k: i for i, k in enumerate(keys)}
bi = {b: i for i, b in enumerate(blocks)}

ships = [k[0] for k in keys]
workshop = [k[1] for k in keys]

# Which hulls actually mount a jump drive, read from the composition rather than inferred from
# whichever block happened to end up hottest — the drive is 67 % of the corpus's load heat and the
# split is a finding, so it has to be membership and not a proxy.
has_drive = [0] * len(keys)
for r in composition:
    if "JumpDrive" in r["subtype"] and key_of(r) in shi:
        has_drive[shi[key_of(r)]] = 1

# ---- the survey -------------------------------------------------------------------------------
outcomes = [[
    shi[key_of(r)], si[r["scenario"]], int(number(r, "blocks")),
    r2(number(r, "peak_k")), r2(number(r, "median_k")),
    int(number(r, "over_critical")), r2(number(r, "over_share") * 100, 3),
    int(number(r, "seconds_to_critical", -1)),
    r2(number(r, "substeps_demanded")), r2(number(r, "substeps_granted"), 1),
    int(number(r, "generation_w")), bi[r["hottest_block"]],
    r2(number(r, "hotspot_k")),
] for r in survey if key_of(r) in shi]

# ---- the census -------------------------------------------------------------------------------
CENSUS_KEEP = [
    ("large", int), ("blocks", int), ("grids", int), ("rooms", int),
    ("exposed_blocks", int), ("buried_blocks", int), ("buried_share", lambda v: r2(v, 4)),
    ("sealed_blocks", int), ("exposed_area_m2", lambda v: r2(v, 1)),
    ("thermal_mass_j_per_k", int), ("mean_capacity_j_per_k", lambda v: r2(v, 1)),
    ("armor_n", int), ("producer_n", int), ("installed_power_w", int),
    ("store_n", int), ("store_power_w", int), ("thruster_n", int), ("thrust_n", int),
    ("tool_n", int), ("consumer_n", int), ("consumer_draw_w", int), ("other_n", int),
    ("waste_idle_w", int), ("waste_full_w", int), ("waste_burn_w", int),
    ("exposure_m2_per_kw", lambda v: r2(v, 3)), ("capacity_j_per_k_per_w", lambda v: r2(v, 3)),
    # ---- arrangement, which is what a hot spot is about -------------------------------------
    ("heat_sources", int), ("w_per_m2", lambda v: r2(v, 2)),
    ("max_depth", int), ("heat_depth_mean", lambda v: r2(v, 3)), ("heat_depth_max", int),
    ("clumping", lambda v: r2(v, 3)), ("heat_gini", lambda v: r2(v, 4)),
    ("local_w_max", int), ("local_w_per_m2_max", lambda v: r2(v, 1)),
    ("heat_spread_m", lambda v: r2(v, 2)),
    ("hottest_conductance_w_per_k", lambda v: r2(v, 2)),
]
census_rows = [[shi[key_of(r)]] + [cast(number(r, name)) for name, cast in CENSUS_KEEP]
               + [bi.get(r.get("top_source", ""), -1)] for r in census if key_of(r) in shi]

# ---- composition, two ways ---------------------------------------------------------------------
# Corpus-wide: where the heat is made, one row per block type.
totals = {}
for r in composition:
    name = r["subtype"]
    watts, count, hulls = totals.get(name, (0.0, 0, 0))
    totals[name] = (watts + number(r, "waste_full_w"), count + int(number(r, "count")), hulls + 1)

heat_by_block = sorted(
    ([bi[name], int(watts), count, hulls] for name, (watts, count, hulls) in totals.items()),
    key=lambda row: -row[1])[:60]

# Per panel ship: what this hull is made of, so a distribution reads against its heating.
panel_keys = set(key_of(r) for r in panel)
panel_composition = {}
for r in composition:
    k = key_of(r)
    if k not in panel_keys:
        continue
    panel_composition.setdefault(shi[k], []).append(
        [bi[r["subtype"]], int(number(r, "count")), int(number(r, "waste_full_w"))])
for entries in panel_composition.values():
    entries.sort(key=lambda e: -e[2])
    del entries[12:]

# ---- the panel ----------------------------------------------------------------------------------
rules = sorted(set(r["rule"] for r in panel))
ri = {r: i for i, r in enumerate(rules)}
whys = {}
for r in panel:
    whys.setdefault(r["rule"], r["why"])

panel_rows = [[
    shi[key_of(r)], ri[r["rule"]], int(number(r, "large")), int(number(r, "blocks")),
    r2(number(r, "buried_share"), 3), r2(number(r, "exposure_m2_per_kw"), 2),
    r2(number(r, "capacity_j_per_k_per_w"), 2), int(number(r, "waste_full_w")),
    r2(number(r, "idle_peak_k"), 1), r2(number(r, "load_peak_k"), 1),
    r2(number(r, "burn_peak_k"), 1), int(number(r, "scenarios_critical")),
    int(number(r, "load_seconds_to_critical", -1)),
] for r in panel if key_of(r) in shi]

# ---- the knob sweep --------------------------------------------------------------------------
knob_names = sorted(set(r["knob"] for r in knobs))
ki = {k: i for i, k in enumerate(knob_names)}
knob_scenarios = sorted(set(r["scenario"] for r in knobs))
ksi = {s: i for i, s in enumerate(knob_scenarios)}

knob_rows = [[
    ki[r["knob"]], r2(number(r, "level"), 4), int(number(r, "shipped")),
    ksi[r["scenario"]], shi.get(key_of(r), -1), int(number(r, "large")),
    r2(number(r, "peak_k")), r2(number(r, "median_k")),
    int(number(r, "over_critical")), int(number(r, "seconds_to_critical", -1)),
    r2(number(r, "seconds_to_settle"), 1), r2(number(r, "substeps_demanded")),
] for r in knobs if key_of(r) in shi]

payload = {
    "scenarios": scenarios,
    "ships": ships,
    "workshop": workshop,
    "hasDrive": has_drive,
    "blocks": blocks,
    "outcomeCols": ["ship", "scen", "blocks", "peak", "median", "overN", "overPct",
                    "tcrit", "demand", "granted", "genW", "hot", "hotspot"],
    "outcomes": outcomes,
    "censusCols": ["ship"] + [name for name, _ in CENSUS_KEEP] + ["topSource"],
    "census": census_rows,
    "heatByBlock": heat_by_block,
    "heatByBlockCols": ["block", "watts", "count", "ships"],
    "panelRules": rules,
    "panelWhy": [whys[r] for r in rules],
    "panelCols": ["ship", "rule", "large", "blocks", "buried", "exposurePerKw",
                  "capacityPerW", "wasteW", "idleK", "loadK", "burnK", "scenariosCritical",
                  "loadTcrit"],
    "panel": panel_rows,
    "panelComposition": panel_composition,
    "knobNames": knob_names,
    "knobScenarios": knob_scenarios,
    "knobCols": ["knob", "level", "shipped", "scen", "ship", "large", "peak", "median",
                 "overN", "tcrit", "settle", "demand"],
    "knobs": knob_rows,
}

os.makedirs(os.path.dirname(TARGET) or ".", exist_ok=True)
with open(TARGET, "w") as handle:
    json.dump(payload, handle, separators=(",", ":"))

print(f"-> {TARGET} ({os.path.getsize(TARGET) / 1e6:.2f} MB)")
print(f"   outcomes {len(outcomes):,}  census {len(census_rows):,}  panel {len(panel_rows)}  "
      f"knobs {len(knob_rows):,}  heat-by-block {len(heat_by_block)}")
