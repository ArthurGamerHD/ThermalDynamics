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
import io
import os
import shutil
import sys
import tempfile
import unittest
import unittest.mock

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
    """A mark at `minute` past noon. Minutes past fifty-nine roll into the hour, so a fixture can
    span one without the reader having to do the arithmetic."""
    return (f"{12 + minute // 60:02d}:{minute % 60:02d}:00", files)


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


class ASlicedWalkReadsAsOneWalk(unittest.TestCase):
    """**Relaunching to resume is the documented normal path, and it broke this estimator.**

    A walk taken in slices writes many blocks into one progress file.
    Each begins `air: resuming, N blueprints already finished`, and every count after it restarts
    from zero *within that slice*. Read literally, the 2026-08-28 air walk's ninetieth file of its
    sixth slice sat at 272 minutes after the first slice's first mark, and this file — which exists
    to refuse a bad estimate — reported 9.45x per file where the honest figure was 2.15x.

    Two things follow, and both are asserted below: a count is cumulative from the resume line
    above it, and the gap between one slice's last mark and the next slice's first is not walked
    time.
    """

    def sliced(self, *blocks):
        """A progress file of several slices: each block is `(already done, [(minute, files)])`."""
        handle = tempfile.NamedTemporaryFile("w", suffix=".txt", delete=False, encoding="utf-8")
        for first, (done, lines) in enumerate(blocks):
            if first or done:
                handle.write(f"12:00:00 air: resuming, {done} blueprints already finished"
                             f" and {8144 - done} to go\n")
            for when, files in lines:
                handle.write(f"{when} air batch {files}/{8144 - done} ships {files}"
                             f" files {files}\n")
        handle.close()
        return handle.name

    def test_a_slices_counts_continue_from_what_earlier_slices_finished(self):
        path = self.sliced((0, [at(0, 10), at(10, 20)]),
                           (20, [at(40, 10), at(50, 20)]))

        self.assertEqual([10, 20, 30, 40], [files for _, files in pace.marks(path)])

    def sliced_at(self, *blocks):
        """The same, with each slice's resume line placed at a stated minute."""
        handle = tempfile.NamedTemporaryFile("w", suffix=".txt", delete=False, encoding="utf-8")
        for done, resumed, lines in blocks:
            if resumed is not None:
                stamp = at(resumed, 0)[0]
                handle.write(f"{stamp} air: resuming, {done} blueprints already"
                             f" finished and {8144 - done} to go\n")
            for when, files in lines:
                handle.write(f"{when} air batch {files}/{8144 - done} ships {files}"
                             f" files {files}\n")
        handle.close()
        return handle.name

    def test_the_gap_between_slices_is_not_walked_time(self):
        # Slice one walks minutes 0 to 10. Slice two is relaunched at minute 40 and walks to 60.
        # The walk walked thirty minutes; fifty went by.
        path = self.sliced_at((0, None, [at(0, 10), at(10, 20)]),
                              (20, 40, [at(50, 10), at(60, 20)]))

        self.assertEqual(30.0, pace.elapsed(pace.marks(path), 40))

    def test_a_sliced_walk_and_the_same_walk_in_one_run_agree(self):
        """The property that matters: slicing is a way of splitting a walk, not a measurement.

        Ten minutes a mark either way. The sliced run's second slice resumes at minute 40 and
        reaches its first mark at 50, which is the same ten minutes of walking the whole run spends
        between its marks at 20 and 30 — so the two must report the same elapsed and the same
        ratio against a reference.
        """
        whole = pace.marks(progress(at(0, 10), at(10, 20), at(20, 30), at(30, 40)))
        cut = pace.marks(self.sliced_at((0, None, [at(0, 10), at(10, 20)]),
                                        (20, 40, [at(50, 10), at(60, 20)])))

        self.assertEqual([files for _, files in whole], [files for _, files in cut])
        self.assertEqual(pace.elapsed(whole, 40), pace.elapsed(cut, 40))

        # And so does the ratio, which is the figure a session actually reads.
        reference = pace.marks(progress(at(0, 10), at(5, 20), at(10, 30), at(15, 40)))
        self.assertEqual(pace.ratio(whole, reference), pace.ratio(cut, reference))

    def test_the_ratio_uses_a_resumed_walks_marks_and_not_just_its_first_slice(self):
        """**A resumed walk's marks do not land on the reference's grid, and that hid most of them.**

        A walk writes a mark every ten files, so an unbroken run's marks are multiples of ten and
        two such walks share nearly all of them. A resumed one counts from where it left off: its
        marks are 2,106 and 2,116 where the reference has 2,100 and 2,110, and the intersection is
        empty. Measured on the 2026-08-28 air re-take, 21 of 268 marks were being used — all of
        them from before the first resume — so the estimate had not moved in two hours of walking.
        """
        reference = pace.marks(progress(*[at(i, (i + 1) * 10) for i in range(0, 60, 5)]))

        # A subject resumed at 63, so every mark after it is 73, 83, ... — off the grid entirely.
        cut = pace.marks(self.sliced_at(
            (0, None, [at(0, 10), at(5, 20)]),
            (63, 20, [at(25, 10), at(30, 20), at(35, 30)])))

        counts = [files for _, files in cut]
        self.assertIn(83, counts, "the fixture is not producing off-grid marks")

        found, first, last = pace.ratio(cut, reference)

        self.assertIsNotNone(found)
        self.assertEqual(10, first)
        self.assertEqual(93, last, "the ratio stopped at the last mark shared with the reference,"
                                   " so it is reading the first slice alone")

    def test_the_ratio_is_unchanged_for_a_walk_that_was_never_resumed(self):
        """Interpolation must not move the answer where the marks already line up."""
        reference = pace.marks(progress(at(0, 10), at(5, 20), at(10, 30), at(15, 40)))
        whole = pace.marks(progress(at(0, 10), at(10, 20), at(20, 30), at(30, 40)))

        found, first, last = pace.ratio(whole, reference)

        self.assertAlmostEqual(2.0, found, places=6)
        self.assertEqual(10, first)
        self.assertEqual(40, last)

    def test_a_resume_line_that_names_no_count_does_not_throw(self):
        handle = tempfile.NamedTemporaryFile("w", suffix=".txt", delete=False, encoding="utf-8")
        handle.write("12:00:00 air: resuming, everything already finished\n")
        handle.write("12:00:10 air batch 10/8144 ships 10 files 10\n")
        handle.close()

        self.assertEqual([10], [files for _, files in pace.marks(handle.name)])


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


class ANarrowedWalkIsAShareOfWhatItWasNarrowedTo(unittest.TestCase):
    """A selection walk reads a different corpus, and every share here depends on which one.

    **The error is invisible at the ends and largest in the middle**, which is why it survived a
    reading: nought is nought and everything is everything, so a walk's first mark looks right. On
    the 2026-08-28 core cap walk the share printed and the share meant were 67.4 % against 52.8 %
    at file 950. The walk names its own selection on its first line, so this is read rather than
    guessed (`P2`, `E8`).
    """

    def setUp(self):
        self.root = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, self.root)
        # Four ships, largest first by file size, of which two are in the selection.
        self.sizes = {"aaa": 4000, "bbb": 3000, "ccc": 2000, "ddd": 1000}
        for workshop, size in self.sizes.items():
            os.makedirs(os.path.join(self.root, workshop))
            with open(os.path.join(self.root, workshop, "bp.sbc"), "w") as handle:
                handle.write("x" * size)

        self.chosen = os.path.join(self.root, "selection.txt")
        with open(self.chosen, "w") as handle:
            handle.write("# a comment is not a path\n")
            for workshop in ("aaa", "ccc"):
                handle.write("/content/244850/%s/bp.sbc\n" % workshop)

        self.counts = {"aaa": 400, "bbb": 300, "ccc": 200, "ddd": 100}

    def progress(self, header):
        path = os.path.join(self.root, "progress.txt")
        with open(path, "w") as handle:
            handle.write(header)
            handle.write("00:01:00 cap batch 10/4 ships 10 files 10\n")
        return path

    def test_the_selection_is_read_off_the_walks_own_first_line(self):
        path = self.progress("00:00:00 walking 2 blueprints named by %s\n" % self.chosen)
        self.assertEqual(pace.selection(path), {"aaa", "ccc"})

    def test_a_walk_of_the_corpus_names_no_selection(self):
        path = self.progress("00:00:00 walking the corpus\n")
        self.assertIsNone(pace.selection(path))

    def test_a_selection_file_that_is_gone_is_unknown_rather_than_assumed(self):
        path = self.progress("00:00:00 walking 2 blueprints named by /nowhere/at/all.txt\n")
        self.assertIsNone(pace.selection(path))

    def test_the_denominator_is_the_selections_blocks_and_not_the_populations(self):
        whole = pace.coverage([], self.counts, root=self.root)
        narrowed = pace.coverage([], self.counts, root=self.root, only={"aaa", "ccc"})

        # Whole: aaa is 400 of 1,000 blocks. Narrowed: aaa is 400 of 600, and the walk order holds
        # two files rather than four, so the same file index means a different ship.
        self.assertEqual(len(whole), 4)
        self.assertEqual(len(narrowed), 2)
        self.assertAlmostEqual(whole[0], 0.4)
        self.assertAlmostEqual(narrowed[0], 400.0 / 600.0)

    def test_a_selection_walk_and_a_corpus_walk_have_no_per_file_ratio(self):
        """File N of each is a different ship, so the ratio is refused rather than printed."""
        narrowed = self.progress("00:00:00 walking 2 blueprints named by %s\n" % self.chosen)
        whole = os.path.join(self.root, "reference.txt")
        with open(whole, "w") as handle:
            handle.write("00:00:00 walking the corpus\n")
            for mark in range(1, 5):
                handle.write("00:%02d:00 air batch %d/4 ships %d files %d\n"
                             % (mark, mark * 10, mark * 10, mark * 10))

        out = io.StringIO()
        with unittest.mock.patch.object(sys, "stdout", out), \
                unittest.mock.patch.object(
                    sys, "argv", ["pace.py", narrowed, "--reference", whole]):
            pace.main()
        report = out.getvalue()

        self.assertIn("narrowed to a selection", report)
        self.assertIn("there is no ratio to take", report)
        self.assertNotIn("x per file", report)


if __name__ == "__main__":
    unittest.main()
