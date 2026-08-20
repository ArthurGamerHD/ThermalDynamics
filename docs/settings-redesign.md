# Settings redesign

The menu, the config file and the definition files, treated as one surface.

The current split is an accident of how the mod grew: `ThermodynamicsConfig.cfg` holds what the
solver reads, `Cubes.xml`, `Planets.xml` and `Loops.xml` hold what the definitions read, and the
menu shows the first and not the second. A player asking "how fast does coolant move" is asking a
settings question, and the answer is in a file the settings menu has never mentioned.

This is the plan for closing that, written after looking at the built menu in game rather than at
the API.

## What the built menu got wrong

Measured against a screenshot of it running, not against intent.

| Fault | Cause |
| --- | --- |
| Status text arrived with both ends cut off | a `TerminalLabel` is one centred line that clips rather than wrapping |
| `Cost and stabilit` in the rail | a page name clips at about seventeen characters |
| Debug drew on top of the World row | a root page added *after* a folder renders against the folder's row |
| Half-empty tiles | a category is a fixed tall band whatever is in it |
| `Systems (cont.)` | an overflow row named after the fact that it overflowed |
| Save and Reset as buttons | a menu asking you to confirm what you already did |
| A profile changed nine settings | it read as a full preset and behaved as a patch |

The first five are fixed. The last two are fixed: every change saves itself a second later, a
profile now sets every world setting, and `default` is therefore the way to start over.

## What is left, in the order it should be done

### 1. A page per system, with its own switch on it

**The Mechanisms page goes away.** Four switches sitting together because they are all switches is
filing by part of speech. `EnableConvection` belongs at the top of the convection page, above the
convection dials, where switching it off visibly greys what it governs.

Proposed pages, each one a system with its enable switch first and its own knobs under it:

| Folder | Pages |
| --- | --- |
| Solver | Cost limits · Pace |
| Heat transfer | Conduction · Radiation · Convection · Solar · Occlusion |
| Ship systems | Coolant loops · Heat pumps · Room air · Waste heat · Friction · Overheat damage · Point sources |
| World | Climate · Weather · Underground |
| — | Overview · Status · Debug |

`SolarSelfShadowing` moves to Occlusion, where it belongs — it is what a grid does to itself. It
sat under Solar because that is where its *setting name* starts, which is the code's filing system
rather than a reader's.

### 2. The definition files become part of the menu

This is the half the menu has never had, and it is where several of the knobs people ask for
actually live.

| File | Entries | Values | Feasibility |
| --- | ---: | ---: | --- |
| `Loops.xml` | 1 | 8 | **Easy.** One entry, and `ThermalSimulation.LoopProperties` already has a setter that marks topology dirty. This is where `LargeGridFlowRate` lives — the coolant flow rate the menu is missing. |
| `Planets.xml` | 1 shipped | 11 | **Easy.** One entry per planet type, applied per grid on a planet change. |
| `Cubes.xml` | 54 | 8 each | **Hard, and different in kind.** 432 values, and the interesting ones belong to blocks the player is looking at rather than to a list they scroll. Properties are cached per definition in `ThermalBlockCatalog`, so changing one at runtime needs the cache invalidated and every node of that type refreshed. |

A mod folder is read-only in a workshop install, so none of this writes the XML. It writes a
**per-world override layer** into world storage, applied over what the definitions loaded, and
replicated to clients like every other setting.

Cubes is the one worth splitting: the first cut edits `DefaultThermodynamics` and the mod's own
blocks — the entries that decide how everything unauthored behaves — and a later one can add
editing the block you are aiming at, which is the form the question usually takes.

### 3. Status becomes the panel worth opening

Today it lists what changed, which the Overview already counts. It should be the mod's own report:
what the simulation is costing, what it is doing, and what is wrong with it.

* **Cost**, per stage rather than as one number: topology, room mapping, exposure, solver, room
  pressure, with the same figures the telemetry report carries.
* **Load**, as a projection rather than an instant: substeps demanded against granted, what the
  per-block cap is flooring, and what a change to the caps would cost — the sweep
  [field-tuning.md](field-tuning.md) does by hand.
* **Faults**: settings that cancel each other, grids running below real time, compartments the game
  seals and this model does not, pumps in a ring that oppose each other, definition entries that
  fell back to the default because their `TypeId` was wrong. Every one of these has been a real
  defect at least once, and each was found by reading a dump rather than by the mod saying so.

**Graphs need a decision.** Rich HUD has no chart control. A sparkline can be drawn as text in a
`TextPage`, which is cheap and honest; a real plot means a custom HUD element, which is a different
size of job.

### 4. The config file follows the menu

The file is flat: forty-eight elements in one list, in the order they were added. If the menu is
organised by system then the file should be too, because they are read by the same person for the
same reason.

```xml
<Solver>
  <Frequency>4</Frequency>
  <MaxSubsteps>16</MaxSubsteps>
</Solver>
<CoolantLoops enabled="true">
  <LargeGridFlowRate>10</LargeGridFlowRate>
</CoolantLoops>
```

Two things this must not break:

* **A world's existing values.** Defaults live on the fields, so a reader that finds nothing leaves
  them alone — which means a restructure silently resets every tuned world unless the old flat
  shape is read first and migrated. That is a v6 → v7 read path, and it is the whole risk of this
  step.
* **The names.** `/thermal set`, the mod API and the sync all address settings by name. Grouping
  may change where a name sits in the file; it should not change the name.

## Order

1. Pages per system, mechanisms page removed. *No file changes; menu only.*
2. Loops and Planets overrides, with coolant flow rate on the Coolant loops page. *Adds a world
   override file.*
3. Status panel as a real report.
4. Config restructure with a v6 migration.
5. Cubes overrides, defaults and shipped blocks first.

Each step stands alone and each is separately revertible, which is why the file restructure — the
one with a migration risk — is late rather than first.
