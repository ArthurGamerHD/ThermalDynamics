#!/usr/bin/env python3
"""`censusdiff.py` on censuses built here, so its arithmetic is checked rather than trusted.

The thing worth checking is not the subtraction — it is the two ways this kind of script lies. A
comparison over the ships two datasets *share* prints a clean number when one of them lost half the
population, and a prediction scored against a band is worth nothing if the band is read from the
data. So the cases below are: a re-take that drops ships, a re-take that adds them, and a
prediction that fails.
"""
import csv
import os
import shutil
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import censusdiff

CENSUS_FIELDS = ["ship", "workshop_id", "blocks", "grids", "joints", "rooms",
                 "exposed_blocks", "buried_blocks", "sealed_blocks", "exposed_area_m2",
                 "thermal_mass_j_per_k", "waste_idle_w", "waste_full_w", "waste_burn_w",
                 "installed_power_w", "consumer_draw_w"]

COMPOSITION_FIELDS = ["ship", "workshop_id", "subtype", "type_id", "count",
                      "waste_full_w", "share_of_waste"]


def ship(name, waste, area=100.0, blocks=10):
    row = {field: "0" for field in CENSUS_FIELDS}
    row["ship"] = name
    row["workshop_id"] = name
    row["blocks"] = str(blocks)
    row["exposed_area_m2"] = str(area)
    row["waste_full_w"] = str(waste)
    return row


def write(directory, ships, parts=()):
    os.makedirs(directory, exist_ok=True)

    with open(os.path.join(directory, "census.csv"), "w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=CENSUS_FIELDS)
        writer.writeheader()
        for row in ships:
            writer.writerow(row)

    if not parts:
        return

    with open(os.path.join(directory, "composition.csv"), "w", newline="",
              encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=COMPOSITION_FIELDS)
        writer.writeheader()
        for row in parts:
            writer.writerow(row)


def part(name, type_id, watts):
    return {"ship": name, "workshop_id": name, "subtype": "", "type_id": type_id,
            "count": "1", "waste_full_w": str(watts), "share_of_waste": "0"}


class CensusDiffReadsBothSides(unittest.TestCase):
    def setUp(self):
        self.root = tempfile.mkdtemp(prefix="censusdiff-")

    def tearDown(self):
        shutil.rmtree(self.root, ignore_errors=True)

    def paths(self):
        return os.path.join(self.root, "before"), os.path.join(self.root, "after")

    def test_a_rise_in_waste_is_scored_against_its_band(self):
        before, after = self.paths()
        write(before, [ship("a", 100), ship("b", 100)])
        write(after, [ship("a", 109), ship("b", 109)])

        self.assertEqual(0, censusdiff.main([before, after, "--expect", "waste_full_w=8:10"]))
        self.assertEqual(1, censusdiff.main([before, after, "--expect", "waste_full_w=0:5"]))

    def test_a_prediction_that_fails_makes_the_run_fail(self):
        before, after = self.paths()
        write(before, [ship("a", 100)])
        write(after, [ship("a", 400)])

        self.assertEqual(1, censusdiff.main([before, after, "--expect", "waste_full_w=8:10"]))

    def test_the_ship_count_is_its_own_prediction(self):
        before, after = self.paths()
        write(before, [ship("a", 100), ship("b", 100)])
        write(after, [ship("a", 100)])

        # The shared-ship comparison is spotless — a is identical — and the population halved.
        self.assertEqual(0, censusdiff.main([before, after, "--expect", "waste_full_w=-1:1"]))
        self.assertEqual(1, censusdiff.main([before, after, "--expect", "ships=0:0"]))

    def test_a_census_with_nothing_in_common_is_refused_rather_than_reported(self):
        before, after = self.paths()
        write(before, [ship("a", 100)])
        write(after, [ship("b", 100)])

        self.assertEqual(1, censusdiff.main([before, after]))

    def test_a_missing_census_is_refused(self):
        before, after = self.paths()
        write(before, [ship("a", 100)])

        self.assertEqual(2, censusdiff.main([before, after]))

    def test_a_type_that_appears_for_the_first_time_is_visible(self):
        before, after = self.paths()
        write(before, [ship("a", 100)], [part("a", "OxygenGeneratorSmall", 100)])
        write(after, [ship("a", 600)],
              [part("a", "OxygenGeneratorSmall", 100), part("a", "OxygenGenerator", 500)])

        watts_before, ships_before = censusdiff.composition(before)
        watts_after, ships_after = censusdiff.composition(after)

        self.assertNotIn("OxygenGenerator", watts_before)
        self.assertEqual(500.0, watts_after["OxygenGenerator"])
        self.assertEqual(1, ships_after["OxygenGenerator"])
        self.assertEqual({}, {k: v for k, v in ships_before.items() if k == "OxygenGenerator"})

    def test_share_refuses_to_divide_by_nothing(self):
        self.assertIsNone(censusdiff.share(0.0, 5.0))
        self.assertAlmostEqual(100.0, censusdiff.share(1.0, 2.0))


if __name__ == "__main__":
    unittest.main()
