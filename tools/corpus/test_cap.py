"""**The cap report reads a sampled walk with weights, and every way of forgetting that is here.**

`C3` is decided on the paired cap walk, and after `A13` that walk has to be re-taken on a corpus
whose blocks are what the game says they are. The whole corpus costs about eleven hours in paired
arms; the core corpus costs 78 minutes and is a **weighted sample**, so `cap.py` has to do three
things it did not have to do when every dataset was the population:

* recognise a core dataset without being told, because a flag is a thing a reader forgets and the
  failure is silent -- a core walk read unweighted prints a complete-looking population whose
  giants are outnumbered twenty to one (`P1`, `E2`);
* weight every figure that is a statement about the population, and **not** weight the ones that
  are statements about the walk -- the identity check is an assertion that the capped arm did what
  a cap does, and every pair walked is one observation of it;
* refuse to score the halves a sample cannot answer. A maximum is one observation: the core corpus
  reads 76 % under the population's `work max`, so the cost prediction's *max under 10 K* half is
  unscored on a sampled walk rather than passed (`E8`).

The rules these pin are stated canonically in [rules.md](../../docs/rules.md): `P5` `E2` `E8` `M10`.

    python3 -m unittest discover -s tools/corpus -p 'test_*.py'
"""
import io
import os
import sys
import unittest
import unittest.mock

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import cap
import scoring


# row operation.
def row(ship, workshop, scenario, cap_value, **columns):
    """One outcome row, with the columns the cap report reads and defaults for the rest."""
    out = {
        "ship": ship,
        "workshop_id": workshop,
        "scenario": scenario,
        "cap": str(cap_value),
        "substeps_demanded": "10",
        "substep_cost": "1000",
        "peak_k": "300",
        "peak_rate_k_per_s": "0",
        "floored": "0",
        "blocks": "100",
    }
    out.update({key: str(value) for key, value in columns.items()})
    return out


# paired operation.
def paired(workshop, scenario="vacuum-shadow", demand=10.0, peak_off=300.0, peak_on=300.0,
           cost=1000.0, floored=0.0, blocks=100.0):
    """A control arm and its capped partner, which is the unit every figure here is taken over."""
    return [
        row("ship-" + workshop, workshop, scenario, cap.OFF, substeps_demanded=demand,
            substep_cost=cost, peak_k=peak_off, blocks=blocks),
        row("ship-" + workshop, workshop, scenario, 6, substeps_demanded=min(demand, 6.0),
            substep_cost=cost, peak_k=peak_on, floored=floored, blocks=blocks),
    ]


class ASampledWalkIsRecognisedRatherThanDeclared(unittest.TestCase):
    """The dataset says what it is by its contents; nothing has to be passed on the command line.

    The failure this prevents is the quiet one: a core dataset has every column a full one has, so
    a reader who forgets which directory came from which walk gets a population-shaped report about
    a population that does not exist.
    """

# setUp operation.
    def setUp(self):
        self.selection = list(scoring.core_selection())
        self.assertTrue(self.selection, "the committed core selection is missing")

# test a walk of the selection carries the selection weights operation.
    def test_a_walk_of_the_selection_carries_the_selection_weights(self):
        rows = []
        for workshop in self.selection:
            rows.extend(paired(workshop))
        weights, core = cap.weights(rows)

        self.assertTrue(core)
        self.assertEqual(len(weights), len(self.selection))
        self.assertGreater(max(weights.values()), 1.0,
                           "a core walk whose weights are all 1 is a core walk read unweighted")

# test a full corpus walk is unweighted operation.
    def test_a_full_corpus_walk_is_unweighted(self):
        rows = []
        for workshop in self.selection:
            rows.extend(paired(workshop))
        for extra in range(len(self.selection)):
            rows.extend(paired("outside-%d" % extra))

        weights, core = cap.weights(rows)
        self.assertFalse(core)
        self.assertEqual(set(weights.values()), {1.0})

# test a ship outside the selection is a stray rather than a default operation.
    def test_a_ship_outside_the_selection_is_a_stray_rather_than_a_default(self):
        """A weight of 1 invented for an unknown ship is the silent wrong answer (`E4`)."""
        rows = []
        for workshop in self.selection:
            rows.extend(paired(workshop))
        rows.extend(paired("not-in-the-selection"))

        weights, core = cap.weights(rows)
        self.assertTrue(core)
        self.assertNotIn("not-in-the-selection", weights)

        pairs, _, _, strays = cap.pair(rows, weights)
        self.assertEqual(strays, 2)
        self.assertEqual(len(pairs), len(self.selection))


class AWeightIsCarriedIntoEveryPopulationFigure(unittest.TestCase):
    """A weighted figure over the sample equals the unweighted figure over what it stands for.

    This is the property that makes the core corpus an estimate of the corpus rather than a
    description of itself, and it is checked end to end through `cap.pair` rather than on the
    percentile helper alone -- `test_core.py` already pins the helper.
    """

# test a giant standing for twenty moves the percentile like twenty operation.
    def test_a_giant_standing_for_twenty_moves_the_percentile_like_twenty(self):
        heavy = [(9.0e6, 20.0)] + [(1.0e5, 1.0)] * 80
        expanded = [9.0e6] * 20 + [1.0e5] * 80

        for q in (0.5, 0.9, 0.95, 0.99):
            self.assertAlmostEqual(scoring.weighted_percentile(heavy, q),
                                   scoring.percentile(expanded, q), places=6)

# test the reach share counts a sampled giant for what it stands for operation.
    def test_the_reach_share_counts_a_sampled_giant_for_what_it_stands_for(self):
        """The reach is a share of node-runs, which is the one figure a sample estimates cleanly."""
        weights = {"small": 1.0, "giant": 20.0}
        rows = paired("small", scenario="reentry", floored=0.0, blocks=100.0) + \
            paired("giant", scenario="reentry", floored=50.0, blocks=100.0)

        pairs, _, _, _ = cap.pair(rows, weights)
        floored = sum(scoring.number(capped, "floored") * weight for _, _, capped, weight in pairs)
        blocks = sum(scoring.number(capped, "blocks") * weight for _, _, capped, weight in pairs)

        self.assertAlmostEqual(100.0 * floored / blocks, 100.0 * 1000.0 / 2100.0, places=6)


class AWalkCheckIsNotWeighted(unittest.TestCase):
    """The identity is an assertion about the walk, and weighting it would change the sentence.

    *Did the capped arm cap* is answered by every pair the walk ran. Weighted, the same count would
    read as *how much of the population was checked*, which nobody asked and which would hide a
    broken pair on a ship of weight 1 behind twenty good ones on a ship of weight 20.
    """

# test a single broken pair is reported whatever its weight operation.
    def test_a_single_broken_pair_is_reported_whatever_its_weight(self):
        rows = paired("light", demand=10.0) + paired("heavy", demand=10.0)
        rows[3]["substeps_demanded"] = "10"

        pairs, _, _, _ = cap.pair(rows, {"light": 1.0, "heavy": 20.0})
        broken = sum(1 for _, control, capped, _ in pairs
                     if abs(scoring.number(capped, "substeps_demanded")
                            - min(scoring.number(control, "substeps_demanded"), 6.0)) > 0.01)
        self.assertEqual(broken, 1)


class WhatASampleCannotAnswerIsUnscored(unittest.TestCase):
    """`?` rather than `HOLDS`, because a pass a dataset cannot support is worse than no reading.

    The cost prediction is a p99 under 1 K **and** a max under 10 K. The core corpus keeps one
    giant in twenty and its largest delta is the largest of the sample, so on a sampled walk the
    max half has no reading and the verdict says so (`E8`).
    """

# run report operation.
    def run_report(self, rows):
        out = io.StringIO()
        with unittest.mock.patch.object(cap, "load", lambda path: rows), \
                unittest.mock.patch.object(sys, "stdout", out):
            cap.main()
        return out.getvalue()

# test a core walk leaves the cost verdict unscored when the p99 holds operation.
    def test_a_core_walk_leaves_the_cost_verdict_unscored_when_the_p99_holds(self):
        rows = []
        for workshop in scoring.core_selection():
            rows.extend(paired(workshop, peak_off=300.0, peak_on=300.001))
        report = self.run_report(rows)

        self.assertIn("CORE CORPUS", report)
        self.assertIn("[  ?  ] the cost", report)
        self.assertIn("a sampled walk cannot score", report)

# test a core walk still fails the cost verdict on the half it can score operation.
    def test_a_core_walk_still_fails_the_cost_verdict_on_the_half_it_can_score(self):
        """An unscorable half never rescues a failing one: a p99 over 1 K is a failure outright."""
        rows = []
        for index, workshop in enumerate(sorted(scoring.core_selection())):
            apart = 50.0 if index % 2 else 0.0
            rows.extend(paired(workshop, peak_off=300.0, peak_on=300.0 + apart))
        report = self.run_report(rows)

        self.assertIn("[FAILS] the cost", report)

# test a full walk scores both halves operation.
    def test_a_full_walk_scores_both_halves(self):
        rows = []
        for workshop in scoring.core_selection():
            rows.extend(paired(workshop, peak_off=300.0, peak_on=300.001))
        for extra in range(len(scoring.core_selection())):
            rows.extend(paired("outside-%d" % extra, peak_off=300.0, peak_on=300.001))

        report = self.run_report(rows)
        self.assertNotIn("CORE CORPUS", report)
        self.assertIn("[HOLDS] the cost", report)
        self.assertIn("against the predicted max under", report)


class ASummaryIsWhatMakesAFigureQuotable(unittest.TestCase):
    """A page quoting a cap figure has to be able to check it against the walk (`E5`).

    The failure this prevents is the one `A13`'s air half hit: a prediction scored against a number
    that lived in a paragraph, from a walk whose dataset could not be re-read to recover it. A
    summary that names its own population is also what stops a core figure being compared with a
    full-walk figure as though they were the same reading.
    """

# summary operation.
    def summary(self, rows, directory):
        cap.FIGURES[:] = []
        out = io.StringIO()
        with unittest.mock.patch.object(cap, "load", lambda path: rows), \
                unittest.mock.patch.object(cap, "DATA", directory), \
                unittest.mock.patch.object(sys, "stdout", out):
            cap.main()
        return {statistic: value for statistic, value, _ in cap.FIGURES}

# test a core summary names its figures weighted and its max a sample operation.
    def test_a_core_summary_names_its_figures_weighted_and_its_max_a_sample(self):
        rows = []
        for workshop in scoring.core_selection():
            rows.extend(paired(workshop))
        figures = self.summary(rows, "out/does-not-exist")

        self.assertEqual(figures["dataset core corpus"], 1)
        self.assertIn("C3 weighted dpeak p99", figures)
        self.assertIn("C3 off sample work max", figures)
        self.assertNotIn("C3 dpeak p99", figures)
        self.assertNotIn("C3 off work max", figures)

# test a full summary names them plainly operation.
    def test_a_full_summary_names_them_plainly(self):
        rows = []
        for workshop in scoring.core_selection():
            rows.extend(paired(workshop))
        for extra in range(len(scoring.core_selection())):
            rows.extend(paired("outside-%d" % extra))
        figures = self.summary(rows, "out/does-not-exist")

        self.assertEqual(figures["dataset core corpus"], 0)
        self.assertIn("C3 dpeak p99", figures)
        self.assertIn("C3 off work max", figures)
        self.assertNotIn("C3 weighted dpeak p99", figures)

# test a dataset with no provenance records absent rather than nothing operation.
    def test_a_dataset_with_no_provenance_records_absent_rather_than_nothing(self):
        rows = paired("a") + paired("b")
        figures = self.summary(rows, "out/does-not-exist")
        self.assertEqual(figures["provenance"], "absent")

# test an unscored verdict is recorded as unscored not as a failure operation.
    def test_an_unscored_verdict_is_recorded_as_unscored_not_as_a_failure(self):
        rows = []
        for workshop in scoring.core_selection():
            rows.extend(paired(workshop, peak_off=300.0, peak_on=300.001))
        figures = self.summary(rows, "out/does-not-exist")
        self.assertEqual(figures["C3 cost verdict"], "unscored")


class TheProvenanceRowsAreOneImplementation(unittest.TestCase):
    """`verdict.py` and `cap.py` write the same rows from the same reader (`P5`).

    Two transcriptions of one file are two things that drift, and the thing they would drift about
    is which build a published figure was measured on.
    """

# test a resumed walk carries every version it saw operation.
    def test_a_resumed_walk_carries_every_version_it_saw(self):
        import provenance
        import tempfile

        with tempfile.TemporaryDirectory() as directory:
            with open(os.path.join(directory, "provenance.txt"), "w") as handle:
                handle.write("# a comment is context, not a statistic\n"
                             "walk cap started one\ncommit aaa\nCubes.xml 111\n"
                             "\nwalk cap started two\ncommit bbb\nCubes.xml 222\n")
            figures = {s: v for s, v, _ in provenance.summary_rows(directory)}

        self.assertEqual(figures["provenance commit"], "bbb")
        self.assertEqual(figures["provenance commit versions"], 2)
        self.assertEqual(figures["provenance commit all"], "aaa bbb")
        self.assertEqual(figures["provenance Cubes.xml all"], "111 222")
        self.assertNotIn("provenance #", figures)


if __name__ == "__main__":
    unittest.main()
