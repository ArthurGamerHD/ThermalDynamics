#!/usr/bin/env python3
"""**What a progress file can and cannot be asked**, pinned so that changing it fails a check.

Every rule here is one that was got wrong once, and the one that cost a run is the first: a walk's
cost was projected from the rate it was covering the population's *blocks* at, on a corpus that is
walked largest first, so the rate fell throughout a healthy run and the projection ran away with
it. `CorpusCapWalk` was abandoned on that projection.

The rules these pin are stated canonically in [rules.md](../../docs/rules.md): `P2` `P4` `P6`.

    python3 -m unittest discover -s tools/corpus -p 'test_*.py'
"""
import datetime
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import pace


def progress(*lines):
    """A progress file holding the given `HH:MM:SS files` pairs, as a path."""
    handle = tempfile.NamedTemporaryFile("w", suffix=".txt", delete=False, encoding="utf-8")
    for when, files in lines:
        handle.write(f"{when} walk batch {files}/8144 ships {files} files {files}\n")
    handle.close()
    return handle.name


def at(minute, files):
    return (f"12:{minute:02d}:00", files)


class AProgressFileIsReadAsMarks(unittest.TestCase):
    def test_a_repeated_final_line_is_not_a_stall(self):
        # A walk writes its last batch line twice, once from the batch and once from the end. Kept
        # as two marks it reads as a walk that did nothing for the interval between them.
        order = pace.marks(progress(at(0, 10), at(5, 20), at(5, 20)))
        self.assertEqual([10, 20], [files for _, files in order])

    def test_lines_that_are_not_batch_lines_are_ignored(self):
        path = progress(at(0, 10))
        with open(path, "a", encoding="utf-8") as handle:
            handle.write("12:01:00 corpus excluded 8 oversized blueprints\n")

        self.assertEqual([10], [files for _, files in pace.marks(path)])

    def test_elapsed_is_measured_from_the_first_mark(self):
        order = pace.marks(progress(at(0, 10), at(30, 20)))
        self.assertEqual(30.0, pace.elapsed(order, 20))
        self.assertIsNone(pace.elapsed(order, 30))


class TheRatioComparesTheSameFiles(unittest.TestCase):
    """`P6` — the comparison holds everything but the subject equal, and the thing held equal here
    is *which ships were walked*, not how long each walk had been running."""

    def test_it_is_taken_over_the_marks_the_two_walks_share(self):
        subject = pace.marks(progress(at(0, 10), at(20, 20), at(40, 30)))
        reference = pace.marks(progress(at(0, 10), at(10, 20), at(20, 30), at(25, 40)))

        dearer, first, last = pace.ratio(subject, reference)
        self.assertAlmostEqual(2.0, dearer)
        self.assertEqual((10, 30), (first, last))

    def test_two_walks_with_one_mark_in_common_have_no_ratio(self):
        subject = pace.marks(progress(at(0, 10), at(20, 20)))
        reference = pace.marks(progress(at(0, 10), at(10, 70)))

        dearer, _, _ = pace.ratio(subject, reference)
        self.assertIsNone(dearer)


class TheBlockShareEstimateIsReportedAsASpread(unittest.TestCase):
    """The estimator that abandoned the cap walk. It is kept so it can be shown, not trusted."""

    def test_a_falling_rate_projects_a_longer_and_longer_run(self):
        # Half the blocks in the first ten minutes, a tenth in the next ten: exactly the shape a
        # largest-first walk produces whatever its health.
        order = pace.marks(progress(at(0, 10), at(10, 20), at(20, 30)))
        shares = [0.0] * 9 + [0.1] + [0.0] * 9 + [0.6] + [0.0] * 9 + [0.7]

        rows = pace.block_share_estimate(order, shares)
        self.assertEqual(3, len(rows))
        self.assertLess(rows[1][2], rows[2][2])

    def test_the_spread_is_the_range_and_not_the_last_mark(self):
        rows = [(10.0, 0.1, 120.0), (20.0, 0.2, 600.0), (30.0, 0.3, 300.0)]
        self.assertEqual("3 marks projecting 120 to 600 min, median 300", pace.spread(rows))

    def test_a_stalled_mark_projects_no_finite_time_and_is_not_counted(self):
        rows = [(10.0, 0.1, float("inf"))]
        self.assertEqual("no finite projection", pace.spread(rows))


class CoverageNeedsThePopulationItIsAShareOf(unittest.TestCase):
    """`P2` — what the instrument could not see is part of the result. Without the corpus on this
    machine there is no denominator, and a guessed one would read as a measurement."""

    def test_no_corpus_means_no_coverage_rather_than_an_assumed_one(self):
        self.assertEqual([], pace.coverage([], {"1": 100}, root="/nonexistent"))


if __name__ == "__main__":
    unittest.main()
