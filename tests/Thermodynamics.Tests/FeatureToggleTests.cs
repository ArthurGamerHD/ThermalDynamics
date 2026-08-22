using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// Every mechanism is meant to be independently switchable, and switching one off is meant to
    /// remove exactly its own contribution and nothing else. These pin that down one mechanism at
    /// a time: build the same grid twice, change one switch, and check that the difference is the
    /// one mechanism.
    ///
    /// <para>
    /// **One test per switch, and the switch is the subject.** Most of these mechanisms are tested
    /// hard somewhere else — friction against the cube of airspeed, self-shadowing against a block
    /// standing behind another, coolant against the energy it carries — but those rigs reach the
    /// mechanism through its physics rather than through its switch, and several of them turn other
    /// switches off only to keep the fixture quiet. A mechanism that ignores its own setting passes
    /// every one of them. What is checked here is narrower and duller: the setting is honoured, and
    /// what disappears when it is off is that mechanism's contribution and not a neighbour's.
    /// </para>
    ///
    /// <para>
    /// Where a mechanism reports watts, the assertion is on the watts rather than on a temperature
    /// that moved, because a temperature only says something happened. Diagnostics are opt-in, so
    /// those rigs set <c>CollectDiagnostics</c>.
    /// </para>
    /// </summary>
    public class FeatureToggleTests
    {
        private static ThermalSimulation TwoBlocks(ThermalSettings settings, float hot, float cold)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.HeavyArmor(), Vector3I.Zero);
            builder.Place(Catalog.HeavyArmor(), new Vector3I(1, 0, 0));

            ThermalSimulation simulation = builder.BuildSimulation(settings, cold);
            simulation.Solver.Nodes[0].Temperature = hot;
            return simulation;
        }

        [Fact]
        public void ConductionOffLeavesNeighboursAlone()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableConduction = false;
            settings.Derive();

            ThermalSimulation simulation = TwoBlocks(settings, 500f, 300f);
            simulation.StepExact(20, Worlds.Shadow());

            Assert.Equal(500f, simulation.Solver.Nodes[0].Temperature, 3);
            Assert.Equal(300f, simulation.Solver.Nodes[1].Temperature, 3);
        }

        [Fact]
        public void ConductionOnEqualisesThem()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.Derive();

            ThermalSimulation simulation = TwoBlocks(settings, 500f, 300f);
            simulation.StepExact(1000, Worlds.Shadow());

            float a = simulation.Solver.Nodes[0].Temperature;
            float b = simulation.Solver.Nodes[1].Temperature;
            Assert.True(Math.Abs(a - b) < 1f, "expected the pair to equalise, got " + a + " and " + b);
            Assert.Equal(400f, a, 0);
        }

        [Fact]
        public void RadiationOffStopsAHotBlockCoolingIntoSpace()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableRadiation = false;
            settings.Derive();

            ThermalSimulation simulation = TwoBlocks(settings, 900f, 900f);
            simulation.StepExact(50, Worlds.Shadow());

            Assert.Equal(900f, simulation.Solver.Nodes[0].Temperature, 2);
        }

        [Fact]
        public void RadiationOnCoolsIt()
        {
            ThermalSimulation simulation = TwoBlocks(new ThermalSettings(), 900f, 900f);
            simulation.StepExact(50, Worlds.Shadow());

            Assert.True(simulation.Solver.Nodes[0].Temperature < 899f);
        }

        /// <summary>
        /// Radiation sheds what Stefan-Boltzmann says it sheds, not merely something.
        ///
        /// "Cooler than it was" is satisfied by any leak at all, which is most of what could go
        /// wrong here — a coefficient off by the emissivity, a T⁴ term written as T, an ambient
        /// term dropped. The node carries εσA as <c>RadiationCoefficient</c>, so the law it is
        /// multiplied by can be checked against the watts actually applied, and the coefficient
        /// itself against the area and emissivity it is supposed to be built from.
        /// </summary>
        [Fact]
        public void RadiationShedsWhatTheLawSays()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableConduction = false;
            settings.Derive();

            ThermalSimulation simulation = TwoBlocks(settings, 900f, 900f);
            simulation.Solver.CollectDiagnostics = true;

            ThermalNode node = simulation.Solver.Nodes[0];

            Assert.Equal(node.Thermal.Emissivity * ThermalConstants.StefanBoltzmann * node.ExposedArea,
                node.RadiationCoefficient, 6);

            float temperature = node.Temperature;
            float ambient = settings.VacuumTemperature;
            simulation.StepExact(1, Worlds.Shadow());

            // Signed, and a hot block in the dark is shedding, so the figure is negative.
            double expected = node.RadiationCoefficient
                * (Math.Pow(ambient, 4d) - Math.Pow(temperature, 4d));

            Assert.True(node.LastRadiationWatts < 0f);
            Assert.True(Math.Abs(node.LastRadiationWatts - expected) < Math.Abs(expected) * 0.01d,
                "expected about " + expected.ToString("n0") + " W, got " + node.LastRadiationWatts);
        }

        [Fact]
        public void ConvectionOffLeavesOnlyRadiationInAtmosphere()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableConvection = false;
            settings.Derive();

            ThermalSimulation withoutConvection = TwoBlocks(settings, 600f, 600f);
            withoutConvection.StepExact(20, Worlds.PlanetSurface(1f, 0.5f));

            ThermalSimulation withConvection = TwoBlocks(new ThermalSettings(), 600f, 600f);
            withConvection.StepExact(20, Worlds.PlanetSurface(1f, 0.5f));

            // Sea level convection dwarfs radiation, so switching it off has to leave the block
            // hotter by a wide margin.
            Assert.True(withoutConvection.Solver.Nodes[0].Temperature
                > withConvection.Solver.Nodes[0].Temperature + 10f);
        }

        /// <summary>
        /// Convection off removes the convective watts and leaves the radiative ones where they
        /// were.
        ///
        /// A margin of ten Kelvin says the switch does *something*. What the class promises is
        /// narrower — that it removes exactly its own stage — and the way a switch breaks that
        /// promise is by taking a neighbour with it, which a temperature cannot distinguish from
        /// working correctly. One step, so the two runs are still at the same temperature and their
        /// radiation figures are directly comparable.
        ///
        /// **Thin air, deliberately.** The model hands the whole exchange to convection as the air
        /// thickens, and at sea level the radiative share is exactly zero — so a sea-level version
        /// of this test compares nothing to nothing and passes whatever the switch does. At three
        /// tenths of an atmosphere both paths carry real watts and the comparison bites.
        /// </summary>
        [Fact]
        public void ConvectionOffLeavesTheRadiativeShareUntouched()
        {
            ThermalSettings off = new ThermalSettings();
            off.EnableConvection = false;
            off.Derive();

            ThermalSimulation without = TwoBlocks(off, 600f, 600f);
            without.Solver.CollectDiagnostics = true;
            without.StepExact(1, ThinAir());

            ThermalSimulation with = TwoBlocks(new ThermalSettings(), 600f, 600f);
            with.Solver.CollectDiagnostics = true;
            with.StepExact(1, ThinAir());

            Assert.Equal(0f, without.Solver.Nodes[0].LastConvectionWatts, 5);
            Assert.True(with.Solver.Nodes[0].LastConvectionWatts < 0f,
                "a 600 K block in air should be shedding convective watts");

            // Both sides must have a radiative share for the comparison to mean anything.
            Assert.True(with.Solver.Nodes[0].LastRadiationWatts < 0f);
            Assert.Equal(with.Solver.Nodes[0].LastRadiationWatts,
                without.Solver.Nodes[0].LastRadiationWatts, 3);
        }

        /// <summary>
        /// Air thin enough that radiation and convection both carry a real share of the exchange.
        /// </summary>
        private static EnvironmentSample ThinAir()
        {
            return Worlds.PlanetSurface(0.3f, 0.5f);
        }

        /// <summary>
        /// The master switch takes both of its children with it, and nothing else.
        ///
        /// <c>EnableEnvironment</c> sits above radiation and convection and is used across the
        /// suite to quieten fixtures, which is exactly the position from which a switch stops being
        /// tested: everything relies on it and nothing checks it. Waste heat is a source rather
        /// than an exchange with the environment, so it has to survive.
        ///
        /// In thin air for the same reason as the test above: at sea level the radiative share is
        /// zero whatever this switch is set to, and half the assertion would be empty.
        /// </summary>
        [Fact]
        public void TheEnvironmentSwitchRemovesRadiationAndConvectionAndLeavesSourcesAlone()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.Reactor(), Vector3I.Zero).Producing(15e6f);

            ThermalSimulation simulation = builder.BuildSimulation(settings, 600f);
            simulation.Solver.CollectDiagnostics = true;
            simulation.StepExact(1, ThinAir());

            ThermalNode node = simulation.Solver.Nodes[0];
            Assert.Equal(0f, node.LastRadiationWatts, 5);
            Assert.Equal(0f, node.LastConvectionWatts, 5);

            // The reactor is still making its waste heat, and with nowhere to send it the block
            // can only climb.
            Assert.True(node.HeatGenerationWatts > 0f);
            Assert.True(node.Temperature > 600f);

            // The positive control: the same rig with the switch left alone does shed through both
            // paths, so the zeroes above are the switch working rather than a quiet fixture.
            ThermalSimulation exchanging = builder.BuildSimulation(new ThermalSettings(), 600f);
            exchanging.Solver.CollectDiagnostics = true;
            exchanging.StepExact(1, ThinAir());

            Assert.True(exchanging.Solver.Nodes[0].LastRadiationWatts < 0f);
            Assert.True(exchanging.Solver.Nodes[0].LastConvectionWatts < 0f);
        }

        [Fact]
        public void WasteHeatOffStopsAReactorHeatingItself()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableWasteHeat = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.Reactor(), Vector3I.Zero).Producing(15e6f);

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.StepExact(50, Worlds.Shadow());

            Assert.Equal(293.15f, simulation.Solver.Nodes[0].Temperature, 3);
        }

        [Fact]
        public void SwitchingASettingTakesEffectWithoutARebuild()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.Reactor(), Vector3I.Zero).Producing(15e6f);

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.StepExact(10, Worlds.Shadow());
            float heated = simulation.Solver.Nodes[0].Temperature;
            Assert.True(heated > 293.15f);

            // The same settings object every grid already holds, changed mid-session.
            settings.EnableWasteHeat = false;
            settings.Derive();

            simulation.StepExact(10, Worlds.Shadow());
            Assert.Equal(heated, simulation.Solver.Nodes[0].Temperature, 3);
        }

        [Fact]
        public void ChangingHeatTimeScaleMidSessionRescalesCapacity()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.HeatTimeScale = 1f;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.HeavyArmor(), Vector3I.Zero);

            ThermalSimulation simulation = builder.BuildSimulation(settings, 300f);
            float slow = simulation.Solver.Nodes[0].ThermalMass;

            settings.HeatTimeScale = 100f;
            settings.Derive();
            simulation.StepExact(1, Worlds.Shadow());

            Assert.Equal(slow / 100f, simulation.Solver.Nodes[0].ThermalMass, 4);
        }

        [Fact]
        public void CoolantLoopsSwitchOffMidSession()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, 4, 3));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            Assert.NotEmpty(simulation.Solver.Loops);

            settings.EnableCoolantLoops = false;
            settings.Derive();
            simulation.StepExact(1, Worlds.Shadow());

            Assert.Empty(simulation.Solver.Loops);
        }

        // ---- the switches whose mechanisms are tested elsewhere, but never through the switch ---

        /// <summary>
        /// Friction off leaves no frictional watts on a hull at speed.
        ///
        /// The mechanism is tested thoroughly — against the cube of airspeed, against the 50 m/s
        /// threshold, against relative wind — but every one of those rigs reaches it through the
        /// airspeed rather than through <c>EnableFriction</c>, and every other appearance of the
        /// setting in the suite is a fixture being quietened. Nothing checked that the switch is
        /// read at all.
        /// </summary>
        [Fact]
        public void FrictionOffLeavesAHullAtSpeedCold()
        {
            ThermalNode cold = HullAtSpeed(false);
            ThermalNode hot = HullAtSpeed(true);

            Assert.Equal(0f, cold.LastFrictionWatts, 5);
            Assert.Equal(293.15f, cold.Temperature, 3);

            // The positive control. Without it this test passes just as well on a rig that could
            // never have produced a frictional watt in the first place.
            Assert.True(hot.LastFrictionWatts > 0f);
            Assert.True(hot.Temperature > 293.15f);
        }

        /// <summary>A block held in thick air at 300 m/s, with friction on or off.</summary>
        private static ThermalNode HullAtSpeed(bool friction)
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.EnableDamage = false;
            settings.EnableFriction = friction;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.HeavyArmor(), Vector3I.Zero);

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.Solver.CollectDiagnostics = true;
            simulation.Planet = PlanetThermalProperties.Default();

            simulation.StepExact(4, Worlds.Flight(0.8f, 300f));
            return simulation.Solver.Nodes[0];
        }

        /// <summary>
        /// Solar off leaves no solar watts on a node facing the sun.
        ///
        /// The switch is checked one layer down, where <c>EnvironmentSolver</c> reports the sample
        /// as fully occluded, but nothing followed it up through the per-face projection into the
        /// node that the heat would land on. That is the layer the rest of the simulation reads.
        /// </summary>
        [Fact]
        public void SolarOffLeavesNoSolarWattsOnASunwardNode()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);

            ThermalSettings off = new ThermalSettings();
            off.EnableSolarHeat = false;
            off.Derive();

            ThermalSimulation dark = builder.BuildSimulation(off, 293.15f);
            dark.Solver.CollectDiagnostics = true;
            dark.StepExact(1, Worlds.Space(new Vector3(1f, 0f, 0f)));

            ThermalSettings on = new ThermalSettings();
            on.EnableSolarHeat = true;
            on.Derive();

            ThermalSimulation lit = builder.BuildSimulation(on, 293.15f);
            lit.Solver.CollectDiagnostics = true;
            lit.StepExact(1, Worlds.Space(new Vector3(1f, 0f, 0f)));

            Assert.Equal(0f, dark.Solver.Nodes[0].LastSolarWatts, 5);
            Assert.True(lit.Solver.Nodes[0].LastSolarWatts > 0f,
                "a block facing the sun should be taking solar watts");
        }

        /// <summary>
        /// Coolant loops off stop the ring carrying heat, not just stop it being built.
        ///
        /// Both existing checks assert that <c>Solver.Loops</c> is empty, which is a statement
        /// about the structure the builder produced. A loop that is built and then contributes
        /// nothing, or one that goes on transporting after the switch is thrown, both satisfy an
        /// empty collection or are invisible to it. What matters to a ship is whether the heat
        /// moved, so that is what is measured: the same sink-faced ring both ways.
        /// </summary>
        [Fact]
        public void CoolantLoopsOffStopTheRingCarryingHeat()
        {
            float withLoops = HotBlockBesideARing(true);
            float withoutLoops = HotBlockBesideARing(false);

            Assert.True(withLoops < withoutLoops - 1f,
                "the ring should have carried heat away from the block: with loops it reached "
                + withLoops + " K, without them " + withoutLoops + " K");
        }

        /// <summary>
        /// A 900 K block bolted to a sink face of a pumped ring, stepped, and the temperature it
        /// came down to. Conduction stays on in both runs, so what the comparison isolates is the
        /// heat the *loop* carried rather than the heat the pipes conducted.
        /// </summary>
        private static float HotBlockBesideARing(bool loops)
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.EnableFriction = false;
            settings.EnableDamage = false;
            settings.EnableCoolantLoops = loops;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();

            Dictionary<int, Vector3I> sinks = new Dictionary<int, Vector3I>();
            sinks[1] = Vector3I.Down;

            PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, 3, 3), -1, sinks);
            builder.Place(Catalog.HeavyArmor(), new Vector3I(1, -1, 0));
            BlockInstance hot = builder.Last;

            ThermalSimulation simulation = builder.BuildSimulation(settings, 300f);
            Assert.Equal(loops, simulation.Solver.Loops.Count > 0);

            ThermalNode node = simulation.Solver.GetNode(hot);
            node.Temperature = 900f;
            simulation.StepExact(20, Worlds.Shadow());

            return node.Temperature;
        }

        /// <summary>
        /// Room air off stops the air carrying heat across a sealed room.
        ///
        /// As with the loops, what was checked was that the collection is empty. With conduction
        /// switched off the air is the only path between two walls of a sealed box, so whether it
        /// is carrying anything is a question the far wall can answer.
        /// </summary>
        [Fact]
        public void RoomAirOffStopsTheAirCarryingHeatAcrossARoom()
        {
            float withAir = FarWallOfASealedBox(true);
            float withoutAir = FarWallOfASealedBox(false);

            Assert.Equal(293.15f, withoutAir, 3);
            Assert.True(withAir > withoutAir + 1f,
                "the air should have warmed the far wall, got " + withAir + " K against "
                + withoutAir + " K");
        }

        /// <summary>
        /// A sealed shell with one wall held hot and conduction switched off, and the temperature
        /// the opposite wall reached. The air is the only route between the two.
        /// </summary>
        private static float FarWallOfASealedBox(bool air)
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableConduction = false;
            settings.EnableRoomAir = air;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(5, 5, 5));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.SetRoomPressure(new Vector3I(2, 2, 2), 1f);

            simulation.Solver.GetNodeAt(new Vector3I(2, 0, 2)).Temperature = 600f;
            ThermalNode far = simulation.Solver.GetNodeAt(new Vector3I(2, 4, 2));

            simulation.StepExact(400, Worlds.Shadow());
            return far.Temperature;
        }
    }
}
