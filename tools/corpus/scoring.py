#!/usr/bin/env python3
"""Scoring helpers shared by a report and by its tests.

`verdict.py` is a script: its body runs on import, against whatever dataset the command line names.
So anything of its arithmetic that deserves a test lives here instead, where a test can call it
without running a report.
"""
import csv
import os



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
    return weighted_percentile([(value, 1.0) for value in values], q)


def weighted_percentile(pairs, q):
    """The `q` quantile of `(value, weight)` pairs, on the same definition as `percentile`.

    **A reduced corpus is a sample with weights, and a sample read without them is a different
    population** (`P1`). `core.py` keeps every small ship and one in twenty of the giants, so a
    giant in the core stands for twenty in the corpus; counting it once would report the population
    as though it were mostly small, which is the shape of error `E2` is about. Each kept ship
    carries the count it represents and every figure is taken over those counts.

    **It is one implementation rather than two** (`P5`, `D3`). `percentile` is this function with
    every weight at 1, and `WeightedPercentileMatchesTheUnweightedOne` holds the two together — the
    failure it prevents is a weighted corpus figure and an unweighted panel figure differing by the
    estimator rather than by the population, which is exactly the confusion that made `percentile`
    one definition in the first place.

    An item of weight `w` occupies a *span* of `w` ranks rather than a point, which is what makes
    an integer weight mean exactly what repeating the row that many times means — the property
    `AnIntegerWeightIsTheSameAsRepeatingTheRow` holds. A span of weight 1 is a single rank, so the
    unweighted case is the same arithmetic on the same ranks. Returns None for an empty series
    (`E8`).
    """
    ordered = sorted((value, weight) for value, weight in pairs if weight and weight > 0)
    if not ordered:
        return None
    if len(ordered) == 1:
        return ordered[0][0]

    total = sum(weight for _, weight in ordered)
    if total <= 1:
        return ordered[0][0]

    spans = []
    below = 0.0
    for value, weight in ordered:
        spans.append((below, below + weight - 1.0))
        below += weight

    at = (total - 1.0) * q
    if at <= spans[0][1]:
        return ordered[0][0]
    for index in range(len(spans)):
        start, end = spans[index]
        if start <= at <= end:
            return ordered[index][0]
        if at < start:
            last = spans[index - 1][1]
            if start <= last:
                return ordered[index][0]
            reach = (at - last) / (start - last)
            return ordered[index - 1][0] + (ordered[index][0] - ordered[index - 1][0]) * reach
    return ordered[-1][0]


def percentiles(values):
    """The five figures a population row prints, all from `percentile`.

    This is `weighted_percentiles` with every weight at 1, for the same reason `percentile` is
    `weighted_percentile` with every weight at 1 (`P5`): a full-corpus report and a core-corpus
    report differing by their estimator rather than by their population is the confusion the whole
    weighted path exists to avoid.
    """
    return weighted_percentiles([(value, 1.0) for value in values])


def weighted_percentiles(pairs):
    """The five figures a population row prints, over `(value, weight)` pairs.

    **`min` and `max` are not estimates and are labelled as such by their absence from the error
    table `core.py` prints.** A weighted sample's extremes are the extremes *of the sample*: the
    core corpus keeps one giant in twenty, so its `max` is 76 % under the population's and no
    weighting can repair that — a maximum is one observation and cannot be sampled. They are
    returned because a reader of a full walk wants them; a reader of a sampled one is told by
    `cap.py` that the row is the sample's own, not the population's.
    """
    ordered = sorted(value for value, weight in pairs if weight and weight > 0)
    if not ordered:
        return {}

    return {
        "min": ordered[0],
        # **p10 and p90 are here for the constants rather than for the row.** `Census.Corpus`
        # states the corpus's substep demand as p10/p50/p90 and those figures were transcribed by
        # hand from a walk's output, which is the shape `F14` records going wrong once already —
        # a page saying twenty-four sealed blocks where the dataset said 1,184. A summary that
        # carries them is a summary a test can hold the constants against (`E5`).
        "p10": weighted_percentile(pairs, 0.1),
        "p50": weighted_percentile(pairs, 0.5),
        "p90": weighted_percentile(pairs, 0.9),
        "p95": weighted_percentile(pairs, 0.95),
        "p99": weighted_percentile(pairs, 0.99),
        "max": ordered[-1],
    }


# ---- the per-block cap, and the rule that decides whether it ships -------------------------

# What the mod already accepts as the price of an approximation it ships: refusing 1.15x of the
# substep demand costs this much on the hottest block of a driven census hull over 600 simulated
# seconds, and `C19` closed by keeping the cap and letting `G6` fail rather than paying for
# fidelity nobody can perceive.
# ---- the four predictions, as balance-lab.md registered them before the walk ------------------
#
# **These are the falsifiers, not the decision rule.** A prediction and a decision are different
# questions and the pre-registration keeps them apart: the predictions say what the walk was
# expected to find, and `CAP_ACCEPTED_KELVIN` / `CAP_REFUSED_KELVIN` below say what would be done
# about it. A prediction can be falsified while the decision is unchanged, and that is worth seeing.

#: The predicted capped work p99, and the band outside which the prediction is falsified. The
#: figure was arithmetic — the air walk's rows with each demand replaced by min(demand, 6), times a
#: hull's link-to-node ratio — so *under* the band falsifies it as surely as over.
CAP_BENEFIT_BAND = (1500000.0, 4000000.0)

#: The rate the settle test tolerates, in kelvin a second: 0.25 K over a sixty-second chunk.
#:
#: **A run that satisfies it is not a run that has stopped.** Held for the rest of a 1,800 s
#: scenario, that rate is another 7.5 K — so two arms can both be *settled* by this criterion and
#: still be tens of kelvin apart, because each is still travelling at its own speed. It is used to
#: split the cost figure into the pairs where both arms are at rest and the pairs where they are
#: not; see `at_rest`.
SETTLE_RATE_KELVIN_PER_SECOND = 0.25 / 60.0

#: The predicted delta-peak: p99 under a kelvin, max under ten. Its only evidence was one hull, and
#: the pre-registration says so.
CAP_COST_P99_KELVIN = 1.0
CAP_COST_MAX_KELVIN = 10.0

#: The predicted share of blocks the cap holds back, in air.
CAP_REACH_BAND = (3.0, 10.0)


# ---- the over-budget floor, and the rule that decides whether it ships ------------------------
#
# **the over-budget floor's predictions, which lived only in prose until 2026-08-29.** They were registered in
# balance-lab.md before `CorpusFloorWalk` ran and then scored by hand, so the figures the page
# quotes had no source anything could check them against — which is the failure `E5` is about, and
# the reason `floor.py` exists. Written here beside `C3`'s so the two mechanisms are scored against
# constants under test rather than against sentences (`D3`).

#: The predicted share of blocks the floor holds back: **fewer** than `C3`'s fixed cap of 6 did,
#: because a grid's own budget is a larger grant than 6. The band is open at the bottom and closed
#: at the cap's measured 5.83 %, which is what the prediction named as its falsifier.
FLOOR_REACH_BAND = (0.0, 5.83)

#: The predicted delta-peak: p99 under the 0.03 K this mod already accepts, on the argument that a
#: budget is a gentler cap than 6 and 6 cost 0.024 K at rest. It is the prediction with an argument
#: rather than a measurement behind it, and it failed by three orders of magnitude.
FLOOR_COST_P99_KELVIN = 0.03

#: Substeps of slack before a floored arm counts as having come back *stiffer* than it went in,
#: which the mechanism must never do. The walk's own tolerance, restated here so the report and the
#: walk cannot drift apart (`D3`).
FLOOR_SLACK_SUBSTEPS = 0.5

CAP_ACCEPTED_KELVIN = 0.03

# What the mod already refuses as the price of a default: `MaxSubstepsPerBlock 6` cost this much on
# the worst-placed block when it was first measured, and that is what made it a switch rather than
# a default. See backlog.md, C3.
CAP_REFUSED_KELVIN = 0.6


def cap_decision(p99_delta_kelvin):
    """Whether a per-block cap's measured cost puts it inside what the mod already accepts.

    **Written before the walk that produces the number** (`E1`, `E11`), which is the whole point:
    the two thresholds are not invented for this decision, they are the two calibration points this
    repository already has for what *imperceptible* means, and both were set by decisions taken for
    other reasons. Returns one of `ship`, `switch` or `judgement`.

    `judgement` is not a failure to decide. It is the statement that the number landed between the
    thing the mod accepts and the thing it refuses, where nothing but an argument can settle it --
    and that argument belongs in the open, in the commit that moves a default, rather than in a
    threshold chosen once the data is in.

    Returns None for no measurement, because an unmeasured cost decides nothing (`E8`).
    """
    if p99_delta_kelvin is None:
        return None

    if p99_delta_kelvin <= CAP_ACCEPTED_KELVIN:
        return "ship"
    if p99_delta_kelvin >= CAP_REFUSED_KELVIN:
        return "switch"

    return "judgement"


def delta_peak(control, capped):
    """How far apart two arms of one paired run finished, in kelvin.

    Absolute, because a cap can leave a block cooler as well as hotter: it raises the capacity of
    the stiffest elements, which slows a transient in whichever direction the transient was going.
    The claim being scored is *how wrong*, not *how much hotter*.

    Returns None where either arm has no peak, so a pair that did not both run reports nothing
    rather than a delta against a zero.
    """
    if control is None or capped is None:
        return None

    return abs(capped - control)


def split_arms(rows):
    """`(arms, shipped rows)` for a paired walk, or `([], rows)` for an ordinary one.

    **A paired dataset is scored on the arm that ships, not on both.** Every criterion this file
    reports is about the shipped configuration, and mixing two configurations into one percentile is
    two experiments read as one (`M1`, `P6`). The other arm is not discarded quietly — the caller
    counts and names it, and `cap.py` is the tool that compares them.

    The shipped arm is `cap` 0 or absent, because 0 is what `MaxSubstepsPerBlock` ships at and a
    walk with no such column has only one arm to begin with.
    """
    if not rows:
        return [], rows

    arms = sorted({row.get("cap", "") for row in rows})
    if len(arms) < 2:
        return [], rows

    return arms, [row for row in rows if row.get("cap", "") in ("", "0")]


def at_rest(control_peak_rate):
    """Whether a run had actually stopped moving when it was read.

    **The settle test is an average over a chunk and this is the instantaneous rate at the end**,
    which are different questions: a hull drifting at exactly the tolerated rate passes the first
    for ever. On the cap walk the pairs more than a kelvin apart have a control still moving at a
    median 0.03 K/s — seven times the tolerance — while the population's median is 0.001.

    So a delta read on a travelling pair is *how far apart two arms are on the way somewhere*, and a
    delta read on a resting pair is what the approximation costs at equilibrium. Both are real; they
    are not the same number and a criterion written for one should not be scored on the other
    without saying so.

    Returns None where the dataset does not carry the rate, which is every walk before this one.
    """
    if control_peak_rate is None:
        return None

    return abs(control_peak_rate) < SETTLE_RATE_KELVIN_PER_SECOND


def resolves(runs, threshold_percent):
    """Whether `runs` rows can answer a criterion that turns on `threshold_percent` of them.

    **A criterion stated as a share of the corpus needs a corpus fine enough to state it.** `G1`
    fails above about 1 % of ships critical at idle; on thirteen ships one ship is 7.7 %, so *zero
    critical* and *one per cent critical* are the same reading and the criterion has not been
    answered — it has been asked of a dataset that cannot distinguish its two sides. This is the
    rule rather than a floor typed here, because a floor would be another opinion and this follows
    from the threshold each criterion already states.

    It is a resolution test, not a confidence one: it says the dataset can tell the two sides of
    the line apart, and says nothing about sampling error. A partial walk that passes this is still
    a partial walk (`P1`).
    """
    if not runs or threshold_percent <= 0:
        return False
    return runs * threshold_percent / 100.0 >= 1.0


def too_coarse(runs, threshold_percent, what):
    """The `measured:` line for a criterion its dataset cannot resolve."""
    each = 100.0 / runs if runs else 0.0
    needed = int(-(-100.0 // threshold_percent))
    return (f"{runs:,} {what} cannot answer this: one is {each:.1f} % of the dataset and the "
            f"criterion turns on {threshold_percent:g} %, so both sides of the line read the same. "
            f"It needs {needed:,}.")


# ---- is a dataset the whole corpus (`E4`) -------------------------------------------------------

# Where the corpus lives when nothing says otherwise, as `pace.py` has it and development.md
# documents. Only needed to count blueprints, which is a directory listing.
CORPUS = os.path.expanduser(
    os.environ.get("THERMAL_CORPUS_CONTENT",
                   "~/.local/share/thermal-dynamics/corpus/steamapps/workshop/content/244850"))

# **A walk is allowed to lose a few ships and still be whole.** The blueprint filters reject hulls
# — not vanilla, too small, unparsable — so the finished 2026-08-21 survey recorded 8,132 distinct
# ships over the corpus's 8,144 blueprints, which is 99.9 %, and the `A13` reader fix moved four
# more. Demanding equality would call every complete dataset partial. Ninety-five per cent is far
# above any rejection rate this corpus has shown and far below any interruption worth catching: the
# case `E4` is about read 43 % when it was quoted.
WHOLE_ENOUGH = 0.95


def corpus_population(stated=None):
    """Blueprints the corpus holds, or None where that cannot be established here.

    **None is not zero and is not "whole".** A machine without the corpus checked out can still
    read a dataset, and answering "is this partial" with silence there is the failure `E4` is
    about; the caller says *unknown* instead.
    """
    if stated is not None:
        try:
            return int(stated)
        except (TypeError, ValueError):
            return None

    if not os.path.isdir(CORPUS):
        return None

    # **Recursive, because a workshop item can be a collection.** One id directory can hold several
    # named blueprint folders — `.../372787238/T.N.F. Planetary Dropship 'Ghost' Mk.III (M)/bp.sbc`
    # — and counting one level deep misses fourteen of them and reports a population *smaller than
    # the walk that covered it*, which reads as a dataset more than whole. A tenth of a second.
    count = 0
    for _, _, filenames in os.walk(CORPUS):
        if "bp.sbc" in filenames:
            count += 1

    return count or None


def walked_share(walked, population):
    """What share of the corpus a dataset reached, or None where the population is unknown."""
    if not population:
        return None
    return walked / float(population)


# ---- the core corpus ------------------------------------------------------------------------

CORE_SELECTION = os.path.join(os.path.dirname(os.path.abspath(__file__)), "core-corpus.csv")

# How much of the selection a walk must cover before it is called a core walk. A core walk that
# died halfway is still a core walk and still must not be read unweighted, so this is well under
# one; a full-corpus walk contains every core ship and is separated by the other half of the
# test -- it also contains thousands that are not in the selection.
CORE_ENOUGH = 0.5


def core_selection(path=None):
    """The workshop ids of the core corpus, or an empty set where the selection is not on disk."""
    path = path or CORE_SELECTION
    if not os.path.exists(path):
        return set()
    with open(path) as handle:
        return set(row["workshop_id"] for row in csv.DictReader(handle))


def is_core_walk(walked, path=None):
    """Whether a set of walked workshop ids is a walk of the core corpus rather than the corpus.

    **The failure this prevents is the quiet one.** A core dataset has every column a full one has
    and two thirds of its ships, so read without weights it prints a complete-looking population
    whose giants are outnumbered twenty to one. It is told apart by what it does *not* contain:
    a core walk covers most of the selection and almost nothing outside it, and a full walk covers
    the selection and thousands more.
    """
    selection = core_selection(path)
    if not selection or not walked:
        return False
    inside = len(walked & selection)
    if inside < CORE_ENOUGH * len(selection):
        return False
    return len(walked - selection) < 0.1 * len(selection)
