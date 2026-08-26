using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **What correcting the coolant's grid-size defect would do, which is not one thing.**
    ///
    /// <para>
    /// `CoolantMassPerPipe` is a flat 50 kg with no grid size in it: **3.2 kg/m³ in a 2.5 m cube,
    /// which is a gas, against 400 kg/m³ in a 0.5 m one, which is a liquid**. That is the same
    /// defect <see cref="LoopThermalProperties.HeatTransferCoefficient"/> was corrected for when it
    /// stopped being a conductivity divided by half a cell, and correcting it the same way — a fixed
    /// density, the mass following the cell — is what <see cref="LoopCandidate"/> proposes. It is
    /// `C43`.
    /// </para>
    ///
    /// <para>
    /// **It is not shipped, because it is a large gain and a large loss rather than a correction.**
    /// These measure both halves, so the decision is made against numbers rather than against the
    /// tidiness of the physics. See balance.md, *The coolant mass is doing an undeclared job*.
    /// </para>
    /// </summary>
    public class LoopCoolantMassTests
    {
        private readonly ITestOutputHelper output;

        public LoopCoolantMassTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static ThermalSettings Isolated()
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

        /// <summary>
        /// A ring with one sink face on a 125 kW source, run to something like steady state, and
        /// what the fluid reached. Same rig at both grid sizes and under both charges (`P6`).
        /// </summary>
        private void Ring(bool large, bool corrected, out float hottest, out float substeps,
            out float massPerPipe)
        {
            GridBuilder builder = large ? GridBuilder.Large() : GridBuilder.Small();

            List<Vector3I> cells = PipeFitter.RectangleXZ(Vector3I.Zero, 5, 5);
            Dictionary<int, Vector3I> sinks = new Dictionary<int, Vector3I>();
            sinks[1] = Vector3I.Down;
            PipeFitter.BuildRing(builder, cells, 5, sinks);
            builder.Place(Catalog.Reactor(), cells[1] + Vector3I.Down).Wasting(125000f);

            ThermalSimulation simulation = builder.BuildSimulation(Isolated(), 300f);

            if (corrected)
            {
                simulation.LoopProperties = LoopCandidate.For(simulation.Grid.GridSize);
                simulation.RebuildAll();
            }

            CoolantLoop loop = simulation.Solver.Loops[0];
            simulation.StepExact(LabClock.Steps(400), Worlds.Shadow());

            hottest = loop.HottestSegment;
            substeps = simulation.Solver.RequiredSubsteps(0.25f);
            massPerPipe = loop.Properties.CoolantMassPerPipe;

            output.WriteLine("{0,-6} {1,-10} {2,8:n1} kg/pipe   hottest {3,7:n1} K   substeps {4,6:n2}",
                large ? "large" : "small", corrected ? "corrected" : "shipped",
                massPerPipe, hottest, substeps);
        }

        /// <summary>
        /// **The correction is a large gain on a large grid and a large loss on a small one**, which
        /// is why it is not a correction anybody can just apply. A flat charge per pipe is stingy in
        /// a 2.5 m cell and generous in a 0.5 m one, so making the density honest moves the two grid
        /// sizes in opposite directions.
        /// </summary>
        [Fact]
        public void TheDensityCorrectionHelpsLargeGridsAndHurtsSmallOnes()
        {
            float largeBefore, largeAfter, smallBefore, smallAfter, ignored, mass;

            Ring(true, false, out largeBefore, out ignored, out mass);
            Ring(true, true, out largeAfter, out ignored, out mass);
            Ring(false, false, out smallBefore, out ignored, out mass);
            Ring(false, true, out smallAfter, out ignored, out mass);

            Assert.True(largeAfter < largeBefore - 100f,
                "a large-grid ring reached " + largeAfter.ToString("n1") + " K corrected against "
                + largeBefore.ToString("n1") + " K shipped; the correction was expected to be worth"
                + " a great deal there, and if it is not then balance.md needs re-reading");

            Assert.True(smallAfter > smallBefore + 100f,
                "a small-grid ring reached " + smallAfter.ToString("n1") + " K corrected against "
                + smallBefore.ToString("n1") + " K shipped; the loss is half the reason this is not"
                + " applied, and a correction that stopped costing anything would change the"
                + " decision");
        }

        /// <summary>
        /// **And it costs the integrator nothing either way**, which is the half that makes it worth
        /// keeping on the table. Coolant mass is the *denominator* of a segment's substep — the
        /// conductance is untouched — so more of it can only make a ring cheaper to integrate, and
        /// in this rig the demand is set by something else entirely and does not move at all.
        /// </summary>
        [Fact]
        public void TheDensityCorrectionCostsNoSubsteps()
        {
            float ignored, mass, before, after;

            Ring(true, false, out ignored, out before, out mass);
            Ring(true, true, out ignored, out after, out mass);
            Assert.True(after <= before + 0.01f,
                "a large-grid ring demanded " + after + " substeps corrected against " + before);

            Ring(false, false, out ignored, out before, out mass);
            Ring(false, true, out ignored, out after, out mass);
            Assert.True(after <= before + 0.01f,
                "a small-grid ring demanded " + after + " substeps corrected against " + before
                + "; less coolant is a stiffer parcel, so this is the assertion that would catch"
                + " the correction becoming expensive on small grids");
        }
    }
}
