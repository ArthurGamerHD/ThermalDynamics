using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// How the coolant is carried round the ring.
    ///
    /// The fluid does not move: the ring's origin does. Parcels sit in a fixed array and each pipe
    /// reads the parcel currently passing through it. Because a pipe's index is a whole number, the
    /// rounded offset collapses to one integer shift shared by every pipe — which is what makes it a
    /// bijection at any speed, exact, and free of the stability limit a blended scheme has.
    /// </summary>
    public class CoolantFlowTests
    {
        private static ThermalSettings Isolated(float segmentsPerSecond = 4f)
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.EnableFriction = false;
            settings.EnableDamage = false;
            settings.MaxSubsteps = 4096;
            settings.MaxLinkVisitsPerStep = 0;
            return settings.Derive();
        }

        private static ThermalSimulation Ring(int width, int depth, float segmentsPerSecond,
            out CoolantLoop loop)
        {
            GridBuilder builder = GridBuilder.Large();
            List<Vector3I> cells = PipeFitter.RectangleXZ(Vector3I.Zero, width, depth);
            PipeFitter.BuildRing(builder, cells);

            ThermalSimulation simulation = builder.BuildSimulation(Isolated(), 300f);
            loop = simulation.Solver.Loops[0];

            loop.Properties.SegmentsPerSecondAtFullFlow = segmentsPerSecond;
            loop.RefreshFlow();
            return simulation;
        }

        /// <summary>
        /// Every pipe reads a different parcel, at every rotation. Two pipes sharing one parcel would
        /// double-couple it and starve another; a skipped parcel would hold heat nothing could reach.
        /// </summary>
        [Fact]
        public void TheParcelMappingIsABijectionAtEveryRotation()
        {
            CoolantLoop loop;
            ThermalSimulation simulation = Ring(5, 5, 4f, out loop);

            int count = loop.PipeCount;
            Assert.Equal(16, count);

            // Step far enough to pass through every rotation several times over.
            for (int step = 0; step < 200; step++)
            {
                simulation.StepExact(1, Worlds.Shadow());

                bool[] seen = new bool[count];
                for (int pipe = 0; pipe < count; pipe++)
                {
                    int parcel = loop.ParcelOf(pipe);

                    Assert.InRange(parcel, 0, count - 1);
                    Assert.False(seen[parcel],
                        "two pipes read parcel " + parcel + " at step " + step);
                    seen[parcel] = true;
                }
            }
        }

        /// <summary>
        /// Carrying the fluid neither creates nor destroys heat, because it only changes which parcel
        /// is where. Asserted with the exchange switched off, so rotation is the only thing acting.
        /// </summary>
        [Fact]
        public void RotationAloneMovesHeatWithoutChangingIt()
        {
            CoolantLoop loop;
            ThermalSimulation simulation = Ring(5, 5, 4f, out loop);

            // An uneven ring, so a scheme that averaged would show up immediately.
            for (int i = 0; i < loop.PipeCount; i++)
            {
                loop.SetSegmentTemperature(i, 250f + (i * 40f));
            }

            float before = loop.Energy;
            float hottestBefore = loop.HottestSegment;
            float coldestBefore = loop.ColdestSegment;

            for (int i = 0; i < 500; i++) loop.Advect(1f / 24f);

            // Exactly, not approximately: nothing was arithmetic on a temperature.
            Assert.Equal(before, loop.Energy, 3);
            Assert.Equal(hottestBefore, loop.HottestSegment, 3);
            Assert.Equal(coldestBefore, loop.ColdestSegment, 3);
        }

        /// <summary>
        /// A hot parcel arrives at the far side of the ring still hot. Plug flow: the profile travels
        /// rather than smearing, and only exchange with the pipes smooths it.
        /// </summary>
        [Fact]
        public void AHotParcelTravelsRoundTheRingIntact()
        {
            CoolantLoop loop;
            Ring(5, 5, 4f, out loop);

            int count = loop.PipeCount;
            loop.Temperature = 300f;
            loop.SetSegmentTemperature(0, 900f);

            float peak = loop.HottestSegment;
            Assert.True(peak > 400f, "the pulse should start hot: " + peak);

            // Which pipe holds the pulse, before and after carrying it a quarter of the way round.
            int startPipe = PipeHoldingThePulse(loop);

            for (int i = 0; i < count / 4; i++) loop.Advect(1f / 4f);

            int nowPipe = PipeHoldingThePulse(loop);

            Assert.NotEqual(startPipe, nowPipe);
            Assert.Equal(peak, loop.HottestSegment, 3);
        }

        private static int PipeHoldingThePulse(CoolantLoop loop)
        {
            int worst = 0;
            float best = float.MinValue;
            for (int i = 0; i < loop.PipeCount; i++)
            {
                if (loop.SegmentTemperature(i) <= best) continue;
                best = loop.SegmentTemperature(i);
                worst = i;
            }
            return worst;
        }

        /// <summary>
        /// Flow speed costs the solver nothing, so it can be set for how the game should feel.
        ///
        /// A blended scheme is stable only below one parcel per substep, which made the flow rate a
        /// compromise with the integrator: this is the property that removed that.
        /// </summary>
        [Theory]
        [InlineData(1f)]
        [InlineData(4f)]
        [InlineData(50f)]
        [InlineData(5000f)]
        public void FlowSpeedDoesNotCostSubsteps(float segmentsPerSecond)
        {
            CoolantLoop slow, fast;
            ThermalSimulation a = Ring(5, 5, 1f, out slow);
            ThermalSimulation b = Ring(5, 5, segmentsPerSecond, out fast);

            a.StepExact(20, Worlds.Shadow());
            b.StepExact(20, Worlds.Shadow());

            Assert.Equal(a.Solver.LastRequiredSubsteps, b.Solver.LastRequiredSubsteps, 3);

            // And it stays finite however absurd the rate.
            Assert.False(float.IsNaN(fast.Temperature));
            Assert.False(float.IsInfinity(fast.Temperature));
        }

        /// <summary>
        /// A ring whose pumps are all stopped carries nothing, however hot one end of it gets. This is
        /// the behaviour the whole model exists for.
        /// </summary>
        [Fact]
        public void AStoppedPumpLeavesTheFarSideOfTheRingCold()
        {
            Dictionary<int, Vector3I> sinks = new Dictionary<int, Vector3I>();
            sinks[2] = Vector3I.Down;

            GridBuilder builder = GridBuilder.Large();
            List<Vector3I> cells = PipeFitter.RectangleXZ(Vector3I.Zero, 6, 5);
            PipeFitter.BuildRing(builder, cells, -1, sinks);
            builder.Place(Catalog.Reactor(), cells[2] + Vector3I.Down).Producing(500000f);

            ThermalSimulation simulation = builder.BuildSimulation(Isolated(), 300f);
            CoolantLoop loop = simulation.Solver.Loops[0];

            // Stop every pump in the ring.
            for (int i = 0; i < loop.Pumps.Count; i++) loop.Pumps[i].Enabled = false;
            loop.RefreshFlow();
            Assert.Equal(0f, loop.FlowSegmentsPerSecond);

            simulation.StepExact(2000, Worlds.Shadow());

            float spread = loop.HottestSegment - loop.ColdestSegment;
            Assert.True(spread > 50f,
                "with nothing circulating the ring should be unevenly hot, spread was " + spread + " K");

            // Now run the pumps and the ring evens out.
            for (int i = 0; i < loop.Pumps.Count; i++) loop.Pumps[i].Enabled = true;
            loop.RefreshFlow();
            simulation.StepExact(2000, Worlds.Shadow());

            float mixed = loop.HottestSegment - loop.ColdestSegment;
            Assert.True(mixed < spread,
                "circulating should even the ring out: " + spread + " K then " + mixed + " K");
        }
    
        /// <summary>
        /// The well-mixed model is one parcel holding the whole ring's coolant, so it costs one
        /// accumulator and one integration however long the ring is.
        ///
        /// Worth pinning because the first version of the toggle reproduced the old *behaviour* while
        /// keeping the new *cost*: it carried a parcel per pipe and levelled them afterwards, so a
        /// setting whose only reason to exist is to be cheaper was not.
        /// </summary>
        [Fact]
        public void TheWellMixedRingIsOneParcelHoldingEverything()
        {
            ThermalSettings settings = Isolated();
            settings.WellMixedCoolant = true;

            GridBuilder builder = GridBuilder.Large();
            PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, 5, 5));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 300f);
            CoolantLoop mixed = simulation.Solver.Loops[0];

            Assert.Equal(16, mixed.PipeCount);
            Assert.Equal(1, mixed.ParcelCount);

            // The one parcel holds what sixteen would have held between them.
            Assert.Equal(mixed.ThermalMass, mixed.SegmentThermalMass, 3);

            // Every pipe reads it, so there is no spread to have.
            for (int i = 0; i < mixed.PipeCount; i++)
            {
                Assert.Equal(0, mixed.ParcelOf(i));
            }
            Assert.Equal(mixed.HottestSegment, mixed.ColdestSegment, 3);
        }

        /// <summary>
        /// Both models hold the same coolant, so a ring at one temperature has the same heat either
        /// way. The models differ in where that heat can go, not in how much there is.
        /// </summary>
        [Fact]
        public void BothModelsHoldTheSameHeatAtTheSameTemperature()
        {
            ThermalSettings mixedSettings = Isolated();
            mixedSettings.WellMixedCoolant = true;

            GridBuilder a = GridBuilder.Large();
            PipeFitter.BuildRing(a, PipeFitter.RectangleXZ(Vector3I.Zero, 5, 5));
            CoolantLoop segmented = a.BuildSimulation(Isolated(), 400f).Solver.Loops[0];

            GridBuilder b = GridBuilder.Large();
            PipeFitter.BuildRing(b, PipeFitter.RectangleXZ(Vector3I.Zero, 5, 5));
            CoolantLoop mixed = b.BuildSimulation(mixedSettings, 400f).Solver.Loops[0];

            Assert.Equal(segmented.ThermalMass, mixed.ThermalMass, 2);
            Assert.Equal(segmented.Energy, mixed.Energy, 1);
        }
    }
}
