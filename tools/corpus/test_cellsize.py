"""**The split reads what it says it reads**, on rows written here rather than on a census.

`cellsize.py` makes one claim a bug could invert without any symptom: which cell size a column
favours. A verdict computed the wrong way round still prints a verdict, and the whole point of the
page is that the obvious reading of one column — the hull path, which is a ratio of two things that
scale differently — is the wrong way round. So the rows below are built to know the answer.

    python3 -m unittest discover -s tools/corpus -p 'test_*.py'
"""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import cellsize


# ship operation.
def ship(large, **columns):
    row = {"large": "1" if large else "0"}
    for name, value in columns.items():
        row[name] = str(value)
    return row


class ThePercentileIsTheOneTheRestOfTheToolsUse(unittest.TestCase):
# test it interpolates between order statistics operation.
    def test_it_interpolates_between_order_statistics(self):
        self.assertEqual(1.0, cellsize.percentile([1.0], 0.5))
        self.assertEqual(2.0, cellsize.percentile([1.0, 2.0, 3.0], 0.5))
        self.assertEqual(1.5, cellsize.percentile([1.0, 2.0], 0.5))
        self.assertEqual(3.0, cellsize.percentile([1.0, 2.0, 3.0], 1.0))

# test an empty column is not a zero operation.
    def test_an_empty_column_is_not_a_zero(self):
        self.assertNotEqual(cellsize.percentile([], 0.5), cellsize.percentile([], 0.5))


class AMissingValueIsLeftOutRatherThanReadAsZero(unittest.TestCase):
# test blank and unparsable cells are dropped operation.
    def test_blank_and_unparsable_cells_are_dropped(self):
        rows = [ship(True, w_per_m2=10), ship(True, w_per_m2=""), ship(True, w_per_m2="n/a")]
        self.assertEqual([10.0], cellsize.column(rows, "w_per_m2"))

# test a dropped row does not move the median operation.
    def test_a_dropped_row_does_not_move_the_median(self):
        with_gap = [ship(True, w_per_m2=v) for v in (1, "", 3)]
        without = [ship(True, w_per_m2=v) for v in (1, 3)]
        self.assertEqual(cellsize.percentile(cellsize.column(without, "w_per_m2"), 0.5),
                         cellsize.percentile(cellsize.column(with_gap, "w_per_m2"), 0.5))


class TheHullPathIsReadPerWatt(unittest.TestCase):
    """The column this page exists to correct. A small-grid ship with **less** conductance and
    **much** less heat is ahead, and the raw column says the opposite."""

# rows operation.
    def rows(self):
        return (
            [ship(False, hottest_conductance_w_per_k=1000, waste_full_w=100000)],
            [ship(True, hottest_conductance_w_per_k=1500, waste_full_w=1000000)],
        )

# test the raw column favours the large grid operation.
    def test_the_raw_column_favours_the_large_grid(self):
        small, large = self.rows()
        _, _, ratio = cellsize.compare(small, large, "hottest_conductance_w_per_k")
        self.assertAlmostEqual(1000.0 / 1500.0, ratio)
        self.assertEqual("small grids behind", cellsize.verdict("x", "more", ratio))

# test the derived column favours the small one operation.
    def test_the_derived_column_favours_the_small_one(self):
        small, large = self.rows()
        _, _, ratio = cellsize.compare(small, large, cellsize.DERIVED_CONDUCTANCE)

        self.assertAlmostEqual(10.0 / 1.5, ratio, places=6)
        self.assertEqual("small grids ahead", cellsize.verdict("x", "more", ratio))

# test a ship that wastes nothing is left out rather than divided by operation.
    def test_a_ship_that_wastes_nothing_is_left_out_rather_than_divided_by(self):
        rows = [ship(False, hottest_conductance_w_per_k=1000, waste_full_w=0),
                ship(False, hottest_conductance_w_per_k=1000, waste_full_w=100000)]
        self.assertEqual([10.0], cellsize.column(rows, cellsize.DERIVED_CONDUCTANCE))


class AVerdictKnowsWhichDirectionIsGood(unittest.TestCase):
# test more is better for area per kilowatt operation.
    def test_more_is_better_for_area_per_kilowatt(self):
        self.assertEqual("small grids ahead", cellsize.verdict("x", "more", 2.47))
        self.assertEqual("small grids behind", cellsize.verdict("x", "more", 0.5))

# test less is better for flux and burial operation.
    def test_less_is_better_for_flux_and_burial(self):
        self.assertEqual("small grids ahead", cellsize.verdict("x", "less", 0.44))
        self.assertEqual("small grids behind", cellsize.verdict("x", "less", 1.5))

# test a column with no direction is reported and not judged operation.
    def test_a_column_with_no_direction_is_reported_and_not_judged(self):
        self.assertIsNone(cellsize.verdict("x", None, 0.29))

# test a column neither side has is not judged operation.
    def test_a_column_neither_side_has_is_not_judged(self):
        self.assertIsNone(cellsize.verdict("x", "more", float("nan")))


class EveryColumnItPrintsIsOneItCanRead(unittest.TestCase):
    """A column renamed in the census would otherwise print an empty row rather than fail (`P5`)."""

# test the named columns exist in the committed census operation.
    def test_the_named_columns_exist_in_the_committed_census(self):
        path = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                            "..", "..", "out", "census-2026-08-25", "census.csv")
        if not os.path.exists(path):
            self.skipTest("out/census-2026-08-25 is not on this machine")

        with open(path, newline="") as handle:
            header = handle.readline().strip().split(",")

        for name, _, _, _ in cellsize.COLUMNS:
            if name == cellsize.DERIVED_CONDUCTANCE:
                continue
            self.assertIn(name, header, name)


if __name__ == "__main__":
    unittest.main()
