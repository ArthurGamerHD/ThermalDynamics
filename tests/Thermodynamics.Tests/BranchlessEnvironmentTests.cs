using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **A buried node reaches the same answer through the exposed arithmetic**, so the substeps
    /// that only read the environment rows do not ask which kind of node they are looking at.
    ///
    /// <para>
    /// Half a hull is buried — 49 % of nodes are exposed at 126,731 blocks — which makes
    /// `nodeExposedFaces[i] &lt;= 0` a coin toss, taken once per node per substep, that a processor
    /// cannot predict. It is also unnecessary: a buried node's radiation coefficient is zero,
    /// because that is emissivity times *exposed area*, and its convection row is zeroed when the
    /// rows are filled. Both terms come out zero and its watts are its own source row, which is
    /// exactly what the branch used to write for it.
    /// </para>
    ///
    /// <para>
    /// "Comes out the same" is the whole claim, so it is checked rather than argued: the same grid
    /// run both ways, every temperature and both published watts figures compared bit for bit
    /// (`D3`, `D8`), on a hull that has buried nodes and exposed ones and is doing all of
    /// radiation, convection, solar gain and friction at once.
    /// </para>
    /// </summary>
    public class BranchlessEnvironmentTests
    {
        private readonly ITestOutputHelper output;

        public BranchlessEnvironmentTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static ThermalSimulation Hull(bool branchless, int blocks)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build("ship", blocks));

            ThermalSimulation simulation = builder.BuildSimulation(Hulls.Uncapped(), 293.15f);
            simulation.Solver.BranchlessEnvironment = branchless;
            simulation.RebuildAll();
            while (simulation.Rooms.HasWorkPending) simulation.Rooms.Step(65536);
            simulation.Solver.RefreshExposure(simulation.Rooms.Map);
            LoadBenchmarks.SeedSpread(simulation);
            Census.DriveCensus(simulation);
            return simulation;
        }

        /// <summary>
        /// The hull the comparison runs on has both kinds of node, or the two paths were never
        /// asked to differ.
        /// </summary>
        [Fact]
        public void TheHullHasBuriedNodesAndExposedOnes()
        {
            ThermalSimulation simulation = Hull(true, 3000);
            IList<ThermalNode> nodes = simulation.Solver.Nodes;

            int exposed = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].TotalExposedFaces > 0) exposed++;
            }

            output.WriteLine(nodes.Count.ToString("n0") + " nodes, " + exposed.ToString("n0")
                + " exposed (" + (100.0 * exposed / nodes.Count).ToString("n1") + " %)");

            Assert.True(exposed > 0, "no node is exposed, so the arithmetic under test never runs");
            Assert.True(exposed < nodes.Count,
                "every node is exposed, so the claim being checked — that a buried one comes out the"
                + " same — is never exercised");
        }

        [Fact]
        public void BuriedNodesReachTheSameAnswerThroughTheExposedArithmetic()
        {
            ThermalSimulation branched = Hull(false, 3000);
            ThermalSimulation branchless = Hull(true, 3000);

            EnvironmentState state = EnvironmentSolver.Solve(
                branched.Settings, branched.Planet, Worlds.Flight(1f, 300f));
            float step = branched.Settings.StepSeconds;

            for (int i = 0; i < 8; i++)
            {
                branched.Solver.Step(step, state);
                branchless.Solver.Step(step, state);

                Assert.True(Bits(branched.Solver.LastEnvironmentWatts) == Bits(branchless.Solver.LastEnvironmentWatts),
                    "step " + i + ": the grid shed " + branched.Solver.LastEnvironmentWatts.ToString("R")
                    + " W with the branch and " + branchless.Solver.LastEnvironmentWatts.ToString("R") + " without");

                Assert.Equal(Bits(branched.Solver.LastHeatGainWatts), Bits(branchless.Solver.LastHeatGainWatts));
            }

            IList<ThermalNode> a = branched.Solver.Nodes;
            IList<ThermalNode> b = branchless.Solver.Nodes;
            Assert.Equal(a.Count, b.Count);
            Assert.True(a.Count > 1000, "only " + a.Count + " nodes");

            int buriedJudged = 0;
            for (int i = 0; i < a.Count; i++)
            {
                Assert.True(Bits(a[i].Temperature) == Bits(b[i].Temperature),
                    "node " + i + " (" + a[i].TotalExposedFaces + " exposed faces) is "
                    + a[i].Temperature.ToString("R") + " with the branch and "
                    + b[i].Temperature.ToString("R") + " without");

                if (a[i].TotalExposedFaces == 0) buriedJudged++;
            }

            Assert.True(buriedJudged > 100,
                "only " + buriedJudged + " buried nodes were compared, so the case this change is"
                + " about is barely covered");
        }

        /// <summary>
        /// And in vacuum, where convection is off and only radiation runs — a different pair of
        /// flags through the same loop.
        /// </summary>
        [Fact]
        public void TheTwoPathsAgreeInVacuumToo()
        {
            ThermalSimulation branched = Hull(false, 2000);
            ThermalSimulation branchless = Hull(true, 2000);

            EnvironmentState state = EnvironmentSolver.Solve(
                branched.Settings, branched.Planet, Worlds.Space(new Vector3(0f, 1f, 0f)));
            float step = branched.Settings.StepSeconds;

            for (int i = 0; i < 6; i++)
            {
                branched.Solver.Step(step, state);
                branchless.Solver.Step(step, state);
            }

            IList<ThermalNode> a = branched.Solver.Nodes;
            IList<ThermalNode> b = branchless.Solver.Nodes;

            for (int i = 0; i < a.Count; i++)
            {
                Assert.True(Bits(a[i].Temperature) == Bits(b[i].Temperature),
                    "node " + i + " differs in vacuum: " + a[i].Temperature.ToString("R")
                    + " against " + b[i].Temperature.ToString("R"));
            }
        }

        private static int Bits(float value)
        {
            return BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
        }
    }
}
