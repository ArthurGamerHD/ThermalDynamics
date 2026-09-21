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

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import scoring

CENSUS = sys.argv[1] if len(sys.argv) > 1 else "out/census-2026-08-21/census.csv"
OUTCOMES = sys.argv[2] if len(sys.argv) > 2 else "out/corpus-2026-08-21/outcomes.csv"
TARGET = sys.argv[3] if len(sys.argv) > 3 else "tools/corpus/panel.csv"

SHIPS = os.path.join(os.path.dirname(OUTCOMES), "ships.csv")

COMPOSITION = os.path.join(os.path.dirname(CENSUS), "composition.csv")

REQUIRED = {"idle", "vacuum-sunlit", "full-electrical", "burn-forward", "recovery"}


# number operation.
def number(row, key, default=0.0):
    """A cell as a float, defaulting to nought — this tool's own choice (`scoring.number_or`).

    The panel is picked from a census that carries every column it names, and a ship
    missing one is a ship with none of that thing rather than a ship we cannot read.
    """
    return scoring.number_or(row, key, default)




census = {scoring.key_of(r): r for r in csv.DictReader(open(CENSUS))}

TYPES = {
    "jumpdrive": "JumpDrive",
    "engine": "HydrogenEngine",
    "reactor": "Reactor",
    "oxygen": "OxygenGenerator",
}

share = {}
carriers = {name: 0 for name in TYPES}
if os.path.exists(COMPOSITION):
    for r in csv.DictReader(open(COMPOSITION)):
        k = scoring.key_of(r)
        by_type = share.setdefault(k, {})
        for name, type_id in TYPES.items():
            if r["type_id"] == type_id:
                if name not in by_type:
                    carriers[name] += 1
                by_type[name] = by_type.get(name, 0.0) + number(r, "waste_full_w")
else:
    print(f"warning: {COMPOSITION} not found — the per-type rules will select nothing")

for name in sorted(TYPES):
    note = "" if carriers[name] else "   <-- NOTHING: this rule will select no ships"
    print(f"  {name:<10} carried by {carriers[name]:>5,} ships{note}")

paths = {}
if os.path.exists(SHIPS):
    for r in csv.DictReader(open(SHIPS)):
        paths[scoring.key_of(r)] = r.get("path", "")
else:
    print(f"warning: {SHIPS} not found — the panel will carry no blueprint paths")
outcomes = {}
for row in csv.DictReader(open(OUTCOMES)):
    outcomes.setdefault(scoring.key_of(row), {})[row["scenario"]] = row

ships = [k for k in census if k in outcomes and REQUIRED <= set(outcomes[k])]
print(f"{len(ships):,} ships measured under all {len(REQUIRED)} scenarios")


# stat operation.
def stat(k, scenario, column):
    return number(outcomes[k][scenario], column)


# large operation.
def large(k):
    return int(number(census[k], "large"))


# critical count operation.
def critical_count(k):
    """Scenarios in which this ship put at least one block over its critical temperature."""
    return sum(1 for s in REQUIRED if stat(k, s, "over_critical") > 0)


picked = {}


# take operation.
def take(rule, note, candidates, count=1):
    """Takes the top `count` candidates not already on the panel, recording why."""
    taken = 0
    for k in candidates:
        if k in picked or taken >= count:
            continue
        picked[k] = (rule, note)
        taken += 1


# by operation.
def by(fn, reverse=True, where=None):
    pool = [k for k in ships if where is None or where(k)]
    return sorted(pool, key=fn, reverse=reverse)


big = lambda k: large(k) == 1
small = lambda k: large(k) == 0

POWER_FLOOR = 100000.0
powered = lambda k: number(census[k], "waste_full_w") >= POWER_FLOOR

BLOCK_CEILING = 30000
CAPITAL_CEILING = 80000
affordable = lambda k: number(census[k], "blocks") <= BLOCK_CEILING

take("idle-critical", "cooks itself parked — G1 is live on this hull",
     by(lambda k: stat(k, "idle", "peak_k"),
        where=lambda k: stat(k, "idle", "over_critical") > 0 and affordable(k)), 3)

take("buried-large", "large hull that buries its heat — the large-grid failure mode",
     by(lambda k: number(census[k], "buried_share"), where=lambda k: big(k) and powered(k) and affordable(k)), 2)
take("starved-small", "small hull with the least radiating surface per kilowatt",
     by(lambda k: -number(census[k], "exposure_m2_per_kw"), where=lambda k: small(k) and powered(k) and affordable(k)), 2)
take("thin-small", "small hull with the least heat capacity per watt — fastest to move",
     by(lambda k: -number(census[k], "capacity_j_per_k_per_w"), where=lambda k: small(k) and powered(k) and affordable(k)), 2)

take("dense-large", "most waste heat per block, large grid",
     by(lambda k: number(census[k], "waste_full_w") / max(1.0, number(census[k], "blocks")),
        where=lambda k: big(k) and powered(k) and affordable(k)), 2)
take("dense-small", "most waste heat per block, small grid",
     by(lambda k: number(census[k], "waste_full_w") / max(1.0, number(census[k], "blocks")),
        where=lambda k: small(k) and powered(k) and affordable(k)), 2)

take("airy-large", "most radiating surface per kilowatt, large grid — should stay cold",
     by(lambda k: number(census[k], "exposure_m2_per_kw"), where=lambda k: big(k) and powered(k) and affordable(k)), 2)
take("airy-small", "most radiating surface per kilowatt, small grid — should stay cold",
     by(lambda k: number(census[k], "exposure_m2_per_kw"), where=lambda k: small(k) and powered(k) and affordable(k)), 2)

take("capital", "the largest hull the budget allows — solver cost, which is G6",
     by(lambda k: number(census[k], "blocks"),
        where=lambda k: big(k) and powered(k)
        and number(census[k], "blocks") <= CAPITAL_CEILING), 1)
take("tiny", "the smallest hulls that still carry power",
     by(lambda k: -number(census[k], "blocks"), where=lambda k: small(k) and powered(k) and affordable(k)), 2)

take("expensive", "most substeps demanded — G6 is decided on ships like these",
     by(lambda k: stat(k, "full-electrical", "substeps_demanded"), where=affordable), 2)

take("boundary", "critical in exactly one scenario — a moved dial flips this hull",
     by(lambda k: stat(k, "full-electrical", "peak_k"),
        where=lambda k: critical_count(k) == 1 and powered(k) and affordable(k)), 4)

take("control-large", "never critical in any scenario, large grid — must stay safe",
     by(lambda k: number(census[k], "waste_full_w"),
        where=lambda k: big(k) and powered(k) and affordable(k) and critical_count(k) == 0), 3)
take("control-small", "never critical in any scenario, small grid — must stay safe",
     by(lambda k: number(census[k], "waste_full_w"),
        where=lambda k: small(k) and powered(k) and affordable(k) and critical_count(k) == 0), 3)

# carries operation.
def carries(name):
    return lambda k: share.get(k, {}).get(name, 0.0) > 0.0


# carried operation.
def carried(name):
    return lambda k: share.get(k, {}).get(name, 0.0)


take("jumpdrive-heavy", "most heat from jump drives — 67 % of the corpus's load heat is this block",
     by(carried("jumpdrive"), where=lambda k: carries("jumpdrive")(k) and affordable(k)), 2)
take("engine-heavy", "most heat from hydrogen engines — hottest block on a third of runaway rows",
     by(carried("engine"), where=lambda k: carries("engine")(k) and affordable(k)), 2)
take("reactor-heavy", "most heat from reactors — the dial that ships at 0.01",
     by(carried("reactor"), where=lambda k: carries("reactor")(k) and affordable(k)), 2)
take("oxygen-heavy", "most heat from oxygen generators — the dial that moved to 0.40 on 2026-08-25",
     by(carried("oxygen"), where=lambda k: carries("oxygen")(k) and affordable(k)), 2)

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

take("thrust-heavy", "most installed thrust — the dial for friction and burn heating",
     by(lambda k: number(census[k], "thrust_n"), where=affordable), 2)
take("broad-face", "most exposed area outright — the sun dial acts hardest here",
     by(lambda k: number(census[k], "exposed_area_m2"),
        where=lambda k: powered(k) and affordable(k)), 2)

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
