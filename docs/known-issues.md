# Known issues and limits

Current as of the review that added room air, thresholds, point heat sources and the mod API. Split into defects, unfinished work, and limits that are deliberate.

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

**Rich HUD Framework integration.** The readouts use Text HUD API and the terminal. A Rich HUD
client would give a proper settings menu and a richer overlay, and needs its client half vendored
into `Data/Scripts` from the framework's repository — the workshop copy ships only the server half.

**The heat pump's electrical hookup is only checkable in game.** The simulation half is under test
offline. The half that makes it cost anything — a `MyResourceSinkComponent` attached in code during
`Init`, because an upgrade module has no definition field for one — cannot be exercised without a
session, so whether the grid's resource distributor picks the sink up is unverified.

**The heat pump changed block type.** It was a `CubeBlock` and is now an `UpgradeModule`, because
only a functional block has a terminal to switch it from. A grid saved with the old block loses it
on load. Nothing was lost by doing it: the old block had no behaviour at all.

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
