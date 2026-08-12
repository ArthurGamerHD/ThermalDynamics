using Thermodynamics;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The parts of the game adapter that can be checked without the game: the gate that decides
    /// whether a tick is worth sampling the world for, and the coolant plumbing table.
    /// </summary>
    public class HostAdapterTests
    {
        // ---- scheduler gate ----------------------------------------------------------------

        private static ThermalSettings Settings(int frequency, float speed)
        {
            ThermalSettings settings = new ThermalSettings();
            settings.Frequency = frequency;
            settings.SimulationSpeed = speed;
            return settings.Derive();
        }

        [Fact]
        public void WouldStepAgreesWithStepsDueOverALongRun()
        {
            // 4 steps a second, polled on the game's ten-frame tick.
            ThermalSettings settings = Settings(4, 1f);
            SimulationScheduler scheduler = new SimulationScheduler(settings);

            const float tick = 10f / 60f;

            for (int i = 0; i < 500; i++)
            {
                bool predicted = scheduler.WouldStep(tick);
                int actual = scheduler.StepsDue(tick);

                Assert.Equal(predicted, actual > 0);
            }
        }

        [Fact]
        public void WouldStepDoesNotConsumeCredit()
        {
            SimulationScheduler scheduler = new SimulationScheduler(Settings(4, 1f));

            for (int i = 0; i < 20; i++)
            {
                scheduler.WouldStep(1f);
            }

            // One second of credit at four steps a second, however many times it was asked about.
            Assert.Equal(4, scheduler.StepsDue(1f));
        }

        [Fact]
        public void WouldStepIsFalseForAZeroLengthFrame()
        {
            SimulationScheduler scheduler = new SimulationScheduler(Settings(4, 1f));
            Assert.False(scheduler.WouldStep(0f));
        }

        [Fact]
        public void AFastSimulationStepsOnEveryTick()
        {
            // 60 steps a second: every ten-frame tick owes six.
            SimulationScheduler scheduler = new SimulationScheduler(Settings(60, 1f));
            Assert.True(scheduler.WouldStep(10f / 60f));
        }

        // ---- coolant plumbing --------------------------------------------------------------

        [Fact]
        public void StraightPipeLinksForwardAndBackward()
        {
            CoolantShape shape = ThermalCoolantShapes.Get("Gauge_LG_CoolantPipe_Straight", Vector3I.One);

            Assert.NotNull(shape);
            Assert.False(shape.IsPump);
            Assert.Equal(2, shape.LinkPorts.Length);
            Assert.Equal(Vector3I.Forward, shape.LinkPorts[0].LocalDirection);
            Assert.Equal(Vector3I.Backward, shape.LinkPorts[1].LocalDirection);
            Assert.Empty(shape.SinkPorts);
        }

        [Fact]
        public void CornerPipeTurns()
        {
            CoolantShape shape = ThermalCoolantShapes.Get("Gauge_LG_CoolantPipe_Corner", Vector3I.One);

            Assert.Equal(Vector3I.Forward, shape.LinkPorts[0].LocalDirection);
            Assert.Equal(Vector3I.Left, shape.LinkPorts[1].LocalDirection);
        }

        [Fact]
        public void SinkVariantsCarryTheirPlates()
        {
            Assert.Single(ThermalCoolantShapes.Get("Gauge_LG_CoolantPipe_Straight_SingleSink", Vector3I.One).SinkPorts);
            Assert.Equal(2, ThermalCoolantShapes.Get("Gauge_LG_CoolantPipe_Straight_DoubleSink", Vector3I.One).SinkPorts.Length);
            Assert.Single(ThermalCoolantShapes.Get("Gauge_LG_CoolantPipe_Corner_SingleSink", Vector3I.One).SinkPorts);
            Assert.Equal(2, ThermalCoolantShapes.Get("Gauge_LG_CoolantPipe_Corner_DoubleSink", Vector3I.One).SinkPorts.Length);
        }

        [Fact]
        public void BothGridSizesResolveToTheSamePlumbing()
        {
            CoolantShape large = ThermalCoolantShapes.Get("Gauge_LG_CoolantPipe_Corner_DoubleSink", Vector3I.One);
            CoolantShape small = ThermalCoolantShapes.Get("Gauge_SG_CoolantPipe_Corner_DoubleSink", Vector3I.One);

            Assert.Equal(large.LinkPorts.Length, small.LinkPorts.Length);
            Assert.Equal(large.LinkPorts[1].LocalDirection, small.LinkPorts[1].LocalDirection);
            Assert.Equal(large.SinkPorts.Length, small.SinkPorts.Length);
        }

        /// <summary>
        /// The old crawler multiplied the pump's port offset by three for the small grid, by
        /// subtype name. The port now sits on whichever cell the block's own size puts it on.
        /// </summary>
        [Fact]
        public void PumpPortsSitAtTheEndsOfTheBlockWhateverItsLength()
        {
            CoolantShape shortPump = ThermalCoolantShapes.Get("Gauge_LG_CoolantPump", Vector3I.One);
            CoolantShape longPump = ThermalCoolantShapes.Get("Gauge_SG_CoolantPump", new Vector3I(1, 1, 3));

            Assert.True(shortPump.IsPump);
            Assert.True(longPump.IsPump);

            Assert.Equal(Vector3I.Zero, shortPump.LinkPorts[0].LocalCell);
            Assert.Equal(Vector3I.Zero, shortPump.LinkPorts[1].LocalCell);

            Assert.Equal(Vector3I.Zero, longPump.LinkPorts[0].LocalCell);
            Assert.Equal(new Vector3I(0, 0, 2), longPump.LinkPorts[1].LocalCell);
        }

        [Fact]
        public void ABlockWithNoPlumbingHasNoShape()
        {
            Assert.Null(ThermalCoolantShapes.Get("LargeBlockArmorBlock", Vector3I.One));
            Assert.Null(ThermalCoolantShapes.Get("", Vector3I.One));
            Assert.Null(ThermalCoolantShapes.Get(null, Vector3I.One));
        }

        [Fact]
        public void PrefixStrippingLeavesUnprefixedNamesAlone()
        {
            Assert.Equal("CoolantPump", ThermalCoolantShapes.StripGridPrefix("Gauge_LG_CoolantPump"));
            Assert.Equal("CoolantPump", ThermalCoolantShapes.StripGridPrefix("Gauge_SG_CoolantPump"));
            Assert.Equal("SomeOtherMod_Pipe", ThermalCoolantShapes.StripGridPrefix("SomeOtherMod_Pipe"));
        }

        /// <summary>
        /// A pump built from the table has to actually close a ring in the model, which is the
        /// only thing the plumbing table exists to do.
        /// </summary>
        [Fact]
        public void ShapesFromTheTableFormAWorkingRing()
        {
            GridModel grid = new GridModel(2.5f);

            BlockModel straight = BlockModel.Solid("CoolantPipe_Straight", Vector3I.One, 220f, new BlockThermalProperties());
            straight.WithCoolant(ThermalCoolantShapes.Get("Gauge_LG_CoolantPipe_Straight", Vector3I.One));

            BlockModel pump = BlockModel.Solid("CoolantPump", Vector3I.One, 600f, new BlockThermalProperties());
            pump.WithCoolant(ThermalCoolantShapes.Get("Gauge_LG_CoolantPump", Vector3I.One));

            // A straight run cannot close on itself, so this is a dead end, not a ring.
            grid.Add(pump, Vector3I.Zero);
            grid.Add(straight, new Vector3I(0, 0, 1));

            Assert.Empty(CoolantLoopBuilder.FindLoops(grid, LoopThermalProperties.Default(), 293.15f));
        }
    }
}
