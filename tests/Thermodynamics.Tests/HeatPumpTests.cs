using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class HeatPumpTests
    {
/// <summary>Rig operation.</summary>
        private static ThermalSimulation Rig(out ThermalNode cold, out ThermalNode hot, out HeatPumpDevice pump)
        {
            return Rig(Catalog.HeatPump(), out cold, out hot, out pump);
        }

/// <summary>Rig operation.</summary>
        private static ThermalSimulation Rig(BlockModel pumpModel, out ThermalNode cold, out ThermalNode hot, out HeatPumpDevice pump)
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableConduction = false;
            settings.EnableWasteHeat = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();

            builder.Place(Catalog.LightArmor(), new Vector3I(0, 0, -1));
            builder.Place(pumpModel, Vector3I.Zero);
            builder.Place(Catalog.LightArmor(), new Vector3I(0, 0, 1));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 300f);

            cold = simulation.Solver.GetNodeAt(new Vector3I(0, 0, -1));
            hot = simulation.Solver.GetNodeAt(new Vector3I(0, 0, 1));

            pump = simulation.GetHeatPump(simulation.Grid.GetAtCell(Vector3I.Zero));
            return simulation;
        }

/// <summary>ConductingRig operation.</summary>
        private static ThermalSimulation ConductingRig(out BlockInstance cold, out BlockInstance hot,
            out HeatPumpDevice pump)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.HeavyArmor(), Vector3I.Zero);
            cold = builder.Last;
            builder.Place(Catalog.HeatPump(), new Vector3I(0, 0, 1),
/// <summary>BlockOrientation operation.</summary>
                new BlockOrientation(Base6Directions.Direction.Forward, Base6Directions.Direction.Up));
            BlockInstance pumpBlock = builder.Last;
            builder.Place(Catalog.HeavyArmor(), new Vector3I(0, 0, 2));
            hot = builder.Last;

/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableDamage = false;
            settings.Derive();

            ThermalSimulation simulation = builder.BuildSimulation(settings, 300f);
            pump = simulation.Solver.GetHeatPump(pumpBlock);
            return simulation;
        }

        [Fact]
/// <summary>APumpBindsToTheBlocksEitherSideOfIt operation.</summary>
        public void APumpBindsToTheBlocksEitherSideOfIt()
        {
            ThermalNode cold, hot;
            HeatPumpDevice pump;
/// <summary>Rig operation.</summary>
            ThermalSimulation simulation = Rig(out cold, out hot, out pump);

            Assert.NotNull(pump);
            Assert.True(pump.IsConnected);
            Assert.Equal(cold.Index, pump.ColdNodeIndex);
            Assert.Equal(hot.Index, pump.HotNodeIndex);
            Assert.Single(simulation.HeatPumps);
        }

        [Fact]
/// <summary>APumpWithNothingOnOneFaceIsNotConnected operation.</summary>
        public void APumpWithNothingOnOneFaceIsNotConnected()
        {
/// <summary>ThermalSettings operation.</summary>
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

        [Fact]
/// <summary>ARunningPumpMovesHeatAgainstTheGradient operation.</summary>
        public void ARunningPumpMovesHeatAgainstTheGradient()
        {
            ThermalNode cold, hot;
            HeatPumpDevice pump;
/// <summary>Rig operation.</summary>
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
/// <summary>APumpThatIsOffDoesNothing operation.</summary>
        public void APumpThatIsOffDoesNothing()
        {
            ThermalNode cold, hot;
            HeatPumpDevice pump;
/// <summary>Rig operation.</summary>
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
/// <summary>TheSwitchTurnsTheMechanismOff operation.</summary>
        public void TheSwitchTurnsTheMechanismOff()
        {
            ThermalNode cold, hot;
            HeatPumpDevice pump;
/// <summary>Rig operation.</summary>
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

        [Fact]
/// <summary>TheHotSideGetsTheLiftPlusTheWork operation.</summary>
        public void TheHotSideGetsTheLiftPlusTheWork()
        {
            ThermalNode cold, hot;
            HeatPumpDevice pump;
/// <summary>Rig operation.</summary>
            ThermalSimulation simulation = Rig(out cold, out hot, out pump);

            cold.Temperature = 300f;
            hot.Temperature = 400f;
            pump.Enabled = true;

            simulation.StepExact(1, Worlds.Shadow());

            Assert.True(pump.LastPowerWatts > 0f);
            Assert.Equal(pump.LastLiftedWatts + pump.LastPowerWatts, pump.LastRejectedWatts, 1);

            float lost = (300f - cold.Temperature) * cold.ThermalMass;
            float gained = (hot.Temperature - 400f) * hot.ThermalMass;
            Assert.True(gained > lost,
                "the hot side must gain more than the cold side lost, got " + gained + " vs " + lost);
        }

        [Fact]
/// <summary>TheEnergyTheGridGainsIsTheWorkDrawn operation.</summary>
        public void TheEnergyTheGridGainsIsTheWorkDrawn()
        {
            ThermalNode cold, hot;
            HeatPumpDevice pump;
/// <summary>Rig operation.</summary>
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

        [Fact]
/// <summary>LiftingAcrossAWiderGapCostsMorePerWatt operation.</summary>
        public void LiftingAcrossAWiderGapCostsMorePerWatt()
        {
            ThermalNode cold, hot;
            HeatPumpDevice pump;
/// <summary>Rig operation.</summary>
            ThermalSimulation narrow = Rig(out cold, out hot, out pump);
            cold.Temperature = 300f;
            hot.Temperature = 310f;
            pump.Enabled = true;
            narrow.StepExact(1, Worlds.Shadow());
            float narrowCoefficient = pump.LastCoefficient;

            ThermalNode cold2, hot2;
            HeatPumpDevice pump2;
/// <summary>Rig operation.</summary>
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
/// <summary>TheCoefficientIsCappedWhenThereIsNoGapToPumpAgainst operation.</summary>
        public void TheCoefficientIsCappedWhenThereIsNoGapToPumpAgainst()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();

            Assert.Equal(
                settings.HeatPumpMaxCoefficient,
                HeatPumpDevice.Coefficient(400f, 300f, settings.HeatPumpCarnotFraction, settings.HeatPumpMaxCoefficient));

            Assert.Equal(
                settings.HeatPumpMaxCoefficient,
                HeatPumpDevice.Coefficient(300f, 300.5f, settings.HeatPumpCarnotFraction, settings.HeatPumpMaxCoefficient));
        }

        [Fact]
/// <summary>APumpCannotDriveItsColdSideToAbsoluteZero operation.</summary>
        public void APumpCannotDriveItsColdSideToAbsoluteZero()
        {
            ThermalNode cold, hot;
            HeatPumpDevice pump;
/// <summary>Rig operation.</summary>
            ThermalSimulation simulation = Rig(out cold, out hot, out pump);

            cold.Temperature = 300f;
            hot.Temperature = 300f;
            pump.Enabled = true;

            simulation.StepExact(4000, Worlds.Shadow());

            Assert.True(cold.Temperature > ThermalConstants.MinimumTemperature,
                "the cold side reached " + cold.Temperature + " K");
            Assert.True(cold.Temperature < 300f, "it should still have cooled something");

            Assert.True(pump.LastCoefficient < 1f,
                "the coefficient should have collapsed, got " + pump.LastCoefficient);
        }

        [Fact]
/// <summary>ABrownedOutPumpLiftsProportionallyLess operation.</summary>
        public void ABrownedOutPumpLiftsProportionallyLess()
        {
            ThermalNode cold, hot;
            HeatPumpDevice pump;
/// <summary>Rig operation.</summary>
            ThermalSimulation full = Rig(out cold, out hot, out pump);
            cold.Temperature = 300f;
            hot.Temperature = 500f;
            pump.Enabled = true;
            pump.PowerAvailable = 1f;
            full.StepExact(1, Worlds.Shadow());
            float atFullPower = pump.LastLiftedWatts;

            ThermalNode cold2, hot2;
            HeatPumpDevice pump2;
/// <summary>Rig operation.</summary>
            ThermalSimulation half = Rig(out cold2, out hot2, out pump2);
            cold2.Temperature = 300f;
            hot2.Temperature = 500f;
            pump2.Enabled = true;
            pump2.PowerAvailable = 0.5f;
            half.StepExact(1, Worlds.Shadow());

            Assert.Equal(atFullPower * 0.5f, pump2.LastLiftedWatts, atFullPower * 0.02f);
            Assert.True(pump2.LastPowerWatts < pump.LastPowerWatts);
        }

        [Fact]
/// <summary>DemandIsWhatItWouldDrawNotWhatItGot operation.</summary>
        public void DemandIsWhatItWouldDrawNotWhatItGot()
        {
            ThermalNode cold, hot;
            HeatPumpDevice pump;
/// <summary>Rig operation.</summary>
            ThermalSimulation simulation = Rig(out cold, out hot, out pump);

            cold.Temperature = 300f;
            hot.Temperature = 500f;
            pump.Enabled = true;
            pump.PowerAvailable = 0.25f;

            simulation.StepExact(1, Worlds.Shadow());

            Assert.True(pump.LastDemandWatts > pump.LastPowerWatts,
                "demand " + pump.LastDemandWatts + " should exceed the " + pump.LastPowerWatts + " actually drawn");
            Assert.Equal(pump.MaxPowerWatts, pump.LastDemandWatts, pump.MaxPowerWatts * 0.02f);

            simulation.StepExact(20, Worlds.Shadow());
            Assert.Equal(pump.MaxPowerWatts, pump.LastDemandWatts, pump.MaxPowerWatts * 0.02f);
        }

        [Fact]
/// <summary>TheRatingBindsWhenTheGapIsSmall operation.</summary>
        public void TheRatingBindsWhenTheGapIsSmall()
        {
            ThermalNode cold, hot;
            HeatPumpDevice pump;
/// <summary>Rig operation.</summary>
            ThermalSimulation simulation = Rig(Catalog.HeatPump(10000f, 20000f), out cold, out hot, out pump);

            cold.Temperature = 300f;
            hot.Temperature = 305f;
            pump.Enabled = true;

            simulation.StepExact(1, Worlds.Shadow());

            Assert.Equal(10000f, pump.LastLiftedWatts, 10f);
            Assert.True(pump.LastPowerWatts < pump.MaxPowerWatts,
                "it should not draw full power to move its rated heat across a small gap");
        }

        [Fact]
/// <summary>ReportedWattsDescribeTheWholeStepNotTheLastSubstep operation.</summary>
        public void ReportedWattsDescribeTheWholeStepNotTheLastSubstep()
        {
            ThermalNode cold, hot;
            HeatPumpDevice pump;
/// <summary>Rig operation.</summary>
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
/// <summary>RemovingTheBlockOnTheColdFaceDisconnectsThePump operation.</summary>
        public void RemovingTheBlockOnTheColdFaceDisconnectsThePump()
        {
            ThermalNode cold, hot;
            HeatPumpDevice pump;
/// <summary>Rig operation.</summary>
            ThermalSimulation simulation = Rig(out cold, out hot, out pump);
            pump.Enabled = true;

            simulation.RemoveBlock(simulation.Grid.GetAtCell(new Vector3I(0, 0, -1)));
            simulation.RebuildAll();

            HeatPumpDevice rebuilt = simulation.HeatPumps[0];
            Assert.False(rebuilt.IsConnected);

            Assert.True(rebuilt.Enabled);
        }

        [Fact]
/// <summary>AWidePumpsFacesSitOnTheMiddleOfItsEndCaps operation.</summary>
        public void AWidePumpsFacesSitOnTheMiddleOfItsEndCaps()
        {
            HeatPumpShape wide = HeatPumpShape.Centred(Vector3I.Forward, new Vector3I(3, 3, 1), 1f, 1f);

            Assert.Equal(new Vector3I(1, 1, 0), wide.ColdCell);
            Assert.Equal(new Vector3I(1, 1, 0), wide.HotCell);

            HeatPumpShape along = HeatPumpShape.Centred(Vector3I.Forward, new Vector3I(1, 1, 3), 1f, 1f);

            Assert.Equal(new Vector3I(0, 0, 0), along.ColdCell);
            Assert.Equal(new Vector3I(0, 0, 2), along.HotCell);
        }

        [Fact]
/// <summary>AWidePumpBindsToTheBlocksOffItsMiddle operation.</summary>
        public void AWidePumpBindsToTheBlocksOffItsMiddle()
        {
/// <summary>ThermalSettings operation.</summary>
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

        [Fact]
/// <summary>TheColdFaceFollowsTheBlocksOrientation operation.</summary>
        public void TheColdFaceFollowsTheBlocksOrientation()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableConduction = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), new Vector3I(-1, 0, 0));
            builder.Place(Catalog.HeatPump(), Vector3I.Zero,
/// <summary>BlockOrientation operation.</summary>
                new BlockOrientation(Base6Directions.Direction.Left, Base6Directions.Direction.Up));
            builder.Place(Catalog.LightArmor(), new Vector3I(1, 0, 0));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 300f);

            HeatPumpDevice pump = simulation.HeatPumps[0];
            Assert.True(pump.IsConnected);

            ThermalNode left = simulation.Solver.GetNodeAt(new Vector3I(-1, 0, 0));
            Assert.Equal(left.Index, pump.ColdNodeIndex);
        }
    

        [Theory]
        [InlineData(1f)]
        [InlineData(0.5f)]
        [InlineData(0.25f)]
/// <summary>TheThrottleCapsWhatThePumpDraws operation.</summary>
        public void TheThrottleCapsWhatThePumpDraws(float setting)
        {
            BlockInstance cold, hot;
            HeatPumpDevice pump;
/// <summary>ConductingRig operation.</summary>
            ThermalSimulation simulation = ConductingRig(out cold, out hot, out pump);
            pump.Enabled = true;
            pump.PowerAvailable = 1f;
            pump.PowerSetting = setting;

            Assert.Equal(pump.MaxPowerWatts * setting, pump.SettablePowerWatts, 2);

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

        [Fact]
/// <summary>AThrottleOfZeroMovesNothingAndDrawsNothing operation.</summary>
        public void AThrottleOfZeroMovesNothingAndDrawsNothing()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.HeavyArmor(), Vector3I.Zero);
            builder.Place(Catalog.HeatPump(), new Vector3I(0, 0, 1),
/// <summary>BlockOrientation operation.</summary>
                new BlockOrientation(Base6Directions.Direction.Forward, Base6Directions.Direction.Up));
            BlockInstance pumpBlock = builder.Last;
            builder.Place(Catalog.HeavyArmor(), new Vector3I(0, 0, 2));

/// <summary>ThermalSettings operation.</summary>
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


        [Fact]
/// <summary>APumpRunningDownhillSaturatesItsCoefficientAndAchievesLittle operation.</summary>
        public void APumpRunningDownhillSaturatesItsCoefficientAndAchievesLittle()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.HeavyArmor(), Vector3I.Zero);
            BlockInstance hotBlock = builder.Last;

            builder.Place(Catalog.HeatPump(), new Vector3I(0, 0, 1),
/// <summary>BlockOrientation operation.</summary>
                new BlockOrientation(Base6Directions.Direction.Forward, Base6Directions.Direction.Up));
            BlockInstance pumpBlock = builder.Last;
            builder.Place(Catalog.HeavyArmor(), new Vector3I(0, 0, 2));
            BlockInstance coldBlock = builder.Last;

/// <summary>ThermalSettings operation.</summary>
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

            Assert.Equal(8f, pump.LastCoefficient, 2);
            Assert.True(pump.LastLiftedWatts > 50000f, "it does move heat: " + pump.LastLiftedWatts);

            Assert.True(Math.Abs(coldNode.LastConductionWatts) > pump.LastLiftedWatts,
                "conduction " + coldNode.LastConductionWatts + " W should dominate the pump's "
                + pump.LastLiftedWatts + " W, which is what makes this pointless rather than strong");
        }

        [Fact]
/// <summary>CascadingPumpsHelpsByABoundedAmount operation.</summary>
        public void CascadingPumpsHelpsByABoundedAmount()
        {
/// <summary>CascadeCoefficient operation.</summary>
            float one = CascadeCoefficient(1);
/// <summary>CascadeCoefficient operation.</summary>
            float four = CascadeCoefficient(4);
/// <summary>CascadeCoefficient operation.</summary>
            float eight = CascadeCoefficient(8);

            Assert.True(four > one, "four stages " + four + " should beat one stage " + one);

            Assert.True(eight < four * 1.5f,
                "eight stages " + eight + " against four " + four + " is not a plateau");
            Assert.True(eight < 1f,
                "no arrangement should lift more heat than the work it spends across a gap this wide");
        }

/// <summary>CascadeCoefficient operation.</summary>
        private static float CascadeCoefficient(int stages)
        {
            GridBuilder builder = GridBuilder.Large();
/// <summary>List operation.</summary>
            List<BlockInstance> blocks = new List<BlockInstance>();

            builder.Place(Catalog.HeavyArmor(), Vector3I.Zero);
            blocks.Add(builder.Last);

            for (int i = 0; i < stages; i++)
            {
                int z = (i * 2) + 1;
                builder.Place(Catalog.HeatPump(), new Vector3I(0, 0, z),
/// <summary>BlockOrientation operation.</summary>
                    new BlockOrientation(Base6Directions.Direction.Forward, Base6Directions.Direction.Up));
                builder.Place(Catalog.HeavyArmor(), new Vector3I(0, 0, z + 1));
                blocks.Add(builder.Last);
            }

/// <summary>ThermalSettings operation.</summary>
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
    
        [Theory]
        [InlineData(310f, 30f)]     // a 10 K gap against a 40 K allowance: 30 K spare
        [InlineData(340f, 0f)]      // exactly at the limit
        [InlineData(400f, -60f)]    // 100 K gap: sixty degrees too wide
/// <summary>TheOptimalMarginSaysHowFarTheGapIsFromFullOutput operation.</summary>
        public void TheOptimalMarginSaysHowFarTheGapIsFromFullOutput(float hotSide, float expected)
        {
            BlockInstance cold, hot;
            HeatPumpDevice pump;
/// <summary>ConductingRig operation.</summary>
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

            if (expected >= 0f) Assert.Equal(pump.RatedWatts, pump.LastLiftedWatts, 0);
            else Assert.True(pump.LastLiftedWatts < pump.RatedWatts);
        }

        [Fact]
/// <summary>ThrottlingShrinksTheOptimalMargin operation.</summary>
        public void ThrottlingShrinksTheOptimalMargin()
        {
            BlockInstance cold, hot;
            HeatPumpDevice pump;
/// <summary>ConductingRig operation.</summary>
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

            Assert.Equal(10f, pump.LastOptimalMarginKelvin, 0);
        }
    }
}
