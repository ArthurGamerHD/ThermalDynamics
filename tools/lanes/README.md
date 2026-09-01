# The two test lanes, checked

**`lanes.py` reads a test run and says which classes are in the wrong lane.** The rule it checks is
stated in [tests/README.md](../../tests/README.md#the-two-lanes-and-the-rule-that-sorts-them): a
class costing more than about two seconds of the suite carries `[Trait("speed", "slow")]`, and the
fast lane is everything else.

```bash
dotnet test tests/Thermodynamics.Tests --logger "trx;LogFileName=lanes.trx" \
    --results-directory out/lanes
python3 tools/lanes/lanes.py out/lanes/lanes.trx
```

**This exists because the rule had rotted twice and nothing noticed.** The fast lane measured
**3 m 45 s** on 2026-08-24 against a documented fifteen seconds; nineteen classes were tagged, and it
rotted again inside two days to 37 s with ten more classes past the threshold untagged. The page
that states the rule says plainly why: *nothing checks it*, and the honest check is a class's own
measured cost, which a test inside that class cannot read. It can be read from outside, which is
this — the manual refresh as one command.

**It reports both directions, and only one of them fails the run.**

* **Over the threshold and untagged** is what makes the fast lane slow, and exits non-zero.
* **Tagged and costing almost nothing** is the drift nobody looks for: a class tagged when it *was*
  expensive, left tagged after the thing that made it expensive was optimised away. It costs
  coverage rather than time, so it is reported and does not fail the run — on 2026-08-31 it found
  thirteen such classes, and untagging them put **107 cases** back in the fast lane at **no change to
  its four seconds**.

**Two cautions on reading it.** The durations are per-test and summed, so the total is nearer CPU
time than wall clock — the suite runs collections in parallel, and 274 s of test time is 74 s of
clock. And a class whose cost varies between runs will cross the cheap threshold in one run and not
the next: `ScenarioTests` measured 2.71 s and 0.43 s in two runs an hour apart, so the cheap list is
a prompt to look rather than a verdict.

| File | What it is |
| --- | --- |
| `lanes.py` | The checker. Takes a trx path and, optionally, the test source directory. |
| `test_lanes.py` | What it pins: how a duration is read, how the trait is found above a class, and which direction fails a run. |

## Change log

| Date | Change |
| --- | --- |
| 2026-08-31 | Written, because the lane rule had rotted twice and the page that states it says plainly that nothing checks it. On the first run it found nothing over the threshold untagged — the lane was holding at 4 s — and **thirteen classes tagged while costing under half a second**, tagged when they were expensive and left that way after the performance passes made them cheap. Untagging those put 107 cases back in the fast lane for no change to its four seconds. |
