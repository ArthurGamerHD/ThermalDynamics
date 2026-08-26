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

            Assert.NotEqual(LoopCandidate.PumpedCoefficient, shipped.HeatTransferCoefficient);
            Assert.NotEqual(LoopCandidate.StoppedShare, shipped.StagnantTransferFraction);

            LoopThermalProperties large = LoopCandidate.For(2.5f);
            Assert.NotEqual(large.CoolantMassPerPipe, shipped.CoolantMassPerPipe);
        }

        /// <summary>
        /// **A stopped ring under the candidate carries exactly what a stopped ring carries today.**
        /// That is the whole reason the stopped share is 0.16 rather than something rounder: the
        /// package must not make any situation worse, so what it changes is that running the pump
        /// buys something, not that stopping it costs something it did not cost before.
        /// </summary>
        [Fact]
        public void AStoppedRingUnderTheCandidateMatchesWhatShipsToday()
        {
            LoopThermalProperties candidate = LoopCandidate.For(2.5f);
            LoopThermalProperties shipped = LoopThermalProperties.Default();

            float stopped = candidate.HeatTransferCoefficient * candidate.StagnantTransferFraction;

            Assert.Equal(shipped.HeatTransferCoefficient, stopped, 1);
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
        /// **It reaches a ring**: the same pipes carry more under the candidate than under what
        /// ships, while circulating. Reach and direction only — the size of the gain is balance.md's
        /// to state and this must survive it moving.
        /// </summary>
        [Fact]
        public void ThePumpedRingCarriesMoreUnderTheCandidate()
        {
            GridBuilder builder = GridBuilder.Large();
            PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, 3, 3), 1);

            ThermalSimulation simulation = builder.BuildSimulation(Isolated());
            CoolantLoop shipped = simulation.Solver.Loops[0];
            for (int i = 0; i < shipped.Pumps.Count; i++) shipped.Pumps[i].Enabled = true;
            shipped.RefreshFlow();
            float before = shipped.LinkConductance(0);

            simulation.LoopProperties = LoopCandidate.For(simulation.Grid.GridSize);
            simulation.RebuildAll();

            CoolantLoop candidate = simulation.Solver.Loops[0];
            for (int i = 0; i < candidate.Pumps.Count; i++) candidate.Pumps[i].Enabled = true;
            candidate.RefreshFlow();
            float after = candidate.LinkConductance(0);

            Assert.True(before > 0f, "the shipped ring carried nothing, so nothing here is measured");
            Assert.True(after > before,
                "a pumped ring under the candidate carried " + after + " W/K against the shipped "
                + before + ", so the package the retrofit lab measures reaches no fluid");
        }
    }
}
