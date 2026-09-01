#!/usr/bin/env python3
"""**What a walk in progress will cost, and how to tell whether the estimate means anything.**

A corpus walk runs for hours and its progress file is the only thing anybody can read while it
does. The obvious estimate off that file — the share of the population's *blocks* it has covered,
divided by the rate it covered them at — is the one that was used to abandon `CorpusCapWalk` on
2026-08-24, and it is not an instrument. **The corpus is walked largest first**, so
blocks-per-minute falls throughout every healthy run: the rate is a property of the ordering rather
than of the walk, and it is also violently noisy, because a mark is ten files and ten files can be
one capital hull or ten fighters.

So this script reports that estimate as the *spread* it actually has, and, where a **finished**
walk is named, runs the same estimator over that walk and prints what it says against the answer
that walk already gave (`P4` — nothing is its own oracle). An estimator that cannot recover the
cost of a run that is over is not evidence about the cost of a run that is not.

The honest estimate is the second one: **a walk costs what a walk of the same shape cost, times
the ratio of the work per ship.** `CorpusCapWalk` is `CorpusAirWalk`'s four scenarios plus a second
arm on each, sharing one parse, so its ceiling is 2x by construction and its measured ratio is what
this prints.

    python3 tools/corpus/pace.py out/cap-2026-08-24/progress.txt \
        --reference out/air-corpus-2026-08-24/progress.txt \
        --outcomes  out/air-corpus-2026-08-24/outcomes.csv

**A walk taken in slices is read as one walk**, which it was not until 2026-08-28. Relaunching to
resume is the documented normal path and a sliced walk writes many blocks into one progress file,
each restarting its own file count; read literally, the air re-take's ninetieth file of its sixth
slice sat at 272 minutes after the first slice's first mark, and this file — which exists to refuse
a bad estimate — reported **9.45x** per file where the honest figure was 3.05x. Counts continue
from the resume line above them, the gap between slices is not walked time, and a slice's clock
starts at its resume line rather than at its first mark, because a mark is ten files and the work
before the first one is real.

The rules argued here are stated canonically in [rules.md](../../docs/rules.md): `P2` `P4` `E2`.
"""
import argparse
import csv
import datetime
import os
import sys

# Where the corpus lives when nothing says otherwise. Only needed for the block-share estimate,
# which needs a file's size to know where in the walk order it sits.
CORPUS = os.path.expanduser(
    "~/.local/share/thermal-dynamics/corpus/steamapps/workshop/content/244850")


def marks(path):
    """`(minutes walked, files done)` for every batch line a walk wrote, earliest first.

    A walk writes a line every ten files and repeats its last line at the end, so a mark is kept
    only the first time its count is seen — a repeat would otherwise read as a stall.

    **A resumed walk restarts both counters, and reading it as one run is how this file came to
    produce the estimate it exists to refuse.** Relaunching to resume is the documented normal path,
    and a walk taken in slices writes many blocks into one progress
    file: each begins `air: resuming, N blueprints already finished`, and every `files` count after
    it is a count *within that slice*. Read literally, the 2026-08-28 air walk's ninetieth file of
    its sixth slice sat at 272 minutes after the first slice's first mark — 9.45x per file against
    its reference, where the honest figure over the same file range is a fifth of that.

    So a count is made cumulative from the resume line above it, and the gap between one slice's
    last mark and the next slice's first is **not** counted: the walk was not running in it. What
    this returns is minutes the walk was walking, which is the only thing a per-file cost can be built on.
    """
    seen = {}
    order = []

    finished = 0            # what earlier slices had already done, from the resume line
    walked = 0.0            # minutes this walk has actually been walking
    slice_start = None      # when the current slice's first mark landed
    slice_last = None       # and its most recent one

    with open(path, encoding="utf-8") as handle:
        for line in handle:
            parts = line.split()
            if len(parts) < 3:
                continue

            if parts[2] == "resuming," and len(parts) > 3:
                # Bank the slice that just ended and start a new one; its marks begin from zero.
                if slice_start is not None and slice_last is not None:
                    walked += (slice_last - slice_start).total_seconds() / 60.0

                # **The new slice's clock starts here, not at its first mark.** A mark is every ten
                # files, so the work before the first one is real walking — for the first slice
                # that is excluded by the convention this file has always used, and repeating that
                # exclusion once per slice would undercount a sliced walk by a mark's worth each
                # time. The cost of restarting the process is inside this and is part of what
                # slicing costs.
                slice_start = datetime.datetime.strptime(parts[0], "%H:%M:%S")
                slice_last = slice_start
                try:
                    finished = int(parts[3])
                except ValueError:
                    pass
                continue

            if parts[2] != "batch":
                continue

            when = datetime.datetime.strptime(parts[0], "%H:%M:%S")
            if slice_start is None:
                slice_start = when
            slice_last = when

            files = finished + int(parts[3].split("/")[0])
            if files in seen:
                continue

            minutes = walked + (when - slice_start).total_seconds() / 60.0
            seen[files] = minutes
            order.append((minutes, files))

    return order


def elapsed(order, files):
    """Minutes walked up to the mark at `files`, or None where there is no such mark.

    Minutes *walked*, not minutes since the first mark: see `marks`. On a walk taken in one run the
    two are the same number, which is why nothing noticed.
    """
    if not order:
        return None

    for minutes, count in order:
        if count == files:
            return minutes - order[0][0]

    return None


def selection(path):
    """The selection a walk was narrowed to, as workshop ids, or None where it walked the corpus.

    **A walk of a selection is not a walk of the corpus, and both estimators here silently assume
    it is.** `walk_order` is the corpus largest-first, so the block-share estimate reads *file 950*
    as the 950th-largest hull in the population; on a walk of the core corpus the 950th file is the
    950th-largest ship **of the selection**, and the two are not the same ship. Measured on the
    2026-08-28 core cap walk, the share it printed and the share it meant part company by up to
    **fifteen points** — 67.4 % against 52.8 % at file 950, 52.5 % against 43.3 % at file 500 —
    and they agree at the ends, which is what makes the error hard to see: nought is nought and
    everything is everything, and the walk's own first mark is close enough to look right.

    The walk records what it was narrowed to on its own first line — `walking N blueprints named by
    <path>` — so this is read rather than guessed, and a selection file that has since been
    overwritten or deleted returns None the same way no selection does. That is the honest state:
    *this walk was narrowed and the narrowing is gone* leaves nothing to compute a share over
    (`E8`, `P2`).
    """
    if not os.path.exists(path):
        return None

    named = None
    with open(path, encoding="utf-8") as handle:
        for line in handle:
            if " blueprints named by " in line:
                named = line.strip().split(" blueprints named by ", 1)[1]
                break

    if not named or not os.path.exists(named):
        return None

    ids = set()
    with open(named, encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if line.startswith("#") or "/content/244850/" not in line:
                continue
            ids.add(line.split("/content/244850/")[1].split("/")[0])
    return ids or None


def walk_order(root=CORPUS, only=None):
    """The corpus in the order a walk reads it: every `bp.sbc`, largest file first.

    This is `CorpusFixture.Find`'s sort, repeated here rather than assumed, because the whole
    point of the block-share estimate is that it depends on the ordering. `only` narrows it to a
    selection, which is the same sort over the same files and is what a narrowed walk actually
    reads.
    """
    sized = []

    if not os.path.isdir(root):
        return sized

    for entry in os.scandir(root):
        if only is not None and os.path.basename(entry.path) not in only:
            continue
        blueprint = os.path.join(entry.path, "bp.sbc")
        try:
            sized.append((os.path.getsize(blueprint), os.path.basename(entry.path)))
        except OSError:
            continue

    sized.sort(key=lambda item: -item[0])
    return sized


def block_counts(path):
    """`workshop id -> blocks`, from any walk's outcomes.

    A block count is a property of the blueprint rather than of the walk that read it, so the
    finished walk's outcomes are a legitimate source for a running walk's coverage.
    """
    counts = {}

    with open(path, newline="", encoding="utf-8") as handle:
        for row in csv.DictReader(handle):
            try:
                counts[row["workshop_id"]] = int(row["blocks"])
            except (KeyError, TypeError, ValueError):
                continue

    return counts


def coverage(order, counts, root=CORPUS, only=None):
    """Cumulative share of the walked blocks after each file of the walk order, 0..1.

    Returns an empty list when the corpus is not on this machine, because the share of a
    population cannot be guessed from a walk that has not finished it (`P2`). `only` makes the
    denominator the selection's blocks rather than the population's, which is what a narrowed walk
    is a share of.
    """
    sized = walk_order(root, only)
    if not sized:
        return []

    if only is not None:
        counts = {workshop: blocks for workshop, blocks in counts.items() if workshop in only}

    total = float(sum(counts.values()))
    if total <= 0:
        return []

    running = 0.0
    shares = []
    for _, workshop in sized:
        running += counts.get(workshop, 0)
        shares.append(running / total)

    return shares


def block_share_estimate(order, shares):
    """The estimator that abandoned the cap walk: remaining blocks over the latest block rate.

    Reported as `(elapsed, covered, projected total)` in minutes and share, one row per mark. The
    rate is taken over the last interval, which is what was done on 2026-08-24 and is the part
    that makes it fall.
    """
    rows = []
    if not order or not shares:
        return rows

    start = order[0][0]
    previous = None

    for walked, files in order:
        if files > len(shares):
            continue

        minutes = walked - start
        covered = shares[files - 1]

        if previous is not None and minutes > previous[0]:
            rate = (covered - previous[1]) / (minutes - previous[0])
        else:
            rate = covered / minutes if minutes > 0 else 0.0

        projected = minutes + (1.0 - covered) / rate if rate > 0 else float("inf")
        rows.append((minutes, covered, projected))
        previous = (minutes, covered)

    return rows


def spread(rows):
    """What the block-share estimate says over a run of marks, as a range rather than a number.

    One mark's projection is not a reading: a mark is ten files, and ten files of a
    largest-first corpus can be one capital hull or ten fighters. Stating the median with the
    range it was drawn from is what makes the noise visible (`P2`).
    """
    finite = sorted(row[2] for row in rows if row[2] != float("inf"))
    if not finite:
        return "no finite projection"

    middle = finite[len(finite) // 2]
    return (f"{len(finite)} marks projecting {min(finite):.0f} to {max(finite):.0f} min, "
            f"median {middle:.0f}")


#: Files a walk covers between marks. Stated by the walk, not inferred: it writes one every ten.
MARK_FILES = 10

#: How wide a gap between two reference marks may be and still be read between.
#:
#: **Five marks, and the reason it is bounded at all is the ordering.** The corpus is walked largest
#: first, so cost per file falls steeply and a straight line across a wide gap is a poor assumption
#: — which is what the *one mark in common* case was always about: a reference with marks at ten
#: files and seventy carries no information about file twenty, and reading it there would be
#: inventing one. Within a few marks the line is the same assumption the marks themselves carry.
WIDEST_BRACKET = 5 * MARK_FILES


def bracket(counts, files):
    """How far apart the two reference marks either side of `files` are."""
    if files <= counts[0] or files >= counts[-1]:
        return 0

    low, high = 0, len(counts) - 1
    while high - low > 1:
        middle = (low + high) // 2
        if counts[middle] <= files:
            low = middle
        else:
            high = middle

    return counts[high] - counts[low]


def at(counts, walked, files):
    """Minutes the reference had walked by `files`, interpolated between the marks either side.

    A walk's cost per file is not constant — the corpus is largest first — so this is a straight
    line between two marks ten files apart rather than a model of anything. Over that interval it
    is the same assumption the marks themselves carry.
    """
    if files <= counts[0]:
        return walked[0]
    if files >= counts[-1]:
        return walked[-1]

    low, high = 0, len(counts) - 1
    while high - low > 1:
        middle = (low + high) // 2
        if counts[middle] <= files:
            low = middle
        else:
            high = middle

    span = counts[high] - counts[low]
    if span <= 0:
        return walked[low]

    share = (files - counts[low]) / float(span)
    return walked[low] + (walked[high] - walked[low]) * share


def ratio(subject, reference):
    """How much dearer the subject walk is per file, over the marks the two walks share.

    Measured over the same files rather than the same time, which is the whole of why it is a
    comparison and not two readings (`P6`). Returns `(ratio, first mark, last mark)`.
    """
    mine = {files: walked for walked, files in subject}

    # **The reference is read at the subject's file counts rather than at counts the two happen to
    # share**, which is not a refinement — without it a resumed walk is compared on its first slice
    # alone. A walk writes a mark every ten files, so an unbroken run's marks are multiples of ten
    # and two such walks share nearly all of them; a *resumed* one counts from where it left off,
    # so its marks are 2,106 and 2,116 where the reference has 2,100 and 2,110 and the intersection
    # is empty. Measured on the 2026-08-28 air re-take: 21 of 268 marks were being used, all of
    # them from before the first resume, and the ratio had not moved since.
    ordered = sorted(reference, key=lambda pair: pair[1])
    counts = [files for _, files in ordered]
    walked = [minutes for minutes, _ in ordered]

    if len(counts) < 2:
        return None, None, None

    usable = sorted(f for f in mine
                    if counts[0] <= f <= counts[-1] and bracket(counts, f) <= WIDEST_BRACKET)
    if len(usable) < 2:
        return None, None, None

    first, last = usable[0], usable[-1]
    span_subject = mine[last] - mine[first]
    span_reference = at(counts, walked, last) - at(counts, walked, first)

    if span_reference <= 0:
        return None, first, last

    return span_subject / span_reference, first, last


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("progress", help="the progress file of the walk being estimated")
    parser.add_argument("--reference", help="the progress file of a walk that finished")
    parser.add_argument("--outcomes", help="any walk's outcomes.csv, for block counts")
    parser.add_argument("--corpus", default=CORPUS, help="the corpus root")
    args = parser.parse_args()

    subject = marks(args.progress)
    if not subject:
        print(f"no batch lines in {args.progress}")
        return 1

    print(f"{args.progress}: {len(subject)} marks, "
          f"{subject[-1][1]} files, {elapsed(subject, subject[-1][1]):.1f} min so far")

    # **A narrowed walk reads a different corpus, and every figure below depends on which one.**
    # The walk says so on its own first line, so it is read rather than guessed.
    narrowed = selection(args.progress)
    if narrowed:
        print(f"  narrowed to a selection of {len(narrowed):,} blueprints, so every share below is "
              f"of that selection")

    shares = []
    if args.outcomes:
        shares = coverage(subject, block_counts(args.outcomes), args.corpus, narrowed)
        if not shares:
            print("  no corpus on this machine, so the block-share estimate is not available")

    if shares:
        rows = block_share_estimate(subject, shares)
        if rows:
            minutes, covered, _ = rows[-1]
            print(f"  block share: {covered * 100:.1f} % of blocks in {minutes:.1f} min")
            print("  block-share estimate: " + spread(rows))

    if not args.reference:
        return 0

    reference = marks(args.reference)
    if not reference:
        print(f"no batch lines in {args.reference}")
        return 1

    total = elapsed(reference, reference[-1][1])
    dearer, first, last = ratio(subject, reference)

    print(f"{args.reference}: finished, {reference[-1][1]} files in {total:.1f} min")

    # **The per-file ratio compares file N with file N, and that is only a comparison when the two
    # walks read the same file N.** A selection walk's 520th file is the 520th largest ship *of the
    # selection*; a corpus walk's is the 520th largest in the population, which is very much bigger.
    # Refused rather than printed, because the number it produces looks entirely reasonable — on
    # the 2026-08-28 core cap walk it read **0.04x per file** and projected **0.2 hours** for a walk
    # budgeted at 78 minutes, which is the ratio of a fighter to a capital hull and not a ratio
    # between two walks (`P2`, `E8`).
    reference_narrowed = selection(args.reference)
    if bool(narrowed) != bool(reference_narrowed) or (
            narrowed and reference_narrowed and narrowed != reference_narrowed):
        print("  the two walks did not read the same blueprints in the same order — one is "
              "narrowed to a selection and the other is not, or they are narrowed to different "
              "ones — so file N of each is a different ship and there is no ratio to take")
        return 0

    if dearer is None:
        print("  the two walks share fewer than two marks, so there is no ratio to take")
        return 0

    print(f"  the subject is {dearer:.2f}x per file over files {first}..{last}")
    print(f"  so it costs about {dearer * total / 60.0:.1f} h")

    if shares and not narrowed:
        rows = block_share_estimate(reference, shares)
        cut = elapsed(subject, subject[-1][1])
        early = [row for row in rows if row[0] <= cut]
        if early:
            print(f"  the same block-share estimate, run over this finished walk's own first "
                  f"{cut:.0f} min: " + spread(early))
            print(f"  against the {total:.0f} min it took. That is the check the estimate never "
                  f"had (`P4`).")

    return 0


if __name__ == "__main__":
    sys.exit(main())
