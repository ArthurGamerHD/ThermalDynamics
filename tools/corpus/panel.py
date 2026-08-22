#!/usr/bin/env python3
"""Choose a standing panel of ships from the corpus, each for a stated reason.

A population answers "what do ships do"; it cannot answer "what does this knob do", because
re-simulating 8,142 ships for every value of every dial is hours per dial. A panel is the
instrument for that question, and it is only as good as its spread: it has to carry both grid
sizes, both failure modes the census separated, the archetypes that misbehave, and enough
well-behaved hulls that a change which breaks them is visible.

Every ship is picked by a named rule and the rule is written into the output, so the panel can be
audited and rebuilt rather than trusted.

Usage: panel.py <census.csv> <outcomes.csv> [out.csv]
"""
import csv
import os
import sys

CENSUS = sys.argv[1] if len(sys.argv) > 1 else "out/census-2026-08-21/census.csv"
OUTCOMES = sys.argv[2] if len(sys.argv) > 2 else "out/corpus-2026-08-21/outcomes.csv"
TARGET = sys.argv[3] if len(sys.argv) > 3 else "tools/corpus/panel.csv"

# ships.csv is the only file carrying each blueprint's path, and the sweep needs it: without a
# path it has to walk and fully parse all 9,981 corpus blueprints to find 36 ships, with 31
# workers opening quarter-gigabyte files at once. That is the exact shape CorpusFixture documents
# as how earlier runs died, and it wedged the first smoke test. With paths the sweep opens 36
# files. Falls back to the survey's own directory when not given one.
SHIPS = os.path.join(os.path.dirname(OUTCOMES), "ships.csv")

# The per-type dials need hulls that actually mount the type, or they measure nothing at all.
COMPOSITION = os.path.join(os.path.dirname(CENSUS), "composition.csv")

# Scenarios that must exist for a ship to be eligible: a panel member has to be measurable in
# every state the sweep will put it in, or its rows are holes in the response surface.
REQUIRED = {"idle", "vacuum-sunlit", "full-electrical", "burn-forward", "recovery"}


def number(row, key, default=0.0):
    try:
        return float(row[key])
    except (TypeError, ValueError, KeyError):
        return default


def key_of(row):
    return (row["ship"], row["workshop_id"])


census = {key_of(r): r for r in csv.DictReader(open(CENSUS))}

# Which hulls carry each offending type, and how much of their heat it is.
share = {}
if os.path.exists(COMPOSITION):
    for r in csv.DictReader(open(COMPOSITION)):
        k = key_of(r)
        by_type = share.setdefault(k, {})
        for name, match in (("jumpdrive", "JumpDrive"), ("engine", "HydrogenEngine"),
                            ("reactor", "Reactor"), ("thrust", "Thrust")):
            if match.lower() in r["subtype"].lower():
                by_type[name] = by_type.get(name, 0.0) + number(r, "waste_full_w")
else:
    print(f"warning: {COMPOSITION} not found — the per-type rules will select nothing")

paths = {}
if os.path.exists(SHIPS):
    for r in csv.DictReader(open(SHIPS)):
        paths[key_of(r)] = r.get("path", "")
else:
    print(f"warning: {SHIPS} not found — the panel will carry no blueprint paths")
outcomes = {}
for row in csv.DictReader(open(OUTCOMES)):
    outcomes.setdefault(key_of(row), {})[row["scenario"]] = row

# A ship is eligible when both passes measured it under every scenario.
ships = [k for k in census if k in outcomes and REQUIRED <= set(outcomes[k])]
print(f"{len(ships):,} ships measured under all {len(REQUIRED)} scenarios")


def stat(k, scenario, column):
    return number(outcomes[k][scenario], column)


def large(k):
    return int(number(census[k], "large"))


def critical_count(k):
    """Scenarios in which this ship put at least one block over its critical temperature."""
    return sum(1 for s in REQUIRED if stat(k, s, "over_critical") > 0)


picked = {}


def take(rule, note, candidates, count=1):
    """Takes the top `count` candidates not already on the panel, recording why."""
    taken = 0
    for k in candidates:
        if k in picked or taken >= count:
            continue
        picked[k] = (rule, note)
        taken += 1


def by(fn, reverse=True, where=None):
    pool = [k for k in ships if where is None or where(k)]
    return sorted(pool, key=fn, reverse=reverse)


big = lambda k: large(k) == 1
small = lambda k: large(k) == 0

# **A hull with no heat cannot answer a question about heat**, and the floor has to be a ship's
# load rather than a token. At 1 kW the "most surface per kilowatt" rules selected unpowered
# stations — a Death Star with no reactor reports 1,351,882 m2/kW because the denominator is
# rounding error, not because it is well cooled. A third of the corpus makes under 100 kW.
POWER_FLOOR = 100000.0
powered = lambda k: number(census[k], "waste_full_w") >= POWER_FLOOR

# **The panel has a block budget, because it is going to be re-simulated hundreds of times.**
# Measured on the survey, a block-scenario costs about 0.127 ms, so five scenarios over a panel
# of 350,000 blocks is roughly four minutes — which is what makes a forty-point sweep of a dial
# an afternoon rather than a week. Megahulls are excluded on that ground alone; one capital is
# admitted under its own rule because solver cost is criterion G6 and only large hulls show it.
BLOCK_CEILING = 30000
CAPITAL_CEILING = 80000
affordable = lambda k: number(census[k], "blocks") <= BLOCK_CEILING

# ---- the archetype the survey named ------------------------------------------------------
# Idle-critical ships are the finding that started this: enclosed ground vehicles that cook
# themselves parked. They are the only ships where G1 is live, so the panel must carry them.
take("idle-critical", "cooks itself parked — G1 is live on this hull",
     by(lambda k: stat(k, "idle", "peak_k"),
        where=lambda k: stat(k, "idle", "over_critical") > 0 and affordable(k)), 3)

# ---- the two failure modes the census separated -------------------------------------------
# Large grids fail by burial (rho +0.57 buried_share); small grids fail by having neither
# surface nor mass per watt (rho -0.84 on both). One extreme of each, per grid size.
take("buried-large", "large hull that buries its heat — the large-grid failure mode",
     by(lambda k: number(census[k], "buried_share"), where=lambda k: big(k) and powered(k) and affordable(k)), 2)
take("starved-small", "small hull with the least radiating surface per kilowatt",
     by(lambda k: -number(census[k], "exposure_m2_per_kw"), where=lambda k: small(k) and powered(k) and affordable(k)), 2)
take("thin-small", "small hull with the least heat capacity per watt — fastest to move",
     by(lambda k: -number(census[k], "capacity_j_per_k_per_w"), where=lambda k: small(k) and powered(k) and affordable(k)), 2)

# ---- power density, which the census made the strongest design predictor -------------------
take("dense-large", "most waste heat per block, large grid",
     by(lambda k: number(census[k], "waste_full_w") / max(1.0, number(census[k], "blocks")),
        where=lambda k: big(k) and powered(k) and affordable(k)), 2)
take("dense-small", "most waste heat per block, small grid",
     by(lambda k: number(census[k], "waste_full_w") / max(1.0, number(census[k], "blocks")),
        where=lambda k: small(k) and powered(k) and affordable(k)), 2)

# ---- the well-cooled end, so a change that ruins good designs is visible --------------------
take("airy-large", "most radiating surface per kilowatt, large grid — should stay cold",
     by(lambda k: number(census[k], "exposure_m2_per_kw"), where=lambda k: big(k) and powered(k) and affordable(k)), 2)
take("airy-small", "most radiating surface per kilowatt, small grid — should stay cold",
     by(lambda k: number(census[k], "exposure_m2_per_kw"), where=lambda k: small(k) and powered(k) and affordable(k)), 2)

# ---- scale, at both ends -------------------------------------------------------------------
take("capital", "the largest hull the budget allows — solver cost, which is G6",
     by(lambda k: number(census[k], "blocks"),
        where=lambda k: big(k) and powered(k)
        and number(census[k], "blocks") <= CAPITAL_CEILING), 1)
take("tiny", "the smallest hulls that still carry power",
     by(lambda k: -number(census[k], "blocks"), where=lambda k: small(k) and powered(k) and affordable(k)), 2)

# ---- solver cost, which is criterion G6 -----------------------------------------------------
take("expensive", "most substeps demanded — G6 is decided on ships like these",
     by(lambda k: stat(k, "full-electrical", "substeps_demanded"), where=affordable), 2)

# ---- the boundary, which is where a balance change actually shows ---------------------------
# A ship already melting stays melting and a cold ship stays cold; the ones that cross under one
# scenario and not another are where a moved dial changes the verdict rather than the number.
take("boundary", "critical in exactly one scenario — a moved dial flips this hull",
     by(lambda k: stat(k, "full-electrical", "peak_k"),
        where=lambda k: critical_count(k) == 1 and powered(k) and affordable(k)), 4)

# ---- controls -------------------------------------------------------------------------------
# Ships that never go critical anywhere, at both grid sizes. If a change puts these over, it is
# too aggressive whatever it does for the offenders.
take("control-large", "never critical in any scenario, large grid — must stay safe",
     by(lambda k: number(census[k], "waste_full_w"),
        where=lambda k: big(k) and powered(k) and affordable(k) and critical_count(k) == 0), 3)
take("control-small", "never critical in any scenario, small grid — must stay safe",
     by(lambda k: number(census[k], "waste_full_w"),
        where=lambda k: small(k) and powered(k) and affordable(k) and critical_count(k) == 0), 3)

# ---- the offending types, so the per-type dials have somewhere to act -------------------------
# A dial aimed at jump drives measures nothing on a panel with no jump drives. Each of these picks
# the hulls where that type carries the most heat, so the dial has the largest lever to show.
def carries(name):
    return lambda k: share.get(k, {}).get(name, 0.0) > 0.0


def carried(name):
    return lambda k: share.get(k, {}).get(name, 0.0)


take("jumpdrive-heavy", "most heat from jump drives — 67 % of the corpus's load heat is this block",
     by(carried("jumpdrive"), where=lambda k: carries("jumpdrive")(k) and affordable(k)), 2)
take("engine-heavy", "most heat from hydrogen engines — hottest block on a third of runaway rows",
     by(carried("engine"), where=lambda k: carries("engine")(k) and affordable(k)), 2)
take("reactor-heavy", "most heat from reactors — the dial that ships at 0.01",
     by(carried("reactor"), where=lambda k: carries("reactor")(k) and affordable(k)), 2)

# ---- how the heat is arranged, which is what a hot spot is about -------------------------------
# The census measures clumping and burial depth; the panel carries the extremes of both so a dial
# can be judged against arrangement rather than only against totals.
if any("clumping" in r for r in census.values()):
    take("clumped", "heat sources stacked in one place — the lowest clumping index",
         by(lambda k: -number(census[k], "clumping"),
            where=lambda k: powered(k) and affordable(k)
            and number(census[k], "clumping") > 0), 2)
    take("dispersed", "heat spread more evenly than chance — the highest clumping index",
         by(lambda k: number(census[k], "clumping"),
            where=lambda k: powered(k) and affordable(k)), 2)
    take("deep", "heat buried furthest from anything that radiates, in conduction hops",
         by(lambda k: number(census[k], "heat_depth_mean"),
            where=lambda k: powered(k) and affordable(k)), 2)
    take("dense-surface", "most watts per square metre of hull — the whole-ship density",
         by(lambda k: number(census[k], "w_per_m2"),
            where=lambda k: powered(k) and affordable(k)), 2)

# ---- thrust and sun, so the friction and solar dials have somewhere to land ------------------
take("thrust-heavy", "most installed thrust — the dial for friction and burn heating",
     by(lambda k: number(census[k], "thrust_n"), where=affordable), 2)
take("broad-face", "most exposed area outright — the sun dial acts hardest here",
     by(lambda k: number(census[k], "exposed_area_m2"),
        where=lambda k: powered(k) and affordable(k)), 2)

# ---- write it out ---------------------------------------------------------------------------
columns = ["ship", "workshop_id", "path", "rule", "why", "large", "blocks", "buried_share",
           "clumping", "heat_depth_mean", "w_per_m2", "local_w_per_m2_max", "heat_spread_m",
           "exposed_area_m2", "exposure_m2_per_kw", "capacity_j_per_k_per_w",
           "installed_power_w", "waste_full_w", "thrust_n",
           "idle_peak_k", "load_peak_k", "burn_peak_k", "scenarios_critical",
           "load_seconds_to_critical", "substeps_demanded", "top_source"]

rows = []
for k, (rule, note) in picked.items():
    c = census[k]
    rows.append({
        "ship": k[0], "workshop_id": k[1], "path": paths.get(k, ""),
        "rule": rule, "why": note,
        "large": int(number(c, "large")), "blocks": int(number(c, "blocks")),
        "buried_share": round(number(c, "buried_share"), 3),
        "clumping": round(number(c, "clumping"), 3),
        "heat_depth_mean": round(number(c, "heat_depth_mean"), 2),
        "w_per_m2": round(number(c, "w_per_m2"), 2),
        "local_w_per_m2_max": round(number(c, "local_w_per_m2_max"), 1),
        "heat_spread_m": round(number(c, "heat_spread_m"), 1),
        "exposed_area_m2": round(number(c, "exposed_area_m2"), 1),
        "exposure_m2_per_kw": round(number(c, "exposure_m2_per_kw"), 3),
        "capacity_j_per_k_per_w": round(number(c, "capacity_j_per_k_per_w"), 3),
        "installed_power_w": int(number(c, "installed_power_w")),
        "waste_full_w": int(number(c, "waste_full_w")),
        "thrust_n": int(number(c, "thrust_n")),
        "idle_peak_k": round(stat(k, "idle", "peak_k"), 1),
        "load_peak_k": round(stat(k, "full-electrical", "peak_k"), 1),
        "burn_peak_k": round(stat(k, "burn-forward", "peak_k"), 1),
        "scenarios_critical": critical_count(k),
        "load_seconds_to_critical": round(stat(k, "full-electrical", "seconds_to_critical"), 1),
        "substeps_demanded": round(stat(k, "full-electrical", "substeps_demanded"), 2),
        "top_source": c.get("top_source", ""),
    })

rows.sort(key=lambda r: (r["rule"], -r["blocks"]))
with open(TARGET, "w", newline="") as handle:
    writer = csv.DictWriter(handle, fieldnames=columns)
    writer.writeheader()
    writer.writerows(rows)

missing = sum(1 for r in rows if not r["path"])
if missing:
    print(f"warning: {missing} panel ships have no blueprint path — the sweep will scan for those")
print(f"panel of {len(rows)} ships -> {TARGET}")
print(f"  large grid {sum(1 for r in rows if r['large'])}, "
      f"small grid {sum(1 for r in rows if not r['large'])}")
print(f"  blocks {min(r['blocks'] for r in rows):,} to {max(r['blocks'] for r in rows):,}")
print(f"  never critical {sum(1 for r in rows if r['scenarios_critical'] == 0)}, "
      f"always critical {sum(1 for r in rows if r['scenarios_critical'] == 5)}")
total = sum(r["blocks"] for r in rows)
print(f"  panel blocks {total:,} -> about {total * 5 * 0.127 / 1000 / 60:.1f} min per configuration")
