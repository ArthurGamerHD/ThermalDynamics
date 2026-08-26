using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **Every dial moves something.**
    ///
    /// <para>
    /// A balance pass turns dials and reads what happened. The failure that wastes a pass is a dial
    /// that reaches nothing: the curve comes back flat, which reads as *this dial does not matter*
    /// and is indistinguishable from *this dial is not wired to anything*. This repository has been
    /// caught by that shape repeatedly — a coolant sink face no test ever exercised, a reactor
    /// making no heat while 1,179 tests stayed green, a panel rule that selected no reactors.
    /// </para>
    ///
    /// <para>
    /// <see cref="KnobBaselineTests"/> is the other half and does not overlap: it checks each
    /// dial's *shipped* value is the one that actually ships, so a curve is read from where the mod
    /// sits. This checks the dial is connected to the simulation at all.
    /// </para>
    ///
    /// <para>
    /// **It is a reach test, not a balance test.** It asserts a dial changes an outcome and says
    /// nothing about whether the change is the right size or the right direction — a direction is a
    /// claim about the model and belongs with the model. Keeping it to reach is what makes it
    /// survive every retune without being touched.
    /// </para>
    /// </summary>
    public class DialReachTests
    {
        private readonly ITestOutputHelper output;

        public DialReachTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        /// <summary>
        /// A hull with something of everything on it: a heat source that produces, one that draws,
        /// armour around them, and faces on the sky. A dial that acts on any of those has somewhere
        /// to act.
        /// </summary>
        private static ThermalSimulation Rig(
            ThermalSettings settings,
            Func<string, string, BlockThermalProperties, BlockThermalProperties> material)
        {
            GridBuilder builder = GridBuilder.Large();

            BlockThermalProperties armour = Catalog.DefaultThermal();
            if (material != null) armour = material("CubeBlock", "LargeBlockArmorBlock", armour);

            // A shell rather than a solid block, so the two heat sources have cells of their own
            // and the armour still wraps them: a source buried in armour is how one is installed.
            builder.Shell(BlockModel.Solid("LargeBlockArmorBlock", Vector3I.One, 500f, armour),
                Vector3I.Zero, new Vector3I(4, 4, 4));

            Vanilla.Block reactor = Vanilla.Find("LargeBlockLargeGenerator");
            BlockThermalProperties reactorThermal = reactor.Thermal;
            if (material != null) reactorThermal = material("Reactor", reactor.Subtype, reactorThermal);

            builder.Place(
                BlockModel.Solid(reactor.Subtype, Vector3I.One, reactor.Mass, reactorThermal),
                new Vector3I(1, 1, 1));
            builder.Producing(reactor.PowerOutputMegawatts * ThermalConstants.MegawattsToWatts);

            Vanilla.Block thruster = Vanilla.Find("LargeBlockLargeThrust");
            BlockThermalProperties thrusterThermal = thruster.Thermal;
            if (material != null)
            {
                thrusterThermal = material("Thrust", thruster.Subtype, thrusterThermal);
            }

            builder.Place(
                BlockModel.Solid(thruster.Subtype, Vector3I.One, thruster.Mass, thrusterThermal),
                new Vector3I(2, 1, 1));
            builder.Thrusting(thruster.PowerDrawMegawatts * ThermalConstants.MegawattsToWatts);

            // **A drive and an engine, because two dials each act on nothing else.** A rig without
            // them reports `jumpdrive-waste` and `engine-waste` as inert, which is true of the rig
            // and says nothing about the mod — the failure this test exists to tell apart.
            Charging(builder, material, "LargeJumpDrive", new Vector3I(1, 2, 1));
            Charging(builder, material, "LargeHydrogenEngine", new Vector3I(2, 2, 1));

            return builder.BuildSimulation(settings ?? new ThermalSettings());
        }

        /// <summary>
        /// Places one of the game's own blocks at its rated draw or output, so a dial aimed at that
        /// type has somewhere to act. Silently does nothing where the game is not installed, which
        /// is the same terms every other test here runs on.
        /// </summary>
        private static void Charging(GridBuilder builder,
            Func<string, string, BlockThermalProperties, BlockThermalProperties> material,
            string subtype, Vector3I at)
        {
            GameBlocks.Definition definition;
            if (!GameBlocks.BySubtype().TryGetValue(subtype, out definition)) return;

            BlockThermalProperties thermal = ShippedBlocks.DeriveWithFunction(
                definition.Components, definition.TypeId, definition.PowerEfficiency);

            if (material != null) thermal = material(definition.TypeId, definition.SubtypeId, thermal);

            builder.Place(BlockModel.Solid(subtype, Vector3I.One, definition.Mass, thermal), at);

            if (definition.PowerOutputWatts > 0f) builder.Producing(definition.PowerOutputWatts);
            else builder.Consuming(definition.PowerDrawWatts);
        }

        /// <summary>
        /// What the rig settles at, in the environment a dial is measured in. One reading, so a
        /// dial that moves the answer moves this.
        /// </summary>
        private static void Settled(KnobLab.Configuration configuration, string scenario,
            out float hottest, out int overCritical, out float early)
        {
            ThermalSimulation simulation = Rig(configuration.Settings(), configuration.Material());

            ScenarioRunner runner = new ScenarioRunner(simulation);
            runner.Environment = t => World(scenario);

            // **Read early as well as settled, because one dial is a transient dial.** Heat
            // capacity does not change where a block ends up, only how fast it gets there, so a
            // reading taken at rest cannot see `jumpdrive-capacity` at all — a third fact about the
            // instrument rather than about the mod.
            runner.Run(300f, 60f);
            early = Hottest(simulation);

            runner.Run(3300f, 300f);

            hottest = 0f;
            overCritical = 0;

            for (int i = 0; i < simulation.Solver.Nodes.Count; i++)
            {
                ThermalNode node = simulation.Solver.Nodes[i];
                if (node.Temperature > hottest) hottest = node.Temperature;
                if (node.Temperature > node.Thermal.CriticalTemperature) overCritical++;
            }
        }

        private static float Hottest(ThermalSimulation simulation)
        {
            float hottest = 0f;
            for (int i = 0; i < simulation.Solver.Nodes.Count; i++)
            {
                float t = simulation.Solver.Nodes[i].Temperature;
                if (t > hottest) hottest = t;
            }
            return hottest;
        }

        /// <summary>The environments the dials are swept in, as this rig can build them.</summary>
        private static EnvironmentSample World(string scenario)
        {
            switch (scenario)
            {
                case "vacuum-sunlit": return Worlds.Ab.SunlitVacuum();
                case "surface-hot-noon": return Worlds.PlanetSurface(1f, 0.5f);
                case "flight-100": return Worlds.Flight(1f, 100f);
                case "reentry": return Worlds.Flight(1f, 1500f);
                default: return Worlds.Shadow();
            }
        }

        /// <summary>
        /// **Every dial `KnobLab` sweeps changes an outcome somewhere it is swept.**
        ///
        /// Each dial is tried at its own levels, in its own scenarios, against the same rig at its
        /// shipped value. A dial that never moves the reading is either disconnected or is being
        /// swept in an environment it cannot act in, and both are a dial whose curve is worthless.
        /// </summary>
        [Fact]
        public void EveryDialMovesAnOutcomeSomewhere()
        {
            List<KnobLab.Knob> knobs = KnobLab.Knobs();

            Assert.True(knobs.Count > 10,
                "only " + knobs.Count + " dials were read, so this test would pass on a table"
                + " that had lost most of them");

            List<string> inert = new List<string>();
            int judged = 0;

            foreach (KnobLab.Knob knob in knobs)
            {
                float widest = 0f;
                string where = "";

                foreach (string scenario in knob.Scenarios)
                {
                    float lowest = float.MaxValue;
                    float highest = float.MinValue;
                    int fewestCritical = int.MaxValue;
                    int mostCritical = int.MinValue;
                    float earlyLow = float.MaxValue;
                    float earlyHigh = float.MinValue;

                    foreach (float level in knob.Levels)
                    {
                        KnobLab.Configuration configuration = new KnobLab.Configuration
                        {
                            Knob = knob,
                            Level = level,
                            Scenarios = knob.Scenarios,
                            IsShipped = level == knob.Shipped,
                        };

                        float settled;
                        int over;
                        float early;
                        Settled(configuration, scenario, out settled, out over, out early);
                        judged++;

                        if (settled < lowest) lowest = settled;
                        if (settled > highest) highest = settled;
                        if (over < fewestCritical) fewestCritical = over;
                        if (over > mostCritical) mostCritical = over;
                        if (early < earlyLow) earlyLow = early;
                        if (early > earlyHigh) earlyHigh = early;
                    }

                    // **Two outcomes, because one dial moves neither temperature nor the other.**
                    // `critical-temperature` cannot change where a block settles — it changes
                    // whether settling there destroys it — so a rig reading only kelvin reports it
                    // inert, which is a fact about the reading.
                    float spread = highest - lowest;
                    if (mostCritical > fewestCritical) spread = Math.Max(spread, 1f);
                    spread = Math.Max(spread, earlyHigh - earlyLow);

                    if (spread > widest)
                    {
                        widest = spread;
                        where = scenario;
                    }
                }

                output.WriteLine("{0,-22} widest spread {1,9:n2} K  in {2}", knob.Name, widest, where);

                // A tenth of a kelvin. Small enough that a dial with a genuinely weak effect still
                // counts as connected, large enough that floating-point noise does not.
                if (widest < 0.1f) inert.Add(knob.Name + " moved nothing across its own levels");
            }

            Assert.True(judged > 50,
                "only " + judged + " readings were taken, so this test is not exercising the dials");

            inert.Sort(StringComparer.Ordinal);
            Assert.True(inert.Count == 0,
                "dials that reach nothing they are swept in:\n  " + string.Join("\n  ", inert.ToArray()));
        }
    }
}
