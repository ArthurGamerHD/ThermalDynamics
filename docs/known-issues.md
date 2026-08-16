# Known issues and limits

Current as of the review that added room air, thresholds, point heat sources, the mod API and the
thermal vision overlay. Split into defects, unfinished work, and limits that are deliberate.

## Unfinished

**The heat pump does nothing.** `Gauge_LG_HeatPump` and `Gauge_SG_HeatPump` have models,
definitions and thermal properties, but no entry in
[ThermalCoolantShapes](../Data/Scripts/Thermodynamics/Game/ThermalCoolantShapes.cs) and no
behaviour anywhere else. They are ordinary blocks with a suggestive name. Moving heat against a
gradient for a power cost is the feature the block implies; nothing implements it.

**Radiators cannot be inline loop segments.** A radiator sheds heat when a pipe's sink face is
pressed against it, which works and is what the `radiator` scenario measures. It has no coolant
ports of its own, so a loop cannot run *through* one. The block is 1×5×2 with mount points only on
its top and bottom, so adding ports needs the port geometry checked against the model.

**Room air is not saved.** Block and loop temperatures persist; a room's air temperature does not.
On load a room's air starts at the average temperature of the surfaces around it, which is a good
estimate but not the value it had. Adding a section to the storage codec is straightforward — rooms
would need to be keyed by anchor cell, as they are in memory.

**No network replication.** `SENetworkAPI` is initialised on channel `30323` and nothing is
registered on it. Clients run their own simulation from the same inputs and reach the same answers,
but nothing reconciles them: a client that joins mid-session starts from saved temperatures, and
divergence is never corrected. Damage and settings are server authoritative, so the divergence is
cosmetic, but it is real.

**Rich HUD Framework integration.** The readouts use Text HUD API and the terminal. A Rich HUD
client would give a proper settings menu and a richer overlay, and needs its client half vendored
into `Data/Scripts` from the framework's repository — the workshop copy ships only the server half.

## Deliberate limits

**Solar occlusion is per grid.** One raycast decides whether the whole grid is lit. Self-shadowing
is not modelled, so a large ship is entirely lit or entirely shaded. Per-block shadowing would cost
a raycast per block.

**Point sources are not occluded.** A registered heat source heats through walls and through other
ships. Occlusion is left to the host, which can simply not register a source it knows is hidden.

**Pressurisation is only known through air vents.** The game exposes room pressure nowhere else a
mod can reach. A sealed compartment with no vent holds no air as far as this mod is concerned.

**A room that changes shape loses its air temperature.** Rooms are matched across rebuilds by their
lowest cell. Building inside a compartment gives it a fresh air mass at the temperature of its
walls.

**Thermal vision redraws the world rather than recolouring it.** There is no shader or frame buffer
access for mods, so the rendered view is blanked and everything with a temperature is drawn again.
Three consequences follow. Terrain is a few hundred sampled patches, so its relief is coarse and it
does not resolve small features. Asteroids are discs at ambient, not shapes. And anything the mod
does not draw — dropped components, debris, particle effects — is simply absent from the view.

**Thermal vision's depth order depends on the renderer sorting billboards back to front.** Bodies
are projected into a shallow band that preserves their real depth order, so correct layering follows
if — and only if — transparent billboards are drawn far to near. That is the ordinary behaviour for
alpha blending, but it is an assumption about the engine rather than something the mod controls.

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
particular the vent sweep, the terminal readout, the thermal vision overlay and the mod API's
delegate table have no automated coverage. The API's *shape* is checkable without a session and is
worth pinning down.

`docs/bugs-and-performance.md` records findings from earlier stress work; entries there that are
still open are the performance ones, not the correctness ones.
