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

**The instrument.** `NodeActivityLab` (`bench activity`) reports, at steps 10, 50 and 200 after
an event, the share of nodes whose per-step movement is under 0.0001 / 0.001 / 0.01 / 0.1 K, and
the share of links with both ends quiet — on a parked hull in air and on a driven hull in vacuum
— then disturbs one node by 300 K and tracks the active set for fifty steps. No equilibrium is
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

The labs above have not yet run at evaluation size; this section is filled by the commit that
runs them, so the criteria in this page's history demonstrably predate the data.

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
| 2026-09-05 | Opened: the survey of measured refusals, the two open candidates — change-local room remapping and activity tracking — and their criteria, committed before the labs first run at evaluation size (`E1`). |
