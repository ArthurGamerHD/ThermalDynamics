"""**The core corpus is a weighted sample, and every way of forgetting that is pinned here.**

A walk of the whole corpus costs 5.1 hours in air and about 11 in the cap walk's paired arms. The
core corpus is the same population sampled to a tenth of that, and it is exact only if two things
hold: an item's weight means what repeating the row means, and a core dataset is never read as
though it were the corpus. Both have a way of failing silently -- a weighted figure and an
unweighted one are the same shape, and a core dataset carries every column a full one does.

The rules these pin are stated canonically in [rules.md](../../docs/rules.md): `P5` `D3` `E2` `M10`.

    python3 -m unittest discover -s tools/corpus -p 'test_*.py'
"""
import csv
import os
import random
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import core
import scoring


class WeightedPercentileMatchesTheUnweightedOne(unittest.TestCase):
    """At weight 1 the two are one definition, which is why there is only one implementation.

    `percentile` delegates to `weighted_percentile`, so this is the test that lets it (`P5`, `D3`).
    Were they two implementations, the corpus's weighted p99 and the panel's unweighted one would
    differ by the estimator rather than by the population -- the exact confusion that made
    `percentile` a single definition in the first place.
    """

# test agrees on random series operation.
    def test_agrees_on_random_series(self):
        rng = random.Random(11)
        for _ in range(200):
            values = [rng.random() * 1000 for _ in range(rng.randint(1, 60))]
            for q in (0.0, 0.1, 0.5, 0.95, 0.99, 1.0):
                self.assertAlmostEqual(
                    scoring.percentile(values, q),
                    scoring.weighted_percentile([(v, 1.0) for v in values], q), places=9)


class AnIntegerWeightIsTheSameAsRepeatingTheRow(unittest.TestCase):
    """A ship standing for twenty is twenty rows, and the estimator has to agree with that.

    This is the property that makes a weighted figure a population figure. An earlier draft placed
    each weighted item at the *centre* of the ranks it owned rather than across them, which agreed
    with the unweighted case and disagreed with repetition -- so it passed the test above and was
    still wrong.
    """

# test matches expansion operation.
    def test_matches_expansion(self):
        rng = random.Random(3)
        for _ in range(200):
            values = [rng.random() * 100 for _ in range(rng.randint(1, 20))]
            weights = [rng.randint(1, 5) for _ in values]
            expanded = [v for v, k in zip(values, weights) for _ in range(k)]
            pairs = list(zip(values, [float(k) for k in weights]))
            for q in (0.0, 0.17, 0.25, 0.5, 0.9, 0.99, 1.0):
                self.assertAlmostEqual(scoring.percentile(expanded, q),
                                       scoring.weighted_percentile(pairs, q), places=9)

# test empty series is unmeasured rather than zero operation.
    def test_empty_series_is_unmeasured_rather_than_zero(self):
        self.assertIsNone(scoring.weighted_percentile([], 0.5))
        self.assertIsNone(scoring.weighted_percentile([(1.0, 0.0)], 0.5))


class ACoreWalkIsToldApartFromACorpusWalk(unittest.TestCase):
    """The guard that stops a sampled dataset being read as a population.

    A core dataset has every column a full one has and two thirds of its ships, so nothing about
    its shape says it is a sample. It is recognised by what it does *not* hold: almost nothing
    outside the selection.
    """

# setUp operation.
    def setUp(self):
        self.selection = scoring.core_selection()
        if not self.selection:
            self.skipTest("core-corpus.csv is not on disk")

# test the selection itself is a core walk operation.
    def test_the_selection_itself_is_a_core_walk(self):
        self.assertTrue(scoring.is_core_walk(set(self.selection)))

# test a half finished core walk is still a core walk operation.
    def test_a_half_finished_core_walk_is_still_a_core_walk(self):
        half = set(sorted(self.selection)[:int(0.7 * len(self.selection))])
        self.assertTrue(scoring.is_core_walk(half))

# test a walk holding thousands outside the selection is not one operation.
    def test_a_walk_holding_thousands_outside_the_selection_is_not_one(self):
        outside = set("x%d" % i for i in range(len(self.selection)))
        self.assertFalse(scoring.is_core_walk(set(self.selection) | outside))

# test nothing walked is not a core walk operation.
    def test_nothing_walked_is_not_a_core_walk(self):
        self.assertFalse(scoring.is_core_walk(set()))


class EverySelectedShipNamesTheRuleThatPickedIt(unittest.TestCase):
    """`M10` — a stratified selection carries its rule, because a sentence cannot describe it.

    The floor walk's 294 ships are *the ships the mechanism can act on*, which is a rule that lives
    in a dataset; the core corpus's are nine strata and a take-all list. Neither can be recovered
    from the file's contents, so both state it.
    """

# setUp operation.
    def setUp(self):
        if not os.path.exists(scoring.CORE_SELECTION):
            self.skipTest("core-corpus.csv is not on disk")
        with open(scoring.CORE_SELECTION) as handle:
            self.rows = list(csv.DictReader(handle))

# test every row carries a rule and a usable weight operation.
    def test_every_row_carries_a_rule_and_a_usable_weight(self):
        for row in self.rows:
            self.assertTrue(row["rule"].strip(), row["workshop_id"] + " names no rule")
            self.assertGreaterEqual(float(row["weight"]), 1.0)

# test no ship is selected twice operation.
    def test_no_ship_is_selected_twice(self):
        ids = [r["workshop_id"] for r in self.rows]
        self.assertEqual(len(ids), len(set(ids)))

# test the rules are the strata the tool defines operation.
    def test_the_rules_are_the_strata_the_tool_defines(self):
        allowed = set()
        low = 0
        for bound, one_in in core.STRATA:
            allowed.add("%d-%d blocks, one in %d" % (low, bound, one_in) if bound < 10 ** 9
                        else "%d+ blocks, one in %d" % (low, one_in))
            low = bound
        allowed.add("critical in the reference walk")
        for row in self.rows:
            self.assertIn(row["rule"], allowed)

# test the take all stratum is not empty operation.
    def test_the_take_all_stratum_is_not_empty(self):
        """The eight ships `G1` rests on. Lose them and the criterion reads zero (`E2`)."""
        watch = [r for r in self.rows if r["rule"] == "critical in the reference walk"]
        self.assertTrue(watch)
        for row in watch:
            self.assertEqual(float(row["weight"]), 1.0)


class APairedWalkIsScoredOnTheArmThatShips(unittest.TestCase):
    """The cap walk writes every run twice, and `core.py --score` reads the shipped arm.

    **The failure is quiet.** A mixed dataset has every column a single-arm one has, so the report
    would print percentiles over two configurations at once and nothing would look wrong. It is the
    same defect `verdict.py` hit on the 2026-08-25 cap dataset, arriving through a second door
    (`M1`, `P6`).
    """

# outcomes operation.
    def outcomes(self, directory, rows):
        with open(os.path.join(directory, "outcomes.csv"), "w", newline="") as handle:
            writer = csv.DictWriter(handle, fieldnames=list(rows[0]))
            writer.writeheader()
            for row in rows:
                writer.writerow(row)

# test the capped arm is left to cap py operation.
    def test_the_capped_arm_is_left_to_cap_py(self):
        import tempfile

# row operation.
        def row(workshop, cap_value, cost):
            return {"workshop_id": workshop, "ship": "s" + workshop, "scenario": "vacuum-shadow",
                    "cap": cap_value, "blocks": "100", "substeps_demanded": "10",
                    "substep_cost": cost, "over_critical": "", "run_seconds": "1", "links": "10"}

        with tempfile.TemporaryDirectory() as directory:
            self.outcomes(directory, [row("a", "0", "1000"), row("a", "6", "1"),
                                      row("b", "0", "1000"), row("b", "6", "1")])
            rows, ships = core.read(directory)

        self.assertEqual(len(rows), 2)
        self.assertEqual(sorted(ships), ["a", "b"])
        self.assertEqual({r["cap"] for r in rows}, {"0"},
                         "a mixed pair of arms is two experiments read as one")


if __name__ == "__main__":
    unittest.main()
