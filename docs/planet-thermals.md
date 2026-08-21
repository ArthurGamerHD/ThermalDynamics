# Planet thermals

What the game says about how hot a world is, what it leaves out, and where every figure in
[Planets.xml](../Data/Planets.xml) comes from.

## 1. What Space Engineers actually says about temperature

Decompiled from `Sandbox.Game.dll`. There are two things, and neither is a temperature.

### `DefaultSurfaceTemperature` — a five-level enum

```csharp
public static float LevelToTemperature(MyTemperatureLevel level) => level switch
{
    MyTemperatureLevel.ExtremeFreeze => 0f,
    MyTemperatureLevel.Freeze        => 0.25f,
    MyTemperatureLevel.Cozy          => 0.5f,
    MyTemperatureLevel.Hot           => 0.75f,
    MyTemperatureLevel.ExtremeHot    => 1f,
    _                                => 0.5f,
};
```

Five levels on a comfort scale, no kelvin anywhere. **This is the entire statement the game makes
about how hot a planet is** — and four of the eight shipped worlds do not author it, taking the
`Cozy` default by omission.

### `GetTemperatureInPoint` — a comfort figure, and it is worse than it looks

```csharp
float oxygenInPoint = MyOxygenProviderSystem.GetOxygenInPoint(worldPoint);
if (oxygenInPoint < 0.01f) return 0f;
...
return MathHelper.Lerp(0f, value2, oxygenInPoint);
```

It returns **0..1, not kelvin**, and it **returns zero wherever there is no oxygen**. Mars, Europa
and the Moon are all unbreathable, so `GetTemperatureInPoint` is flatly zero on three of the eight
worlds and near it on a fourth. The `game_temperature` column in this mod's environment telemetry is
this figure: useful as a cross-check on breathable worlds and meaningless everywhere else. Worth
knowing before anybody tries to validate the climate model against it.

### What is *not* there

**Orbital distance.** One sun, one intensity, and no field anywhere saying how far a planet sits from
it. A world cannot be cold *because* it is far away — the level is the whole answer. That single
absence is why section 3 has judgement calls in it.

## 2. What the definitions do supply

Every figure read from `PlanetGeneratorDefinitions.sbc`, with the object builder's defaults filled in
where a field is not authored (`Density 1`, `OxygenDensity 1`, `LimitAltitude 2`,
`SolarRadiationProtectionFactor 1`, `DefaultSurfaceTemperature Cozy`):

| World | level | gravity | air | breathable | solar protection |
| --- | --- | ---: | ---: | --- | ---: |
| EarthLike | *Cozy (default)* | 1.00 g | 1.00 | yes | 1.80 |
| Alien | *Cozy (default)* | 1.10 g | 1.20 | yes | 1.80 |
| Mars | *Cozy (default)* | 0.90 g | 1.00 | no | 0.20 |
| Pertam | **Hot** | 1.20 g | 1.00 | yes | 0.75 |
| Triton | **ExtremeFreeze** | 1.00 g | 1.00 | yes | 1.80 |
| Europa | **ExtremeFreeze** | 0.25 g | 1.00 | no | 0.00 |
| Titan | *Cozy (default)* | 0.25 g | 1.00 | yes | 1.80 |
| Moon | *Cozy (default)* | 0.25 g | **none** | – | 1.00 |

Two things jump out. **Every world with air has density exactly 1** — so anything derived from air
density gives the same answer for seven of eight, and only the Moon differs. And **SE's Triton is a
breathable, full-density, 1 g world** with nothing in common with the real Triton but its name.

## 3. The derivation

[`PlanetThermalDerivation`](../Data/Scripts/Thermodynamics/Core/Definitions/PlanetThermalDerivation.cs)
turns those inputs into the mod's figures. It is pure and tested, and it serves twice: it generates
the shipped entries offline, and it is the sensible fallback for a **modded** planet nobody has
authored an entry for — which previously got an earthlike climate whatever it was.

| Figure | How |
| --- | --- |
| **Mean temperature** | Five authored anchors, one per level: **100 K** (Titan 94, Europa 102), **215 K** (Mars), **288 K** (Earth), **325 K** (a hot desert), **450 K** (between Mercury's day side and Venus). Not evenly spaced — spread evenly, `Cozy` would land at 275 K, below freezing. |
| **Day–night swing** | From air. 11 K with a full atmosphere (Earth's equatorial range), 220 K with none (the Moon runs 100 K to 390 K). Falls off faster than linearly, because the first tenth of an atmosphere does most of the damping. |
| **Lapse rate** | `Γ = g/c_p`, times two thirds for the environmental rate. **The one true derivation here.** An earthlike world comes out at 6.44 K/km against Earth's measured 6.5. `c_p` is 1005 for breathable air and 850 for unbreathable, taken as CO₂. |
| **Pole drop** | From air, which is what carries heat polewards: 40 K with a full atmosphere, 120 K with none. |
| **Ambient lag** | Scales with air — a bare rock answers the sun almost at once. 45 s at full density, floored at 5. |
| **Solar decay** | The engine's `SolarRadiationProtectionFactor`, scaled so 1.8 → 0.30, which is about what Earth's atmosphere really absorbs and scatters. Mars's 0.2 → 0.03. |
| **Convection** | 50 W/(m²·K) in proportion to air density; zero on an airless world. |
| **Interior** | Not derived. Nothing in a planet generator definition says anything about a planet's inside, so damping depth, core temperature and the sea-level deadzone carry the defaults through. |

## 4. Where the shipped entries depart from it, and why

**The rule: an authored level is followed; an unauthored one is not treated as intent.**

Where a definition authors `DefaultSurfaceTemperature`, the game has made a decision and it stands,
whatever the real body does. SE's Triton is breathable with 1 g and full air; the definition says
`ExtremeFreeze`, so it is 100 K and no override applies.

Where a definition is **silent** and the world is named after a real place, silence is an omission
rather than a statement. Reading `Cozy` out of it would put Titan at 288 K and have players landing
on an ice moon in shirtsleeves. Three worlds are anchored to their real analogues instead:

| World | derived | shipped | why |
| --- | --- | --- | --- |
| **Mars** | 282–294 K | **185–245 K** | Unauthored. Anchored to Mars's measured 215 K mean and its ~60 K daily range, which its thin real air cannot damp. |
| **Titan** | 282–294 K | **92–96 K** | Unauthored. Anchored to Titan's measured 94 K, with the very small daily range a thick cold nitrogen atmosphere gives it. |
| **Moon** | 178–398 K | **100–390 K** | Unauthored. The derivation already gets the enormous airless swing right; this pins the mean to the Moon's measured extremes rather than the `Cozy` default. |

Each departure is written into the generated file beside the entry it affects, under
`DEPARTS FROM THE DERIVATION`, and `PlanetThermalTests` fails if one loses its reason.

## 5. The file is generated

`Data/Planets.xml` is produced by `PlanetLab.Xml()`, not typed:

```
dotnet run --project tests/Thermodynamics.Sim -- planets                        # the table
dotnet run --project tests/Thermodynamics.Sim -- planets --write Data/Planets.xml
```

`TheShippedPlanetsFileIsWhatThisCodeGenerates` fails if the file on disk drifts from what the code
produces. That is deliberate: the reasoning behind every figure lives in code beside a test, and a
number that cannot be regenerated is a number nobody can check. Other tests hold that every entry
carries every property the mod reads (a missing one silently takes a reader default), that the
fallback entry is byte-for-byte the earthlike climate the mod always shipped, and that no combination
of definition inputs — including ones no shipped world uses — produces a negative, NaN or inverted
climate.

## 6. What happens when the file does not reach the mod

The entries are read through Draygo's BlockExtensions API, which is a second mod answering on a
message. Two things follow, and a field dump found both the hard way — an earthlike world reading
2.7 K of ambient at 0.93 air density, no convection, no solar decay, and 232,000 points of heat
damage behind it.

**A definition overrides only what it carried.** Every value the lookup did not answer for used to
arrive as zero and be written over the model's own default, so a planet with no thermal group — or
a pack authoring three values of eleven — became a vacuum. `PlanetProperties.Merge` takes only the
fields the read actually supplied; the rest keep the earthlike defaults on
`PlanetThermalProperties`.

**A lookup that is not up yet is not an answer.** The first grid to tick can ask before the other
mod has sent its handlers, which returns nothing for every field and was then cached for the
session. The lookup returns null until it is ready, and nothing caches a null.

The report names both: the Climate section prints the climate each planet is being simulated with
and which fields its definition supplied, and the World section lists the mods loaded — which is
where a missing BlockExtensions shows up.

That line found two more on its first outing (`[from definition: None]` on all five planets of a
world with the API loaded). **The file itself was illegal XML**: the header comment carried the
regen command's `--`, which no strict parser accepts, and Definition Extensions rejected the whole
file on every load of every world since the file was generated. **And the lookup key was wrong**:
the planet entity's `DefinitionId` is `MyObjectBuilder_Planet/(null)`, so the per-planet entries —
keyed `PlanetGeneratorDefinition/EarthLike` — could never have matched; the lookup now asks with
`Entity.Generator.Id`. `DefinitionFileTests` strict-parses everything `definitionextensions.txt`
names, so a file the importer would reject fails the suite instead of a session.

## 7. Reading it back: planet probes

`TelemetryPlanetProbes` sweeps **72 fixed points** — every latitude from −80° to +80° including the
equator, eight longitudes each — at five heights, on an interval, whether or not anything is standing
there. It records the wind and the climate together, since they share every input, and runs the same
`ClimateModel` calls a grid's ambient goes through: the day-night target at that latitude, cooled for
altitude, thinned by the air, then lagged.

`Thermodynamics_PlanetProbes_*.csv` carries `air_density`, `ambient_target_k`, `ambient_k` and
`ambient_c` beside the wind columns. The target and the lagged value sit side by side on purpose:
the difference between them *is* the lag, and it is the only way to see that the day's peak lands
after noon rather than at it.

## 8. What is still guesswork

- **Seven of eight worlds have identical air density**, so swing, pole drop, lag and convection are
  the same figure for all of them. The only real differentiation between shipped atmospheric worlds
  comes from gravity (lapse rate) and solar protection.
- **The five level anchors are authored.** 100/215/288/325/450 K are defensible against real bodies
  and are not fitted to anything.
- **`c_p` from breathability** is the crudest step here. Breathable → nitrogen and oxygen,
  unbreathable → carbon dioxide, and nothing in the game says otherwise.
- **The interior is untouched** — and the sea-level deadzone is still 2 km, deeper than SE's voxels
  reach, so the core gradient remains unreachable in ordinary play (backlog C5).
- **Nothing here has been measured in game.** Section 6 exists so the next sentence can be a number.
