# Thermal rendering: synthetic optimization audit

## Scope and reproduction

### Nearest-block query traversal

The nearest-block search calculated each child's bounding-box distance to order
the children, then calculated it again on entry to that child. The traversal now
passes the computed bound into the recursive call. Pruning, tie order, leaf
distances and returned temperatures are unchanged.

On 16,384 outside-kernel queries over 4,096 blocks, the frozen reference took
4.826 ms and the optimized query took 3.919 ms (18.8% less CPU). Every returned
temperature matched exactly; neither query loop allocates (40 measured bytes are
the stopwatch). The immediately preceding current implementation measured 4.826
ms in a separate run as well. This targets field refresh sampling, not billboard
submission, and is not a forecast of whole-frame improvement. Reproduce using
`--nearest`; results are in `tools/thermal-performance/results-nearest-query.json`.

### General overhead pass

Near-camera subdivision now evaluates the centre only when none of the edge
samples already requires a split. A frozen-reference differential benchmark
checks exact positions, temperatures, triangle count and ordering across 1,024
caps and five fade positions: 54,466 identical triangles, median CPU time
2.986 → 2.441 ms (18.2% reduction). Sampling allocates no objects with the
scratch list preallocated; the measured 40 bytes are the stopwatch. Reproduce
with the performance project's `--caps` option; results are in
`tools/thermal-performance/results-cap-sampling.json`.

Removed the independent renderer's second per-region world-AABB/camera-frustum
test after local-space BSP visibility traversal. The surviving leaves already
passed the tighter grid-local test. Moving/rotating-grid tests compare surviving
leaves against independent eight-corner world bounds over 80 camera positions.
Per-face and per-patch culling remain. No temperature tolerance, LOD, range or
billboard cap changed. Native frame-time savings for this removal are unmeasured.

### Near-camera closing surfaces

Inspection of the installed `VRage.Render11.dll` (`MyBillboardRenderer`) confirms
PostPP bucket 4 retains submission order; triangle UV0/UV1/UV2 are forwarded to
the shader. `Transparent/Billboards.hlsl` uses linear texture sampling and the
PostPP path uses premultiplied blending. Both thermal ramp DDS files contain
opaque alpha. This does not rule out geometry/depth intersection artifacts.

Near-plane caps previously sampled only each fan triangle's three corners. They
now sample edge midpoints and the centroid, subdividing where temperature differs
from linear interpolation by more than 20 K. Samples include the current temporal
blend. Recursion is limited to three levels (64 triangles per original fan triangle),
with a reused scratch list. This is a sampled approximation, not a global error
guarantee or model-surface reconstruction. Flat caps remain one triangle.

A synthetic cooling saddle checks dense barycentric samples, positive winding,
total area conservation and collapse to a single triangle when uniform. The live
`cap-triangles` counter records submitted cap geometry so its contribution can be
measured. Native appearance and additional per-frame cost remain unverified.

### Close-up leaf-gradient refinement

The close-up screenshot shows diagonal wedges and cloudy transitions. Inspection
found that the minimum lattice cell stopped subdivision even when its bilinear
saddle exceeded the surface error tolerance. The native shader interpolates UVs
per triangle; the palette DDS files have fully opaque alpha. These checks identify
a real approximation issue, but do not establish it as the sole screenshot cause.

At the highest partition tier (512), single-cell patches now subdivide according
to `ceil(sqrt(abs(A-B+C-D)/(4*20K)))`, limited to eight subdivisions per axis.
Both temporal endpoints determine subdivision. Each new vertex samples the same
scalar lattice, preserving source temperatures. Merged multi-cell crease patches
are excluded. Lower detail tiers, exposure, depth materials and billboard quota
policy are unchanged. There is no guarantee of 20 K error for extreme saddles
that reach the eight-subdivision limit, or of error relative to true block heat.

Synthetic 300/700 K saddle tests reduce maximum error against the lattice from
200 K to 12.5 K using 16 patches instead of one. Heating and cooling fades are
checked across dense sample points. Flat cells remain one patch. This deliberately
spends extra triangles on nearby gradient fidelity; native appearance and added
cost still require verification. Geometry overlap and overall darkness remain
separate possibilities, not claimed resolved by this change.

### Visible-grid preparation priority (September 19 live dump)

Follow-up dump `20260920_011346`: 4,150 captured frames, zero render errors,
1,079 preview frames (26%). Renderer CPU mean 25.644 ms, peak 434.291 ms.
These are different camera trajectories, not a controlled before/after benchmark.
The retained event window shows two visible grids: the nearby 3,026-block ship
refreshes in 1.40–1.67 s and the farther 999-block ship in 3.49–3.73 s.
Most preparation events hit exactly 256 iterator steps at well below the 2 ms
deadline. Raised the defensive step ceiling to `max(4096, visibleGrids * 128)`
so small visible fleets can consume their available time. Deadline, idle exit,
priority policy and rendering fidelity are unchanged. Background preparation
still took 5.485 ms in one reported frame; the cooperative deadline cannot
interrupt an expensive iterator step. This change does not eliminate that
existing limitation. Regression tests cover
remaining-time progress, deadline, idle exit and the finite step ceiling.

The 23:53:25 dump contained 2,379 frames with at least one preview out of
4,045 captured frames. Small distant grids repeatedly completed temperature
refreshes while initial detail elsewhere remained pending. The old equal
round-robin preparation did not prioritize distance despite sorting the draw list.

Preparation now assigns approximately 80% of measured iterator time to the nearest
visible grid awaiting its first detailed snapshot. Once initial snapshots are ready,
the nearest eligible refresh gets that share. The remaining 20% rotates through
visible grids. Priority is recomputed each frame and on foreground completion;
viewport/occlusion filtering runs first. Existing snapshots remain visible and jobs
survive camera turns. Service debt persists across frames so a costly cooperative
step cannot repeatedly consume every frame's allocation and starve background work.

The existing 2 ms soft preparation deadline and uncapped stress-test submission
policy are unchanged. New `independent preparation` events report foreground grid,
distance, measured foreground/background time and steps. This changes convergence
order, not total mesh cost: the dump's mean renderer CPU cost was 38.115 ms and
peak was 613.561 ms, so priority alone cannot promise instantaneous fleet detail.
Synthetic unequal-cost service tests check the 80/20 allocation and background
progress; live latency still requires a world reload and another dump.

These are deterministic synthetic blocks and surface lattices, not captured game
ships. They exercise production core code against a frozen pre-pass reference in
`tools/thermal-performance/BaselineBlockField.cs`. No GPU rendering, billboard
submission or simulation scheduling is measured. Timings are local .NET 9 Release
medians of nine runs after four warmups, with tiered compilation disabled. They are
not predicted frame rates. Allocation figures are bytes allocated per operation,
not retained memory. The timer contributes 40 bytes to each measurement.

Run from the repository root:

```sh
DOTNET_TieredCompilation=0 dotnet run --project tools/thermal-performance/ThermalPerformance.csproj -c Release -p:ThermalBuildRoot=/tmp/thermal-perf-build/
```

Raw measurements: `tools/thermal-performance/results.json`.

For block fixtures, the measured operation constructs the temperature index and
samples every block corner. Dense/hull/sparse/multi-cell use up to 2,880 blocks;
dense-large has 23,040 blocks and 184,320 queries. Shared corners intentionally
exercise the reuse encountered at adjacent thermal cells. Actual BSP cell query
locality differs, so savings must still be checked in live dumps.

## Results

| Fixture | Before ms | After ms | Reduction | Allocated MB before / after |
| --- | ---: | ---: | ---: | ---: |
| dense | 9.999 | 1.681 | 83.2% | 2.38 / 1.76 |
| hull | 1.600 | 0.585 | 63.4% | 1.05 / 0.96 |
| sparse | 1.996 | 1.868 | 6.4% | 3.19 / 1.79 |
| multi-cell | 11.632 | 1.913 | 83.6% | 2.61 / 1.87 |
| dense-large | 90.246 | 16.451 | 81.8% | 18.67 / 11.13 |
| surface-patches | 11.000 | 1.972 | 82.1% | 6.54 / 6.54 |
| traversal-10 | 0.176 | 0.035 | 80.2% | 0.00 / 0.00 |
| traversal-40 | 0.252 | 0.080 | 68.5% | 0.00 / 0.00 |
| traversal-100 | 0.235 | 0.059 | 74.7% | 0.00 / 0.00 |

The surface fixture constructs 400 irregular lattices and their patches. Traversal
fixtures use 8,000 regions, with respectively 632, 7,960 and 8,000 visible leaves.
The traversal benchmark compares hierarchical filtering with filtering each ordered
leaf; the native renderer retains its additional world-AABB camera check, so these
are core traversal measurements, not total native draw costs.

## Retained changes

- Precompute each heat kernel's centre and scale once. Preserve division and
  summation order; no reciprocal approximation or temperature quantization.
- Store kernel indices in spatial buckets instead of duplicating full records.
- Use smaller buckets only when block-count/bounds density warrants it. Keep wider
  buckets for sparse shapes; the density estimate affects speed, not heat values.
- Preallocate from the known block count, with bounded initial capacities.
- Cache exact repeated sample positions, with at most 4,096 entries. Refresh the
  bounded cache when full to retain locality through large grids; disable it after
  a low-hit-rate trial. Clear it on source mutation and bypass caller-dependent
  fallback cases. Independent grids and refreshes never share cache entries.
- Read known face lattice vertices directly and average the four face samples for
  cell centres instead of repeatedly reconstructing trilinear coordinates.
- Prune off-screen BSP subtrees; descendants fully inside the local frustum avoid
  additional tree-level plane checks. Surviving leaf order is unchanged.
- Pre-size publication dictionaries. Earlier temperature-only refresh and release
  of expired transition lattices remain in place.

## Validation

The runner checks exact baseline equality for all block-corner results and 12,000
additional queries across small/large block sizes, variable blur radii, dense/sparse
bucket selection and nonuniform bounds. The 400 surface cases had identical patch
counts, positions and temperatures in this run (the harness permits at most 0.001 K
vector error to account for floating-point coordinate reconstruction). This is
strong synthetic evidence, not a proof for all inputs at merge thresholds.

Regression tests cover cache bounds, invalidation, sparse-query shutoff and
caller-dependent fallback values. Moving/rotating grid and camera cases compare
hierarchical traversal with ordered leaf filtering. Existing thermal tests cover
palette behavior, field bounds, transitions, interior occupancy and occlusion.

## Other strategies assessed

| Strategy | Disposition |
| --- | --- |
| 2026-09-19 | Reviewed the new stress dump and added conservative same-grid subtree occlusion with visibility and removal checks. |
| Always halve bucket width | Rejected: sparse build allocations increased roughly threefold in the exploratory run. Density-based selection retained. |
| Unbounded vertex memoization | Rejected: sparse queries became slower and allocated unnecessarily. Bounded adaptive cache retained. |
| Keep only the first 4,096 cached points | Rejected: poor locality on large ships; refreshing the bounded cache reduced the large-fixture total substantially. |
| More aggressive merging / temperature quantization | Not applied: changes the approved gradients and risks the rejected banding. |
| Distant render quotas | Not applied: user explicitly requested uncapped stress testing and every visible grid covered. |
| Reduce refresh frequency | Not applied: reduces thermal responsiveness; this pass removes redundant work instead. |
| Reuse transformed world vertices across frames | Not applied: moving grids and cameras invalidate them; grid-local geometry already persists. |
| Share temperature caches across grids | Rejected: risks mixing independently heated grids. |
| Background game-entity reads | Not applied: requires a verified thread-safe snapshot boundary; game collections are mutable. |
| Pool all refresh objects | Deferred: needs lifecycle/memory profiling to justify complexity around suspended iterators and retained snapshots. Preallocation is measured and retained. |
| Combine wall blockers / portal occlusion | Deferred: must conservatively preserve holes, windows and partial blocks; current single-solid occlusion remains. |
| Fully opaque mesh rendering / GPU temperature lookup | Requires renderer facilities not established for a standalone Workshop mod; synthetic CPU timings cannot validate it. |
| GPU fill-rate, overdraw and engine billboard capacity | Requires native measurement; core benchmark cannot establish GPU savings or eliminate the shared engine cap. |

This completes the measured candidates in this pass, not a claim that every possible
optimization is exhausted. Further work should target native draw/submission costs
and lifetime profiling using the new cache and visibility telemetry, rather than
extrapolating these CPU microbenchmarks into an FPS promise.

## Follow-up: index allocations and refresh starvation

The nearest-source search tree no longer creates sample lists for empty children.
Lists are allocated only when a leaf receives data and released when it becomes
an internal node. Occupied leaves allocate their eight-sample capacity once,
avoiding the intermediate four-sample array. Differential reference checks still
pass. Compared with the preceding optimized run, allocated bytes for the large
fixture fell from 11,129,151 to 9,822,771 (about 11.7%); the dense fixture fell from
1,760,997 to 1,597,913. These are per-operation allocations, not retained memory.
Raw follow-up results are in `tools/thermal-performance/results-memory.json`.

The native preparation deadline previously used elapsed time since the start of
all thermal drawing, including discovery and occlusion. At 2 ms of preceding work,
refresh would not advance. It now measures its existing 2 ms window from preparation
start. This may add up to roughly 2 ms of preparation to a frame previously starved
by discovery; it prioritizes continued thermal updates rather than claiming a total
frame-time reduction. The deadline remains cooperative, not a hard bound on one
iterator step. No billboard quota was added.

A synthetic scheduling regression supplies 3–7 ms of discovery overhead on every
frame for 60 frames and checks that all 32 grids progress fairly while the
preparation window, idle stop and per-grid iteration bounds remain enforced.
Publication telemetry adds `refresh-wall-ms` (including time between yields, not
CPU time). `discovery-ms` now excludes the separately reported occlusion duration.

## Celestial sampling follow-up

Distant sky discs now prepare their coordinate basis once per body and their sphere
intersection once per ring. A reusable azimuth table replaces per-vertex trigonometry.
The small planet list uses an allocation-free insertion sort instead of a captured
comparison delegate. Climate properties are deliberately still read each frame so
world climate setting changes are not hidden by stale cached definitions.

The 32-disc fixture samples 40,352 vertices with unchanged tessellation. One local
release run measured 1.3421 ms for the original sampler versus 0.0978 ms for prepared
rings (92.7% less sampling time). This is CPU geometry work only: triangle submission,
depth interaction and GPU overdraw are unchanged, and it is not an FPS estimate.
[Recorded results](../tools/thermal-performance/results-celestial.json) include the
other existing benchmark fixtures. The reported 40 allocated bytes per measurement
come from the benchmark stopwatch, not vertex sampling.

An independent reference sampler is retained for numerical regression. Tests cover
100 deterministic body sizes and orientations, 13 rings and 96 azimuths, checking
ray agreement within 1e-12 and surface normal agreement within 1e-9. Projection and
limb tests remain in place. No triangle caps or quality reductions were introduced.

## Interior near-plane allocation follow-up

The live independent renderer now clips near-plane caps into two reusable 16-vertex
buffers. The convex starting quad can gain at most one vertex per clipping plane
(ten vertices for six planes), so the buffers do not grow in ordinary clipping.
The array-returning API remains for lab callers. Temperature blending no longer
samples the current field twice at each cap vertex, and the camera inverse is
computed once per frame instead of once per visible grid.

A frozen pre-change cap implementation checks exact vertex order and coordinates
for 4,096 rotated cameras, alternating full, partial and empty intersections.
Measured cap generation fell from 0.4885 to 0.2357 ms for the whole synthetic batch;
allocated bytes fell from 3,668,984 to 40 (the benchmark stopwatch). A warmed-up
unit test independently verifies zero clipping allocations and buffer reuse across
empty/nonempty cases. Existing near-plane membership tests verify the geometry.
See [the recorded run](../tools/thermal-performance/results-near-cap.json).

This removes CPU and GC work without changing triangles, temperatures or LOD.
The batch is a stress fixture, not an estimate of the number of caps in a live
frame. GPU overdraw and the cloudy interior appearance still need native evaluation.

## Visible face traversal follow-up

The live gradient draw loop now indexes the `IList<Patch>` returned by `Face`.
Previously `foreach` used the interface enumerator and boxed the list's enumerator
once per face. Because the enumerator stores the large current patch struct, this
allocated 160 bytes per face in the offline runtime. Indexed traversal removes it.
Neutral exit faces also reuse the world-space tangent vectors and centre already
computed for their frustum bounds, avoiding three repeated vector transforms.

The benchmark traverses the same 400 synthetic fields 16 times (38,400 faces),
checking identical accumulated temperatures and retaining patch order. It measured
2.9393 ms and 6,144,040 allocated bytes before, versus 1.4511 ms and 40 bytes after.
The remaining 40 bytes are the timing harness. This is traversal only, without
game billboard submission; native runtime timings and allocation sizes may differ.
[Recorded results](../tools/thermal-performance/results-face-traversal.json) retain
all existing benchmark fixtures. Geometry, temperatures and billboard count are
unchanged. Build and the 171-test thermal/whitelist/documentation suite pass.

## Temperature lattice sampling follow-up

Surface interpolation now computes the base corner index, row/layer strides and
complementary weights once. Explicit corner accumulation retains the original
multiplication association and summation order. It changes neither smoothing nor
the auto-range; the same sampler serves refresh transitions and near-plane caps.

The frozen reference and optimized sampler agree exactly for 102,400 deterministic
samples across 400 synthetic fields, including clamped exterior samples. One local
run measured 2.5912 ms before and 1.6575 ms after (36% less sampling CPU time).
Both paths allocate no sampling memory; the 40 reported bytes are the stopwatch.
[Recorded results](../tools/thermal-performance/results-lattice-sampling.json) retain
all fixtures. An independent trilinear polynomial regression covers all 64 lattice
shapes (one through four subdivisions per axis), with 200 samples per shape and
boundary clamping. This benchmark excludes engine rendering and is not an FPS claim.

## Deferred patch-coordinate construction

Temperature error checks now run before computing patch corner positions. Candidate
patches that split do not need those positions: only retained leaves create them.
No visibility-based omission was applied, because a face needed after camera motion
must remain available. The temperature checks, recursion order, extrema and patch
limits are unchanged.

The benchmark retains the immediately preceding implementation in
`tools/thermal-performance/BaselineSurfaceBuild.cs` to isolate this change from
older optimizations. Across 400 deterministic fields it verifies every patch's
coordinates, order, current/previous temperatures and the complete field range.
A local run measured 2.2414 ms before and 2.0039 ms after (10.6% less construction
CPU time); allocations were unchanged at 6,537,104 bytes for the batch.
[Recorded results](../tools/thermal-performance/results-patch-build.json) include
the other fixtures. These are synthetic CPU results, not native frame timings.

## Live fleet dump: projected-size spatial detail

`Thermodynamics_Telemetry_20260919_193657.log` from ToastyBugs, manually dumped at
19:47:45 UTC, records 10,075 captured frames and zero render exceptions. One retained
frame reports 128 visible grids, 13,764 regions, 191,343 billboards and 278.834 ms in
draw, versus 0.066 ms discovery and 2.796 ms preparation. Several small-grid refresh
publications took tens of seconds wall time; a 999-block topology rebuild took
385.9 seconds including time between yields. These are observations, not timings
attributable exclusively to any one optimization. The report contains both lighter
and heavier views; its aggregate is not a controlled before/after comparison.

The live path formerly assigned 512 detail regions regardless of apparent size.
It now estimates diameter in pixels from the grid bounding-box diagonal, camera
focal scale and distance to the closest world bounds. This conservative estimate
can overestimate rotated or elongated grids. Demand is `(diameterPixels / 8)^2`,
rounded up to powers of two between 16 and 512. Existing levels are held while
demand lies between 60% and 140% of the current level. Interior views retain 512.
A level change invalidates partition reuse, while the prior complete snapshot
remains visible until atomic publication and the existing 0.3-second temperature
transition. This does not implement native mesh LOD access or guarantee invisible
geometry changes; native appearance must still be checked.

This is spatial preparation LOD, not a billboard submission cap. All visible grids
remain eligible, and the requested uncapped stress-test path remains uncapped.
Temperature fields still sample block heat continuously; no palette quantization
was added. Large nearby fleets can still submit excessive billboards. The change
specifically targets unnecessary distant detail rather than guaranteeing a global
rendering limit. Publication events now include `detail-budget` and `diameter-px`.
Tests cover monotonic approach/retreat, jitter around a threshold and complete
source-block coverage at region budgets 16 through 512. Live savings are unmeasured.

## Shared face normals

The live smooth-patch path now computes facing, normalization and the float normal
once per planar face instead of for each patch. Patches produced by the rectangular
surface builder have positive area and consistent winding; they share this normal.
Near-plane caps retain their separate triangle calculation. Triangle positions, UVs,
submission order and count remain unchanged. This does not reduce the 191,343
billboard workload observed in the large-fleet dump.

The calculation uses transformed face tangents, which already exist for culling,
instead of subtracting translated patch positions. This reduces cancellation at
large world coordinates. A regression checks 200 rigid orientations, both viewing
sides and 16 patches per face at million-metre coordinates against independent
world-triangle normals, plus degenerate-face rejection. No native FPS improvement
or timing percentage is claimed for this change.

## Partial-face viewport culling

The independent renderer now checks individual rectangular gradient patches when
their parent face crosses the camera frustum. Previously a face touching the
viewport submitted every patch on it. Single-patch faces bypass this extra work;
fully contained faces perform one classification and then skip per-patch tests.
Rejected patches skip interpolation, coordinate transforms and both billboard
submissions. Local bounds have 4 mm padding around the patch, including the
existing 1 mm camera-facing offset. The game face-level frustum check remains.

A synthetic wide face with sixteen patches at ten metres retains four patches:
eight submitted triangles rather than thirty-two. Independent clip-coordinate
sample checks cover twenty rotated camera orientations and displaced vertices,
and verify that visible samples are never classified as off-screen. This is a
fixture result, not a predicted fleet-wide reduction. No occluded-but-on-screen
surfaces are removed by this change, and billboard submission remains uncapped.
The dump adds `offscreen-patches` to the independent draw event. Build and all 181
thermal/whitelist/documentation tests pass; live savings remain unmeasured.

## Triangle reduction with selective partition search

The live surface builder now optionally searches all lattice-aligned binary cuts
of a face, choosing the smallest rectangular patch partition that passes the same
vertex and cell-centre checks. The existing 20 K tolerance and unit-cell stopping
rule are unchanged. The search is bounded by the 4x4 face lattice (at most 100
nonempty rectangles), with memoized costs and an early exit at two patches for a
rejected parent. It cannot increase the baseline patch count. This is a minimum
among tested guillotine partitions, not arbitrary triangle meshes.

Unconditional search was rejected: in the 400-field noisy benchmark it reduced
52,794 triangles to 52,704 but increased construction time from 2.37 to 4.36 ms.
The retained version first builds the existing partition, skips flat faces and
faces already using at least 75% of their unit rectangles, and searches only the
remaining structured faces. On that noisy fixture, it retains 52,794 triangles,
unchanged allocations, and comparable timings (2.33 versus 2.31 ms; not a speedup
claim). Two 625-byte state arrays are allocated only when search is attempted and
reused across that field's faces. Structured refreshes can take extra CPU work in
exchange for reduced recurring draw submissions.

Production geometry exports demonstrate a hot strip going from 10 to 6 triangles
and a cooled patch from 18 to 14. A linear gradient remains at 2. Rendering each
export at 400x400 with a shared temperature range gives a maximum temperature
image difference below 3e-13 K for those fixtures. Other accepted partitions may
differ within the existing error checks; these examples do not prove lossless
simplification for every field. The source lattice, extrema and hotspot samples
are retained, and transitions still use the prior field at the new patch corners.

[Comparison image](../tools/thermal-performance/patch-study/comparison.png),
[exported geometry](../tools/thermal-performance/patch-study/examples.json) and
[benchmark results](../tools/thermal-performance/results-selective-patches.json)
are reproducible with the performance executable and `render_patch_comparison.py`.
`prepared-tri-saved` in grid publication events counts all six prepared faces,
not actual visible billboard savings. Live fleet savings remain unmeasured.

## Transition-safe simplification

The live builder now tests candidate merges against both the newly sampled lattice
and the previous lattice used for the temperature fade. Previously, a new uniform
field could collapse a cooling hotspot to one rectangle before the old endpoint
had finished fading. The old hotspot then had no interior vertex to represent it.
A shared partition now preserves both endpoints at the existing vertex/centre
checks; the convex temporal blend preserves those checked-sample error bounds.
The unit-cell stopping rule and 20 K tolerance remain unchanged.

This may temporarily retain more triangles during heating/cooling. Once both
snapshots are uniform, the next completed refresh returns the face to two
triangles. Stable gradients retain the selective partition savings. The previous
lattice must match bounds and subdivisions (the live resampling already does so);
mismatched input is rejected. The build-only reference is released after planning.
A regression follows an off-centre hotspot through five fade fractions and verifies
its temperature, final uniform simplification and mismatched-lattice rejection.
This is a fidelity correction supporting triangle reduction, not a claimed extra
performance gain.

## Alternate patch diagonals

Selective partition search can now choose the BD diagonal when the usual AC
split fails and BD passes the existing lattice-vertex and cell-centre checks.
Both transition endpoints must accept the same diagonal. The selected diagonal
is stored in the patch and honored by native triangle winding and UV submission.
Default/fallback patches retain AC. The 20 K criterion and unit-cell exception
have not been loosened; this is not a global continuous-surface error guarantee.

A synthetic diagonal crease now uses 2 triangles instead of 20. A sharper crease
is rejected, and an incompatible old temperature field prevents a premature merge.
The new image is not lossless relative to the old triangulation: the 400x400 export
comparison differs by up to 29.7 K in the crease strip, although the candidate
passes the existing 20 K checks against the source lattice. Comparing two different
approximations is distinct from comparing either one to the source. The other three
fixtures remain equivalent to floating-point precision, with their previous counts.

[Four-fixture comparison](../tools/thermal-performance/diagonal-study/comparison.png)
and [geometry export](../tools/thermal-performance/diagonal-study/examples.json)
include the diagonal flag and can be reproduced with `render_patch_comparison.py`.
[Benchmark run](../tools/thermal-performance/results-diagonal-patches.json) retains
the noise rejection fixture. These are synthetic surfaces; live fleet savings and
appearance require verification. Regression coverage includes a dense analytic
crease check, sharper-crease rejection and incompatible transition endpoints.

## Uniform quad investigation (not enabled)

Installed `VRage.Game.MyTransparentGeometry` inspection confirms that the oriented
quad helper allocates from the engine pool, calls `CreateBillboard` and fixes UV
size to one. It does not expose arbitrary per-corner gradient UVs. `CreateBillboard`
also converts tint to linear RGB, whereas the triangle overload stores tint directly.
Replacing the texture with a CPU tint without validating these differences would
risk visible seams. Custom billboards expose more control but require allocation
and render-thread lifetime handling that has not been validated here.

Exact uniform temperatures at both fade endpoints are eligible for a potential
constant-colour quad; temperature quantization is not used. The synthetic hot strip
has one eligible patch (6 to 5 potential billboard submissions), and the cooled
patch has three (14 to 11). Linear/diagonal gradient patches are not eligible.
A quad still renders two GPU triangles. This proposal targets shared billboard
buffer pressure, not GPU triangle reduction. No native quad path is enabled.
[Opportunity counts](../tools/thermal-performance/quad-opportunity.json) distinguish
current/potential submissions and unchanged GPU triangles.

The live draw diagnostic now counts `uniform-patches` after viewport rejection,
only on the once-per-30-frame diagnostic sample. It conservatively checks both
stored endpoints even after a fade has finished. This lets a future dump quantify
the opportunity without altering appearance or billboard counts. Tests cover
uniform endpoints, tiny differences and nonfinite inputs.

## Nearby solid-wall rejection within a grid

The `Thermodynamics_Telemetry_20260920_051131.log` stress dump captured 5,208
frames with no render errors or menu suppression. Renderer CPU averaged
37.959 ms (maximum 732.836 ms). A retained draw event submitted 124,439
billboards across 73 grids: 193.121 ms drawing versus 2.001 ms preparation.
Near-camera cap triangles were zero in that event. Submission remains the
main stress-scene cost; the user-requested uncapped mode remains enabled.

Whole-grid rejection already used nearby, revalidated solid armor. The same
blockers now prune fully hidden subtrees within their own grid before thermal
sampling and billboard submission. Only intact, undeformed vanilla full armor
cubes qualify, with inset bounds to preserve edge visibility. Every corner must
be behind a single solid blocker; separate blockers are never combined across
gaps. Blockers are refreshed each frame, so removal and grid movement do not
leave persistent visibility results. No new engine dependencies enter the
simulation model or presentation library.

The synthetic fixture retains 848 of 4,096 regions, matching the independent
transformed-box reference and preserving near-to-far order. Removing the blocker
restores all original visible regions. Hierarchical rejection took 7.525 ms for
100 traversals, compared with 39.135 ms for flat per-region rejection. These are
CPU traversal measurements, not native FPS or billboard-count predictions.
The telemetry counter `own-grid-hidden-subtrees` counts pruned tree nodes,
not hidden regions. The thermal test selection passed 224 tests.

This optimization targets hidden geometry inside or close to a ship. It does
not reduce legitimately visible ships in open space, and conservative rejection
can leave partially hidden regions submitted. Live savings require a world
reload and a new dump from an interior or nearby-wall view.

## Follow-up dump: persistent activation

`Thermodynamics_Telemetry_20260920_053259.log` records 1,800 rendered frames,
120 UI-suppressed frames, and zero render errors. Chat signals were active from
179.27 through 181.86 seconds, including a period when the framework chat signal
outlasted the game GUI signal. This explains a temporary overlay disappearance;
there is no retained automatic-off event in this dump.

Chat/cursor visibility no longer suppresses passive rendering. Hotkey input
remains separately gated during text entry. Camera changes and HUD reconnection
preserve the request, and a failing render frame no longer disables it. Original
first-person/camera eligibility still suspends rendering in an unsupported view.
Consecutive exception logging is deduplicated to avoid per-frame log/notification
spam while retrying.

Renderer CPU averaged 34.318 ms, maximum 190.948 ms. These are different camera
samples from the prior dump and are not an optimization comparison. Sampled
same-grid hidden-subtree counts were all zero: no live benefit of that rejection
is established here. Open-space draw events still submitted 104,196–124,956
billboards and spent approximately 142–154 ms drawing. Uncapped stress-test
submission remains unchanged. The report also flags inconsistent simulation
clocks; simulation totals must not be treated as reliable frame attribution.

## Change log

| Date | Change |
| --- | --- |
| 2026-09-19 | Reused nearest-search child distance bounds; exact-output query benchmark measured 18.8% less CPU. |
| 2026-09-19 | Short-circuited redundant cap samples with exact-output benchmark and removed duplicate region visibility checks. |
| 2026-09-19 | Inspected installed billboard ordering/UV path and added bounded adaptive thermal sampling to near-camera caps. |
| 2026-09-19 | Refined high-detail single-cell gradient saddles across both fade endpoints; added heating/cooling approximation checks. |
| 2026-09-19 | Reviewed follow-up priority dump and removed small-fleet preparation underuse while retaining the time deadline. |
| 2026-09-19 | Investigated uniform quad packing; added conservative diagnostic eligibility and synthetic opportunity counts, leaving native rendering unchanged. |
| 2026-09-19 | Added transition-compatible alternate diagonals under existing error checks, with rendered approximation comparison. |
| 2026-09-19 | Made surface simplification preserve both temperature-transition endpoints to avoid premature hotspot flattening. |
| 2026-09-19 | Added selective minimum-patch search with unchanged temperature tolerance, rendered comparisons and prepared-triangle telemetry. |
| 2026-09-19 | Added padded per-patch viewport rejection for partially visible faces, fast paths and telemetry. |
| 2026-09-19 | Shared planar-face normals across patches and tested rigid-motion/winding equivalence at large coordinates. |
| 2026-09-19 | Used live fleet dump to add projected-size partition detail with hysteresis, coverage tests and per-grid LOD telemetry. |
| 2026-09-19 | Deferred corner construction until patch acceptance; verified exact patch output against the preceding builder. |
| 2026-09-19 | Reduced repeated lattice index/weight work with exact-reference benchmarks and independent interpolation checks. |
| 2026-09-19 | Removed boxed visible-face enumeration and reused neutral-face transforms; recorded synthetic allocation and CPU comparison. |
| 2026-09-19 | Reused live near-plane clipping buffers, removed redundant cap sampling and measured exact-output allocation savings. |
| 2026-09-19 | Added prepared celestial geometry sampling, independent equivalence tests and recorded CPU measurements. |
| 2026-09-19 | Added reproducible synthetic thermal CPU and allocation benchmarks, reference-output checks and optimization tradeoffs. |
