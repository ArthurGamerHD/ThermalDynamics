# The rules

The standing rules this repository is bound by, in one place, reduced to the principles they
follow from.

They were not invented here. Every one was already written down somewhere — in
[development.md](development.md), in [balance-lab.md](balance-lab.md), in a test class's own
summary, in the header of a tools page, or in a note kept outside the tree entirely — and that
was the problem. A rule stated in the one document its author was writing at the time is a rule
the next reader finds by accident. This page is the canonical statement of each; where another
page argues one at length, this page states it and points there.

## The one observation

Every rule here converts a *silent* failure into a loud one.

That is not a stylistic claim, it is what the evidence behind these rules has in common. A drifted
copy does not throw. A check that judged nothing reports success. A partial sweep prints a clean
number. Two runs stopped on different criteria produce two comparable-looking figures. A renamed
benchmark case arrives as two healthy rows. A parallel run that shares a mutable block instance
finishes without complaint. A block with no exposed face climbs to 342,510 K and looks like
physics. A dead link reads as though somebody checked.

So the test for whether something belongs on this page is not *is it good practice* — it is
**would breaking it be noticed**. If a violation announces itself, the compiler or the suite is
already the rule, and it does not need a sentence here.

## How to read a rule

Each entry is one sentence a change can be measured against, the failure it exists to prevent,
and four fields:

* **Applies to** — the scope. A rule with no scope is a rule that will one day forbid something
  useful. Most errors found while assembling this page were rules stated more broadly than the
  evidence behind them.
* **Checked by** — the test, script or build setting that fails when the rule is broken. A dash
  means nothing checks it; that is not a defect in every case, but an unchecked rule is a hope,
  and marking it says so. *Reported by* is weaker than *checked by* and is written as such: a
  script that prints a verdict and exits zero has told a human something, not enforced anything.
* **Retires when** — the condition under which the rule stops being worth following. Present only
  where such a condition exists. A rule that works around a defect names the fix that would
  delete it.
* **From** — where it is argued in full.

Rules are cited by identifier (`E3`, `M6`) in commit messages and in review. The letter is the
subject — **E**vidence, **M**ethod, **D**efect, **C**ode, **R**epository, **O**perations,
**J**udgement — and is historical: the classification below is orthogonal to it.

## The three categories

| Category | Test for membership |
| --- | --- |
| **Load-bearing** | Remove it and something breaks that cannot be seen breaking: a published claim becomes false, the mod fails in someone else's session, or a day's work is lost. Unconditional inside its scope. |
| **Conditional** | It holds inside a stated scope and is silent outside it. Written as an absolute, each of these forbids ordinary correct work — the scope *is* the rule, and quoting the sentence without it is the failure mode. |
| **Low value** | It is not earning its keep: it costs more than it prevents, it restates rules that already exist, or it works around a defect that is cheaper to fix than to obey. Each names its disposition. |

A rule is not low value merely because nothing checks it. Seventeen of the fifty load-bearing
rules are unchecked and stay load-bearing, because the failure they prevent is severe and silent —
restructuring `Models/` is caught by nothing and costs a re-export of every block model. The
category is about what the rule buys, not about who enforces it.

---

## The principles

Fourteen principles account for every rule on this page. They are the reduction: if the rule list
were lost, these are what would have to be re-derived, and each rule below is one of them applied
to a specific artefact. Six rules have been added since the reduction and every one of them landed
under a principle that already existed, which is the only evidence available that the fourteen are
the right fourteen — see [testing the reduction](#testing-the-reduction).

| # | Principle | Rules |
| --- | --- | --- |
| **P1** | **A figure carries its scope.** A number without its population, its basis and its clock is not a number; a sample stands for a population only by a written rule. | `E2` `E3` `E6` `M10` `M11` `J3` |
| **P2** | **What the instrument could not see is part of the result.** Censoring, the noise floor, an unfinished sweep and a check that judged nothing are all the same failure: reading a blind spot as a value. | `E4` `E8` `E9` `M4` `M5` |
| **P3** | **The claim is fixed before the data and corrected in place after.** A criterion that can move once the numbers are in is not a criterion; a finding that is corrected somewhere other than where it was published is not corrected. | `E1` `E10` `E11` `M9` `D5` `R12` |
| **P4** | **Nothing is its own oracle.** A test that asks the model the same question twice agrees with whatever the model does; a harness fault looks exactly like physics. | `E7` `D1` `D4` `D7` `D8` |
| **P5** | **One definition, and at least one consumer.** Two definitions drift, and the drift is silent in both directions; zero consumers means the thing does not exist however well it is written and tested. | `E5` `D2` `D3` `M6` `R7` `R8` `R9` `R10` `R11` `R13` `R14` |
| **P6** | **A comparison holds everything but the subject equal.** Two numbers are comparable only when the stop criterion, the machine, the key and the baseline were the same. | `M1` `M2` `M3` `M7` |
| **P7** | **The game is the authority.** The local build and the suite are an approximation of a compiler and a whitelist neither of them can see, and where the game already answers a question — what is sealed, what is destroyed — the mod reads that answer instead of forming its own. | `C1` `C2` `C3` `C9` `C10` |
| **P8** | **Off means off, and costs nothing.** A mechanism nobody is using must cost nothing, and the way to turn it off must be unambiguous. | `C4` `C7` `C8` |
| **P9** | **The core is a library the game happens to call.** That is what makes it testable in seconds, profilable, drivable by other mods and portable to another engine. | `C5` `R9` |
| **P10** | **The solver's three invariants are the definition of correctness.** Order independence, energy conservation, boundedness — everything else is tuning. | `C6` |
| **P11** | **The repository is the publish.** Everything committed here reaches the workshop, so what must not ship must not be here. | `R2` `R3` |
| **P12** | **Do not edit what this repository cannot regenerate.** Model binaries, workshop identity and vendored code have their source of truth outside this tree. | `R4` `R5` `R6` |
| **P13** | **A long run is designed for its own death.** It will be killed — by the OOM killer, a timeout, a mistake or a power cut — so cap it, resume it, and never let a timer guess its duration. | `O1` `O2` `O3` `O5` |
| **P14** | **Unobservable fidelity is cost.** Take the cheap form where the difference cannot be perceived, say what it gives up, and write the price down. | `D6` `M8` `O4` |

### Testing the reduction

Two questions decide whether fourteen is the reduction rather than a number that happened. Is each
principle **necessary** — does it generate a rule none of the others generates? And is the set
**sufficient** — does every rule follow from one of them?

Necessity was tested by attempting the two mergers that look available from the table.

* **P1 with P2.** Both say a figure is incomplete without its extent, and a merged principle would
  read *a measurement carries what it covered and what it could not see*. Rejected: the failure
  modes are opposites. P1 fails by mislabelling a scope somebody knew about — the wrong population,
  the wrong clock. P2 fails by reading a scope nobody looked at — a censored tail, a noise floor, a
  sweep that stopped, a check that judged nothing. Merged they make an eleven-rule principle whose
  second half reads as a qualification of the first, and the second half is the one that gets
  forgotten.
* **P7 with P11 and P12.** All three are about authority sitting outside this repository: the
  game's compiler, the workshop the repository publishes to, the model exporter and the upstream
  authors. Rejected: the shared idea generates no rule. What is actionable is the difference —
  *the game decides*, *everything here ships*, *do not touch what you cannot rebuild* — and a
  principle that has to be unpacked into three before it can be applied is a heading, not a
  principle.

One merger did succeed, and it came from the completeness audit rather than from the table. `C9`
and `C10` arrived needing *the game decides what is sealed* and *the server decides what is
destroyed*. Neither is about compiling, and both are the same idea P7 already held, so P7 widened
from **the game is the authority on what compiles** to **the game is the authority**. That is the
one piece of evidence available that the fourteen are the right fourteen: six rules were added to
this page after the reduction, from four sources that had never been read for rules, and every one
of them landed under a principle that already existed.

Sufficiency was tested by reading every markdown file in the tree for a sentence a change could
violate, and matching each against a rule. Most matched. Six did not:

| Added | Was stated only in |
| --- | --- |
| `C9` the game's answer is read, never overridden | [document-of-intent.md](document-of-intent.md#what-this-mod-deliberately-is-not) and `RoomPressure`'s summary |
| `C10` the server is authoritative over damage | [document-of-intent.md](document-of-intent.md#what-the-mod-owes-a-multiplayer-client) and one comment in `ThermalGridSimulation` |
| `D7` measure before replacing what the counts accuse | [document-of-intent.md](document-of-intent.md#what-correctness-means), as one of two standing instructions from the developer |
| `D8` an optimisation is pinned against the code it replaced | [tests/README.md](../tests/README.md), as the title of a row in the suite index |
| `R12` a page states its scope, describes the present, and logs its changes | [development.md](development.md#documentation-conventions) |
| `R13` a standing rule is stated here once, and argued elsewhere | [development.md](development.md#documentation-conventions), as an aside inside `R12`'s convention |

Two things that audit cannot do. It reads the tree, so a rule that lives only in someone's head is
invisible to it — which is how the operational rules `O1` to `O5` came to be written down late, and
they were recovered from notes rather than from the repository. And it cannot tell a rule from a
finding stated forcefully: the pages are full of bolded sentences that are conclusions about heat
rather than constraints on work, and the test applied to each was the one at the top of this
page — *would breaking it be noticed?*

Three rules from the previous revision are gone as rules, absorbed into the principles above:
they stated a premise rather than something a change could violate. `R1` is P11, `J1` is P14, and
`J2` was a four-part slogan whose checkable halves are `C4`, `C5`, `C7` and `R9`. They are listed
under [low value](#low-value) so a citation to them does not dangle.

---

## The index

| # | Rule | Category | From | Checked by |
| --- | --- | --- | --- | --- |
| **E1** | Criteria before data | load-bearing | P3 | reported by `verdict.py` |
| **E2** | Measure the population, not the specimen | conditional | P1 | `CensusFidelityTests` |
| **E3** | Name the population and the basis of every figure | load-bearing | P1 | — |
| **E4** | A partial sweep is not a result | load-bearing | P2 | — |
| **E5** | Every figure on a page comes from the dataset the page is about | load-bearing | P5 | `EveryQuotedDatasetCountIsCurrent` |
| **E6** | Pair the terms before dividing | conditional | P1 | — |
| **E7** | Check a claim against something that is not the model | load-bearing | P4 | `LegacyFormulas` `Reference` `DumpAuditTests` |
| **E8** | A check that judged nothing has not passed | load-bearing | P2 | `CorpusSurvey` |
| **E9** | Read a censored column as censored | load-bearing | P2 | reported by `verdict.py` |
| **E10** | Correct a published finding in place | load-bearing | P3 | — |
| **E11** | A criterion changes only in the open | load-bearing | P3 | — |
| **M1** | Compare only runs that were stopped the same way | conditional | P6 | `SunlightPanelWalk` |
| **M2** | Parallel for values, linear for durations | load-bearing | P6 | `ParallelAndLinearProduceTheSameMatrix` |
| **M3** | The run is the unit of parallelism | conditional | P6 | `ParallelAndLinearProduceTheSameMatrix` |
| **M4** | Keep the fastest of N, and publish the noise floor | load-bearing | P2 | `PerformanceReportTests` |
| **M5** | A figure inside the noise floor has not moved | load-bearing | P2 | `PerformanceReportTests` |
| **M6** | The case key is the contract | load-bearing | P5 | `BenchmarkBaselineTests` |
| **M7** | Measure a pass against its own start | load-bearing | P6 | — |
| **M8** | A scenario earns its place by answering what nothing else answers | load-bearing | P14 | — |
| **M9** | A scenario's conclusion is pinned so it cannot invert | load-bearing | P3 | `ScenarioClaimTests` |
| **M10** | Specimens are chosen by coverage, and every pick names its rule | load-bearing | P1 | `panel.csv` carries the rule |
| **M11** | The synthetic ship is refreshed against the field | load-bearing | P1 | `CensusFidelityTests` |
| **D1** | Disbelieve a plausible number | load-bearing | P4 | `ScreeningTests` `LabInvariantTests` |
| **D2** | Hunt for what is built, documented and reached by nothing | load-bearing | P5 | `SimCommandTests` `DocumentationTests` |
| **D3** | Where one thing exists twice, a test compares the two | load-bearing | P5 | `BothParsersKnowTheSamePropertyNames` |
| **D4** | Test the state before the first step, and the artefacts the mod writes | load-bearing | P4 | `DumpAuditTests` `FieldDumpTests` |
| **D5** | A known defect is pinned by a test, not only described | load-bearing | P3 | `CorpusSurvey` |
| **D6** | A deliberate simplification is recorded as a limit | load-bearing | P14 | — |
| **D7** | Measure before replacing what the counts accuse | conditional | P4 | — |
| **D8** | An optimisation is pinned against the code it replaced | load-bearing | P4 | `SolverAb` and the five bit-identity suites |
| **C1** | C# 6 only | load-bearing | P7 | `LangVersion` on the core project |
| **C2** | The whitelist covers types the local build accepts | load-bearing | P7 | — |
| **C3** | Target `net48`, and never reference the native assembly | load-bearing | P7 | the build |
| **C4** | Nothing allocates on the stepping path | load-bearing | P8 | `bench report` |
| **C5** | The core speaks no game type | load-bearing | P9 | `CoreIsolationTests` |
| **C6** | The solver's three invariants hold | load-bearing | P10 | `ConductionTests` `StabilityTests` `ConductionClampGateTests` |
| **C7** | Every mechanism has a switch that removes its own cost | load-bearing | P8 | `FeatureToggleTests` |
| **C8** | Absent and empty mean the same thing | load-bearing | P8 | — |
| **C9** | The game's own answer is read, never overridden | load-bearing | P7 | `RoomPressureTests` |
| **C10** | The server is authoritative over damage | load-bearing | P7 | — |
| **R1** | *The repository is the mod folder* | low value | P11 | absorbed into P11 |
| **R2** | Build output and the corpus live outside it | load-bearing | P11 | `Directory.Build.props` |
| **R3** | No credential is written into the tree | load-bearing | P11 | `CredentialScanTests` |
| **R4** | `Models/` is not restructured | load-bearing | P12 | — |
| **R5** | The workshop identity files are not regenerated | load-bearing | P12 | — |
| **R6** | Vendored code is replaced, never edited | load-bearing | P12 | — |
| **R7** | Every document is indexed, and every link resolves | load-bearing | P5 | `DocumentationTests` |
| **R8** | Every setting is documented, wired, and read by something | load-bearing | P5 | `ConfigurationDocTests` `SettingsWiringTests` |
| **R9** | The API page is part of the contract | load-bearing | P5 P9 | `EveryModApiEntryIsDocumented` |
| **R10** | Every test class says what it is for | load-bearing | P5 | `EveryTestClassSaysWhatItIsFor` |
| **R11** | A check is cited only if it runs | load-bearing | P5 | `EveryCheckCitedByTheRulesPageResolves` |
| **R12** | A page states its scope, describes the present, and logs its changes | load-bearing | P3 | partly |
| **R13** | A standing rule is stated here once, and argued elsewhere | load-bearing | P5 | `EveryRuleCitedByAPageExists` |
| **R14** | A comment names something that is there | load-bearing | P5 | `NoDocCommentDescribesSomethingThatIsNotThere` |
| **O1** | Every long run is capped | load-bearing | P13 | — |
| **O2** | A run with no bounded duration gets no hang timeout | conditional | P13 | — |
| **O3** | Long sweeps resume, and progress is measured in bytes | load-bearing | P13 | — |
| **O4** | Corpus walks run alone | low value | P14 | `xunit.runner.json` |
| **O5** | A lab streams, and counts what it did not measure | load-bearing | P13 | — |
| **J1** | *Game mod first* | low value | P14 | absorbed into P14 |
| **J2** | *Light, isolated, tested, open* | low value | — | absorbed into `C4` `C5` `C7` `R9` |
| **J3** | The corpus filters are strict, and their cost is recorded | conditional | P1 | `BlueprintTests` |

Fifty-one load-bearing, seven conditional, four low value. Sixteen of the load-bearing rules have
no automated check, and say so.

---

## Load-bearing

### P1 — A figure carries its scope

#### E3 — Name the population and the basis of every figure

**Say which population a statistic is over, and put the basis in a column rather than a
footnote.**

The corpus is 8,142 hulls, 8,132 distinct name-and-id pairs and 8,054 distinct names, and a
statistic over one of the three is not a statistic over another. The same applies to units and
clocks: substep demand is proportional to step length, so a figure taken at `Frequency 4` is
double the same figure at `Frequency 8`, and seconds in a scenario table are simulated seconds
rather than seconds a player waits.

*Applies to:* every reported figure.
*Checked by:* — nothing checks that a page names its population.
*From:* [balance.md](balance.md), [benchmarks.md](benchmarks.md).

#### M10 — Specimens are chosen by coverage, and every pick names its rule

**A panel is cut by taking the extremes of every feature axis and then the ship furthest from
everything already taken, and the rule that selected each ship is written beside it.**

Picking the most popular ships yields near-duplicates from the densest part of the design space
and no examples of anything unusual, which is backwards: the interior of a cluster is predictable
from its edges, and a balance figure fails at the edges first. Writing the rule down is what lets
a panel be audited and rebuilt rather than trusted.

*Applies to:* any reduced sample standing in for the corpus. The claim that a panel reproduces
the corpus is a hypothesis and is still unchecked.
*Checked by:* `panel.csv` carries a `rule` and a `why` column per ship.
*From:* [balance-lab.md](balance-lab.md), [tools/corpus/README.md](../tools/corpus/README.md).

#### M11 — The synthetic ship is refreshed against the field

**When a new dump arrives, the census tiers and the field observations beside them are both
updated.**

Every benchmark builds from a measured block population rather than an invented one, because a
step takes as many substeps as the stiffest block needs and a hull's cost is therefore set by its
lightest block. The old invented mix asked for three substeps where a real ship asks for twenty
to thirty, which made every scale figure about six times too cheap.

*Applies to:* `Census` and `Census.Field`.
*Checked by:* `CensusFidelityTests`.
*From:* [benchmarks.md](benchmarks.md#keeping-it-honest), [tests/README.md](../tests/README.md).

### P2 — What the instrument could not see is part of the result

#### E4 — A partial sweep is not a result

**A corpus run is quoted whole, or quoted with the words "partial" and the count attached.**

The corpus is walked largest-first, so an interrupted run has measured capital ships and nothing
else. At 500 of 8,142 ships the load criterion read 95 % against a true 75 %, the affordability
criterion read *failing* when it passes, and the medians were three times high.

*Applies to:* anything drawn from a sweep that has not finished.
*Checked by:* — the walk records its own progress, but nothing stops a partial file being read.
*From:* [balance.md](balance.md).

#### E8 — A check that judged nothing has not passed

**Every invariant reports how many cases it judged, and a run where a claim was never evaluated
fails rather than passes.**

An equilibrium gate that admits nothing is silent. One was written that admitted zero of 88
ships, and the guard on the judged count is what found it. The same shape appears in the
documentation tests, which assert they saw more than a thousand test names before believing they
found none missing.

*Applies to:* every invariant, audit and survey.
*Checked by:* `CorpusSurvey` — its balance claim asserts a non-zero judged count, and the
population claims fail when a reader subsystem goes dark. Not by `verdict.py`, which prints `?`
for a criterion its dataset cannot answer and exits zero either way; that is a report to a human,
not a gate.
*From:* [balance-lab.md](balance-lab.md).

#### E9 — Read a censored column as censored

**The lab never destroys an overheating block, so a peak temperature above critical is not a
prediction.**

Anything past critical keeps generating for the rest of the clock; the 541,648 K reading is
1,800 s of an undamped source, not a temperature the mod can reach. Peaks above about 1,500 K say
*ran away* and nothing finer, and population statistics on the right tail describe the harness.
Crossing times and shares are decided before the divergence and are unaffected — the findings
rest on those.

*Applies to:* every peak, p95, p99 and maximum drawn from a lab run.
*Checked by:* reported by `verdict.py`, which prints the censored share beside the criteria.
*From:* [known-issues.md](known-issues.md#deliberate-limits).

#### M4 — Keep the fastest of N, and publish the noise floor

**Every timed case is measured several times and the fastest kept, and every report opens with
the spread between fastest and slowest of a repeated case.**

Timing noise is one-sided: a sample is the true cost plus whatever else the machine was doing, so
averaging it in measures the operating system. Without the floor printed beside them, several
features cost less than the spread and would read as making the solver faster.

*Applies to:* every millisecond in a report.
*Checked by:* `PerformanceReportTests`.
*From:* [benchmarks.md](benchmarks.md).

#### M5 — A figure inside the noise floor has not moved

**A change to a number that was never measurable is not a regression and is not an improvement.**

Making the solver leaner lifts several feature costs out of the noise at once, and each arrives in
a diff as a large percentage increase over a number that never meant anything. The first run of
the comparison against a real optimisation reported sixteen regressions, fifteen of them figures
that had previously been unmeasurable.

*Applies to:* every benchmark comparison.
*Checked by:* `PerformanceReportTests` — the comparison declines to count them.
*From:* [benchmarks.md](benchmarks.md#reading-a-comparison).

### P3 — The claim is fixed before the data and corrected in place after

#### E1 — Criteria before data

**Write the criterion, and the number that fails it, before the run that tests it.**

Criteria invented after seeing the data are criteria fitted to the data. G1–G6 were written
before a corpus existed, which is what makes a run that fails them a finding rather than an
excuse to move a threshold.

*Applies to:* population and balance claims. A synthetic rig may be built to answer a question
that arose this morning; a criterion may not.
*Checked by:* reported by `tools/corpus/verdict.py`, which evaluates the criteria as written and
names the ones a dataset cannot answer instead of skipping them. It prints; it does not fail.
*From:* [balance-lab.md](balance-lab.md).

#### E10 — Correct a published finding in place

**When a finding turns out to be wrong, the correction goes where the finding is, in its own
commit, naming what was wrong and what the number actually is.**

The alternative is a document that is right on its newest page and wrong on the one a reader
found first. Two corrections in an earlier pass followed this shape: a ratio that was not a ratio,
and a verdict script quoting a figure it could compute. This page is held to it too, and its own
corrections are in the [change log](#change-log).

*Applies to:* anything already written down, including this page.
*Checked by:* — judgement.
*From:* the commit history.

#### E11 — A criterion changes only in the open

**A balance criterion or a threshold may be changed, but only in a commit that says why, keeps
the old value visible, and does not carry the run that failed it.**

`E1` is worth nothing without this one. A criterion written before the data and then quietly
relaxed the week a run fails it has been fitted to the data by a slower route, and the commit
history is the only place that is visible. Separating the two commits is what makes the change
reviewable: a criterion moved for a stated reason is a decision, and a criterion moved in the same
diff as the numbers it now admits is a result.

*Applies to:* G1–G6, every threshold in `verdict.py` and in the lab invariants, and every bound
in a pinned defect test.
*Checked by:* — judgement. A commit touching both a threshold and a dataset is the shape to look
for in review.
*From:* the gap it closes is `E1`'s.

#### M9 — A scenario's conclusion is pinned so it cannot invert

**Every scenario states a conclusion in its summary line, and a test asserts that conclusion
cannot quietly flip.**

A scenario whose headline can invert unnoticed is worse than none, because it reads like
evidence.

*Applies to:* every scenario in the harness.
*Checked by:* `ScenarioClaimTests`, `SelfShadowScenarioTests`, `CoolingScenarioClaimTests`.
*From:* [development.md](development.md).

#### D5 — A known defect is pinned by a test, not only described

**A defect that is understood and not yet fixed gets a test asserting the behaviour it currently
has, so that fixing it fails a test rather than moving a number nobody is watching.**

Bound rather than asserted to zero where the scale is what matters: the sealed-block ratio fails
on a regression to the earlier scale without letting one unexplained ship hold the suite hostage.

*Applies to:* every entry in the defect list that is not fixed in the same pass.
*Checked by:* the pinned tests themselves. The sealed-block bound now lives in `CorpusSurvey`
rather than in the standalone walk that used to carry it.
*From:* [tests/README.md](../tests/README.md), [known-issues.md](known-issues.md).

#### R12 — A page states its scope, describes the present, and logs its changes

**Every page opens by saying what it covers and what it does not, its body is in the present tense,
and the only place a revision is recorded is a `## Change log` at the end.**

A page that narrates what was tried reads as a description of the code long after it has stopped
being one, and nothing about it looks wrong — the prose is accurate about a repository that no
longer exists. Present tense is what makes that drift visible, and the change log is where `E10`'s
correction goes: a finding corrected somewhere other than where it was published is not corrected.
A measurement's before-and-after table is present-tense evidence and stays; the story around it
does not. A page that cannot state its scope in a paragraph is two pages.

*Applies to:* every markdown file in the tree, this one included.
*Checked by:* `EveryPageHasAChangeLog` and `EveryDocumentIsInTheIndex`. The scope paragraph and the
present tense are judgement — nothing reads prose for tense.
*From:* [development.md](development.md#documentation-conventions).

### P4 — Nothing is its own oracle

#### E7 — Check a claim against something that is not the model

**A test that asks the model the same question twice agrees with whatever the model happens to
do.**

So the independent oracles are kept and used: `LegacyFormulas` is a verbatim copy of the original
mod's equations, `Reference` works sun visibility out the slow obvious way by intersecting rays
with every cell, and `benchmarks/field-dump/` is three CSVs a real world produced. The
self-shadow scenario's claim test recomputes its shares by ray-versus-cube rather than by calling
the model.

*Applies to:* any test whose subject is the model's own arithmetic.
*Checked by:* `DumpAuditTests`, `SelfShadowScenarioTests`, and the oracles themselves.
*From:* [tests/README.md](../tests/README.md).

#### D1 — Disbelieve a plausible number

**A harness fault looks exactly like physics, so a figure with no prior is investigated before it
is explained.**

Three faults were found this way and not one was caught by a test: a gyro's `ForceMagnitude` read
as thrust rather than torque, turning one gyro into 33.6 MW and a hull into 342,510 K; a
definition that declares no mount points read as a block that mounts nowhere, giving batteries no
conduction and no exposure; and a store counted as charging and discharging at once. Each was a
definition read wrongly, so **a definition misread is the first hypothesis, not the last**. A
block with no exposed face and no conduction has nowhere to send its heat and climbs without
symptom — that shape is a harness fault until proven otherwise.

*Applies to:* any figure from the harness that nobody can bracket in advance.
*Checked by:* `ScreeningTests` holds the standing versions of that disbelief —
`OnlyAThrusterCarriesThrust`, `ABlockThatDeclaresNoMountPointsStillConducts`,
`AStoreIsNotBothChargingAndDischarging` — and `LabInvariantTests` holds the units guard, that no
definition claims a preposterous amount of power.
*From:* [balance-lab.md](balance-lab.md).

#### D4 — Test the state before the first step, and the artefacts the mod writes

**Both are where a fault survives longest, because both look like output rather than behaviour
and neither changes a temperature.**

Three faults found in one pass over a field dump had exactly that shape: a grid's substep demand
was conduction-only until it had stepped, a grid's one-off build was charged to no row in the
cost table, and a block type's peak temperature could sit below its own maximum. Every test
measures a grid *after* stepping it, and nothing read the report the mod writes about itself.

*Applies to:* new instrumentation, new report rows, and anything read outside a step.
*Checked by:* `DumpAuditTests`, `FieldDumpTests`.
*From:* [tests/README.md](../tests/README.md).

#### D8 — An optimisation is pinned against the code it replaced

**A change whose stated purpose is cost rather than behaviour is asserted bit-identical to what it
replaced, on a fixture that proves it exercised something.**

The old code is the only oracle that answers the question actually being asked — *did this change
anything?* — and a benchmark cannot answer it, because a figure that moved and a figure that broke
look the same. Five suites are held to this shape: the precomputed environment rows, the fixed
source row, the gated conduction clamp, the batched diagnostics and a step spread across frames.

The second half is the half that is easy to leave out. `Hulls.Driven` asserts its own
postconditions — producers running, temperatures spanning at least 100 K — because two runs of a
hull that built nothing agree perfectly, and agreement is the whole assertion. That is `E8` at the
one place where a passing comparison is least trustworthy.

*Applies to:* any rewrite offered as a saving.
*Checked by:* `SolverAb.AssertIdentical` and the suites that call it —
`PrecomputedEnvironmentTests`, `FixedSourceRowTests`, `WattsClearFusionTests`,
`ConductionClampGateTests`, `DiagnosticBatchingTests`, `SpreadStepTests` — over `Hulls.Driven`,
which checks the fixture it built.
*From:* [tests/README.md](../tests/README.md), [benchmarks.md](benchmarks.md).

### P5 — One definition, and at least one consumer

#### E5 — Every figure on a page comes from the dataset the page is about

**No count, share or total is written into prose by hand when the data behind it can be read.**

The balance bench stated 36 panel ships for as long as the panel had 50, because the header had
been typed rather than generated. A reader who catches one wrong count stops believing the right
ones.

*Applies to:* every page that reports on a dataset, including this one's own claims about the
suite.
*Checked by:* `EveryQuotedSuiteSizeIsCurrent` for a stale suite size and
`EveryQuotedDatasetCountIsCurrent` for a count of the panel, of the values authored in `Cubes.xml`
or of the suite's classes; the pack scripts generate the figures the report pages carry. A count of
a dataset that is not in this repository — the corpus, the game's own definitions — is still caught
by nobody.
*From:* [tools/corpus/README.md](../tools/corpus/README.md).

#### D2 — Hunt for what is built, documented and reached by nothing

**The highest-yield defect class here is a thing that exists, is described accurately, and is
called by nothing.**

The code is careful and the prose is careful, so the defects are not wrong logic. Four turned up
in one pass: a 297-line lab answering an open balance criterion with no command and no test; a
documented and fully tested block property the in-game parser had no name for, so it worked in
every test and in no world; six harness commands in no help text; and a Save and a Reset button
the menu had not had for months. Two more turned up in this one: `BlockThermalProperties.Validate`
and `ThermalSettings.Validate` produce diagnostics no caller under `Data/Scripts` asks for, and
five retired lab invariants sit in the suite with no caller at all. Anything declared once and
mentioned nowhere else is the place to look, and a name-frequency scan finds them in seconds —
though it gives a false negative on a misspelled declaration.

*Applies to:* every pass over the repository.
*Checked by:* `SimCommandTests` — including `EveryLabThatProducesAReportIsReachable` —
`DocumentationTests`, and `EverySettingIsReadBySomething`. Nothing yet scans the test project for
an uncalled `internal static` invariant, which is how the four above survived.
*From:* the defect record kept with the session notes; [backlog.md](backlog.md) A19.

#### D3 — Where one thing exists twice, a test compares the two

**One file format read by two parsers will drift, and the drift is silent in both directions.**

This repository has two of most things by necessity: two definition parsers, one that needs a
session and one that must not; a harness and a game adapter; a page and the code it describes; a
committed baseline and the report that produces it. Each pair can drift and neither half looks
wrong on its own.

*Applies to:* any name list, format or figure that exists in two places.
*Checked by:* `BothParsersKnowTheSamePropertyNames`, `DefinitionFileTests`,
`CriticalTemperatureMirrorTests`, `BenchmarkBaselineTests`, and the documentation tests.
*From:* [known-issues.md](known-issues.md).

#### M6 — The case key is the contract

**A benchmark case is renamed only deliberately, because a rename turns one regression into two
unrelated rows.**

The report is keyed on `section / case / metric`, and that key is the only thing joining this
run to the last. A silently renamed case is the one way a benchmark suite lies about itself.

*Applies to:* the performance report, and by extension any keyed artefact that is diffed —
including the corpus CSVs.
*Checked by:* `BenchmarkBaselineTests` compares the committed baseline's keys against a fresh
run's; `EveryFigureHasAUniqueKey` guards the keys themselves.
*From:* [benchmarks.md](benchmarks.md).

#### R7 — Every document is indexed, and every link resolves

**A page under `docs/` is listed in the README's table, every relative link points at a file that
exists, every anchor names a heading that exists, and no page cites a test by a name it no longer
has.**

A dead link is worse than a missing one: it reads as though somebody checked. A rename left nine
dead links behind, three pages had fallen out of the index entirely, and three more pointed at
scripts that stopped existing when this became its own repository.

*Applies to:* every markdown file in the tree.
*Checked by:* `DocumentationTests` — `EveryDocumentIsInTheIndex`, `EveryRelativeLinkResolves`,
`EveryAnchorNamesAHeading`, `NoPageNamesATestThatHasBeenRenamed`, `EveryTestClassIsInTheIndex`.
*From:* [tests/README.md](../tests/README.md).

#### R8 — Every setting is documented, wired, and read by something

**A setting exists in `Settings.cs`, in the reference table, in the menu, and in code that reads
it — or it does not exist.**

Twenty-one settings had accumulated that were in the code and not the reference. The opposite
also happened: a toggle with a menu entry, a config field, a label and no reader at all.

*Applies to:* every world setting.
*Checked by:* `ConfigurationDocTests` and `SettingsWiringTests`, including
`EverySettingIsReadBySomething`, `NoTwoSettingsShareAProtoMemberNumber` and
`EverySettingIsClampedOrDeliberatelyNot`.
*From:* [configuration.md](configuration.md).

#### R9 — The API page is part of the contract

**The mod API is a dictionary of strings to delegates, so a name that has moved is discovered at
run time, in someone else's session, by a cast that fails.**

`api.md` is the only place those names and signatures are written down for a reader, which makes
it part of the contract rather than a description of one.

*Applies to:* every entry in the delegate table, and the version number beside it.
*Checked by:* `EveryModApiEntryIsDocumented`.
*From:* [api.md](api.md).

#### R10 — Every test class says what it is for

**In its own summary, not in an index — and the index says only where to look.**

*Applies to:* every test class.
*Checked by:* `EveryTestClassSaysWhatItIsFor`, `EveryTestClassIsInTheIndex`.
*From:* [tests/README.md](../tests/README.md).

#### R11 — A check is cited only if it runs

**A page names a test as the thing that enforces a rule only when that test is live: an
executable case, or a build setting the compiler reads. A superseded check is deleted, not left
in the tree for a page to point at.**

An unchecked rule marked as unchecked is honest. An unchecked rule marked as *checked* is worse
than either, because it ends the search. Three citations on the previous revision of this page
failed that test — a check named after a file rather than a class, three invariants attributed to
the wrong class, and a defect bound cited to a method that stopped running when it was demoted to
an uncalled helper. All three read as enforcement and none of them was.

`NoPageNamesATestThatHasBeenRenamed` does not catch this. It fires only when a cited name shares
a three-word prefix with a real test, and it only looks at names of four camel-case words or more,
so a two-word citation like a file name is invisible to it, and a name that never existed at all
is invisible unless it happens to look like a rename.

*Applies to:* the *Checked by* field on this page, and any page that names a test as evidence.
*Checked by:* `EveryCheckCitedByTheRulesPageResolves`, which resolves every name in a *Checked by*
field to a live test case, a class holding one, a type or member the code refers to somewhere other
than its own declaration, or a file that exists. It would have caught all three stale citations.
Two limits it states rather than hides: it reads this page's fields only, not every page that names
a test, and *referred to somewhere* is not *reached at run time*, so a helper called only by another
dead helper still resolves.
*From:* the three stale citations above, generalised.

#### R13 — A standing rule is stated here once, and argued elsewhere

**A page that depends on a rule cites it by identifier and does not restate it. This page holds the
sentence; the page that has the evidence holds the argument.**

This is `D3` applied to prose, and it is why this page exists. A rule written into the one document
its author happened to be writing is a rule the next reader finds by accident: eleven pages each
stated one in passing, several operational rules lived only in notes outside the tree, and two
notes contradicted each other outright over `--blame-hang-timeout` because neither knew the other
was a rule. Restating a rule in two places is the same defect in slower motion — both copies read
as authoritative, and the drift between them is silent in both directions.

*Applies to:* every page, and every rule.
*Checked by:* `EveryRuleCitedByAPageExists` reads each page's banner and fails on a citation this
page does not state, and on a banner that cites nothing. `TheRulesPageIndexesEveryRuleItStates`
holds the page's own halves together: a rule stated here is indexed here and follows from a
principle, unless it is filed as low value. Neither can see a rule that has never been written
down, which is what the audit is for.
*From:* [development.md](development.md#documentation-conventions); the assembly of this page.

#### R14 — A comment names something that is there

**A comment describes a definition that exists, in the space a name needs; where it would argue,
it names the page that argues.**

A member moved or deleted leaves its comment behind, and the comment comes to rest on whatever is
below it. Nothing complains: it compiles, it reads as documentation, and it describes a different
thing or no thing at all. Twenty-three had accumulated, several still describing code that had been
replaced — a coolant advection scheme that is now a rotation, a setting renamed into a different
unit, a per-profile definition overlay that no longer exists. That is `D2`'s defect class in prose:
written, accurate-looking, and attached to nothing.

The length limit is the other half and it is what prevents the first. A comment that argues a topic
has to be maintained against a subject it does not sit next to, so it rots where a name does not —
and the argument belongs on a page, where it can be found by somebody who is not already looking at
that line.

*Applies to:* every comment under `Data/Scripts` and `tests`, excluding vendored code.
*Checked by:* `NoDocCommentDescribesSomethingThatIsNotThere`, which fails on two `<summary>` blocks
in a row — the signature of an orphan, since C# allows one per member. It states rather than hides
its limit: an orphan landing somewhere with no comment of its own is invisible to it. Nothing checks
the length, which is judgement.
*From:* [document-of-intent.md](document-of-intent.md#what-a-code-comment-is-for).

### P6 — A comparison holds everything but the subject equal

#### M2 — Parallel for values, linear for durations

**A settling temperature is a pure function of a ship and a scenario, so it may be measured
alongside thirty others. A duration may not.**

The moment a result is a time, every other core is noise — cache pressure, memory bandwidth,
turbo headroom, the scheduler. A balance figure taken linearly is the same figure; a cost figure
taken in parallel is a different one, and nothing in the number says which it is.

*Applies to:* every lab and benchmark run.
*Checked by:* `ParallelAndLinearProduceTheSameMatrix`, which fails when shared state returns.
*From:* [balance-lab.md](balance-lab.md).

#### M7 — Measure a pass against its own start

**To say what a pass cost, take a report at the pass's starting commit and one at its tip,
minutes apart on one idle machine.**

The committed baseline is pinned at an old commit, so a diff against it spans every commit since
and attributes all of them to whoever ran it.

*Applies to:* the per-pass figures in the iteration log.
*Checked by:* — procedure.
*From:* [benchmarks.md](benchmarks.md#the-iteration-log).

### P7 — The game is the authority

Breaking one of these does not fail a test here. It fails at world load, or in someone else's
session. The first three are the compiler and the whitelist; the last two are the game answering a
question this mod could answer for itself and should not.

#### C1 — C# 6 only

**No tuples, no `switch` expressions, no `out var`, no string interpolation beyond what C# 6
supports.**

The game compiles `Data/Scripts/**/*.cs` itself at world load with its own compiler.

*Applies to:* everything under `Data/Scripts`. The test projects link the same files, so the
constraint is theirs too.
*Checked by:* `LangVersion` is pinned to 6 on the core project.
*From:* [development.md](development.md).

#### C2 — The whitelist covers types the local build accepts

**A green build and a green suite are necessary and not sufficient.**

Space Engineers applies a type whitelist that neither the local build nor the tests can see.
`IndexOutOfRangeException` and `ArgumentOutOfRangeException` are both prohibited in game and both
compile happily here; `Exception`, `ArgumentException`, `ArgumentNullException`,
`InvalidOperationException` and `FormatException` are confirmed allowed. Prefer bounds-checking
to catching a throw. No reflection, no file I/O outside `MyAPIGateway.Utilities`, no threading.

*Applies to:* everything the game compiles.
*Checked by:* — nothing local can check it.
*From:* [development.md](development.md), [tests/README.md](../tests/README.md).

#### C3 — Target `net48`, and never reference the native assembly

**`VRage.Platform.Windows` and its dependencies are built against .NET Framework 4.8 and silently
fail to resolve at 4.7.2. `VRage.Native.dll` is unmanaged and produces `MSB3246`.**

*Applies to:* the compile-check project and the test projects.
*Checked by:* the build.
*From:* [development.md](development.md).

#### C9 — The game's own answer is read, never overridden

**Where the game already decides something — whether a room is sealed, how much oxygen it holds —
this model reads that answer rather than forming its own, and every source may veto air while none
may require it.**

Three things can empty a room: a world with oxygen or pressurisation off, the game's own sealing
test, and the vents. Each is a veto and none is a requirement. The asymmetry is not tidiness. Air
is heat capacity, so a room wrongly given air warms and cools as a mass of gas that every bounding
surface exchanges with, while a room wrongly denied air loses only some interior inertia — the two
errors are not the same size. The models disagree by construction, because this model's rooms are
pieces of the game's and its cells are coarser than a sloped block, so what matters is the
direction of a disagreement rather than its existence.

*Applies to:* room pressure, and anything else the game already answers.
*Checked by:* `RoomPressureTests` — one case per veto — and `RoomAirPressureTests`, which reports
the one direction that is a fault: air in the game and none here.
*From:* [document-of-intent.md](document-of-intent.md#what-this-mod-deliberately-is-not),
[thermal-model.md](thermal-model.md), and `RoomPressure`'s own summary.

#### C10 — The server is authoritative over damage

**Clients run the same simulation from the same inputs and never apply what it concludes.**

Temperatures are deliberately not reconciled — the mod sends as little as it can and tolerates
drift — which makes damage the one conclusion that must not be reached twice. A client that
applied its own overheat damage would not throw, would not desync visibly, and would destroy blocks
a server that disagreed by a degree was keeping.

*Applies to:* overheat damage, and any future conclusion with a world-visible consequence.
*Checked by:* — nothing. The guard is one `IsServer` early-out in
`ThermalGridSimulation.ApplyOverheatDamage`, and the suite runs no session.
*Retires when:* nothing retires it. How far a client may drift before it is corrected is still
undecided, and that question does not touch this rule.
*From:* [document-of-intent.md](document-of-intent.md#what-the-mod-owes-a-multiplayer-client),
[known-issues.md](known-issues.md).

### P8 — Off means off, and costs nothing

#### C4 — Nothing allocates on the stepping path

**Anything allocating per frame shows up in the report.**

The raycast result lists and the grid list are pooled and the `kA` arrays cached for this reason.

*Applies to:* the solver, the environment pass and everything a step reaches.
*Checked by:* `bench report` — measured rather than asserted.
*From:* [development.md](development.md).

#### C7 — Every mechanism has a switch that removes its own cost

**Switching a feature off removes exactly its own cost, on the next step, with no reload.**

A readout, diagnostic or overlay that is off costs nothing. This is what makes the feature table
in the benchmark report readable and what lets a server operator pay only for what they use.

*Applies to:* every mechanism, readout and diagnostic.
*Checked by:* `FeatureToggleTests`, and the benchmark feature table measures each cost.
*From:* [README.md](../README.md), [configuration.md](configuration.md).

#### C8 — Absent and empty mean the same thing

**A gate read from the environment or from configuration treats an empty value as absent, so the
obvious way to write "off" is off.**

`THERMAL_CORPUS_TESTS` gates the opt-in corpus walks, and both of its guards test the variable
against `null`. On Linux an exported variable with an empty value is not null, so
`THERMAL_CORPUS_TESTS= dotnet test` — the obvious way to spell *not this time* — enables every
corpus walk instead, and the failure mode is a suite that appears to hang. The general form is the
rule, because a gate whose off position is unreachable by the obvious spelling will eventually be
switched on by somebody trying to switch it off.

*Applies to:* every environment variable and configuration value used as a gate.
*Checked by:* `CorpusGuardTests.OffMeansOffHoweverItIsSpelled`, over every spelling of off anyone
reaches for. `CorpusFixture.OptedIn` is the one decision and `LabInvariantTests` calls it rather
than repeating it.
*From:* [backlog.md](backlog.md) H3, generalised.

### P9 — The core is a library the game happens to call

#### C5 — The core speaks no game type

**The simulation references exactly one Space Engineers assembly, `VRage.Math`, and no public
surface in it takes or returns a game type.**

That is what lets the model be built, tested and profiled in seconds instead of by loading a
world, and it is the boundary an SE2 adapter would bind to.

*Applies to:* `Data/Scripts/Thermodynamics/Core`.
*Checked by:* `CoreIsolationTests`.
*From:* [tests/README.md](../tests/README.md), [architecture.md](architecture.md).

### P10 — The solver's three invariants are the definition of correctness

#### C6 — The solver's three invariants hold

**Order independence, energy conservation, and boundedness.**

Every exchange reads the temperatures at the start of the substep and writes into an accumulator,
so iteration order cannot change the result; every internal exchange is applied equally and
oppositely; and with the overshoot clamp on, no pairwise exchange can carry more than the energy
that brings the pair to equilibrium. A change that breaks the first also removes the only reason
the pass could ever be parallelised.

*Applies to:* the solver.
*Checked by:* `ConductionTests` and `StabilityTests` — both in `PhysicsTests.cs`, which is a file
and not a class — and `ConductionClampGateTests`.
*From:* [thermal-model.md](thermal-model.md).

### P11 — The repository is the publish

The game loads this directory directly, which is what makes a change testable without copying,
and it is also what makes everything sitting here part of what a workshop publish uploads.

#### R2 — Build output and the corpus live outside it

**So that publishing needs no cleanup step, because a cleanup step that has to be remembered is
one that eventually is not.**

Build output goes to a sibling directory; the blueprint corpus lives in the user data directory
and is overridable by `THERMAL_CORPUS`. They were 394 MB and over a hundred gigabytes
respectively. A blame hang dump, at 1.9 GB, was one commit from shipping before `TestResults/`
was ignored.

*Applies to:* anything generated or fetched.
*Checked by:* `Directory.Build.props` sets the first; `.gitignore` and the corpus path handle the
second. Neither stops a new generated directory from appearing.
*From:* [development.md](development.md).

#### R3 — No credential is written into the tree

**A Steam Web API key is taken from the command line or the environment, redacted from anything
printed, and never written to a file. SteamCMD is invoked with a user name and no password.**

Anything else would mean this program handling a password, and it has no business doing that.

*Applies to:* the corpus fetcher and anything else that talks to an external service.
*Checked by:* `CredentialScanTests`, which scans every file a person writes for a private key
block, a provider-prefixed token, a credential assigned a literal, and a Steam key beside a word
saying it is one — and whose second case checks the scan fires on each of those and on none of the
things this repository legitimately writes, `--key <key>` among them.
*From:* [development.md](development.md), [balance-lab.md](balance-lab.md).

### P12 — Do not edit what this repository cannot regenerate

#### R4 — `Models/` is not restructured

**LOD and build-stage model paths are baked into the `.mwm` binaries at export time.**

Moving or renaming a folder under `Models/` silently breaks LOD switching in game, and the only
fix is re-exporting the models — which needs the source scene, not this repository.

*Applies to:* `Models/` and everything under it.
*Checked by:* — nothing; both `note.txt` files say so and that is all.
*From:* [development.md](development.md).

#### R5 — The workshop identity files are not regenerated

**`modinfo.sbmi` holds the workshop id; regenerating it publishes the mod as a new item, and
every subscriber stays on the old one.**

*Applies to:* `modinfo.sbmi`, `metadata.mod`.
*Checked by:* — judgement.
*From:* [development.md](development.md).

#### R6 — Vendored code is replaced, never edited

**Rich HUD Framework, the Definition Extensions client and SENetworkAPI are replaced wholesale
when their authors publish a new version.**

An edit here is lost at the next update, and silently: the replacement compiles.

*Applies to:* the three vendored paths.
*Checked by:* — judgement.
*From:* [development.md](development.md).

### P13 — A long run is designed for its own death

Every rule here was bought with a lost day.

#### O1 — Every long run is capped

**Wrap `dotnet test`, a benchmark or a sweep in a cgroup with a hard memory ceiling.**

```bash
systemd-run --user --scope -p MemoryMax=24G -p MemorySwapMax=0 --quiet dotnet test ...
```

An uncapped validation run grew to about 80 GB resident on a 91 GB machine and tripped the global
OOM killer, which took the editor down with it and lost uncommitted work. A cap turns that into
one dead process and a clear signal. Choose the ceiling above the run's measured working set
rather than by habit, and sample resident size during a long run rather than learning the peak
from a kernel log. Several capped runs at once still sum on one box.

*Applies to:* anything that runs for minutes or reads the corpus.
*Checked by:* — procedure.
*From:* the operations record kept with the session notes.

#### O3 — Long sweeps resume, and progress is measured in bytes

**Corpus order is deterministic and data is written per ship, so a killed run resumes by skipping
what is already on disk — which turned a lost eight hours into a seventeen-minute finish.**

Progress must be counted in bytes rather than files: the walk is largest-first, so file 500 of
9,981 is 5 % of the files and 50 % of the work. A kill pattern must not match the relaunch that
has just started.

*Applies to:* every corpus sweep.
*Checked by:* — procedure.
*Retires when:* the resume counts ships rather than files. It currently counts files while the
writing is per ship, so an interrupted batch is re-emitted, the shipped dataset carries 50
duplicate rows, and `verdict.py` drops them and prints the count. That workaround and the caveats
on three pages retire together; [backlog.md](backlog.md) H2.
*From:* the operations record, [balance.md](balance.md).

#### O5 — A lab streams, and counts what it did not measure

**A parsed ship holds every grid and every block, so a corpus read in one pass is what takes the
machine down. Batch it, and report the count of what was skipped rather than dropping it.**

The two halves are one rule: batching is what keeps the run alive, and counting the skips is what
keeps the result honest about the population it actually covered — `P1` and `P2` applied to a run
that had to be cut down to fit in memory.

*Applies to:* every lab that reads the corpus.
*Checked by:* — the labs do this; nothing enforces it.
*From:* [balance-lab.md](balance-lab.md).

### P14 — Unobservable fidelity is cost

#### D6 — A deliberate simplification is recorded as a limit

**A shortcut taken on purpose is written into the limits list, with what it costs, so nobody
rediscovers it as a bug.**

Build state not changing a block's thermal properties, a surface described by two constants
rather than a spectrum, point sources not occluded, a room with no vent holding no air: each is a
decision, and each is recorded with the machinery that would undo it. This is the operative half of P14 — the shortcut
is free to take and not free to leave unwritten.

*Applies to:* every decision to approximate.
*Checked by:* — judgement.
*From:* [known-issues.md](known-issues.md#deliberate-limits).

#### M8 — A scenario earns its place by answering what nothing else answers

**Nothing is in the battery because it seems interesting; where two scenarios answer the same
question, the cheaper is kept, and directional cases are enumerated rather than sampled.**

`idle` and `vacuum-shadow` were the same simulation — 27 ships ran under both and differed by
0.000 K — and one was cut. `all-peak` was cut from the corpus for the opposite reason: it puts
96 % of ships over critical, and a melting grid never settles, so it starves the invariant it is
run for. A ship is not symmetric, so both thrust and travel expand to six directions.

*Applies to:* the scenario battery.
*Checked by:* — judgement.
*From:* [balance-lab.md](balance-lab.md).

---

## Conditional

Seven rules hold only inside a stated scope. Every one of them was first written as an absolute,
and written that way each forbids work that is ordinary and correct. The scope is the rule.

### D7 — Measure before replacing what the counts accuse

**A subsystem suspected on aggregate counts is compared against ground truth before it is
changed.**

More than one thing here has looked guilty from the counts alone and been innocent. The room map
is the standing example: it was suspected of losing compartments, on a count of rooms that looked
too low, and when the comparison was finally run it was right about twelve of twelve. A rewrite
would have been judged against nothing, would have passed, and would have replaced working code
with a change nobody could argue about afterwards.

The counts are worth trusting as a place to look and not as a verdict, which is `D1` seen from the
other side: `D1` is disbelieving a number that looks right, this is disbelieving one that looks
wrong.

*Applies to:* a suspicion raised by aggregates rather than by a reproduction.
*Checked by:* — judgement.
*From:* [document-of-intent.md](document-of-intent.md#what-correctness-means), one of two standing
instructions from the developer; [known-issues.md](known-issues.md).

**Why conditional.** As an absolute it forbids ordinary correct work: a crash with a stack trace, a
dead link, a typo in a label are all fixed on sight, and nothing is bought by measuring first. The
rule bites exactly where the evidence is a count and the proposed fix is a replacement.

### E2 — Measure the population, not the specimen

**A claim about what players build is measured against the workshop corpus in the lab. A figure
from a running session is one sample and is labelled as one.**

One ship is one ship. The tiers every benchmark rests on came from a single 1,381-block hull,
and the corpus later showed that hull to be a 96th-percentile ship for heat. A session's figure
also cannot be re-examined: the world is gone and what else was true of it is unrecorded.

*Applies to:* population, balance and cost claims. It does **not** forbid in-game measurement —
several things are only observable in a session, including the heat pump's electrical hookup,
the whitelist's verdict on a type, the settings menu's multiplayer path, and the cost of terrain
occlusion. Those are recorded as in-game observations, and a lab claim is not built on them.
*Checked by:* `CensusFidelityTests`, which fails when the synthetic hull drifts from the field
observations recorded beside it.
*From:* [balance-lab.md](balance-lab.md), [known-issues.md](known-issues.md).

**Why conditional.** The short form — *do not trust in-game numbers* — would retire three test
suites that check the mod against a world, and would forbid measuring the several properties that
have no other observer.

### E6 — Pair the terms before dividing

**A ratio is formed from two measurements of the same thing. Two extrema drawn from different
members of a population do not form one.**

A hull's air peak divided by its vacuum peak was published as sensitivity to air, at 1.02 against
a real 1.73. They are different blocks — the air peak an exposed fitting, the vacuum peak a
buried heavy block — so the quotient described no block at all. Asked per block, the same hull is
ordinary. Take the aggregate of the per-unit ratio; before changing a shared fixture on a derived
number, read the underlying terms once by hand.

*Applies to:* any quotient of aggregates, and any figure derived from one.
*Checked by:* — judgement.
*From:* [benchmarks.md](benchmarks.md), and the correction in commit `46ba54f`.

**Why conditional.** A ratio of two aggregates over the *same* subject — total watts vented over
total watts made, for one grid — is exactly the right figure, and the absolute form of this
sentence bans it.

### M1 — Compare only runs that were stopped the same way

**A quantity read at the end of an adaptively-stopped run is comparable only to one from a run
that stopped on the same criterion at the same point.**

Runs stop when the hottest block flattens, so two ships warming from the same start are sampled
at different points on their curves. One ship in five appeared to violate *sunlight never cools*
that way, with the sunlit run's bulk still falling at megawatts. The fix is a cold start at the
vacuum floor on equal fixed clocks.

*Applies to:* quantities read at the end of a run.
*Checked by:* `SunlightPanelWalk` runs its comparison on fixed clocks.
*From:* [balance-lab.md](balance-lab.md).

**Why conditional.** It does not apply to quantities decided before the stop — crossing times,
shares, seconds-to-critical — which are comparable across runs that stopped differently. Most of
the corpus findings rest on exactly those.

### M3 — The run is the unit of parallelism

**Two scenarios on one ship must not run at once.**

Every simulation built from a ship shares that ship's `BlockInstance` objects, and the load is
written onto them, so concurrent runs overwrite each other — which does not throw and does not
look wrong in a report. Each job reads the blueprint again for grid state of its own; block
models stay cached and shared.

*Applies to:* the harness's own scheduling.
*Checked by:* `ParallelAndLinearProduceTheSameMatrix`.
*Retires when:* the load is applied without mutating the block instance. This is a constraint
imposed by mutable shared state, not by physics, and it is the only rule on this page that
describes an implementation rather than a fact about the world.
*From:* [balance-lab.md](balance-lab.md).

**Why conditional.** Outside the harness's scheduler it says nothing, and inside it, it stops
being true the moment the mutation does.

### O2 — A run with no bounded duration gets no hang timeout

**`--blame-hang-timeout` kills a healthy run at exactly the timeout, and the test runner cannot
tell slow from deadlocked.**

It ended a corpus survey at eight hours. On an ordinary suite run, where every test has a
bounded duration, `--blame-crash --blame-hang --blame-hang-timeout` is worth having, because a
kill then names the test that was in progress.

*Applies to:* the corpus walks and any single test that legitimately runs for hours. It does not
apply to the ordinary suite.
*Checked by:* — procedure.
*From:* the operations record. This resolves a direct contradiction between two standing notes,
one requiring the flag and one forbidding it; the scope above is the reconciliation.

**Why conditional.** Both absolute forms are wrong, and both were written down. The flag is
correct for the suite and destructive for a sweep.

### J3 — The corpus filters are strict, and their cost is recorded

**One unresolved subtype disqualifies a ship, and ships under 25 blocks are fragments.**

There is no way to tell whether a block that failed to resolve was a decorative panel or the
reactor, so a softer rule would mean the corpus measures *nearly* vanilla balance. The cost is
real and is recorded rather than hidden: no corpus ship carries one of this mod's cooling blocks,
which is why the cooling criterion had to be answered by a retrofit lab instead of by the survey.

*Applies to:* corpus acquisition and screening, while the question is vanilla balance.
*Checked by:* `BlueprintTests` covers the yield.
*Retires when:* the yield falls to where the survivors stop being a population — currently about
four in five, so not soon. The 25-block floor is an unexamined constant that has never been varied
to see whether it changes a population figure.
*From:* [balance-lab.md](balance-lab.md).

**Why conditional.** The retrofit lab deliberately adds this mod's blocks to corpus ships. Applied
there, the filter would reject its own input.

---

## Low value

Four entries: one rule that costs measurably more than it prevents, and three that turned out not
to be rules at all. They are kept here, rather than deleted, so that a citation to one resolves to
its disposition rather than to nothing — and so that the reasoning survives the next person who
thinks of writing them again.

### O4 — Corpus walks run alone

**Four walks across thirty-one workers on thirty-two cores measured seventeen times slower than
running them one at a time, and it hid behind a 93 % CPU reading because the cores were busy
thrashing cache.**

The observation is sound. The rule as implemented is not: `xunit.runner.json` sets
`maxParallelThreads: 1` for the whole test project, so every run of every test pays for the
isolation of four opt-in walks that most runs never execute. Measured on 2026-08-22: 53.1 s
serial against 16.7 s at eight threads, with the same passing count in every configuration and ten
for ten in a parallel burn-in.

*Applies to:* the opt-in corpus walks — but the mechanism applies to every test class in the
project.
*Checked by:* `xunit.runner.json`, more broadly than the rule needs.
*Retires when:* the walks move into a collection that disables parallelism for itself and the
project-wide setting comes off. The narrower form already exists in the suite: `LoadTests`
declares its own collection with `DisableParallelization`. [backlog.md](backlog.md) F8.
*From:* the operations record.

**Why low value.** It buys correct isolation for four tests and charges every run three times its
duration, when a mechanism that charges only the four is already in use ten lines away.

### R1 — The repository is the mod folder

**Absorbed into P11.** It states a fact about how the game loads this directory, not something a
change can violate — and everything that *can* be violated because of it is already written down:
`R2` keeps generated bulk out, `R3` keeps credentials out, `R5` protects the identity files.

### J1 — Game mod first

**Absorbed into P14.** *Where a simplification costs nothing a player can perceive and avoids real
expense, take the shortcut, and say plainly when the shortcut would be visible.* That is a prior
that decides what gets built; it is not a rule a diff can be measured against. Its operative half
is `D6`: take the shortcut, and record it as a limit.

### J2 — Light, isolated, tested, open

**Absorbed into `C4`, `C5`, `C7` and `R9`.** Four claims in one sentence, each of which already
has its own rule and its own check. As a rule it could not be broken independently of the four,
and a rule that cannot be broken on its own is a summary. It remains a good description of the
mod on the [README](../README.md); it is not a rule.

---

## Where these came from

Nothing on this page is invented here except `E11`, `C8` and `R11`, and those are generalisations of
defects the tree already records. Every other rule was already written down somewhere — in a page,
in a test class's own summary, in the header of a tools page, or in a note kept outside the tree —
and that was the problem it exists to fix. Each rule's *From* field names where it came from.

Those pages keep the argument and the evidence. **When a rule and its source disagree, the source is
right and this page is stale**; say so and fix it here.

---

## Change log

| Date | Change |
| --- | --- |
| 2026-08-22 | `R3` and `E5` are checked rather than judged ([backlog.md](backlog.md) `F9`). The tree is scanned for four credential shapes, and the scan is itself checked against a value of each shape and against the text this repository legitimately writes. Counts a page states about the panel, about `Cubes.xml` and about the suite's own classes are compared with those datasets; a change log is exempt, because `R12` makes it a record of what was true rather than a claim about now. It found two stale figures on its first run — 432 authored values against 654, and 135 test classes against 160. |
| 2026-08-22 | Added `R14`, from the standard [document-of-intent.md](document-of-intent.md#what-a-code-comment-is-for) states and a pass that applied it: twenty-four comments were found describing a member that no longer exists, having come to rest on the one below. `NoDocCommentDescribesSomethingThatIsNotThere` catches the shape, and was run against a deliberate orphan in both spellings before being believed — the first version caught only one of the two and missed a real orphan in `CoolantLoopTests`. |
| 2026-08-22 | Moved *What the extraction changed* into this log, where a record of a revision belongs (`R12`). It held four things, each still true and each now recorded once. **Three *Checked by* citations named something that does not run:** `C6` cited `PhysicsTests`, which is a file whose classes are `ConductionTests` and `StabilityTests`; `D1` attributed three invariants to `LabInvariantTests` that live in `ScreeningTests`; and `D5` cited `SealedBlocksAreRare`, which commit `991d4d9` demoted to an uncalled helper when `CorpusSurvey` absorbed the standalone walks. `R11` is the rule those three produced. **`E1` and `E8` overstated `verdict.py`**, which prints `HOLDS`, `FAILS` or `?` and exits zero either way — both fields now say reported rather than checked. **Two rules came out of the reduction rather than an incident:** `E11` closes `E1`'s hole, and `C8` generalises the gate whose off position cannot be spelled. **Three rules stopped being rules and one changed category** — `R1`, `J1` and `J2` are premises rather than things a change can violate, and `O4` was reclassified low value against a measurement taken the same day. All four dispositions are in [Low value](#low-value). |
| 2026-08-22 | Put this page under the checks it asks of every other page. `EveryCheckCitedByTheRulesPageResolves` resolves every name in a *Checked by* field to something that runs, which retires `R11`'s unchecked state and closes [backlog](backlog.md) F10; `EveryRuleCitedByAPageExists` fails on a page citing a rule this one does not state; `TheRulesPageIndexesEveryRuleItStates` holds the index, the body and the principle table together. Each was run against a deliberate violation before being believed. |
| 2026-08-22 | Read every page in the tree against the rule list and added the six rules it was missing: `C9` `C10` `D7` `D8` `R12` `R13`, each of which existed only in the one page or the one type summary that needed it. Widened `P7` from *the game is the authority on what compiles* to *the game is the authority*, which is where `C9` and `C10` belong. Recorded the necessity and sufficiency tests the principle list was put through in [Testing the reduction](#testing-the-reduction). Corrected `O4`, which quoted 135 test classes against a project that now holds more than 150; the figure is now stated without a hand-typed count, per `E5`. |
| 2026-08-22 | Repointed the *From* fields at the pages that absorbed the ones they cited: `corpus-shape.md` into [balance.md](balance.md), `iterations.md` into [benchmarks.md](benchmarks.md). Added this change log. The rule inventory itself is unchanged. |
| 2026-08-22 | Extracted the standing rules from the eleven pages that each stated one in passing, reduced them to fourteen principles, and classified every rule as load-bearing, conditional or low value. Marked the sixteen load-bearing rules that nothing checks as unchecked rather than leaving the gap implicit. |
