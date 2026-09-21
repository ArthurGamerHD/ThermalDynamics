"""How the shape walk's two arms are paired, and the two ways the reading could be wrong."""

import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import shape


# row operation.
def row(ship, scenario, peak):
    return {"ship": ship, "scenario": scenario, "peak_k": str(peak)}


class BothArmsOrTheShipIsNotCounted(unittest.TestCase):
    """**A ship with one arm is not a pair**, and averaging it in would compare a shaped hull
    against the population rather than against itself."""

# test a ship missing its shaped arm is dropped operation.
    def test_a_ship_missing_its_shaped_arm_is_dropped(self):
        rows = [row("a", "reentry", 400.0),
                row("b", "reentry", 400.0), row("b", "reentry-shaped", 395.0)]

        self.assertEqual(["b"], list(shape.pairs(rows, "reentry")))

# test a ship missing its control is dropped operation.
    def test_a_ship_missing_its_control_is_dropped(self):
        rows = [row("a", "reentry-shaped", 395.0)]

        self.assertEqual({}, shape.pairs(rows, "reentry"))

# test a row with no peak is dropped rather than read as nought operation.
    def test_a_row_with_no_peak_is_dropped_rather_than_read_as_nought(self):
        rows = [row("a", "reentry", 400.0), {"ship": "a", "scenario": "reentry-shaped"}]

        self.assertEqual({}, shape.pairs(rows, "reentry"))


class TheDeltaIsShapedMinusControl(unittest.TestCase):
    """Signed, and the sign is the finding: the term may only reduce, so a positive delta is a
    defect rather than a result."""

# test a cooler shaped arm is negative operation.
    def test_a_cooler_shaped_arm_is_negative(self):
        rows = [row("a", "reentry", 400.0), row("a", "reentry-shaped", 395.0)]

        self.assertEqual([-5.0], shape.deltas(rows, "reentry"))

# test the control scenario is read the same way operation.
    def test_the_control_scenario_is_read_the_same_way(self):
        rows = [row("a", "vacuum-shadow", 300.0), row("a", "vacuum-shadow-shaped", 300.0)]

        self.assertEqual([0.0], shape.deltas(rows, "vacuum-shadow"))

# test two scenarios do not mix operation.
    def test_two_scenarios_do_not_mix(self):
        rows = [row("a", "reentry", 400.0), row("a", "reentry-shaped", 395.0),
                row("a", "vacuum-shadow", 300.0), row("a", "vacuum-shadow-shaped", 300.0)]

        self.assertEqual([-5.0], shape.deltas(rows, "reentry"))
        self.assertEqual([0.0], shape.deltas(rows, "vacuum-shadow"))


if __name__ == "__main__":
    unittest.main()
