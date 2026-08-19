# Definition reference

Thermal Dynamics reads none of its tuning values from its own C# constants. Everything lives in
`<ModExtensions>` groups attached to game definitions, read at runtime through Draygo's
**Definition Extensions** mod. That means any mod — including one that does not depend on this
one — can declare thermal properties for its own blocks, and this mod will pick them up.

The three files that ship with the mod are listed in
[Data/definitionextensions.txt](../Data/definitionextensions.txt), which is how Definition
Extensions discovers them:

```
Cubes.xml
Planets.xml
Loops.xml
```

## Group: `ThermalBlockProperties`

Read by `ThermalCellDefinition.GetDefinition`
([Definitions/ThermalCellDefinition.cs](../Data/Scripts/Thermodynamics/Definitions/ThermalCellDefinition.cs)).

| Property | Type | Clamp | Meaning |
| --- | --- | --- | --- |
| `ExcludeFromSimulation` | Bool | — | `true` excludes the block from the simulation entirely: no cell is created, it conducts nothing and blocks nothing. |
| `Conductivity` | Decimal | `0 … 1` | Scales heat transfer to neighbours. `1` is a perfect conductor for this model, not a W/(m·K) value. |
| `SpecificHeat` | Decimal | `≥ 0` | Heat capacity in **real J/(kg·K)** — look the material up. Steel 450, copper 385, aluminium 900, graphite 710, water 4184. Higher = slower to heat and to cool. See [the note below](#specific-heat-is-real-and-the-clock-is-not). |
| `Emissivity` | Decimal | `≥ 0` | Fraction of blackbody radiation emitted, and equally the fraction of incident solar energy absorbed. Physically `0 … 1`. |
| `ExposedSurfaceMultiplier` | Decimal | `≥ 0` | Multiplies the geometric face area. Use `> 1` for finned or folded surfaces (the radiator uses `1.25`). It scales **every** external path, so a large value also multiplies solar gain and reentry friction — and it buys much less than it looks: see [balance.md](balance.md). |
| `ProducerWasteEnergy` | Decimal | `≥ 0` | Fraction of *generated* power converted to heat. |
| `ConsumerWasteEnergy` | Decimal | `≥ 0` | Fraction of *consumed* power converted to heat. |
| `CriticalTemperature` | Decimal | `≥ 0` | Kelvin above which the block takes damage. |
| `OverheatDamagePerKelvin` | Decimal | `≥ 0` | Damage per Kelvin of overshoot, per second. |

### Retired property names

These were renamed because they said the wrong thing. **Both spellings are read**, current name
first, so a definition written before the rename still means what it said — Definition Extensions
matches on the string, and dropping the old name would silently revert third-party definitions to
the shipped defaults with no error and no log line.

| Retired | Current | Why |
| --- | --- | --- |
| `CriticalTemperatureScaler` | `OverheatDamagePerKelvin` | Read as "multiply the critical temperature", which is not remotely what it does |
| `SurfaceAreaScaler` | `ExposedSurfaceMultiplier` | Said what it multiplied but not that the scope is the *outside* faces only |
| `IgnoreThermals` | `ExcludeFromSimulation` | Read as "ignores heat damage" rather than "is not simulated at all" |
| `PlateSurfaceAreaScaler` | `SinkContactMultiplier` | Nothing else in the mod calls a sink face a plate |
| `PipeSurfaceAreaScaler` | `PipeContactMultiplier` | Pairs with the above; the two are siblings and now look it |
| `MassPerPipe` | `CoolantMassPerPipe` | Mass of what, in a file full of blocks |

New definitions should use the current names. The old ones are not planned for removal.

### `Conductivity` is not in W/(m·K)

Worth stating plainly, because the name says otherwise: the value is clamped to **0…1** and
multiplied by a 200 W/(m·K) reference at the point of use. A tabulated figure typed in here —
steel's 50, copper's 400 — clamps to 1 and quietly means "the best there is". `1` is a very good
conductor, `0.6` is the default for most blocks, `0` conducts nothing.

### Specific heat is real, and the clock is not

`SpecificHeat` is in the units you would find in a materials table, so a definition reads as a
description of what the block is made of:

| Material | J/(kg·K) | Used by |
| --- | --- | --- |
| Copper | 385 | coolant pipes |
| Steel | 450 | armour, most blocks, pumps, thrusters |
| Graphite-shielded steel | 600 | reactors |
| Aluminium | 900 | radiators |
| Water-glycol | 3400 | the coolant fluid itself |

A real ship at real heat capacity is also a *slow* ship: a hot hull takes hours to cool, which
is accurate and not much of a game. The pace comes from one global setting,
`HeatTimeScale` ([configuration.md](configuration.md)), which divides every heat capacity —
blocks and coolant alike — by the same number. Dividing capacity is exactly running thermal time
faster: every mechanism is a rate over that capacity, so **equilibrium temperatures, the balance
between conduction and radiation, and the ratios between block types are all unchanged**. Only
the clock moves.

That separation is the point. Tune *what a block is* here, in real units, and tune *how fast the
game feels* there, in one place.

The shipped default is 225, which is what makes steel's 450 J/(kg·K) behave the way the flat
game value of `2` used to.

### Lookup and fallback

For a block with definition id `TypeId/SubtypeId`, `GetDefinition` resolves in this order:

1. `TypeId/SubtypeId` — an exact per-block entry.
2. `TypeId/DefaultThermodynamics` — a per-type default (this is how *all* thrusters and *all*
   reactors get their properties without listing every subtype).
3. `EnvironmentDefinition/DefaultThermodynamics` — the global fallback.

The probe for steps 1 and 2 is whether the definition id is indexed **and** exposes
`ExcludeFromSimulation`, so a partial group without `ExcludeFromSimulation` falls through to the next level
rather than being read with zeros.

### Shipped values ([Data/Cubes.xml](../Data/Cubes.xml))

| Definition | Cond. | Spec. heat | Emiss. | Area | Prod. waste | Cons. waste | Critical | Scaler |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `EnvironmentDefinition/DefaultThermodynamics` (global) | 0.60 | 2 | 0.125 | 1 | 0.05 | 0.05 | 900 | 1 |
| `Thrust/DefaultThermodynamics` | 1 | 3 | 0.15 | 1 | 0 | 0.25 | 1050 | 0.25 |
| `Reactor/DefaultThermodynamics` | 1 | 6 | 0.25 | 1 | 0 | 0.25 | 1200 | 0.25 |
| Coolant pipes, pumps (LG + SG) | 1 | 2 | 0.125 | 1 | 0 | 0 | 1000 | 1 |
| Heat pumps (LG + SG) | 1 | 2 | 0.125 | 1 | 0.05 | 0.05 | 1000 | 1 |
| Radiators (LG + SG) | 1 | **1** | **0.35** | **1.25** | 0 | 0 | 1000 | 1 |

The radiator is the only block tuned to shed heat: low thermal mass so it responds fast, high
emissivity, and 25 % extra surface area.

> Note that the reactor entry uses `ConsumerWasteEnergy`, not `ProducerWasteEnergy` — reactors
> report through a resource *sink* for their fuel, so waste heat is driven by consumption.

## Group: `ThermalPlanetProperties`

Read by `PlanetDefinition.GetDefinition`
([Definitions/PlanetDefinition.cs](../Data/Scripts/Thermodynamics/Definitions/PlanetDefinition.cs)),
keyed on a `PlanetGeneratorDefinition` id, falling back to
`PlanetGeneratorDefinition/DefaultThermodynamics`.

| Property | Default | Meaning |
| --- | --- | --- |
| `NightTemperature` | 283.15 K | Ambient with the sun on the far side. **Equatorial, at sea level.** |
| `DayTemperature` | 294.261 K | Ambient with the sun directly overhead. **Equatorial, at sea level.** |
| `PoleTemperatureDrop` | 40 K | How much colder a pole is than the equator. Earth's is about 40. 0 gives one climate for a whole world. |
| `AmbientLagSeconds` | 45 s | How long the air takes to answer the sun. Without it the day's peak is exactly noon. Worth raising on a world with a long day and lowering on a short one. |
| `AmbientLapseRate` | 4 K/km | How much colder a kilometre above sea level is. Earth's is 6.5; lower here because the ground table already makes mountains snowy. 0 switches altitude off. |
| `UndergroundTemperature` | 280 K | Ambient deep enough underground that the surface's day no longer reaches; also forces solar occlusion. |
| `UndergroundDampingDepth` | 20 m | Metres of rock that blunt the surface's day-night swing to nothing. Above it a buried block still feels part of the day; below it, none. |
| `CoreTemperature` | 3000 K | Temperature at the planet's centre. The rock warms toward it below `SealevelDeadzone`. |
| `SealevelDeadzone` | 2000 m | Depth **below sea level** at which core heating starts. Measured from sea level, so a tunnel into a mountain stays cold however deep it goes. Lower it to make reachable mining depths hot. |
| `SolarDecay` | 0.5 | Fraction of solar energy lost in a full-density atmosphere. |
| `ConvectionCoefficient` | 50 | W/(m²·K) base heat transfer into the air. |

The last five rows carry the model's own defaults rather than zero when the definition omits them,
unlike the rows above. They were added after the planet definitions were written, and zero is a
real setting for every one of them — no latitude, no lag, no lapse, no damping, no core — so a
planet file predating them would otherwise silently ask for all five to be switched off.

Weather is **not** in this group. The mod reads the weather's name from the game and looks it up in
[WeatherResponse](../Data/Scripts/Thermodynamics/Core/Definitions/WeatherResponse.cs), whose figures
are derived from Keen's own `WeatherEffects.sbc`, so a mod adding a weather called `RainHeavier`
gets rain behaviour without annotating anything. See
[thermal-model.md](thermal-model.md#weather).

Per-planet values are set by adding a definition with that planet's `SubtypeId`. The shipped
[Data/Planets.xml](../Data/Planets.xml) only defines the fallback, so **every** planet currently
uses Earthlike numbers.

## Group: `ThermalLoopProperties`

Read by `ThermalLoopDefintion.GetDefinition`
([Definitions/ThermalLoopDefinition.cs](../Data/Scripts/Thermodynamics/Definitions/ThermalLoopDefinition.cs)).
There is currently one loop definition and every loop uses it:
`EnvironmentDefinition/DefaultThermodynamicsLoop` in [Data/Loops.xml](../Data/Loops.xml).

| Property | Default | Clamp | Meaning |
| --- | --- | --- | --- |
| `Mass` | 500 | `≥ 1` | Coolant mass for the whole loop, kg. |
| `Conductivity` | 1 | `0 … 1` | Transfer scaling for both pipe and plate exchanges. |
| `SpecificHeat` | 3400 | `≥ 0` | Coolant heat capacity in real J/(kg·K). Water-glycol is about 3400, which is why a loop carries so much more heat than the steel around it. Scaled by `HeatTimeScale` exactly as a block is. |
| `PipeContactMultiplier` | 1 | `≥ 0` | Contact area between fluid and the pipe block it runs through. |
| `SinkContactMultiplier` | 1 | `≥ 0` | Contact area between fluid and a block pressed against a sink face. |

## Adding thermal properties for another mod's blocks

Create a definition file listing the block ids you want to describe, register it in your own
`definitionextensions.txt`, and Definition Extensions will index it — no dependency on Thermal
Dynamics' assembly required:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Definitions xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
             xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <CubeBlocks>
    <Definition>
      <Id>
        <TypeId>Refinery</TypeId>
        <SubtypeId>MyMod_BigSmelter</SubtypeId>
      </Id>
      <ModExtensions>
        <Group Name="ThermalBlockProperties">
          <Bool    Name="ExcludeFromSimulation"           Value="false" />
          <Decimal Name="Conductivity"             Value="0.8"   />
          <Decimal Name="SpecificHeat"             Value="500"   />
          <Decimal Name="Emissivity"               Value="0.2"   />
          <Decimal Name="ExposedSurfaceMultiplier"        Value="1"     />
          <Decimal Name="ProducerWasteEnergy"      Value="0"     />
          <Decimal Name="ConsumerWasteEnergy"      Value="0.35"  />
          <Decimal Name="CriticalTemperature"      Value="1100"  />
          <Decimal Name="OverheatDamagePerKelvin" Value="0.5"  />
        </Group>
      </ModExtensions>
    </Definition>
  </CubeBlocks>
</Definitions>
```

Tuning guidance:

* Always declare **every** property in the group. Unset properties default to `0`, and a
  `SpecificHeat` of 0 produces a divide-by-zero in the cell constants.
* `SpecificHeat` × block mass is the real knob for thermal inertia. Heavy blocks are already
  slow; do not double-count by also raising specific heat.
* Set `ExcludeFromSimulation` to `true` for decorative, zero-mass or projector-only blocks.
* Waste-energy fractions above ~0.3 make a block a serious heat source; the vanilla default is
  0.05.
