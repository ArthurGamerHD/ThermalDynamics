# Cleanup effort

What a pass over the code found, what was changed, and — more usefully — what was found and
**deliberately not** changed, with the reason. A reduction nobody records is a reduction the next
reader proposes again.

> The rules argued here are stated canonically in [docs/rules.md](docs/rules.md): `E5` `D5` `M7` `P4`.

## What was changed

| # | Change | Why it was safe |
| --- | --- | --- |
| 1 | **One preamble for the API's grid accessors.** Five accessors in `ThermalApi` opened with the same three-line resolve — null grid, null game logic, no `ThermalGrid` — so `GridOf(IMyCubeGrid)` now sits beside the `GridOf(IMySlimBlock)` that already existed. | A pure extraction: the same three checks in the same order. Five copies is five chances to check two of the three, and the failure would be an exception crossing the API boundary into another mod. |
| 2 | **`LiveGrids` cannot be null, and three callers checked anyway.** `Live` is a `static readonly` list only added to and removed from. Three callers guarded against null and six did not, which left a reader unable to tell whether the six were a latent `NullReferenceException` or the three were dead code. | They were dead code. The contract is now stated on the property, and it names the case that *is* real: the list can be empty, which is a different check and is kept. |

| 4 | **The per-face frame, computed once instead of four times.** Counting a block's exposure, auditing it and building a room map each walked the six faces of a block's bounds and each worked out the same six numbers to do it. `BoxGeometry.Span` returns them. | Measured, because it is the surfaces hot path — see below. 52 bit-identity tests pass unchanged, and `FaceSpanTests` is written against the arithmetic rather than against the extraction, so it would have caught a copy that had drifted before it was one function. |

### Measuring cleanup 4, and why one reading was not enough

**The first paired reading said the refactor was 1.9 % slower and it was noise.** Best-of-N moved
5.474 → 5.579 ms while the median moved the other way, −0.3 %, which is the signature of a
difference smaller than the run's own spread.

Three paired rounds, each running both branches inside one hold:

| round | master | cleanup 4 | Δ |
| --- | ---: | ---: | ---: |
| 1 | 5.518 ms | 5.498 ms | −0.020 |
| 2 | 5.591 ms | 5.578 ms | −0.013 |
| 3 | 5.617 ms | 5.611 ms | −0.006 |

**The refactor is at worst neutral: it is faster in all three pairs, by a quarter of a per cent.**
What the table shows more usefully is `master` drifting **upward across rounds** — 5.518 to 5.617,
1.8 %, on unchanged code — which is larger than the effect being measured and is why the two
branches have to be read inside one round rather than across two runs. A before-and-after taken an
hour apart on this machine measures the hour.

## What was found and not changed

### Nine unused `TryGet*` methods in `DefinitionExtensionsAPI.cs`

`TryGetString`, `TryGetInt`, `TryGetLong`, `TryGetFloat`, `TryGetColor`, `TryGetVector2I`,
`TryGetVector2D`, `TryGetVector3I` and `TryGetVector3D` are called from nowhere in this repository;
only `TryGetBool` and `TryGetDouble` are, from the three typed readers in `Definitions/`.

**Not removed, because the file is vendored.**
[architecture.md](docs/architecture.md) files it with `NetworkAPI/` and `RichHudFramework/` as
*third-party API clients — do not edit; replace wholesale when upstream updates*. Trimming a
vendored client to the subset this mod happens to use converts every future update from a copy into
a merge, and buys 456 lines that cost nothing at runtime: they are never called, so they are never
JITted.

**This is the rule rather than the exception for the three vendored trees**, and the reason is worth
stating once: dead code inside a vendored client is not this repository's dead code. It is
somebody else's live code that this repository does not happen to call.

---

## Change log

| Date | Change |
| --- | --- |
| 2026-08-29 | Opened. Two changes merged — one preamble for the API's grid accessors, and the three dead null checks against a `LiveGrids` that cannot be null — and one finding recorded rather than acted on: the nine unused `TryGet*` methods in the vendored `DefinitionExtensionsAPI.cs`, which stay because trimming a vendored client to the subset this mod happens to call turns every future update from a copy into a merge. |
