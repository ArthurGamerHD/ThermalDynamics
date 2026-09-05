#!/usr/bin/env python3
"""**What the scorers count and what they drop**, pinned so that changing it fails a check.

Every rule here is one that was got wrong once. `pairs.py` reported conduction x8 as satisfying
`G8` because its crossing median was taken over the hulls that crossed rather than over the hulls
that were loaded, and at x8 that is thirteen of forty — so a cell where two thirds of the
population never overheats read as the best cell in the grid.

The rules these pin are stated canonically in [rules.md](../../docs/rules.md): `E9` `E6`.

    python3 -m unittest discover -s tools/corpus -p 'test_*.py'
"""
import csv
import os
import sys
import shutil
import tempfile
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

    def test_a_step_costs_its_substeps_times_what_one_substep_costs(self):
        # One substep of a grid whose `SubstepCost` the walk recorded as 6,000 -- 1,000 nodes at
        # four visits each and 2,000 links at one.
        self.assertAlmostEqual(6000.0, scoring.step_work(6000, 1), places=3)

        # And it is linear in the substeps, which is what makes it the cost the cap decides.
        self.assertAlmostEqual(60000.0, scoring.step_work(6000, 10), places=3)

    def test_the_cost_comes_from_the_walk_rather_than_being_rebuilt_here(self):
        """The defect this signature exists to make impossible.

        `step_work` took `(nodes, links, substeps)` until 2026-08-24 and every caller handed it the
        `joints` column for `links`. A joint is a rotor or a piston between two grids and there are
        none on most blueprints, so `links + 4 x nodes` evaluated to `4 x nodes` and every published
        step-work figure was the node half alone -- 1.51x low on a 2,000-block census hull. The
        signature now takes the cost the walk recorded from `ThermalSimulation.SubstepCost`, so
        there is no second place for the arithmetic to be got wrong (`P5`).
        """
        # Nothing here can be handed a node count and a link count any more: one number, and it is
        # the mod's own.
        self.assertAlmostEqual(12000.0, scoring.step_work(6000, 2), places=3)

    def test_the_unit_is_the_one_the_allowance_is_denominated_in(self):
        # The weight is `ThermalSettings.NodeCostInLinks`, which is what
        # `ThermalSimulation.SubstepCost` charges and therefore what the allowance is divided by.
        # It was 2.125 until 2026-08-24 -- `ThermalSolverStep.SubstepWork`, the unit a step is
        # *paced* in -- which scored the criterion in one currency against a bound stated in
        # another, 1.45x apart on a census hull.
        self.assertEqual(4.0, scoring.NODE_COST_IN_LINKS)

    def test_a_missing_column_reports_nothing_rather_than_a_figure_built_from_a_zero(self):
        # A walk taken before the `substep_cost` column existed reports no cost at all, which is
        # what makes `G6` read as one half unmeasured rather than as a failure (`E8`, `P2`).
        self.assertIsNone(scoring.step_work(None, 4))
        self.assertIsNone(scoring.step_work(6000, None))

        # A grid with no elements, or a row whose step never ran, is not a free step.
        self.assertIsNone(scoring.step_work(0, 4))
        self.assertIsNone(scoring.step_work(6000, 0))

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



class PerBlockCap(unittest.TestCase):
    """`C3`'s decision rule, which was written down before the walk that produces its number."""

    def test_the_two_thresholds_are_the_ones_the_mod_already_set(self):
        # Neither is invented for this decision. 0.03 K is what `C19` accepted as the price of the
        # substep ceiling's breach; 0.6 K is what made `MaxSubstepsPerBlock` a switch rather than a
        # default in the first place. A rule built from thresholds chosen once the data is in is
        # not a rule (`E1`, `E11`).
        self.assertEqual(0.03, scoring.CAP_ACCEPTED_KELVIN)
        self.assertEqual(0.6, scoring.CAP_REFUSED_KELVIN)

    def test_a_cost_the_mod_already_accepts_ships(self):
        self.assertEqual("ship", scoring.cap_decision(0.0))
        self.assertEqual("ship", scoring.cap_decision(0.028))
        self.assertEqual("ship", scoring.cap_decision(scoring.CAP_ACCEPTED_KELVIN))

    def test_a_cost_the_mod_already_refuses_stays_a_switch(self):
        self.assertEqual("switch", scoring.cap_decision(scoring.CAP_REFUSED_KELVIN))
        self.assertEqual("switch", scoring.cap_decision(0.607))
        self.assertEqual("switch", scoring.cap_decision(12.0))

    def test_between_them_is_an_argument_rather_than_a_threshold(self):
        self.assertEqual("judgement", scoring.cap_decision(0.031))
        self.assertEqual("judgement", scoring.cap_decision(0.3))
        self.assertEqual("judgement", scoring.cap_decision(0.599))

    def test_an_unmeasured_cost_decides_nothing(self):
        self.assertIsNone(scoring.cap_decision(None))

    def test_a_delta_is_absolute_because_a_cap_can_cool_as_well_as_heat(self):
        # The floor raises the capacity of the stiffest elements, which slows a transient in
        # whichever direction it was going -- so a capped hull finishing *below* its control is the
        # same size of error as one finishing above it.
        self.assertAlmostEqual(0.25, scoring.delta_peak(300.0, 300.25), places=6)
        self.assertAlmostEqual(0.25, scoring.delta_peak(300.0, 299.75), places=6)

    def test_a_pair_missing_an_arm_reports_nothing(self):
        self.assertIsNone(scoring.delta_peak(None, 300.0))
        self.assertIsNone(scoring.delta_peak(300.0, None))

class ACriterionNeedsADatasetFineEnoughToStateIt(unittest.TestCase):
    """`resolves` is the guard on a partial walk being read as a population.

    A criterion stated as a share of the corpus needs a corpus that can tell its two sides apart.
    On thirteen ships one ship is 7.7 %, so *nothing critical* and *one per cent critical* are the
    same reading and `G1` has not been answered — it has been asked of a dataset that cannot
    distinguish the answers. The partial survey of 2026-08-25 read `[HOLDS]` on exactly that until
    this existed.
    """

    def test_one_row_finer_than_the_threshold_resolves_it(self):
        self.assertTrue(scoring.resolves(100, 1.0))
        self.assertTrue(scoring.resolves(101, 1.0))
        self.assertTrue(scoring.resolves(5, 20.0))

    def test_one_row_coarser_than_the_threshold_does_not(self):
        self.assertFalse(scoring.resolves(13, 1.0))
        self.assertFalse(scoring.resolves(99, 1.0))
        self.assertFalse(scoring.resolves(4, 20.0))

    def test_an_empty_dataset_resolves_nothing(self):
        self.assertFalse(scoring.resolves(0, 1.0))
        self.assertFalse(scoring.resolves(13, 0.0))

    def test_the_note_says_what_the_dataset_would_need(self):
        note = scoring.too_coarse(13, 1.0, "idle runs")

        self.assertIn("13 idle runs", note)
        self.assertIn("7.7 %", note)
        self.assertIn("It needs 100", note)

    def test_the_note_rounds_the_requirement_up(self):
        # 3 % needs 34 rows, not 33: at 33 one row is 3.03 % and still coarser than the line.
        self.assertIn("It needs 34", scoring.too_coarse(10, 3.0, "runs"))
        self.assertTrue(scoring.resolves(34, 3.0))
        self.assertFalse(scoring.resolves(33, 3.0))


class NoughtAndNothingAreDifferentAnswers(unittest.TestCase):
    """**A cell a dataset does not carry reads as unmeasured, not as zero** (`E8`, `C8`).

    There were twelve copies of this function across the tools and they had already drifted into
    three behaviours: seven returned `None`, three took a `default=0.0`, and `censusdiff.py`
    returned `0.0` outright.

    **The last was a live defect rather than a style difference.** `censusdiff` sums a column over
    the ships two censuses share, so a column one census does not carry read nought on every ship
    and printed as a total — *before 0, after 12,345* reads as a column that grew, when what
    happened is that one census has no such column. Comparing censuses taken on different builds is
    what that tool is for, and columns coming and going between builds is what `C8` is about, so it
    is the case rather than the corner.
    """

    def test_a_missing_column_is_unmeasured(self):
        self.assertIsNone(scoring.number({"a": "1"}, "b"))

    def test_an_unparseable_cell_is_unmeasured(self):
        self.assertIsNone(scoring.number({"a": ""}, "a"))
        self.assertIsNone(scoring.number({"a": "n/a"}, "a"))
        self.assertIsNone(scoring.number({"a": None}, "a"))

    def test_a_real_zero_is_still_a_zero(self):
        """The distinction is only worth anything if nought still reads as nought."""
        self.assertEqual(0.0, scoring.number({"a": "0"}, "a"))
        self.assertEqual(0.0, scoring.number({"a": "0.0"}, "a"))

    def test_a_negative_and_an_exponent_read_as_written(self):
        self.assertEqual(-2.5, scoring.number({"a": "-2.5"}, "a"))
        self.assertEqual(1200.0, scoring.number({"a": "1.2e3"}, "a"))

    def test_write_summary_round_trips_through_the_reader_verdict_uses(self):
        """The summary page format, pinned from both sides of its contract.

        Seven tools write this page and `verdict.py --baseline` reads it back with a
        DictReader keyed on these three column names — so the pin is a round trip, not a
        header string: a page written here must come back as the figures that went in.
        """
        root = tempfile.mkdtemp()
        try:
            path = os.path.join(root, "summary.csv")
            scoring.write_summary(path, [("ships", 8142, "blueprints"), ("share", 0.75, "")])
            with open(path) as handle:
                rows = list(csv.DictReader(handle))
            self.assertEqual(
                [{"statistic": "ships", "value": "8142", "unit": "blueprints"},
                 {"statistic": "share", "value": "0.75", "unit": ""}], rows)
        finally:
            shutil.rmtree(root)

    def test_load_reads_rows_and_reads_a_missing_page_as_empty(self):
        """The shared loader: rows as dicts, and no file is an empty list, not an error.

        Three tools carried this body; the empty list is for a dataset that legitimately
        lacks a page, and each tool still prints its own guidance when the page it cannot
        run without is the empty one.
        """
        root = tempfile.mkdtemp()
        try:
            with open(os.path.join(root, "page.csv"), "w") as handle:
                handle.write("a,b\n1,2\n")
            self.assertEqual([{"a": "1", "b": "2"}], scoring.load(root, "page"))
            self.assertEqual([], scoring.load(root, "absent"))
        finally:
            shutil.rmtree(root)

    def test_number_or_defaults_only_where_number_is_unmeasured(self):
        """The caller's default answers exactly the cells `number` calls unmeasured — no more.

        A measured nought must come back as the measurement, not the default, or the
        distinction the class above defends is silently lost one wrapper out.
        """
        self.assertEqual(7.0, scoring.number_or({"a": "1"}, "b", 7.0))
        self.assertEqual(7.0, scoring.number_or({"a": ""}, "a", 7.0))
        self.assertEqual(0.0, scoring.number_or({"a": "0"}, "a", 7.0))
        self.assertEqual(-2.5, scoring.number_or({"a": "-2.5"}, "a", 7.0))



class ARowIsNamedByAShipAndAnId(unittest.TestCase):
    """**A ship is a name *and* a workshop id, and an arm where the dataset has one.**

    Both halves have failed once. Keyed by id alone, the fourteen ships that shared `workshop_id` 0
    through a workshop *collection* were one ship. Keyed without the arm, `verdict.py` called half
    of a paired walk's rows duplicates — *912 duplicate rows* on the 2026-08-25 cap dataset — and
    scored `G6` on whichever arm happened to come first.
    """

    def test_two_ships_sharing_an_id_are_two_rows(self):
        a = {"ship": "Raptor", "workshop_id": "0"}
        b = {"ship": "Ghost", "workshop_id": "0"}
        self.assertNotEqual(scoring.key_of(a), scoring.key_of(b))

    def test_two_ships_sharing_a_name_are_two_rows(self):
        a = {"ship": "Drone", "workshop_id": "1"}
        b = {"ship": "Drone", "workshop_id": "2"}
        self.assertNotEqual(scoring.key_of(a), scoring.key_of(b))

    def test_the_arm_is_part_of_the_identity_when_asked_for(self):
        control = {"ship": "Drone", "workshop_id": "1", "cap": "0"}
        capped = {"ship": "Drone", "workshop_id": "1", "cap": "6"}

        self.assertEqual(scoring.key_of(control), scoring.key_of(capped))
        self.assertNotEqual(scoring.key_of(control, "cap"), scoring.key_of(capped, "cap"))

    def test_a_column_a_row_does_not_have_reads_as_absent_rather_than_throwing(self):
        self.assertEqual((None, None), scoring.key_of({}))



if __name__ == "__main__":
    unittest.main()


class APairedWalkIsScoredOnTheArmThatShips(unittest.TestCase):
    """A paired dataset carries two configurations, and every criterion is about one of them.

    The defect this pins was found on a dry run before the data landed: `verdict.py` keyed a row by
    ship and scenario, so the capped arm read as *912 duplicate rows* and half the dataset was
    dropped with a note about a resume that had not happened. It kept the right arm by accident.
    """

    @staticmethod
    def arm(cap, peak):
        return {"ship": "a", "workshop_id": "1", "scenario": "reentry",
                "cap": cap, "peak_k": str(peak)}

    def test_an_ordinary_walk_is_not_split(self):
        rows = [self.arm("0", 300), self.arm("0", 310)]
        arms, shipped = scoring.split_arms(rows)
        self.assertEqual([], arms)
        self.assertEqual(rows, shipped)

    def test_a_walk_with_no_cap_column_at_all_is_not_split(self):
        rows = [{"ship": "a", "scenario": "reentry"}]
        arms, shipped = scoring.split_arms(rows)
        self.assertEqual([], arms)
        self.assertEqual(rows, shipped)

    def test_a_paired_walk_keeps_the_shipped_arm_and_names_both(self):
        rows = [self.arm("0", 300), self.arm("6", 290)]
        arms, shipped = scoring.split_arms(rows)
        self.assertEqual(["0", "6"], arms)
        self.assertEqual([rows[0]], shipped)

    def test_the_two_arms_of_one_run_are_not_duplicates_of_each_other(self):
        # The failure itself: keyed without the arm, these are one row seen twice.
        rows = [self.arm("0", 300), self.arm("6", 290)]
        self.assertNotEqual(rows[0]["cap"], rows[1]["cap"])
        self.assertEqual(rows[0]["scenario"], rows[1]["scenario"])


class APartialDatasetIsNamedAsPartial(unittest.TestCase):
    """`E4` — *a corpus run is quoted whole, or quoted with the words "partial" and the count
    attached* — had nothing checking it until 2026-08-26. The failure it is about is not a small
    population: the corpus is walked largest-first, so a killed walk holds the capital ships and
    reads 95 % where the truth is 75 %."""

    def test_a_finished_walk_is_whole_and_a_third_of_one_is_not(self):
        # The 2026-08-21 survey against the corpus it walked, and the 2026-08-25 survey at the
        # point `E4` would have been quoted from it.
        self.assertGreaterEqual(scoring.walked_share(8132, 8144), scoring.WHOLE_ENOUGH)
        self.assertLess(scoring.walked_share(2683, 8144), scoring.WHOLE_ENOUGH)

    def test_the_filters_rejections_do_not_make_a_finished_walk_partial(self):
        """A walk that lost one ship in a hundred is finished. The threshold has to sit between
        that and an interruption, and this is what says it does."""
        self.assertGreaterEqual(scoring.walked_share(8062, 8144), scoring.WHOLE_ENOUGH)

    def test_an_unknown_population_is_not_a_whole_one(self):
        """A machine with no corpus can still read a dataset, and answering *is this partial* with
        silence there is the failure the rule is about (`P2`)."""
        self.assertIsNone(scoring.walked_share(8132, None))
        self.assertIsNone(scoring.walked_share(8132, 0))

    def test_a_stated_population_overrides_the_count(self):
        self.assertEqual(1234, scoring.corpus_population("1234"))
        self.assertEqual(1234, scoring.corpus_population(1234))

    def test_a_population_that_cannot_be_read_is_none_rather_than_zero(self):
        self.assertIsNone(scoring.corpus_population("not a number"))

        keep = scoring.CORPUS
        try:
            scoring.CORPUS = os.path.join(tempfile.gettempdir(), "no-corpus-here-" + os.urandom(8).hex())
            self.assertIsNone(scoring.corpus_population())
        finally:
            scoring.CORPUS = keep

    def test_a_collection_of_blueprints_under_one_workshop_id_is_counted(self):
        """**One workshop item can hold several blueprints**, in named folders under its id. Counting
        one level deep misses fourteen of this corpus's and reports a population *smaller than the
        walk that covered it*, which reads as a dataset more than whole."""
        root = tempfile.mkdtemp()
        keep = scoring.CORPUS
        try:
            os.makedirs(os.path.join(root, "111"))
            open(os.path.join(root, "111", "bp.sbc"), "w").close()

            for name in ("Ghost Mk.III", "Ghost Mk.IV"):
                os.makedirs(os.path.join(root, "222", name))
                open(os.path.join(root, "222", name, "bp.sbc"), "w").close()

            os.makedirs(os.path.join(root, "333"))       # an item with no blueprint in it

            scoring.CORPUS = root
            self.assertEqual(3, scoring.corpus_population())
        finally:
            scoring.CORPUS = keep
            shutil.rmtree(root, ignore_errors=True)
