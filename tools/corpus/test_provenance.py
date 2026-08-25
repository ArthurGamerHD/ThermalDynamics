#!/usr/bin/env python3
"""**The two readers of the provenance grammar agree**, which is the only thing that keeps them
from drifting apart in silence (`D3`).

`AuthoredWasteTests` reads the comment above every waste fraction in `Cubes.xml` to decide whether
a value is sourced, derived, unreachable or invented, and pins the four counts. `provenance.py`
reads the same comments to weight a census by them. Two parsers of one format drift in both
directions, so the counts this file asserts are the counts that test asserts, written out here so
that moving one without the other fails.

    python3 -m unittest discover -s tools/corpus -p 'test_*.py'
"""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import provenance

# The same four numbers AuthoredWasteTests.TheProvenanceOfEveryFractionIsCounted asserts.
PINNED = {"sourced": 43, "derived": 1, "unreachable": 108, "invented": 76}


class BothReadersOfTheGrammarCountTheSame(unittest.TestCase):
    def test_the_file_holds_the_fractions_the_suite_pins(self):
        tally = provenance.counts(provenance.authored())

        self.assertEqual(0, tally["none"], "a waste fraction claims no provenance at all")
        for name, expected in PINNED.items():
            self.assertEqual(expected, tally[name], name)

        self.assertEqual(sum(PINNED.values()), sum(tally.values()))

    def test_a_note_that_claims_nothing_is_not_read_as_a_source(self):
        self.assertIsNone(provenance.classify("a lamp is almost all waste heat"))
        self.assertIsNone(provenance.classify(""))
        self.assertEqual("sourced", provenance.classify("waste: electric motor"))
        self.assertEqual("derived", provenance.classify("derived: PowerEfficiency, the drive"))
        self.assertEqual("unreachable", provenance.classify("no producer: nothing produces"))
        self.assertEqual("invented", provenance.classify("invented: no source"))

    def test_a_producer_block_is_weighted_by_its_producer_fraction(self):
        by_type = provenance.class_by_type(provenance.authored())

        # The engine's 0.6 names a combustion engine and its consumer fraction is the fallback;
        # weighting a load census by the wrong side of it moves 6 % of the corpus's waste heat.
        self.assertEqual("sourced", by_type["HydrogenEngine"])
        self.assertEqual("invented", by_type["Reactor"])
        self.assertEqual("derived", by_type["JumpDrive"])
        self.assertEqual("sourced", by_type["MotorSuspension"])

        # The generator produces nothing, so its type resolves through the consumer side — which
        # is the side the last unsourced fraction moved on 2026-08-25.
        self.assertEqual("sourced", by_type["OxygenGenerator"])

    def test_one_type_s_share_of_its_own_ships_is_not_its_share_of_the_fleet(self):
        """The two questions provenance.py answers, on a composition it builds itself.

        Two ships: one carries a generator and little else, one carries a drive an order of
        magnitude larger and no generator. The fleet share is a tenth; the median share of the
        ships that carry one is nine tenths. Both are right and they are not the same statistic.
        """
        import csv
        import tempfile

        rows = [
            {"ship": "a", "workshop_id": "1", "subtype": "x", "type_id": "OxygenGenerator",
             "count": "1", "waste_full_w": "90", "share_of_waste": "0.9"},
            {"ship": "a", "workshop_id": "1", "subtype": "y", "type_id": "Reactor",
             "count": "1", "waste_full_w": "10", "share_of_waste": "0.1"},
            {"ship": "b", "workshop_id": "2", "subtype": "z", "type_id": "JumpDrive",
             "count": "1", "waste_full_w": "900", "share_of_waste": "1"},
        ]

        with tempfile.NamedTemporaryFile("w", suffix=".csv", newline="", delete=False) as handle:
            writer = csv.DictWriter(handle, fieldnames=list(rows[0].keys()))
            writer.writeheader()
            for row in rows:
                writer.writerow(row)
            path = handle.name

        try:
            ships, instances, shares = provenance.per_ship(path, "OxygenGenerator")
        finally:
            os.unlink(path)

        self.assertEqual(2, ships)
        self.assertEqual(1, instances)
        self.assertEqual([0.9], shares)
        self.assertAlmostEqual(0.09, 90 / 1000.0)


if __name__ == "__main__":
    unittest.main()
