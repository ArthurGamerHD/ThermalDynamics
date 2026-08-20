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
| `Conductivity` | Decimal | `≥ 0` | Thermal conductivity in **real W/(m·K)** — the number a materials table gives. Mild steel 50, stainless 15, glass 1, aluminium 237, copper 400. The game's pace is set once, globally, in `ThermalConstants.ConductionScale`, so this stays a description of the material. |
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
| `SegmentsPerSecondAtFullFlow` | `LargeGridFlowRate` / `SmallGridFlowRate` | Parcels per second is the solver's unit, not a player's; and one parcel is one pipe block, so a single figure meant 10 m/s on a large grid and 2 m/s on a small one. Converted on read, per grid. |

New definitions should use the current names. The old ones are not planned for removal.

### `Conductivity` is in real W/(m·K)

Type the figure a materials table gives: mild steel 50, stainless 15, glass 1, aluminium 237,
copper 400. `ThermalLink` multiplies it by `ThermalConstants.ConductionScale`, which is one global
constant setting the game's pace, so the value here stays a description of the material rather than
a balance dial.

This was not always true. It used to be a 0…1 quality value against a 200 W/(m·K) reference, where
a tabulated figure clamped to 1 and quietly meant "the best there is" — which made every metal
conduct like every other metal. **A coolant loop's `Conductivity` still works the old way**, and
deliberately: fluid-to-wall transfer is convective, and the honest dial for it is a heat transfer
coefficient in W/(m²·K), which is a change to the loop equations rather than to a number.

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

### Where a block's properties come from

**Almost nothing is authored.** A block's material properties are *derived* from its build
components — the list of steel plates, glass, power cells and motors the game already publishes for
every block that exists. `BlockMaterials` gives each of the game's thirty-two components real
material figures, and `BlockThermalDerivation` blends them by mass.

That means a window is glass, a battery is lithium, a medical bay is mostly water and a plushie is
fabric, without anyone having written a line for any of them — and the same is true of every block
of every other mod, which previously all fell through to one entry describing mild steel.

| Derived from components | Taken from the block's type | 
| --- | --- |
| `Conductivity`, `SpecificHeat`, `Emissivity`, `CriticalTemperature` | `ProducerWasteEnergy`, `ConsumerWasteEnergy`, `ExposedSurfaceMultiplier`, `OverheatDamagePerKelvin` |

The split is not arbitrary. What a block is made of cannot say what it does with power: two blocks
of identical construction, one a thruster and one a girder, differ entirely in what they put into
the ship. The functional half is a table keyed by block type in `BlockThermalDerivation`, and it is
the only place opinions are left.

Of the three derived blends, only specific heat is exact — heat capacity is additive, so the
mass-weighted mean is the right answer rather than an approximation of one. Conductivity and
critical temperature are mass-weighted because a build cost does not say how the phases are
arranged. Emissivity is a property of the *surface*, so it comes from the heaviest component rather
than from a blend.

Run the sim harness's `blocks` command to print the whole table, every type and every subtype that
deviates from it.

### Lookup and fallback

An entry in `Cubes.xml` **overrides** the derivation. For a block with definition id
`TypeId/SubtypeId`, `GetDefinition` resolves in this order:

1. `TypeId/SubtypeId` — an exact per-block entry.
2. `TypeId/DefaultThermodynamics` — a per-type entry.
3. `EnvironmentDefinition/DefaultThermodynamics` — the global fallback.

The probe for steps 1 and 2 is whether the definition id is indexed **and** exposes
`ExcludeFromSimulation`, so a group without that bool is not an entry at all and falls through.

Two rules matter more than the order:

* **A partial entry is merged, not substituted.** Whatever an entry declares wins; whatever it omits
  is derived. So an author who wants a block to run hotter writes one line — a
  `CriticalTemperature` and nothing else — and keeps the material the block's components imply.
  Before this, an omitted property read as *zero*, and a zero `SpecificHeat` is a block with no heat
  capacity while a zero `CriticalTemperature` is a block above critical the moment it is placed.
  `EveryEntryInCubesDeclaresEveryPropertyTheGameReads` guards the shipped file against that.
* **Landing on the global fallback counts as no answer.** That entry describes mild steel, which was
  the best available guess before a block's own build cost could be read and is a worse one now, so
  it does not override a derivation that actually describes the block. It applies to a block with no
  priced components at all.

### Shipped values ([Data/Cubes.xml](../Data/Cubes.xml))

`Cubes.xml` no longer carries an entry per block type. It holds only deliberate deviations from the
derivation:

| Definition | What it overrides and why |
| --- | --- |
| Coolant pipes and pumps (LG + SG) | Copper: conductivity 400, specific heat 385. The block exists to move heat. |
| Heat pumps (LG + SG) | Both waste fractions **0** — the solver already puts every watt the pump draws into its hot side, so a fraction on top would charge the same energy twice. |
| Radiators (LG + SG) | Emissivity 0.35 and 1.25× surface against the default 0.15 and 1×. Shedding is the block's whole purpose, so these are design decisions rather than consequences of a build cost. |
| `EnvironmentDefinition/DefaultThermodynamics` | The floor for a block with no priced components. |

Every vanilla block's properties now come from the derivation instead. Notable results, all of them
consequences rather than choices:

| Type | k W/(m·K) | c J/(kg·K) | ε | Critical K | Why |
| --- | --- | --- | --- | --- | --- |
| `CubeBlock` (armour) | 45 | 535 | 0.15 | 868 | steel |
| Windows | 8 | 788 | 0.92 | 814 | glass conducts two orders of magnitude worse and radiates six times better |
| `BatteryBlock` | 34 | 704 | 0.85 | **735** | lithium cells are the least heat-tolerant thing on a ship |
| `MedicalRoom` | 28 | 964 | 0.90 | **592** | mostly water, plastics and fluids |
| `Reactor` | 43 | 553 | 0.25 | 1,086 | fuel in a graphite and steel assembly |
| `HydrogenEngine` | 98 | 550 | 0.25 | 1,121 | carries a prototech cooling unit |
| `JumpDrive` | **173** | 460 | 0.20 | 745 | forty per cent superconductor by mass |
| `Thrust` | 56 | 472 | 0.40 | 1,115 | refractory nozzle alloy |
| Plushies | **0.05** | **1,300** | 0.95 | 500 | fabric, not the steel they used to be |

> A reactor's heat runs through `ProducerWasteEnergy` and nothing else. It delivers power through
> `MyResourceSourceComponent`, so its consumer fraction is dead text — getting that backwards is how
> every reactor in the game ran at 0 W. See [balance.md](balance.md#reactor-waste-heat).

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
| `CoolantMassPerPipe` | 50 | `≥ 1` | Coolant in each pipe block, kg, so a ring's charge scales with its length. |
| `Conductivity` | 1 | `0 … 1` | Transfer scaling for both the pipe and the sink-face exchange. **Not** the real W/(m·K) that a block's `Conductivity` now takes — the fluid-to-wall path is convective, and its honest dial would be a heat transfer coefficient in W/(m²·K). Unchanged for now. |
| `SpecificHeat` | 3400 | `≥ 0` | Coolant heat capacity in real J/(kg·K). Water-glycol is about 3400, which is why a loop carries so much more heat than the steel around it. Scaled by `HeatTimeScale` exactly as a block is. |
| `PipeContactMultiplier` | 1 | `≥ 0` | Contact area between fluid and the pipe block it runs through. |
| `SinkContactMultiplier` | 1 | `≥ 0` | Contact area between fluid and a block pressed against a sink face. |
| `LargeGridFlowRate` | 10 | `≥ 0` | How fast the coolant moves on a large grid with one pump at full power, **m/s** — the unit the terminal reports. Flow rises with the square root of combined pumping, so four pumps carry twice this, not four times. |
| `SmallGridFlowRate` | 10 | `≥ 0` | The same for a small grid. Split because it is a balance dial, not a physical constant: a small-grid pump is a much smaller machine driving a much shorter ring. Shipped equal. A small-grid pipe is a fifth as long, so the same speed is five times the parcel rate and the ring levels out sooner. |
| `StagnantTransferFraction` | 1 | `0 … 1` | Fraction of transfer that survives with nothing circulating. A stagnant pipe still conducts into the coolant touching it; it just cannot carry that heat anywhere. |

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
