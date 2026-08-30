#!/usr/bin/env python3
"""**A derived cruise speed, and the ways the derivation could be quietly wrong.**

`K12`'s argument is that a top speed does not have to be authored: a ship cruises where its thrust
balances its drag, which this model has every term of. The question the row says decides it is
whether the derived speeds land where the authored ones do on real ships — measured by `cruise.py`
over the census, and pinned here at the level of the arithmetic.

The rules argued here are stated canonically in [rules.md](../../docs/rules.md): `E8` `E5`.

    python3 -m unittest discover -s tools/corpus -p 'test_*.py'
"""
import math
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import cruise


class TheBalanceOfThrustAndDrag(unittest.TestCase):
    """`T = ½ C_d ρ A v²` solved for `v`, which is the whole of the derivation."""

    def test_it_is_the_speed_where_drag_equals_thrust(self):
        thrust, area, cd, rho = 500000.0, 400.0, 1.0, 1.225
        speed = cruise.cruise(thrust, area, cd, rho)

        drag = 0.5 * cd * rho * area * speed * speed
        self.assertAlmostEqual(thrust, drag, places=3)

    def test_four_times_the_thrust_is_twice_the_speed(self):
        """A square law, so a ship needs four times the engine to go twice as fast."""
        one = cruise.cruise(100000.0, 400.0, 1.0, 1.225)
        four = cruise.cruise(400000.0, 400.0, 1.0, 1.225)
        self.assertAlmostEqual(2.0 * one, four, places=6)

    def test_four_times_the_area_is_half_the_speed(self):
        one = cruise.cruise(100000.0, 100.0, 1.0, 1.225)
        four = cruise.cruise(100000.0, 400.0, 1.0, 1.225)
        self.assertAlmostEqual(one / 2.0, four, places=6)

    def test_thinner_air_is_faster_by_a_square_root(self):
        """Which is why the comparison is made at sea level: it is the slowest air there is."""
        thick = cruise.cruise(100000.0, 400.0, 1.0, 1.225)
        thin = cruise.cruise(100000.0, 400.0, 1.0, 1.225 / 4.0)
        self.assertAlmostEqual(2.0 * thick, thin, places=6)


class AShipWithNothingToBalanceHasNoCruiseSpeed(unittest.TestCase):
    """**Nought thrust is not a cruise speed of nought, it is no cruise speed** (`E8`).

    A station has no thrusters and a hulk has no power; giving them a speed of zero would put 1,785
    ships of the 8,137 into the distribution at the bottom, and every quantile below the median
    would then be describing stations rather than ships.
    """

    def test_no_thrust_is_unmeasured(self):
        self.assertIsNone(cruise.cruise(0.0, 400.0, 1.0, 1.225))
        self.assertIsNone(cruise.cruise(None, 400.0, 1.0, 1.225))

    def test_no_area_is_unmeasured(self):
        self.assertIsNone(cruise.cruise(100000.0, 0.0, 1.0, 1.225))
        self.assertIsNone(cruise.cruise(100000.0, None, 1.0, 1.225))

    def test_a_world_with_no_drag_at_all_has_no_answer_rather_than_an_infinity(self):
        """`C_d` or density at nought is a world where nothing ever stops accelerating."""
        self.assertIsNone(cruise.cruise(100000.0, 400.0, 0.0, 1.225))
        self.assertIsNone(cruise.cruise(100000.0, 400.0, 1.0, 0.0))


class TheAuthoredBandIsWhatItIsComparedAgainst(unittest.TestCase):
    """The band is RTS's own, stated once so a page cannot quote a different one (`E5`, `D3`)."""

    def test_the_band_is_the_authored_one(self):
        self.assertEqual((60.0, 110.0), cruise.AUTHORED_BAND)

    def test_the_comparison_density_is_sea_level(self):
        self.assertAlmostEqual(1.225, cruise.SEA_LEVEL_DENSITY, places=6)


if __name__ == "__main__":
    unittest.main()
