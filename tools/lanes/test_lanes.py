"""What the lane checker has to get right to be worth running."""

import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lanes


class ADurationIsReadOrItIsNothing(unittest.TestCase):
    """A run the runner recorded no duration for must read as nought rather than throw, because one
    malformed row would otherwise take the whole report down."""

# test the trx form is hours minutes seconds operation.
    def test_the_trx_form_is_hours_minutes_seconds(self):
        self.assertAlmostEqual(1.5, lanes.seconds("00:00:01.5000000"), places=6)
        self.assertAlmostEqual(63.25, lanes.seconds("00:01:03.2500000"), places=6)
        self.assertAlmostEqual(3600.0, lanes.seconds("01:00:00.0000000"), places=6)

# test anything unreadable is nought operation.
    def test_anything_unreadable_is_nought(self):
        self.assertEqual(0.0, lanes.seconds(None))
        self.assertEqual(0.0, lanes.seconds(""))
        self.assertEqual(0.0, lanes.seconds("nonsense"))


class TheTraitIsFoundAboveTheClassAndNotBesideIt(unittest.TestCase):
    """**The attribute block is taken whole rather than matched adjacently.** A `[Collection]` between
    the trait and the class is common in this suite, and matching adjacently would read every one of
    those as untagged — which is the direction that fails a run."""

# written operation.
    def written(self, text):
        folder = tempfile.mkdtemp()
        with open(os.path.join(folder, "T.cs"), "w", encoding="utf-8") as handle:
            handle.write(text)
        return folder

# test a trait directly above the class is found operation.
    def test_a_trait_directly_above_the_class_is_found(self):
        folder = self.written('[Trait("speed", "slow")]\npublic class Slow { }\n')
        self.assertEqual({"Slow"}, lanes.tagged(folder))

# test a trait above another attribute is still found operation.
    def test_a_trait_above_another_attribute_is_still_found(self):
        folder = self.written(
            '[Trait("speed", "slow")]\n[Collection("alone")]\npublic class Slow { }\n')
        self.assertEqual({"Slow"}, lanes.tagged(folder))

# test a class with no trait is not found operation.
    def test_a_class_with_no_trait_is_not_found(self):
        folder = self.written('[Collection("alone")]\npublic class Fast { }\n')
        self.assertEqual(set(), lanes.tagged(folder))

# test another trait is not this one operation.
    def test_another_trait_is_not_this_one(self):
        folder = self.written('[Trait("subject", "slow")]\npublic class Fast { }\n')
        self.assertEqual(set(), lanes.tagged(folder))


class OnlyOneDirectionFailsTheRun(unittest.TestCase):
    """**Over the threshold and untagged makes the fast lane slow; tagged and free costs coverage.**
    The second is worth reporting and must not fail a run, or the checker refuses to pass on a
    machine that happened to run quickly."""

# test an untagged heavy class is reported operation.
    def test_an_untagged_heavy_class_is_reported(self):
        heavy, idle = lanes.drift({"Big": 9.0, "Small": 0.9}, set())

        self.assertEqual([("Big", 9.0)], heavy)
        self.assertEqual([], idle)

# test a tagged heavy class is not drift operation.
    def test_a_tagged_heavy_class_is_not_drift(self):
        heavy, idle = lanes.drift({"Big": 9.0}, {"Big"})

        self.assertEqual([], heavy)
        self.assertEqual([], idle)

# test a tagged free class is the other direction operation.
    def test_a_tagged_free_class_is_the_other_direction(self):
        heavy, idle = lanes.drift({"Tiny": 0.01}, {"Tiny"})

        self.assertEqual([], heavy)
        self.assertEqual([("Tiny", 0.01)], idle)

# test a class between the two thresholds is neither operation.
    def test_a_class_between_the_two_thresholds_is_neither(self):
        heavy, idle = lanes.drift({"Middling": 1.0}, {"Middling"})

        self.assertEqual([], heavy)
        self.assertEqual([], idle)


if __name__ == "__main__":
    unittest.main()
