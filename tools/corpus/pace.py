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
    """`(time, files)` for every batch line a walk wrote, earliest first.

    A walk writes a line every ten files and repeats its last line at the end, so a mark is kept
    only the first time its count is seen — a repeat would otherwise read as a stall.
    """
    seen = {}
    order = []

    with open(path, encoding="utf-8") as handle:
        for line in handle:
            parts = line.split()
            if len(parts) < 4 or parts[2] != "batch":
                continue

            when = datetime.datetime.strptime(parts[0], "%H:%M:%S")
            files = int(parts[3].split("/")[0])
            if files in seen:
                continue

            seen[files] = when
            order.append((when, files))

    return order


def elapsed(order, files):
    """Minutes from the first mark to the mark at `files`, or None where there is no such mark."""
    if not order:
        return None

    start = order[0][0]
    for when, count in order:
        if count == files:
            return (when - start).total_seconds() / 60.0

    return None


def walk_order(root=CORPUS):
    """The corpus in the order a walk reads it: every `bp.sbc`, largest file first.

    This is `CorpusFixture.Find`'s sort, repeated here rather than assumed, because the whole
    point of the block-share estimate is that it depends on the ordering.
    """
    sized = []

    if not os.path.isdir(root):
        return sized

    for entry in os.scandir(root):
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


def coverage(order, counts, root=CORPUS):
    """Cumulative share of the population's blocks after each file of the walk order, 0..1.

    Returns an empty list when the corpus is not on this machine, because the share of a
    population cannot be guessed from a walk that has not finished it (`P2`).
    """
    sized = walk_order(root)
    if not sized:
        return []

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

    for when, files in order:
        if files > len(shares):
            continue

        minutes = (when - start).total_seconds() / 60.0
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


def ratio(subject, reference):
    """How much dearer the subject walk is per file, over the marks the two walks share.

    Measured over the same files rather than the same time, which is the whole of why it is a
    comparison and not two readings (`P6`). Returns `(ratio, first mark, last mark)`.
    """
    mine = {files: when for when, files in subject}
    theirs = {files: when for when, files in reference}

    shared = sorted(set(mine) & set(theirs))
    if len(shared) < 2:
        return None, None, None

    first, last = shared[0], shared[-1]
    span_subject = (mine[last] - mine[first]).total_seconds()
    span_reference = (theirs[last] - theirs[first]).total_seconds()

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

    shares = []
    if args.outcomes:
        shares = coverage(subject, block_counts(args.outcomes), args.corpus)
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

    if dearer is None:
        print("  the two walks share fewer than two marks, so there is no ratio to take")
        return 0

    print(f"  the subject is {dearer:.2f}x per file over files {first}..{last}")
    print(f"  so it costs about {dearer * total / 60.0:.1f} h")

    if shares:
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
