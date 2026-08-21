using System;
using System.Collections.Generic;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// The hull the solver's A/B tests are run on.
    ///
    /// <para>
    /// Several suites pin an optimisation against the thing it replaced — the precomputed
    /// environment rows, the fixed source row, the gated conduction clamp, the batched
    /// diagnostics — and every one of them needs the same starting point: a census hull, so the
    /// stiff tail is a real ship's rather than a shape chosen to make a point; its producers
    /// running, so waste heat is in play; a spread of temperatures, because conduction skips a
    /// link whose ends agree and a settled grid exercises almost none of the pass; and no ceiling
    /// on the substep count, so the grid takes the substeps it asks for rather than the ones a
    /// default allows.
    /// </para>
    ///
    /// <para>
    /// Four copies of that had drifted apart on block count and substep ceiling before this
    /// existed. A fixture that differs between two suites makes their results incomparable, which
    /// is the one thing an A/B suite cannot afford.
    /// </para>
    /// </summary>
    public static class Hulls
    {
        /// <summary>Blocks in the hull. Large enough for a real block mix, small enough for a suite.</summary>
        public const int DefaultBlocks = 2000;

        /// <summary>Substep ceiling that never binds, so the hull is granted what it demands.</summary>
        public const int Unbounded = 4096;

        /// <summary>
        /// Settings with neither the substep count nor the step budget capped.
        ///
        /// A capped run is a different measurement — the overshoot clamps come live and the
        /// grid integrates a refused demand — and the suites that want that ask for it by
        /// passing a real ceiling.
        /// </summary>
        public static ThermalSettings Uncapped(int maxSubsteps = Unbounded)
        {
            ThermalSettings settings = new ThermalSettings();
            settings.MaxSubsteps = maxSubsteps;
            settings.MaxElementVisitsPerStep = 0;
            settings.Derive();
            return settings;
        }

        /// <summary>
        /// A census hull with its heat producers running and its temperatures spread across
        /// 250-750 K, built and mapped.
        ///
        /// Every postcondition an A/B suite depends on is checked here rather than assumed. A
        /// hull that built nothing, found no producer or came back unseeded still lets two runs
        /// of it agree perfectly, and agreement is the whole assertion those suites make — so the
        /// failure has to be raised where it happened, not left to a green test run.
        /// </summary>
        public static ThermalSimulation Driven(ThermalSettings settings, int blocks = DefaultBlocks)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build("ship", blocks));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.RebuildAll();

            int nodes = simulation.Solver.Nodes.Count;
            if (nodes < blocks / 2)
            {
                throw new InvalidOperationException(
                    "the census hull built " + nodes + " nodes for " + blocks + " blocks asked for");
            }

            if (Census.DriveCensus(simulation) <= 0)
            {
                throw new InvalidOperationException(
                    "the census hull has no heat producer, so waste heat is not in play");
            }

            LoadBenchmarks.SeedSpread(simulation);
            RequireSpread(simulation);
            return simulation;
        }

        /// <summary>
        /// Conduction skips a link whose ends agree, so a hull whose blocks all sit at one
        /// temperature exercises almost none of a step.
        /// </summary>
        private static void RequireSpread(ThermalSimulation simulation)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            float low = float.MaxValue;
            float high = float.MinValue;

            for (int i = 0; i < nodes.Count; i++)
            {
                float t = nodes[i].Temperature;
                if (t < low) low = t;
                if (t > high) high = t;
            }

            if (high - low < 100f)
            {
                throw new InvalidOperationException(
                    "the census hull spans " + (high - low).ToString("n1")
                    + " K, which is not a gradient a step would do work against");
            }
        }

        /// <summary>The same hull at the default uncapped settings.</summary>
        public static ThermalSimulation Driven()
        {
            return Driven(Uncapped());
        }
    }
}
