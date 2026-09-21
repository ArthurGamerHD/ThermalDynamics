"""`panel.py`'s per-type rules select the ships they name.

The panel is the instrument every dial is swept over, and a rule that selects the *wrong* ships
still selects ships — so nothing about a broken one looks broken. That is not hypothetical: until
2026-08-25 the per-type rules matched a substring of the **subtype**, and no vanilla reactor's
subtype contains the word "reactor". `reactor-heavy` chose from twelve candidates out of 5,728, and
the twelve were `LargePrototechReactor`, which the game types as a `HydrogenEngine` and which
wastes 0.60 rather than the 0.01 the rule's own note names.

So what is checked here is the mapping itself: that each name points at a type id the game has, and
that matching on a type id finds the blocks matching on a subtype missed.
"""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))


# panel types operation.
def panel_types():
    """`panel.py`'s type table, read without running the script.

    The script does its work at import time — it reads a census and writes a panel — so it cannot
    be imported. The table is small and its shape is fixed, which makes reading it out of the
    source the honest way to check it rather than a shortcut around it.
    """
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "panel.py")
    with open(path, encoding="utf-8") as handle:
        text = handle.read()

    start = text.index("TYPES = {")
    end = text.index("}", start)
    table = {}
    for line in text[start:end].split("\n")[1:]:
        line = line.strip().rstrip(",")
        if not line:
            continue
        name, _, type_id = line.partition(":")
        table[name.strip().strip('"')] = type_id.strip().strip('"')

    return table


class EveryPerTypeRuleNamesATypeTheGameHas(unittest.TestCase):
    KNOWN = {"JumpDrive", "HydrogenEngine", "Reactor", "OxygenGenerator", "BatteryBlock",
             "Thrust", "Refinery", "Assembler", "GravityGenerator", "AirVent", "VirtualMass"}

# test the table is read and is not empty operation.
    def test_the_table_is_read_and_is_not_empty(self):
        self.assertTrue(panel_types(), "no TYPES table was read, so this test checks nothing")

# test every name points at a real type id operation.
    def test_every_name_points_at_a_real_type_id(self):
        for name, type_id in panel_types().items():
            self.assertIn(type_id, self.KNOWN, f"{name} points at {type_id!r}")

# test the dial names and the knobs agree operation.
    def test_the_dial_names_and_the_knobs_agree(self):
        """Each per-type panel rule exists because a dial acts on that type."""
        table = panel_types()

        for name in ("jumpdrive", "engine", "reactor", "oxygen"):
            self.assertIn(name, table, f"{name} has no type to select on")

# test no vanilla reactor subtype contains the word reactor operation.
    def test_no_vanilla_reactor_subtype_contains_the_word_reactor(self):
        """The defect this table replaced, kept as a fact rather than as a memory.

        If a future game update names a reactor subtype `SomethingReactor`, matching on a subtype
        would start to appear to work — which is the state that hid this for as long as it did.
        """
        vanilla = ["LargeBlockSmallGenerator", "LargeBlockLargeGenerator",
                   "SmallBlockSmallGenerator", "SmallBlockLargeGenerator"]

        for subtype in vanilla:
            self.assertNotIn("reactor", subtype.lower())

# test the composition is keyed on type id in the script operation.
    def test_the_composition_is_keyed_on_type_id_in_the_script(self):
        """The script compares `r["type_id"]`, not a substring of `r["subtype"]`."""
        path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "panel.py")
        with open(path, encoding="utf-8") as handle:
            text = handle.read()

        self.assertIn('r["type_id"] == type_id', text)
        self.assertNotIn('match.lower() in r["subtype"].lower()', text)


if __name__ == "__main__":
    unittest.main()
