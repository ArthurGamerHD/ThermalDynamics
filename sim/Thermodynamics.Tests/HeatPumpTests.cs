using System;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The heat pump. It is the only thing in the model that moves heat the wrong way up a
    /// gradient, and the only one that spends energy from outside the grid to do it.
    /// </summary>
    public class HeatPumpTests
    {
        /// <summary>
        /// Cold block, pump, hot block in a row along Z, touching nothing else. Conduction is off
        /// throughout: these tests are about the pump, and three blocks in contact would otherwise
        /// equalise on their own and hide it.
        /// </summary>
        private static ThermalSimulation Rig(out ThermalNode cold, out ThermalNode hot, out HeatPumpDevice pump)
        {
            return Rig(Catalog.HeatPump(), out cold, out hot, out pump);
        }

        private static ThermalSimulation Rig(BlockModel pumpModel, out ThermalNode cold, out ThermalNode hot, out HeatPumpDevice pump)
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableConduction = false;
            settings.EnableWasteHeat = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();

            // the pump's cold face is its local forward, which is -Z
            builder.Place(Catalog.LightArmor(), new Vector3I(0, 0, -1));
            builder.Place(pumpModel, Vector3I.Zero);
            builder.Place(Catalog.LightArmor(), new Vector3I(0, 0, 1));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 300f);

            cold = simulation.Solver.GetNodeAt(new Vector3I(0, 0, -1));
            hot = simulation.Solver.GetNodeAt(new Vector3I(0, 0, 1));

            pump = simulation.GetHeatPump(simulation.Grid.GetAtCell(Vector3I.Zero));
            return simulation;
        }

        [Fact]
        public void APumpBindsToTheBlocksEitherSideOfIt()
        {
            ThermalNode cold, hot;
            HeatPumpDevice pump;
            ThermalSimulation simulation = Rig(out cold, out hot, out pump);

            Assert.NotNull(pump);
            Assert.True(pump.IsConnected);
            Assert.Equal(cold.Index, pump.ColdNodeIndex);
            Assert.Equal(hot.Index, pump.HotNodeIndex);
            Assert.Single(simulation.HeatPumps);
        }

        [Fact]
        public void APumpWithNothingOnOneFaceIsNotConnected()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.HeatPump(), Vector3I.Zero);
            builder.Place(Catalog.LightArmor(), new Vector3I(0, 0, 1));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 300f);
            HeatPumpDevice pump = simulation.HeatPumps[0];

            Assert.False(pump.IsConnected);

            pump.Enabled = true;
            simulation.StepExact(20, Worlds.Shadow());

            Assert.Equal(0f, pump.LastLiftedWatts);
            Assert.Equal(0f, pump.LastPowerWatts);
        }

        /// <summary>The whole point of the block: heat crosses from cold to hot.</summary>
        [Fact]
        public void ARunningPumpMovesHeatAgainstTheGradient()
        {
            ThermalNode cold, hot;
            HeatPumpDevice pump;
            ThermalSimulation simulation = Rig(out cold, out hot, out pump);

            cold.Temperature = 300f;
            hot.Temperature = 400f;
            pump.Enabled = true;

            simulation.StepExact(40, Worlds.Shadow());

            Assert.True(cold.Temperature < 300f,
                "the cold side should have been cooled below its start, got " + cold.Temperature);
            Assert.True(hot.Temperature > 400f,
                "the hot side should have been warmed above its start, got " + hot.Temperature);
        }

        [Fact]
        public void APumpThatIsOffDoesNothing()
        {
            ThermalNode cold, hot;
            HeatPumpDevice pump;
            ThermalSimulation simulation = Rig(out cold, out hot, out pump);

            cold.Temperature = 300f;
            hot.Temperature = 400f;
            pump.Enabled = false;

            simulation.StepExact(40, Worlds.Shadow());

            Assert.Equal(300f, cold.Temperature, 4);
            Assert.Equal(400f, hot.Temperature, 4);
            Assert.Equal(0f, pump.LastPowerWatts);
        }

        [Fact]
        public void TheSwitchTurnsTheMechanismOff()
        {
            ThermalNode cold, hot;
            HeatPumpDevice pump;
            ThermalSimulation simulation = Rig(out cold, out hot, out pump);

            simulation.Settings.EnableHeatPumps = false;
            simulation.Settings.Derive();

            cold.Temperature = 300f;
            hot.Temperature = 400f;
            pump.Enabled = true;

            simulation.StepExact(40, Worlds.Shadow());

            Assert.Equal(300f, cold.Temperature, 4);
            Assert.Equal(400f, hot.Temperature, 4);
        }

        /// <summary>
        /// The hot side receives the heat lifted plus the work that lifted it. That is what makes
        /// a heat pump worth building and also what makes it dangerous: it warms its exhaust by
        /// more than it cools its intake, so a ship cannot pump its way out of a heat problem
        /// without somewhere to put the total.
        /// </summary>
        [Fact]
        public void TheHotSideGetsTheLiftPlusTheWork()
        {
            ThermalNode cold, hot;
            HeatPumpDevice pump;
            ThermalSimulation simulation = Rig(out cold, out hot, out pump);

            cold.Temperature = 300f;
            hot.Temperature = 400f;
            pump.Enabled = true;

            simulation.StepExact(1, Worlds.Shadow());

            Assert.True(pump.LastPowerWatts > 0f);
            Assert.Equal(pump.LastLiftedWatts + pump.LastPowerWatts, pump.LastRejectedWatts, 1);

            // and that is exactly the energy the two blocks exchanged
            float lost = (300f - cold.Temperature) * cold.ThermalMass;
            float gained = (hot.Temperature - 400f) * hot.ThermalMass;
            Assert.True(gained > lost,
                "the hot side must gain more than the cold side lost, got " + gained + " vs " + lost);
        }

        /// <summary>
        /// Energy is conserved once the electricity is counted. The grid gains exactly the work
        /// the pump drew — nothing appears from nowhere, which is the property the rest of the
        /// solver is built on.
        /// </summary>
        [Fact]
        public void TheEnergyTheGridGainsIsTheWorkDrawn()
        {
            ThermalNode cold, hot;
            HeatPumpDevice pump;
            ThermalSimulation simulation = Rig(out cold, out hot, out pump);

            cold.Temperature = 300f;
            hot.Temperature = 400f;
            pump.Enabled = true;

            float before = simulation.Solver.TotalEnergy;
            simulation.StepExact(1, Worlds.Shadow());
            float after = simulation.Solver.TotalEnergy;

            float work = pump.LastPowerWatts * simulation.Settings.StepSeconds;
            Assert.Equal(work, after - before, Math.Max(1f, work * 0.001f));
        }

        /// <summary>
        /// Carnot, which is the whole balance of the block: the wider the gap, the worse the deal.
        /// </summary>
        [Fact]
        public void LiftingAcrossAWiderGapCostsMorePerWatt()
        {
            ThermalNode cold, hot;
            HeatPumpDevice pump;
            ThermalSimulation narrow = Rig(out cold, out hot, out pump);
            cold.Temperature = 300f;
            hot.Temperature = 310f;
            pump.Enabled = true;
            narrow.StepExact(1, Worlds.Shadow());
            float narrowCoefficient = pump.LastCoefficient;

            ThermalNode cold2, hot2;
            HeatPumpDevice pump2;
            ThermalSimulation wide = Rig(out cold2, out hot2, out pump2);
            cold2.Temperature = 300f;
            hot2.Temperature = 800f;
            pump2.Enabled = true;
            wide.StepExact(1, Worlds.Shadow());

            Assert.True(narrowCoefficient > pump2.LastCoefficient,
                "a narrow gap should be more efficient, got " + narrowCoefficient
                + " against " + pump2.LastCoefficient);
            Assert.True(pump.LastLiftedWatts > pump2.LastLiftedWatts,
                "a narrow gap should move more heat for the same power");
        }

        [Fact]
        public void TheCoefficientIsCappedWhenThereIsNoGapToPumpAgainst()
        {
            ThermalSettings settings = new ThermalSettings();

            // pumping downhill is just a very efficient pump, not a special case
            Assert.Equal(
                settings.HeatPumpMaxCoefficient,
                HeatPumpDevice.Coefficient(400f, 300f, settings.HeatPumpCarnotFraction, settings.HeatPumpMaxCoefficient));

            // and the cap binds long before the gap closes entirely
            Assert.Equal(
                settings.HeatPumpMaxCoefficient,
                HeatPumpDevice.Coefficient(300f, 300.5f, settings.HeatPumpCarnotFraction, settings.HeatPumpMaxCoefficient));
        }

        /// <summary>
        /// The block cannot be run to absolute zero. Nothing clamps it there: the cost of a kelvin
        /// rises without limit as the cold side approaches zero, so the pump's own electrical
        /// rating stops it. This is the property that keeps the block from being a cheat.
        /// </summary>
        [Fact]
        public void APumpCannotDriveItsColdSideToAbsoluteZero()
        {
            ThermalNode cold, hot;
            HeatPumpDevice pump;
            ThermalSimulation simulation = Rig(out cold, out hot, out pump);

            cold.Temperature = 300f;
            hot.Temperature = 300f;
            pump.Enabled = true;

            simulation.StepExact(4000, Worlds.Shadow());

            Assert.True(cold.Temperature > ThermalConstants.MinimumTemperature,
                "the cold side reached " + cold.Temperature + " K");
            Assert.True(cold.Temperature < 300f, "it should still have cooled something");

            // by now the pump is buying almost nothing for its full power draw
            Assert.True(pump.LastCoefficient < 1f,
                "the coefficient should have collapsed, got " + pump.LastCoefficient);
        }

        [Fact]
        public void ABrownedOutPumpLiftsProportionallyLess()
        {
            ThermalNode cold, hot;
            HeatPumpDevice pump;
            ThermalSimulation full = Rig(out cold, out hot, out pump);
            cold.Temperature = 300f;
            hot.Temperature = 500f;
            pump.Enabled = true;
            pump.PowerAvailable = 1f;
            full.StepExact(1, Worlds.Shadow());
            float atFullPower = pump.LastLiftedWatts;

            ThermalNode cold2, hot2;
            HeatPumpDevice pump2;
            ThermalSimulation half = Rig(out cold2, out hot2, out pump2);
            cold2.Temperature = 300f;
            hot2.Temperature = 500f;
            pump2.Enabled = true;
            pump2.PowerAvailable = 0.5f;
            half.StepExact(1, Worlds.Shadow());

            Assert.Equal(atFullPower * 0.5f, pump2.LastLiftedWatts, atFullPower * 0.02f);
            Assert.True(pump2.LastPowerWatts < pump.LastPowerWatts);
        }

        /// <summary>
        /// A browned-out pump must keep asking for what it wants, not for what it got. Asking for
        /// what it managed would lower the request every step and never recover when the power
        /// came back.
        /// </summary>
        [Fact]
        public void DemandIsWhatItWouldDrawNotWhatItGot()
        {
            ThermalNode cold, hot;
            HeatPumpDevice pump;
            ThermalSimulation simulation = Rig(out cold, out hot, out pump);

            cold.Temperature = 300f;
            hot.Temperature = 500f;
            pump.Enabled = true;
            pump.PowerAvailable = 0.25f;

            simulation.StepExact(1, Worlds.Shadow());

            Assert.True(pump.LastDemandWatts > pump.LastPowerWatts,
                "demand " + pump.LastDemandWatts + " should exceed the " + pump.LastPowerWatts + " actually drawn");
            Assert.Equal(pump.MaxPowerWatts, pump.LastDemandWatts, pump.MaxPowerWatts * 0.02f);

            // and it stays put rather than ratcheting down
            simulation.StepExact(20, Worlds.Shadow());
            Assert.Equal(pump.MaxPowerWatts, pump.LastDemandWatts, pump.MaxPowerWatts * 0.02f);
        }

        /// <summary>
        /// Against a small gap the machine's rating binds rather than its efficiency, so it draws
        /// less than its maximum: it cannot use power it has no heat capacity to move.
        /// </summary>
        [Fact]
        public void TheRatingBindsWhenTheGapIsSmall()
        {
            ThermalNode cold, hot;
            HeatPumpDevice pump;
            ThermalSimulation simulation = Rig(Catalog.HeatPump(10000f, 20000f), out cold, out hot, out pump);

            cold.Temperature = 300f;
            hot.Temperature = 305f;
            pump.Enabled = true;

            simulation.StepExact(1, Worlds.Shadow());

            Assert.Equal(10000f, pump.LastLiftedWatts, 10f);
            Assert.True(pump.LastPowerWatts < pump.MaxPowerWatts,
                "it should not draw full power to move its rated heat across a small gap");
        }

        /// <summary>
        /// Reported per step, not per substep. A value written once per substep describes only the
        /// last one, which is how the old model came to under-report every rate it published.
        /// </summary>
        [Fact]
        public void ReportedWattsDescribeTheWholeStepNotTheLastSubstep()
        {
            ThermalNode cold, hot;
            HeatPumpDevice pump;
            ThermalSimulation simulation = Rig(Catalog.HeatPump(10000f, 20000f), out cold, out hot, out pump);

            cold.Temperature = 300f;
            hot.Temperature = 305f;
            pump.Enabled = true;

            simulation.StepExact(1, Worlds.Shadow());

            float energy = pump.LastLiftedWatts * simulation.Settings.StepSeconds;
            float lost = (300f - cold.Temperature) * cold.ThermalMass;

            Assert.Equal(lost, energy, Math.Max(1f, lost * 0.01f));
        }

        [Fact]
        public void RemovingTheBlockOnTheColdFaceDisconnectsThePump()
        {
            ThermalNode cold, hot;
            HeatPumpDevice pump;
            ThermalSimulation simulation = Rig(out cold, out hot, out pump);
            pump.Enabled = true;

            simulation.RemoveBlock(simulation.Grid.GetAtCell(new Vector3I(0, 0, -1)));
            simulation.RebuildAll();

            HeatPumpDevice rebuilt = simulation.HeatPumps[0];
            Assert.False(rebuilt.IsConnected);

            // and the switch survived the rebuild: it belongs to the block, not to the table
            Assert.True(rebuilt.Enabled);
        }

        /// <summary>
        /// A pump wider than one cell puts its faces on the middle of its end caps. The shipped
        /// small-grid block is three by three, and a port left on its corner cell would bind the
        /// pump to whatever sat diagonally behind it rather than to what it faces.
        /// </summary>
        [Fact]
        public void AWidePumpsFacesSitOnTheMiddleOfItsEndCaps()
        {
            HeatPumpShape wide = HeatPumpShape.Centred(Vector3I.Forward, new Vector3I(3, 3, 1), 1f, 1f);

            Assert.Equal(new Vector3I(1, 1, 0), wide.ColdCell);
            Assert.Equal(new Vector3I(1, 1, 0), wide.HotCell);

            // and a long one puts them at opposite ends of the axis it runs along
            HeatPumpShape along = HeatPumpShape.Centred(Vector3I.Forward, new Vector3I(1, 1, 3), 1f, 1f);

            Assert.Equal(new Vector3I(0, 0, 0), along.ColdCell);
            Assert.Equal(new Vector3I(0, 0, 2), along.HotCell);
        }

        [Fact]
        public void AWidePumpBindsToTheBlocksOffItsMiddle()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableConduction = false;
            settings.Derive();

            BlockModel model = BlockModel.Solid("HeatPumpWide", new Vector3I(3, 3, 1), 800f, Catalog.DefaultThermal());
            model.WithHeatPump(HeatPumpShape.Centred(Vector3I.Forward, new Vector3I(3, 3, 1), 60000f, 20000f));

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), new Vector3I(1, 1, -1));
            builder.Place(model, Vector3I.Zero);
            builder.Place(Catalog.LightArmor(), new Vector3I(1, 1, 1));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 300f);
            HeatPumpDevice pump = simulation.HeatPumps[0];

            Assert.True(pump.IsConnected);
            Assert.Equal(simulation.Solver.GetNodeAt(new Vector3I(1, 1, -1)).Index, pump.ColdNodeIndex);
            Assert.Equal(simulation.Solver.GetNodeAt(new Vector3I(1, 1, 1)).Index, pump.HotNodeIndex);
        }

        /// <summary>A rotated pump draws from whatever its forward face is now pointing at.</summary>
        [Fact]
        public void TheColdFaceFollowsTheBlocksOrientation()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableConduction = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), new Vector3I(-1, 0, 0));
            builder.Place(Catalog.HeatPump(), Vector3I.Zero,
                new BlockOrientation(Base6Directions.Direction.Left, Base6Directions.Direction.Up));
            builder.Place(Catalog.LightArmor(), new Vector3I(1, 0, 0));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 300f);

            HeatPumpDevice pump = simulation.HeatPumps[0];
            Assert.True(pump.IsConnected);

            ThermalNode left = simulation.Solver.GetNodeAt(new Vector3I(-1, 0, 0));
            Assert.Equal(left.Index, pump.ColdNodeIndex);
        }
    }
}
