"""**Reading a dial sweep as what moved, and the two ways that goes quietly wrong.**

`KnobSweep` writes one row per ship, scenario and level. Turning that into *what this dial does*
needs two things right, and both fail silently: a level has to be compared against **the same ship**
at the shipped level, and a dial that only touches one block type has to be judged on the ships it
can reach rather than on every ship it was run over.

The rules argued here are stated canonically in [rules.md](../../docs/rules.md): `E4` `E8` `M1`.

    python3 -m unittest discover -s tools/corpus -p 'test_*.py'
"""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import knob


# row operation.
def row(ship, scenario, level, shipped, peak):
    return {"knob": "d", "ship": ship, "workshop_id": ship, "scenario": scenario,
            "level": str(level), "shipped": str(shipped), "peak_k": str(peak)}


class ALevelIsComparedAgainstTheSameShip(unittest.TestCase):
    """**Not against the population, which is a different statement** (`M1`).

    A delta between two *different* ships at two levels is a statement about the ships. The pairing
    is by ship and scenario, which is the same shape `cap.py` and `floor.py` use.
    """

# test the pair is one ship in one scenario operation.
    def test_the_pair_is_one_ship_in_one_scenario(self):
        rows = [row("a", "s", 1, 1, 300.0), row("a", "s", 2, 1, 310.0),
                row("b", "s", 1, 1, 500.0), row("b", "s", 2, 1, 505.0)]

        shipped, levels, orphans = knob.pairs(rows, "d")

        self.assertEqual("1", shipped)
        self.assertEqual(0, orphans)
        deltas = sorted(abs(p - b) for b, p, _ in levels["2"])
        self.assertEqual([5.0, 10.0], deltas)

# test a row with no shipped partner is counted rather than compared operation.
    def test_a_row_with_no_shipped_partner_is_counted_rather_than_compared(self):
        """An interrupted sweep leaves exactly that, and a default would compare against nothing."""
        rows = [row("a", "s", 2, 1, 310.0)]
        shipped, levels, orphans = knob.pairs(rows + [row("b", "s", 1, 1, 300.0)], "d")

        self.assertEqual(1, orphans)
        self.assertEqual({}, dict(levels))

# test a dial with no row at its shipped level is refused operation.
    def test_a_dial_with_no_row_at_its_shipped_level_is_refused(self):
        """Nothing says what it moved *from*, so there is no delta to take (`E8`)."""
        rows = [row("a", "s", 2, 1, 310.0), row("a", "s", 4, 1, 320.0)]
        with self.assertRaises(SystemExit):
            knob.pairs(rows, "d")


class ADialIsJudgedOnTheShipsItReaches(unittest.TestCase):
    """**A dial that only touches one block type moves nothing on hulls without it.**

    Folding those zeroes into the band reports the dial's *reach* as though it were its effect, and
    in the direction that makes every dial look harmless — which is the same error `floor.py`
    avoids by scoring the cells its mechanism engaged on.
    """

# test untouched cells would drown the effect operation.
    def test_untouched_cells_would_drown_the_effect(self):
        rows = []
        for i in range(4):
            rows.append(row("carrier%d" % i, "s", 1, 1, 300.0))
            rows.append(row("carrier%d" % i, "s", 2, 1, 340.0))
        for i in range(96):
            rows.append(row("plain%d" % i, "s", 1, 1, 300.0))
            rows.append(row("plain%d" % i, "s", 2, 1, 300.0))

        _, levels, _ = knob.pairs(rows, "d")
        deltas = [abs(p - b) for b, p, _ in levels["2"]]
        reached = [d for d in deltas if d > 1e-4]

        self.assertEqual(100, len(deltas))
        self.assertEqual(4, len(reached))

        self.assertEqual(0.0, sorted(deltas)[len(deltas) // 2])

        self.assertEqual(40.0, sorted(reached)[len(reached) // 2])


if __name__ == "__main__":
    unittest.main()
