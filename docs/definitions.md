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
| `IgnoreThermals` | Bool | — | `true` excludes the block from the simulation entirely: no cell is created, it conducts nothing and blocks nothing. |
| `Conductivity` | Decimal | `0 … 1` | Scales heat transfer to neighbours. `1` is a perfect conductor for this model, not a W/(m·K) value. |
| `SpecificHeat` | Decimal | `≥ 0` | Thermal mass per kg. Higher = slower to heat and to cool. Not J/(kg·K); it is a relative game unit. |
| `Emissivity` | Decimal | `≥ 0` | Fraction of blackbody radiation emitted, and equally the fraction of incident solar energy absorbed. Physically `0 … 1`. |
| `SurfaceAreaScaler` | Decimal | `≥ 0` | Multiplies the geometric face area. Use `> 1` for finned or folded surfaces (the radiator uses `1.25`). |
| `ProducerWasteEnergy` | Decimal | `≥ 0` | Fraction of *generated* power converted to heat. |
| `ConsumerWasteEnergy` | Decimal | `≥ 0` | Fraction of *consumed* power converted to heat. |
| `CriticalTemperature` | Decimal | `≥ 0` | Kelvin above which the block takes damage. |
| `CriticalTemperatureScaler` | Decimal | `≥ 0` | Damage per Kelvin of overshoot, per cell update. |

### Lookup and fallback

For a block with definition id `TypeId/SubtypeId`, `GetDefinition` resolves in this order:

1. `TypeId/SubtypeId` — an exact per-block entry.
2. `TypeId/DefaultThermodynamics` — a per-type default (this is how *all* thrusters and *all*
   reactors get their properties without listing every subtype).
3. `EnvironmentDefinition/DefaultThermodynamics` — the global fallback.

The probe for steps 1 and 2 is whether the definition id is indexed **and** exposes
`IgnoreThermals`, so a partial group without `IgnoreThermals` falls through to the next level
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
| `NightTemperature` | 283.15 K | Ambient with the sun on the far side. |
| `DayTemperature` | 294.261 K | Ambient with the sun directly overhead. |
| `UndergroundTemperature` | 280 K | Ambient below the surface; also forces solar occlusion. |
| `CoreTemperature` | 3000 K | **Not yet used.** Reserved for depth-based heating. |
| `SealevelDeadzone` | 2000 | **Not yet used.** Reserved for the depth at which core heating starts. |
| `SolarDecay` | 0.5 | Fraction of solar energy lost in a full-density atmosphere. |
| `ConvectionCoefficient` | 50 | W/(m²·K) base heat transfer into the air. |

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
| `SpecificHeat` | 5 | `≥ 0` | Coolant thermal mass per kg. |
| `PipeSurfaceAreaScaler` | 1 | `≥ 0` | Contact area between fluid and the pipe block it runs through. |
| `PlateSurfaceAreaScaler` | 1 | `≥ 0` | Contact area between fluid and a block pressed against a sink face. |

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
          <Bool    Name="IgnoreThermals"           Value="false" />
          <Decimal Name="Conductivity"             Value="0.8"   />
          <Decimal Name="SpecificHeat"             Value="4"     />
          <Decimal Name="Emissivity"               Value="0.2"   />
          <Decimal Name="SurfaceAreaScaler"        Value="1"     />
          <Decimal Name="ProducerWasteEnergy"      Value="0"     />
          <Decimal Name="ConsumerWasteEnergy"      Value="0.35"  />
          <Decimal Name="CriticalTemperature"      Value="1100"  />
          <Decimal Name="CriticalTemperatureScaler" Value="0.5"  />
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
* Set `IgnoreThermals` to `true` for decorative, zero-mass or projector-only blocks.
* Waste-energy fractions above ~0.3 make a block a serious heat source; the vanilla default is
  0.05.
