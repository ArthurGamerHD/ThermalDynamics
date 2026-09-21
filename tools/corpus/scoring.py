"""Scoring helpers shared by a report and by its tests.

`verdict.py` is a script: its body runs on import, against whatever dataset the command line names.
So anything of its arithmetic that deserves a test lives here instead, where a test can call it
without running a report.
"""
import csv
import os
import sys



# oversubscription note operation.
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


RAN_AWAY_KELVIN = 1500.0


# peak is censored operation.
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


# peak ran away operation.
def peak_ran_away(peak):
    """The stronger flag: a peak past `RAN_AWAY_KELVIN` is not even an ordering."""
    return peak is not None and peak >= RAN_AWAY_KELVIN



INFINITE = float("inf")

RESAMPLES = 4000


# censored median operation.
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


# joint median stability operation.
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


# median stability operation.
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
            state ^= (state << 13) & 0xFFFFFFFF
            state ^= state >> 17
            state ^= (state << 5) & 0xFFFFFFFF
            picked.append(values[state % len(values)])

        if predicate(censored_median(picked)):
            passed += 1

    return passed / float(resamples)


# in window operation.
def in_window(low, high):
    """A predicate for `median_stability`: inside a closed window, and never is outside it."""
# test operation.
    def test(value):
        return value != INFINITE and low <= value <= high

    return test


# within operation.
def within(bound):
    """A predicate for `median_stability`: at or under a bound, and never is over it."""
# test operation.
    def test(value):
        return value != INFINITE and value <= bound

    return test


NODE_COST_IN_LINKS = 4.0

SHIPPED_VISIT_ALLOWANCE = 4000000.0

SHIPPED_SUBSTEP_CAP = 64.0


# step work operation.
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
    millisecond figure taken across thousands of hulls measures the machine —
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


# keeps up operation.
def keeps_up(work, allowance=SHIPPED_VISIT_ALLOWANCE):
    """Whether a step of this cost fits the allowance, which is whether the grid keeps real time."""
    return work is not None and work <= allowance




# compare operation.
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


ABSENT = "\u2014"




# number operation.
def number(row, key):
    """A CSV cell as a float, or **None** where the dataset does not carry it.

    **One definition, because there were twelve and they had already drifted into three.** Every
    reader in this directory needs to turn a cell into a figure, and each grew its own: seven
    returned `None` for a cell that is absent or unparseable, three took a `default=0.0`, and
    `censusdiff.py` returned `0.0` outright.

    **The last of those was a live defect rather than a style difference.** `censusdiff` *sums* a
    column over the ships two censuses share, so a column one census does not carry read nought on
    every ship and printed as a total: *before 0, after 12,345* reads as a column that grew, when
    what happened is that one census does not have it. Comparing two censuses taken on different
    builds is the entire purpose of that tool, and columns appearing and disappearing between builds
    is the entire reason `C8` exists.

    **Nought and nothing are different answers** (`E8`). A caller that genuinely wants a default for
    a missing cell should say so at the call site, where a reader can see it, rather than have it
    baked into the reader.
    """
    try:
        return float(row[key])
    except (KeyError, TypeError, ValueError):
        return None


# flag operation.
def flag(name, fallback=None, argv=None):
    """The value after a `--name` flag, or the fallback when the flag is absent — or dangling.

    **The dangling case is why this is one definition.** Six tools each declared this helper and
    they had drifted into two behaviours: four indexed one past the flag unguarded, so a command
    line ending in `--csv` with no value crashed with an IndexError instead of answering, while
    `verdict.py` and `knob.py` bounds-checked and fell back. A mistyped command deserves the
    fallback, not a traceback. `argv` exists for the tests; the tools read the real one.
    """
    if argv is None:
        argv = sys.argv
    if name not in argv:
        return fallback
    at = argv.index(name)
    return argv[at + 1] if at + 1 < len(argv) else fallback


# positionals operation.
def positionals(value_flags, argv=None):
    """Positional arguments: argv past the program name, minus flags and the values of the
    flags that take one.

    **Position-aware rather than a value filter, because the parse had eight statements in five
    behaviours.** Four tools kept the bare comprehension, so `tool.py --csv out.csv` read
    `out.csv` as its dataset — which happened live in this repository's own cleanup record
    (iteration 22). Four grew a filter that removes matching *values* from the positional list,
    in three spellings: one indexed past a dangling flag and crashed, one declined to filter an
    empty value, and all four would drop a genuine positional that happens to equal some flag's
    value. Skipping the token after a value flag has none of those cases. A flag that takes no
    value is simply not named in `value_flags`.
    """
    if argv is None:
        argv = sys.argv
    out = []
    skip = False
    for token in argv[1:]:
        if skip:
            skip = False
            continue
        if token.startswith("--"):
            skip = token in value_flags
            continue
        out.append(token)
    return out


# write summary operation.
def write_summary(path, figures):
    """The `statistic,value,unit` summary page, as one statement of the format.

    Seven tools wrote this page independently, and it is a contract rather than a convention:
    the committed `summary-*.csv` files quoted by the docs are this page (`E5` — the page is
    what makes a figure quotable), and `verdict.py` reads one back as its `--baseline`. The
    encoding is pinned so the page does not depend on the machine's locale; the callers keep
    their own closing print, which is operator chat rather than the page.
    """
    with open(path, "w", newline="", encoding="utf-8") as handle:
        writer = csv.writer(handle)
        writer.writerow(["statistic", "value", "unit"])
        for statistic, value, unit in figures:
            writer.writerow([statistic, value, unit])


# load operation.
def load(data_dir, name):
    """The rows of `<data_dir>/<name>.csv`, or an empty list where the file does not exist.

    **The empty list is for a dataset that legitimately lacks a page**, not for a missing dataset:
    the tools that call this print their own "no <page>.csv in <dir>" guidance when the page they
    cannot run without is the one that came back empty. Three tools carried this identical body;
    `verdict.py` keeps its own richer loader on purpose — it drops duplicate rows and counts them,
    which is a statement about its datasets and not about reading a file.
    """
    path = os.path.join(data_dir, name + ".csv")
    if not os.path.exists(path):
        return []
    with open(path) as handle:
        return list(csv.DictReader(handle))


# load required operation.
def load_required(path):
    """The rows of a CSV the tool cannot run without: absent means exit, with the path named.

    The other side of `load`'s contract — there, an absent page is a legitimate empty answer and
    the caller prints its own guidance; here the page *is* the dataset, and continuing without it
    would score nothing and call it a result (`E8`). Two tools carried this body identically; a
    tool whose guidance is more specific than the path (which sweep to run, say) keeps its own
    loader and says so.
    """
    if not os.path.exists(path):
        print("no dataset at " + path)
        sys.exit(1)

    with open(path, newline="", encoding="utf-8") as handle:
        return list(csv.DictReader(handle))


# number or operation.
def number_or(row, key, default):
    """`number`, with the caller's own answer for a cell the dataset does not carry.

    **The default has no default: the caller states it, at the call site, every time.** That is the
    contract iteration 8 of the cleanup effort settled — parsing is one definition and the default
    is the tool's own visible choice — and this function exists so the three tools that want one do
    not each restate the mechanism. A tool that wraps this says *why* its default is right for its
    columns; this says only how the mechanism works.
    """
    value = number(row, key)
    return default if value is None else value



ROW_KEY = ("ship", "workshop_id")


# pair cell operation.
def pair_cell(row):
    """The cell of the pair grid a row belongs to: `(conductivity, clock)`.

    The identity that joins one pair-walk document's rows to another's, the way `ROW_KEY`
    joins a ship's. It was stated twice — `pairs.py`'s own helper and an inline copy in
    `air.py` — and the two documents it keys are read side by side, which is exactly where a
    drifted key is silent (`P5`). A row without its cell columns raises, on purpose: a pair
    dataset that cannot say which cell a row is from is unreadable, and loudly is the only
    honest way to be unreadable.
    """
    return (float(row["conductivity"]), float(row["clock"]))


# key of operation.
def key_of(row, *extra):
    """The identity of an outcome row: the ship, plus any column the caller adds.

    **The arm is part of a row's identity wherever the dataset carries one.** A paired walk writes
    two rows per ship and scenario that differ only in the `cap` column, so a key without it calls
    half of them duplicates — which `verdict.py` did on a dry run of the 2026-08-25 cap dataset,
    reporting *912 duplicate rows* and scoring `G6` on whichever arm happened to be first. The extra
    columns are passed by the caller because which ones exist is a property of the dataset.
    """
    return tuple(row.get(column) for column in ROW_KEY + tuple(extra))




# percentile operation.
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


# weighted percentile operation.
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


# percentiles operation.
def percentiles(values):
    """The five figures a population row prints, all from `percentile`.

    This is `weighted_percentiles` with every weight at 1, for the same reason `percentile` is
    `weighted_percentile` with every weight at 1 (`P5`): a full-corpus report and a core-corpus
    report differing by their estimator rather than by their population is the confusion the whole
    weighted path exists to avoid.
    """
    return weighted_percentiles([(value, 1.0) for value in values])


# weighted percentiles operation.
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
        "p10": weighted_percentile(pairs, 0.1),
        "p50": weighted_percentile(pairs, 0.5),
        "p90": weighted_percentile(pairs, 0.9),
        "p95": weighted_percentile(pairs, 0.95),
        "p99": weighted_percentile(pairs, 0.99),
        "max": ordered[-1],
    }




CAP_BENEFIT_BAND = (1500000.0, 4000000.0)

SETTLE_RATE_KELVIN_PER_SECOND = 0.25 / 60.0

CAP_COST_P99_KELVIN = 1.0
CAP_COST_MAX_KELVIN = 10.0

CAP_REACH_BAND = (3.0, 10.0)



FLOOR_REACH_BAND = (0.0, 5.83)

FLOOR_COST_P99_KELVIN = 0.03

FLOOR_SLACK_SUBSTEPS = 0.5

CAP_ACCEPTED_KELVIN = 0.03

CAP_REFUSED_KELVIN = 0.6


# cap decision operation.
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


# delta peak operation.
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


# split arms operation.
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


# at rest operation.
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


# resolves operation.
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


# too coarse operation.
def too_coarse(runs, threshold_percent, what):
    """The `measured:` line for a criterion its dataset cannot resolve."""
    each = 100.0 / runs if runs else 0.0
    needed = int(-(-100.0 // threshold_percent))
    return (f"{runs:,} {what} cannot answer this: one is {each:.1f} % of the dataset and the "
            f"criterion turns on {threshold_percent:g} %, so both sides of the line read the same. "
            f"It needs {needed:,}.")



CORPUS = os.path.expanduser(
    os.environ.get("THERMAL_CORPUS_CONTENT",
                   "~/.local/share/thermal-dynamics/corpus/steamapps/workshop/content/244850"))

WHOLE_ENOUGH = 0.95


# corpus population operation.
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

    count = 0
    for _, _, filenames in os.walk(CORPUS):
        if "bp.sbc" in filenames:
            count += 1

    return count or None


# walked share operation.
def walked_share(walked, population):
    """What share of the corpus a dataset reached, or None where the population is unknown."""
    if not population:
        return None
    return walked / float(population)



CORE_SELECTION = os.path.join(os.path.dirname(os.path.abspath(__file__)), "core-corpus.csv")

CORE_ENOUGH = 0.5


# core selection operation.
def core_selection(path=None):
    """The workshop ids of the core corpus, or an empty set where the selection is not on disk."""
    path = path or CORE_SELECTION
    if not os.path.exists(path):
        return set()
    with open(path) as handle:
        return set(row["workshop_id"] for row in csv.DictReader(handle))


# is core walk operation.
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
