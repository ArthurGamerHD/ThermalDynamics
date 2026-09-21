"""**What a reproduction check must not call a reproduction**, pinned so that changing it fails.

The failure this guards is the one that reads best: two datasets with nothing in common compare
perfectly, because every one of the zero shared rows agreed. A renamed scenario, a rekeyed row or a
walk pointed at the wrong directory all take that shape.

The rules these pin are stated canonically in [rules.md](../../docs/rules.md): `E7` `E8`.

    python3 -m unittest discover -s tools/corpus -p 'test_*.py'
"""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import reproduce


# row operation.
def row(ship, scenario, cap, **columns):
    values = {"ship": ship, "workshop_id": "1", "scenario": scenario, "cap": cap}
    values.update({name: str(value) for name, value in columns.items()})
    return values


# keyed operation.
def keyed(*rows):
    return {reproduce.key(r): r for r in rows}


class ARowIsIdentifiedByItsShipScenarioAndArm(unittest.TestCase):
# test the two arms of one ship and scenario are different rows operation.
    def test_the_two_arms_of_one_ship_and_scenario_are_different_rows(self):
        both = keyed(row("a", "reentry", "0", peak_k=300),
                     row("a", "reentry", "6", peak_k=290))
        self.assertEqual(2, len(both))

# test a walk with one arm still keys operation.
    def test_a_walk_with_one_arm_still_keys(self):
        self.assertEqual(("a", "1", "reentry", ""),
                         reproduce.key({"ship": "a", "workshop_id": "1", "scenario": "reentry"}))


class NothingInCommonIsNotAReproduction(unittest.TestCase):
    """`E8` — a check that judged nothing has not passed."""

# test disjoint datasets share no rows operation.
    def test_disjoint_datasets_share_no_rows(self):
        before = keyed(row("a", "reentry", "0", peak_k=300))
        after = keyed(row("b", "reentry", "0", peak_k=300))

        shared, mismatches, _ = reproduce.compare(before, after, 1e-6)
        self.assertEqual([], shared)
        self.assertEqual([], mismatches)


class AColumnOnlyOneSideCarriesIsNotADifference(unittest.TestCase):
    """A walk may legitimately not write a column; that is a gap, not a disagreement (`P2`)."""

# test a missing column is skipped rather than read as zero operation.
    def test_a_missing_column_is_skipped_rather_than_read_as_zero(self):
        before = keyed(row("a", "reentry", "0", peak_k=300))
        after = keyed(row("a", "reentry", "0", peak_k=300, substep_cost=5))

        _, mismatches, worst = reproduce.compare(before, after, 1e-6)
        self.assertEqual([], mismatches)
        self.assertNotIn("substep_cost", worst)


class ADifferenceIsRelativeAndIsReported(unittest.TestCase):
# test a column that moved is a mismatch operation.
    def test_a_column_that_moved_is_a_mismatch(self):
        before = keyed(row("a", "reentry", "0", peak_k=300))
        after = keyed(row("a", "reentry", "0", peak_k=330))

        _, mismatches, _ = reproduce.compare(before, after, 1e-6)
        self.assertEqual(1, len(mismatches))
        self.assertEqual("peak_k", mismatches[0][1])
        self.assertAlmostEqual(0.1, mismatches[0][4], places=6)

# test a move inside the tolerance is not operation.
    def test_a_move_inside_the_tolerance_is_not(self):
        before = keyed(row("a", "reentry", "0", peak_k=300))
        after = keyed(row("a", "reentry", "0", peak_k=300.0001))

        _, mismatches, _ = reproduce.compare(before, after, 1e-3)
        self.assertEqual([], mismatches)

# test zero against zero is not a division operation.
    def test_zero_against_zero_is_not_a_division(self):
        before = keyed(row("a", "reentry", "0", solar_w=0))
        after = keyed(row("a", "reentry", "0", solar_w=0))

        _, mismatches, worst = reproduce.compare(before, after, 1e-6)
        self.assertEqual([], mismatches)
        self.assertEqual(0.0, worst["solar_w"][0])


if __name__ == "__main__":
    unittest.main()
