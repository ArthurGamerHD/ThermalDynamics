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
| Weather intensity | `MyVisualScriptLogicProvider.GetWeatherIntensity` | Yes, as a 0..1 intensity. |
| Which weather | `MyVisualScriptLogicProvider.GetWeather` → the effect's subtype name | Yes. `RainHeavy`, `SandStormLight`, and thirty-one others. |
| What that weather does | `WeatherEffects.sbc` carries `TemperatureModifier`, `SolarOutputModifier` and `WindOutputModifier` per effect | Yes, and the source of the whole response table. |
| Depth below ground | `MyPlanet.GetClosestSurfacePointGlobal`, against the grid's own radius | Yes, and already cached for the ground material. |
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

## What the four-minute day measured

The sun was set to a full cycle in four minutes, which fixed the aliasing the previous run
complained about: sixteen climate rows per day instead of three and a half. Three grids, 851
seconds, the same three sites. With a readable day curve, three faults that had been invisible
became unmissable.

**Ambient collapsed toward vacuum wherever the air thinned.** The snowfield, 5.6 km up at an air
density of 0.612, reported **36 K — −237 °C — for the entire run**, and every block on the grid
froze to match: peak block temperature 51 K. The desert and grass sites were untouched, which is
what made it look like a site problem rather than a model one.

It was neither. The scale for thin air was applied to the *running* ambient rather than to the
target the ambient was chasing, so it compounded against the lag on every step:

```
ambient = Follow(previousAmbient, target, dt, τ)   // chases the raw target
ambient = ambient × atmosphereFactor               // then scales the result
```

`previousAmbient` is the already-scaled value, so the steady state is `f·k / (1 − f + f·k)` with
`k = 1 − e^(−dt/τ)`. At `f = 0.977`, `dt = 1/6 s` and `τ = 45 s` that is **13.8% of target**.
Simulating those constants against the site's own latitude and ground predicts 35.88–36.94 K; the
telemetry measured 35.86–36.92. The desert and grass sites escaped only because their density
rounds `f` to 1.0000.

Fixed by making everything a target and applying the lag once, last. The equations are in
[thermal-model.md](thermal-model.md#environment); the arrangement is pinned by
`ThinAirDoesNotCompoundAgainstTheLag`, which reproduces the failure at 40 K if the ordering is ever
put back.

**Every grid froze on world load.** Ambient started at 2.7 K and took about three minutes of play
to climb to its real value. The desert's mean block temperature fell from 257 K to **103 K** in the
first nineteen seconds before recovering. The first step of a session resolves no planet, so the
vacuum seed becomes the previous ambient, and `Follow`'s `if (current <= 0f)` guard never fires
because 2.7 is not zero. A sample now says whether it has a history worth chasing from, and a grid
that has just arrived — or has just crossed from one planet to another — starts at its climate.

**The lag does not scale with day length.** 45 seconds against a four-minute day attenuates the
swing to about 46%: the desert's intended 18.7 K measured 8.7 K. Against a default two-hour SE day
the same setting is nearly a no-op. The lag is physically a fraction of a day and is written as
absolute seconds. `AmbientLagSeconds` is now settable per planet in
[Planets.xml](../Data/Planets.xml) so a short-day world can be tuned, but it is still a number
somebody has to know to change — see [Open](#open).

Two things the run confirmed working: the lag puts the desert's ambient peak about 50 seconds after
solar noon, which is exactly what it is for; and latitude and ground now separate the three sites,
which was the previous round's whole purpose.

Unrelated but visible in the same report: **the solver clamped on every step of the run** — 3,389
of 3,389, on all three grids, at the 16-substep cap.

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
* **Lag.** First-order, `AmbientLagSeconds` (45 s of play), applied once to a finished target. The
  day's peak then lands after noon and the low before dawn without either being written down.
* **Altitude.** `AmbientLapseRate` (4 K/km) cools the air as it rises, which is what makes a
  mountain colder than its valley. Earth's figure is 6.5; this is lower because the ground table
  already makes mountains snowy, and the two together put a 5.6 km peak near −50 °C.
* **Thin air.** Ambient fades toward vacuum on `1 − (1 − d)⁸`, blunter than the `1 − (1 − d)⁴`
  curve convection and solar decay run on. Density decides when there stops being air to have a
  temperature at all, and that happens at the edge of space rather than gradually all the way up.
* **Weather.** The air is colder in rain and snow, warmer in a sandstorm, the sun is dimmer, the
  wind is the weather's own rather than the intensity's, and wet air strips heat faster. See
  [Weather](#weather) below.
* **Depth.** Underground the day damps out over tens of metres and the rock warms toward the core
  below the sea-level deadzone. See [Underground](#underground) below.

Predicted for the same three sites, in clear air, against the real places they resemble:

| site | altitude | night | noon | swing | resembles |
| --- | --- | --- | --- | --- | --- |
| Desert | −42 m | 14.0 °C | 32.9 °C | 18.9 K | Sahara, 5–38 °C |
| Grass | 293 m | 5.2 °C | 16.3 °C | 11.1 K | temperate, 8–22 °C |
| Snow | 5,588 m | −34.5 °C | −26.7 °C | 7.8 K | high alpine, −25 to −15 °C |

The desert and grass rows are essentially the previous round's predictions — both are near sea
level, so the lapse rate barely touches them and the collapse never did. The grass site drops a
kelvin because its 293 m is now worth something. The snow row is the one
that moved, and it moved twice: from the 36 K it actually measured up to the −18 to −10 °C the model
intended, and then back down to −34 °C once altitude became a term of its own. **The next dump
should check the snow row first, and it is the row most likely to still be too cold** — the lapse
rate and the snow ground offset are both saying "it is cold up here" and only one of them has
evidence behind it.

## Weather

The game has a weather system and, until this round, the temperature model ignored it. `weather` was
read, used to pick a point on a calm-to-storm scale for the wind, and thrown away. In the
four-minute run only the grass site saw any weather at all — an intensity of 1.0 decaying to 0 — and
its entire visible effect was the wind falling from 50.5 to 11 m/s. Ambient, solar and convection
did not notice a storm.

The game knows more than that, and had all along. `MyVisualScriptLogicProvider.GetWeather` returns
the effect's subtype name, and every effect in Keen's `WeatherEffects.sbc` carries three authored
modifiers:

| | `TemperatureModifier` | `SolarOutputModifier` | `WindOutputModifier` |
| --- | --- | --- | --- |
| `SnowHeavy` | −2 | 0.10 | 2.00 |
| `RainHeavy` | 0.4 | 0.30 | 1.45 |
| `SandStormHeavy` | 3 | 0.10 | 2.25 |
| `FogHeavy` | 0.3 | 0.15 | 0.10 |
| `ExtremeHeat` | 2 | 1.75 | 0.10 |

[WeatherResponse](../Data/Scripts/Thermodynamics/Core/Definitions/WeatherResponse.cs) is that table,
converted. The two multipliers pass straight through — one onto solar irradiance, one onto the wind
the field produces. `TemperatureModifier` is a factor on the game's 0..1 comfort figure rather than
a temperature, so it becomes kelvin as `clamp(m − 1, −3, 3) × 6 K`: heavy snow lands at −18 K, a
sandstorm at +12 K. The clamp is there for the alien weathers, which run to 11.

Matched on the kind word rather than the exact subtype, exactly as the ground table is. There are
33 weathers in the base game and most are the same handful of kinds with a prefix —
`AlienRainHeavy`, `MarsStormLight` — so thirteen words cover all of them, and a mod that adds
`RainHeavier` gets rain behaviour without annotating anything. A name carrying `light` gets half the
departure from calm, which is about what Keen's own light/heavy pairs differ by. Order matters in
one place: `lowwind` has to be tested before `wind`, or `LowWinds` reads as a gale.

Two things are the mod's own rather than the game's:

* **Convection.** Nothing in the definitions records that rain is *wet*, and wet air pulls heat off
  a hull far faster than dry air of the same speed — a fog barely moves and still carries heat away,
  which the wind term alone cannot express. Heavy rain 2.5×, hail 2.8×, snow 2.2×, sandstorm 1.4×,
  fog 1.3×. These are opinions in the way the whole ground table is.
* **The swing.** Cloud that keeps the sun off by day keeps the heat in at night, so it is one fact
  and not two: the day-night swing follows the solar multiplier rather than getting a column,
  halving under full overcast and untouched in clear air.

`ClimateWeatherInfluence` scales the lot, and 0 restores exactly the old behaviour — weather
affecting nothing but the wind. Clear air costs one float compare: an intensity of zero softens the
whole response to calm, whose every term is the identity.

## Underground

`UndergroundTemperature` used to be the entire model: one number, at any depth, on any planet, at
any latitude. `CoreTemperature` and `SealevelDeadzone` had been defined in
[Planets.xml](../Data/Planets.xml) and read into the properties for as long as the file has existed,
and nothing had ever looked at them.

Two things happen going down, and they happen at very different scales.

**The day stops.** Rock is slow, so the further into it a tunnel goes the less of the surface's
swing reaches it. `UndergroundDampingDepth` (20 m) is where that finishes: at the surface a buried
block feels the whole day, halfway down it feels half, and below it there is no day left and the air
is simply `UndergroundTemperature`. Sun, weather and the ground table all damp out with that one
term, for free — a cold night and a hot noon converge on the same rock, which is the point of it.

**The planet is hot inside.** Below `SealevelDeadzone` (2 km) the rock warms linearly toward
`CoreTemperature`, reaching it at the centre. On an earthlike's 60 km radius with the shipped 3000 K
core that is about 47 K/km, roughly twice Earth's crustal gradient.

The deadzone is measured from **sea level** and not from the surface, which is the detail that makes
it behave: a tunnel bored a kilometre into a mountainside stays cold however far in it goes, because
it is deep in the rock and still four kilometres above the hot part, while a shaft sunk from a beach
reaches the same depth and starts warming.

Depth costs nothing. The surface height under a grid is already looked up for the ground material
and cached on the same 40 m movement rule, so between refreshes depth is the difference of two
radii — a subtraction, exact for a shaft sunk straight down, and anything moving far enough sideways
for it not to be has already tripped the resample.

**With the shipped 2 km deadzone the core term is out of reach in ordinary play**, since SE's voxels
do not go down that far. That is deliberate rather than an oversight: the model is correct and the
tuning lever is documented. Lowering `SealevelDeadzone` to a few hundred metres is how a planet
author makes deep mining hot.

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

* **Nothing here has been measured in game yet.** Everything above is predicted from the model with
  the test world's own latitudes, materials and altitudes. The whole point of the next dump is to
  check it, and the new `depth_m`, `weather`, `weather_ambient_k` and `convection_coeff` columns are
  in the climate CSV precisely so it can be.
* **The lag is in absolute seconds and the day is not.** `AmbientLagSeconds` = 45 attenuates the
  swing to 46% of its intended size against a four-minute day and does essentially nothing against a
  default two-hour one. It is physically a fraction of a day. `MySectorWeatherComponent` exposes a
  `RotationInterval` property, which is the sun's period and would let the lag be expressed as a
  share of it — whether that type is reachable under the mod whitelist is untested, and until it is,
  a short-day world tunes the figure by hand in [Planets.xml](../Data/Planets.xml).
* **The lapse rate and the ground table both say mountains are cold.** 4 K/km is a compromise chosen
  to keep the snow site from landing near −50 °C with Earth's 6.5, not a figure fitted to anything.
  The honest fix is probably that the ground offsets should shrink as the lapse rate grows, since
  a site is only snowy *because* it is high. The snow row is where that will show.
* **No latitude spread in the data.** The three sites span 7°–41°. The pole drop is still the least
  evidenced term in the model; a fourth grid near a pole would settle it.
* **No weather in the data either.** Only one of three sites saw any weather in 851 seconds, and
  none of it was snow, sand or fog. `/weather SnowHeavy` forces one, which is the cheapest way to
  get a row per kind into a dump.
* **Ground and convection offsets are opinions.** Both tables' numbers were chosen to look like
  Earth, not fitted to anything. The game's own `game_temperature` column is in the dump precisely
  so they can be.
* **The core gradient is unreachable in play.** See [Underground](#underground): correct, and behind
  a 2 km deadzone that SE's voxel depth does not reach. Worth revisiting if the deadzone default
  should be lower rather than the model deeper.
* **The solver clamps on every step.** 3,389 of 3,389 in the four-minute run, on all three grids, at
  the 16-substep cap — nothing to do with the climate, and it means the integrator is running at its
  accuracy floor throughout. See [bugs-and-performance.md](bugs-and-performance.md).
