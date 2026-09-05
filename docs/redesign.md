# Redesign potential, evaluated

What this page covers: the structural redesigns still available to this codebase — the changes
that alter *what* is computed rather than how fast a line runs — with the measured refusals that
close some of them, the criteria that decide the open ones, and the labs that measure each
criterion. It does not cover local optimisation, which
[performance.md](performance.md#pass-10--what-the-pass-moved)'s tenth pass closed at a measured
floor stage by stage, and it does not cover the SE2 port, which has its own evaluation in
[se2-labs.md](se2-labs.md).

The method is the repository's usual one: a criterion is written before the data (`E1`), the
instrument is a lab in the tree reachable by a command (`D2`), and a candidate leaves this page
either as a design row in [backlog.md](backlog.md) or as a refusal with a figure attached.

## The survey: what is already answered

Most of the redesign space has been measured before, and the answers are citations rather than
work. They are collected here so the next evaluation starts from the record instead of
rediscovering it.

| Candidate | Verdict | The figure behind it |
| --- | --- | --- |
| Struct-of-arrays for `ThermalNode` / `BlockInstance` | **refused** | the hot path already reads 22 flat per-node arrays; the objects are cold handles, so the change buys memory and no speed, for 24 shipped and 98 test files of blast radius (`E12`) |
| Lumping stiff nodes (`D5`) | **refused for SE1** | the per-block substep cap already collects the prize — the stiff set is ~43 blocks in 43,000, so lumping is "a few per cent over a setting that already exists" ([stiffness.md](stiffness.md#2-lumping--the-same-idea-done-properly)); it re-opens if the stiff set stops being a thin tail |
| Multirate stepping (`D6`) | **refused for SE1** | `0.001 × 28 + 0.999 × 1 ≈ 1.03` substep-equivalents — arithmetically the best answer, every saving already collected by the cap, and the desynchronisation risk is real ([stiffness.md](stiffness.md#3-multirate-stepping--when-the-tail-is-not-thin)) |
| Implicit integration | **not for this fleet** | a 20-iteration CG solve roughly matches what `MaxSubstepsPerBlock 4` already delivers, at far more risk; it is the answer where stiffness is not a thin tail ([stiffness.md](stiffness.md#4-implicit-integration--the-endgame)) |
| Cell-order link walks | **refused** | six schemes measured 0.91–1.40; the stage is bound by one grid-sized memory touch per neighbour, and the orderings that remove it move every temperature's last bit (`D3b`) |
| Deriving face weights from packed counts | **refused** | bit-identical and +10 % on the solver stage ([performance.md](performance.md#pass-10-iteration-7--deriving-the-face-weights-costs-a-tenth-of-the-step-tried-and-dropped)) |
| One grid per thread (`D19`) | **built, ships off** | 10.2× on a 242-grid fleet at 32 threads; the three questions left are a session's, not a lab's |
| Coarse-cell room flood | **SE2's, not SE1's** | the ÷k³ win needs block-aligned sealing at the coarse stride; on SE1 a block *is* a cell, so a supercell misaligns everywhere — `bench coarserooms` exists and verifies the SE2 case cell for cell ([se2-labs.md](se2-labs.md)) |

## The two candidates still open, and their criteria

Two redesigns survive the survey: both change *which elements a pass visits*, which is exactly
the direction pass 10's close said was left. Each criterion below was fixed before its lab first
ran, and the labs are `bench remaplocality` and `bench activity`.

### 1. A change-local room remap

**The candidate.** A sealing change requests a full remap: the flood rewalks the whole bounding
box — fourteen times the block count — and the budgeted tick count to converge is the box over
4,096 cells, which is `D2`'s structural wait. A change-local design would reflood only the region
a change can reach: the mutated cell's component and whatever it seals or unseals. The design is
not sketched here; what this page decides is whether it is worth sketching.

**The instrument.** `RemapLocalityLab` (`bench remaplocality`) mutates a census hull one block at
a time — a block added on the skin, an exposed block removed, a buried block removed, a
room-boundary block removed — refloods fully after each, and diffs the two maps cell for cell,
with rooms named by their smallest cell key so renumbering cannot read as change. The changed
count over the visited count is the share of the full remap that was not rediscovery.

**The criterion, fixed 2026-09-05 before the first run.** A change-local remap earns a design row
if the median changed-to-visited ratio across the four mutations is **under one per cent** — that
is, if at least ninety-nine per cent of a full remap's budgeted work rediscovers what the map
already said. The room-boundary mutation is expected to be the large one and is in the panel so
the criterion is tested against the case where a big change is the *correct* answer.

### 2. Activity tracking — the sleep threshold, measured before designed

**The candidate.** A step touches every node and every link, however quiet. `D1` records that
only visiting fewer elements can shrink a step, and `G5` left "the sleep threshold as a gameplay
knob before a performance one" open. The ceiling on any sleeping scheme is the quiet share of a
real grid — reached only by a scheme with no bookkeeping at all, so a measured ceiling that is
already low kills the idea cheaply.

**The instrument.** `NodeActivityLab` (`bench activity`) reports, at marks after an event —
steps 10, 50 and 200 as first built, with 2,000 and 20,000 added after the first run showed the
short marks measure only the transient — the share of nodes whose per-step movement is under
0.0001 / 0.001 / 0.01 / 0.1 K, and the share of links with both ends quiet, on a parked hull in
air and on a driven hull in vacuum; then it disturbs one node by 300 K on the quietest background
it has and tracks the active set for fifty steps. No equilibrium is
claimed anywhere (`M12`): activity is a function of steps since the last event, which is the
shape a sleeping scheme actually faces.

**The criteria, fixed 2026-09-05 before the first run.** Activity tracking earns a design row if
**both** hold at the millikelvin-per-step threshold — below anything a readout shows:

* the parked hull is **more than half quiet by step 200**, and
* the disturbance's active set stays **under ten per cent** of the grid across its first fifty
  steps.

The driven hull's share is reported either way: a scheme that only helps parked ships is worth
much less, and a scheme that hurts working ships is worth nothing.

## Findings

### The change-local remap: criterion met, decisively

`bench remaplocality`, 2026-09-05, census hulls, exact map diffs:

| mutation | 32,800 blocks: visited → changed | 126,731 blocks: visited → changed |
| --- | ---: | ---: |
| skin add | 381,239 → **1** | 1,678,096 → **1** |
| skin remove | 381,238 → **1** | 1,678,109 → **1** |
| buried remove | 378,397 → **1** | 1,671,526 → **1** |
| room boundary remove | 378,395 → **2,206** (0.58 %) | 1,671,523 → **6,846** (0.41 %) |

The median changed-to-visited ratio is one cell in ~1.7 million — the criterion asked for under
one per cent and the panel's *worst* row is under half of one. Three of the four mutations change
exactly the mutated cell's own classification; even opening a whole compartment reclassifies 0.4 %
of what the full pass visits. **A change-local remap earns its design row**: the full reflood a
mid-session block change pays — 410 budgeted ticks at 126,731 blocks, 3,934 at a million — is more
than 99.5 % rediscovery in every case measured, and the room-boundary case shows the change region
is discoverable (it is the opened room plus the mutated cell's neighbourhood, both reachable from
the mutation). Filed as `D21` in [backlog.md](backlog.md).

### Activity tracking: the criterion as written fails, and what failed was its clock

`bench activity`, 2026-09-05, 32,800 blocks, node shares quiet at each threshold:

| scenario | step | <0.0001 K | <0.001 K | <0.01 K | <0.1 K | links quiet (<0.001 K) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| parked in air | 200 | 0.0 % | **0.1 %** | 0.2 % | 5.5 % | 0.0 % |
| parked in air | 2,000 | 32.1 % | 51.5 % | 82.4 % | 100 % | 52.5 % |
| parked in air | 20,000 | **100 %** | **100 %** | 100 % | 100 % | **100 %** |
| driven in vacuum | 200 | 0.0 % | 0.0 % | 0.0 % | 0.1 % | 0.0 % |
| driven in vacuum | 2,000 | 0.0 % | 0.0 % | 29.1 % | 92.5 % | 0.0 % |
| driven in vacuum | 20,000 | **100 %** | **100 %** | 100 % | 100 % | **100 %** |

**The criterion as fixed is not met and the verdict stands against it as written** (`E1`): at step
200 the parked hull is 0.1 % quiet against the 50 % the criterion demanded, and its second clause
was unjudgeable on the first run because the wavefront was measured against a background that had
never gone quiet — the whole grid read active, which said nothing about the disturbance (`P2`).

What the longer marks then showed is that the criterion's *clock* was the error, chosen by guess
at 50 simulated seconds when the physics of a hull's decay puts the quiet horizon at the order of
a simulated hour: by step 20,000 (≈ 83 simulated minutes) **both** hulls are completely quiet at a
tenth of a millikelvin — the driven one included, because a ship at thermal steady state moves
nothing per step even with every producer running. And on that quiet background, a 300 K
disturbance's active set peaks at **159 nodes of 32,800 — 0.48 % of the grid** — and is shrinking
again by step 50.

### The revised criterion, changed in the open

Per `E11`: the original criterion stays above, this revision names what moved and why, and the
run that decides it is not in the commit that states it. **Revised**: activity tracking earns a
design row if, at the millikelvin-per-step threshold, both hulls are more than 90 % quiet by step
20,000 *and* the disturbance clause holds as originally written (under ten per cent of the grid
across its first fifty steps, measured on a background that is itself quiet). What moved is only
the mark the first clause is read at — 200 → 20,000 steps — because 200 steps was an arbitrary
clock that measured the transient, not the regime a sleeping scheme lives in; the thresholds and
the shares did not move. The confirming run, at a size this page has not yet judged, follows in
its own commit.

## Limits

Everything here is measured on census hulls, not the workshop corpus: these are structural
potentials (shares of a box, shares of a node population), not balance figures, and the corpus
rule ([validate-on-real-grids](balance-lab.md)) is about the latter. If either candidate graduates
to a design, its acceptance figures move to corpus scenarios at that point. And a lab's quiet
share says nothing about the cost of *knowing* a node is quiet — the bookkeeping is the design
problem, and this page only prices the prize.

## Change log

| Date | Change |
| --- | --- |
| 2026-09-05 | **Findings recorded.** The remap candidate meets its criterion decisively (median one changed cell in ~1.7 million visited; worst case 0.58 %) and is filed as `D21`. The activity criterion **fails as written** — 0.1 % quiet at its step-200 clock — and the long marks show the clock was the error: both hulls are 100 % quiet by step 20,000 and a 300 K disturbance peaks at 0.48 % of the grid on a quiet background. A revised criterion (the mark moves to 20,000; thresholds and shares unmoved) is stated per `E11`, with its confirming run left to the next commit. The wavefront's first run also corrected the lab: measured on a 200-step background the whole grid read active, so the number described the background, not the disturbance (`P2`). |
| 2026-09-05 | Opened: the survey of measured refusals, the two open candidates — change-local room remapping and activity tracking — and their criteria, committed before the labs first run at evaluation size (`E1`). |
