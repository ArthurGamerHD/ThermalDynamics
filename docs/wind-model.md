# The wind model

What the game gives, why it is not enough, what replaces it, and what is still guesswork.

## 1. What Space Engineers actually computes

Decompiled from the shipped `Sandbox.Game.dll`, in full:

```csharp
public float GetAirDensity(Vector3D worldPosition)
{
    if (Generator == null) return 0f;
    if (Generator.HasAtmosphere)
    {
        double num = (worldPosition - base.WorldMatrix.Translation).Length();
        return (float)MathHelper.Clamp(
            1.0 - (num - (double)AverageRadius) / (double)AtmosphereAltitude, 0.0, 1.0)
            * Generator.Atmosphere.Density;
    }
    return 0f;
}

public float GetWindSpeed(Vector3D worldPosition)
{
    if (Generator == null) return 0f;
    float airDensity = GetAirDensity(worldPosition);
    return Generator.Atmosphere.MaxWindSpeed * airDensity;
}
```

That is the whole of the engine's wind. Seven facts follow from it, and they decide everything else
in this document:

1. **There is no direction.** It returns a scalar. Nothing anywhere in the mod API exposes a wind
   vector; the only directional wind in the engine is `MyRenderProxy.Settings.WindStrength`, which is
   a foliage animation parameter and carries no bearing either.
2. **There is no time.** Midnight and noon return the same number.
3. **There is no latitude or longitude.** Every point at a given altitude on a planet returns the
   same number. A pole and the equator are identical.
4. **There is no terrain.** The altitude term is measured against `AverageRadius` — the planet's mean
   sphere — not against the ground. A valley floor 500 m *below* the average radius therefore returns
   a **higher** wind than the ridge above it, which is backwards.
5. **The altitude falloff is linear, not exponential**, and reaches exactly zero at
   `AtmosphereAltitude`.
6. **It is a rating, not a wind.** `MaxWindSpeed` is the figure wind turbines are balanced against.
   On an earthlike world it is 80 m/s, and `Atmosphere.Density` is 1, so sea level returns 80 m/s —
   290 km/h, a strong hurricane, everywhere, permanently.
7. **The one thing it is good for** is being a per-planet scale. It is the only figure in the engine
   that says "this planet is windier than that one", it is authored per planet, and modded planets
   set it. It costs one subtraction and a clamp.

**Verdict: unusable as a wind, worth keeping as a ceiling.** That is exactly how this mod uses it,
and it was already the conclusion the field was built on — using it directly put every parked ship
over the friction threshold and ran convection at nearly twice its still-air rate planet-wide. What
is new here is that the conclusion is now checked against the code rather than inferred from
behaviour, and that fact 4 in particular — the terrain inversion — was not previously known.

## 2. What real wind does

Four mechanisms, each with a standard model behind it.

### The logarithmic profile

In the atmospheric surface layer, wind speed rises logarithmically with height:

```
u(z) = (u* / κ) · ln(z / z₀)
```

where `u*` is the friction velocity, `κ ≈ 0.4` is the von Kármán constant, and **z₀ is the roughness
length** — the height at which the wind extrapolates to zero, roughly a tenth of the height of the
obstacles covering the ground. Open water is 0.0002 m, grassland 0.03 m, forest or a built-up area
around 0.5 m.

Expressed against a reference height, the constants cancel:

```
f(z) = ln((z + z₀)/z₀) / ln((z_ref + z₀)/z₀)
```

Ten metres is the reference, because that is the height the world's weather stations measure at.
The power law `u ∝ z^α` with `α = 1/7` is the common shortcut, and it is the wrong one here: the 1/7
exponent is only reasonable over open ground, and gives poor answers exactly where terrain and cover
matter, which is the case this model is being built for.

The profile holds through the surface layer and no further. Above the boundary layer the wind is set
by the pressure field rather than by friction with the ground, so the curve flattens at a **gradient
height**. Combined with the engine's density falloff, the full shape is a rise, a plateau, and a
fall — which is the "stronger for a time" behaviour asked for, arrived at from the physics rather
than fitted to the request.

### The daily cycle, which runs two ways at once

This is the part that surprises people, and it is the strongest argument for modelling the day-night
cycle at all.

By day the sun heats the ground, convection mixes the boundary layer, and momentum from aloft is
dragged down to the surface. **Surface wind peaks in the afternoon.** After sunset the ground cools,
the mixing stops, and the air above **decouples from the surface friction that was holding it back**.
It accelerates — often past the geostrophic speed — into a **nocturnal low-level jet**, typically
between 50 and 1000 m up, peaking in the pre-dawn hours, while the surface below goes calm. This is
the Blackadar mechanism; the Holton mechanism adds a contribution from sloping terrain.

So surface wind and wind aloft are **anticorrelated over the day**, and between them is a crossover
height where the daily variation vanishes. A player who lands at dusk into still air and then climbs
to two hundred metres and meets a gale is seeing the documented behaviour of a real atmosphere.

The phase matters: the peak is not noon but mid-afternoon, because it follows the *ground* being
warm, not the sun being high. This model gets that free by driving the cycle through the same
first-order lag the ambient temperature already uses — the windiest part of the afternoon then lines
up with the warmest part by construction, and no clock, sun-rotation interval, or day-length setting
is needed.

### Slope winds, which the ground makes rather than merely shapes

Everything below is **mechanical** — what terrain does to a wind that was already blowing. Slope
winds are **thermal**: the ground creates them, out of sunlight and gravity, and they blow on a day
when nothing else does.

- **Anabatic, by day.** Sunlight heats a slope; the air against it warms, becomes buoyant, and rises
  *along the ground* rather than straight up — so the flow runs toward the summit. **3–5 m/s**,
  hundreds of metres deep, starting after sunrise and strongest in the afternoon.
- **Katabatic, by night.** The slope radiates its heat to the sky; the air against it cools, grows
  dense, and drains downhill to pool in the valley. **3–8 m/s** — up to 50 over Antarctic ice — and
  **far shallower, 10–100 m**, roughly a twentieth of the drop it has fallen.

The condition that matters most for a game: **both are weak-wind phenomena.** They form under calm,
clear, high-pressure conditions and a real synoptic wind simply overruns them.

### Terrain: speed-up, shelter, channelling

- **Speed-up.** Air driven over a rise is squeezed between the hill and the flow above and has to
  accelerate. Linearised flow theory — Jackson and Hunt, the basis of WAsP and every wind atlas since
  — gives a fractional speed-up of roughly `2H/L` for a hill of height `H` and half-length `L`. UK
  summits commonly run **two to three times** the wind of the valley below. Wind loading codes cap
  what they will credit to topography (ASCE caps its `K₁` at 0.50 for ridges and escarpments, 0.29
  for hills) precisely because the linear theory stops being true on the steep ground that produces
  the largest numbers.
- **Sheltering.** Behind a ridge is a wind shadow. The natural measure is the greatest upward angle
  from the site to the terrain upwind of it — the more of the upwind sky is blocked, the less wind
  arrives. This is the `Sx` index used in snow science.
- **Channelling.** Wind is channelled along valleys and through gaps, and this **modifies direction
  substantially, almost always into the along-valley direction**. Where a valley narrows, the flow
  accelerates through the constriction — the Venturi effect. Of the three, steering is the largest
  and the most visible: wind that follows the ground reads as weather, and wind that ignores it reads
  as a texture laid over a landscape.

## 3. What this mod computes

```
speed     = ceiling(planet, altitude)          // the engine's figure, as a scale only
          × band(latitude, weather, place)     // WindField — circulation and weather
          × profile(height above ground)       // WindProfile — the logarithmic rise
          × diurnal(heating, height)           // WindProfile — the daily cycle, reversing with height
          × speedUp(relief)                    // WindTerrain — exposure
          × shelter(upwind horizon)            // WindTerrain — wind shadow

          + slope(fall line, hour, height)     // WindSlope — added as a velocity, not a factor

direction = band(latitude)                     // WindField — the circulation
            steered by channel(valley axis)    // WindTerrain
            turned by the slope wind it is added to
```

Each factor is 1 when it has nothing to say, so a reading can be attributed by reading the columns
rather than by arguing about them.

### Where the terrain comes from

A ring of **eight compass bearings at two radii — sixteen height lookups** — each giving the ground's
height relative to the site's own. That is enough for all three effects:

- the **mean of the ring** is exposure: the site standing proud of the land around it, as a slope;
- the **upwind arc** is shelter, interpolated between the two samples straddling the upwind bearing,
  taking the steeper of the two radii, so a near obstruction shelters more than a far one of the
  same height;
- the **second harmonic of the ring** is the valley axis: fit `h(θ) ≈ mean + A·cos(2(θ − φ))`, which
  is the shape a valley or a ridge makes when you walk a circle round a point in one. A harmonic
  rather than picking the lowest of four opposite pairs, because pairs quantise the answer to 45° and
  a wind that snaps between eight directions as you walk looks like a bug.

The ring is re-read only when a grid has moved a quarter of the sampling radius, so sixteen lookups
buy a few hundred metres of travel.

**The fall line is the ring's *first* harmonic**, where the valley axis is its second, and the two
are genuinely different shapes: a symmetric valley is high on both sides and so has no first harmonic
at all — correctly, since a valley floor has no single downhill direction — while a hillside has no
second. Fitting both means a site on a valley wall gets an along-valley axis *and* a fall line, which
is what it really has. It costs eight multiply-adds over heights already in memory.

**The channelling rule is "turn toward the lowest ground", and it deliberately gives two different
answers.** In a valley the lowest ground is along the floor, so the wind runs along it. On a ridge
crest the lowest ground is down either side, so the wind is left crossing the ridge — which is what
air going over a ridge does. A ridge is not a valley upside down as far as steering goes, and the
first version of the test for this assumed it was.

### Where terrain stops mattering

All three terrain effects fade linearly to nothing at the gradient height. They are surface-layer
phenomena, and steering a ship two kilometres up along a valley it is nowhere near would be worse
than not modelling terrain at all.

## 4. Reading it: the telemetry

The environment CSV carries the wind decomposed, one row per sample per grid:

| Column | What it says |
| --- | --- |
| `wind_ceiling` | The engine's figure — `MaxWindSpeed × airDensity`. The scale everything else works against. |
| `wind_band_share` | Share of that ceiling the circulation band and the weather were blowing, 0..1. |
| `wind_agl_m` | Metres above the **ground**, which is what the profile is a function of — as against `altitude_surface`, which is the grid's own height. |
| `wind_profile` | Vertical profile × time of day, as a multiple of the reference-height wind. |
| `wind_heating` | Lagged share of the day's heating, 0..1: 0 at the coldest hour, 1 at peak afternoon. This is the phase of the daily cycle. |
| `wind_speedup` | Terrain exposure. Above 1 on a rise, below 1 in a hollow. |
| `wind_shelter` | Terrain sheltering. 1 in the open, less behind an obstruction. |
| `wind_channel_deg` | Degrees the terrain turned the wind away from the band's own bearing. |
| `wind_speed` | The relative wind the solver actually used, m/s. |
| `wind_bearing_deg` | Where it was going, degrees east of the planet's north. |

Alongside the columns already there — `latitude_deg`, `sun_elevation_deg`, `altitude_surface`,
`altitude_sealevel`, `air_density`, `weather`, `weather_intensity`, `surface_material`,
`game_temperature` — that is enough to check every claim in section 3 against a real world.

**What a validation world should collect.** A parked grid samples its own position continuously, so
the cheapest useful test is several stationary grids left running for a few in-game days:

- a valley floor and the ridge directly above it, to separate speed-up from shelter and to see
  whether the channelling turns the valley grid along the valley;
- a site at each of several latitudes, which is the gap the climate data still has — the existing
  three sites span 7° to 41° and the pole drop is the least evidenced term in the model;
- one grid parked and one hovering at 200 m at the same place, which is the only way to see the
  crossover and the nocturnal jet directly: `wind_speed` on the two should diverge overnight and
  converge by afternoon;
- a full day at each, since `wind_heating` is meaningless without one.

## 5. Measuring it: three sources, one schema

The model is a pure function of position, weather and the hour, which means it can be measured three
ways — and all three now write the **same columns**, so they can be put side by side without
translating anything.

| Source | What it is | Reach |
| --- | --- | --- |
| **Grid telemetry** | Every grid samples its own position. `Thermodynamics_Environment_*.csv`. | Wherever somebody parked. |
| **Planet probes** | 72 fixed points on the planet — every latitude from −80° to +80° including the equator, eight longitudes each — at five heights, sampled on an interval whether or not anything is standing there — **wind and climate together**, since they share every input. `Thermodynamics_PlanetProbes_*.csv`. Set `TelemetryPlanetProbes` to a step interval; needs `EnableTelemetry`. | The whole planet, over a real day. |
| **The offline model** | `dotnet run --project tests/Thermodynamics.Sim -- wind`. A synthetic planet with the engine's own wind function reproduced from its decompiled source, a heightmap with features from forty kilometres down to four hundred metres, and a full day at 73 times of day. Add `--csv out/`, `--flat` or `--weather 1`. | Everything, in about a second. |

The probes and the offline model both call `WindSolver.Solve` — the same function the grids do — so
none of them can drift from the model without the model itself changing. The offline planet models
only *the world*: the engine's `GetWindSpeed`, and ground.

**The first field run showed why the other two were needed.** It collected 3,465 in-atmosphere
samples across 112 grids and could answer almost nothing:

- **2.9 minutes long, at dusk.** `wind_heating` was 0 for 95% of samples and peaked at 0.286. The
  daily cycle — the whole reason the model has an hour in it — was never exercised. What it *did*
  confirm is that the lag is right: heating fell 0.286 → 0.003 over 180 s, against 0.005 predicted
  by `e^(−180/45)`.
- **Latitudes 5.2° to 29.8°.** No equator, no poles, no band edges.
- **99.3% of samples in one weather.** The clear-air baseline is 23 samples.
- **Nearly flat ground.** Sheltering never took more than 7% where the model allows 75%;
  channelling turned the wind less than 2° for 57% of samples. Exactly one grid, parked on a
  mountain at 5,987 m, saw the terrain model do anything real: speed-up 1.370 and 41° of
  channelling. The model was correct — that ground *is* gentle — and the run therefore said nothing
  about whether the terrain model works.

A single offline run covers all four gaps, and a probe sweep covers them in the real world.

## 6. World size, and why every borrowed constant lands oddly

Space Engineers planets are not small Earths. They are a different kind of object, and the model's
constants all came from a literature written about the other kind.

Everything the engine builds is a fraction of the radius, read out of `Sandbox.Game.dll`:

```
maxHillHeight      = HillParams.Max × radius
minHillHeight      = HillParams.Min × radius
OuterRadius        = radius + maxHillHeight
InnerRadius        = radius + minHillHeight
AtmosphereAltitude = maxHillHeight × Atmosphere.LimitAltitude
```

| World | radius | hills | atmosphere | 30° band | horizon @2 m |
| --- | ---: | ---: | ---: | ---: | ---: |
| EarthLike / Alien / Mars | 60 km | −600 … 7,200 m | 14,400 m | 31 km | 490 m |
| Triton | 40 km | −2,000 … 8,000 m | **3,760 m** | 21 km | 400 m |
| Pertam | 30 km | −750 … 750 m | 1,500 m | 16 km | 346 m |
| Europa / Titan / Moon | 9.5 km | ±285 m | 285 m (Moon: none) | 5 km | 195 m |
| *Earth, for scale* | *6,371 km* | *−11,000 … 8,849 m* | *~100,000 m* | *3,336 km* | *5,048 m* |

Five consequences, none of them obvious before the numbers were on the page:

1. **SE terrain is about eighty times steeper than real terrain** relative to its world — hills at 12%
   of the radius against Earth's 0.14%. Every terrain constant borrowed from wind engineering is
   therefore operating far outside the range it was fitted in. Measured: **speed-up, shelter and
   channelling all saturate their caps on ordinary SE ground.** The caps are not a safety net here,
   they *are* the model.
2. **A circulation band is 31 km wide**, not 3,300. The general circulation is a local feature you can
   fly across in a minute.
3. **The horizon from head height is 490 m** on the largest world in the game. The local wind map
   reaches five kilometres — ten times past the horizon.
4. **Triton's peaks stand in vacuum.** 20% of its radius in mountain against `LimitAltitude` 0.47, so
   the air runs out less than half way up its highest ground. Its mean modelled wind is 1.5 m/s
   against an earthlike world's 7.1, because most of its surface is not in air.
5. **On a small moon the boundary layer is taller than the whole atmosphere.** The shipped gradient
   height is 600 m; Titan's air is 285 m deep. The vertical profile gets asked about heights in
   vacuum, and the only thing keeping the answer sane is the ceiling reaching zero first.

### The scenario matrix

`dotnet run --project tests/Thermodynamics.Sim -- wind scenarios` runs the model over **28 scenarios**
— every shipped world at its usual size, four sizes plus two modded extremes, ten settings pushed to
their ends, and four degenerate worlds (airless, no wind rating, flat, and a day shorter than the lag
that follows it) — at fifteen latitudes chosen to hit the equator, the band middles and the band
edges exactly, and twelve heights from the ground to 20 km. About three million samples.

`WindScenarioTests` runs the same matrix at a coarse day as part of the suite. What it holds:

- **not one NaN, infinity or negative wind anywhere** across the whole matrix;
- every terrain factor inside its declared cap on every world;
- airless worlds and zero-rated worlds produce exactly zero, and nothing divides by it;
- a flat world leaves every terrain factor exactly 1, and so does `WindTerrainInfluence = 0`;
- the engine's derivations reproduce exactly, including Triton's peaks-above-air;
- **the storm case blowing out, pinned as a defect** — see below.

## 7. What is still guesswork

Stated plainly, because none of it came from evidence and all of it is now visible on the map:

- **The circulation bands themselves.** Three bands per hemisphere, a bearing that rotates rather
  than components that are mixed, an eighth of the ceiling in fair weather and half in a storm, a
  900 m variation scale. All chosen to be plausible and cheap.
- **The equator reverses.** The meridional component is poleward everywhere it is non-zero and at
  full strength at 0°, 30° and 60°, so air diverges from the equator — the opposite of Earth's
  converging trades — and the vector flips end for end across the equator itself. Nothing tests the
  meridional component, and the continuity test that exists measures only the east component. This
  is the largest known fault in the pattern.
- **Every constant in `WindTerrain`.** The caps (0.6 speed-up, 0.45 slow-down, 0.75 shelter), the
  40° full-shelter angle and the one-in-three full-channelling slope are all reasoned from the
  literature's ranges rather than fitted to anything.
- **The diurnal amplitude and crossover.** 0.35 and 80 m are plausible middles of wide real ranges;
  measured low-level jets sit anywhere from 50 m to 1000 m.
- **No slope winds.** Katabatic drainage down a valley at night and anabatic flow up it by day are
  real, strong, and would sit naturally on top of the terrain sampling that is now in place — the
  ring already knows which way the ground falls. Not built.
- **No sea breeze, no gap-wind acceleration, no lee turbulence.**
- **Roughness is one number for a whole world.** The ground material under a grid is already
  classified for the temperature model, and roughness is exactly the kind of thing it should vary
  with — snow smooth, forest rough. Not wired.

Nothing in this file has been measured in game. The telemetry in section 4 exists so that the next
sentence in this section can be a number.
