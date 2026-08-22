# Mod API

Thermal Dynamics is meant to be built on. Everything the simulation knows is readable, everything
it does is switchable, and both heat sources and temperature events can be driven from another mod.

Space Engineers mods cannot reference one another's assemblies, so the contract is a dictionary of
delegates passed by mod message. Bind to it once and hold the delegates you need.

* **Channel:** `2985582372` (the mod's workshop id)
* **Payload:** `Dictionary<string, Delegate>`
* **Version:** `1` — read `ApiVersion` and refuse a major you were not written against.

## Binding

Register a handler and send anything on the channel to ask for the table. Thermal Dynamics also
broadcasts once at session start, so either order works.

```csharp
const long ThermalChannel = 2985582372;

Func<IMySlimBlock, float> getTemperature;

public override void LoadData()
{
    MyAPIGateway.Utilities.RegisterMessageHandler(ThermalChannel, OnThermalApi);
    MyAPIGateway.Utilities.SendModMessage(ThermalChannel, null);   // ask, in case we loaded late
}

private void OnThermalApi(object payload)
{
    var api = payload as Dictionary<string, Delegate>;
    if (api == null) return;

    var version = api["ApiVersion"] as Func<int>;
    if (version == null || version() != 1) return;

    getTemperature = api["GetBlockTemperature"] as Func<IMySlimBlock, float>;
}
```

Unregister the handler in `UnloadData`.

## Reading

| Key | Signature | Returns |
| --- | --- | --- |
| `ApiVersion` | `Func<int>` | API major version. |
| `GetBlockTemperature` | `Func<IMySlimBlock, float>` | Kelvin, or 0 when the block is not simulated. |
| `GetBlockThermals` | `Func<IMySlimBlock, MyTuple<float,float,float,int>>` | Temperature K, heat capacity J/K, critical temperature K, exposed faces. |
| `GetGridSummary` | `Func<IMyCubeGrid, MyTuple<float,float,int,int>>` | Hottest block K, ambient K, blocks over critical, coolant loops. |
| `GetRoom` | `Func<IMyCubeGrid, Vector3I, MyTuple<bool,float,float,float>>` | Is a sealed room, air temperature K, pressure 0..1, volume m³. |
| `GetGridHeatBalance` | `Func<IMyCubeGrid, MyTuple<float,float>>` | Watts the grid is venting, watts it is making. Venting reads zero while a grid is net absorbing. |

A block that is not simulated — excluded by `ExcludeFromSimulation`, on a grid without physics, or not yet
registered — reads as zero rather than throwing.

## Writing

| Key | Signature | Effect |
| --- | --- | --- |
| `SetBlockTemperature` | `Func<IMySlimBlock, float, bool>` | Sets a block's temperature outright. |
| `AddBlockHeat` | `Func<IMySlimBlock, float, bool>` | Adds joules. Converted through the block's own heat capacity, so the same energy warms a girder more than a battery. |
| `SetRoomPressure` | `Func<IMyCubeGrid, Vector3I, float, bool>` | Sets how full of air the room containing a cell is, 0..1. |

Each returns false when the target does not exist. Prefer `AddBlockHeat` for anything physical: it
is the only one of the three that conserves energy.

## Heat sources

A point source radiates like a small sun: the mod resolves it to `P / (4π r²)` at each grid, turns
it into a direction in that grid's frame, and the solver weights it by which faces look at it.

| Key | Signature | Effect |
| --- | --- | --- |
| `AddHeatSource` | `Func<IMyEntity, float, float, int>` | Follows an entity. Watts, range in metres. Returns an id, or 0. |
| `AddHeatSourceAt` | `Func<Vector3D, float, float, int>` | Fixed at a world position. |
| `UpdateHeatSource` | `Func<int, float, bool>` | Changes the output of a registered source. |
| `RemoveHeatSource` | `Func<int, bool>` | Removes it. |

A source bound to an entity disappears when the entity does, so a source on a projectile or a
burning wreck needs no cleanup. Beyond `range` a source is not sampled at all — keep it to the
distance the effect should actually be felt at, because every source costs one pass over the
exposed blocks of every grid within it.

```csharp
var addSource = api["AddHeatSource"] as Func<IMyEntity, float, float, int>;
int id = addSource(missile, 5e6f, 300f);       // 5 MW, felt out to 300 m
```

## Thresholds

Register a temperature; get called when any block crosses it.

| Key | Signature |
| --- | --- |
| `AddThreshold` | `Func<float, int, Action<IMySlimBlock,int,float,float,bool>, int>` |
| `RemoveThreshold` | `Func<int, bool>` |

`AddThreshold(temperature, direction, callback)` where direction is `0` rising, `1` falling, `2`
both. Returns an id for `RemoveThreshold`. The callback receives the block, the threshold id, the
threshold, the temperature reached, and whether the crossing was upward.

```csharp
var addThreshold = api["AddThreshold"]
    as Func<float, int, Action<IMySlimBlock, int, float, float, bool>, int>;

addThreshold(450f, 0, (block, id, threshold, temperature, rising) =>
{
    // 450 K on the way up
});
```

Thresholds are registered against the mod, not a grid, so they apply to grids created later too.
Crossings are raised once per step, after the step completes, on the server and on clients alike.
A callback that throws is dropped and logged rather than allowed to stop the simulation — do not
rely on being called again after throwing.

## Settings

Every setting is live: writing one takes effect on the next step of every grid, with no reload.

| Key | Signature |
| --- | --- |
| `ListSettings` | `Func<List<string>>` |
| `GetSetting` | `Func<string, float>` |
| `SetSetting` | `Func<string, float, bool>` |

Switches are 0 and 1. `GetSetting` returns `float.NaN` for a name that does not exist, and
`SetSetting` returns false. Changes are not written to the config file — they last for the session
unless an administrator saves them with `/thermal save`. The names are listed in
[configuration.md](configuration.md).

## In-process extension

A mod that wants more than the message API can go further, at the cost of coupling to this one's
source. The simulation core in
[Data/Scripts/Thermodynamics/Core](../Data/Scripts/Thermodynamics/Core) has no dependency on the
game session, and its two seams are meant to be used:

* **`IBlockAdjacency`** — supply "which blocks touch this one" from a better index than a cell map.
* **`ThermalSimulation`** — `AddBlock`, `RemoveBlock`, `RefreshBlock`, `RefreshBlockSealing`,
  `Update`, `StepExact`, `Save`, `Load`, and read back node temperatures, overheat events and
  threshold crossings. This is the whole surface a host needs; the test harness under
  [tests/](../tests) drives it with plain numbers and the game adapter drives it from the session.

## Guarantees

* No API call throws into the caller. Bad arguments return `false`, `0` or `NaN`.
* No API call is required to be made on a particular thread or update phase.
* Delegate signatures use whitelisted types only, so scripts and mods can both bind.
* Keys will not change meaning within a major version. New keys may be added; missing keys mean an
  older build, so test for null after casting.
