# Known issues and limits

Current as of the review that added room air, thresholds, point heat sources and the mod API. Split into defects, unfinished work, and limits that are deliberate.

## Fixed, worth remembering

**Never assign `NeedsUpdate` from a game logic component that asked for entity updates.**
`[MyEntityComponentDescriptor(typeof(MyObjectBuilder_CubeGrid), true)]` makes the component's
`NeedsUpdate` property the *grid entity's* update flags. Assigning to it clears whatever the grid
set for itself, and `MyCubeGrid` re-arms `EACH_FRAME` only when its scheduled-update queue goes from
empty to non-empty — so clearing it once stops that queue being drained for the rest of the session.
The visible symptom was ship control: `MyGroupControlSystem` recalculates the controlling cockpit
from that queue, so sitting down gave "Someone else is using this ship!" forever. Use `|=`, and stop
work with a flag of your own rather than by taking the entity's updates away.

## Unfinished

**Radiators cannot be inline loop segments.** A radiator sheds heat when a pipe's sink face is
pressed against it, which works and is what the `radiator` scenario measures. It has no coolant
ports of its own, so a loop cannot run *through* one. The block is 1×5×2 with mount points only on
its top and bottom, so adding ports needs the port geometry checked against the model.

**No network replication.** `SENetworkAPI` is initialised on channel `30323` and nothing is
registered on it. Clients run their own simulation from the same inputs and reach the same answers,
but nothing reconciles them: a client that joins mid-session starts from saved temperatures, and
divergence is never corrected. Damage and settings are server authoritative, so the divergence is
cosmetic, but it is real.

**The settings menu cannot change anything from a multiplayer client.** The Rich HUD menu edits the
same server-side config the chat commands do, so on a client every simulation control is disabled
and only the four presentation switches work. Making them editable means replicating settings, and
nothing is registered on the network channel yet — see the replication entry above.

**The heat pump's electrical hookup is only checkable in game.** The simulation half is under test
offline. The half that makes it cost anything — a `MyResourceSinkComponent` attached in code during
`Init`, because an upgrade module has no definition field for one — cannot be exercised without a
session, so whether the grid's resource distributor picks the sink up is unverified.

**The heat pump changed block type.** It was a `CubeBlock` and is now an `UpgradeModule`, because
only a functional block has a terminal to switch it from. A grid saved with the old block loses it
on load. Nothing was lost by doing it: the old block had no behaviour at all.

## Fixed, worth remembering

**Config defaults belong on the fields, not in a factory method.** A world's config file has no
element for a setting added after that file was written, and the XML reader leaves such fields at
`default(T)` — so every setting added since a world was first loaded ran as `false` or `0` in that
world, silently. A test world reported `SolarOcclusionPlanets False`, `SolarTerrainRange 0` and a
room overlay span of one kelvin while its owner had changed none of them. Bumping the file version
would only have papered over it, and thrown away real customisation each time. The defaults now
live on the field declarations, where a reader that finds nothing leaves them alone.

**The game has no wind field, and its wind speed is a rating.** `MyPlanet.GetWindSpeed` returns the
planet definition's maximum wind scaled by air density — a constant per altitude, the same at every
latitude and longitude, with no direction. It is what wind turbines are balanced against. Used as a
wind it read 80 m/s over a parked ship on an earthlike world, which tripped friction heating and
doubled convection; the mod now treats it as a ceiling and supplies its own field. Nothing in the
API exposes a real local wind, so the field is invented rather than read.

## Suspected defects

**A face bolted to a block that does not seal is counted as buried.** Exposure rejects any cell face
where two mount surfaces meet, regardless of what the neighbour is. Against a grating, lattice or
any other non-airtight block that is wrong twice over: the room mapper calls the cell beyond that
face *external* — air floods through it — and the face still neither radiates nor takes sunlight.
Pinned by `AFaceAgainstAnOpenLatticeIsRejectedAsMountedNotSealed` in
[ExposureAuditTests](../sim/Thermodynamics.Tests/ExposureAuditTests.cs), which characterises the
behaviour rather than endorsing it. The fix is a judgement call about what a mount joint means:
either exempt neighbours that do not seal, or scale the face by the mounted fraction rather than
dropping it whole.

## Deliberate limits

**Solar occlusion against the rest of the world is per grid.** One raycast decides whether the whole
grid is shaded by a planet or another ship, so a capital ship half in a station's shadow is lit or
shaded in its entirety. A grid's *own* shadow is modelled — see `SolarSelfShadowing` — but only its
own: nothing else on the map casts onto it at block resolution.

**A room with no air vent holds no air.** The game exposes a room's oxygen level through vents and
nowhere else, so a sealed compartment that was never piped is indistinguishable from one that cannot
be measured, and is treated as empty. It costs that compartment the heat capacity of its air; the
walls still conduct and radiate as they should.

**Point sources are not occluded.** A registered heat source heats through walls and through other
ships. Occlusion is left to the host, which can simply not register a source it knows is hidden.

**Pressurisation is only known through air vents.** The game exposes room pressure nowhere else a
mod can reach. A sealed compartment with no vent holds no air as far as this mod is concerned.

**A room that changes shape loses its air temperature.** Rooms are matched across rebuilds by their
lowest cell. Building inside a compartment gives it a fresh air mass at the temperature of its
walls. The same key carries air across a save, so a compartment rebuilt while the world was closed
comes back at the temperature of its walls rather than the one it was saved at.

**There is no thermal view.** A heat overlay was built and removed: mods get no shader, no
post-process and no frame buffer, so the only way to recolour the world is to blank it and redraw
every body as a billboard. That gives coarse terrain, discs for asteroids, and nothing at all for
anything the mod does not draw. Seeing temperature is the terminal readout, the cockpit summary, the
crosshair readout and the x-ray block overlay instead — the overlay keeps the part of that work that
was worth keeping, since a debug view *wants* to see through the hull.

**Emissivity is used as absorptivity.** The grey-body assumption. A block cannot be made shiny to
the sun and black to space.

**`Conductivity` is a 0..1 quality value, not W/(m·K).** It is multiplied by a reference 200 W/(m·K)
to get real units. Definitions describe a block's material by specific heat, which is real, and its
conduction by a quality, which is not.

**Mount coverage is combined as independent fractions.** Where both sides of a joint are only partly
mounted, the bolted area is `coverage_a × coverage_b`. The model records coverage per face, not per
cell, so it cannot know whether two partial mounts line up.

## Testing gaps

The test suite covers the model thoroughly and the adapter barely — `HostAdapterTests` exercises
what can be reached without a session, and the rest of `Game/` is only exercised in the game. In
particular the vent sweep, the terminal readout and the mod API's delegate table have no automated
coverage. The API's *shape* is checkable without a session and is
worth pinning down.

`docs/bugs-and-performance.md` records findings from earlier stress work; entries there that are
still open are the performance ones, not the correctness ones.
