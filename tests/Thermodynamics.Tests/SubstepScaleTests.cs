using Thermodynamics.Core;
using Thermodynamics.Harness;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// A substep count is a number about a step, and quoting one without the step it belongs to is
    /// the mistake this suite exists to make expensive.
    ///
    /// <para>
    /// A step of <c>dt</c> simulated seconds needs <c>dt · r_max / safety</c> substeps, so demand
    /// is <b>proportional to the step length</b>. Halve the step and every figure halves with it:
    /// what the grid asks for, which blocks a given cap reaches, and what that cap is worth.
    /// </para>
    ///
    /// <para>
    /// The cap tables in [stiffness.md](../../docs/stiffness.md) were taken at <c>Frequency 4</c>
    /// and read for a year as if they described the shipped default, which had moved to
    /// <c>Frequency 8</c>. Every speed-up on the page was therefore about twice what a default
    /// world would see, and the backlog item arguing for a shipped cap was reasoned from it. The
    /// tables were not wrong; they were unlabelled, which turned out to be the same thing.
    /// </para>
    ///
    /// <para>
    /// Nothing caught it because nothing asserted the relationship. These tests do, and they are
    /// cheap — no stepping, only the estimate — so the invariant that makes a table readable is
    /// checked on every run.
    /// </para>
    /// </summary>
    public class SubstepScaleTests
    {
        private static ThermalSimulation Hull(int frequency, int cap = 0, int blocks = 4000)
        {
            ThermalSettings settings = new ThermalSettings { Frequency = frequency };
            settings.MaxSubstepsPerBlock = cap;

            // Both bounds that would otherwise hide the relationship: one refuses the substeps the
            // estimate asks for, the other shortens the step rather than pay for it.
            settings.MaxSubsteps = 4096;
            settings.MaxElementVisitsPerStep = 0;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build("ship", blocks));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.RebuildAll();
            return simulation;
        }

        private static float Demand(ThermalSimulation simulation)
        {
            return simulation.Solver.RequiredSubsteps(simulation.Settings.StepSeconds);
        }

        /// <summary>
        /// Halving the step halves the demand. This is the whole reason a substep figure cannot be
        /// quoted without its step length, and the reason the two cap tables in stiffness.md are
        /// two tables rather than one.
        /// </summary>
        [Theory]
        [InlineData(1, 2)]
        [InlineData(2, 4)]
        [InlineData(4, 8)]
        [InlineData(8, 16)]
        public void DemandIsProportionalToTheStepLength(int slow, int fast)
        {
            float slower = Demand(Hull(slow));
            float faster = Demand(Hull(fast));

            Assert.True(slower > 0f, "the census hull asked for no substeps at Frequency " + slow
                + ", so there is no proportionality to test");

            // A ratio rather than a difference, and a tight tolerance: this is arithmetic on one
            // number, not a measurement, so anything but two is a change to the estimator.
            float ratio = slower / faster;
            Assert.True(ratio > 1.98f && ratio < 2.02f,
                "Frequency " + slow + " asked for " + slower.ToString("n3")
                + " substeps and Frequency " + fast + " for " + faster.ToString("n3")
                + " — a ratio of " + ratio.ToString("n4") + " where halving the step should halve"
                + " the demand exactly. A substep count is no longer proportional to the step it"
                + " is counted against, which makes every cap table in the docs unreadable.");
        }

        /// <summary>
        /// The same fact seen from the cap's side, and the sharper statement of it.
        ///
        /// The floor raises a node to <c>C_min = ΣG · StepSeconds / (safety · N)</c>, which depends
        /// on the step length and the cap only through their ratio. So a cap of N at one rate and a
        /// cap of 2N at half that rate are <b>the same floor</b> and must raise the same blocks —
        /// which is exactly the alignment the two corrected tables in stiffness.md show, row for
        /// row, one cap apart. Reading either table without its rate slides the whole column.
        /// </summary>
        [Theory]
        [InlineData(8, 16)]
        [InlineData(4, 8)]
        [InlineData(2, 4)]
        [InlineData(1, 2)]
        public void ACapMeansTheSameFloorAtTwiceTheRateAndTwiceTheCap(int fastCap, int slowCap)
        {
            ThermalSimulation fast = Hull(8, fastCap);
            ThermalSimulation slow = Hull(4, slowCap);

            // The floor is applied by the step prologue, which asking for the estimate runs. Read
            // before that, both sides report zero raised and agree on nothing.
            Demand(fast);
            Demand(slow);

            Assert.Equal(fast.Solver.FlooredNodes, slow.Solver.FlooredNodes);

            Assert.True(fast.Solver.FlooredNodes > 0,
                "cap " + fastCap + " at Frequency 8 raised no blocks at all, so the two sides agree"
                + " on nothing rather than on a floor");
        }

        /// <summary>
        /// A cap only binds below what the grid would have asked for anyway, so the same cap is a
        /// different thing at different rates: at the shipped eighth-second step the census hull
        /// asks for about eleven substeps, and a cap of sixteen is inert.
        ///
        /// This is the one that would have caught the reading directly. The old page credited
        /// `MaxSubstepsPerBlock 16` with removing 172 blocks' worth of stiffness; at the default
        /// rate it removes none, because the hull never asks for sixteen.
        /// </summary>
        [Fact]
        public void ACapAboveWhatTheHullAsksForDoesNothingAtTheShippedRate()
        {
            ThermalSimulation uncapped = Hull(8);
            float demand = Demand(uncapped);

            Assert.True(demand < 16f, "the census hull asks for " + demand.ToString("n2")
                + " substeps at the shipped Frequency 8, so a cap of 16 is not the inert case this"
                + " test was written around and the reasoning below needs redoing");

            ThermalSimulation capped = Hull(8, 16);

            Assert.Equal(demand, Demand(capped), 3);
            Assert.Equal(0, capped.Solver.FlooredNodes);
        }
    }
}
