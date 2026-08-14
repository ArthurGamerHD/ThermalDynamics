# Development

## Prerequisites

* Space Engineers installed (the project references DLLs from `Bin64/` directly).
* .NET Framework 4.7.2 targeting pack, or a Mono/MSBuild setup that can target `net472`.
* The mod's own runtime dependencies for testing in game: Definition Extensions
  (`2756894170`) and Text HUD API.

## Building

`Generic.csproj` / `Generic.sln` exist **only to get IntelliSense and compiler errors**. Space
Engineers compiles `Data/Scripts/**/*.cs` itself at world load; the produced `Generic.dll` is
not shipped or loaded.

```bash
dotnet build Generic.csproj -c Release
```

The `<HintPath>` entries are absolute and point at this machine's Steam library:

```
/home/gauge/Steam/SteamLibrary/steamapps/common/SpaceEngineers/Bin64/
```

Adjust them for another machine. Two things about that reference set are easy to get wrong:

* **Target `net48`, not `net472`.** `VRage.Platform.Windows` and its RestSharp dependency are
  built against .NET Framework 4.8; at 4.7.2 they silently fail to resolve.
* **Do not reference `VRage.Native.dll`.** It is unmanaged and produces
  `MSB3246: PE image does not have metadata`.

XML documentation ships next to the DLLs for `Sandbox.Game`, `VRage.Game`, `VRage` and others,
so IntelliSense will show the engine's own docs once the references resolve. See
[engine-api-notes.md](engine-api-notes.md) for a survey of what is available. Both `*.csproj` and `*.sln` are in
[.gitignore](../../.gitignore) at the repo root, along with `bin/`, `obj/` and `.vs/` — the
checked-in copies here predate that rule, so avoid committing further changes to them.

Constraints that matter when writing code for this project:

* **C# 6 only** (`<LangVersion>6</LangVersion>`). No tuples, no `switch` expressions, no
  string interpolation beyond what C# 6 supports, no `out var`.
* Space Engineers' script whitelist applies: no reflection, no file I/O outside
  `MyAPIGateway.Utilities`, no threading. The solver is order-independent by
  construction, so it could be parallelised if the whitelist ever allowed it.
* **The whitelist covers exception types too**, and this is easy to miss because the local build
  and `sim/` both accept them — only the in-game compiler rejects them, at world load. Confirmed
  prohibited: `IndexOutOfRangeException`, `ArgumentOutOfRangeException`. Confirmed allowed:
  `Exception`, `ArgumentException`, `ArgumentNullException`, `InvalidOperationException`,
  `FormatException`. Prefer bounds-checking over catching an out-of-range throw — see
  `ThermalStorageCodec.TryDecodeVersion2`, which validates each record count against the
  remaining payload rather than reading and catching.
* Anything that allocates per frame will show up. The existing code pools the raycast result
  lists (`_overlapResultPool`, `_gridPool`) and caches `kA` arrays for this reason.

## Deploying to the game

Space Engineers loads local mods from its `Mods` directory. On Windows the repo ships
[symlink-to-semods.bat](../../symlink-to-semods.bat) at the repo root, which creates junctions
from each mod folder into `%AppData%/SpaceEngineers/Mods`. On Linux, symlink manually:

```bash
ln -s /home/gauge/Content/git/SpaceEngineers/One/ThermalDynamics \
      ~/.local/share/SpaceEngineers/Mods/ThermalDynamics
```

Then enable "ThermalDynamics" in the world's mod list alongside the two dependencies.

Script changes take effect on world reload. Definition XML changes also require a reload.

## Repo conventions

* One mod per top-level folder in the `One` repository; this folder is self-contained.
* [metadata.mod](../metadata.mod) and [modinfo.sbmi](../modinfo.sbmi) carry the workshop
  identity — `modinfo.sbmi` holds the Steam workshop id `2985582372`. Do not regenerate them,
  or the mod will publish as a new item.
* [ModIdFinder.sh](../../ModIdFinder.sh) at the repo root resolves workshop ids across the
  mods in this repository.

## Do not restructure `Models/`

Both [note.txt](../note.txt) and [Models/note.txt](../Models/note.txt) say the same thing:

> don't change the folder structure of /models/ otherwise the block LODs break.

LOD and build-stage model paths are baked into the `.mwm` binaries at export time. Moving or
renaming a folder under `Models/` silently breaks LOD switching in game, and the only fix is
re-exporting the models.

Naming pattern under `Models/Gauge/{LG,SG}/`:

| Suffix | Meaning |
| --- | --- |
| *(none)* | Full-detail model |
| `_LOD1` … `_LOD3` | Progressively lower detail |
| `_BS1` … `_BS3` | Build-stage models, referenced from `<BuildProgressModels>` |

## Vendored third-party code

Do not hand-edit these; replace them wholesale when the upstream author publishes a new version.

| Path | Upstream |
| --- | --- |
| [HudAPIv2.cs](../Data/Scripts/Thermodynamics/HudAPIv2.cs) | Draygo's Text HUD API client |
| [DefinitionExtensionsAPI.cs](../Data/Scripts/Thermodynamics/DefinitionExtensionsAPI.cs) | Draygo's Definition Extensions client |
| [NetworkAPI/](../Data/Scripts/Thermodynamics/NetworkAPI) | SENetworkAPI |

## Debugging

* **In game:** the debug toggles in [configuration.md](configuration.md#debug-toggles) cover
  temperature, solar intensity, exposed surfaces, friction, and the solar/wind rays. The
  crosshair readout in [Debug.cs](../Data/Scripts/Thermodynamics/Debug.cs) dumps a block's full
  thermal state including raw surface bits.
* **Logs:** everything logs to the SE log with the `[Thermodynamics]` prefix via
  `MyLog.Default.Info`.
* **Timing:** switch telemetry on with `/thermal telemetry on` and take a report with
  `/thermal dump`. The cost section breaks the update into topology rebuild, room mapping,
  exposure refresh and solver, which is the breakdown you want before optimising anything. See
  [telemetry.md](telemetry.md).
* **Outside the game:** most questions are faster to answer in [`sim/`](../sim) —
  `dotnet test`, or `dotnet run --project Thermodynamics.Sim -- run perf` for throughput.

## Where to start when adding a feature

| Goal | Touch |
| --- | --- |
| New thermal property on blocks | `ThermalCellDefinition` and `BlockThermalProperties`, the copy in `ThermalBlockCatalog.ToThermalProperties`, then `Data/Cubes.xml` |
| New pipe or pump shape | `ThermalCoolantShapes`, a new SBC in `Data/CubeBlocks/`, an entry in `Cubes.xml`, and the block variant group. The table is linked into the test project, so add a case to `HostAdapterTests` |
| Change how heat moves between blocks | `ConductionBuilder` in `Core/Simulation/ThermalLink.cs` |
| Change environmental response | `EnvironmentSolver`, and `ThermalGrid.Sample` for what the world feeds it |
| Change what counts as exposed | `SurfaceMap.GetExposedFaces`, and `BlockSurfaceBuilder` for what a block declares |
| Change what the game tells a block | `ThermalBlock.Attach` |
| Per-block sun shadowing | `ThermalNode`'s directional weighting is the hook; the solver resolves the sun into six per-face weights once per step |
| New instrumentation | `ISimulationProfiler` for a stage, `GridTelemetry` for a figure, `TelemetryReport` for a row |
| Multiplayer sync | `SENetworkAPI` is already initialised in `Session.Init` with channel `30323`; no commands are registered yet |
