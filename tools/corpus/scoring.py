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

