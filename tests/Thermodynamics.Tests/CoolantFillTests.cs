using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **Coolant is a consumable, and a ring that is part full costs the integrator nothing extra.**
    ///
    /// <para>
    /// backlog.md `B43` decided that refilling a loop costs energy and time, and `B44` named the
    /// trap in getting there: a loop's substep demand is `SegmentConductance / SegmentThermalMass`,
    /// so scaling the fluid's *capacity* with the fill and leaving its *coupling* alone makes a
    /// 5 %-full ring twenty times stiffer than a full one — on an element that already competes to
    /// be a grid's worst, against a cap of 64.
    /// </para>
    ///
    /// <para>
    /// The design scales both, which makes the ratio invariant and is also the physical answer:
    /// half the fluid touching a wall carries half the heat through it. **That invariance is what
    /// this file exists to hold**, because a consumer of a link's conductance that forgets to scale
    /// it is not a compile error — it is a ring that is quietly stiffer than it should be.
    /// </para>
    /// </summary>
    public class CoolantFillTests
    {
        private readonly ITestOutputHelper output;

        public CoolantFillTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static ThermalSettings Isolated()
        {
            ThermalSettings s = new ThermalSettings();
            s.EnableEnvironment = false;
            s.EnableSolarHeat = false;
            s.EnableWasteHeat = false;
            s.Derive();
            return s;
        }

        private static ThermalSimulation Ring(out CoolantLoop loop)
        {
            GridBuilder builder = GridBuilder.Large();
            PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, 3, 3));

            ThermalSimulation simulation = builder.BuildSimulation(Isolated());
            loop = simulation.Solver.Loops[0];
            return simulation;
        }

        /// <summary>
        /// **The substep demand does not move with the fill**, which is the whole reason both the
        /// capacity and the coupling are scaled rather than only the capacity.
        ///
        /// A tenth full is the interesting case: it is where the cliff would be steepest, and it is
        /// the state a ring is in for most of a refill.
        /// </summary>
        [Theory]
        [InlineData(1.0f)]
        [InlineData(0.5f)]
        [InlineData(0.1f)]
        [InlineData(0.01f)]
        public void TheSubstepDemandIsInvariantInTheFill(float fill)
        {
            CoolantLoop loop;
            ThermalSimulation simulation = Ring(out loop);

            float full = simulation.Solver.RequiredSubsteps(1f);

            loop.FillFraction = fill;
            float part = simulation.Solver.RequiredSubsteps(1f);

            output.WriteLine("full {0:n4} substeps, {1:p0} full {2:n4}", full, fill, part);

            Assert.True(full > 0f, "the full ring demanded nothing, so this test compares two zeroes");
            Assert.InRange(part, full * 0.99f, full * 1.01f);
        }

        /// <summary>
        /// The capacity and the coupling both move with the fill, and by the same factor. This is
        /// the mechanism the test above measures the consequence of; separating them says which of
        /// the two broke when the ratio moves.
        /// </summary>
        [Fact]
        public void BothTheCapacityAndTheCouplingScaleWithTheFill()
        {
            CoolantLoop loop;
            Ring(out loop);

            float massFull = loop.SegmentThermalMass;
            float linkFull = loop.LinkConductance(0);

            Assert.True(linkFull > 0f, "the first link carries nothing, so this checks nothing");

            loop.FillFraction = 0.25f;

            Assert.InRange(loop.SegmentThermalMass, massFull * 0.2475f, massFull * 0.2525f);
            Assert.InRange(loop.LinkConductance(0), linkFull * 0.2475f, linkFull * 0.2525f);
        }

        /// <summary>
        /// **A dry ring is still a ring**: it exists, holds nothing, transports nothing, and can be
        /// refilled. *The loop stops existing* is the shape this area has failed in twice, so zero
        /// is a level rather than a deletion.
        /// </summary>
        [Fact]
        public void ADryRingStillExistsAndCouplesToNothing()
        {
            CoolantLoop loop;
            ThermalSimulation simulation = Ring(out loop);

            loop.FillFraction = 0f;

            Assert.Single(simulation.Solver.Loops);
            Assert.Equal(8, loop.PipeCount);
            Assert.Equal(0f, loop.LinkConductance(0));
            Assert.Equal(0f, loop.HeldKilograms);

            // And it comes back, which is the point of it still being there.
            loop.FillFraction = 1f;
            Assert.True(loop.LinkConductance(0) > 0f, "a refilled ring couples to nothing");
        }

        /// <summary>
        /// The fill is a fraction and is held to it, so a caller that computes one from a division
        /// cannot drive the ring past full or below empty.
        /// </summary>
        [Fact]
        public void TheFillIsClampedToItsRange()
        {
            CoolantLoop loop;
            Ring(out loop);

            loop.FillFraction = 4f;
            Assert.Equal(1f, loop.FillFraction);

            loop.FillFraction = -1f;
            Assert.Equal(0f, loop.FillFraction);
        }

        /// <summary>
        /// Kilograms are what a refill is priced in, so the ring reports them: a full eight-pipe
        /// large-grid ring is eight parcels of the shipped 50 kg.
        /// </summary>
        [Fact]
        public void ARingReportsWhatItHoldsInKilograms()
        {
            CoolantLoop loop;
            Ring(out loop);

            float perPipe = loop.Properties.CoolantMassPerPipe;

            Assert.Equal(8f * perPipe, loop.CapacityKilograms);
            Assert.Equal(8f * perPipe, loop.HeldKilograms);

            loop.FillFraction = 0.5f;
            Assert.Equal(4f * perPipe, loop.HeldKilograms);
        }
    }
}
