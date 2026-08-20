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
            settings.MaxElementVisitsPerStep = 0;
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

            loop.Properties.LargeGridFlowRate = segmentsPerSecond * loop.ParcelLengthMetres;
            loop.Properties.SmallGridFlowRate = loop.Properties.LargeGridFlowRate;
            loop.RefreshFlow();
            return simulation;
        }

        /// <summary>
        /// Each grid size has its own flow dial, and equal dials mean equal speed.
        ///
        /// The dial used to be parcels per second, and a parcel is one pipe block — so one number
        /// drove a large-grid ring at 10 m/s and a small-grid ring at 2 m/s. Nothing said so, and
        /// the terminal reported both honestly, which made small-grid pumps look simply worse.
        ///
        /// Stating it in metres per second removed the accident; splitting it in two puts the
        /// choice back, deliberately. Shipped equal, so the grids behave alike until someone
        /// decides they should not.
        /// </summary>
        [Fact]
        public void EachGridSizeHasItsOwnFlowRate()
        {
            CoolantLoop large = RingOfSize(Catalog.LargeGridSize);
            CoolantLoop small = RingOfSize(Catalog.SmallGridSize);

            // Shipped equal: the same speed on both, and the sign is only which way round the ring
            // the pump drives it.
            Assert.Equal(10f, Math.Abs(large.FlowMetresPerSecond), 3);
            Assert.Equal(10f, Math.Abs(small.FlowMetresPerSecond), 3);

            // Same speed, shorter parcels: five times the rotation rate on the small grid.
            Assert.Equal(4f, Math.Abs(large.FlowSegmentsPerSecond), 3);
            Assert.Equal(20f, Math.Abs(small.FlowSegmentsPerSecond), 3);

            // And they are genuinely independent: moving one leaves the other where it was.
            small.Properties.SmallGridFlowRate = 2f;
            large.Properties.SmallGridFlowRate = 2f;
            small.RefreshFlow();
            large.RefreshFlow();

            Assert.Equal(2f, Math.Abs(small.FlowMetresPerSecond), 3);
            Assert.Equal(10f, Math.Abs(large.FlowMetresPerSecond), 3);
        }

        private static CoolantLoop RingOfSize(float gridSize)
        {
            GridBuilder builder = new GridBuilder(gridSize);
            PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, 4, 4));

            ThermalSimulation simulation = builder.BuildSimulation(Isolated(), 300f);
            CoolantLoop loop = simulation.Solver.Loops[0];
            loop.RefreshFlow();
            return loop;
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
    
        // ---- flow faster than the step can resolve -----------------------------------------
        //
        // A rotation advancing by a constant k parcels per substep means pipe i only ever reads
        // parcels in the subgroup k generates modulo N. Whenever gcd(k, N) > 1 the ring splits into
        // that many disjoint sets and heat cannot cross between them. Measured on an eight parcel
        // ring at a one second step, a pipe saw four of eight parcels at two per substep, two at
        // four, and one at eight — the ring frozen at maximum pump speed with every figure about it
        // looking healthy. These pin the behaviour that replaced it.

        /// <summary>
        /// A ring that is short of parcels for its flow rate must not transport *less* than a slower
        /// one. Before mixing was added the spread across a heated ring went 49.5 K at one parcel per
        /// substep, then 89, 202 and 583 K as the flow rose — faster circulation making the ring less
        /// even, which is the exact inversion of what a pump does.
        /// </summary>
        [Theory]
        [InlineData(2f)]
        [InlineData(4f)]
        [InlineData(8f)]
        [InlineData(64f)]
        public void FasterFlowNeverEvensTheRingOutLessThanSlowFlow(float fastRate)
        {
            float slow = SpreadAcrossAHeatedRing(1f);
            float fast = SpreadAcrossAHeatedRing(fastRate);

            Assert.True(fast <= slow,
                "at " + fastRate + " parcels per second the ring's spread was " + fast
                + " K against " + slow + " K at one; faster flow is transporting less");
        }

        /// <summary>
        /// As the flow outruns the step the ring converges on well mixed, which is what a ring
        /// circulating far faster than it is observed physically is.
        /// </summary>
        [Fact]
        public void VeryFastFlowConvergesOnAWellMixedRing()
        {
            float spread = SpreadAcrossAHeatedRing(64f);

            Assert.True(spread < 5f,
                "a ring lapping 64 times a second should be near uniform, spread was " + spread + " K");
        }

        /// <summary>The mixing that replaces carrying is reported, so the regime is visible.</summary>
        [Fact]
        public void MixingReportsWhenFlowOutrunsTheStep()
        {
            CoolantLoop loop;
            Ring(3, 3, 4f, out loop);

            // Below a parcel per substep nothing is mixed: this is plug flow.
            Assert.Equal(0f, loop.MixingFraction(0.25f));

            // At two parcels per substep, half of the transport is mixing.
            Assert.Equal(0.5f, loop.MixingFraction(0.5f), 3);

            // And it approaches one rather than exceeding it.
            Assert.True(loop.MixingFraction(100f) < 1f);
            Assert.True(loop.MixingFraction(100f) > 0.99f);
        }

        /// <summary>
        /// Mixing conserves heat, because the parcels have equal capacity: moving each of them the same
        /// fraction of the way to their own mean cannot change the sum.
        ///
        /// Asserted as a ratio rather than as an absolute figure. It is exact in exact arithmetic; in
        /// single precision, 200 rounds of it on a ring holding 7.9 MJ leave half a joule behind, which
        /// is the last representable bit at that magnitude rather than a leak.
        /// </summary>
        [Fact]
        public void MixingConservesHeat()
        {
            CoolantLoop loop;
            Ring(5, 5, 1000f, out loop);

            for (int i = 0; i < loop.PipeCount; i++)
            {
                loop.SetSegmentTemperature(i, 200f + (i * 60f));
            }

            float before = loop.Energy;
            for (int i = 0; i < 200; i++) loop.Advect(1f / 4f);

            Assert.Equal(1f, loop.Energy / before, 5);
        }

        /// <summary>
        /// The spread across parcels of a ring heated at one point, after it has settled. A ring that
        /// transports evens out; one that aliases does not.
        /// </summary>
        /// <summary>
        /// **A fixed defect, guarded.** The loop path used to have a stiffness ceiling.
        ///
        /// <c>SpreadAcrossAHeatedRing</c> deliberately runs one substep across a whole second and
        /// relies on the overshoot clamps to keep that bounded. They do — up to a point. Raising the
        /// pipe's conductivity walks the ring off a cliff:
        ///
        /// <code>
        ///   effective W/(m K)   360    480    600    960
        ///   flow tests failing    0      3      5      5
        /// </code>
        ///
        /// The cause was the loop's clamp, not the material. It bounded each link against the
        /// parcel's whole heat capacity — correct for a pair, and wrong for a parcel carrying more
        /// than one link, which gets twice the energy equalising it takes and overshoots further
        /// every substep. A pipe with a sink face has exactly that shape; a well-mixed ring puts
        /// every link in the ring on one parcel.
        ///
        /// With the ring's own limit applied the same case settles at under a kelvin, and the
        /// shipped pipes are copper again. This test now guards that rather than recording it.
        /// </summary>
        [Theory]
        [InlineData(400f, 5f)]      // copper, what ships
        [InlineData(1600f, 100f)]   // four times copper: degraded but bounded, not 291,360 K
        public void TheLoopPathSurvivesAVeryConductivePipe(float conductivity, float tolerance)
        {
            try
            {
                // Only the coolant, identified by its own specific heat, so the reactor beside it
                // stays exactly as it was and the stiffness change has one source.
                Catalog.MaterialOverride = properties =>
                {
                    if (Math.Abs(properties.SpecificHeat - 385f) < 0.5f) properties.Conductivity = conductivity;
                    return properties;
                };

                float spread = SpreadAcrossAHeatedRing(64f);

                Assert.True(spread < tolerance,
                    "a ring lapping 64 times a second should stay bounded whatever the pipe is made "
                    + "of; at " + conductivity + " W/(m K) it spread " + spread + " K");
            }
            finally
            {
                Catalog.MaterialOverride = null;
            }
        }

        private static float SpreadAcrossAHeatedRing(float segmentsPerSecond)
        {
            ThermalSettings settings = new ThermalSettings();
            settings.Frequency = 1;                  // a one second step
            settings.EnableEnvironment = false;
            settings.EnableDamage = false;
            settings.MaxSubsteps = 1;                // one substep, so the whole second is one h
            settings.MaxSubstepsPerBlock = 0;
            settings.MaxElementVisitsPerStep = 0;
            settings.Derive();

            Dictionary<int, Vector3I> sinks = new Dictionary<int, Vector3I>();
            sinks[2] = Vector3I.Down;

            GridBuilder builder = GridBuilder.Large();
            List<Vector3I> cells = PipeFitter.RectangleXZ(Vector3I.Zero, 3, 3);
            PipeFitter.BuildRing(builder, cells, -1, sinks);
            builder.Place(Catalog.Reactor(), cells[2] + Vector3I.Down).Producing(300000f);

            ThermalSimulation simulation = builder.BuildSimulation(settings, 300f);
            CoolantLoop loop = simulation.Solver.Loops[0];
            loop.Properties.LargeGridFlowRate = segmentsPerSecond * loop.ParcelLengthMetres;
            loop.Properties.SmallGridFlowRate = loop.Properties.LargeGridFlowRate;
            loop.RefreshFlow();

            simulation.StepExact(300, Worlds.Shadow());
            return loop.HottestSegment - loop.ColdestSegment;
        }
    
        // ---- what the pumps cost ------------------------------------------------------------

        /// <summary>
        /// A pump's draw is linear in its speed, and that is what stops pump count being a discount.
        ///
        /// The affinity law — power with the cube of speed, which is what a real centrifugal pump does
        /// — was tried first and is an exploit here rather than a trade. Flow goes as the square root
        /// of combined pumping, so a given flow from N pumps needs each at speed K/N, and cubed power
        /// makes the bill fall as 1/N squared: ten pumps idling cost a hundredth of one pump working,
        /// and the best build is always "more pumps, all barely on".
        /// </summary>
        [Fact]
        public void PumpPowerIsLinearInSpeed()
        {
            CoolantPump pump = new CoolantPump();
            pump.MaxPowerWatts = 20000f;

            pump.Speed = 1f;
            Assert.Equal(20000f, pump.DemandWatts, 2);

            pump.Speed = 0.5f;
            Assert.Equal(10000f, pump.DemandWatts, 2);

            pump.Speed = 0.25f;
            Assert.Equal(5000f, pump.DemandWatts, 2);

            // Off is off, whatever the slider says.
            pump.Enabled = false;
            Assert.Equal(0f, pump.DemandWatts);
        }

        /// <summary>
        /// The property linear power buys: the bill for a given flow is the same however many pumps
        /// deliver it. A second pump is redundancy and headroom, not a cheaper way to move the fluid.
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        [InlineData(9)]
        public void TheBillForAGivenFlowDoesNotDependOnHowManyPumpsDeliverIt(int pumpCount)
        {
            CoolantLoop loop;
            Ring(9, 9, 4f, out loop);

            // Reuse the ring's own pump entry as a template, then stand in the pumps by hand: what is
            // being checked is the arithmetic relating flow to the bill, not the ring's plumbing.
            loop.Pumps.Clear();

            // Each pump at the speed that lands the same total demand, so the same flow.
            const float totalDemand = 1f;
            for (int i = 0; i < pumpCount; i++)
            {
                CoolantPump pump = new CoolantPump();
                pump.MaxPowerWatts = 20000f;
                pump.Speed = totalDemand / pumpCount;
                loop.Pumps.Add(pump);
            }

            loop.RefreshFlow();

            float bill = 0f;
            for (int i = 0; i < loop.Pumps.Count; i++) bill += loop.Pumps[i].DemandWatts;

            // Same flow...
            Assert.Equal(loop.Properties.FlowRateFor(loop.ParcelLengthMetres) / loop.ParcelLengthMetres,
                loop.FlowSegmentsPerSecond, 3);

            // ...for the same money, whatever the pump count.
            Assert.Equal(20000f, bill, 1);
        }

        /// <summary>
        /// Flow rises with the square root of combined pumping: four pumps carry twice one pump's
        /// flow, not four times. Real parallel-pump behaviour against a fixed circuit, and honest
        /// diminishing returns on redundancy.
        /// </summary>
        [Fact]
        public void FlowRisesWithTheSquareRootOfCombinedPumping()
        {
            CoolantLoop loop;
            Ring(9, 9, 10f, out loop);

            loop.Pumps.Clear();
            for (int i = 0; i < 4; i++)
            {
                CoolantPump pump = new CoolantPump();
                pump.MaxPowerWatts = 20000f;
                loop.Pumps.Add(pump);

                loop.RefreshFlow();
                Assert.Equal(10f * (float)Math.Sqrt(i + 1), loop.FlowSegmentsPerSecond, 3);
            }
        }
    
        // ---- which way round the ring turns -------------------------------------------------

        /// <summary>
        /// A pump fitted the other way round drives the loop backwards rather than not working, and a
        /// loop driven backwards works exactly as well. A build mistake becomes a build choice.
        /// </summary>
        [Fact]
        public void APumpFittedBackwardsDrivesTheRingInReverse()
        {
            CoolantLoop forward = RingWithPump(false);
            CoolantLoop reverse = RingWithPump(true);

            Assert.Single(forward.Pumps);
            Assert.Single(reverse.Pumps);

            // Which sign counts as "forward" is not fixed: a ring is traced from an arbitrary block
            // in whichever direction its first port leads, so direction is only ever meaningful
            // relative to the ring's own order. What must hold is that turning the pump round flips
            // it, and that the ring then turns the other way at the same speed.
            Assert.Equal(-forward.Pumps[0].Direction, reverse.Pumps[0].Direction);
            Assert.Equal(forward.FlowSegmentsPerSecond, -reverse.FlowSegmentsPerSecond, 3);
            Assert.NotEqual(0f, reverse.FlowSegmentsPerSecond);
        }

        /// <summary>
        /// A ring driven backwards carries heat as well as one driven forwards.
        ///
        /// Close rather than identical: the sink does not sit symmetrically between the pump and
        /// itself, so reversing the flow changes how far the heated coolant travels before it comes
        /// back round. A couple of percent is that asymmetry; anything larger would mean one
        /// direction transports worse than the other, which is the thing being ruled out.
        /// </summary>
        [Fact]
        public void AReversedRingCarriesHeatJustAsWell()
        {
            float forward = SpreadAfterHeatingOneSink(false);
            float reverse = SpreadAfterHeatingOneSink(true);

            Assert.True(forward > 0f && reverse > 0f, "both directions should be transporting");
            Assert.Equal(1f, reverse / forward, 1);
        }

        /// <summary>
        /// Two pumps facing each other cancel. The ring holds its coolant, both pumps draw their
        /// power, and nothing circulates — which is worth knowing before building it.
        /// </summary>
        [Fact]
        public void OpposedPumpsCancelAndTheRingStops()
        {
            CoolantLoop loop;
            Ring(5, 5, 4f, out loop);

            loop.Pumps.Clear();

            CoolantPump forward = new CoolantPump();
            forward.MaxPowerWatts = 20000f;
            forward.Direction = 1;
            loop.Pumps.Add(forward);

            loop.RefreshFlow();
            Assert.True(loop.FlowSegmentsPerSecond > 0f);

            CoolantPump against = new CoolantPump();
            against.MaxPowerWatts = 20000f;
            against.Direction = -1;
            loop.Pumps.Add(against);

            loop.RefreshFlow();
            Assert.Equal(0f, loop.FlowSegmentsPerSecond, 4);
            Assert.Equal(0f, loop.PumpDemand, 4);

            // And they are both still drawing, which is the part worth warning about.
            Assert.Equal(20000f, forward.DemandWatts, 2);
            Assert.Equal(20000f, against.DemandWatts, 2);
        }

        /// <summary>
        /// Three pumps one way against one the other leave net flow for two, not four — the square
        /// root is taken of what survives the subtraction.
        /// </summary>
        [Fact]
        public void OpposedPumpsSubtractBeforeTheSquareRoot()
        {
            CoolantLoop loop;
            Ring(5, 5, 10f, out loop);

            loop.Pumps.Clear();
            for (int i = 0; i < 4; i++)
            {
                CoolantPump pump = new CoolantPump();
                pump.MaxPowerWatts = 20000f;
                pump.Direction = i < 3 ? 1 : -1;
                loop.Pumps.Add(pump);
            }

            loop.RefreshFlow();

            // Net demand of two, so flow of sqrt(2) times the base rate.
            Assert.Equal(2f, loop.PumpDemand, 3);
            Assert.Equal(10f * (float)Math.Sqrt(2f), loop.FlowSegmentsPerSecond, 3);
        }

        /// <summary>Reversed flow carries its fractional debt with the right sign, and conserves heat.</summary>
        [Fact]
        public void ReverseRotationIsExactToo()
        {
            CoolantLoop loop;
            Ring(5, 5, 4f, out loop);

            loop.Pumps.Clear();
            CoolantPump against = new CoolantPump();
            against.MaxPowerWatts = 20000f;
            against.Direction = -1;
            loop.Pumps.Add(against);
            loop.RefreshFlow();

            for (int i = 0; i < loop.PipeCount; i++)
            {
                loop.SetSegmentTemperature(i, 250f + (i * 40f));
            }

            float before = loop.Energy;
            float hottest = loop.HottestSegment;

            // A whole number of laps: the ring must come back to exactly where it started.
            for (int i = 0; i < 4 * loop.PipeCount; i++) loop.Advect(1f / 4f);

            Assert.Equal(1f, loop.Energy / before, 5);
            Assert.Equal(hottest, loop.HottestSegment, 2);
        }

        private static CoolantLoop RingWithPump(bool reversed)
        {
            GridBuilder builder = GridBuilder.Large();
            PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, 5, 5), -1, null, reversed);

            return builder.BuildSimulation(Isolated(), 300f).Solver.Loops[0];
        }

        private static float SpreadAfterHeatingOneSink(bool reversed)
        {
            Dictionary<int, Vector3I> sinks = new Dictionary<int, Vector3I>();
            sinks[2] = Vector3I.Down;

            GridBuilder builder = GridBuilder.Large();
            List<Vector3I> cells = PipeFitter.RectangleXZ(Vector3I.Zero, 5, 5);
            PipeFitter.BuildRing(builder, cells, -1, sinks, reversed);
            builder.Place(Catalog.Reactor(), cells[2] + Vector3I.Down).Producing(300000f);

            ThermalSimulation simulation = builder.BuildSimulation(Isolated(), 300f);
            simulation.StepExact(4000, Worlds.Shadow());

            CoolantLoop loop = simulation.Solver.Loops[0];
            return loop.HottestSegment - loop.ColdestSegment;
        }
    
    
        /// <summary>
        /// Flow is reported in metres per second, because parcels per second is the solver's unit and
        /// nobody has any intuition for it. One parcel is one pipe block, so the conversion is the
        /// grid's cell size.
        /// </summary>
        [Fact]
        public void FlowIsReportedInMetresPerSecond()
        {
            CoolantLoop loop;
            Ring(5, 5, 4f, out loop);

            // A large grid cell is 2.5 m, so four parcels a second is ten metres a second. Which sign
            // that carries depends on how the ring happened to be traced, so it is the magnitude that
            // is fixed and the negation that is asserted.
            Assert.Equal(2.5f, loop.ParcelLengthMetres, 3);
            Assert.Equal(loop.FlowSegmentsPerSecond * 2.5f, loop.FlowMetresPerSecond, 3);
            Assert.Equal(10f, Math.Abs(loop.FlowMetresPerSecond), 2);

            // The sign is the direction, and it survives the conversion.
            float before = loop.FlowMetresPerSecond;
            loop.Pumps[0].Direction = -loop.Pumps[0].Direction;
            loop.RefreshFlow();
            Assert.Equal(-before, loop.FlowMetresPerSecond, 2);

            // A stopped ring is zero either way round.
            loop.Pumps[0].Enabled = false;
            loop.RefreshFlow();
            Assert.Equal(0f, loop.FlowMetresPerSecond, 4);
        }
    }
}
