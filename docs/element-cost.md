# What a substep costs

The step budget bounds a step at a number of **link visits** — substeps times links. A substep
also runs the environment pass, which is per *node*. The budget cannot see that, so two grids with
the same link count and different shapes are granted the same allowance for different work.

This measures the difference, so the budget can charge for what a substep actually does.

```bash
cd tests
dotnet run --project Thermodynamics.Sim -- bench elements --nodes 100000 --seconds 4
dotnet run --project Thermodynamics.Sim -- bench elements --nodes 250000 --seconds 4 \
    --shapes stick,comb,plate,hollow,box --csv out/bench
```

## Method, and two things that had to be got right first

Shapes are chosen for their **link-to-node ratio** rather than for realism: isolated blocks touch
nothing, a stick is a chain, a plate is two-dimensional, a hollow box is a shell, and a solid box
approaches three links per node. The whole substep is timed and the coefficients fitted by least
squares through the origin. The `ship` shape is held out of every fit and predicted from it, which
is the only honest check that the coefficients describe anything.

**A single fit does not work, and the reason is geometric.** Fitting `cost = a·nodes + b·links`
with the environment on gave 0.37 ns a link, a negative r², and a solid box measuring *cheaper*
than a stick with a third of the links. On a cube lattice every cell face is either bonded or
exposed, so `faces ≈ 6·nodes − 2·links`: exposure and link count are nearly collinear across any
family of shapes. The box was not cheaper because links are free — it was cheaper because its
interior blocks have no exposed faces to integrate. The environment is therefore separated by
**differencing**: each shape is measured twice, with radiation, convection and solar off and on.

**A substep figure has to be measured where substeps dominate.** At the shipped `HeatTimeScale`
these grids demand *one* substep a step, so a per-substep number is really the per-step overhead —
array syncing, environment sampling, write-back — which is an order of magnitude larger than the
per-element work and is charged once a step, not per element. The lab runs at `HeatTimeScale`
20,000, which puts every connected shape at 27–81 substeps a step. The `dust` shape cannot be
driven there at all: with no links it has no conduction stiffness, so it stays at one substep and
is excluded from the fits. Leaving it in drove the per-link coefficient negative.

## What it measured

Best of three per condition, .NET 9, this machine.

| nodes | ns per node | ns per link | env adds, per node | **a node is worth** | an exposed face |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 4,000 | 2.63 | 1.45 | 1.37 | **2.8 links** | 0.14 links |
| 60,000 | 2.60 | 2.85 | 1.58 | **1.5 links** | 0.14 links |
| 100,000 | 6.1–6.6 | 2.6–2.9 | 1.8–3.4 | **3.3 links** (2.9–3.7, n=3) | ~0.19 links |
| 250,000 | 13.2–13.9 | 1.8–2.2 | 1.4–2.3 | **7.5 links** (6.6–9.0, n=3) | ~0.4 links |
| 500,000 | 15.0 | 1.67 | 7.0 | ~13 links (one noisy run) | ~0 |

Conduction fits well — r² 0.88 to 0.99, and the held-out ship predicted within 1–15 %. The
environment fit is much weaker, r² 0.08 to 0.72, for a reason that is itself the finding.

## Three conclusions

**1. A node is not free, and it is not one link either.** The budget charges zero for it. At the
sizes where the budget binds it is worth three to eight links. On the field ship's ratio of 2.4
links per node, counting links alone under-charges a step by about 2.4× at 100k blocks and 4× at
250k.

**2. The weight rises with grid size, because nodes are the working set.** Per-link cost is flat at
1.5–2.9 ns across a hundredfold size range: links stream. Per-node cost goes 2.6 → 6.4 → 13.4 ns as
the node state stops fitting in cache. This is why one constant cannot serve every grid — and why
it does not need to. The budget's own documentation notes that grids below about a hundred
thousand blocks never reach it, so the weight only has to be right from 100k up, where it measures
**3 to 8**.

**3. Exposure is not what the budget is missing.** An exposed face costs 0.1–0.5 of a link, and the
environment fit's poor r² says the same thing from the other side: most of the environment pass is
per node — sampling, the radiation terms' setup, the write-back — and only the loop inside it
scales with exposure. A budget that counts nodes and links, and ignores faces entirely, loses very
little.

## What this recommends

Count **`links + 4·nodes`** and leave faces out. Four is chosen at the low end of the measured
3-to-8 range, at the size where the budget first binds; a grid past a quarter of a million blocks
is still slightly under-charged, which errs towards letting a large grid run rather than throttling
it on a machine that could have kept up.

The default value has to move with the unit. See
[load-and-hitching.md](load-and-hitching.md#calibrating-the-step-budget-against-a-real-world) for
the field measurement of what the current default buys in game, and
[known-issues.md](known-issues.md) for the defect this closes.

**Absolute nanoseconds here do not transfer to the game.** The harness is .NET 9 and the game is
.NET 4.8; a field dump measured 34.8 ns per element against roughly 1.9–2.0 ns here — and both
figures move with the solver, so the ratio is only meaningful between a dump and a harness run on
the same tree. The *ratios* are
arithmetic per element rather than throughput, which is why the lab reports link-equivalents, and
`AWeightIsLinksPerNodeAndSurvivesAChangeOfUnits` in `ElementCostFitTests` pins that they do not
move when the machine does.
