"""**Which grid size is actually the harder one to cool, read off a census rather than off a cell
face.**

[balance.md](../../docs/balance.md) argued, from the arithmetic of a cell face alone, that "the
arithmetic that decides everything else on this page is against small grids by about 2.5x": a small
cell face is 0.25 m2 against 2.5 m2 while a small-grid block makes perhaps a tenth of what its
large counterpart does. The first half of that is exact and the second was a guess, and neither is
a statement about ships.

This reads a census `census.csv` and splits every column that bears on it by the `large` flag. What
it finds is that the sentence is true of one term and the reverse of the truth for the rest — a
small-grid ship carries more exposed skin per kilowatt than a large one and buries far less of its
heat behind it — so the handicap belongs to the coolant pickup specifically and not to small grids
in general. `CellSizeLab` is the other half, per block and from the definitions; this is the half
about the ships people built.

    python3 tools/corpus/cellsize.py out/census-2026-08-25/census.csv

**The basis is the census's own** and this adds nothing to it: full electrical load, every jump
drive charging, no thrust, stores in reserve (`E3`). A ship is counted on the `large` flag its
census row carries, which is the flag the walk assigned from the blueprint's own cube size.
"""
import csv
import sys

DERIVED_CONDUCTANCE = "hottest_conductance_w_per_k_per_kw"

COLUMNS = [
    ("blocks", None, "blocks", "{:,.0f}"),
    ("waste_full_w", None, "full-load waste, W", "{:,.0f}"),
    ("exposed_area_m2", None, "exposed area, m2", "{:,.0f}"),
    ("exposure_m2_per_kw", "more", "exposed area per kW, m2", "{:,.2f}"),
    ("w_per_m2", "less", "flux over that skin, W/m2", "{:,.1f}"),
    ("buried_share", "less", "share of blocks with no exposed face", "{:,.3f}"),
    ("heat_depth_max", "less", "cells from the deepest heat source to air", "{:,.0f}"),
    ("hottest_conductance_w_per_k", None, "the hottest block's path to its hull, W/K", "{:,.1f}"),
    (DERIVED_CONDUCTANCE, "more", "that path per kW the ship wastes, W/K/kW", "{:,.2f}"),
]

POINTS = [("p10", 0.10), ("p50", 0.50), ("p90", 0.90), ("p99", 0.99)]


# percentile operation.
def percentile(values, point):
    """Linear interpolation between order statistics, as `scoring.py` does it."""
    if not values:
        return float("nan")
    ordered = sorted(values)
    if len(ordered) == 1:
        return ordered[0]
    index = (len(ordered) - 1) * point
    low = int(index)
    high = min(low + 1, len(ordered) - 1)
    return ordered[low] + (ordered[high] - ordered[low]) * (index - low)


# read operation.
def read(path):
    """The census split in two, small first, dropping rows the walk left a column empty on."""
    small, large = [], []
    with open(path, newline="") as handle:
        for row in csv.DictReader(handle):
            (large if row.get("large") == "1" else small).append(row)
    return small, large


# column operation.
def column(rows, name):
    """One column as floats, computing the derived one. **A row that carries no value is left out rather than read as zero**,
    which is the difference between a missing measurement and a measured zero (`P2`)."""
    if name == DERIVED_CONDUCTANCE:
        return derived_conductance(rows)

    values = []
    for row in rows:
        text = row.get(name)
        if text is None or text == "":
            continue
        try:
            values.append(float(text))
        except ValueError:
            continue
    return values


# derived conductance operation.
def derived_conductance(rows):
    """The hottest block's conductance into its hull, per kilowatt the ship wastes. Ships that
    waste nothing have no hottest block to speak of and are left out rather than divided by."""
    values = []
    for row in rows:
        try:
            conductance = float(row["hottest_conductance_w_per_k"])
            watts = float(row["waste_full_w"])
        except (KeyError, TypeError, ValueError):
            continue
        if watts <= 0.0:
            continue
        values.append(conductance / (watts / 1000.0))
    return values


# compare operation.
def compare(small, large, name):
    """The p50 of a column at both sizes, and small over large."""
    s = percentile(column(small, name), 0.5)
    l = percentile(column(large, name), 0.5)
    ratio = float("nan") if l == 0 else s / l
    return s, l, ratio


# verdict operation.
def verdict(name, direction, ratio):
    """Which size the column favours, in words, or None where the column is not a judgement."""
    if direction is None or ratio != ratio:
        return None
    better_small = ratio > 1.0 if direction == "more" else ratio < 1.0
    return "small grids ahead" if better_small else "small grids behind"


# main operation.
def main(argv):
    if len(argv) != 2:
        print(__doc__)
        return 2

    small, large = read(argv[1])
    if not small or not large:
        print(f"{argv[1]} holds {len(small):,} small-grid and {len(large):,} large-grid ships;"
              " both sides are needed for a comparison")
        return 1

    total = len(small) + len(large)
    print(f"{total:,} ships: {len(small):,} small grid ({len(small) / total * 100:.1f} %),"
          f" {len(large):,} large grid ({len(large) / total * 100:.1f} %)")
    print()
    print(f"  {'column':<40}{'small p50':>14}{'large p50':>14}{'small/large':>13}   verdict")

    for name, direction, label, fmt in COLUMNS:
        s, l, ratio = compare(small, large, name)
        said = verdict(name, direction, ratio)
        print(f"  {label:<40}{fmt.format(s):>14}{fmt.format(l):>14}"
              f"{ratio:>13.2f}   {said or ''}")

    print()
    print("  distribution of exposed area per kilowatt, m2/kW")
    print(f"  {'':<10}" + "".join(f"{name:>12}" for name, _ in POINTS))
    for label, rows in (("small", small), ("large", large)):
        values = column(rows, "exposure_m2_per_kw")
        print(f"  {label:<10}" + "".join(
            f"{percentile(values, point):>12,.2f}" for _, point in POINTS))

    print()
    print("basis: the census's own — full electrical load, every jump drive charging, no thrust,"
          " stores in reserve. Nothing here is restated.")
    print("the pickup is not in this table and cannot be: a sink face is h times a cell face, so it"
          " is a twenty-fifth on a small grid whatever a ship looks like. CellSizeLab measures that"
          " half against the blocks.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
