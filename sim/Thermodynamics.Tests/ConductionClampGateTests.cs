using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The conduction pass skips the overshoot clamp on any step where the clamp cannot bind, and
    /// the only thing that makes that safe is that running it would have produced the same bits.
    ///
    /// <para>
    /// Both halves of the clamp are the same stability test written twice. A node under-relaxes
    /// only when <c>h * G &gt; C</c> for that node; a link's exchange is capped at equilibrium only
    /// when <c>h * conductance &gt;</c> its reduced mass. Neither reads a temperature, so both are
    /// settled for the whole grid before the substeps run. On a grid granted the substeps it
    /// demands — which is what the substep count is chosen to guarantee — neither holds anywhere,
    /// and every clamped branch computes a value it then discards.
    /// </para>
    ///
    /// <para>
    /// The assertion is <b>bit-identical</b>, not "close enough". A gate that is nearly right
    /// would drop clamping on grids that needed it and diverge silently, which is worse than
    /// paying for the clamp. The margin in <c>ClampBindingMargin</c> is what buys that: the fast
    /// path is taken only with a hundred parts per million of headroom, three orders of magnitude
    /// above the rounding in the comparison the clamp itself makes.
    /// </para>
    ///
    /// <para>
    /// The last two tests are the ones that keep the rest honest. Without them a gate that never
    /// engaged, or one that engaged always, would pass every equivalence assertion above.
    /// </para>
    /// </summary>
    public class ConductionClampGateTests
    {
        /// <summary>
        /// A census hull — the block mix of a real ship, so the stiff tail is the decorative
        /// blocks it is in the field rather than a shape chosen to make a point.
        /// </summary>
        private static ThermalSimulation Build(bool gate, int maxSubsteps)
        {
            ThermalSettings settings = new ThermalSettings();
            settings.MaxSubsteps = maxSubsteps;
            settings.MaxElementVisitsPerStep = 0;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build("ship", 2000));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.RebuildAll();
            simulation.Solver.GateConductionClamp = gate;

            Census.DriveCensus(simulation);
            LoadBenchmarks.SeedSpread(simulation);
            return simulation;
        }

        private static float[] Temperatures(ThermalSimulation simulation)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            float[] values = new float[nodes.Count];
            for (int i = 0; i < nodes.Count; i++) values[i] = nodes[i].Temperature;
            return values;
        }

        private static void AssertIdentical(ThermalSimulation always, ThermalSimulation gated, string what)
        {
            float[] expected = Temperatures(always);
            float[] actual = Temperatures(gated);

            Assert.Equal(expected.Length, actual.Length);

            for (int i = 0; i < expected.Length; i++)
            {
                Assert.True(expected[i].Equals(actual[i]),
                    what + ": block " + i + " is " + actual[i].ToString("r")
                    + " with the clamp gated and " + expected[i].ToString("r") + " with it always on");
            }
        }

        /// <summary>
        /// The case the gate exists for: enough substeps that nothing is near its stability limit.
        /// </summary>
        [Fact]
        public void SkippingTheClampIsBitIdenticalWhenTheGridIsResolved()
        {
            ThermalSimulation always = Build(gate: false, maxSubsteps: 4096);
            ThermalSimulation gated = Build(gate: true, maxSubsteps: 4096);

            EnvironmentSample sample = Worlds.PlanetSurface(0.8f, timeOfDay: 0.35f, windSpeed: 22f);

            always.StepExact(40, sample);
            gated.StepExact(40, sample);

            Assert.False(gated.Solver.ConductionClampLive);
            AssertIdentical(always, gated, "resolved");
        }

        /// <summary>
        /// One substep against a hull that asks for twenty. Every stiff element is over its limit,
        /// the gate must find one, and the clamp must run exactly as it did before.
        /// </summary>
        [Fact]
        public void TheClampStillRunsWhenTheSubstepCountWasRefused()
        {
            ThermalSimulation always = Build(gate: false, maxSubsteps: 1);
            ThermalSimulation gated = Build(gate: true, maxSubsteps: 1);

            EnvironmentSample sample = Worlds.PlanetSurface(0.8f, timeOfDay: 0.35f, windSpeed: 22f);

            always.StepExact(40, sample);
            gated.StepExact(40, sample);

            Assert.True(gated.Solver.ConductionClampLive);
            AssertIdentical(always, gated, "clamped");
        }

        /// <summary>
        /// Vacuum as well as atmosphere: the substep demand is lower without convection, so a cap
        /// that binds in air may not bind in space and the gate flips between the two.
        /// </summary>
        [Fact]
        public void SkippingTheClampIsBitIdenticalInVacuum()
        {
            ThermalSimulation always = Build(gate: false, maxSubsteps: 4096);
            ThermalSimulation gated = Build(gate: true, maxSubsteps: 4096);

            EnvironmentSample sample = Worlds.Space(new Vector3(0.3f, 0.9f, 0.2f));

            always.StepExact(40, sample);
            gated.StepExact(40, sample);

            AssertIdentical(always, gated, "vacuum");
        }

        /// <summary>
        /// Across the whole range of substep caps, including the ones either side of where the
        /// clamp starts to bind. The gate is decided once per step from the substep length, so a
        /// cap that moves the length moves the decision.
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        [InlineData(8)]
        [InlineData(16)]
        [InlineData(24)]
        [InlineData(64)]
        public void TheGateNeverChangesTheAnswerAtAnySubstepCap(int cap)
        {
            ThermalSimulation always = Build(gate: false, maxSubsteps: cap);
            ThermalSimulation gated = Build(gate: true, maxSubsteps: cap);

            EnvironmentSample sample = Worlds.PlanetSurface(0.8f, timeOfDay: 0.35f, windSpeed: 22f);

            always.StepExact(20, sample);
            gated.StepExact(20, sample);

            AssertIdentical(always, gated, "cap " + cap);
        }

        /// <summary>
        /// The same, with the step spread across frames the way the game runs it. The gate is set
        /// in <c>BeginStep</c> and read by every slice of every substep, so a slice boundary must
        /// not be able to see a different answer from the one the step began with.
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(97)]
        [InlineData(5000)]
        public void TheGateSurvivesTheStepBeingSpread(int budget)
        {
            ThermalSimulation whole = Build(gate: false, maxSubsteps: 4096);
            ThermalSimulation spread = Build(gate: true, maxSubsteps: 4096);

            EnvironmentState state = EnvironmentSolver.Solve(
                whole.Settings, whole.Planet,
                Worlds.PlanetSurface(0.8f, timeOfDay: 0.35f, windSpeed: 22f));

            for (int step = 0; step < 8; step++)
            {
                whole.Solver.Step(whole.Settings.StepSeconds, state);

                spread.Solver.BeginStep(spread.Settings.StepSeconds, state);
                while (!spread.Solver.AdvanceStep(budget)) { }
            }

            AssertIdentical(whole, spread, "spread over " + budget);
        }

        /// <summary>
        /// The gate reports what it did. Without this, a gate wired to a constant would satisfy
        /// every equivalence test above — the ungated run is the reference, so agreeing with it is
        /// what a gate that never engages does best.
        /// </summary>
        [Fact]
        public void TheGateEngagesOnAResolvedGridAndNotOnAStiffOne()
        {
            ThermalSimulation resolved = Build(gate: true, maxSubsteps: 4096);
            ThermalSimulation stiff = Build(gate: true, maxSubsteps: 1);

            EnvironmentSample sample = Worlds.PlanetSurface(0.8f, timeOfDay: 0.35f, windSpeed: 22f);

            resolved.StepExact(4, sample);
            stiff.StepExact(4, sample);

            Assert.False(resolved.Solver.ConductionClampLive);
            Assert.True(stiff.Solver.ConductionClampLive);
        }

        /// <summary>
        /// Switching the clamp off in settings is still switching it off: the gate reports the
        /// clamp dead, and it must not resurrect it on a grid stiff enough to bind.
        /// </summary>
        [Fact]
        public void TheSettingStillWins()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.MaxSubsteps = 1;
            settings.MaxElementVisitsPerStep = 0;
            settings.ClampConductionOvershoot = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build("ship", 500));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.RebuildAll();
            Census.DriveCensus(simulation);
            LoadBenchmarks.SeedSpread(simulation);

            simulation.StepExact(4, Worlds.PlanetSurface(0.8f, timeOfDay: 0.35f, windSpeed: 22f));

            Assert.False(simulation.Solver.ConductionClampLive);
        }
    }
}
