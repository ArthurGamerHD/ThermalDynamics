using System;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **What windward shielding would cost, measured before it is built — because the one thing
    /// `K7` says has no measurement behind it is the thing that decides whether it can work at all.**
    ///
    /// <para>
    /// The machinery exists. `SunShadowMap` is not a sun pass, it is a **direction** pass:
    /// `Restart(grid, direction, occluders)` takes the direction as an argument, walks the grid
    /// against it and produces a per-face lit fraction. Aimed at the relative wind it would give
    /// per-face windward exposure, so a block behind another stops being heated and dragged as
    /// though it were in the open.
    /// </para>
    ///
    /// <para>
    /// **The unmeasured part is the cadence, and the cadence is not about the weather.** The pass
    /// restarts when its direction moves more than `SunRebuildCosine`, which is cos(2°), and the
    /// direction it tracks is in **grid-local** space. The sun crosses two degrees in tens of
    /// seconds. The relative wind crosses two degrees whenever the *ship turns two degrees* — so
    /// the rate is set by the pilot, not the sky.
    /// </para>
    ///
    /// <para>
    /// This measures how long a pass takes on a real hull and states the turn rate at which it can
    /// never finish. It is the number `K7` has to be designed against.
    /// </para>
    /// </summary>
    public class WindShieldingCostTests
    {
        private readonly ITestOutputHelper output;

        public WindShieldingCostTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        /// <summary>The rebuild threshold the shadow pass ships with, in degrees.</summary>
        private const double RebuildDegrees = 2.0;

        private static ThermalSimulation Hull(int side)
        {
            ThermalSettings settings = new ThermalSettings();
            settings.SolarSelfShadowing = true;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.HeavyArmor(), Vector3I.Zero, new Vector3I(side, side, side));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.Planet = PlanetThermalProperties.Default();
            return simulation;
        }

        /// <summary>
        /// **How many substeps a direction pass needs on a hull, and what turn rate outruns it.**
        ///
        /// A pass that restarts before it completes never produces a result, so the shielding would
        /// cost its whole budget and return the unshielded answer for ever. The turn rate that does
        /// that is `2° / (substeps × step seconds)`.
        /// </summary>
        [Theory]
        [InlineData(8)]
        [InlineData(16)]
        [InlineData(32)]
        [InlineData(48)]
        public void ADirectionPassIsOutrunByAnOrdinaryTurnRate(int side)
        {
            ThermalSimulation simulation = Hull(side);
            ThermalSolver solver = simulation.Solver;

            // One step to build the grid's structures, so the pass measures the walk rather than
            // the first-time setup around it.
            simulation.StepExact(1, Worlds.Space(Vector3.Forward));

            SunShadowMap map = new SunShadowMap();
            map.Restart(solver.Grid, Vector3.Forward, null);

            int substeps = 0;
            while (map.IsRunning && substeps < 100000)
            {
                map.Step(solver.SunShadowBudget);
                substeps++;
            }

            Assert.True(map.IsBuilt, "the pass never completed, so its cost is unmeasured");

            // The step the pass is sliced across: `Frequency` substeps a second at the default.
            double stepSeconds = 1.0 / simulation.Settings.Frequency;
            double secondsToComplete = substeps * stepSeconds;
            double outrunDegreesPerSecond = RebuildDegrees / Math.Max(1e-9, secondsToComplete);

            output.WriteLine(
                "{0,3} cells a side: {1,5} blocks, {2,6} substeps, {3,7:0.00} s to complete, "
                + "outrun above {4,7:0.00} deg/s",
                side, solver.Nodes.Count, substeps, secondsToComplete, outrunDegreesPerSecond);

            Assert.True(substeps > 0, "the pass completed in no substeps, so nothing was walked");
        }

        /// <summary>
        /// **The sun and the wind are not the same problem, and this is the ratio.**
        ///
        /// Space Engineers' default day is 2 hours, so the sun moves 0.05 deg/s and crosses the
        /// two-degree threshold every 40 seconds. A ship yawing at even 10 deg/s crosses it every
        /// fifth of a second — **200 times more often**. Whether the pass can keep up is the test
        /// above; that it is a different question from the solar one is this.
        /// </summary>
        [Fact]
        public void TheWindDirectionMovesOrdersOfMagnitudeFasterThanTheSun()
        {
            const double dayLengthSeconds = 2 * 60 * 60;
            double sunDegreesPerSecond = 360.0 / dayLengthSeconds;

            double sunSecondsPerRebuild = RebuildDegrees / sunDegreesPerSecond;
            double yawSecondsPerRebuild = RebuildDegrees / 10.0;

            output.WriteLine("sun crosses 2 deg every {0:0.0} s; a 10 deg/s yaw every {1:0.00} s — "
                + "{2:0} times more often", sunSecondsPerRebuild, yawSecondsPerRebuild,
                sunSecondsPerRebuild / yawSecondsPerRebuild);

            Assert.True(sunSecondsPerRebuild / yawSecondsPerRebuild > 100.0,
                "the wind and the sun move at comparable rates, so the solar cadence would carry "
                + "over and this whole concern is misplaced");
        }
    }
}
