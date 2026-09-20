# Offline thermal vision lab

The user should not have to reload the world for every development increment. Build, regressions, workload checks and software-depth fixtures run locally before a consolidated engine test is requested. This lab is not a substitute for the engine renderer and does not certify a complete product.

## Sun and distant planets

The default thermal view now submits analytic sun and planet discs independently of voxel
mesh loading and the grid view-distance cutoff. Angular viewport checks reject off-screen
bodies. Planet discs use the registered planet's average radius and climate day/night and
latitude targets; missing climate data remains neutral. The sun saturates the active palette.
Neither contributes to automatic exposure, preserving ship contrast in both colour and grey.

This is a distant-sky approximation: it does not simulate surface materials, weather or thermal
lag. Nearby opaque terrain keeps the existing depth treatment. The solar disc uses vanilla's
outer angular size; customized environment sun sizes are not yet read. Far-to-near planet
submission puts nearer discs over farther bodies and the sun. Foreground opaque depth is
retained, but interaction with native distant planet impostor depth still needs a live check.
Do not treat this as verified temperature mapping of close planetary terrain.

Tessellation follows angular size, with at most 2,208 triangles per visible planet and 96 for
the sun. Vertex storage is reused; no voxel mesh extraction or terrain raycasts are performed.
Telemetry records registered planet count, submitted sky triangles and sky-plane depth.
Three core regressions cover angular limb geometry, camera projection and distant visibility;
the installed-game API probe checks celestial member access.

## Preserved regional renderer — rejected as the complete presentation

The native regional renderer remains executable as `/thermal vision lab regions grey` or
`/thermal vision lab regions colour`. The former `/thermal vision regions ...` commands are
compatibility aliases. Activation and the panel explicitly label it a lab. Use `/thermal vision off`
to close; all existing cleanup and suit/camera eligibility rules still apply. No files are removed,
no game installation changes are needed, and the synthetic regression fixtures remain executable.

Preserved components include native-depth boundaries, disjoint max-temperature regions, adaptive
coarsening, surface-anchored LOD, viewport culling, exposure, telemetry and cooled-patch replacement.
The user confirmed that local cooling was distinguishable, but rejected the overall flat presentation.
The `20260919_040642` dump has 2,268 frames, zero errors, 2.132 ms mean adapter CPU and 243 populated
2.5 m cells in the retained nearest band. Native appearance remains below the product requirement.
This is a useful backend/measurement baseline, not a completed visual feature.

The current acceptance priority is a clear temperature gradient; surface definition can be approximate.
Do not count higher triangle counts, configured LOD sizes, or passing offline tests as visual acceptance.


## Run

```sh
dotnet build tests/Thermodynamics.slnx -p:ThermalBuildRoot=/tmp/thermal-vision-build/ --no-restore -m:1
dotnet test tests/Thermodynamics.Tests/Thermodynamics.Tests.csproj -p:ThermalBuildRoot=/tmp/thermal-vision-build/ --no-build --no-restore --filter 'FullyQualifiedName~ThermalVision|FullyQualifiedName~ScriptWhitelistTests|FullyQualifiedName~DocumentationTests'
dotnet run --project tests/Thermodynamics.Sim -p:ThermalBuildRoot=/tmp/thermal-vision-build/ --no-build --no-restore -- thermal-vision /tmp/thermal-vision-lab
```

The output directory must be outside the published mod tree. `report.md` contains measured offline extraction work and explicit product gaps; `preview.html`, `colour.svg` and `grey.svg` show synthetic depth fixtures. `adaptation.csv` records a deterministic 360-frame hot/cold/viewpoint-change sequence. Use `--require-complete` after the output directory to return exit code 2 for the current incomplete full-scene product. Without that flag, zero means only that lab assertions completed; unexpected exceptions fail the run.

## Shared implementation

`ThermalVisionMeshBuild` is used by both the scene's resumable job and the aimed-block extraction path. The game supplies an `IMyModel` adapter; tests supply deterministic triangles and material rejection. The builder reads each source index exactly once, bounds allocation, retains work across slices and never publishes a partial model. Source counts include the 26,914 and 61,199 cases observed in game. These fixtures reproduce workload sizes, not the proprietary asset geometry or an exact camera trace.

`ThermalVisionGeometry.Project` performs the actual early backface rejection, world transform, degeneracy check and surface offset before the native billboard call. The software oracle consumes those results and independently rasterizes depth. A cold opaque plate must hide a hotter rear plate regardless of submission order; a visible hot slope supplies contrast. Missing-temperature/background pixels remain explicitly unmodelled. These images are regression fixtures, not claims about the final art direction.

The production palette, exposure, viewpoint state and eligibility policy run without the game. `ThermalVisionViewpoint` consumes the engine adapter's view facts; tests reject third-person, spectator/turret ownership, seated/remote control, missing client context, death, inactive/broken/closing cameras and missing/closing characters. Camera loss must disable the optics until explicit reactivation. Tests also cover both palettes, luminance ordering, frame-rate-independent adaptation, empty/invalid sample sets, smoothing and reset. The engine's reporting of these facts and actual camera ownership still require integration validation; synthetic policy tests do not establish those inputs.

## Conservative batch rejection

Revision 8 builds plane bounds for contiguous groups of up to 64 retained triangles during the same incremental extraction. A group is rejected only when its interval bound proves all member triangles face away from the eye; uncertain, grazing or degenerate groups retain individual processing. Mirrored/singular transforms bypass the local-space optimization. No triangle positions, silhouettes, holes or material classifications are approximated.

The lab includes a closed, non-overlapping 62,208-triangle tessellated hull, tested against an independent double-precision face oracle from 26 camera directions. An axial view retains 10,368 candidate triangles; a three-face view retains 31,104. These fit the full 32,768 examination allowance individually, although other scene geometry can consume the remaining budget. A rotated smaller hull must produce a pixel-identical software image with and without batch rejection. `hull.svg` is the fixture image. These are controlled synthetic results, not a game benchmark.

## Reconstructed sensor study

The game now includes the [survey-scope test candidate](thermal-vision-survey.md). Its `ThermalVisionRayScan<T>` scheduler and `ThermalVisionSensorOptics` projection/presentation helpers run in the shared core. Offline tests exercise out-of-order, duplicate, foreign and superseded completions, hard per-frame/outstanding bounds across repeated resets, private complete-frame publication, off-centre perspective rays, rotated cameras and hatched unknown/no-return semantics. These are actual candidate helpers; the analytic ray scene itself remains a substitute for game physics.

Run `dotnet run --project tests/Thermodynamics.Sim -p:ThermalBuildRoot=/tmp/thermal-vision-build/ --no-build --no-restore -- thermal-vision-rays /tmp/thermal-vision-rays` to produce `preview.html`, both palette SVGs at four resolutions, and `report.md`.

This is a new renderer feasibility fixture, not production renderer code or an engine performance simulation. It traces analytic boxes, a sphere and a ground plane. A foreground plate must hide the hotter reactor behind it irrespective of submission order. Terrain, rock and a character-shaped box carry explicitly illustrative temperature estimates; no-return pixels remain non-quantitative background. The common range is locked to 225–625 K so missed hot pixels cannot silently change the comparison's exposure.

At 10 complete images per second, 64×36 costs 23,040 queries/s, 96×54 costs 51,840, and 160×90 costs 144,000. Under a hypothetical 2 ms per game frame at 60 FPS, the 96×54 case allows only 2.31 µs/query on average **before** temperature lookup, drawing, scheduling and allocation. No native query timing is measured here.

Nearest-neighbour enlargement of those images disagrees with the 640×360 reference's surface IDs on 1.21%, 0.75% and 0.41% of its pixels respectively. These aggregate figures hide a serious defect: all three low-resolution grids miss all 44 reference pixels of the thin hot pole. The reference is itself finite resolution, not a proof of continuous coverage. A held 10 Hz image can approach 100 ms of age; at 90 degrees/s camera rotation that is nearly 9 degrees of misalignment. Depth reprojection and selective new samples merit research, but cannot recover a completely unsampled object or guarantee correctness during motion.

These results support investigating a sensor-style reconstruction with explicit fidelity compromises. They do not establish professional appearance, renderer access, native HUD composition, game collision fidelity, or acceptable performance. The user asked to explore compromises, not to silently remove the full-scene requirement.

## Temporal sample reuse experiment

Run `dotnet run --project tests/Thermodynamics.Sim -p:ThermalBuildRoot=/tmp/thermal-vision-build/ --no-build --no-restore -- thermal-vision-reuse /tmp/thermal-vision-reuse` for the report and per-update CSVs. This is offline research, not another game mode or a request for another reload.

`ThermalVisionReuseLab` starts with a fully sampled analytic scene (warmup queries counted separately), reprojects cached world points, selects nearest cached depth, repairs empty pixels first and uses leftover queries for round-robin refresh. It permits at most 128 new queries per update over 60 updates. The static-scene reference is evaluated separately and does not inform sample selection. Cases cover a stationary view, 90 degrees/s rotation, and a new cold foreground occluder appearing at update 20. They do not test translation, skinned meshes, engine collision fidelity or native renderer timing.

The stationary control is exact at both 64×36 and 128×72. That success does not carry over to movement. At 128×72, point reuse leaves up to 8,522 pixels without a sample during the pan; the new-occluder case has up to 114 hot cached pixels showing where the cold occluder should be. Widening each sample to a simple 3×3 footprint reduces peak pan holes to 2,993, but increases peak wrong-surface pixels from 69 to 301. The two metrics must be considered separately: wrong-surface counts exclude holes. After one second, the widened-footprint occluder case still has 391 wrong-surface pixels.

These are counterexamples for two simple reuse strategies, not a proof that all temporal reconstruction is impossible. They show why enlarging cached samples or passing only a stationary test cannot establish the integrated feature's acceptance. A better design needs visibility-aware surface reconstruction and invalidation of occluded/newly visible regions; those capabilities have not been demonstrated here. The new tests check the query bound, stationary control and that the adversarial fixtures actually expose stale heat and camera holes. Passing those tests validates the experiment, not product readiness.

## Readiness gates

Full-scene coverage must pass without a client plugin. The user explicitly rejected a reduced-coverage standalone fallback; an optional plugin cannot turn a failing standalone result into a pass. The [renderer audit](thermal-vision-design.md#distribution-requirement-and-renderer-blocker) records the unresolved API boundary. Do not request another routine in-game dump to resolve that missing architecture.

Full-scene readiness currently fails. In particular, a fully front-facing opaque model with 61,199 triangles exceeds the scene's 32,768-triangle examination limit, even after extraction succeeds. Large candidate sets exceed the block-attempt cap. Frustum membership is not pixel visibility: hidden surfaces can consume budget and affect exposure. The native implementation still lacks terrain, characters, sky, transparent/cutout surfaces and deformed armour.

Offline milliseconds are CPU measurements from a different runtime and synthetic sources. They do not predict the game GPU, billboard allocation cost, draw-call batching or pauses such as the observed 211 ms stall. Aggregate telemetry has insufficient information to reconstruct that stall or the original camera path. Slow-model events and staged timings help eventual diagnosis but do not assign a cause.

Before another routine user test, resolve as many of these gates as the permitted engine APIs allow and produce one reviewable candidate with an explicit remaining engine checklist. The eventual engine test must verify native whitelist acceptance, depth/material composition, suit/camera restrictions, geometry alignment and GPU/frame-time behavior. No synthetic success may be reported as those checks passing.

## Native-depth compositor checks

The new engine experiment is described in the [renderer design](thermal-vision-design.md#ordered-native-depth-layers-new-engine-experiment).
`ThermalVisionTests` checks its shared near-to-far plan, terminal background, rotated/off-centre
frustum coverage across aspect ratios, and an independent scalar depth/blend model that fails
when layer order is reversed. These checks do not execute the native shader or establish GPU
cost, transparent-surface coverage, or temperature assignment.

## Native material API check

Run `python3 tools/thermal-api-check/run.py /path/to/SpaceEngineers` from the repository.
It requires .NET 9 and `ilspycmd` (on PATH or at `~/.dotnet/tools/ilspycmd`); `--ilspy` overrides
that path. Generated bootstrap source, project, assemblies and `report.log` go into a fresh
`/tmp/thermal-api-check-*` directory, outside the published mod. Installed files are read only.

The tool decompiles `MySpaceGameDefaultIlChecker` locally and runs the installed whitelist
analyzer on six focused C# 6 snippets. It reproduces the game/API grants; a small framework
subset avoids runtime-version differences in `Delegate` and `RuntimeType`. Namespace duplicates
caused by framework assembly consolidation are deduplicated only when prior permission covers
the later target; unexpected changes fail. It does not alter the game's whitelist, execute the
snippets, or load them into the mod. Reflection is confined to this offline host to instantiate
the internal analyzer and obtain its compilation references.

Four patterns pass compilation and analysis: entity texture override, voxel render-state
copy/restore, scratch definition initialisation from a base builder, and XML-to-base-builder
access. Two negative controls compile but must receive prohibited-member diagnostics: direct
voxel texture-field mutation and naming the concrete DX11 builder type. The runner exits nonzero
if compilation fails, analyzer execution fails, or expected acceptance/rejection changes. This
is narrower than a real session test: no native XML deserialization, material rendering,
restoration, GPU cost or whole-scene coverage is claimed. Existing framework-only whitelist
tests do not replace this check.

## Temperature ownership study

The ownership study runs with `dotnet run --project tests/Thermodynamics.Sim -- thermal-vision-volumes /tmp/thermal-vision-volumes`.
It writes a report and intentionally exits 2 because the feature remains incomplete. The three
candidates use the current production depth-layer spacing where relevant, but their temperature
assignment algorithms are offline experiments. Reference opaque objects and coarse temperature
cells are separate; the analytic hit result's owner is used only to score results. No candidate
is credited for mapping a cold or unknown foreground surface to a hot cell merely because it
preserves the silhouette. `ThermalVisionLabTests` pins isolated, foreground and open-frame cases.

## Composite native validation

The new `composite colour|grey` experiment combines two neutral native-depth planes with measured
model triangles. Follow the consolidated protocol in [the design](thermal-vision-design.md#native-depth-and-measured-surface-composite-experiment).
Offline geometry tests do not validate PostPP order, display-space colour, depth precision, viewport
coverage or GPU performance. No synthetic success should mark these native gates as passed.
The API checker now includes permitted voxel enumeration and prohibited model-material enumeration.

## Approximate regional heat proof

`thermal-vision-volumes` now also runs `ordered-box-faces`: six region boundary polygons sorted by
centre depth and clipped against analytic original-scene depth. The initial isolated/foreground tests
pass, while foreign surfaces inside a region inherit its heat. This is relevant to the user's revised
block-group accuracy target; rotated, overlapping, camera-inside and motion cases remain prerequisites
for a native trial. The command still exits 2 because its original strict ownership gate fails.
See [the revised design](thermal-vision-design.md#accepted-approximation-and-regional-rendering-direction).

Optional read-only model inventory:

```sh
python3 tools/thermal-material-audit/audit.py "$GAME/Content/Models/Cubes" /tmp/thermal-material-audit/cubes.json
```

The tool never executes engine code or changes render state. It records indexed MWM material names,
techniques, texture paths and geometry-asset references; it does not automatically resolve references
or translate glass aliases through the live material registry. Eight legacy files in the audited game
lack version headers and are rejected explicitly (exit 1). Totals cover all files including LODs and
construction variants, not the triangles visible in a gameplay scene. The API checker also tests the
allowed binary-content reader and rejected cube-grid render-data access.

## Region stability stress checks

The volume report also includes nested overlapping regions, an inside-region camera, and adjacent
regions. Raw overwrites are compared with the shared disjoint-region builder plus an analytic near-plane
seed. Separate tests exercise the real clipped near-cap polygon at three camera rotations, maximum-heat
field values in both insertion orders, and transactional capacity failure. This is preparation and
software-depth evidence only; it does not establish general face ordering or native visual quality.

## Spatial ordering verification

`ThermalVisionRegionOrderTests` compares spatial-tree traversal against independent ray/box intervals
for 1,200 rays and compares 19,200 field samples before/after fragmentation. It includes an inside-field
viewpoint, capacity failure, empty input and a 512-region aligned workload. It validates ordering and
field preservation, not native polygon precision, GPU timings or whole-world aggregation.
See [spatial ordering preparation](thermal-vision-design.md#spatial-ordering-preparation).

## Regional scan workload

`ThermalVisionRegionScanTests` checks fair work across sources, conservative maximum-temperature
aggregation, complete-only publication and failure cleanup. Its dense fixture has 113 sources and
265,324 samples; it requires 131 advances at 4,096 logical work units per advance. No wall-time or GPU
performance assertion is made. This exposes refresh latency instead of hiding it behind a successful
coverage count. See [sampling and publication](thermal-vision-design.md#fair-regional-sampling-and-publication).

## First regional native trial

The `regions colour|grey` mode is now wired to the shared sampler, spatial ordering and near-cap
geometry. Follow [the consolidated native protocol](thermal-vision-design.md#regional-renderer-ready-for-the-first-native-test).
The completed-scan integration test validates ordering connectivity and the standalone billboard
ceiling; actual GPU visibility, resource sharing, cooling feedback and frame rate still require this trial.

## Regional capacity regression

The first native region dump never produced a field: 5/10/20 m attempts exhausted capacity and the
20 m retry loop could not recover. The adaptive scan regression now distributes 8,000 samples over
more than 1,536 distinct 20 m cells. It verifies completion without rereading sources, max-temperature
preservation including negative coordinates, and complete-only publication after in-scan coarsening.
It does not treat the resulting group size as proof of acceptable extinguisher targeting accuracy.

## Fine temperature focus regressions

`ThermalVisionRegionOrderTests` verifies that a 300 K cooled cell replaces a 700 K coarse group,
a neighbouring 600 K cell remains distinct, unmeasured gaps stay unmeasured, and exterior context is
preserved. A dense 512-cell fine volume crossing coarse boundaries fits alongside 512 coarse cells
within the native fragment ceiling. A negative-coordinate capacity failure returns no partial field,
preserves its source scans, and can be retried successfully with adequate capacity. These checks
exercise the shared production partition and ordering logic, not native GPU appearance.

## Distance LOD regressions

The shared `ThermalVisionRegionLod` is exercised through the production partition and ordering code.
An approach fixture observes the same hot/cold pair at 100 m, 30 m and 3 m: distant cells use the
hotter group value, while close cells resolve the cool neighbour separately. A fully occupied volume
at negative coordinates forces per-band coarsening, stays within the fragment ceiling, and preserves
single ownership and temperature across both band boundaries and outer context. Neither test proves
smooth visual transitions; the design page provides the native approach/retreat protocol.

## Surface focus regression

A 30 m hot/cold pair lies outside the previous camera fine volume. The surface-anchored LOD test
checks both temperatures after ordered composition and asserts a populated fine band. Parameterized
approach checks cover 2.5, 5, 10, 20 and 40 m target cell sizes. Native surface selection and perceived
transition quality still need an in-game test.

## Temperature-gradient partition study

Run `python3 tools/thermal-gradient-study/study.py /tmp/thermal-gradient-study` for the reproducible
CPU-only estimator comparison. It writes `metrics.json`, `report.md` and `slices.svg`. These are
synthetic scalar-field slices, not predicted in-game screenshots. All panels share a fixed 290–850 K
white-hot display scale. Each fixture contains 32³ equal-volume, axis-aligned block samples.

Compare fixed four-block-wide maximum cells (512 cells), an error-prioritized octree (up to 512 or
1,536 leaves), and a binary partition choosing the axis with the lowest resulting squared peak error
(up to 512 leaves). Output temperatures remain leaf maxima; improvements come from partition shape,
not averaging away hot samples. The study independently rasterizes output boxes to verify non-overlap,
complete coverage and agreement with sample assignment. It also checks capacity, peak conservation
and mixed-fixture input-order invariance.

| Fixture | Fixed 512 mean error K | Octree ≤512 mean error K | Directed ≤512 mean error K |
| --- | ---: | ---: | ---: |
| Axis gradient | 12.000 | 12.000 | 0.000 |
| Single hot block | 0.961 | 0.000 | 0.000 |
| Cooled patch | 0.684 | 0.000 | 0.000 |
| Axis gradient with hot and cooled patches | 9.209 | 9.209 | 0.000 |
| Diagonal gradient | 18.000 | 18.000 | 18.000 |
| Rounded hotspot | 22.468 | 20.666 | 35.968 |
| Alternating hot/cold blocks | 250.000 | 250.000 | 250.000 |

Directed partitioning reproduces the first four fixtures using 32, 16, 80 and 188 leaves respectively.
It removes the 63 falsely hot neighbours around a single hot block and recovers all 64 cells of the
cooled patch. However it does not improve the diagonal fixture, and its greedy squared-error splits
increase mean error on the rounded fixture. All methods fail the checkerboard at this budget.
Do not describe this as a universally better estimator or claim native visual acceptance.

The batch study stores all points and repeatedly allocates child lists. It is deliberately not called
from game code. Before an engine candidate, investigate variance/error priority, grid-local partitions,
surface sampling, continuous-value approximation, temporal stability, resumable preparation, world
boundary conversion and actual renderer fragmentation. Source bounds and volume weighting must replace
point-only ownership when using real blocks; empty space and overlapping grids are not modelled here.

## Corpus visual evaluation panel

Visual appearance is the first acceptance gate. `thermal-corpus-export` selects three vanilla,
single-grid corpus ships: AM-200 Atmominer (2771284480, 200 blocks), (Cylon)-Heavy Raider
(1650715158, 1,199 blocks) and Pegasus Chrysaor (605516903, 4,064 blocks). These cover a compact
miner, medium combat design and larger hull. Selection is a visual panel, not a representative
statistical sample of the corpus.

The miner and Pegasus use `Battery.All()`'s `full-electrical` load/environment for 60 simulated
seconds. The Raider uses `recovery`: 60 seconds at its Everything load, then 60 seconds idle,
with snapshots before and after idle. These are explicitly fixed-clock shortened scenarios,
not full equilibrium battery outcomes. The existing `ShipAssembly`, `ShipLoad`, `AssemblyRunner`
and installed game block definitions supply all temperatures. No temperatures are invented for
visual effect. Each JSON snapshot records the blueprint path, SHA-256, workshop ID, phase, clock,
block bounds, block type, grid scale and per-node kelvin.

Reproduce after building the Sim project:

```bash
dotnet /tmp/thermal-vision-build/Thermodynamics.Sim/bin/Debug/net9.0/Thermodynamics.Sim.dll thermal-corpus-export /tmp/thermal-corpus-visuals/data
python3 tools/thermal-gradient-study/corpus_render.py /tmp/thermal-corpus-visuals/data /tmp/thermal-corpus-visuals/gallery
python3 -m unittest discover -s tools/thermal-gradient-study -p 'test_corpus_render.py' -v
```

The gallery contains 16 PNG comparison sheets: four snapshots × exterior/cutaway × white-hot/Cividis.
Each sheet uses one camera, geometry and temperature scale for all three columns: per-block reference,
uniform maximum groups, and directed maximum groups. Temperature ranges span all simulated node
values and remain fixed across a ship's phases, with at least a 20 K span and small padding. The
Cividis table is read from the mod's actual palette source. Each estimator has a maximum 512-cell
budget; uniform powers-of-two cells may use fewer (counts are printed, not hidden).

These are offline block-bound geometry proxies, not game screenshots. Slopes, detailed meshes,
transparent materials, lighting, postprocessing and the native volume compositor are not reproduced.
The renderer uses orthographic projection and a triangle depth buffer. A fixed 0–12% face-shading
cue is applied equally to all candidates as an illustrative appearance treatment, not an implemented
in-game shader. The cutaway removes blocks whose centres lie beyond the grid midpoint on Z; it is
labelled and is not a through-wall thermal feature. Directional estimates are evaluated at block
positions and then assigned to their proxy surfaces, not rendered as actual native volume boundaries.

The renderer tests check foreground occlusion in both submission orders, bounded shading and
estimator input-order consistency. The panel already exposes a candidate weakness: directed mean
block-temperature error on Pegasus is about 93.7 K versus 88.7 K for uniform grouping, despite using
more leaves. On the loaded Raider it improves from about 25.6 K to 9.0 K. Do not promote the candidate
based on the favourable ship alone. Reject poor visual candidates here before native testing; an
accepted offline image still requires later native rendering validation.

## Exact block-bound native probe

`/thermal vision lab blocks grey` or `colour` selects a new single-grid probe. It chooses the nearest
viewport-intersecting thermal grid within 500 m, orders its block bounds in grid-local coordinates,
then transforms the faces into the live grid pose. Temperatures are not aggregated, quantized or
averaged; spatial splitting for ordering preserves the source block temperature. This avoids
world-aligned expansion of rotated blocks. It does not reproduce exact block meshes or establish a
pixel-exact grid mask: glass, model overhangs and other surfaces inside a block bound remain caveats.

The initial native limit is 1,536 source blocks and 1,536 ordered fragments. A larger nearest grid
or a split that exceeds capacity leaves normal view active with a notification; it never publishes
an arbitrary block prefix. Other grids remain neutral context while a complete selected field is
shown. This is explicitly a bounded lab probe, not full-scene completion. The field rebuilds at most
twice per second; pose transforms update every draw. Preparation is currently synchronous and requires
native CPU measurement before expansion to larger fleets.

Faces use oriented quad billboards instead of two triangle billboards. Near-plane caps still use
triangles. With at most six face quads and eight cap triangles per fragment plus two context planes,
the conservative ceiling is 21,506 submissions, below the installed 32,768 shared limit. Other mods
still share that limit. Both palettes, auto/manual range, suit/camera gating, off, menu suppression,
exception cleanup and world reset use the existing lab control path.

`thermal-corpus-export` now also orders the exact grid-local bounds for each snapshot and checks
that each original block centre belongs to exactly one output fragment with its original temperature.
The larger offline audit limit is 16,384 fragments; it does not imply native permission to draw all.

| Corpus ship | Source blocks | Ordered fragments | Face triangles | Face quads |
| --- | ---: | ---: | ---: | ---: |
| AM-200 Atmominer | 200 | 262 | 3,144 | 1,572 |
| Heavy Raider | 1,199 | 1,278 | 15,336 | 7,668 |
| Pegasus Chrysaor | 4,064 | 4,863 | 58,356 | 29,178 |

Counts exclude near caps and other engine users. They demonstrate that per-block temperature is
not ruled out by submission count for every grid; they do not demonstrate GPU appearance or speed.
No equal-temperature merging or hidden-surface rejection is implemented in this probe yet.

For appearance review before any game test, append `--flat` to `corpus_render.py`. This produces
unshaded comparison sheets, removing the earlier illustrative face cue. The per-block reference
column is the target; candidate columns still depict the older grouping study. These are not images
captured from the new native probe. Inspect the flat reference first, then optionally reload and use
the new command on a small grid. The current Pegasus exceeds the native probe's initial limit.

## First accepted native per-block appearance

The user reports that the per-block result looks good once the grid appears, supported by the
colour screenshot and `Thermodynamics_Telemetry_20260919_051818.log`. This is an accepted visual
baseline for the displayed grid, not evidence of full-scene coverage or completed loading behaviour.

The dump records 2,992 captured frames and zero render errors. The retained publications contain
999 source blocks and 1,124 ordered fragments, refreshed about every 0.5 seconds. Adapter CPU averages
1.532 ms overall (1.692 ms on per-block rows), with a 23.880 ms maximum; GPU completion is unmeasured.
There are 293 normal-view/unavailable-field frames and 100 zero-geometry submissions among the
per-block rows. The retained event window does not establish the cause of all those frames. Do not
attribute them to slow acquisition, capacity rejection or target switching without additional evidence.
The renderer still selects one nearest grid and rejects over-budget grids; those limits are relevant
when investigating the user's "when the grid loads in" observation.

Preserve this visual treatment while addressing acquisition/selection continuity and fleet coverage.

## Viewport fleet extension after the three-grid screenshot

The user accepted the foreground per-block appearance but identified two neutral grids in the same
viewport as unacceptable. The original single-grid selection caused that absence by design.
`/thermal vision lab blocks colour|grey` now selects a viewport fleet using the active game camera frustum instead.

`ThermalVisionFleetBudget` reserves up to 64 cells per visible grid before distributing remaining
capacity in proportion to projected-size estimates (bounding radius squared / squared surface distance).
The source budget is 1,400; the final disjoint/order limit remains 1,536. At least one cell is reserved
per grid under extreme counts; more than 1,024 visible eligible grids is explicitly rejected, not
silently truncated. Uniformly tempered or very coarsely sampled grids can still appear flat; the
system does not invent a gradient where samples do not provide one.

Each grid is scanned fairly with round-robin 128-unit slices, a 32,768-unit frame ceiling and the
existing soft 2 ms acquisition allowance. Exact block bounds are kept when they fit that grid's
allocation and ordering budget. Other grids use independently coarsened local cells. No grid gets
dropped merely because the foreground grid is expensive. Full-field partitioning and ordering are
still synchronous and can exceed the soft budget; native telemetry must measure the preparation spike.

Fields are transformed into the dominant grid's basis and combined into one disjoint, globally
ordered field. The dominant grid retains its exact block orientation. Differently rotated secondary
grids currently use conservative bounds in that shared basis; overlapping estimates take the hotter
value. That can spread secondary heat and is a remaining approximation, not an exact multi-grid mask.
If exact composition exceeds capacity, a complete all-coarse composition is attempted. If that also
fails, normal view and an explicit capacity status replace the incomplete field. No prefix is published.

The previously published field can remain during acquisition when its poses/topology remain valid.
Movement/topology/age checks invalidate stale data; rapidly moving grids can delay acquisition.
New viewport grids acquire on the next complete scan. Events report fleet grid count, exact/coarse
counts, fragments and individual grid allocations and cell sizes. The initial 1,536-block single-grid
limit is replaced by these per-grid and combined budgets; large grids may now receive coarse coverage.

Regression checks combine a 1,000-block exact foreground grid and two independently coarsened distant
grids, verifying unique ownership and expected temperatures for every output region within the native
billboard ceiling. Budget checks cover a dominant foreground grid, 1,000 simultaneous grids and refusal
of impossible allocations. These establish field coverage offline, not native visual acceptance.
Reload, reuse `lab blocks colour`, and capture all three grids plus `/thermal dump` for native validation.

## Game view distance and native LOD

The block fleet lab now uses `IMyCamera.IsInFrustum` without a separate 5 km radial cutoff.
This includes the camera's far plane and keeps grids intersecting the viewport's distant corners;
a sphere with the same radius would incorrectly exclude some of those grids. The black sky-clearing
plane follows `IMyCamera.FarPlaneDistance`, just inside the clip boundary. The installed interface
identifies that distance as the session `ViewDistance`; session settings provide a fallback if the
camera reports an invalid value. HUD and publication telemetry report the effective distance.
Changes to the camera reach are read live. Newly visible grids join the next complete acquisition.
This covers locally available, simulated grids; increasing rendering distance cannot create thermal
data for grids the server has not replicated. Archived region/depth/composite diagnostics retain
their original fixed ranges.

Native mesh LOD remains authoritative for silhouettes because thermal quads read the game's original
depth buffer. Local inspection of `VRage.Render11.GeometryStage2.Lodding.MyLodStrategy` shows that
actual model LOD selection also uses per-model thresholds, distance preprocessing, global offsets
and multipliers, plus transition hysteresis. Its current/transition LOD is renderer-internal, not an
exposed grid-level ModAPI setting. A grid can contain many models at different LODs; copying one
nominal distance ladder would not synchronize it with that renderer.

Consequently thermal LOD remains separate: all visible eligible grids reserve a coverage budget,
then apparent-size estimates allocate additional block/group detail. This does not override native
mesh LOD or claim to read the graphics quality setting. Existing complete-field publication prevents
half-built levels appearing; smooth crossfades and thermal allocation hysteresis are not implemented.
The increased view distance does not raise the 1,400-source/1,536-fragment limits. More visible grids
can reduce foreground detail or expose the existing field-capacity fallback. Native validation should
include grids beyond 5 km, approaching those grids, changing view distance, and camera zoom.

## Selected candidate: mixed block detail with mean groups

The accepted native per-block appearance is the foundation. `/thermal vision colour` (or `color`)
and `/thermal vision grey` (or `gray`) now select the viewport fleet candidate. `/thermal vision
lab blocks colour|grey` remains an alias. The original aimed-block probe is preserved at
`/thermal vision lab target colour|grey`; regional, depth, survey and composite labs are retained.
`/thermal vision off` closes the view. This is the primary test candidate, not a claim that the
full-scene product acceptance gate has passed.

Evaluation found two damaging discontinuities: a large grid lost all block-level detail when it
exceeded its budget, and maximum aggregation spread small hotspots across whole coarse cells.
The implementation now:

- reserves coverage for each visible eligible grid and redistributes allocations above small-grid
  demand; caps apparent-area weights to prevent an enclosing grid consuming almost all detail;
- scans complete coarse coverage, then uses a second resumable pass at the established cell size
  to compute block/cell-intersection-volume-weighted temperatures, without restart/coarsening bias;
- retains exact full-grid block temperatures when ordering fits the allocation;
- otherwise replaces coarse estimates around the nearest eligible visible blocks with exact values,
  including cooler readings after extinguishing; replacement failure preserves the previous field;
- retries fewer refined blocks if ordering overflows, then falls back to complete coarse coverage;
- binds local samples to current grid transforms at publication, so rigid motion no longer cancels
  acquisition. A single-grid published field follows its grid each frame. Independently moving
  multi-grid fields can still invalidate and temporarily return to normal view;
- uses a compact palette/range/view-distance HUD, keeping detailed counts and costs in telemetry.

Per-grid preparation retains at most its allocation in exact candidates plus allocation/12 nearest
candidates. Coarse acquisition targets allocation/4 cells and local replacement uses half the
allocation to reserve ordering room. These are bounded approximations, not a smooth LOD crossfade.
The global 1,400 allocation, 1,536 ordered-fragment ceiling and native billboard estimates remain.
Two scan passes increase acquisition latency; global composition is still synchronous. Both need
native cost measurements. The same block count cannot detect all topology replacements during a scan.

Volume means can understate a small hotspot at long distance. That is an intentional tradeoff for
readable gradients without painting a whole region at its hottest sample. Near refinement restores
individual readings, but does not promise every close block is exact on a very large visible fleet.
Secondary rotated-grid bounds can still spread temperatures. Terrain, characters and unavailable
thermal data remain unmeasured. These gaps prevent calling this a finished full-scene thermal camera.

### Reproducible appearance evaluation

`thermal-corpus-export` now calls the same `ThermalVisionBlockDetail` and mean scanner used in-game
at allocations 128, 512 and 1,400. It checks unique block-centre coverage and ordering limits before
exporting estimates. `tools/thermal-gradient-study/mixed_render.py` makes eight colour/greyscale
comparison sheets across three ships and four solver snapshots. Each shows the exact reference and
three allocations on the same flat, unlit block-bound geometry and shared temperature range.
The three allocations are independent per-grid scenarios, not a simultaneous fleet capacity claim.

```sh
dotnet /tmp/thermal-vision-build/Thermodynamics.Sim/bin/Debug/net9.0/Thermodynamics.Sim.dll thermal-corpus-export /tmp/thermal-mixed-corpus
python3 tools/thermal-gradient-study/mixed_render.py /tmp/thermal-mixed-corpus /tmp/thermal-mixed-gallery
```

Measured mean absolute block-centre error, Kelvin:

| Corpus snapshot | 128 allocation | 512 allocation | 1,400 allocation |
| --- | --- |
| 2026-09-19 | Added executable continuous scalar-field surface prototype, twelve comparison sheets, geometry counts and independent Python regressions; retained explicit native-transfer and scale limitations. | --- | --- |
| AM-200, 60 s | 0.32 | 0.00 | 0.00 |
| Heavy Raider, loaded 60 s | 46.96 | 28.50 | 0.00 |
| Heavy Raider, recovery 120 s | 6.75 | 3.94 | 0.00 |
| Pegasus, 60 s | 95.20 | 50.15 | 29.33 |

The initial mixed-detail maximum-group implementation measured 129.03 K on Pegasus at 1,400;
the mean-group implementation initially reduced this to 34.22 K before the grid-origin alignment
fix (final values above). At that budget Pegasus retains 116 exact nearby blocks and needs 741
ordered fragments. Grid-bound origins also prevent signed-coordinate coarsening from exhausting
a one-cell allocation on a tiny grid. Heavy Raider is entirely exact at 1,278
fragments, AM-200 at 262. These tests evaluate estimates at block centres, not native mesh pixels.
The images are explicitly offline proxies: grid rotation overlaps, depth blending, viewpoint motion,
GPU limits and cooling while moving still require native evaluation.

## Full-screen persistence and chat regression

The fleet context passes now run whenever the mode is active, even with no completed temperature
field. Empty space, acquisition, movement invalidation and capacity fallback retain the neutral
depth context and black sky. Unknown remains unmeasured; this does not fabricate temperatures.
Archived diagnostics keep their prior behavior. Empty viewports report `NO MEASURED GRIDS IN VIEW`
and no longer repeat acquisition notifications. Earlier mentions of normal-view fleet fallback are
superseded by this persistent context behavior.

The user also reports that chat opens but typing produces no text. The thermal renderer has no
keyboard blacklist or editable text box. Its status labels are now explicitly cursor-passive, and
all thermal submission yields to either the game chat flag or Rich HUD's immediate `IsChatOpen`
signal, as well as cursor/menu visibility. This addresses the opening-frame timing gap but does
not establish the cause of blocked typing. Telemetry records UI transitions, framework input mode,
and the mod client's blacklist without recording typed text. No global input filter is forcibly
cleared. Native confirmation is still needed; if typing remains blocked, enable telemetry before
vision and create a dump after reopening/closing chat (or after switching vision off).

## Dump 20260919_060711: capacity retries and input state

The native run captured 1,750 adapter frames with 615 UI-suppressed frames and no render errors.
Of those, 1,313 were context-only awaiting a field; only 437 had measured regions. Average adapter
CPU was 10.238 ms, maximum 167.066 ms. Measured-field frames averaged 30.773 ms. Repeated field
capacity failures dominated the important events. Successful three-grid publications were often
all-coarse, with 607–892 ordered fragments in the retained events. Camera reach was 50,000 m.
Zero submitted temperature triangles during acquisition does not mean zero fullscreen background;
context triangles were not counted. Events now explicitly report `context-active`.

At 112.77 s the framework detected chat; the game flag followed at 112.82 s. Reported framework
input mode was `NoInput` and this mod client's blacklist was `None`. Drawing was suppressed. This
confirms the new chat gate operates but does not identify why typed characters were absent, nor
rule out another input owner. The dump does not establish that the chat bug is fixed.

Fleet composition now yields between global partition insertions and resumes under the existing
soft 2 ms allowance. It also yields after each local detail build and final ordering attempt; those
individual operations remain synchronous and may exceed the allowance. No partial field is published.
If both detailed and coarse composition exceed capacity, the next acquisition halves its total
source budget, retaining at least one reservation per selected grid. The reduced allocation persists
until vision is switched off, avoiding the same expensive failed rebuild every refresh. Successful
refreshes wait 0.3 s. At minimum allocation repeated failure backs off to 1 s. Existing valid measured
fields remain during retries. Multi-grid poses are rechecked before publishing the asynchronous
composition; stale transforms are rejected. These changes still need a native timing dump.

## Appearance correction: soften heat locations rather than enlarge flat groups

The user rejected the native banding from the mixed-detail candidate. Its corpus metrics do not
supersede that visual rejection. Further increases in flat-cell size are a capacity fallback only,
not the desired presentation. The target is a stable spatial temperature field, resolving individual
blocks nearby and becoming progressively softer with projected distance. Blur must operate on Kelvin
before palette mapping and must not mix unrelated grids or turn unknown surfaces into measurements.

`tools/thermal-gradient-study/soft_reference.py` generates eight appearance references from the same
corpus snapshots: exact visible block temperatures, 2-pixel Gaussian softness and 6-pixel softness.
Normalized masking preserves uniform readings at silhouette edges. Tests verify a centred isolated
hotspot stays centred and cooling stays a local minimum. These widths are illustrative; the images
keep ship size constant for comparison. This is an appearance target, not a game screenshot, native
volume-rendering prediction or proof that Workshop mods can blur the scene. Adjacent/disconnected
parts within one silhouette can still blur together; close occlusion boundaries need depth-aware
handling in any eventual renderer.

Installed renderer inspection provides a candidate for native interpolation: the public
`MyTransparentGeometry.AddTriangleBillboard` overload takes three UV coordinates. The native
`MyBillboardRenderer` forwards them to its triangle vertices. A one-dimensional palette texture
can therefore encode temperature in U, with continuous texture-coordinate interpolation across
triangles. This avoids requiring per-vertex billboard colours or full-scene shader access.

The next native experiment should use opaque palette-ramp triangles with the existing original-depth
ownership tests. Temperature samples must remain anchored to blocks in each grid's local space;
shared vertices must evaluate the same continuous field. Refinement follows temperature error and
projected footprint, while distance increases the sampling kernel gradually. Do not merely sample
already-coarsened flat-cell maxima or fade translucent entry/exit volumes: neither preserves the
intended field. Face-centre hotspot loss, near-plane caps, texture colour space/filtering, cracks at
mixed resolutions and the increased triangle budget remain explicit implementation gates. The
current capacity/performance fixes remain useful, but the grouped renderer is not visually accepted.

## Billboard reduction without temperature coarsening

`ThermalVisionFacePlan` removes only redundant **complete** shared faces among the currently drawn
disjoint regions. It uses exact bounds and exact Kelvin equality, with no temperature quantization:

- Equal-temperature neighbours remove both copies of their internal face.
- Different-temperature neighbours retain the entering thermal face and remove the preceding neutral
  exit face that would immediately be overwritten. Hot/cold boundaries remain.
- Partial matches, exposed boundaries and near-plane caps remain intact. Only visible regions enter
  the face plan, so a culled neighbour cannot cause a missing boundary.

Individual face bounds are also checked against the current camera frustum, including a small margin
for the existing camera-facing offset. This eliminates offscreen faces of otherwise visible regions.
The retained dictionary/list storage is reused across draws; construction is linear in face count.
This is an optimization of the current constant-temperature volume renderer. A future interpolated
field must compare boundary temperature functions before extending the equal-field removal rule;
matching region-centre temperatures alone would not prove equality for that renderer.

The corpus exporter now audits actual face slots before viewport face culling, near caps and two
context billboards. At the 1,400-region per-grid allocation:

| Ship | Before | After shared-face removal | Reduction |
| --- | --- | --- | --- |
| AM-200 | 1,572 | 1,121 | 28.7% |
| Heavy Raider (both phases) | 7,668 | 5,059 | 34.0% |
| Pegasus | 4,446 | 2,701 | 39.2% |

These are independent per-grid allocations, not simultaneous fleet totals or FPS predictions. A
1,000-cell uniform synthetic field falls from 6,000 faces to its 600 external faces (90%). Real
unequal temperatures retain more boundaries. Three thousand independent ray/surface samples verify
hotspot, cooled-patch and inside-camera ownership against the unoptimized field; singleton and partial
neighbour tests guard against missing silhouettes. Temperature arrays and region resolution are unchanged.
Native telemetry now reports `billboards` (including caps/context), `shared-faces-removed` and
`offscreen-faces`; the existing triangle-equivalent metric remains for continuity. Native appearance,
CPU planning cost and GPU savings still need validation. No partial-field billboard truncation or
claim of meeting the proposed 4,000–6,000 total budget is introduced.

## Dump 20260919_062739: excessive persistent degradation

The latest capture has 3,804 adapter frames, 1,616 UI-suppressed frames and no rendering errors.
Adapter CPU mean is 1.567 ms and peak 21.179 ms. This is lower than the earlier capture, but the
camera coverage and detail also changed; it is not an isolated performance comparison.
Capacity retries reduced the source allocation from 1,400 to 68 while viewing a crowded scene.
That allocation then persisted with only four grids in view. Each 999-block test ship received
17 budget units and just three 40 m coarse cells; a 44,632-block grid received two 160 m cells.
That is direct evidence for the especially broad colour bands in the user's screenshot.

The next acquisition now restores the initial budget when the visible grid count falls to half or
less of the count at the last capacity failure. It does not reset simply because another refresh
starts, and it retains the crowded-view backoff. Another capacity failure can still lower detail.
This corrects sticky degradation; it does not implement smooth temperature interpolation or make
flat-cell banding acceptable. No further appearance acceptance is claimed for the grouped renderer.

Repeated chat transitions still report client blacklist `None` and input mode `NoInput`. The
reported typing failure remains unresolved. A separate cleanup defect was found in our settings
window: hiding it did not close explicitly opened numeric editors or release their focus. Hide now
closes/releases only those owned fields, cancelling any uncommitted slider edit. This is preventive
input cleanup, not proof those editors caused this user's issue. Vendor framework code and other
mods' input filters are unchanged.

## Continuous surface synthetic prototype

`tools/thermal-gradient-study/continuous_surface.py` implements scalar temperature interpolation
on depth-tested block-bound surfaces. This is an executable appearance experiment, not the
native mod renderer and not an image-space blur. Run from the repository root with Python,
NumPy, SciPy and Pillow:

```sh
python3 tools/thermal-gradient-study/continuous_surface.py /tmp/thermal-mixed-corpus /tmp/thermal-continuous-surface
python3 -m unittest discover -s tools/thermal-gradient-study -p 'test_*.py'
```

The input snapshots come from `thermal-corpus-export`. Two controlled fixtures add a hotspot
and an extinguisher-like cooled patch using a shared display range. Each corpus snapshot and
fixture gets Cividis and white-hot comparison sheets: exact block reference, near, middle and far.
Temperature coordinates interpolate across triangles before palette lookup. Gaussian-weighted
samples use up to 32 nearest block centres within each grid; fields never mix between grids.
The nearest-neighbour cutoff can introduce small discontinuities and limits effective smoothing
width. Large blocks are represented by their centres rather than their occupied volume.

Full shared faces are removed. Face-centre interpolation error above 2 K can add two triangles
per face within a 6,000-triangle refinement budget. Geometry exceeding that budget remains visible
and is explicitly reported. The fixed camera selects positive-axis faces and software depth testing
resolves overlap. There is no lighting modulation masquerading as temperature.

Observed counts across softness levels: AM-200 664–668 triangles, Heavy Raider snapshots
2,482–3,926, and Pegasus 9,894. Pegasus exceeds the budget even without refinement. These counts
exclude native volume entry/exit passes, near caps and context. Direct surface ownership is assumed
by this offline renderer; transferring it to the Workshop renderer remains unresolved. These are
geometry counts, not GPU timings or a claim of scalability in a crowded world.

The controlled cooled patch remains identifiable near the camera and attenuates at the far setting.
Softness is a synthetic 0–1 distance parameter while displayed ship size stays constant for inspection.
Physical distance calibration, temporal LOD stability, perspective and cross-grid occlusion are not
validated. Four new checks cover extrema location, independent fields, shared-face removal,
over-budget reporting, scalar interpolation and draw-order-independent depth ownership; all eight
Python appearance checks pass. Native code is unchanged by this experiment.

## Error-bounded surface merging

The accepted interpolated mesh now has an offline optimization in
`tools/thermal-gradient-study/optimize_surface.py`. Adjacent coplanar rectangles merge only
when the resulting temperature field stays within 2 K of the original triangles. Validation
includes all original vertices and original-edge intersections with the new diagonal, so errors
are measured against the original field rather than accumulated across merges. Gaps are never
filled, and centre-refined faces retain their local hotspot/cooling detail.

```sh
python3 tools/thermal-gradient-study/optimize_surface.py /tmp/thermal-mixed-corpus /tmp/thermal-optimized-surface
```

The output compares original, optimized and absolute temperature difference at near/far softness
in both palettes. Each of the twelve case/softness combinations also checks identical visible
pixel coverage and a maximum error of 2 K. These are fixed-camera synthetic checks, not native
performance claims. The surface geometry stays the same; internal triangulation changes.

| Snapshot | Near triangles, before → after | Far triangles, before → after |
| --- | --- | --- |
| AM-200, 60 s | 668 → 348 | 664 → 334 |
| Heavy Raider, 60 s | 3,926 → 3,694 | 3,522 → 3,200 |
| Heavy Raider, 120 s | 2,748 → 1,624 | 2,482 → 1,058 |
| Pegasus, 60 s | 9,894 → 8,222 | 9,894 → 7,764 |

All twelve Python tests pass, including uniform-field collapse, preservation of hot/cooled extrema,
gap coverage and exact linear-gradient merging. Pegasus still exceeds the 6,000-triangle target;
shared native rendering overhead and crowded-fleet cost remain unresolved. This conservative
optimization is retained in the synthetic lab and does not change the live mod.

## Pegasus simplification ladder

`tools/thermal-gradient-study/simplification_sweep.py` generates an interactive original/optimized
comparison for Pegasus at 60 s with near softness. Nine levels use the unchanged original followed
by error allowances of 2, 5, 10, 20, 40, 80, 160 and 320 K. Colour and grayscale share the original
fixed temperature range. Every level checks identical pixel coverage and its temperature bound.

```sh
python3 tools/thermal-gradient-study/simplification_sweep.py /tmp/thermal-mixed-corpus/605516903-60.json /tmp/thermal-simplification-ladder
```

Triangle counts are 9,894, 8,222, 6,996, 6,020, 5,166, 4,682, 4,436, 4,312 and 4,270.
These are allowances, not forced errors: the most aggressive result has an observed maximum
pixel error of 81.7 K and mean error of 2.82 K. Increasing tolerance eventually yields little
additional reduction because the method retains geometry. This ladder invites visual acceptance;
it does not select a shipping tolerance or demonstrate a scene-wide/native budget.

## Level 4 quality benchmark

The user judged Pegasus level 4 nearly indistinguishable from the original, with meaningful
quality loss beginning at level 5. Retain the 20 K allowance as an offline benchmark, not a
blanket acceptance for every scene. `optimize_best_order` tries both rectangular merge directions
on each plane and chooses the smaller partition, always comparing against original triangles.
This increases offline mesh preparation work; it is not a measured CPU optimization.

```sh
python3 tools/thermal-gradient-study/optimize_surface.py /tmp/thermal-mixed-corpus /tmp/thermal-quality-level4 --tolerance 20 --best-order
```

Pegasus near decreases from 9,894 to 5,048 triangles (previous level 4: 5,166), and far to 4,586.
Other near/far counts: AM-200 322/314; Raider 60 s 3,416/2,888; Raider 120 s 1,350/888.
The generated twelve case/softness comparisons retain pixel coverage and stay within 20 K.
Thirteen Python tests pass. This remains a synthetic per-ship optimization; a fleet-wide
allocation, engine overhead and native gradient rendering are still unresolved.

## Covered internal surfaces

The `--remove-buried` option on `optimize_surface.py` removes positive-facing triangles whose
entire bounding rectangle has occupied grid cells immediately outside it. This handles a large
face covered by several smaller blocks, which exact matching-face cancellation missed. Partially
covered faces are retained conservatively. Only integer, solid block-bound proxies are supported;
this rule cannot be applied blindly to native meshes with openings inside their bounds.

```sh
python3 tools/thermal-gradient-study/optimize_surface.py /tmp/thermal-mixed-corpus /tmp/thermal-exposed-surface --tolerance 20 --best-order --remove-buried
```

All case/softness combinations check unchanged images immediately after culling, followed by
unchanged coverage and the existing 20 K bound after merging. Pegasus near falls from 5,048 to
3,336 triangles, far from 4,586 to 3,044. AM-200 is 246 at both levels; Raider 60 s is 2,849/2,389;
Raider 120 s is 1,131/751. These remain per-ship synthetic counts, not a native fleet budget.
Tests additionally guard multi-block face coverage and partially covered openings. The occupancy
set grows with occupied grid-cell volume; its preparation cost is not a measured native budget.

## Fixed-view analytical occlusion experiment

Add `--occlusion` to the covered-surface command to discard a triangle only when one nearer
triangle contains its entire orthographic projection. Depth must be strictly nearer at all three
vertices; coplanar ties, partial coverage and degenerate occluders remain. This is analytic
containment, not a pixel visibility approximation. The original thermal field is unchanged.
All twelve case/softness combinations assert identical rendered temperatures before and after
this pass. Independent tests cover draw order, partial overlap, coplanar ties and changed views.

Pegasus near drops from 3,336 to 2,959 triangles, far from 3,044 to 2,743. Raider 60 s becomes
2,636/2,244; Raider 120 s 1,048/681; AM-200 237/237. Sixteen Python tests pass.

This remains a fixed-view offline experiment. The pass has quadratic candidate comparisons,
requires recomputation for camera changes, and does not demonstrate a net native CPU/GPU win.
No engine depth-buffer access is assumed. Native mesh openings, perspective, fleet-wide allocation
and preparation scheduling remain unresolved. Keep this optional rather than treating the new
counts as a production guarantee.

## Projected-size LOD prototype

`distance_preview.py` builds actual geometry levels from corpus grids: full block-bound source,
closest-quarter source surfaces plus merged remainder, merged source, then cells of 2, 4, 8, 16,
32, 64 and 128 grid units, and a minimal box. Coarse geometry samples the original block-temperature
field rather than replacing it with synthetic temperatures. The near protected-quarter selection
is fixed-view surface priority, not native per-block model topology.

```sh
python3 tools/thermal-gradient-study/distance_preview.py /tmp/thermal-mixed-corpus /tmp/thermal-distance-lod
node tools/thermal-gradient-study/test_distance_preview.cjs /tmp/thermal-distance-lod/index.html
```

The gallery displays three ships at relative distances 1, 2.5 and 6. Pixel density follows
`height / (2 * tan(verticalFov / 2) * distance)`, initially 1080 pixels and 60 degrees. Choose
coarsest cell geometry within a 3-pixel cell-diagonal displacement estimate. Request full source
when a 2.5 m block spans 12 pixels. Reserve minimum coverage for every visible grid, then spend
remaining capacity nearest first. This heuristic is not a proven optimum and under pressure can
exceed the desired screen error. A 12% distance hysteresis reduces switching.

The synthetic scene uses a 6,000-triangle global limit. Steady allocations use 5,936, reserving
64 for staged transitions. Both old and new meshes count during the 0.3-second smoothstep fade.
When a direct transition cannot fit, fade to the minimum representation first, then upgrade.
This can briefly soften a ship under pressure. Headless tests execute the actual gallery scheduler
through approaches, retreats and steering, asserting the limit and eventual settlement.

Pegasus levels contain 9,894 / 4,198 / 3,336 / 2,180 / 652 / 198 / 74 / 58 / 40 / 40 / 6 triangles.
The closest surfaces thus receive source detail even when a complete detailed Pegasus cannot fit.
Other generated ladders end at 6 triangles. Twenty-one Python tests pass plus the JavaScript
scheduler regression. Distant three-ship allocation in the scheduler test falls below 1,000.

This guides thermal LOD using engine-like projected scale and hysteresis; it does not read the
renderer-internal per-model LOD. Full source still means block bounds, not native model triangles.
The gallery uses orthographic images scaled by projected-size estimates and presentation alpha
crossfades. Perspective, native blend ownership, per-block near selection, geometry preparation
cost and native topology remain unimplemented. The separate cameras do not validate fleet occlusion.
Actual camera FOV, viewport height and view-distance/frustum filtering must drive the native adapter.

### Raider detail plateau diagnosis

The fixed Pegasus-first demonstration left Raider at 754 triangles from nearest distances
50–2,000 m (Raider distances 125–5,000 m). Its requested level was usually merged source
at 2,849 triangles, or full source at 3,926 at the closest setting. After upgrading Pegasus,
that next discrete step did not fit. This was budget pressure, not a stuck transition.
The preview now permits selecting the nearest ship and labels budget-limited versus
distance-selected allocations. Reordering invalidates allocation hysteresis immediately.
The actual JavaScript scheduler regression confirms Raider reaches full source when nearest,
settles through reordering and stays within 6,000 triangles. No allocator priority changed.
The large 754-to-2,849 step remains an opportunity for additional intermediate representations.

## Native interpolated-temperature material lab

`/thermal vision lab gradient colour` or `grey` now tests temperature UV interpolation on the
actual aimed block model (including supported armour meshes). This is not the synthetic fleet
LOD renderer: only the aimed block within 1,000 m is drawn, and surrounding scene geometry stays
normal. The test isolates the unresolved native palette-texture/depth path before fleet integration.

The adapter gathers the target plus a 5×5×5 cell neighbourhood using direct thermal lookups,
deduplicates multi-cell blocks, samples a one-cell Gaussian temperature field and caches UVs
for shared mesh vertices during each frame. At most 6,000 triangles are examined per frame;
partial geometry is labelled. The existing suit/camera gate and chat suppression remain active.
The range is explicitly locked, not adaptively inferred. Colour uses the canonical Cividis table;
white-hot uses the canonical 18–243 intensity range. Rebuild lookup DDS assets with
`python3 tools/thermal-gradient-study/build_palette_textures.py`.

The local game mod directory is a symlink to this repository. Reload the world to load changed
scripts/material definitions; if new textures fail to load, restart the game. Test sequence:

```text
/thermal telemetry on
/thermal vision range 0 700
/thermal vision lab gradient colour
/thermal vision lab gradient grey
/thermal dump
/thermal vision off
```

Aim within 1,000 m at a block beside a different-temperature block; a uniformly heated neighbourhood
should remain uniform. Check colour/white-hot continuity on the actual mesh, depth occlusion, chat
entry and disabling when leaving first person. A screenshot plus dump records visual results and
frame/geometry diagnostics. Change the fixed range if temperatures clip at either end. This test
must not be described as a live validation of the synthetic scene budget, topology LOD or interiors.
Build passes with obsolete-API/vulnerability-fetch warnings and 140 targeted C# checks pass.
Native UV interpolation, texture colour space and palette filtering still require this live check.

### First native gradient capture

`Thermodynamics_Telemetry_20260919_074409.log` confirms both gradient modes activated, with
4,046 captured frames, zero render errors and zero submitted triangles. Every captured row was
`no-raycast-target`; no palette-rendering conclusion can be drawn from this capture. The original
lab inherited the 15 m interaction ray. The lab now uses a separate 1,000 m ray while extinguisher
and ordinary readouts retain 15 m. HUD/telemetry distinguish no physics hit, non-grid hit,
unsimulated grid and missing cell temperature. Distance is a plausible cause, not established by
the previous generic no-target diagnostic. The command and material compilation did not fail.

## Capture 075143: native submissions, unsuitable scale and model cost

`Thermodynamics_Telemetry_20260919_075143.log` has 2,395 captured frames, 177 UI-suppressed
frames and no render exceptions. Rows total 1,879 frames with submitted geometry and 516 without
(515 no physics hit, one non-grid hit). There are 779 partial frames. This confirms the native
submission path runs for armour and functional models; it does not prove visible palette correctness.

Every captured row uses WhiteHot with 223.15–323.15 K (-50–50 C). Many targeted blocks are
roughly 420–676 K, while other armour is around 190–216 K. These target values fall outside the
scale. Interpolated vertices can differ from target temperatures, so exact saturation percentages
cannot be inferred, but this range cannot distinguish most hot target values. Full-scene adaptive
range must be integrated; another request for manual per-block range tests would miss user scope.

Overall adapter CPU mean is 1.185 ms, maximum 14.121 ms. Large-calibre gun rows average 4.279 ms
and 3,550 submitted triangles for one block; some models hit the examined-work cap. Mesh extraction
also drops unsupported techniques, accounting for some partial results. Detailed native topology
cannot be naively expanded across all visible blocks. The scene renderer needs coarse coverage,
bounded nearest-surface refinement and globally counted submission/work budgets. Native examined
telemetry can exceed 6,000 slightly because the old loop increments before rejecting and is reentered
for subparts; submitted counts and examined-work counts must remain distinct.

The user explicitly requires on/off full-scene behaviour. Keep this aimed-block lab archived as
research evidence; do not ask for another isolated block acceptance test or represent it as the
approved full-scene renderer. No native full-scene integration is validated by this dump.

## Full-scene gradient integration in progress

Plain `/thermal vision colour` and `grey` now route through viewport fleet discovery, with
palette UV interpolation on entry faces and near-plane caps. The prior `lab gradient` command
aliases this scene path; it no longer invokes a crosshair ray. `lab blocks` retains the flat-field
research path. Activation restores adaptive range. The near/far context persists without a target.
Unmeasured scene surfaces remain neutral: this is not a claim that terrain has measured temperatures.

`ThermalVisionSmoothField` creates a compact-support scalar field per sampled grid. Preparation
recovers each partition fragment's input owner before evaluating corners in that grid's frame;
independent grids never average together. Matching-temperature overlaps choose the first containing
source deterministically; missing ownership falls back to the fragment's scalar temperature.
Eight corner temperatures per fragment are prepared cooperatively and published with the complete
field. Drawing interpolates those corners and maps temperature to palette UVs. Near caps use the
same scalar field. Geometry-query and temperature-field preparation remain separate from draw.

Smooth mode permits at most 299 partition leaves. A conservative 20 submissions per leaf plus
two context billboards bounds the scene below 6,000. Exact flat-colour shared-face removal is disabled
for gradients because equal centre temperatures do not prove equal face fields. Capacity failures
coarsen a complete field rather than dropping an arbitrary prefix at draw time. More than 299
visible simulated grids explicitly exceeds this representation's capacity; it cannot guarantee
coverage for arbitrarily many grids.

This is native integration scaffolding, not completion of the accepted synthetic LOD renderer.
It still uses coarse/exact region bounds and mean samples; synthetic topology levels, smooth LOD
transitions, detailed near models and interior acceptance are not ported. Region-bound gradients
are an approximation that needs native validation. Crosshair-targeted probes are no longer the
user acceptance path. Three new core regressions cover grid isolation, bounded monotonic blending
and common face/cap interpolation. No in-game appearance success is inferred from compilation.

## Uncapped submission stress test

At the user's request, smooth fleet rendering no longer uses the 299-leaf restriction derived
from the 6,000-billboard target. It now uses the existing fleet preparation capacities: initial
1,400 source allocation and 1,536 final partition leaves, with preparation-failure backoff.
Every prepared visible face/cap is submitted; there is no thermal draw-time billboard quota.
The engine's shared 32,768-entry limit remains outside mod control. These separate preparation
limits mean this is not an unlimited-geometry implementation.

Activation identifies stress mode, and publication telemetry records `submission-cap=none-stress-test`
as well as the source budget, final fragments and conservative submission estimate. Actual
billboard counters remain enabled. The estimate can reach 30,722 for 1,536 smooth leaves, excluding
other renderers' usage. The native fleet LOD integration is still incomplete; removing the quota
does not establish visual correctness or make this the approved synthetic renderer.

## Original block temperature field — 080552 follow-up

The 080552 dump showed three measured grids, but the settled 999-block ships used
61 / 19 / 3 coarse cells and zero exact refinements. The renderer then smoothed
those cell means. Active thermal CPU averaged 7.9 ms, with a 34.9 ms maximum;
the settled view submitted 2,466 billboards. This was a loss of thermal detail,
not evidence of billboard exhaustion.

The smooth fleet path now collects original block bounds and temperatures during
the existing mean scan, independently of the geometry partition. A grid-local
spatial index evaluates a compact interpolating kernel. A prepared scalar lattice
samples region interiors as well as corners; each axis has at most four segments.
Temperature searches happen during cooperative preparation, not during drawing.
Surface spacing follows four projected pixels with a one-block minimum, using the
actual camera projection and viewport. More distant samples use wider support.

Prepared face patches merge where lattice vertices and cell centres agree within
20 K with a two-triangle approximation. This is a sampled simplification heuristic,
not the synthetic optimizer's continuous error proof. Uniform and linear surfaces
remain two triangles; local peaks request more patches. Automatic range now also
observes prepared field temperatures. Smooth mode skips the unused flat shared-face
planner and no longer reports its unapplied removals.

Geometry is still the existing coarse partition: this is not the completed native
mesh LOD port. Samples outside kernel support fall back to the coarse cell mean;
`field-queries` and `field-fallbacks` report that limitation per grid. Regions with
no recovered source owner also retain their mean. Near-plane caps use the scalar
lattice at their existing vertices, without the face subdivision. These limitations
can still soften or miss heat on coarse enclosing surfaces and require native
visual evaluation. No thermal billboard submission cap has been restored; adaptive
surface detail can increase actual billboard counts during this stress test.

Reload the world and use `/thermal vision grey` or `/thermal vision colour`.
The publication log identifies this path as `surface-field=original-blocks` and
reports `thermal-samples`, `surface-spacing-m`, and query/fallback counts. Existing
full-scene eligibility and off commands are unchanged.

Validation: solution builds; 146 thermal, whitelist, and documentation tests pass.
New regressions cover an interior hot block in coarse geometry, bounded temperature
interpolation, continuity at spatial bucket boundaries, grid isolation, distance
sampling density, and retaining hot vertices while merging a linear face.
Native appearance and performance have not yet been validated for this revision.

## Moving-grid snapshots and responsive activation

Default grey/colour vision now uses independent grid-local snapshots. Ctrl+Shift+T
toggles grey vision; `/thermal vision bind H` rebinds the chord to Ctrl+Shift+H
and saves it in client-local storage. Letters S, W and M remain reserved for other
thermal tools. Chat and cursor UI suppress shortcut handling.

Each newly visible grid receives a first-frame coarse estimate from at most 32
block visits. Existing snapshots are reused after toggling off/on. This removes
the blank acquisition period, but does not make an uncached detailed heat field
available instantly. The HUD identifies initial estimates as refining.

Per-grid scans run cooperatively within the existing 2 ms preparation soft budget.
Completed geometry and temperatures replace only that grid's snapshot; pending or
failed refreshes retain the previous view. The latest grid transform is applied
on every draw, so rigid motion does not invalidate preparation or publication.
Temperature updates blend over 0.3 seconds. Topology is refreshed along with the
field; this revision still reconstructs local geometry rather than caching it
separately. Off cancels preparation but retains completed views; world/HUD reset
clears the cache, and closed grids are removed. Each grid has its own preparation
capacity, without the global fleet backoff that previously reduced all ships.

Within-grid ordering uses the existing BSP. Between grids the current adapter
orders by nearest world-bounds distance; overlapping/interpenetrating grid bounds
are **not** guaranteed correct. Crossing ships, articulated subgrids, topology
changes and native transition appearance still require live testing. Temperature
blending does not guarantee invisible geometry changes. No billboard submission
quota has been restored. The whole-fleet path remains available in archived lab
code; telemetry for the new path reports `independent draw` and per-grid publication.

Validation: build and targeted thermal/whitelist/documentation tests, including
rigid-transform invariance of local ordering and bounded snapshot temperature
transitions. Native first-frame latency and moving-grid overlap are not yet measured.

## Temperature legend and 085543 capture

Thermal vision now shows an upper-right, noninteractive HUD legend. It uses the
same 256-sample ramp textures as the world renderer, with five Celsius labels
spanning the active display range, cooler/hotter direction labels, and AUTO RANGE
or LOCKED RANGE status. Colour and white-hot modes select the corresponding ramp.
The legend uses the HUD's DPI-scaled root, ignores cursor input, and hides on off,
viewpoint loss, UI suppression, rendering failure, and HUD/world reset. Distance-only
diagnostic mode does not display a temperature legend.

The 085543 dump recorded 1,826 frames, zero render errors and no zero-submission
frames in either independent-preview or independent-detail rows. CPU averaged
34.745 ms (maximum 194.260 ms). One retained draw event had 37 grids, 14 previews,
and 35,762 world billboards. Continuous submission is now present in this capture,
but CPU cost and shared billboard capacity remain unresolved. Most captured frames
still included at least one preview; the HUD addition does not fix that preparation
or rendering cost. The stress-test submission policy remains unchanged.

## Cached surface draw work

The independent renderer now caches each patch's four vertex temperatures and
previous-snapshot transition temperatures during preparation. Drawing a patch
uses four cached scalars and four world transforms, instead of six trilinear
samples and six transforms for its two triangles (with additional sampling during
transitions). Both triangles retain the original AC diagonal, palette UV mapping,
winding, depth bias, and submission order. Near-plane caps still use the general
scalar sampler.

Exposure observes each visible field's cached minimum and maximum, replacing up
to 125 lattice observations with two. This is exactly equivalent for the current
min/max-based auto-range algorithm, including its rounding and time constants.
Face preparation stops its error search once a subdivision is already required.
The cooperative scheduler stops after one completely idle pass rather than
polling every waiting grid up to 128 times. New `discovery-ms`, `preparation-ms`,
and `draw-ms` telemetry separates the adapter phases in subsequent dumps.

These optimizations do not change detail selection or billboard submissions.
They do not solve shared-buffer exhaustion or establish a native FPS improvement.
Regression tests compare cached vertex temperatures throughout a transition and
exposure over expanding/contracting ranges against the original sampling paths.

## Cached adjacency and transition lookup

Each completed grid snapshot now records exact opposing face pairs. Drawing skips
only the neutral exiting member of a pair; every temperature-bearing entry face
remains. Partial overlaps, unmatched boundaries, near-plane caps and faces on
other grids are retained. Adjacency is prepared once, survives rigid movement,
and is replaced atomically with its snapshot. The synthetic 10×10×10-cell fixture
eliminates 2,700 neutral exit submissions from an outside view. This is a fixture
result, not an estimate of the live fleet's reduction. `shared-exits-skipped`
counts adjacency skips before per-face frustum rejection, so it is not by itself
an exact count of saved visible billboards; compare actual `billboards` in dumps.

Refresh blending now locates the previous region through its existing BSP instead
of scanning every previous region for every lattice sample. On exact shared
boundaries the lookup preserves the original fixed-origin traversal tie order;
outside points and gaps still use the existing coarse fallback. Independent ray
tests cover hot/cold fields and inside/outside viewpoints with neutral-exit removal;
point-query tests compare against the original linear lookup including boundaries
and gaps. No submission quota or lower detail setting was introduced.

## Contrast, stable partitions and interior occupancy

The 091323 capture still showed coarse-average fallbacks on 5,877/9,781 samples
for one grid and 4,176/6,125 for another. The independent renderer now supplies
source bounds to its block-temperature index. Outside compact interpolation support,
a spatial tree finds the nearest original block AABB and uses its measured temperature
instead of the coarse cell average. This is an approximation on enclosing geometry,
not a new measurement of empty space. It can extend a hotspot into neighbouring
coarse surfaces. `nearest-samples` reports this case separately from missing-data
fallbacks. Existing callers without source bounds retain the old fallback behavior.

Ordinary refreshes reuse the published region partition while block count and a
new add/remove topology revision agree. Same-count block replacements invalidate
it too. This eliminates repeated camera-dependent focus repartitioning. Sampling
spacing uses power-of-two steps and hysteresis rather than changing continuously.
Adaptive face triangulation can still change with temperatures. Complete native
mesh LOD and exact shape-change tracking are not provided by this change.

When the eye is within grid-local block bounds, a twelve-cell-wide focus cube uses
actual occupied block cells and subtracts the enclosing coarse cells, including
empty room space. Fine occupancy is not coarsened. The cube recentres only after
four cells of movement; failed preparation keeps the previous snapshot. This can
increase geometry and still needs native validation for overlap, transition seams,
large rooms and partial block shapes. It is not a claim that interior navigation
or every cloudy artifact is solved. Outside the focus cube the coarse field remains.

Tests compare nearest-source selection with brute-force distances and verify that
an interior room stays empty while its hot/cold walls and distant coverage remain.
Automatic exposure itself is unchanged, so the gradient correction can be evaluated
without changing both the measured field and its display range simultaneously.

## Legend submission order (2026-09-19)

The interior screenshot accompanying the 093739 dump has no visible temperature
legend. The dump predates legend diagnostics, so it cannot establish whether the
HUD was covered, unsubmitted, or clipped by the engine billboard limit.

The legend now retains Rich HUD layout and text measurement but explicitly submits
its boxes and labels after the thermal scene, instead of relying on the relative
order of the framework and mod draw callbacks. The installed renderer preserves
submission order in the PostPP bucket; camera proximity and framework ZOffset do
not provide cross-callback ordering. HUD ramp materials share the world palette
textures but ignore scene depth. No vendor framework code is changed.

The top-right legend displays the active palette, auto/locked range, and five
Celsius ticks derived from the actual low/high Kelvin values used for rendering.
It closes with thermal vision and is suppressed with chat/menus. Dumps now include
legend submission counts, registration, bounds, parent visibility, palette and
range every 120 active frames. These counts describe submission, not GPU visibility.
The uncapped stress-test renderer remains uncapped; exceeding the shared engine
billboard buffer can still drop HUD geometry. Native confirmation is required after
reloading the world. Hidden-grid occlusion is a separate outstanding issue.

## Legend camera anchoring (2026-09-19)

The follow-up live screenshot confirmed the legend was visible, but tilted and
moved out of the viewport during camera movement. The explicit session draw path
was still using the framework's layout-time camera matrix. The legend now builds
its own plane from the current session camera world/projection matrices and
viewport immediately before submission. It uses a private matrix reference, so it
does not modify the framework transform shared by other HUDs. Panel anchoring is
computed from the current viewport and DPI scale; glyph measurement remains in
Rich HUD. Submission still follows the thermal compositor.

Projection regressions move and rotate the camera at large world coordinates and
check both panel corners against fixed pixel insets, at 1080p, 1440p and ultrawide,
with different FOVs, DPI scales and an off-centre projection. These validate the
shared projection math; final in-game camera/render timing still needs confirmation.

## Nearby solid-armor occlusion (2026-09-19)

The fleet renderer now checks whole-grid occlusion before preparation, drawing and
exposure sampling. It collects up to 32 nearby solid blockers with at most 500
block lookups per frame. Only full-integrity, fully built, undeformed vanilla light
and heavy armor cubes qualify; mod-overridden definitions are excluded. Each cube
is inset to 80% of its width to avoid treating beveled edges as solid coverage.
Doors, windows, slopes and arbitrary functional-block bounds never qualify.

All eight corners of a candidate grid's world AABB must lie strictly behind one
blocker's inset box. The convex shadow then covers the entire AABB. Separate
blockers are not combined, so a window or other gap between them cannot be closed
by this test. The blocker's own grid is not culled. All transforms and block state
are read anew every frame; hidden grids keep their completed snapshots for prompt
reappearance. This does not cull hidden regions within the player's own ship and
will deliberately miss many occlusion opportunities. It is a conservative first
pass, not general engine occlusion-query access.

Telemetry adds `hidden-grids`, `solid-occluders` and `occlusion-ms` to independent
draw events. Billboard submissions remain uncapped for the requested stress test.
Regression tests cover complete/partial coverage, a window between blockers,
camera/blocker movement, overlap and an eye inside a blocker. Randomized hidden
cases are checked with independent dense ray intersections. Live performance and
appearance remain to be measured with an interior/exterior dump after world reload.

## Occlusion script-interface correction (2026-09-19)

The first live load of nearby armor occlusion failed because the concrete
`MyCubeGrid.GetCubeBlock` overload infers the prohibited `MySlimBlock` type.
The lookup now uses an explicitly typed `VRage.Game.ModAPI.IMyCubeGrid`, selecting
the supported `IMySlimBlock` interface. The installed-game analyzer harness now
includes a positive case for the full integrity/deformation/definition-context
predicate and a negative case reproducing the concrete-type failure. The corrected
case reports zero compiler and whitelist errors; the negative control reports the
expected prohibited type/member errors. Ordinary .NET compilation and the existing
syntax-only whitelist tests did not detect this inferred-type restriction.

## Temperature-only refresh optimization (2026-09-19)

When block count, topology revision and interior focus permit geometry reuse, the
renderer now collects fresh block temperatures directly into the spatial heat
field. It skips coarse occupancy scanning, coarse means, detail-candidate collection
and shared-face adjacency rebuilding. Existing BSP leaves and their adjacency masks
are retained. Initial acquisition and topology/interior-focus changes still use the
full preparation path. Lattice temperatures, surface patch preparation, transition
blending and atomic publication remain active in both paths. Billboard submissions
remain uncapped.

Previous temperature lattices are released after their 0.3-second transition ends
on a drawn grid. Cached patch transition values remain available on the current
field; the previous lattices are only needed for near-plane cap interpolation during
the transition.

The 512-block regression compares the old scan pipeline against direct collection:
source visits fall from at least 1,024 to 512, with identical values at 300 sample
positions including points outside the block bounds. This measures source-work
reduction, not native frame-time savings. Publication events now include
`refresh-path=temperature-only` or `refresh-path=topology`; live preparation timing
remains available in independent draw events.

## Synthetic CPU optimization audit

See [the benchmark results and strategy audit](thermal-vision-optimization-audit.md)
for the retained index, cache, surface-preparation and visibility-traversal changes,
rejected alternatives, reproduction command and limits of synthetic evidence.

## Change log

- Added voxel-independent distant celestial discs, projection regressions and celestial API validation. Native distant-depth interaction remains to be tested.

| Date | Change |
| --- | --- |
| 2026-09-19 | Added error-bounded coplanar merging and before/after appearance comparisons; ship triangle reductions range from 6% to 57% within 2 K, with Pegasus still over target. |
| 2026-09-19 | Diagnosed sticky budget 68 and three 40 m cells per test ship in 062739; added viewport recovery and settings-editor focus cleanup, with chat and smooth rendering still unresolved. Added exact shared-face elimination and per-face viewport culling; corpus face counts fell 29–39% without changing temperatures, with independent ray regressions and actual billboard telemetry. Recorded rejection of flat banding; added Kelvin-space softness references and audited the per-vertex UV palette-ramp interpolation candidate. Reviewed 060711 dump: identified capacity failures and 167 ms preparation spikes; time-sliced global composition, reduced retry budgets and clarified context/input telemetry. Restored persistent fleet context without measured grids, added immediate chat suppression and input-state diagnostics; blocked typing remains unconfirmed. Selected the per-block foundation with mixed close detail, volume-mean coarse cells, demand-aware budgets, compact HUD and shared-code corpus appearance evaluation. Matched fleet reach and sky clearing to the game camera; audited native mesh LOD and documented its distinction from thermal detail budgeting. Replaced single-grid selection with coverage-first viewport fleet budgets, fair scanning and a three-grid regression. Recorded user-accepted native per-block appearance, 999-block telemetry and unresolved unavailable-field frames. Added exact grid-local per-block native lab with quad boundaries, corpus ownership/submission audits and flat reference previews. Added three-ship corpus snapshots, 16 appearance comparison sheets, provenance and offline depth-buffer checks. Added seven-fixture gradient partition study, independent geometry checks and explicit diagonal/radial/checkerboard counterexamples. Preserved the rejected regional presentation behind an explicit lab command; retained compatibility aliases, native evidence and regression fixtures. |
| 2026-09-19 | Added distance-LOD approach and dense-volume coverage checks. Added fine-focus cooling, mixed-resolution capacity and transactional failure regressions. |
| 2026-09-18 | Added a reproducible installed-analyzer check: entity overrides and voxel XML/base-builder render-state transfers pass; direct renderer-field access and the concrete builder type are rejected. |
| 2026-09-18 | Added `thermal-vision-volumes` ownership study with explicit missing-heat and false-heat counts; even ideal cell-boundary assignment fails a foreign-surface case. |
| 2026-09-18 | Added shared depth-plane projection/order checks and documented the separate native compositor acceptance test. |
| 2026-09-18 | Added bounded temporal point reuse and 3×3 footprint experiments with camera-motion and new-occluder counterexamples; no new live test mode. |
| 2026-09-18 | Added shared survey scheduling/projection/presentation regression tests and linked the implemented snapshot scope's native test plan. |
| 2026-09-18 | Added an analytic sensor-image feasibility study, native-query workload arithmetic, both-palette resolution previews and a thin-hot-object aliasing counterexample. |
| 2026-09-18 | Extracted the production suit/camera eligibility policy into the offline core and added context, ownership, death and camera-failure regressions. |
| 2026-09-18 | Made plugin-independent full-scene coverage an explicit acceptance gate and linked the installed-API renderer audit. |
| 2026-09-18 | Added conservative plane-batch rejection and dense-hull differential fixtures. Added the shared-code synthetic lab, resumable mesh regression, software-depth fixtures, adaptation trace and explicit incomplete-product gate. |
