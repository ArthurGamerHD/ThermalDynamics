using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **The transport package shipped, and these are what says so.**
    ///
    /// <para>
    /// <see cref="LoopBefore"/> was `LoopCandidate`, a proposal: forced convection while the ring is
    /// pumped, the old coefficient while it is stopped, and coolant sized from the cell instead of a
    /// flat 50 kg. All three are now the mod's own defaults — the first two at `C42`, the coolant's
    /// density at `C43` — so the class holds the **before** and these tests are the mirror image of
    /// the ones that used to say *none of it ships*.
    /// </para>
    ///
    /// <para>
    /// **The inversion is the point.** A candidate that quietly became the default would leave every
    /// lab with two identical arms, reporting *the package is worth nothing* in exactly the voice it
    /// would use if the package were worth nothing. That is this repository's recurring defect and it
    /// has already cost two findings, so the first test here pins that the two arms still differ.
    /// </para>
    /// </summary>
    public class LoopBeforeTests
    {
        private static ThermalSettings Isolated()
        {
            return Isolation.DeadWorld();
        }

        /// <summary>
        /// **All three dials moved, and the before arm is still a different loop.** A lab comparing
        /// the two is comparing two things.
        /// </summary>
        [Fact]
        public void TheBeforeArmIsNotWhatShips()
        {
            LoopThermalProperties shipped = LoopThermalProperties.Default();
            LoopThermalProperties before = LoopBefore.For(Catalog.LargeGridSize);

            Assert.NotEqual(before.HeatTransferCoefficient, shipped.HeatTransferCoefficient);
            Assert.NotEqual(before.StagnantTransferFraction, shipped.StagnantTransferFraction);

            Assert.NotEqual(
                before.MassPerPipe(Catalog.LargeGridSize),
                shipped.MassPerPipe(Catalog.LargeGridSize));

            Assert.NotEqual(
                before.MassPerPipe(Catalog.SmallGridSize),
                shipped.MassPerPipe(Catalog.SmallGridSize));
        }

        /// <summary>
        /// **A stopped ring carries exactly what every ring in this mod carried before `C42`.**
        ///
        /// <para>
        /// This is the safety property of that change and the reason the stopped share is 0.16
        /// rather than something rounder: `1000 × 0.16 = 160`, which is what the coefficient was and
        /// is what <see cref="LoopBefore.Coefficient"/> still holds. **So nothing anywhere is worse
        /// than it was**, and what the pair bought is that running the pump makes the ring conduct
        /// rather than only mixing it. A retune that moves one of the two without the other silently
        /// makes a stopped ring better or worse than it has ever been, which is a balance change
        /// nobody asked for, so the product is pinned rather than either number.
        /// </para>
        /// </summary>
        [Fact]
        public void AStoppedRingCarriesWhatEveryRingCarriedBeforeTheRetune()
        {
            LoopThermalProperties shipped = LoopThermalProperties.Default();

            float stopped = shipped.HeatTransferCoefficient * shipped.StagnantTransferFraction;

            Assert.Equal(LoopBefore.Coefficient, stopped, 1);
        }

        /// <summary>
        /// **The flat charge is the defect, stated as the two densities it is.** The same 50 kg in
        /// both cells is 3.2 kg/m³ and 400 kg/m³ for the same fluid in the same plumbing — the
        /// grid-size inconsistency <see cref="LoopThermalProperties.HeatTransferCoefficient"/> was
        /// already corrected for, two orders of magnitude apart.
        /// </summary>
        [Fact]
        public void TheFlatChargeIsTwoDensitiesTwoOrdersOfMagnitudeApart()
        {
            LoopThermalProperties before = LoopBefore.For(Catalog.LargeGridSize);

            float large = before.MassPerPipe(Catalog.LargeGridSize) / Volume(Catalog.LargeGridSize);
            float small = before.MassPerPipe(Catalog.SmallGridSize) / Volume(Catalog.SmallGridSize);

            Assert.True(small > large * 50f,
                "the flat coolant charge was expected to be far denser on a small grid than a large"
                + " one, which is the defect `C43` corrects; it read " + small.ToString("n1")
                + " against " + large.ToString("n1") + " kg/m3");
        }

        /// <summary>
        /// **It reaches a ring**: the same pipes hold different coolant on the two arms, which is the
        /// term the correction acts on. Coolant mass is a *transport* quantity rather than a pickup
        /// one — a ring carries `ṁ·c_p` past a point — so it moves how far the fluid swings between
        /// the sink face and the far side of the loop, and leaves the sink face itself alone.
        /// </summary>
        [Fact]
        public void TheRingHoldsMoreCoolantThanItUsedTo()
        {
            GridBuilder builder = GridBuilder.Large();
            PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, 3, 3), 1);

            ThermalSimulation simulation = builder.BuildSimulation(Isolated());
            CoolantLoop shipped = simulation.Solver.Loops[0];

            float now = shipped.CapacityKilograms;
            float sinkNow = shipped.LinkConductance(0);

            simulation.LoopProperties = LoopBefore.For(simulation.Grid.GridSize);
            simulation.RebuildAll();

            CoolantLoop before = simulation.Solver.Loops[0];

            Assert.True(before.CapacityKilograms > 0f,
                "the before ring held nothing, so nothing here is measured");

            Assert.True(now > before.CapacityKilograms,
                "the ring holds " + now.ToString("n0") + " kg as it ships against "
                + before.CapacityKilograms.ToString("n0") + " kg before, so the correction reaches"
                + " no fluid");

            // The pickup is a separate change with a separate reason, and the before arm moves it
            // too — so this asserts they are different rather than equal, which the candidate
            // version of this class could assert and this one cannot.
            Assert.NotEqual(sinkNow, before.LinkConductance(0), 1);
        }

        private static float Volume(float cell)
        {
            return cell * cell * cell;
        }
    }
}
