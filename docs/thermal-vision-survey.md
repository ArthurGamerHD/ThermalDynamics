# Thermal survey scope: test candidate

This is an implemented, plugin-independent alternative to live fullscreen thermal vision, prepared for a consolidated in-game evaluation. It is a **snapshot instrument**, not a claim that the original live full-scene feature is complete. It samples physics-visible surfaces across the camera view, displays simulated block temperatures, and deliberately labels unmeasured scenery. It cannot reproduce non-colliding surfaces or native visual meshes exactly.

**Current product decision:** the user reaffirmed that the finished feature must integrate into the game's suit/camera view. The separate survey window is diagnostic infrastructure, not an accepted replacement product. Its measurements can inform rendering research; further scope presentation polish does not by itself advance the required integrated thermal view.

## Product design

The player opens a framed sensor display docked at the upper right while using an eligible first-person suit or locally active working camera. A capture uses the entire camera field of view, including camera zoom, at 64×36 samples in quick mode or 128×72 in detail mode, with a 150 m maximum range. The display fits within 36% of the viewport width and 40% of its height, with a maximum 560 logical-pixel width; the camera aspect ratio is preserved. A restrained slate frame and multiline status readout distinguish the instrument from the surrounding native view. The snapshot stays horizontally clear of the centre reticle; the status panel sits above the image. Explicit `TextBuilderModes.Lined` preserves newlines, which Rich HUD's default single-line mode discarded in the first screenshot. No game files or shader installation are needed beyond the existing mod and its Rich HUD setup.

During acquisition the image is blank and the status says **ACQUIRING / hold view steady**. Partial images are not shown. On completion, **SNAPSHOT** and its age remain visible; moving after capture does not move the stored view. This avoids pretending an old low-resolution image is current navigation. The user explicitly refreshes it with `scan`. Palette or range changes acquire a new image rather than changing the legend underneath old colours.

Measured block pixels use the existing Cividis or white-hot palette without lighting modulation. Unmeasured hits use neutral grey diagonal hatching with modest surface-facing shading solely to reveal geometry. They are never assigned a fabricated cold temperature. No-return pixels use neutral dark grey and mean no detected collision within range, not zero kelvin. Terrain, asteroids, characters, debris and unsimulated blocks can therefore appear if the collision query detects them, but are not claimed as simulated temperature readings. Transparent surfaces follow physics occlusion in this candidate; no optical-glass or infrared material model is implied.

Automatic range is calculated from valid measured samples after each completed capture, using the existing padded/quantized range policy. Unknown pixels do not influence exposure. It is immediate exposure for a static capture, not temporal adaptation of a live image. Manual Celsius limits remain available. Neither range nor shape estimates change the simulation.

## Lifecycle and work limits

The existing shared suit/camera eligibility checks apply every draw. Third-person, death, changed camera ownership, a broken/inactive camera, HUD reset and world unload disable and hide the scope. Chat/menu/cursor visibility hides it and cancels unfinished acquisition; returning may show a previously completed labelled snapshot, but never resumes a partial capture automatically.

Queries execute synchronously in the session draw path, never on an arbitrary worker thread. The scanner issues at most 128 queries per draw and stops issuing when its sampling stopwatch reaches 2 ms. A single native call can exceed this soft time limit. HUD submission and non-query work are additional costs, not included in that allowance. Quick mode has 2,304 samples, needs at least 18 sampling draws, and has a two-second wall-clock deadline. Detail mode has 9,216 samples, needs at least 72 sampling draws, and has a four-second deadline. Both deadlines include motion retries and start after the activation chat closes. At 60 draws/s, detail mode needs at least 1.2 seconds before other delays; this is a scheduling lower bound, not a measured native capture time. Both modes retain the same 128-query/soft-2-ms sampling allowance. Detail mode can increase publication and HUD rendering costs; unchanged sampling limits do not guarantee unchanged total frame cost.

More than 5 cm of camera translation, roughly 0.26 degrees of forward/up rotation, or a changed projection invalidates unfinished sampling and starts a fresh sample generation. The total deadline is not extended. Samples are still collected at different world times: moving objects can change during a capture, so a steady camera does not guarantee a simultaneous scene snapshot. This is an acceptance concern, not hidden by the scheduler.

`ThermalVisionRayScan<T>` owns fixed staging/published arrays and a bounded ticket pool. Complete images publish atomically in the scheduler; no partial image is readable. Camera resets do not reset work allowances or forgive outstanding native work. Foreign, duplicate and late tickets cannot release another query's slot or write into a new capture. All scheduler calls require the client update thread. The current adapter completes synchronous tickets immediately; a future asynchronous adapter must marshal callbacks and drain them correctly, not just call this class from workers.

Equal-colour horizontal runs share HUD rectangles. One HUD element and reusable drawing material avoid creating thousands of UI elements per capture. This limits structure and allocation, but native HUD draw cost still needs measurement. The larger normal-view scene is untouched.

## Controls and consolidated test

Reload once to load the candidate source, then:

1. Enable `/thermal telemetry on`.
2. While on foot in first person, run `/thermal vision survey colour`. Hold still until SNAPSHOT or a timeout appears.
3. Include a simulated hot block behind cold armour, a slope, terrain, a character and an unsimulated object in the field of view. Confirm silhouettes are recognisable and that unknown surfaces are hatched rather than falsely cold. Compare the central measured block with the existing crosshair temperature readout.
4. Run `/thermal vision survey grey`; check that the sensor image is entirely greyscale and that temperature order is preserved. The instrument frame and surrounding native view retain their normal colours.
5. Run `/thermal vision detail` for a new 128×72 capture. Hold steady for the longer acquisition and compare thin shapes and the central hot block. `/thermal vision quick` returns to 64×36 and reacquires. Subsequent `scan` and palette changes retain the selected quality. Move after capture: the image must remain explicitly labelled SNAPSHOT and its age must increase. Run `/thermal vision scan` to refresh. Move during acquisition: it must restart or time out rather than combine camera views.
6. Test `/thermal vision range -50 150` and `/thermal vision range auto`; each initiates a new survey. Verify the range label matches the displayed capture.
7. Test third-person, a working camera, zoom, camera loss, chat/menu interruption, off, and world reload. No scope image should survive a disallowed viewpoint or unload. Test a narrow and an ultrawide viewport for distortion and HUD overlap.
8. Run `/thermal dump` after the sequence. `/thermal vision off` closes the candidate.

Important telemetry events contain `survey start`, `survey captured` or `survey timeout`. Capture events include query count, measured/unknown counts, wall duration, total query/sampling CPU milliseconds and the slowest query. The total query count and time include discarded attempts after camera motion; measured/unknown counts describe the completed capture only. General vision frame timings include the adapter work when telemetry is enabled, but not independent Rich HUD rendering cost. Use game frame-time/GPU observation for that remaining cost.

## Full-screenshot follow-up

The supplied full screenshot confirms a visible white-hot collision image, but also exposes two presentation defects: newlines were flattened into one long top banner, and the centred snapshot overlapped the native aiming reticle. The implemented correction uses explicit multiline text and a smaller upper-right dock. An opt-in 128×72 detail capture addresses the coarse sample grid without increasing sampling work per draw. These changes build and have offline layout/scheduler coverage; their native appearance and detailed-capture cost have not yet been measured. The original screenshot does not validate the revised UI.

## First native capture evidence

The manual dump `Thermodynamics_Telemetry_20260918_233019.log`, ending 2026-09-18 23:32:29 UTC, records three successful suit-view captures on game 1.210.14: one Cividis and two white-hot. There are 1,335 recorded vision draw frames, zero render errors, no survey timeout events, and no discarded-query excess in these captures.

| Capture | Queries | Measured / unknown / no return | Wall acquisition | Sample CPU total | Slowest sample |
| --- | ---: | --- | ---: | ---: | ---: |
| Cividis | 2,304 | 1,218 / 0 / 1,086 | 333.3 ms | 34.29 ms | 1.24 ms |
| White-hot | 2,304 | 1,218 / 0 / 1,086 | 351.5 ms | 32.07 ms | 0.08 ms |
| White-hot refresh | 2,304 | 1,218 / 0 / 1,086 | 314.4 ms | 32.05 ms | 0.08 ms |

The range is 325–700 K (51.85–426.85 °C) in all three. No-return counts are inferred as total samples minus measured and unknown samples; they are not proof that the rendered scene is empty. Unknown-surface presentation, cameras/zoom, invalid-view shutdown and movement handling are not established by this dump. A successful capture event does not prove that the native HUD image was visible or visually acceptable.

Combined sample cost is 98.41 ms for 6,912 queries, or about 14.24 µs/sample in this particular scene, including lookup/projection overhead measured by the adapter. At that cost, a naive 64×36 image refreshed at 10 Hz would average about 5.47 ms per 60 Hz game frame for sampling alone; 96×54 would average 12.30 ms. These are linear workload projections, not measured continuous-renderer results. They support the snapshot compromise and do not justify silently switching to a continuously refreshed sensor.

The largest recorded survey adapter draw is 14.428 ms in a white-hot `survey-captured` row. That row includes publication frames as well as held-image frames, so the report cannot identify publication, status updates, allocation, or another pause as the cause. Separate Rich HUD drawing/GPU cost is not included. The overall 0.116 ms mean is diluted by held-image frames and must not be quoted as acquisition cost.

The report also repeats the wider simulation-clock consistency warning (grid totals are 2.39 times the containing session timer; an earlier dump reports 2.40). This is a separate telemetry-accounting issue and prevents treating those global totals as a valid budget comparison. It does not explain the survey spike or establish its cause.

## Native detail-capture evidence

The manual dump `Thermodynamics_Telemetry_20260918_234403.log`, ending 2026-09-18 23:45:51 UTC, records one quick and one detail capture in white-hot suit view, with zero render errors across 1,405 recorded draw frames and no timeout events.

| Capture | Queries | Measured / unknown / no return | Wall acquisition | Sample CPU total | Slowest sample |
| --- | ---: | --- | ---: | ---: | ---: |
| 64×36 quick | 2,304 | 1,119 / 0 / 1,185 | 362.0 ms | 33.24 ms | 1.05 ms |
| 128×72 detail | 9,216 | 4,463 / 3 / 4,750 | 1,466.5 ms | 122.08 ms | 0.10 ms |

No-return counts are inferred as above. Both captures used 325–700 K (about 52–427 °C). The three unknown samples do not identify a surface type or validate terrain/character coverage. The maximum recorded adapter draw was 5.485 ms; Rich HUD/GPU cost remains outside those timings. Rows aggregate held-image and publication work and do not establish the cause of their spikes.

The full screenshot supplied with this dump confirms that status lines now break correctly, the detail image is visible at the upper right, and the native centre reticle is clear of it. It also confirms the remaining product mismatch: this is a separate frozen, visibly coarse image rather than integrated thermal vision. The screenshot's snapshot age is 8.4 seconds; it must not be interpreted as rendering latency or acquisition duration.

Detail sampling averaged approximately 13.25 µs/sample. Repeating that measured workload at 10 complete images/s would require about 1.22 CPU-seconds per second for sampling alone, before HUD submission and GPU costs. This linear projection is not a continuous-renderer benchmark, but establishes that simply enlarging and refreshing the current survey image is not a demonstrated route to smooth integrated vision. Reducing required queries or finding a different rendering path remains necessary; further resolution increases alone do not solve the problem.

## Acceptance decision

This candidate is ready for **evaluation**, not release certification. Accept it as an optional inspection tool only if the native HUD stays readable, unknown hatching is clear, the collision approximation is useful, the scope consistently completes while stationary, and sampling does not create noticeable stalls. A timeout is an explicit failure, not permission to increase budgets blindly. Thin-object loss, own-character/camera collision interference, streaming range, subgrid mapping, moving targets, real HUD ordering and the actual game compiler remain engine checks.

Detail mode now provides a higher-resolution static capture. Selective extra samples for known small hot blocks remain a possible further refinement, evaluated against the thin-pole counterexample; increased resolution alone still cannot guarantee detection of subpixel objects. A continuously refreshed fullscreen sensor is **not** bundled as a second finished solution: camera reprojection, disocclusion and a native performance budget remain unresolved for that design.

## Change log

| Date | Change |
| --- | --- |
| 2026-09-18 | Recorded successful 128×72 native capture and screenshot-confirmed layout fixes; reaffirmed the survey's diagnostic-only role after the user's integration clarification. |
| 2026-09-18 | Fixed flattened status text, docked the scope away from the reticle, and added opt-in 128×72 detail capture with unchanged per-draw sampling limits and a four-second deadline. |
| 2026-09-18 | Recorded the first three successful native survey captures, measured query workload, an unattributed adapter spike, and the remaining visual/coverage checks. |
| 2026-09-18 | Added the implemented survey-scope alternative, complete controls/data semantics/work limits, failure behavior and consolidated native acceptance procedure. |
