#!/usr/bin/env python3
"""**What the scorers count and what they drop**, pinned so that changing it fails a check.

Every rule here is one that was got wrong once. `pairs.py` reported conduction x8 as satisfying
`G8` because its crossing median was taken over the hulls that crossed rather than over the hulls
that were loaded, and at x8 that is thirteen of forty — so a cell where two thirds of the
population never overheats read as the best cell in the grid.

The rules these pin are stated canonically in [rules.md](../../docs/rules.md): `E9` `E6`.

    python3 -m unittest discover -s tools/corpus -p 'test_*.py'
"""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import pairs
import scoring


def loaded(*seconds):
    """A cell's worth of loaded runs, one per hull, -1 where the hull never crossed."""
    return [{"seconds_to_critical": str(value)} for value in seconds]


class CrossingIsMeasuredOverTheHullsThatWereLoaded(unittest.TestCase):
    """The defect that made a censored cell read as the grid's best cell."""

    def test_a_hull_that_never_crossed_is_censored_above_not_dropped(self):
        values = pairs.crossings(loaded(10.0, -1.0, 30.0))
        self.assertEqual([10.0, pairs.INFINITE, 30.0], values)

    def test_the_median_is_over_the_population_not_over_the_crossers(self):
        # Four hulls, two of which crossed. The median of the crossers is 20; the median of the
        # population is between 30 and infinity, so there is no median.
        median, crossed, total = pairs.crossing_median(loaded(10.0, 30.0, -1.0, -1.0))
        self.assertIsNone(median)
        self.assertEqual(2, crossed)
        self.assertEqual(4, total)

    def test_a_majority_that_crossed_still_has_a_median_and_it_is_not_the_crossers(self):
        # Five hulls, four of which crossed at 10, 20, 30, 40. The crossers' median is 25; the
        # population's is 30, because the hull that never crossed sits above all of them.
        median, crossed, total = pairs.crossing_median(loaded(10.0, 20.0, 30.0, 40.0, -1.0))
        self.assertEqual(30.0, median)
        self.assertEqual(4, crossed)
        self.assertEqual(5, total)

    def test_a_cell_where_every_hull_crossed_is_the_plain_median(self):
        median, crossed, total = pairs.crossing_median(loaded(10.0, 20.0, 30.0))
        self.assertEqual(20.0, median)
        self.assertEqual(3, crossed)
        self.assertEqual(3, total)

    def test_a_cell_with_no_runs_has_no_median_and_says_so(self):
        self.assertEqual((None, 0, 0), pairs.crossing_median([]))

    def test_exactly_half_the_hulls_crossing_is_censored(self):
        # The boundary case, written down because it is the one an off-by-one gets wrong: with two
        # of four crossed the 50th percentile averages the second and third values, and the third
        # is infinite.
        median, crossed, _ = pairs.crossing_median(loaded(10.0, 20.0, -1.0, -1.0))
        self.assertIsNone(median)
        self.assertEqual(2, crossed)


class TheWindowIsTheOneWrittenDownBeforeTheData(unittest.TestCase):
    """`E1`: the criterion is in balance-lab.md, and the scorer copies it rather than choosing it."""

    def test_the_window_and_the_recovery_bound_are_g8_as_written(self):
        self.assertEqual((120.0, 300.0), pairs.WINDOW)
        self.assertEqual(3600.0, pairs.RECOVERY_BOUND)


class ABreachIsPricedBesideTheVerdictAndTheMarkerDoesNotMove(unittest.TestCase):
    """`G6` fails on the demand; what the demand is worth is reported beside it, not instead of it.

    The criterion's marker was written before the data (`E1`) and stays there (`E11`). What is new
    is that the error it proxies for has been measured, so a reader can see whether a failure is
    three hundredths of a kelvin or a hull being integrated wrong.
    """

    def test_a_demand_inside_the_ceiling_says_nothing(self):
        self.assertEqual("", scoring.oversubscription_note(60.0, 64))
        self.assertEqual("", scoring.oversubscription_note(64.0, 64))
        self.assertEqual("", scoring.oversubscription_note(73.4, 0))

    def test_the_shipped_breach_is_reported_with_its_price(self):
        note = scoring.oversubscription_note(73.4, 64)
        self.assertIn("1.15x", note)
        self.assertIn("0.03 K", note)

    def test_past_the_measured_range_it_says_so_rather_than_extrapolating(self):
        note = scoring.oversubscription_note(200.0, 64)
        self.assertIn("3.12x", note)
        self.assertIn("unpriced", note)



class CensoringBeginsAtABlocksOwnRatingRatherThanAtARoundNumber(unittest.TestCase):
    """`E9`, and the defect that made `C2` a decision about a harness artefact.

    A run with anything past critical keeps generating undamped for the rest of the clock, so its
    peak has stopped being a temperature. Judging that by peak magnitude instead missed most of it:
    9 % of a retest read as censored where 31 % of it was, and in `burn-forward` 9 % against 85 %.
    """

    def test_anything_past_its_rating_censors_the_peak_however_cool_the_hull_looks(self):
        self.assertTrue(scoring.peak_is_censored(1, 1228.0))
        self.assertTrue(scoring.peak_is_censored(1, 420.0))
        self.assertFalse(scoring.peak_is_censored(0, 1228.0))
        self.assertFalse(scoring.peak_is_censored(None, 300.0))

    def test_the_ran_away_line_is_the_stronger_flag_and_not_the_censoring_test(self):
        self.assertTrue(scoring.peak_ran_away(1500.0))
        self.assertTrue(scoring.peak_ran_away(541648.0))
        self.assertFalse(scoring.peak_ran_away(1228.0))
        self.assertFalse(scoring.peak_ran_away(None))

        # The pair a censored-but-not-runaway row makes: this is the 34-of-40 case in burn-forward.
        self.assertTrue(scoring.peak_is_censored(3, 1228.0))
        self.assertFalse(scoring.peak_ran_away(1228.0))


if __name__ == "__main__":
    unittest.main()
