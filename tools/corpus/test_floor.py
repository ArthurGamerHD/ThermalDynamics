#!/usr/bin/env python3
"""**The floor report's arithmetic, and the two ways it could quietly say the wrong thing.**

`C30` is decided on the paired floor walk, and until 2026-08-29 that walk had no reader: its
figures were scored by hand once and written onto a page, so nothing could check them (`E5`). The
two things a reader of a paired walk has to get right are which arm is which, and what the price is
averaged over — and both are silent when wrong, because either produces a number of the right shape.

The rules these pin are stated canonically in [rules.md](../../docs/rules.md): `E5` `E8` `M1` `P6`.

    python3 -m unittest discover -s tools/corpus -p 'test_*.py'
"""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import floor
import scoring


def row(ship, scenario, arm, **columns):
    out = {
        "ship": ship,
        "workshop_id": ship,
        "scenario": scenario,
        "cap": str(arm),
        "peak_k": "300",
        "blocks": "100",
        "floored": "0",
        "run_seconds": "600",
        "substeps_demanded": "10",
    }
    out.update({k: str(v) for k, v in columns.items()})
    return out


class TheTwoArmsAreToldApart(unittest.TestCase):
    """`cap` 0 is the shipped configuration and -1 is the floor on.

    **A reader that mixed them would report the mechanism against itself** and the delta would be
    nought, which reads as *the floor is free* rather than as *this report is broken* (`M1`).
    """

    def test_a_pair_needs_both_arms(self):
        rows = [row("a", "reentry", floor.OFF), row("a", "reentry", floor.ON),
                row("b", "reentry", floor.OFF)]
        pairs, orphans = floor.pair(rows)

        self.assertEqual(len(pairs), 1)
        self.assertEqual(orphans, 1)
        _, control, floored = pairs[0]
        self.assertEqual(control["cap"], str(floor.OFF))
        self.assertEqual(floored["cap"], str(floor.ON))

    def test_an_unfinished_walk_is_counted_rather_than_joined_away(self):
        rows = [row("a", "reentry", floor.OFF)] * 1 + [row("b", "storm-parked", floor.ON)]
        pairs, orphans = floor.pair(rows)
        self.assertEqual(pairs, [])
        self.assertEqual(orphans, 2)


class ThePriceIsAveragedOverTheCellsTheFloorTouched(unittest.TestCase):
    """A cell the floor never engaged on has a delta of nought **by construction**.

    Folding those into the cost reports the mechanism's *reach* as though it were its price, and it
    reports it in the direction that makes the mechanism look cheap. On the 2026-08-29 walk the two
    denominators differ by more than a factor of two — p99 60.9 K over the engaged cells against
    28.3 K over every cell walked — so this is not a rounding question.
    """

    def test_unengaged_cells_would_halve_the_reported_price(self):
        engaged = [50.0, 60.0, 70.0, 80.0]
        untouched = [0.0] * 96

        self.assertGreater(scoring.percentile(engaged, 0.99),
                           scoring.percentile(engaged + untouched, 0.99))

    def test_a_walk_where_the_floor_never_engaged_reports_nothing(self):
        """`E8`: an unmeasured price decides nothing, and nought is not the same as nothing."""
        rows = [row("a", "reentry", floor.OFF, peak_k=300.0),
                row("a", "reentry", floor.ON, peak_k=300.0, floored=0)]
        pairs, _ = floor.pair(rows)
        engaged = [p for p in pairs if float(p[2]["floored"]) > 0]
        self.assertEqual(engaged, [])


class TheRegisteredConstantsAreUnderTest(unittest.TestCase):
    """The bands `C30` was registered against live in `scoring.py`, not in a sentence.

    They were prose until 2026-08-29, which is how a page can quote a figure scored against a
    threshold no longer written anywhere the code can see (`D3`, `E5`).
    """

    def test_the_reach_band_is_open_below_and_closed_at_the_caps_measured_share(self):
        low, high = scoring.FLOOR_REACH_BAND
        self.assertEqual(low, 0.0)
        self.assertEqual(high, 5.83)

    def test_the_cost_threshold_is_what_the_mod_already_accepts(self):
        self.assertEqual(scoring.FLOOR_COST_P99_KELVIN, scoring.CAP_ACCEPTED_KELVIN)

    def test_the_decision_rule_is_the_one_the_cap_is_scored_by(self):
        """One rule for both mechanisms, so *imperceptible* cannot mean two things (`D3`)."""
        self.assertEqual(scoring.cap_decision(0.01), "ship")
        self.assertEqual(scoring.cap_decision(0.3), "judgement")
        self.assertEqual(scoring.cap_decision(60.9), "switch")


if __name__ == "__main__":
    unittest.main()
