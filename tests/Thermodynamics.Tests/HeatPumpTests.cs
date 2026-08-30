using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Cold block, pump, hot block in a row along Z with **conduction left on**, which is what
        /// separates it from <see cref="Rig(out ThermalNode, out ThermalNode, out HeatPumpDevice)"/>.
        ///
        /// <para>
        /// `Rig` switches conduction off so three touching blocks cannot equalise on their own and
        /// hide the pump. These tests want the opposite: they are about what the pump does against
        /// a hull that is fighting it back, so the blocks are heavy armour and the heat is allowed
        /// to flow. Written out three times before this, identically, which is three chances for
        /// one of them to stop being the same rig as the other two while all three still passed.
        /// </para>
        /// </summary>
        private static ThermalSimulation ConductingRig(out BlockInstance cold, out BlockInstance hot,
            out HeatPumpDevice pump)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.HeavyArmor(), Vector3I.Zero);
            cold = builder.Last;
            builder.Place(Catalog.HeatPump(), new Vector3I(0, 0, 1),
                new BlockOrientation(Base6Directions.Direction.Forward, Base6Directions.Direction.Up));
            BlockInstance pumpBlock = builder.Last;
            builder.Place(Catalog.HeavyArmor(), new Vector3I(0, 0, 2));
            hot = builder.Last;

            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableDamage = false;
            settings.Derive();

            ThermalSimulation simulation = builder.BuildSimulation(settings, 300f);
            pump = simulation.Solver.GetHeatPump(pumpBlock);
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
    
        // ---- the throttle -------------------------------------------------------------------

        /// <summary>
        /// The slider caps what the pump may draw, and it lifts what that buys at the current gap.
        ///
        /// Distinct from a browned-out grid: this is what the block was told to want, not what could
        /// be supplied. A player who only needs cooling sometimes should not be paying for it always.
        /// </summary>
        [Theory]
        [InlineData(1f)]
        [InlineData(0.5f)]
        [InlineData(0.25f)]
        public void TheThrottleCapsWhatThePumpDraws(float setting)
        {
            BlockInstance cold, hot;
            HeatPumpDevice pump;
            ThermalSimulation simulation = ConductingRig(out cold, out hot, out pump);
            pump.Enabled = true;
            pump.PowerAvailable = 1f;
            pump.PowerSetting = setting;

            Assert.Equal(pump.MaxPowerWatts * setting, pump.SettablePowerWatts, 2);

            // A wide gap, so the Carnot cost binds and the draw is the throttle rather than the rating.
            ThermalNode hotNode = simulation.Solver.GetNode(hot);
            ThermalNode coldNode = simulation.Solver.GetNode(cold);

            for (int i = 0; i < 40; i++)
            {
                hotNode.Temperature = 900f;
                coldNode.Temperature = 300f;
                simulation.StepExact(1, Worlds.Shadow());
            }

            Assert.True(pump.LastPowerWatts <= (pump.MaxPowerWatts * setting) + 1f,
                "drew " + pump.LastPowerWatts + " W against a " + setting + " throttle on "
                + pump.MaxPowerWatts + " W");
            Assert.True(pump.LastPowerWatts > 0f, "a throttled pump should still run");
        }

        /// <summary>A throttle of zero is off, and costs nothing.</summary>
        [Fact]
        public void AThrottleOfZeroMovesNothingAndDrawsNothing()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.HeavyArmor(), Vector3I.Zero);
            builder.Place(Catalog.HeatPump(), new Vector3I(0, 0, 1),
                new BlockOrientation(Base6Directions.Direction.Forward, Base6Directions.Direction.Up));
            BlockInstance pumpBlock = builder.Last;
            builder.Place(Catalog.HeavyArmor(), new Vector3I(0, 0, 2));

            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.Derive();

            ThermalSimulation simulation = builder.BuildSimulation(settings, 300f);
            HeatPumpDevice pump = simulation.Solver.GetHeatPump(pumpBlock);
            pump.Enabled = true;
            pump.PowerAvailable = 1f;
            pump.PowerSetting = 0f;

            simulation.Solver.GetNode(pumpBlock).Temperature = 300f;
            simulation.StepExact(20, Worlds.Shadow());

            Assert.Equal(0f, pump.LastLiftedWatts, 3);
            Assert.Equal(0f, pump.LastPowerWatts, 3);
            Assert.Equal(0f, pump.LastDemandWatts, 3);
        }

        // ---- the two ways a player will misuse it -------------------------------------------

        /// <summary>
        /// A pump whose hot side is colder than its cold side runs at the coefficient cap, and that is
        /// deliberate: the Carnot relation has nothing to say about pumping downhill, so it saturates
        /// rather than dividing by a negative.
        ///
        /// The consequence is a trap rather than an exploit, and worth pinning as such. Such a pump
        /// reports the best numbers the block can show — a measured 60 kW moved for 7.5 kW, a
        /// coefficient of 8.00 — while ordinary conduction between the same two blocks was already
        /// carrying 90 kW in that direction for nothing, and the pump's own draw is added to the grid
        /// as heat on top. It looks like the ideal installation and achieves a rounding error.
        /// </summary>
        [Fact]
        public void APumpRunningDownhillSaturatesItsCoefficientAndAchievesLittle()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.HeavyArmor(), Vector3I.Zero);
            BlockInstance hotBlock = builder.Last;

            // Cold face on the HOT block: the wrong way round.
            builder.Place(Catalog.HeatPump(), new Vector3I(0, 0, 1),
                new BlockOrientation(Base6Directions.Direction.Forward, Base6Directions.Direction.Up));
            BlockInstance pumpBlock = builder.Last;
            builder.Place(Catalog.HeavyArmor(), new Vector3I(0, 0, 2));
            BlockInstance coldBlock = builder.Last;

            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableDamage = false;
            settings.Derive();

            ThermalSimulation simulation = builder.BuildSimulation(settings, 300f);
            simulation.Solver.CollectDiagnostics = true;

            HeatPumpDevice pump = simulation.Solver.GetHeatPump(pumpBlock);
            pump.Enabled = true;
            pump.PowerAvailable = 1f;

            ThermalNode hotNode = simulation.Solver.GetNode(hotBlock);
            ThermalNode coldNode = simulation.Solver.GetNode(coldBlock);

            for (int i = 0; i < 40; i++)
            {
                hotNode.Temperature = 900f;
                coldNode.Temperature = 300f;
                simulation.StepExact(1, Worlds.Shadow());
            }

            // It saturates rather than misbehaving.
            Assert.Equal(8f, pump.LastCoefficient, 2);
            Assert.True(pump.LastLiftedWatts > 50000f, "it does move heat: " + pump.LastLiftedWatts);

            // And conduction is already moving considerably more, in the same direction, for nothing.
            Assert.True(Math.Abs(coldNode.LastConductionWatts) > pump.LastLiftedWatts,
                "conduction " + coldNode.LastConductionWatts + " W should dominate the pump's "
                + pump.LastLiftedWatts + " W, which is what makes this pointless rather than strong");
        }

        /// <summary>
        /// Cascading pumps across a gap beats one pump across the whole of it, and the advantage is
        /// bounded — which is what makes it engineering rather than an exploit.
        ///
        /// Measured on a fixed 320 K gap: one stage reaches the single-stage Carnot figure of 0.38,
        /// two stages 0.37, four 0.51 and eight 0.54. It plateaus, because each stage has to lift the
        /// work of every stage below it as well, and that compounding eats the efficiency a narrower
        /// gap buys. Eight stages is eight blocks and five and a half times the power draw — all of
        /// which still has to be radiated — for about 1.4 times the heat moved.
        /// </summary>
        [Fact]
        public void CascadingPumpsHelpsByABoundedAmount()
        {
            float one = CascadeCoefficient(1);
            float four = CascadeCoefficient(4);
            float eight = CascadeCoefficient(8);

            // A cascade beats a single stage across the same gap.
            Assert.True(four > one, "four stages " + four + " should beat one stage " + one);

            // But it plateaus rather than running away: doubling again buys very little.
            Assert.True(eight < four * 1.5f,
                "eight stages " + eight + " against four " + four + " is not a plateau");
            Assert.True(eight < 1f,
                "no arrangement should lift more heat than the work it spends across a gap this wide");
        }

        /// <summary>Heat the first stage lifts per watt the whole chain draws, across a fixed 320 K.</summary>
        private static float CascadeCoefficient(int stages)
        {
            GridBuilder builder = GridBuilder.Large();
            List<BlockInstance> blocks = new List<BlockInstance>();

            builder.Place(Catalog.HeavyArmor(), Vector3I.Zero);
            blocks.Add(builder.Last);

            for (int i = 0; i < stages; i++)
            {
                int z = (i * 2) + 1;
                builder.Place(Catalog.HeatPump(), new Vector3I(0, 0, z),
                    new BlockOrientation(Base6Directions.Direction.Forward, Base6Directions.Direction.Up));
                builder.Place(Catalog.HeavyArmor(), new Vector3I(0, 0, z + 1));
                blocks.Add(builder.Last);
            }

            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableDamage = false;
            settings.MaxSubsteps = 64;
            settings.MaxSubstepsPerBlock = 0;
            settings.MaxElementVisitsPerStep = 0;
            settings.Derive();

            ThermalSimulation simulation = builder.BuildSimulation(settings, 300f);

            IList<HeatPumpDevice> devices = simulation.Solver.HeatPumps;
            for (int i = 0; i < devices.Count; i++)
            {
                devices[i].Enabled = true;
                devices[i].PowerAvailable = 1f;
            }

            ThermalNode coldEnd = simulation.Solver.GetNode(blocks[0]);
            ThermalNode hotEnd = simulation.Solver.GetNode(blocks[blocks.Count - 1]);

            for (int i = 0; i < 120; i++)
            {
                coldEnd.Temperature = 300f;
                hotEnd.Temperature = 620f;
                simulation.StepExact(1, Worlds.Shadow());
            }

            float draw = 0f;
            for (int i = 0; i < devices.Count; i++) draw += devices[i].LastPowerWatts;

            return draw <= 0f ? 0f : devices[0].LastLiftedWatts / draw;
        }
    
        /// <summary>
        /// The margin says how much gap is left before the pump stops reaching its rating, and it is
        /// negative once the gap is past that. It is the figure a player can act on: move either side
        /// by that much.
        ///
        /// The pump saturates while coefficient x power >= rating, and the coefficient is
        /// fraction x Tcold / gap, so the widest gap that still saturates it is
        /// fraction x Tcold x power / rating — 0.4 x 300 x 20000 / 60000 = 40 K at a 300 K cold side.
        /// </summary>
        [Theory]
        [InlineData(310f, 30f)]     // a 10 K gap against a 40 K allowance: 30 K spare
        [InlineData(340f, 0f)]      // exactly at the limit
        [InlineData(400f, -60f)]    // 100 K gap: sixty degrees too wide
        public void TheOptimalMarginSaysHowFarTheGapIsFromFullOutput(float hotSide, float expected)
        {
            BlockInstance cold, hot;
            HeatPumpDevice pump;
            ThermalSimulation simulation = ConductingRig(out cold, out hot, out pump);
            pump.Enabled = true;
            pump.PowerAvailable = 1f;

            ThermalNode coldNode = simulation.Solver.GetNode(cold);
            ThermalNode hotNode = simulation.Solver.GetNode(hot);

            for (int i = 0; i < 30; i++)
            {
                coldNode.Temperature = 300f;
                hotNode.Temperature = hotSide;
                simulation.StepExact(1, Worlds.Shadow());
            }

            Assert.Equal(expected, pump.LastOptimalMarginKelvin, 0);

            // A margin at or above zero is a pump reaching its rating; below it, one that is not.
            if (expected >= 0f) Assert.Equal(pump.RatedWatts, pump.LastLiftedWatts, 0);
            else Assert.True(pump.LastLiftedWatts < pump.RatedWatts);
        }

        /// <summary>Throttling narrows the gap the pump can still saturate across, and the margin says so.</summary>
        [Fact]
        public void ThrottlingShrinksTheOptimalMargin()
        {
            BlockInstance cold, hot;
            HeatPumpDevice pump;
            ThermalSimulation simulation = ConductingRig(out cold, out hot, out pump);
            pump.Enabled = true;
            pump.PowerAvailable = 1f;
            pump.PowerSetting = 0.5f;

            ThermalNode coldNode = simulation.Solver.GetNode(cold);
            ThermalNode hotNode = simulation.Solver.GetNode(hot);

            for (int i = 0; i < 30; i++)
            {
                coldNode.Temperature = 300f;
                hotNode.Temperature = 310f;
                simulation.StepExact(1, Worlds.Shadow());
            }

            // Half the power saturates half the gap: 20 K rather than 40 K, so 10 K spare at a 10 K gap.
            Assert.Equal(10f, pump.LastOptimalMarginKelvin, 0);
        }
    }
}
