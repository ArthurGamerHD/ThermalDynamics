using System;
using System.Collections.Generic;
using System.Reflection;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **Every setting the solver carries reaches the solver.**
    ///
    /// <para>
    /// <see cref="DialReachTests"/> makes this check for the eighteen dials `KnobLab` sweeps, and
    /// thirteen of those are *material* dials rather than world settings — so of the settings a
    /// player can move, five had a reach check and the rest had none. `C33` counts the measurement
    /// gap and this closes the silent half of it: a curve that comes back flat reads as *this dial
    /// does not matter* and is indistinguishable from *this dial is not wired to anything*, and
    /// this repository has been caught by that shape four times.
    /// </para>
    ///
    /// <para>
    /// **Enumerated rather than listed**, exactly as <see cref="LoopDialReachTests"/> is, because a
    /// list is what fails: a field added to <see cref="ThermalSettings"/> tomorrow is checked
    /// tomorrow without anyone remembering this file exists. <see cref="SettingsWiringTests"/>
    /// covers the plumbing either side of it — a proto number, a name, a getter, a setter, a clamp,
    /// a documentation row — and every one of those can be right while the value reaches nothing.
    /// </para>
    ///
    /// <para>
    /// **It is a reach test.** It asserts a setting changes an outcome and says nothing about the
    /// direction or the size, which is what lets it survive a retune untouched. What it cannot do
    /// is speak for a setting whose whole effect is outside the core — a debug overlay, the HUD, the
    /// telemetry stride — and those are not on <see cref="ThermalSettings"/> to begin with.
    /// </para>
    /// </summary>
    [Trait("speed", "slow")]
    public class SettingsDialReachTests
    {
        private readonly ITestOutputHelper output;

        public SettingsDialReachTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        /// <summary>
        /// Settings that are a ceiling on work rather than a quantity. A ceiling above what a rig
        /// demands is a ceiling that does nothing, so these are swept to one and to zero — the
        /// smallest each has, and the "no cap" value two of them carry — rather than scaled.
        /// </summary>
        private static readonly HashSet<string> Caps = new HashSet<string>(StringComparer.Ordinal)
        {
            "MaxSubsteps", "MaxElementVisitsPerStep", "MaxSubstepsPerBlock",
        };

        /// <summary>
        /// Settings on <see cref="ThermalSettings"/> that no rig here can move a reading with, each
        /// with the reason.
        ///
        /// <para>
        /// **The list is the point of the test rather than a way around it.** Every entry is a
        /// statement that can be wrong, and an entry that stops being true fails the test from the
        /// other side — the check below asserts an exempt setting really is inert, so a setting that
        /// gains an effect is reported rather than quietly excused.
        /// </para>
        /// </summary>
        private static readonly Dictionary<string, string> Exempt =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Version", "the config's own version number, not a dial" },

            // Derived rather than set: Frequency and SimulationSpeed produce them, and this file
            // sweeps those two.
            { "StepSeconds", "derived from Frequency and SimulationSpeed" },
            { "StepsPerSecond", "derived from Frequency and SimulationSpeed" },
            { "Revision", "a change counter the host reads, not an input" },
        };

        /// <summary>
        /// Every rig a setting might be reachable in, sampled at one set of settings. Concatenated
        /// into one vector, so a setting that moves any reading in any rig is reached.
        /// </summary>
        private static List<float> Fingerprint(Action<ThermalSettings> apply)
        {
            List<float> readings = new List<float>();

            BuriedSource(apply, readings);
            OnAPlanet(apply, readings);
            InTheSun(apply, readings);
            NearAStar(apply, readings);
            Flying(apply, readings);
            Plumbed(apply, readings);
            Pumped(apply, readings);
            Sealed(apply, readings);
            Cooking(apply, readings);
            Starved(apply, readings);
            OverBudget(apply, readings);
            Paced(apply, readings);
            Suit(apply, readings);

            return readings;
        }

        private static ThermalSettings Settings(Action<ThermalSettings> apply)
        {
            ThermalSettings settings = new ThermalSettings();
            if (apply != null) apply(settings);
            return settings.Derive();
        }

        /// <summary>Temperatures, extremes and cost, which is what every rig below reports.</summary>
        private static void Read(ThermalSimulation simulation, List<float> into)
        {
            float hottest = 0f;
            float coldest = float.MaxValue;
            float total = 0f;
            int over = 0;

            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                float t = nodes[i].Temperature;
                if (t > hottest) hottest = t;
                if (t < coldest) coldest = t;
                total += t;
                if (t > nodes[i].Thermal.CriticalTemperature) over++;
            }

            into.Add(hottest);
            into.Add(coldest == float.MaxValue ? 0f : coldest);
            into.Add(total);
            into.Add(over);
            into.Add(simulation.Solver.RequiredSubsteps(0.25f));
            into.Add(simulation.Solver.LastSubsteps);

            // Damage, so a dial that destroys rather than heats lands somewhere. Read off the
            // solver's own overheat record rather than off the blocks, which carry no health here.
            IList<OverheatEvent> overheats = simulation.Solver.Overheats;
            float damage = 0f;
            for (int i = 0; overheats != null && i < overheats.Count; i++)
            {
                damage += overheats[i].Damage;
            }
            into.Add(damage);
            into.Add(overheats == null ? 0f : overheats.Count);
        }

        /// <summary>A source buried in armour, in shadow: conduction, waste heat, damage, budget.</summary>
        private static void BuriedSource(Action<ThermalSettings> apply, List<float> into)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(5, 5, 5));
            builder.Remove(new Vector3I(2, 2, 2));
            builder.Place(Catalog.Reactor(), new Vector3I(2, 2, 2)).Wasting(400000f);

            ThermalSimulation simulation = builder.BuildSimulation(Settings(apply), 293.15f);
            simulation.StepExact(LabClock.Steps(400), Worlds.Shadow());
            Read(simulation, into);
        }

        /// <summary>The same hull on a planet at noon: solar, planets, convection, wind, radiation.</summary>
        private static void OnAPlanet(Action<ThermalSettings> apply, List<float> into)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(5, 5, 5));
            builder.Remove(new Vector3I(0, 2, 2));
            builder.Place(Catalog.Reactor(), new Vector3I(0, 2, 2)).Wasting(400000f);

            ThermalSimulation simulation = builder.BuildSimulation(Settings(apply), 293.15f);
            simulation.StepExact(LabClock.Steps(400), Worlds.PlanetSurface(1f, 0.5f, 22f));
            Read(simulation, into);
        }

        /// <summary>
        /// An L of armour in raking sunlight in vacuum. **The shape and the angle are the point**:
        /// a symmetrical hull under a sun straight overhead shadows itself the same however the
        /// question is answered, so a rig like that reports `SolarSelfShadowing` inert.
        /// </summary>
        private static void InTheSun(Action<ThermalSettings> apply, List<float> into)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(6, 1, 2));
            builder.Fill(Catalog.LightArmor(), new Vector3I(0, 0, 2), new Vector3I(2, 5, 6));

            ThermalSimulation simulation = builder.BuildSimulation(Settings(apply), 200f);
            simulation.StepExact(LabClock.Steps(300),
                Worlds.Space(new Vector3(0.7f, 0.5f, 0.5f)));

            Read(simulation, into);
        }

        /// <summary>
        /// The same hull beside a point source in the world — the term `EnableHeatSources` gates,
        /// and the only one it gates. It is an environment entry rather than a block, so no rig
        /// built out of blocks alone can see the switch.
        /// </summary>
        private static void NearAStar(Action<ThermalSettings> apply, List<float> into)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(3, 3, 3));

            EnvironmentSample sample = Worlds.Shadow();
            sample.HeatSources = new HeatSourceState[]
            {
                new HeatSourceState(Vector3.Normalize(new Vector3(1f, 0.2f, 0.1f)), 4000f),
            };
            sample.HeatSourceCount = 1;

            ThermalSimulation simulation = builder.BuildSimulation(Settings(apply), 200f);
            simulation.StepExact(LabClock.Steps(300), sample);

            Read(simulation, into);
        }

        /// <summary>
        /// **A block cooking itself past its own rating, integrated coarsely.** Damage only happens
        /// above critical and the overshoot clamps only bind when a substep would overshoot, so
        /// every rig that settles somewhere sensible reports five dials inert. This one is built to
        /// be a hard problem: a small, light, hot source against a cold hull, and the block cap
        /// deliberately left where the caller put it.
        /// </summary>
        private static void Cooking(Action<ThermalSettings> apply, List<float> into)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(4, 4, 4));
            builder.Remove(new Vector3I(1, 1, 1));
            builder.Place(Catalog.Reactor(), new Vector3I(1, 1, 1)).Wasting(40000000f);

            ThermalSimulation simulation = builder.BuildSimulation(Settings(apply), 293.15f);
            simulation.StepExact(LabClock.Steps(200), Worlds.Shadow());

            Read(simulation, into);
        }

        /// <summary>
        /// The same hull driven by **frames** rather than by a step count, for a fixed run of real
        /// time. `SimulationSpeed` scales simulated time against real time, so a rig that asks for
        /// a number of steps gets the same number whatever it is set to — the dial is invisible to
        /// every other rig here by construction rather than by accident.
        /// </summary>
        private static void Paced(Action<ThermalSettings> apply, List<float> into)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(4, 4, 4));
            builder.Remove(new Vector3I(1, 1, 1));
            builder.Place(Catalog.Reactor(), new Vector3I(1, 1, 1)).Wasting(400000f);

            ThermalSimulation simulation = builder.BuildSimulation(Settings(apply), 293.15f);

            EnvironmentSample sample = Worlds.Shadow();
            for (int frame = 0; frame < 1800; frame++) simulation.Update(1f / 60f, sample);

            Read(simulation, into);
            into.Add(simulation.StepsCompleted);
        }

        /// <summary>
        /// **A hot grid integrated far too coarsely and given far too small a work budget.**
        ///
        /// <para>
        /// The two overshoot clamps bind when a substep would carry a link past equilibrium, which
        /// only happens on a step too coarse to resolve its stiffest element. A rig that integrates
        /// properly reaches neither, which is why every other rig here reported both inert.
        /// </para>
        ///
        /// <para>
        /// **The work budget is deliberately left uncapped.** A tight one shortens the step, which
        /// shortens the substep, which is exactly what stops the clamp binding — so the first
        /// version of this rig starved the grid two different ways and reported the clamps inert
        /// for the second reason while trying to reach them with the first.
        /// </para>
        ///
        /// <para>
        /// **The substep cap is fixed after the caller has set it**, so the starvation is the rig's
        /// rather than the sweep's; the cap is still swept, by every other rig.
        /// </para>
        /// </summary>
        private static void Starved(Action<ThermalSettings> apply, List<float> into)
        {
            ThermalSettings settings = new ThermalSettings();
            if (apply != null) apply(settings);

            settings.MaxSubsteps = 2;
            settings.MaxElementVisitsPerStep = 0;
            settings.Derive();

            // **A census hull rather than a cube of armour.** The clamps bind on a stiff *tail* —
            // a light block bolted to a heavy one — and a hull of one material has no tail however
            // hot it is, which is why a uniform rig reports both of them inert. This is the shape
            // `ConductionClampGateTests` uses for the same reason, at a size a reach test can
            // afford to run eighty times.
            ThermalSimulation simulation = Hulls.Driven(settings, 300);
            simulation.StepExact(40, Worlds.Ab.MildAtmosphere());

            Read(simulation, into);
            into.Add(simulation.Solver.ConductionClampLive ? 1f : 0f);
        }

        /// <summary>
        /// **The same hull with a work budget far under what it asks for**, which is the only state
        /// `FloorBlocksWhenOverBudget` exists in: the floor engages per grid and per step, where the
        /// element allowance binds, and nowhere else.
        /// </summary>
        private static void OverBudget(Action<ThermalSettings> apply, List<float> into)
        {
            ThermalSettings settings = new ThermalSettings();
            if (apply != null) apply(settings);

            settings.MaxElementVisitsPerStep = 512;
            settings.Derive();

            ThermalSimulation simulation = Hulls.Driven(settings, 300);
            simulation.StepExact(40, Worlds.Ab.MildAtmosphere());

            Read(simulation, into);
            into.Add(simulation.Solver.AdaptiveSubstepFloor);
            into.Add(simulation.Solver.LastSubsteps);
        }

        /// <summary>A grid moving fast in air: friction, and the threshold under it.</summary>
        private static void Flying(Action<ThermalSettings> apply, List<float> into)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(5, 5, 5));

            ThermalSimulation simulation = builder.BuildSimulation(Settings(apply), 293.15f);
            simulation.StepExact(LabClock.Steps(200), Worlds.Flight(1f, 400f));
            Read(simulation, into);
        }

        /// <summary>A ring with a sink face on a source: the coolant path and its well-mixed model.</summary>
        private static void Plumbed(Action<ThermalSettings> apply, List<float> into)
        {
            GridBuilder builder = GridBuilder.Large();

            List<Vector3I> cells = PipeFitter.RectangleXZ(Vector3I.Zero, 5, 5);
            Dictionary<int, Vector3I> sinks = new Dictionary<int, Vector3I>();
            sinks[1] = Vector3I.Down;
            PipeFitter.BuildRing(builder, cells, 5, sinks);
            builder.Place(Catalog.Reactor(), cells[1] + Vector3I.Down).Wasting(125000f);

            ThermalSimulation simulation = builder.BuildSimulation(Settings(apply), 300f);
            simulation.StepExact(LabClock.Steps(400), Worlds.Shadow());

            Read(simulation, into);

            IList<CoolantLoop> loops = simulation.Solver.Loops;
            if (loops == null || loops.Count == 0) { into.Add(0f); into.Add(0f); return; }

            into.Add(loops[0].HottestSegment);
            into.Add(loops[0].ColdestSegment);
        }

        /// <summary>Armour, a heat pump and armour: the two heat-pump dials and nothing else.</summary>
        private static void Pumped(Action<ThermalSettings> apply, List<float> into)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), new Vector3I(0, 0, -1));
            builder.Place(Catalog.HeatPump(), Vector3I.Zero);
            builder.Place(Catalog.LightArmor(), new Vector3I(0, 0, 1));

            ThermalSimulation simulation = builder.BuildSimulation(Settings(apply), 300f);

            HeatPumpDevice pump = simulation.HeatPumps.Count == 0 ? null : simulation.HeatPumps[0];
            if (pump == null) { into.Add(0f); into.Add(0f); into.Add(0f); return; }

            pump.Enabled = true;

            // **A small lift, because that is where the coefficient cap binds.** Carnot rises
            // without bound as the two sides converge, so `HeatPumpMaxCoefficient` is the only
            // thing holding it — and a rig pumping across a hundred kelvin never reaches the cap
            // and reports it inert.
            simulation.Solver.GetNodeAt(new Vector3I(0, 0, 1)).Temperature = 302f;

            simulation.StepExact(LabClock.Steps(20), Worlds.Shadow());
            into.Add(pump.LastLiftedWatts);
            into.Add(pump.LastPowerWatts);

            simulation.StepExact(LabClock.Steps(180), Worlds.Shadow());
            into.Add(pump.LastLiftedWatts);
            into.Add(pump.LastPowerWatts);
            Read(simulation, into);
        }

        /// <summary>A sealed box with a hot wall: the room air path.</summary>
        private static void Sealed(Action<ThermalSettings> apply, List<float> into)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(5, 5, 5));
            builder.Remove(new Vector3I(0, 2, 2));
            builder.Place(Catalog.Reactor(), new Vector3I(0, 2, 2)).Wasting(120000f);

            ThermalSimulation simulation = builder.BuildSimulation(Settings(apply), 293.15f);

            // The host owns pressure, and `SetRoomPressure` is the door it comes through: writing
            // the node directly leaves the air's links unbuilt, so the rig would carry air that
            // exchanges with nothing and report both air dials inert.
            simulation.SetRoomPressure(new Vector3I(2, 2, 2), 1f);

            simulation.StepExact(LabClock.Steps(400), Worlds.Shadow());

            Read(simulation, into);

            IList<RoomAirNode> air = simulation.RoomAir;
            into.Add(air.Count == 0 ? 0f : air[0].Temperature);
            into.Add(air.Count == 0 ? 0f : air[0].ThermalMass);
        }

        /// <summary>A player in a hot room, which is the only thing the suit dials touch.</summary>
        private static void Suit(Action<ThermalSettings> apply, List<float> into)
        {
            ThermalSettings settings = Settings(apply);

            // Hot enough that the suit loses — the regulator saturates, the interior climbs past
            // critical and damage accrues — because a suit that keeps up reports the capacity and
            // the damage rate as inert. The trajectory is sampled rather than only its end: a heat
            // capacity changes how fast a suit gets somewhere and not where it ends up.
            float interior = SuitThermal.ComfortKelvin;
            float damage = 0f;

            for (int i = 0; i < 400; i++)
            {
                SuitStepResult step = SuitThermal.Step(settings, interior, 900f, false, true, 1f);
                interior = step.InteriorKelvin;
                damage += step.Damage;

                if (i % 40 == 0)
                {
                    into.Add(interior);
                    into.Add(step.RegulatedWatts);
                    into.Add(damage);
                }
            }

            into.Add(interior);
            into.Add(damage);
            into.Add(SuitThermal.SurvivableKelvin(settings));
            into.Add(SuitThermal.SurvivableKelvin(settings, true));
        }

        /// <summary>
        /// The levels a field is tried at. Both directions where the shipped value allows it,
        /// because a dial that saturates one way is still connected the other; a bool is simply
        /// inverted, and an int is moved far enough that rounding cannot swallow it.
        /// </summary>
        private static object[] Levels(FieldInfo field, ThermalSettings shipped)
        {
            if (field.FieldType == typeof(bool))
            {
                return new object[] { !(bool)field.GetValue(shipped) };
            }

            if (field.FieldType == typeof(int))
            {
                // **A cap is swept to a level that binds, not to a quarter of itself.** The three
                // budget dials ship far above what any rig here demands — 4,000,000 element visits
                // against a few thousand — so quartering them changes nothing and reports a
                // perfectly live dial as inert. One is the smallest value each of them has, and
                // zero is "no cap" on two, so both ends are tried.
                if (Caps.Contains(field.Name)) return new object[] { 1, 0 };

                int value = (int)field.GetValue(shipped);
                if (value == 0) return new object[] { 1, 8 };
                return new object[] { Math.Max(1, value / 4), value * 4 };
            }

            // **The sky's own temperature is swept across its range rather than around its value.**
            // 2.7 K against 10.8 K is a radiative sink that differs in the fourth decimal of a
            // 300 K hull — the dial reads inert not because it is unwired but because the arithmetic
            // it is in cannot see a number that small. Its slider goes to 300.
            if (field.Name == "VacuumTemperature") return new object[] { 1f, 250f };

            float number = (float)field.GetValue(shipped);
            if (number == 0f) return new object[] { 1f, 10f };
            return new object[] { number * 0.25f, number * 4f };
        }

        private static bool Same(List<float> a, List<float> b)
        {
            if (a.Count != b.Count) return false;

            for (int i = 0; i < a.Count; i++)
            {
                float scale = Math.Max(Math.Abs(a[i]), Math.Abs(b[i]));
                float tolerance = scale < 1f ? 1e-4f : scale * 1e-5f;
                if (Math.Abs(a[i] - b[i]) > tolerance) return false;
            }

            return true;
        }

        /// <summary>
        /// **Every settable field on <see cref="ThermalSettings"/> changes something the solver
        /// computes, or is named above as one that cannot.**
        /// </summary>
        [Fact]
        public void EverySettingReachesTheSimulation()
        {
            List<FieldInfo> fields = new List<FieldInfo>();

            foreach (FieldInfo field in typeof(ThermalSettings)
                .GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                Type type = field.FieldType;
                if (type != typeof(bool) && type != typeof(int) && type != typeof(float)) continue;
                fields.Add(field);
            }

            Assert.True(fields.Count >= 30,
                "only " + fields.Count + " settings were found on ThermalSettings, so this test"
                + " would pass on a class that had lost most of them");

            List<float> shipped = Fingerprint(null);

            Assert.True(shipped.Count > 40,
                "the rigs produced only " + shipped.Count + " readings between them");

            List<string> inert = new List<string>();
            List<string> unexpectedlyLive = new List<string>();
            int judged = 0;

            foreach (FieldInfo field in fields)
            {
                bool moved = false;

                foreach (object level in Levels(field, new ThermalSettings()))
                {
                    FieldInfo captured = field;
                    object value = level;

                    List<float> readings = Fingerprint(s => captured.SetValue(s, value));
                    judged++;

                    if (Same(shipped, readings)) continue;
                    moved = true;
                    break;
                }

                bool exempt = Exempt.ContainsKey(field.Name);

                if (!moved && !exempt) inert.Add(field.Name);
                if (moved && exempt) unexpectedlyLive.Add(field.Name + " — " + Exempt[field.Name]);

                output.WriteLine("{0,-32} {1}", field.Name,
                    moved ? "reaches" : (exempt ? "inert, and named as inert" : "REACHES NOTHING"));
            }

            output.WriteLine("{0} settings, {1} runs of {2} rigs", fields.Count, judged, 13);

            Assert.True(inert.Count == 0,
                "settings that changed nothing any rig here reads:\n  "
                + string.Join("\n  ", inert)
                + "\nEither the setting is wired to nothing — which is the defect this exists for —"
                + " or no rig here can see it, in which case add a rig or name it in Exempt with"
                + " the reason.");

            // **The other direction, which is what keeps the list honest.** An exemption that has
            // stopped being true is a rig nobody knows they have.
            Assert.True(unexpectedlyLive.Count == 0,
                "settings named as unreachable that a rig here now moves:\n  "
                + string.Join("\n  ", unexpectedlyLive)
                + "\nTake them out of Exempt.");
        }
    }
}
