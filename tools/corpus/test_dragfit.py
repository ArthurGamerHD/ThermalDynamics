#!/usr/bin/env python3
"""The drag milestone's criterion, and that scoring it is arithmetic rather than judgement."""

import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cruise
import dragfit


def ship(thrust, area, mass, shape=None):
    row = {"ship": "s", "thrust_n": str(thrust), "exposed_area_m2": str(area),
           "mass_kg": str(mass)}
    if shape is not None:
        row["shape_factor"] = str(shape)
    return row


class OnlyHullsThatCanLiftThemselvesAreScored(unittest.TestCase):
    """**A hull that cannot lift itself is not a ship drag broke; it never flew.**

    The criterion's own words are *must also still be able to fly*, and the worst ceilings in the
    census are stations with one or two thrusters. Reading the criterion is what excludes them, not
    a subset chosen because it passes.
    """

    def test_a_station_is_not_in_the_population(self):
        # A tonne of ship with a newton of thrust: 0.0001 g, and it has never left the ground.
        _, _, population = dragfit.score([ship(1.0, 100.0, 1000.0)], 0.5,
                                         cruise.SEA_LEVEL_DENSITY, False)
        self.assertEqual(0, population)

    def test_exactly_one_gravity_is_in(self):
        thrust = 1000.0 * dragfit.GRAVITY
        _, _, population = dragfit.score([ship(thrust, 100.0, 1000.0)], 0.5,
                                         cruise.SEA_LEVEL_DENSITY, False)
        self.assertEqual(1, population)

    def test_a_row_missing_mass_is_dropped_rather_than_assumed(self):
        row = ship(10000.0, 100.0, 1000.0)
        del row["mass_kg"]

        _, _, population = dragfit.score([row], 0.5, cruise.SEA_LEVEL_DENSITY, False)
        self.assertEqual(0, population)


class TheCriterionIsBothHalvesOrNeither(unittest.TestCase):
    """Registered as a pair, because the single figure could not separate a modelling error from a
    tuning one: a large share with a high ceiling is drag too strong on small hulls only."""

    def test_both_halves_must_hold(self):
        self.assertTrue(dragfit.verdict(3.13, 78.8))
        self.assertFalse(dragfit.verdict(14.06, 55.7))

        # Either half alone is enough to fail it.
        self.assertFalse(dragfit.verdict(6.0, 78.8))
        self.assertFalse(dragfit.verdict(3.13, 55.7))

    def test_the_registered_thresholds_are_what_the_row_says(self):
        self.assertEqual(5.0, dragfit.MAX_OVERPOWERED_SHARE)
        self.assertEqual(60.0, dragfit.MIN_P1_CEILING)
        self.assertEqual(100.0, dragfit.CRITERION_SPEED)


class TheShapeFactorEntersTheCriterionAsItEntersTheDrag(unittest.TestCase):
    """One factor on the frontal area, in the criterion and in the cruise speed alike — so a hull
    cannot be scored one way by one and another way by the other."""

    def test_a_shape_factor_scales_the_drag_it_is_asked_about(self):
        full = dragfit.drag_newtons(100.0, 0.5, cruise.SEA_LEVEL_DENSITY, 100.0, 1.0)
        half = dragfit.drag_newtons(100.0, 0.5, cruise.SEA_LEVEL_DENSITY, 100.0, 0.5)

        self.assertAlmostEqual(full / 2.0, half, places=9)

    def test_no_shape_reads_a_shaped_census_as_the_model_without_the_term(self):
        rows = [ship(200000.0, 400.0, 5000.0, 0.25)]

        shaped = dragfit.score(rows, 0.5, cruise.SEA_LEVEL_DENSITY, True)
        plain = dragfit.score(rows, 0.5, cruise.SEA_LEVEL_DENSITY, False)

        # A quarter of the drag is twice the ceiling.
        self.assertAlmostEqual(2.0 * plain[1], shaped[1], places=6)

    def test_a_census_without_the_column_scores_as_the_unshaped_one(self):
        rows = [ship(200000.0, 400.0, 5000.0)]

        self.assertAlmostEqual(dragfit.score(rows, 0.5, cruise.SEA_LEVEL_DENSITY, True)[1],
                               dragfit.score(rows, 0.5, cruise.SEA_LEVEL_DENSITY, False)[1],
                               places=9)


class TheDragAtTheCriterionSpeedIsTheOneTheCeilingImplies(unittest.TestCase):
    """**The two halves must be the same model or the pair says nothing.** A hull whose ceiling is
    exactly the criterion speed is exactly the hull whose drag there equals its thrust."""

    def test_a_hull_ceilinged_at_the_criterion_speed_is_on_the_boundary(self):
        area, coefficient, mass = 400.0, 0.5, 5000.0

        # Pick the thrust that puts the ceiling on 100 m/s, then check the drag there matches it.
        thrust = dragfit.drag_newtons(area, coefficient, cruise.SEA_LEVEL_DENSITY,
                                      dragfit.CRITERION_SPEED, 1.0)

        ceiling = cruise.cruise(thrust, area, coefficient, cruise.SEA_LEVEL_DENSITY, 1.0)
        self.assertAlmostEqual(dragfit.CRITERION_SPEED, ceiling, places=6)

        self.assertTrue(thrust > 0 and thrust / (mass * dragfit.GRAVITY) > 1.0)


if __name__ == "__main__":
    unittest.main()
