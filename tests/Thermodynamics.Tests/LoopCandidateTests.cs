using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **The transport candidate is a proposal, and these say what it is.**
    ///
    /// <para>
    /// <see cref="LoopCandidate"/> is the package `C38` measures: forced convection while the ring
    /// is pumped, today's coefficient while it is stopped, and coolant sized from the cell instead
    /// of a flat 50 kg. It is not applied to anything the mod ships, and the first test here is what
    /// says so — a candidate that quietly became the default would make every *before* column on
    /// balance.md a measurement of the *after*.
    /// </para>
    ///
    /// <para>
    /// The rest check it reaches a hull, because a lab arm that changes nothing reports the
    /// proposal as worthless and is indistinguishable from the proposal being worthless. That is
    /// this repository's recurring defect and it has already cost two findings.
    /// </para>
    /// </summary>
    public class LoopCandidateTests
    {
        private static ThermalSettings Isolated()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.EnableFriction = false;
            settings.EnableDamage = false;
            return settings.Derive();
        }

        /// <summary>
        /// **None of it ships.** Every value the candidate proposes differs from the shipped one, so
        /// a reader of the *before* column is reading the mod as it is.
        /// </summary>
        [Fact]
        public void TheCandidateIsNotWhatShips()
        {
            LoopThermalProperties shipped = LoopThermalProperties.Default();

            LoopThermalProperties large = LoopCandidate.For(2.5f);
            Assert.NotEqual(large.CoolantMassPerPipe, shipped.CoolantMassPerPipe);
        }

        /// <summary>
        /// **A stopped ring carries exactly what every ring in this mod carried before `C42`.**
        ///
        /// <para>
        /// This is the safety property of that change and the reason the stopped share is 0.16
        /// rather than something rounder: `1000 × 0.16 = 160`, which is what the coefficient was.
        /// **So nothing anywhere is worse than it was**, and what the pair bought is that running
        /// the pump makes the ring conduct rather than only mixing it. A retune that moves one of
        /// the two without the other silently makes a stopped ring better or worse than it has ever
        /// been, which is a balance change nobody asked for, so the product is pinned rather than
        /// either number.
        /// </para>
        /// </summary>
        [Fact]
        public void AStoppedRingCarriesWhatEveryRingCarriedBeforeTheRetune()
        {
            LoopThermalProperties shipped = LoopThermalProperties.Default();

            float stopped = shipped.HeatTransferCoefficient * shipped.StagnantTransferFraction;

            Assert.Equal(160f, stopped, 1);
        }

        /// <summary>
        /// **Coolant is sized from the cell, which is the defect being corrected.** A flat figure per
        /// pipe is 3.2 kg/m³ in a large cell and 400 kg/m³ in a small one, for the same fluid in the
        /// same plumbing — the grid-size inconsistency `HeatTransferCoefficient` was already fixed
        /// for. Under the candidate the density is what is fixed and the mass follows the cell.
        /// </summary>
        [Fact]
        public void CoolantIsSizedFromTheCellRatherThanPerPipe()
        {
            LoopThermalProperties large = LoopCandidate.For(2.5f);
            LoopThermalProperties small = LoopCandidate.For(0.5f);

            float largeDensity = large.CoolantMassPerPipe / (2.5f * 2.5f * 2.5f);
            float smallDensity = small.CoolantMassPerPipe / (0.5f * 0.5f * 0.5f);

            Assert.Equal(largeDensity, smallDensity, 3);
            Assert.Equal(LoopCandidate.KilogramsPerCubicMetre, largeDensity, 3);

            // And the shipped figure is the thing being corrected: the same 50 kg in both cells is
            // two densities two orders of magnitude apart.
            LoopThermalProperties shipped = LoopThermalProperties.Default();
            float shippedLarge = shipped.CoolantMassPerPipe / (2.5f * 2.5f * 2.5f);
            float shippedSmall = shipped.CoolantMassPerPipe / (0.5f * 0.5f * 0.5f);

            Assert.True(shippedSmall > shippedLarge * 50f,
                "the shipped coolant mass was expected to be far denser on a small grid than a"
                + " large one, which is the defect; it read " + shippedSmall + " against "
                + shippedLarge + " kg/m3");
        }

        /// <summary>
        /// **It reaches a ring**: the same pipes hold more coolant under the candidate, which is the
        /// term it acts on. Coolant mass is a *transport* quantity rather than a pickup one — a ring
        /// carries `ṁ·c_p` past a point — so it moves what the ring can deliver to panels far along
        /// it and leaves the sink face exactly where it was.
        ///
        /// <para>
        /// Reach and direction only. The size belongs to balance.md and must be free to move.
        /// </para>
        /// </summary>
        [Fact]
        public void TheRingHoldsMoreCoolantUnderTheCandidate()
        {
            GridBuilder builder = GridBuilder.Large();
            PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, 3, 3), 1);

            ThermalSimulation simulation = builder.BuildSimulation(Isolated());
            CoolantLoop shipped = simulation.Solver.Loops[0];

            float before = shipped.CapacityKilograms;
            float sinkBefore = shipped.LinkConductance(0);

            simulation.LoopProperties = LoopCandidate.For(simulation.Grid.GridSize);
            simulation.RebuildAll();

            CoolantLoop candidate = simulation.Solver.Loops[0];
            float after = candidate.CapacityKilograms;

            Assert.True(before > 0f, "the shipped ring held nothing, so nothing here is measured");
            Assert.True(after > before,
                "the ring held " + after.ToString("n0") + " kg under the candidate against the"
                + " shipped " + before.ToString("n0") + " kg, so the package reaches no fluid");

            // And it is not secretly a pickup change, which is what ships now.
            Assert.Equal(sinkBefore, candidate.LinkConductance(0), 1);
        }

    }
}
