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

# ---- what a step costs, in the solver's own unit -------------------------------------------

# Element visits a substep charges per node, from `ThermalSettings.NodeCostInLinks` — the weight
# `MaxElementVisitsPerStep` is denominated in. Links are one visit each and are added separately.
#
# **Nothing here multiplies by it any more, and that is the point.** `step_work` reads the walk's
# `substep_cost` column, which is `ThermalSimulation.SubstepCost` — the mod's own arithmetic, taken
# from the run that produced the row. This constant is the *label* a report prints beside the
# figure, and the pinned statement of what the unit is, so a reader is told the currency without a
# second implementation of it existing to drift (`P5`).
#
# **This was 2.125 until 2026-08-24, and 2.125 is a real number about the solver that is the wrong
# one here.** It is `ThermalSolverStep.SubstepWork`: the buffers cleared, the environment pass, the
# apply pass and one more walk, which is how a step is cut into frame-sized slices. The *allowance*
# is spent in a different currency — `ThermalSimulation.SubstepCost` is `links + 4 x nodes`, and
# that is what a step's length is divided by when the bound decides whether to shorten it. Scoring
# work in the pacing unit and comparing it against a bound denominated in the budget unit is a
# comparison between two currencies, 1.45x apart on a census hull. See benchmarks.md, What a
# substep costs, for where the 4 comes from, and What the allowance is worth for what it buys.
NODE_COST_IN_LINKS = 4.0

# What the shipped `MaxElementVisitsPerStep` grants one grid's step. Not a threshold invented for
# this criterion: it is the mod's own statement of what a step may cost, and a step past it is
# spread over more frames rather than refused — so the grid's simulated time runs slower than real
# time. It moves when the default does, deliberately, so this criterion scores the configuration
# that ships rather than one nobody runs; it was 2,000,000 until `C27` priced what a shortened step
# costs. See configuration.md, What a shortened step costs, and balance-lab.md for G6's cost half.
SHIPPED_VISIT_ALLOWANCE = 4000000.0

# What the shipped `MaxSubsteps` grants one step, whatever a grid asks for. The other half of
# `G6`'s "what the shipped caps grant", and pinned here for the same reason the allowance is: the
# criterion has to score the configuration that ships.
#
# **`verdict.py` used the largest `substeps_granted` in the dataset until 2026-08-24, and that is
# not a cap — it is the largest demand the population happened to make.** Granted is `ceil(demand)`
# until something clamps it, so on a population nothing clamps, `max(granted)` is `ceil(max
# demand)`, which is never below p99. The demand half was passing by construction: it compared a
# population against itself. Every figure it produced in vacuum was true anyway, because the real
# margin there is 4.6 against 64.
SHIPPED_SUBSTEP_CAP = 64.0


def step_work(substep_cost, substeps):
    """Element visits one step of a grid charges, which is the cost half of `G6`.

    `substep_cost` is the walk's `substep_cost` column: `ThermalSimulation.SubstepCost` for the
    assembly's most expensive grid, which is `links + 4 x nodes` read out of the mod rather than
    rebuilt here (`P5`). Per grid, because `MaxElementVisitsPerStep` is per grid.

    `substeps` is the demand rather than what was granted wherever a walk records both: the
    allowance shortens a step by comparing the *demand* against what it can afford, so a granted
    count -- already clamped by `MaxSubsteps` and by the allowance itself -- understates a stiff
    grid and is the wrong side of the very decision being scored.

    **A cost that is not a clock.** The corpus deliberately carries no timing column — a per-ship
    millisecond figure taken across thousands of hulls on a shared machine measures the machine —
    so the statistic is the work the solver charges itself and paces on.

    **This took `blocks` and `joints` until 2026-08-24, and `joints` is not the link count.** A
    joint is a rotor or a piston between two grids and there are none on most blueprints; a link is
    a face two blocks share and there are one to three per block. So `links + 4 x nodes` was
    evaluating to `4 x nodes` and every step-work figure this repository published was the node
    half alone — 1.51x low on a 2,000-block census hull, and low by a ship's own link-to-node ratio
    on any other, which is why the walk records the cost rather than a factor being applied here.
    `StepWorkUnitTests` pins it on the harness side.

    Returns None where any input is missing, so a walk that predates the column reports nothing
    rather than a figure built from a zero (`E8`, `P2`).
    """
    if substep_cost is None or substeps is None:
        return None
    if substep_cost <= 0 or substeps <= 0:
        return None

    return substeps * substep_cost


def keeps_up(work, allowance=SHIPPED_VISIT_ALLOWANCE):
    """Whether a step of this cost fits the allowance, which is whether the grid keeps real time."""
    return work is not None and work <= allowance


# ---- reading one run against another --------------------------------------------------------


def compare(before, now):
    """Rows of `(statistic, was, became, change)` for every statistic on either side that moved.

    **Every key on either side, not the intersection.** A statistic that appeared or vanished is
    the finding when two walks are compared — a scenario the other dataset does not carry, a column
    a walk predates — and an inner join drops exactly those (`P2`). Absent reads as an em dash
    rather than as a zero, because nought is a measurement.

    `change` is a percentage where both sides are numbers and the baseline is non-zero, and empty
    otherwise: there is no percentage between two words, and none from nothing.
    """
    rows = []

    for statistic in sorted(set(before) | set(now)):
        was = before.get(statistic, ABSENT)
        became = now.get(statistic, ABSENT)
        if str(was) == str(became):
            continue

        change = ""
        try:
            a, b = float(was), float(became)
            if a:
                change = "%+.1f%%" % ((b / a - 1.0) * 100.0)
        except (TypeError, ValueError):
            pass

        rows.append((statistic, was, became, change))

    return rows


# What a statistic reads as when a dataset does not carry it at all.
ABSENT = "\u2014"


# ---- percentiles ----------------------------------------------------------------------------


def percentile(values, q):
    """The `q` quantile, interpolated between the two ranks it falls between.

    **One definition, because there were two** (`P5`). `air.py` interpolated and `verdict.py` took
    `values[int(q * n)]`, and the documentation prints their outputs side by side — a corpus p99
    against a panel p99. On tens of thousands of samples the two agree to a third of a per cent; on
    forty they do not agree at all, because `int(0.99 * 40)` is 39 and the fortieth value of forty
    is the **maximum**. A p99 that is the largest reading in the set is not a percentile, and every
    figure this repository publishes for the panel is interpolated, so that is the definition kept.

    Returns None for an empty series, which is what an unmeasured thing reports (`E8`).
    """
    ordered = sorted(values)
    if not ordered:
        return None
    if len(ordered) == 1:
        return ordered[0]

    at = (len(ordered) - 1) * q
    low = int(at)
    high = min(low + 1, len(ordered) - 1)
    return ordered[low] + (ordered[high] - ordered[low]) * (at - low)


def percentiles(values):
    """The five figures a population row prints, all from `percentile`."""
    ordered = sorted(values)
    if not ordered:
        return {}

    return {
        "min": ordered[0],
        "p50": percentile(ordered, 0.5),
        "p95": percentile(ordered, 0.95),
        "p99": percentile(ordered, 0.99),
        "max": ordered[-1],
    }
