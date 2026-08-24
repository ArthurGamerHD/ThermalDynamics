using System;
using System.Collections.Generic;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// **Every input a client drives its own simulation from, degraded one at a time and then all
    /// at once**, with the readout scored against the server's.
    ///
    /// <para>
    /// [ClientDriftLab](ClientDriftLab.cs) answers a narrower question: a client that started from
    /// the wrong temperatures and is otherwise perfect. That is one cause of disagreement and it is
    /// the benign one, because it is a **perturbation** — the model is dissipative, so a single
    /// wrong state decays on its own. A client's inputs are not perturbations. A sun direction that
    /// is permanently a degree off, a reactor whose power arrives a second late, a hull the client
    /// believes is in thinner air: each of those is a **standing bias**, and a bias does not decay,
    /// it settles. Nothing measured before this told the two apart.
    /// </para>
    ///
    /// <para>
    /// **The column that tells them apart is the standing error.** Every run reports the worst
    /// disagreement it reached and the mean over its final third; a perturbation ends near zero
    /// whatever it peaked at, and a bias ends where it settled. A degradation whose standing error
    /// is not zero is one the correction is not merely accelerating but holding together.
    /// </para>
    ///
    /// <para>
    /// **What this cannot see, stated rather than assumed.** It runs two instances of the same
    /// solver in one process, so it models neither the transport — latency, loss, the game's send
    /// queue — nor the engine behaviour behind each degraded input. Every magnitude below is a
    /// knob this lab turns, not a figure measured from a session, and the ones that are guesses say
    /// so where they are declared. What the lab does answer is the shape: which inputs bias and
    /// which perturb, and whether the correction reaches each.
    /// </para>
    ///
    /// <para>
    /// See known-issues.md and [backlog.md](../../docs/backlog.md) `B4`.
    /// </para>
    /// </summary>
    public static class ClientInputLab
    {
        /// <summary>
        /// One way a client's view of the world is worse than the server's.
        ///
        /// <para>
        /// Every field defaults to *not degraded*, so a case names only what it breaks and the
        /// combined case is the union of the others rather than a separate opinion about what a bad
        /// client looks like.
        /// </para>
        /// </summary>
        public class Degradation
        {
            /// <summary>What this case is called in the table.</summary>
            public string Name = "none";

            /// <summary>One line on what is being degraded and why that is what the engine does.</summary>
            public string Because = "";

            /// <summary>
            /// The client joined holding the server's state from this many simulated seconds ago.
            /// **A perturbation**: it is what restoring a save gives, and it happens once.
            /// </summary>
            public float StaleSeconds;

            /// <summary>
            /// Simulated seconds between the client dropping its solver backlog, which is what
            /// <see cref="SimulationScheduler.StepsDue"/> does on a frame long enough to exceed its
            /// per-frame cap. **A repeated perturbation.**
            /// </summary>
            public float HitchEverySeconds;

            /// <summary>How much simulated time each hitch costs.</summary>
            public float HitchLosesSeconds = 5f;

            /// <summary>
            /// The client samples the environment the server had this many seconds ago.
            /// **A bias**: grid position and orientation replicate on their own schedule, so a
            /// moving ship's client-side sun, altitude and air are always of a moment ago.
            /// </summary>
            public float EnvironmentLagSeconds;

            /// <summary>
            /// The client's sun direction is off by this many degrees, permanently.
            /// **A bias**, and the one with no measured magnitude behind it — how far a client's
            /// replicated orientation trails the server's is engine behaviour this lab cannot see.
            /// </summary>
            public float SunAngleDegrees;

            /// <summary>
            /// The client's block power is the server's from this many seconds ago. **A bias while
            /// the load is moving**: a reactor's output reaches a client through the game's own
            /// block replication, which is neither instant nor continuous.
            /// </summary>
            public float PowerLagSeconds;

            /// <summary>
            /// The client's block power is wrong by this share, permanently. **A bias**, and it
            /// stands in for the quantisation and rounding in whatever the game replicates rather
            /// than for a measured error.
            /// </summary>
            public float PowerErrorShare;

            /// <summary>
            /// The client's air density is wrong by this much, 0..1. **A bias**: the game's own gas
            /// system decides what is pressurised, and a client's answer can differ — which matters
            /// because convection is the strongest path a hull has.
            /// </summary>
            public float AirDensityError;

            /// <summary>
            /// The client's thrust is wrong by this share, permanently. **A bias, and the one input
            /// the engine does not replicate at all.**
            ///
            /// <para>
            /// A thruster's heat is charged against `CurrentThrust`, which is physics state: the
            /// engine *predicts* it on a client rather than sending it, so a client's thrust is its
            /// own guess about a ship whose physics it is not running. On a burning hull that is
            /// the largest heat term there is
            /// ([backlog.md](../../docs/backlog.md) `F17`).
            /// </para>
            ///
            /// <para>
            /// It only reaches a run that is flying: the `burn` scenario drives thrust on both
            /// sides, and a hull at rest makes this knob measure nothing.
            /// </para>
            /// </summary>
            public float ThrustErrorShare;

            /// <summary>
            /// The client's airspeed is wrong by this share, permanently. **A bias, and the classic
            /// multiplayer prediction error**: a client's ship is where its own physics put it, and
            /// how fast it is going is part of that.
            ///
            /// <para>
            /// It reaches two terms and they are not the same size. Forced convection saturates —
            /// `h = h0 (1 + 0.1 sqrt(v))` — so a fifth more speed is a few per cent more cooling;
            /// aerodynamic friction goes as the **cube** of airspeed, so the same fifth is 1.7x the
            /// heating. That asymmetry is what the 300 m/s constraint in
            /// [balance.md](../../docs/balance.md#the-300-ms-constraint) is about, and this is the
            /// same asymmetry arriving as a client's guess ([backlog.md](../../docs/backlog.md)
            /// `F19`).
            /// </para>
            ///
            /// <para>
            /// It only reaches a run that is flying: the `burn` scenario has air moving past the
            /// hull at 100 m/s, and a hull in vacuum or at rest makes this knob measure nothing.
            /// </para>
            /// </summary>
            public float SpeedErrorShare;

            /// <summary>
            /// How often the client's solar occlusion flag disagrees with the server's, and for how
            /// long. **The one input that is a binary**, so it is not wrong by an amount — it is
            /// wrong by the entire solar term at once.
            ///
            /// <para>
            /// Occlusion is resolved by raycasting against voxels and neighbouring grids, on each
            /// machine's own budget and interval, against world state a client holds differently. A
            /// ship a client believes is in shade while the server has it in full sun is the
            /// largest single-step input error available ([backlog.md](../../docs/backlog.md)
            /// `F18`). Written as a period and a duration, as `hitching` is, because a client that
            /// is permanently wrong is a bound rather than a description.
            /// </para>
            /// </summary>
            public float OcclusionWrongEverySeconds;

            public float OcclusionWrongForSeconds = 3f;

            /// <summary>
            /// The client's block masses are wrong by this share, permanently.
            ///
            /// <para>
            /// **Mass is heat capacity, so this is the one input that is not an error in a heat
            /// flow at all** — it is an error in what a given flow does to a temperature. Every
            /// other knob here moves watts; this one moves the divisor those watts are divided by
            /// (<see cref="ThermalNode.RefreshThermalMass"/>).
            /// </para>
            ///
            /// <para>
            /// It is what the engine does twice over. A block's mass is refreshed on a rota —
            /// `SweepMass` every 8 steps, capped at 4,096 blocks — so a large grid's masses lag
            /// *by design on both machines*, and the two rotas are not in step; and the build
            /// progress and damage the mass is derived from replicate on their own schedule on
            /// top of that. Integrity reaches the model through nothing else: no path in the mod
            /// reads it, so a damaged block is a lighter block and that is all it is
            /// ([backlog.md](../../docs/backlog.md) `F20`).
            /// </para>
            /// </summary>
            public float MassErrorShare;

            /// <summary>
            /// This share of the client's heat producers are believed switched off.
            ///
            /// <para>
            /// **The second binary input, and unlike the first it is per block rather than per
            /// hull.** A block that is off draws no power and makes no waste heat, so a client
            /// that has the switch on the wrong side is not wrong by a percentage of that block's
            /// heat — it is wrong by all of it, on some blocks and not others
            /// ([backlog.md](../../docs/backlog.md) `F20`).
            /// </para>
            ///
            /// <para>
            /// Applied to producers because they are the blocks a switch reaches: turning off an
            /// armour cube changes nothing on either machine.
            /// </para>
            /// </summary>
            public float BlocksOffShare;

            /// <summary>
            /// The client's rooms hold this much less air than the server's, 0..1.
            ///
            /// <para>
            /// **A bias, and the first knob in this sweep that moves a conduction path rather than
            /// a watt or a divisor.** Room air is a well-mixed mass that links every surface
            /// bounding a compartment to every other, so a client that believes a room is empty is
            /// not running a slightly different number — it has lost the coupling entirely
            /// (<see cref="RoomAirNode.HasAir"/>).
            /// </para>
            ///
            /// <para>
            /// It is an input the mod does not own. Pressure comes from the game's own gas system
            /// by `C9` — world pressurisation, the game's airtightness answer and a vent's reported
            /// level, each able to veto air and none able to require it — so a client's answer is
            /// *that machine's* gas system's answer, arrived at on its own schedule
            /// ([backlog.md](../../docs/backlog.md) `F21`).
            /// </para>
            /// </summary>
            public float RoomPressureError;

            /// <summary>
            /// Simulated seconds the client runs before its room map converges.
            ///
            /// <para>
            /// **The second room input, and it is structural rather than a value.** The map is a
            /// local flood fill that publishes atomically — <see cref="RoomMapper.Map"/> is never
            /// partially built — so a client that has not finished a pass is not holding a rough
            /// map, it is holding the previous one, which on a grid it has just built is *empty*.
            /// Every interior face then has open air on the other side
            /// (<see cref="RoomMap.IsExternal"/>), so the hull radiates and convects from a skin
            /// that is 27 % larger than the one the server sees, and the compartments have no air
            /// to couple through because they do not exist yet.
            /// </para>
            ///
            /// <para>
            /// It converges on its own schedule and each machine runs its own: `D2` measures 7,207
            /// ticks on a million blocks. Written as a duration rather than a share because that
            /// is the shape of it — a client is wrong about the whole interior until the pass
            /// lands, and then it is right ([backlog.md](../../docs/backlog.md) `F21`).
            /// </para>
            /// </summary>
            public float RoomMapLagSeconds;

            /// <summary>
            /// The client received the same blocks in a different order.
            ///
            /// <para>
            /// **The one degradation that is not a degradation, and it is here to prove a design
            /// choice.** Every block sits at the cell it sits at on the server and carries the
            /// model it carries there, so the conduction graph, the surfaces, the rooms and the
            /// physics are identical — the only thing that differs is the *node index*, which comes
            /// from the order blocks were added and which two machines have no reason to agree on.
            /// </para>
            ///
            /// <para>
            /// It has to read exactly zero, twice over: zero disagreement, because the two hulls
            /// are the same ship, and zero after the correction, because the hot-tail packet is
            /// keyed on **position** rather than on index. An index-keyed packet would land every
            /// temperature on the wrong block here and the row would be the loudest in the sweep,
            /// which is what makes this the test of that choice
            /// ([backlog.md](../../docs/backlog.md) `F22`).
            /// </para>
            /// </summary>
            public int BuildOrderSeed;

            /// <summary>
            /// This share of the hull has not reached the client yet.
            ///
            /// <para>
            /// **The first knob here that changes which numbers exist rather than what they are.**
            /// A client that is still receiving a pasted blueprint, or that has not yet attached a
            /// subgrid the server has, is not running the same simulation with worse inputs — it is
            /// running a *different ship*: fewer nodes, a different conduction graph, and hull
            /// surfaces exposed to the sky where the missing blocks would have covered them.
            /// </para>
            ///
            /// <para>
            /// Blocks are taken from the client rather than added to it, because the direction that
            /// matters is a client behind the server: the server decides, and what a client has not
            /// been told about yet is what it is missing ([backlog.md](../../docs/backlog.md)
            /// `F22`).
            /// </para>
            /// </summary>
            public float BlocksMissingShare;

            /// <summary>
            /// Simulated seconds before the missing blocks arrive. Zero — the default — means they
            /// never do, which is the bound; a duration is the description, and it is the shape a
            /// subgrid attaching late has.
            /// </summary>
            public float BlocksMissingSeconds;

            /// <summary>
            /// The client is running the shipped defaults while the server is not. **A bias, and
            /// the worst case of one that should not happen**: settings replicate, and a client
            /// fetches them on load, so this is what a fetch that never landed would look like.
            /// </summary>
            public bool OnDefaultSettings;
        }

        /// <summary>What one degraded client did, with the correction off or on.</summary>
        public class Result
        {
            public string Name;
            public string Because;

            /// <summary>The correction under which this was run. Never null.</summary>
            public ClientDriftLab.Correction Protocol = ClientDriftLab.Correction.None;

            public int Blocks;
            public string Scenario;

            /// <summary>The worst disagreement at any sample, K.</summary>
            public float PeakKelvin;

            /// <summary>
            /// The mean disagreement over the final third of the run, K.
            ///
            /// **This is the column that separates a perturbation from a bias.** A one-off wrong
            /// state decays to nothing whatever it peaked at; a wrong input settles somewhere and
            /// stays there. A run whose standing error is not near zero has not recovered and will
            /// not.
            /// </summary>
            public float StandingKelvin;

            /// <summary>Simulated seconds with at least one block on the wrong side of critical.</summary>
            public float SecondsMisreading;

            /// <summary>The worst count behind that, since one block wrong reads like three thousand.</summary>
            public int PeakDisagreeing;

            /// <summary>Bytes a second of simulated time the correction cost.</summary>
            public float BytesPerSecond;

            /// <summary>The largest single update, in blocks.</summary>
            public int PeakBlocksSent;

            /// <summary>
            /// The most blocks the server had that the client did not, at any sample.
            ///
            /// **A block the client has not been told about is not a block it is wrong about**, so
            /// it is counted here rather than scored in the kelvin columns: the client holds no
            /// opinion, and a readout cannot misread a block it is not drawing. It is the column
            /// that says a row degraded *which numbers exist* rather than what they are.
            /// </summary>
            public int PeakMissing;

            /// <summary>
            /// Sealed compartments the hull has, which the server's air was put into.
            ///
            /// **Zero voids the two room rows the way a cold server voids the readout columns**
            /// (`E8`): a hull with no compartment cannot disagree about one. The lab raises rather
            /// than reports when a room knob is turned on such a hull, and the report prints this
            /// count in its header so a reader can see what those rows had to work with.
            /// </summary>
            public int Rooms;

            /// <summary>
            /// The most blocks the **server** had past critical at any sample.
            ///
            /// **Without this the readout columns cannot be believed.** A run on a hull that never
            /// overheats reports nothing wrong with the client's readout, for the same reason a
            /// blank page has no spelling mistakes (`E8`). A zero here voids every readout column
            /// in the row, and the report says so rather than printing them.
            /// </summary>
            public int PeakServerCritical;
        }

        /// <summary>Seconds between readings.</summary>
        public const float SampleSeconds = 5f;

        /// <summary>
        /// The smallest census hull that has a sealed compartment, in blocks asked for.
        ///
        /// **Measured rather than chosen**, and it is why the suite's own rig is this size: at
        /// 500 blocks the census hull is 907 nodes and no room at all, and at 600 it is 1,004
        /// nodes and one. A rig below this reports both room knobs as harmless, which is the
        /// blank-page failure (`E8`), and `ClientInputTests` pins the threshold so it cannot move
        /// quietly under the rows that depend on it.
        /// </summary>
        public const int SmallestHullWithACompartment = 600;

        /// <summary>
        /// How long the scripted load runs before it changes, s.
        ///
        /// **The load has to move or half these knobs are invisible.** A hull driven at a constant
        /// wattage has a client whose lagged power is the same number as the server's, so a power
        /// lag would measure as no error at all — which is a property of the rig, not of the engine.
        /// </summary>
        public const float LoadPeriodSeconds = 120f;

        /// <summary>
        /// The two states the scripted load alternates between, W a producer.
        ///
        /// <para>
        /// **Above the census figure, deliberately, and this is a choice with a cost.** The census
        /// wattage settles a hull *below* its rating, which is what the field measured — and a hull
        /// that never crosses a critical temperature makes every readout column in this table zero,
        /// so the sweep would report a correction that fixed everything by having nothing to fix
        /// (`E8`). The multiplier below is what puts the hull on the other side of critical without
        /// changing anything else about it, which is the same lever the benchmarks use. What it
        /// costs is that the kelvin figures here are of a hull under more load than a real one, so
        /// they are read as *which inputs bias* rather than as *how far a real client drifts*.
        /// </para>
        /// </summary>
        public const float LoadMultiplier = 3f;

        /// <summary>Full load, W a producer.</summary>
        public const float LoadedWatts = Census.ProducerWatts * LoadMultiplier;

        /// <summary>Idle is not zero: a ship with everything off still runs its own systems.</summary>
        public const float IdleWatts = Census.ProducerWatts * LoadMultiplier * 0.1f;

        /// <summary>
        /// Thrust heat a flying hull makes, watts per producer.
        ///
        /// Equal to the electrical load, so the `burn` scenario is a ship making half its heat by
        /// burning and half by drawing — which is what makes a thrust error and a power error
        /// comparable rather than a comparison of two different ships.
        /// </summary>
        public const float ThrustWatts = LoadedWatts;

        /// <summary>
        /// Runs one degraded client against a server, and scores the readout.
        /// </summary>
        public static Result Measure(Degradation degradation, ClientDriftLab.Correction protocol,
            string scenario = "planet", float seconds = 600f, int blocks = 2000,
            float loadPeriodSeconds = LoadPeriodSeconds)
        {
            Degradation how = degradation ?? new Degradation();
            ClientDriftLab.Correction fix = protocol ?? ClientDriftLab.Correction.None;

            ThermalSettings world = new ThermalSettings().Derive();

            // The server's world differs from the shipped defaults, so that a client which never
            // received the settings is running different physics rather than the same physics.
            ThermalSettings served = new ThermalSettings();
            served.HeatTimeScale = world.HeatTimeScale * 0.5f;
            served = served.Derive();

            ThermalSettings serverWorld = how.OnDefaultSettings ? served : world;
            ThermalSettings clientWorld = how.OnDefaultSettings ? world : serverWorld;

            ThermalSimulation server = Hulls.Driven(serverWorld, blocks);
            ThermalSimulation client = Hulls.Driven(clientWorld, blocks, how.BuildOrderSeed);

            // **Aligned by position, not by index**, which is a no-op on two hulls built the same
            // way and the whole point on two that were not. A node's index is its arrival order, so
            // seeding the spread by index gives two differently-ordered hulls two different
            // temperature *fields* — a disagreement produced by the rig rather than by the client.
            Align(client, server);

            // Capacity, not a flow, so it is set once on the hull rather than re-applied with the
            // load: the rota that produces it in game changes a block's mass, not its wattage.
            Reweigh(client, how.MassErrorShare);

            Result result = new Result
            {
                Name = how.Name,
                Because = how.Because,
                Protocol = fix,
                Blocks = server.Solver.Nodes.Count,
                Scenario = scenario,
            };

            // **The compartments hold air, and without this two of the knobs below measure
            // nothing.** The host owns whether a room is pressurised (`C9`), so a harness that
            // never says leaves every compartment in vacuum — no air mass, no links, and a room
            // knob turning against a room that exchanges nothing. A crewed ship's compartments
            // are full, so the server's are.
            result.Rooms = Pressurise(server, 1f);

            // **And a hull with no compartment cannot judge a room knob at all** (`E8`). It is a
            // property of the size asked for rather than of the degradation — the census hull
            // grows its first sealed room somewhere under a thousand blocks — so a caller that
            // turns a room knob on a hull that has none is asking a question of a blank page, and
            // it is raised here rather than reported as a zero. Every other knob is unaffected,
            // which is why the guard is scoped to the two that are not.
            bool asksAboutRooms = how.RoomPressureError > 0f || how.RoomMapLagSeconds > 0f;
            if (asksAboutRooms && result.Rooms <= 0)
            {
                throw new InvalidOperationException(
                    "'" + how.Name + "' degrades a room on a " + blocks + "-block hull that has"
                    + " none, so it would measure nothing; ask for at least "
                    + SmallestHullWithACompartment);
            }

            float clientPressure = 1f - how.RoomPressureError;
            Pressurise(client, clientPressure);

            // Warmed together so the run starts from a hull that is somewhere, and both sides see
            // the same warm-up: what is being measured is the degradation, not the start.
            float warm = 120f;
            Drive(server, LoadedWatts);
            Drive(client, LoadedWatts);

            // A ship under way, when the scenario is one. The client's own thrust is its guess.
            bool flying = scenario == "burn";
            if (flying)
            {
                Census.DriveThrust(server, ThrustWatts);
                Census.DriveThrust(client, ThrustWatts * (1f + how.ThrustErrorShare));
            }

            Silence(client, how.BlocksOffShare);

            Advance(server, Sample(scenario, 0f, how, false), warm);
            Advance(client, Sample(scenario, 0f, how, true), warm);

            if (how.StaleSeconds > 0f)
            {
                float[] stale = Temperatures(client);
                Advance(server, Sample(scenario, 0f, how, false), how.StaleSeconds);
                Restore(client, stale);
            }

            // **The client's room map has not landed yet, when the case says so.** The mapper
            // publishes atomically, so what a client holds mid-pass is the previous map — on a
            // grid it has just built, an empty one. Applied after the warm-up rather than before
            // it, so the run starts from two hulls that agree and the disagreement is the
            // degradation rather than the warm-up.
            // **Part of the hull has not reached the client yet, when the case says so.** Applied
            // after the warm-up rather than before it, so the run starts from two hulls that agree
            // and what follows is the degradation. Taking blocks away rebuilds the map under them,
            // which is why the compartments are refilled here and why this runs before the map
            // knob rather than after it.
            List<BlockInstance> withheld = Withhold(client, how.BlocksMissingShare);
            bool blocksPending = withheld.Count > 0;
            if (blocksPending) Pressurise(client, clientPressure);

            bool mapPending = how.RoomMapLagSeconds > 0f;
            if (mapPending) Unmap(client);

            List<StoredTemperature> selection = new List<StoredTemperature>();
            List<StoredTemperature> received = new List<StoredTemperature>();
            List<float> disagreements = new List<float>();

            float tick = fix.IntervalSeconds > 0f
                ? Math.Min(SampleSeconds, fix.IntervalSeconds) : SampleSeconds;
            if (tick < serverWorld.StepSeconds) tick = serverWorld.StepSeconds;

            float elapsed = 0f;
            float sinceUpdate = float.MaxValue;
            float sinceSample = 0f;
            float sinceHitch = 0f;
            float owed = 0f;

            float servedWatts = float.NaN;
            float clientWatts = float.NaN;

            while (elapsed < seconds)
            {
                float now = elapsed;

                servedWatts = Retune(server, servedWatts, Watts(now, loadPeriodSeconds));
                float wasClientWatts = clientWatts;
                clientWatts = Retune(client, clientWatts,
                    Watts(now - how.PowerLagSeconds, loadPeriodSeconds) * (1f + how.PowerErrorShare));

                // Re-driving the hull puts the switched-off blocks back on, so they go off again.
                if (clientWatts != wasClientWatts) Silence(client, how.BlocksOffShare);

                Advance(server, Sample(scenario, now, how, false), tick);

                if (owed > 0f)
                {
                    owed -= tick;
                }
                else
                {
                    Advance(client, Sample(scenario, now - how.EnvironmentLagSeconds, how, true), tick);
                }

                elapsed += tick;

                if (mapPending && elapsed >= how.RoomMapLagSeconds)
                {
                    mapPending = false;
                    Remap(client, clientPressure);
                }

                if (blocksPending && how.BlocksMissingSeconds > 0f
                    && elapsed >= how.BlocksMissingSeconds)
                {
                    blocksPending = false;
                    Deliver(client, withheld);

                    // The arriving blocks rebuilt the map under the hull, so the state the case put
                    // on it has to be put back: the compartments' air, or the absence of a map
                    // where that is what is being degraded. Their *masses* are already wrong by the
                    // right amount — a withheld block kept the mass it was given before it left.
                    if (mapPending) Unmap(client); else Pressurise(client, clientPressure);

                    // And the new blocks are not driving anything until the hull is re-driven,
                    // which a wattage that cannot equal anything forces at the next sample.
                    clientWatts = float.NaN;
                }

                sinceUpdate += tick;
                sinceSample += tick;
                sinceHitch += tick;

                if (how.HitchEverySeconds > 0f && sinceHitch >= how.HitchEverySeconds)
                {
                    sinceHitch = 0f;
                    owed += how.HitchLosesSeconds;
                }

                if (sinceSample >= SampleSeconds)
                {
                    float worst;
                    int over;
                    int absent;
                    int disagreeing = Compare(server, client, out worst, out over, out absent);

                    if (over > result.PeakServerCritical) result.PeakServerCritical = over;
                    if (absent > result.PeakMissing) result.PeakMissing = absent;
                    disagreements.Add(worst);
                    if (worst > result.PeakKelvin) result.PeakKelvin = worst;
                    if (disagreeing > result.PeakDisagreeing) result.PeakDisagreeing = disagreeing;
                    if (disagreeing > 0) result.SecondsMisreading += sinceSample;

                    sinceSample = 0f;
                }

                if (fix.IntervalSeconds > 0f && sinceUpdate >= fix.IntervalSeconds)
                {
                    sinceUpdate = 0f;

                    int inBand = server.ExportHotTail(fix.BandKelvin, fix.MaxBlocks, selection);
                    byte[] packet = HotTailCodec.Encode(selection);

                    if (HotTailCodec.TryDecode(packet, received))
                    {
                        client.ImportHotTail(received);
                        result.BytesPerSecond += packet.Length;
                        if (selection.Count > result.PeakBlocksSent) result.PeakBlocksSent = selection.Count;
                    }

                    if (inBand < 0) return result;   // unreachable; keeps inBand read where it is set
                }
            }

            result.BytesPerSecond = elapsed > 0f ? result.BytesPerSecond / elapsed : 0f;
            result.StandingKelvin = FinalThird(disagreements);
            return result;
        }

        /// <summary>
        /// The mean of the final third of a series.
        ///
        /// A third rather than the last sample, because one reading is noise and the question is
        /// where the run *settled*.
        /// </summary>
        private static float FinalThird(IList<float> series)
        {
            if (series == null || series.Count == 0) return 0f;

            int from = series.Count - Math.Max(1, series.Count / 3);
            double total = 0d;
            for (int i = from; i < series.Count; i++) total += series[i];

            return (float)(total / (series.Count - from));
        }

        /// <summary>The scripted load at a moment: a square wave, so a lag has something to lag.</summary>
        public static float Watts(float seconds)
        {
            return Watts(seconds, LoadPeriodSeconds);
        }

        /// <summary>
        /// The same, at a chosen period.
        ///
        /// **A period longer than the run is how a caller asks for a steady load**, and one caller
        /// needs to: a heat capacity error is an error in a *rate*, and the equilibrium a hull
        /// settles at does not depend on capacity at all. Telling those two apart needs a run whose
        /// load stops moving, which the shipped period deliberately never does.
        /// </summary>
        public static float Watts(float seconds, float periodSeconds)
        {
            if (seconds < 0f) seconds = 0f;
            if (periodSeconds <= 0f) return LoadedWatts;

            int half = (int)(seconds / periodSeconds);
            return half % 2 == 0 ? LoadedWatts : IdleWatts;
        }

        /// <summary>Re-drives a hull only when its wattage actually changed.</summary>
        private static float Retune(ThermalSimulation simulation, float current, float wanted)
        {
            if (current == wanted) return current;
            Census.DriveCensus(simulation, wanted);
            return wanted;
        }

        /// <summary>
        /// The environment one side sees at a moment. The client's is built from the same function
        /// with its own lag already applied by the caller, plus whatever bias it carries.
        /// </summary>
        private static EnvironmentSample Sample(string scenario, float seconds, Degradation how,
            bool onClient)
        {
            if (seconds < 0f) seconds = 0f;

            if (scenario == "shadow") return Worlds.Shadow();

            // Flying: thick air at speed, which is the world a ship under thrust is actually in and
            // the one where a wrong thrust also drags a wrong airflow behind it.
            if (scenario == "burn")
            {
                float speed = FlyingSpeed;
                if (onClient) speed *= 1f + how.SpeedErrorShare;
                if (speed < 0f) speed = 0f;

                return Worlds.Flight(1f, speed);
            }

            if (scenario == "sunlit")
            {
                // A quarter turn every five minutes, so the sun moves across the hull and a lag or
                // an angle error is a different direction rather than the same one.
                float angle = seconds * (float)(Math.PI / 600d);
                if (onClient) angle += Degrees(how.SunAngleDegrees);

                EnvironmentSample sunlit = Worlds.Space(new Vector3(
                    (float)Math.Cos(angle), (float)Math.Sin(angle), 0.2f));

                if (onClient && Disagreeing(how, seconds))
                {
                    // The whole solar term, gone. Both fields, because the solver reads the share
                    // and the flag is what the share being one means.
                    sunlit.IsSolarOccluded = true;
                    sunlit.SolarOcclusion = 1f;
                }

                return sunlit;
            }

            // A planet day, which drives ambient and sun together out of one number.
            float dayLength = 1200f;
            float timeOfDay = 0.25f + (seconds / dayLength);
            if (onClient) timeOfDay += Degrees(how.SunAngleDegrees) / (float)(2d * Math.PI);

            float air = 1f;
            if (onClient) air = Math.Max(0f, Math.Min(1f, air - how.AirDensityError));

            return Worlds.PlanetSurface(air, timeOfDay);
        }

        /// <summary>
        /// Airspeed the `burn` scenario flies at, m/s. Above the friction threshold, so both the
        /// terms a speed error reaches are live.
        /// </summary>
        public const float FlyingSpeed = 100f;

        /// <summary>
        /// Whether the client's occlusion flag is on the wrong side at this moment.
        ///
        /// A period and a duration rather than a share, so the disagreement is a stretch the hull
        /// can cool through and then recover from — which is what a raycast resolved on the wrong
        /// tick looks like — rather than a flicker that averages out inside one step.
        /// </summary>
        private static bool Disagreeing(Degradation how, float seconds)
        {
            if (how.OcclusionWrongEverySeconds <= 0f) return false;
            if (how.OcclusionWrongForSeconds <= 0f) return false;
            if (seconds < 0f) seconds = 0f;

            float into = seconds % how.OcclusionWrongEverySeconds;
            return into < how.OcclusionWrongForSeconds;
        }

        private static float Degrees(float degrees)
        {
            return (float)(degrees * Math.PI / 180d);
        }

        /// <summary>
        /// Scales every block's mass by <paramref name="share"/>, and with it every block's heat
        /// capacity.
        ///
        /// Every block rather than a sample of them, because the rota that produces this in game
        /// visits every block: what differs between two machines is *when* each was last visited,
        /// and a share applied to the whole hull is the bound on that.
        /// </summary>
        private static void Reweigh(ThermalSimulation simulation, float share)
        {
            if (share == 0f) return;

            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            float scale = Math.Max(0.01f, 1f + share);

            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i].Block.Mass = nodes[i].Block.Mass * scale;
                nodes[i].RefreshThermalMass();
            }
        }

        /// <summary>
        /// Zeroes the heat of <paramref name="share"/> of the hull's producers, as a block the
        /// client believes is switched off makes none.
        ///
        /// <para>
        /// The selection is every n-th producer rather than a random one, so a run is repeatable
        /// and two runs at the same share silence the same blocks. Both terms go: a block that is
        /// off is neither drawing nor thrusting.
        /// </para>
        /// </summary>
        private static void Silence(ThermalSimulation simulation, float share)
        {
            if (share <= 0f) return;

            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            int period = share >= 1f ? 1 : (int)Math.Round(1f / share);
            if (period <= 0) period = 1;

            int producer = 0;

            for (int i = 0; i < nodes.Count; i++)
            {
                if (!Census.IsProducer(nodes[i])) continue;

                if (producer++ % period == 0)
                {
                    nodes[i].Block.PowerProducedWatts = 0f;
                    nodes[i].Block.ThrustWatts = 0f;
                    nodes[i].RefreshHeatGeneration();
                }
            }
        }

        /// <summary>
        /// Fills every sealed compartment on a hull to <paramref name="level"/>, 0..1, and reports
        /// how many took it.
        ///
        /// The count is returned rather than discarded because a hull with no compartment is a rig
        /// that reports a room degradation as harmless (`E8`), and the caller raises that.
        /// </summary>
        private static int Pressurise(ThermalSimulation simulation, float level)
        {
            if (level < 0f) level = 0f;
            if (level > 1f) level = 1f;

            IList<RoomAirNode> air = simulation.RoomAir;
            int filled = 0;

            for (int i = 0; i < air.Count; i++)
            {
                if (simulation.SetRoomPressure(air[i].Anchor, level)) filled++;
            }

            return filled;
        }

        /// <summary>
        /// Puts a hull back to what it looks like before its first room pass completes: no rooms,
        /// so no air and every interior face open to the environment.
        ///
        /// Both halves, because a room that does not exist cannot hold air either. Recomputing
        /// exposure against an empty map is what the mod itself does on a grid whose mapper has
        /// published nothing.
        /// </summary>
        private static void Unmap(ThermalSimulation simulation)
        {
            Pressurise(simulation, 0f);
            simulation.Solver.RefreshExposure(new RoomMap());
        }

        /// <summary>The pass lands: the map is adopted and the compartments refill.</summary>
        private static void Remap(ThermalSimulation simulation, float level)
        {
            simulation.Solver.RefreshExposure(simulation.Rooms.Map);
            Pressurise(simulation, level);
        }

        private static void Drive(ThermalSimulation simulation, float watts)
        {
            Census.DriveCensus(simulation, watts);
        }

        private static void Advance(ThermalSimulation simulation, EnvironmentSample environment,
            float seconds)
        {
            int steps = (int)Math.Round(seconds / simulation.Settings.StepSeconds);
            for (int i = 0; i < steps; i++) simulation.StepExact(1, environment);
        }

        private static float[] Temperatures(ThermalSimulation simulation)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            float[] values = new float[nodes.Count];
            for (int i = 0; i < nodes.Count; i++) values[i] = nodes[i].Temperature;
            return values;
        }

        private static void Restore(ThermalSimulation simulation, float[] temperatures)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            int count = Math.Min(nodes.Count, temperatures.Length);
            for (int i = 0; i < count; i++) nodes[i].Temperature = temperatures[i];
        }

        /// <summary>
        /// Blocks on opposite sides of critical, and the worst disagreement in kelvin.
        ///
        /// <para>
        /// **Matched by position rather than by index**, and that is not a refinement — it is the
        /// only comparison that means anything once the two machines can disagree about *which*
        /// blocks there are. A node's index is its arrival order, so an index-matched comparison of
        /// two hulls built in different orders reads a large disagreement between blocks that are
        /// not the same block, and of a hull missing a block it reads every index after the gap
        /// against its neighbour. It is a no-op on two hulls built identically, which is every row
        /// but two ([backlog.md](../../docs/backlog.md) `F22`).
        /// </para>
        ///
        /// <para>
        /// A block the client does not have is counted in <paramref name="missing"/> rather than
        /// scored: the client is not wrong about its temperature, it has no opinion at all, and a
        /// readout cannot misread a block it is not drawing. That count is what the row reports
        /// instead.
        /// </para>
        /// </summary>
        private static int Compare(ThermalSimulation server, ThermalSimulation client,
            out float worstKelvin, out int serverOverCritical, out int missing)
        {
            IList<ThermalNode> mine = server.Solver.Nodes;

            worstKelvin = 0f;
            serverOverCritical = 0;
            missing = 0;
            int disagreeing = 0;

            for (int i = 0; i < mine.Count; i++)
            {
                ThermalNode ours = mine[i];
                float critical = ours.Thermal.CriticalTemperature;
                bool over = critical > 0f && ours.Temperature > critical;
                if (over) serverOverCritical++;

                ThermalNode yours = client.Solver.GetNodeAt(ours.Block.Position);
                if (yours == null)
                {
                    missing++;
                    continue;
                }

                float difference = Math.Abs(ours.Temperature - yours.Temperature);
                if (difference > worstKelvin) worstKelvin = difference;

                if (critical <= 0f) continue;
                if (over != yours.Temperature > critical) disagreeing++;
            }

            return disagreeing;
        }

        /// <summary>
        /// Copies every temperature the server holds onto the client's block at the same *cell*.
        ///
        /// Position rather than index, for the reason <see cref="Compare"/> is: two hulls built in
        /// different orders hold the same ship at different indices, and a by-index copy would give
        /// the client a scrambled temperature field before the run started.
        /// </summary>
        private static void Align(ThermalSimulation client, ThermalSimulation server)
        {
            IList<ThermalNode> mine = server.Solver.Nodes;

            for (int i = 0; i < mine.Count; i++)
            {
                ThermalNode yours = client.Solver.GetNodeAt(mine[i].Block.Position);
                if (yours != null) yours.Temperature = mine[i].Temperature;
            }
        }

        /// <summary>
        /// Takes <paramref name="share"/> of a hull's blocks away, as blocks a client has not been
        /// told about yet, and hands back what was removed so it can arrive later.
        ///
        /// <para>
        /// Every n-th block rather than a random draw, so a run is repeatable and two runs at one
        /// share take the same blocks. **Producers are taken too**: a subgrid that has not attached
        /// is a thruster pod or a reactor bay, not a spread of armour, and taking only structure
        /// would make the knob a surface-area change with no heat behind it.
        /// </para>
        /// </summary>
        private static List<BlockInstance> Withhold(ThermalSimulation simulation, float share)
        {
            List<BlockInstance> held = new List<BlockInstance>();
            if (share <= 0f) return held;

            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            int period = share >= 1f ? 1 : (int)Math.Round(1f / share);
            if (period <= 0) period = 1;

            for (int i = 0; i < nodes.Count; i++)
            {
                if (i % period == 0) held.Add(nodes[i].Block);
            }

            // The hull is left with at least something, or the run is comparing a ship with an
            // empty grid and every column reads the whole hull (`E8`).
            if (held.Count >= nodes.Count) held.RemoveAt(held.Count - 1);

            for (int i = 0; i < held.Count; i++) simulation.RemoveBlock(held[i]);

            simulation.RebuildAll();
            return held;
        }

        /// <summary>
        /// The withheld blocks arrive.
        ///
        /// They come in at the world's default temperature, which is what a block appearing on a
        /// grid gets: the simulation has no history for it and nothing tells it what the neighbours
        /// are holding. That is a second, smaller wrongness riding on the first, and it is the
        /// engine's rather than the rig's.
        /// </summary>
        private static void Deliver(ThermalSimulation simulation, List<BlockInstance> held)
        {
            if (held == null || held.Count == 0) return;

            for (int i = 0; i < held.Count; i++) simulation.AddBlock(held[i]);

            simulation.RebuildAll();
            held.Clear();
        }

        /// <summary>
        /// The standing set of degradations: each input alone, then all of them together.
        ///
        /// **The combined case is the union of the others and not a separate opinion.** Building it
        /// by hand would let it quietly become the case that makes the correction look best.
        /// </summary>
        public static List<Degradation> All()
        {
            List<Degradation> cases = new List<Degradation>
            {
                new Degradation
                {
                    Name = "none",
                    Because = "the control: two identical clients, which must agree exactly",
                },
                new Degradation
                {
                    Name = "stale join",
                    Because = "joined holding the server's state from 60 s ago, as a save does",
                    StaleSeconds = 60f,
                },
                new Degradation
                {
                    Name = "hitching",
                    Because = "drops its solver backlog every 30 s, as StepsDue does on a long frame",
                    HitchEverySeconds = 30f,
                    HitchLosesSeconds = 5f,
                },
                new Degradation
                {
                    Name = "environment lag",
                    Because = "samples the environment the server had 2 s ago",
                    EnvironmentLagSeconds = 2f,
                },
                new Degradation
                {
                    Name = "sun angle",
                    Because = "its replicated orientation puts the sun 5 degrees off",
                    SunAngleDegrees = 5f,
                },
                new Degradation
                {
                    Name = "power lag",
                    Because = "block power reaches it 2 s late, so a throttle change arrives late",
                    PowerLagSeconds = 2f,
                },
                new Degradation
                {
                    Name = "power error",
                    Because = "block power arrives 5 % out, standing in for what replication rounds",
                    PowerErrorShare = 0.05f,
                },
                new Degradation
                {
                    Name = "thrust error",
                    Because = "the engine predicts thrust rather than sending it, so its 20 % is a guess",
                    ThrustErrorShare = 0.2f,
                },
                new Degradation
                {
                    Name = "speed error",
                    Because = "its predicted velocity is 20 % out, which is 1.7x the friction heat",
                    SpeedErrorShare = 0.2f,
                },
                new Degradation
                {
                    Name = "wrong shadow",
                    Because = "its own raycast puts the hull in shade for 3 s of every 30, in full sun",
                    OcclusionWrongEverySeconds = 30f,
                    OcclusionWrongForSeconds = 3f,
                },
                new Degradation
                {
                    Name = "mass error",
                    Because = "its mass rota is 20 % behind the server's, so its capacities are wrong",
                    MassErrorShare = 0.2f,
                },
                new Degradation
                {
                    Name = "blocks off",
                    Because = "a tenth of its producers are on the wrong side of their own switch",
                    BlocksOffShare = 0.1f,
                },
                new Degradation
                {
                    Name = "thinner air",
                    Because = "its gas system says the hull is in 20 % less air than the server's",
                    AirDensityError = 0.2f,
                },
                new Degradation
                {
                    Name = "room pressure",
                    Because = "its gas system reports the compartments a fifth emptier than the server's",
                    RoomPressureError = 0.2f,
                },
                new Degradation
                {
                    Name = "room map lag",
                    Because = "its flood fill has not landed for 60 s, so it has no rooms at all yet",
                    RoomMapLagSeconds = 60f,
                },
                new Degradation
                {
                    Name = "build order",
                    Because = "received the same blocks in a different order, which is no difference at all",
                    BuildOrderSeed = 20260824,
                },
                new Degradation
                {
                    Name = "blocks missing",
                    Because = "a tenth of the hull has not reached it for 60 s, as a subgrid attaching late",
                    BlocksMissingShare = 0.1f,
                    BlocksMissingSeconds = 60f,
                },
                new Degradation
                {
                    Name = "wrong settings",
                    Because = "never received the world's settings, so it is running other physics",
                    OnDefaultSettings = true,
                },
            };

            Degradation everything = new Degradation
            {
                Name = "all at once",
                Because = "the union of every case above, which is what a bad client is",
            };

            for (int i = 0; i < cases.Count; i++)
            {
                Degradation one = cases[i];
                if (one.StaleSeconds > everything.StaleSeconds) everything.StaleSeconds = one.StaleSeconds;
                if (one.HitchEverySeconds > 0f) everything.HitchEverySeconds = one.HitchEverySeconds;
                if (one.HitchLosesSeconds > everything.HitchLosesSeconds) everything.HitchLosesSeconds = one.HitchLosesSeconds;
                if (one.EnvironmentLagSeconds > everything.EnvironmentLagSeconds) everything.EnvironmentLagSeconds = one.EnvironmentLagSeconds;
                if (one.SunAngleDegrees > everything.SunAngleDegrees) everything.SunAngleDegrees = one.SunAngleDegrees;
                if (one.ThrustErrorShare > everything.ThrustErrorShare) everything.ThrustErrorShare = one.ThrustErrorShare;
                if (one.SpeedErrorShare > everything.SpeedErrorShare) everything.SpeedErrorShare = one.SpeedErrorShare;
                if (one.OcclusionWrongEverySeconds > 0f)
                {
                    everything.OcclusionWrongEverySeconds = one.OcclusionWrongEverySeconds;
                    everything.OcclusionWrongForSeconds = one.OcclusionWrongForSeconds;
                }
                if (one.MassErrorShare > everything.MassErrorShare) everything.MassErrorShare = one.MassErrorShare;
                if (one.BlocksOffShare > everything.BlocksOffShare) everything.BlocksOffShare = one.BlocksOffShare;
                if (one.PowerLagSeconds > everything.PowerLagSeconds) everything.PowerLagSeconds = one.PowerLagSeconds;
                if (one.PowerErrorShare > everything.PowerErrorShare) everything.PowerErrorShare = one.PowerErrorShare;
                if (one.AirDensityError > everything.AirDensityError) everything.AirDensityError = one.AirDensityError;
                if (one.RoomPressureError > everything.RoomPressureError) everything.RoomPressureError = one.RoomPressureError;
                if (one.RoomMapLagSeconds > everything.RoomMapLagSeconds) everything.RoomMapLagSeconds = one.RoomMapLagSeconds;
                if (one.BuildOrderSeed != 0) everything.BuildOrderSeed = one.BuildOrderSeed;
                if (one.BlocksMissingShare > everything.BlocksMissingShare)
                {
                    everything.BlocksMissingShare = one.BlocksMissingShare;
                    everything.BlocksMissingSeconds = one.BlocksMissingSeconds;
                }
                if (one.OnDefaultSettings) everything.OnDefaultSettings = true;
            }

            cases.Add(everything);
            return cases;
        }

        /// <summary>The sweep as a table.</summary>
        public static string Report(IList<Result> results)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("Each input a client drives its own simulation from, degraded, with the");
            text.AppendLine("correction off and on.");
            text.AppendLine();

            // The two room rows are worth what the hull's compartments are worth, so the count is
            // printed rather than assumed: a hull with none cannot be asked about a room at all,
            // and the lab raises rather than reaching this table with a zero (E8).
            int compartments = results.Count > 0 ? results[0].Rooms : 0;
            text.AppendLine("Sealed compartments on the hull, holding air on the server: "
                + compartments.ToString("n0"));
            text.AppendLine();
            text.AppendLine("degradation         fix      peak K   standing K   misreading    worst  absent   B/s");

            int judged = 0;

            for (int i = 0; i < results.Count; i++)
            {
                Result result = results[i];
                bool off = result.Protocol == null || result.Protocol.IntervalSeconds <= 0f;
                bool anythingFailed = result.PeakServerCritical > 0;
                if (anythingFailed) judged++;

                text.AppendLine(string.Format(
                    "{0,-20}{1,-9}{2,8}{3,13}{4,13}{5,9}{6,8}{7,6}",
                    result.Name,
                    off ? "off" : result.Protocol.IntervalSeconds.ToString("n0") + " s"
                        + (result.Protocol.WholeHullOnJoin ? "+j" : ""),
                    result.PeakKelvin.ToString("n1"),
                    result.StandingKelvin.ToString("n2"),
                    anythingFailed ? result.SecondsMisreading.ToString("n0") + " s" : "nothing hot",
                    anythingFailed ? result.PeakDisagreeing.ToString("n0") : "-",
                    result.PeakMissing.ToString("n0"),
                    result.BytesPerSecond.ToString("n0")));
            }

            if (judged < results.Count)
            {
                text.AppendLine();
                text.AppendLine("**" + (results.Count - judged) + " of " + results.Count
                    + " rows had no block past critical on the server**, so their readout columns");
                text.AppendLine("judged nothing and are printed as 'nothing hot' rather than as zero (E8). The");
                text.AppendLine("kelvin columns are still real; the readout columns are not.");
            }

            text.AppendLine();
            text.AppendLine("standing K is the mean disagreement over the final third of the run, and it is the");
            text.AppendLine("column that separates a perturbation from a bias: a one-off wrong state decays to");
            text.AppendLine("nothing whatever it peaked at, and a wrong input settles somewhere and stays. worst");
            text.AppendLine("is the largest number of blocks the two put on opposite sides of critical at once,");
            text.AppendLine("and absent is the largest number the server had that the client did not — a block a");
            text.AppendLine("client has not been told about is not one it is wrong about, so it is counted rather");
            text.AppendLine("than scored.");
            text.AppendLine();
            text.AppendLine("Every magnitude here is a knob this lab turns rather than a figure measured from a");
            text.AppendLine("session; what the table answers is which inputs bias, which perturb, and whether");
            text.AppendLine("the correction reaches each.");

            return text.ToString();
        }
    }
}
