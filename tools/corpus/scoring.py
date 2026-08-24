#!/usr/bin/env python3
"""Scoring helpers shared by a report and by its tests.

`verdict.py` is a script: its body runs on import, against whatever dataset the command line names.
So anything of its arithmetic that deserves a test lives here instead, where a test can call it
without running a report.
"""


def oversubscription_note(p99, cap):
    """What a `G6` breach is worth, in the words of the measurement that priced it.

    The criterion's marker is the demand, and the demand is a proxy for an error nobody had
    measured until [stiffness.md](../../docs/stiffness.md#what-refusing-the-demand-costs):
    refusing 1.15x of a demand costs 0.03 K on the hottest block over 600 simulated seconds, and
    the approximation stays under a tenth of a kelvin out to 1.5x on every hull shape measured.
    Past that the sweep has no figure, and this says so rather than extrapolating (`P2`).

    **The marker itself is deliberately left where it was written** (`E11`). This is a line beside
    the verdict so a reader can see the size of what failed; it is not a relaxation of what fails.
    """
    if not cap or p99 <= cap:
        return ""

    over = p99 / cap
    if over <= 1.5:
        priced = " — worth about 0.03 K on the hottest block, measured"
    else:
        priced = " — past the 1.5x the ceiling sweep has measured, so unpriced"

    return f"; p99 is {over:.2f}x the ceiling{priced}"


# Past here a peak says "ran away" and nothing finer: 1,800 s of an undamped source rather than a
# temperature the mod can reach.
RAN_AWAY_KELVIN = 1500.0


def peak_is_censored(over_critical, peak):
    """Whether a run's peak temperature has stopped being a temperature (`E9`).

    **Censoring begins at a block's own rating, not at a round number.** The lab never destroys an
    overheating block, so from the moment anything is past critical it keeps generating undamped
    for the rest of the clock and the peak describes the harness. Reading it off the magnitude
    instead — a fixed 1,500 K line — called 9 % of a retest censored where 31 % of it was, and 9 %
    against 85 % in the one scenario a finding was drawn from
    ([backlog.md](../../docs/backlog.md) `C2`).
    """
    return (over_critical or 0) > 0


def peak_ran_away(peak):
    """The stronger flag: a peak past `RAN_AWAY_KELVIN` is not even an ordering."""
    return peak is not None and peak >= RAN_AWAY_KELVIN


# ---- how stable a censored median is -------------------------------------------------------

INFINITE = float("inf")

# Resamples per estimate. Enough that the interval is stable to about a per cent, and cheap
# enough to run inside a report: forty hulls resampled four thousand times is a millisecond.
RESAMPLES = 4000


def censored_median(values):
    """The median of a series holding `float("inf")` for the runs that never got there.

    **One definition, because two of these drifted once already.** An infinity in the middle stays
    infinite — averaging `20.0` and `inf` is `inf` in float, which is the reading `G8` needs: with
    exactly half the hulls crossed the 50th percentile is past the end of the run, and that is no
    median rather than a large one. `pairs.crossing_median` calls this and maps the infinity to
    `None` for its own callers.
    """
    values = sorted(values)
    n = len(values)
    if n == 0:
        return INFINITE
    if n % 2:
        return values[n // 2]

    return (values[n // 2 - 1] + values[n // 2]) / 2.0


def joint_median_stability(entries, resamples=RESAMPLES, seed=20260824):
    """The same bootstrap over several series at once, drawn from the same hulls.

    `entries` is a list of `(series, predicate)`, every series indexed by the same hulls in the same
    order. One draw of hulls is applied to all of them and the resample counts only when every
    predicate holds, which is what a criterion with two halves needs: `G8` asks for a crossing
    inside a window **and** a recovery inside a bound, on one fleet. Testing the two separately and
    multiplying would assume they are independent, and they are not — both are medians over the
    same hulls.
    """
    entries = [(list(series), predicate) for series, predicate in entries]
    if not entries or not entries[0][0]:
        return 0.0

    size = len(entries[0][0])
    for series, _ in entries:
        if len(series) != size:
            raise ValueError("every series must be indexed by the same hulls")

    state = seed & 0xFFFFFFFF or 1
    passed = 0

    for _ in range(resamples):
        picked = []
        for _ in range(size):
            state ^= (state << 13) & 0xFFFFFFFF
            state ^= state >> 17
            state ^= (state << 5) & 0xFFFFFFFF
            picked.append(state % size)

        if all(predicate(censored_median([series[i] for i in picked]))
               for series, predicate in entries):
            passed += 1

    return passed / float(resamples)


def median_stability(series, predicate, resamples=RESAMPLES, seed=20260824):
    """How often a resampled fleet's median still satisfies `predicate`.

    **A censored median has a cliff in it, and a cell can be measured on the wrong side of the
    cliff without anything looking wrong.** The median is only defined while more than half the
    hulls crossed; below that it is *never*, which is the reading `pairs.crossing_median` already
    enforces. What that reading cannot show is how close a cell is to the boundary — a cell where
    55 % of hulls cross has a median, and a fleet resampled from the same population drops it below
    a half about half the time.

    This is the bootstrap that says which. Each resample draws `len(series)` hulls with
    replacement, takes the censored median, and asks the predicate. The share that pass is the
    answer, and it is a statement about the *statistic* rather than about the physics: two cells
    with the same median can be 47 % and 77 % reliable, and the repository has already been caught
    once by a crossing share falling under a half ([backlog.md](../../docs/backlog.md) `C12`).

    `series` holds one value per hull, with `float("inf")` for a hull that never crossed. `seed` is
    fixed so a report and its test agree, and so two runs of a decision produce the same number.
    """
    values = list(series)
    if not values:
        return 0.0

    state = seed & 0xFFFFFFFF or 1
    passed = 0

    for _ in range(resamples):
        picked = []
        for _ in range(len(values)):
            # xorshift32 rather than `random`, so the number a report prints does not depend on
            # what else in the process drew from the shared generator first.
            state ^= (state << 13) & 0xFFFFFFFF
            state ^= state >> 17
            state ^= (state << 5) & 0xFFFFFFFF
            picked.append(values[state % len(values)])

        if predicate(censored_median(picked)):
            passed += 1

    return passed / float(resamples)


def in_window(low, high):
    """A predicate for `median_stability`: inside a closed window, and never is outside it."""
    def test(value):
        return value != INFINITE and low <= value <= high

    return test


def within(bound):
    """A predicate for `median_stability`: at or under a bound, and never is over it."""
    def test(value):
        return value != INFINITE and value <= bound

    return test

