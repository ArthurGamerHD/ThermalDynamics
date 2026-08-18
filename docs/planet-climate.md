# Planet climate

How the air outside a grid is decided, what it was measured at, and what is still open. This is a
working record as much as a description: the numbers below came from a test world, and the parts
that are still guesses say so.

The equations themselves live in [thermal-model.md](thermal-model.md#environment); the settings in
[configuration.md](configuration.md); the dump that measures any of it in
[telemetry.md](telemetry.md#climate-dump).

## What the game gives, and what it does not

| Wanted | The game's answer | Usable? |
| --- | --- | --- |
| Ambient temperature | `MyVisualScriptLogicProvider.GetTemperatureInPoint` — a 0..1 comfort scale from the planet's `DefaultSurfaceTemperature`, weather, sun angle and oxygen | Not as kelvin. Logged beside ours as a reference to fit against. |
| Air density | `MyPlanet.GetAirDensity` | Yes, and used directly. |
| Ground material | `MyVoxelBase.GetMaterialAt` → `MyVoxelMaterialDefinition.Id.SubtypeName` | Yes. Sample below the surface point; on the boundary it answers about air as often as ground. |
| Wind speed | `MyPlanet.GetWindSpeed` = the planet definition's **maximum** wind × air density | No. A rating, not a wind — see below. |
| Wind direction | nothing | No. Invented; see `WindField`. |
| Weather | `MyVisualScriptLogicProvider.GetWeatherIntensity` | Yes, as a 0..1 intensity. |
| Is it night here | `MySectorWeatherComponent.IsThereNight` | Not needed; the sun vector answers it. |
| Sun direction | `MyVisualScriptLogicProvider.GetSunDirection` | Yes, and it points **toward** the sun — `MySector.DirectionToSunNormalized` is the same vector. |

## Measured, before any of this

A test world with three grids on an earthlike planet, telemetry on, run across a sunrise. Fifty
climate rows per site:

| site | latitude | air density | ambient | day-night swing |
| --- | --- | --- | --- | --- |
| Desert, `Sand_02` | 6.9° | 1.00 | 11.2 – 20.0 °C | 8.9 K |
| Grass, `Grass bare` | 24.5° | 0.98 | 11.7 – 18.6 °C | 6.9 K |
| Snow, `Snow` | 40.7° | 0.61 | 3.9 – 14.3 °C | 10.4 K |

Three faults, all structural rather than mistuned:

* **Latitude did nothing.** There was no latitude term. The 7° and 41° sites differed only through
  air density, which is a fact about their altitude.
* **The ground did nothing.** A snowfield sat between +4 and +14 °C. The game's own figure puts snow
  far colder relative to desert (0.138 against 0.331 at night), so the information existed and was
  not being read.
* **Ambient tracked the sun exactly**, peaking at the instant of noon, which happens nowhere.

Two defects fell out of the same dumps and are fixed: every occlusion setting was loading as `false`
in that world (see [known-issues.md](known-issues.md)), and parked grids were standing in 80 m/s of
wind.

## What the model does now

`ClimateModel.Target` gives the ambient a place is heading toward, and `ClimateModel.Follow` chases
it:

* **Latitude.** A planet's `DayTemperature` and `NightTemperature` are its *equatorial* figures.
  `PoleTemperatureDrop` (40 K by default) is the span to its poles, interpolated on cos(latitude)
  because that is how squarely the sun strikes a band. Zero gives one climate for a whole world,
  which is what it used to be.
* **Ground.** The voxel material under the grid shifts the air and widens or narrows its day
  ([GroundTemperature](../Data/Scripts/Thermodynamics/Core/Definitions/GroundTemperature.cs)). Snow
  −14 K at 0.7× swing, sand +8 K at 1.7×, because dry ground holds nothing overnight. Matched on the
  *word* in the material name, not the exact subtype, so other worlds' spellings still land; anything
  unrecognised leaves the planet untouched. `ClimateGroundInfluence` scales the whole table.
* **Lag.** First-order, `AmbientLagSeconds` (45 s of play). The day's peak then lands after noon and
  the low before dawn without either being written down.

Predicted for the same three sites, against the real places they resemble:

| site | night | noon | swing | resembles |
| --- | --- | --- | --- | --- |
| Desert | 13.9 °C | 32.6 °C | 18.7 K | Sahara, 5–38 °C |
| Grass | 6.4 °C | 17.4 °C | 11.0 K | temperate, 8–22 °C |
| Snow | −12.0 °C | −4.3 °C | 7.7 K | alpine, −10–2 °C |

Unverified in game at the time of writing: predicted from the model with the test world's own
latitudes and materials, not measured. **The next dump should check these three rows first.**

## Wind

`MyPlanet.GetWindSpeed` returns `Generator.Atmosphere.MaxWindSpeed × airDensity` — 80 m/s everywhere
on an earthlike world at sea level, the same at the pole as at the equator, with no direction. It is
what wind turbines are balanced against. Read as a wind it put every parked ship over the friction
threshold, heating standing still at up to 4.1 kW a block, and ran convection at nearly twice its
still-air rate planet-wide.

[WindField](../Data/Scripts/Thermodynamics/Core/Simulation/WindField.cs) treats that as the ceiling
it is. Earth's bands — trades blowing west to 30°, westerlies to 60°, polar easterlies beyond — as a
**bearing that turns** rather than components that are mixed and normalised, because the latter snaps
round at every band edge where the fading zonal term leaves only the sideways one. Speed is a
fraction of the ceiling: about an eighth in fair weather, half in the worst weather the game reports,
times a steady per-place variation so one valley is windier than the next.

It is a map, not a simulation. The properties worth keeping are that it is steady, cheap, different
in different places, and recognisable from a cockpit.

## Open

* **Air density still multiplies ambient after everything else.** It is doing more work than it
  should: on a small planet it collapses ambient toward vacuum within a couple of kilometres, and it
  was the only reason the three test sites differed before latitude and ground existed. Loosening
  that coupling — ambient on its own gentler curve, convection and solar decay still on density — is
  the next change, and wants one more dump first.
* **The sun is faster than the sampling.** In the test world it moved 105° between two climate rows
  61 seconds apart, so the day is aliased and the lag cannot be checked against it. Either lengthen
  the world's sun rotation interval or shorten `ProfileInterval` in
  [ThermalGridEnvironment](../Data/Scripts/Thermodynamics/Game/ThermalGridEnvironment.cs).
* **No latitude spread in the data.** The three sites span 7°–41°. The pole drop is the least
  evidenced term in the model; a fourth grid near a pole would settle it.
* **Ground offsets are opinions.** The table's numbers were chosen to look like Earth, not fitted to
  anything. The game's own `game_temperature` column is in the dump precisely so they can be.
* **Underground is a single number.** `UndergroundTemperature` ignores depth, latitude and material,
  and `CoreTemperature` and `SealevelDeadzone` are defined but unused.
