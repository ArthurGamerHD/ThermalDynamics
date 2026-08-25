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



class ACensoredMedianHasACliffAndTheBootstrapIsWhereItIs(unittest.TestCase):
    """`C12`: two cells with the same median can be half as likely to hold on another fleet.

    The censoring rule says a median in the tail is *never*. What it cannot say is how close a cell
    is to that boundary, and the difference matters: the load grid's waste route puts 55 % of hulls
    past critical and the conduction route 62.5 %, both above a half and both scoring a median in
    the window — and resampled, one holds on 46 % of fleets and the other on 76 %.
    """

    def test_a_fleet_that_all_crosses_inside_the_window_always_holds(self):
        self.assertEqual(1.0, scoring.median_stability(
            [200.0] * 40, scoring.in_window(120.0, 300.0)))

    def test_a_fleet_that_never_crosses_never_holds(self):
        self.assertEqual(0.0, scoring.median_stability(
            [scoring.INFINITE] * 40, scoring.in_window(120.0, 300.0)))

    def test_a_median_in_the_window_can_still_be_a_coin_flip(self):
        # Twenty-two of forty crossed, all of them inside the window, and the rest never. The
        # median is in the window on the measured fleet; a resample loses it whenever fewer than
        # half the draws are crossers, which is close to half the time.
        series = [200.0] * 22 + [scoring.INFINITE] * 18
        held = scoring.median_stability(series, scoring.in_window(120.0, 300.0))

        self.assertEqual(200.0, scoring.censored_median(series))
        self.assertTrue(0.4 < held < 0.85,
                        "a cell two hulls clear of the cliff held on %.0f %% of fleets" % (100 * held))

    def test_the_same_median_further_from_the_cliff_holds_far_more_often(self):
        near = [200.0] * 22 + [scoring.INFINITE] * 18
        clear = [200.0] * 30 + [scoring.INFINITE] * 10

        self.assertEqual(scoring.censored_median(near), scoring.censored_median(clear))
        self.assertGreater(scoring.median_stability(clear, scoring.in_window(120.0, 300.0)),
                           scoring.median_stability(near, scoring.in_window(120.0, 300.0)) + 0.2)

    def test_a_recovery_bound_reads_never_as_over_the_bound(self):
        self.assertEqual(0.0, scoring.median_stability(
            [scoring.INFINITE] * 20, scoring.within(3600.0)))
        self.assertEqual(1.0, scoring.median_stability(
            [1000.0] * 20, scoring.within(3600.0)))

    def test_the_estimate_is_the_same_on_two_runs(self):
        series = [200.0] * 22 + [scoring.INFINITE] * 18
        self.assertEqual(scoring.median_stability(series, scoring.in_window(120.0, 300.0)),
                         scoring.median_stability(series, scoring.in_window(120.0, 300.0)))

    def test_the_joint_estimate_is_not_the_product_of_the_marginals(self):
        # Two halves that fail on the *same* hulls: every fleet either holds both or neither, so
        # the joint is the marginal rather than its square. Multiplying would have said 25 %.
        crossing = [200.0] * 20 + [scoring.INFINITE] * 20
        recovery = [1000.0] * 20 + [scoring.INFINITE] * 20

        a = scoring.median_stability(crossing, scoring.in_window(120.0, 300.0))
        both = scoring.joint_median_stability([
            (crossing, scoring.in_window(120.0, 300.0)),
            (recovery, scoring.within(3600.0)),
        ])

        self.assertAlmostEqual(a, both, places=6)

    def test_a_half_that_always_holds_does_not_change_the_joint(self):
        crossing = [200.0] * 22 + [scoring.INFINITE] * 18
        recovery = [1000.0] * 40

        self.assertAlmostEqual(
            scoring.median_stability(crossing, scoring.in_window(120.0, 300.0)),
            scoring.joint_median_stability([
                (crossing, scoring.in_window(120.0, 300.0)),
                (recovery, scoring.within(3600.0)),
            ]), places=6)

    def test_series_of_different_lengths_are_refused_rather_than_zipped(self):
        with self.assertRaises(ValueError):
            scoring.joint_median_stability([
                ([1.0, 2.0], scoring.within(3.0)),
                ([1.0], scoring.within(3.0)),
            ])

    def test_an_infinity_in_the_middle_stays_infinite_rather_than_averaging_away(self):
        # The even-length boundary: two crossers and two not is not a median of "somewhere past
        # the second one", it is no median at all.
        self.assertEqual(scoring.INFINITE, scoring.censored_median([10.0, 20.0, scoring.INFINITE, scoring.INFINITE]))


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


class StepWork(unittest.TestCase):
    """`G6`'s cost half: work in the solver's own unit, against the allowance the mod ships."""

    def test_a_step_costs_its_substeps_times_its_elements(self):
        # One substep over 1,000 nodes and 2,000 links: 4,000 node visits and 2,000 link visits.
        self.assertAlmostEqual(6000.0, scoring.step_work(1000, 2000, 1), places=3)

        # And it is linear in the substeps, which is what makes it the cost the cap decides.
        self.assertAlmostEqual(60000.0, scoring.step_work(1000, 2000, 10), places=3)

    def test_the_unit_is_the_one_the_allowance_is_denominated_in(self):
        # The weight is `ThermalSettings.NodeCostInLinks`, which is what
        # `ThermalSimulation.SubstepCost` charges and therefore what the allowance is divided by.
        # It was 2.125 until 2026-08-24 -- `ThermalSolverStep.SubstepWork`, the unit a step is
        # *paced* in -- which scored the criterion in one currency against a bound stated in
        # another, 1.45x apart on a census hull.
        self.assertEqual(4.0, scoring.NODE_COST_IN_LINKS)

    def test_a_missing_column_reports_nothing_rather_than_a_figure_built_from_a_zero(self):
        self.assertIsNone(scoring.step_work(None, 2000, 4))
        self.assertIsNone(scoring.step_work(1000, None, 4))
        self.assertIsNone(scoring.step_work(1000, 2000, None))

        # A walk that recorded no blocks, or a row whose step never ran, is not a free step.
        self.assertIsNone(scoring.step_work(0, 2000, 4))
        self.assertIsNone(scoring.step_work(1000, 2000, 0))

    def test_a_grid_keeps_real_time_until_its_step_passes_the_allowance(self):
        allowance = scoring.SHIPPED_VISIT_ALLOWANCE

        self.assertTrue(scoring.keeps_up(allowance - 1))
        self.assertTrue(scoring.keeps_up(allowance))
        self.assertFalse(scoring.keeps_up(allowance + 1))

        # Nothing measured is not something that keeps up (`E8`).
        self.assertFalse(scoring.keeps_up(None))

    def test_the_allowance_is_the_shipped_one(self):
        # Pinned against the default in ThermalSettings rather than left as a number here: the
        # bound is the mod's own statement of what a step may cost, and if that moves this
        # criterion moves with it rather than describing a configuration nobody runs.
        self.assertEqual(4000000.0, scoring.SHIPPED_VISIT_ALLOWANCE)

    def test_the_substep_cap_is_the_shipped_one_and_not_the_datasets_own_maximum(self):
        # `MaxSubsteps` in ThermalSettings. verdict.py used max(substeps_granted) until
        # 2026-08-24, which is ceil(max demand) on any population nothing clamps -- so the demand
        # half of G6 was comparing a population against itself and could not fail.
        self.assertEqual(64.0, scoring.SHIPPED_SUBSTEP_CAP)


class Percentiles(unittest.TestCase):
    """One definition, because there were two and the documentation prints them side by side."""

    def test_a_quantile_is_interpolated_between_the_ranks_it_falls_between(self):
        self.assertAlmostEqual(2.5, scoring.percentile([1, 2, 3, 4], 0.5), places=6)
        self.assertAlmostEqual(1.0, scoring.percentile([1, 2, 3, 4], 0.0), places=6)
        self.assertAlmostEqual(4.0, scoring.percentile([1, 2, 3, 4], 1.0), places=6)

    def test_a_p99_of_forty_readings_is_not_the_largest_of_them(self):
        # The whole reason this moved. verdict.py took `values[int(q * n)]`, and `int(0.99 * 40)`
        # is 39 -- the fortieth of forty, which is the maximum. A p99 that is the largest reading
        # in the set is not a percentile, and a forty-hull panel is a set this repository scores.
        forty = list(range(1, 41))

        self.assertEqual(40, max(forty))
        self.assertLess(scoring.percentile(forty, 0.99), 40)
        self.assertAlmostEqual(39.61, scoring.percentile(forty, 0.99), places=2)

    def test_nothing_measured_has_no_percentile(self):
        self.assertIsNone(scoring.percentile([], 0.5))
        self.assertEqual({}, scoring.percentiles([]))

    def test_one_reading_is_its_own_every_percentile(self):
        self.assertEqual(7, scoring.percentile([7], 0.99))
        row = scoring.percentiles([7])
        self.assertEqual(7, row["min"])
        self.assertEqual(7, row["p99"])
        self.assertEqual(7, row["max"])


class Compare(unittest.TestCase):
    """Reading one run against another, which is how a walk in air is read against one in vacuum."""

    def test_only_what_moved_is_reported(self):
        rows = scoring.compare({"a": "1", "b": "2"}, {"a": "1", "b": "3"})
        self.assertEqual(1, len(rows))
        self.assertEqual("b", rows[0][0])

    def test_a_statistic_on_one_side_only_is_a_row_rather_than_a_drop(self):
        # The whole reason this is a union: a scenario the other dataset does not carry is the
        # finding, and an inner join is exactly what hides it.
        rows = dict((r[0], r) for r in scoring.compare({"gone": "5"}, {"new": "7"}))

        self.assertEqual(scoring.ABSENT, rows["gone"][2])
        self.assertEqual(scoring.ABSENT, rows["new"][1])

        # Absent is not nought, so neither carries a percentage.
        self.assertEqual("", rows["gone"][3])
        self.assertEqual("", rows["new"][3])

    def test_a_percentage_needs_two_numbers_and_a_baseline_to_divide_by(self):
        self.assertEqual("+100.0%", scoring.compare({"a": "2"}, {"a": "4"})[0][3])
        self.assertEqual("-50.0%", scoring.compare({"a": "2"}, {"a": "1"})[0][3])

        # A verdict is a word, and nought is not something to divide by.
        self.assertEqual("", scoring.compare({"a": "holds"}, {"a": "fails"})[0][3])
        self.assertEqual("", scoring.compare({"a": "0"}, {"a": "3"})[0][3])


if __name__ == "__main__":
    unittest.main()
