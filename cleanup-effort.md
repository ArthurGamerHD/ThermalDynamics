# Cleanup effort

What a pass over the code found, what was changed, and — more usefully — what was found and
**deliberately not** changed, with the reason. A reduction nobody records is a reduction the next
reader proposes again.

The rules this page is bound by are stated canonically in [docs/rules.md](docs/rules.md).

## What was changed

| # | Change | Why it was safe |
| --- | --- | --- |
| 1 | **One preamble for the API's grid accessors.** Five accessors in `ThermalApi` opened with the same three-line resolve — null grid, null game logic, no `ThermalGrid` — so `GridOf(IMyCubeGrid)` now sits beside the `GridOf(IMySlimBlock)` that already existed. | A pure extraction: the same three checks in the same order. Five copies is five chances to check two of the three, and the failure would be an exception crossing the API boundary into another mod. |
| 2 | **`LiveGrids` cannot be null, and three callers checked anyway.** `Live` is a `static readonly` list only added to and removed from. Three callers guarded against null and six did not, which left a reader unable to tell whether the six were a latent `NullReferenceException` or the three were dead code. | They were dead code. The contract is now stated on the property, and it names the case that *is* real: the list can be empty, which is a different check and is kept. |

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
