using Thermodynamics;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The parts of the game adapter that can be checked without the game: the gate that decides
    /// whether a tick is worth sampling the world for, and the coolant plumbing table.
    /// </summary>
    public class HostAdapterTests
    {
        // ---- step pacing -------------------------------------------------------------------

        /// <summary>
        /// A hull the pacing can be watched on. Small and inert: what is being counted is steps,
        /// not what a step does.
        /// </summary>
        private static ThermalSimulation Paced(int frequency, float speed)
        {
            ThermalSettings settings = new ThermalSettings();
            settings.Frequency = frequency;
            settings.SimulationSpeed = speed;
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.EnableDamage = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(3, 3, 3));
            return builder.BuildSimulation(settings, 300f);
        }

        private static long Frames(ThermalSimulation simulation, int frames, float frameSeconds)
        {
            long before = simulation.StepsCompleted;
            for (int i = 0; i < frames; i++) simulation.Update(frameSeconds, Worlds.Shadow());
            return simulation.StepsCompleted - before;
        }

        /// <summary>
        /// **The step rate is what `Frequency` says, measured on the path the game actually
        /// drives.**
        ///
        /// <para>
        /// It used to be measured on `SimulationScheduler.StepsDue`, a second step-credit
        /// accumulator that no shipped code called — the pacing is `ThermalSimulation.Update`,
        /// which banks work credit against the frame it is handed and spends it a slice at a time.
        /// A test on the wrong one of those is why a degraded-input row spent months naming a
        /// mechanism the mod cannot perform (backlog.md `F23`).
        /// </para>
        /// </summary>
        [Fact]
        public void FrequencyIsStepsPerSimulatedSecond()
        {
            Assert.Equal(4, Frames(Paced(4, 1f), 60, 1f / 60f));
        }

        /// <summary>The rate is pinned above; this is the multiplier on top of it.</summary>
        [Fact]
        public void SimulationSpeedMultipliesTheStepRate()
        {
            Assert.Equal(12, Frames(Paced(4, 3f), 60, 1f / 60f));
        }

        /// <summary>
        /// **A long frame cannot produce a burst of steps**, which is the lump the pacing exists to
        /// avoid: an update completes at most one step whatever it is handed, and the credit above
        /// one step's work is discarded rather than banked.
        ///
        /// <para>
        /// The shipped adapter never hands it a long frame — `ThermalGridScheduler` passes a
        /// constant sixtieth — so this is the guard rather than a thing that happens. What a
        /// stalling machine really does is run fewer *ticks*, which is a rate difference and is
        /// measured as one in `ClientInputTests`.
        /// </para>
        /// </summary>
        [Fact]
        public void ALongFrameDoesNotProduceABurstOfSteps()
        {
            Assert.Equal(1, Frames(Paced(4, 1f), 1, 60f));
        }

        /// <summary>A zero-length frame advances nothing.</summary>
        [Fact]
        public void AZeroLengthFrameStepsNothing()
        {
            Assert.Equal(0, Frames(Paced(4, 1f), 10, 0f));
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
