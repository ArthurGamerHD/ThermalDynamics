using System;
using System.Collections.Generic;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    public static class Hulls
    {
        public const int DefaultBlocks = 2000;

        public const int Unbounded = 4096;

/// <summary>Uncapped operation.</summary>
        public static ThermalSettings Uncapped(int maxSubsteps = Unbounded)
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.MaxSubsteps = maxSubsteps;
            settings.MaxElementVisitsPerStep = 0;
            settings.Derive();
            return settings;
        }

        public const float PastCriticalMultiple = 2f;

/// <summary>DrivenPastCritical operation.</summary>
        public static ThermalSimulation DrivenPastCritical(ThermalSettings settings,
            int blocks = DefaultBlocks, int buildOrderSeed = 0)
        {
            return Driven(settings, blocks, buildOrderSeed,
                Census.ProducerWatts * PastCriticalMultiple);
        }

/// <summary>Driven operation.</summary>
        public static ThermalSimulation Driven(ThermalSettings settings, int blocks = DefaultBlocks,
            int buildOrderSeed = 0)
        {
/// <summary>Driven operation.</summary>
            return Driven(settings, blocks, buildOrderSeed, Census.ProducerWatts);
        }

/// <summary>Driven operation.</summary>
        public static ThermalSimulation Driven(ThermalSettings settings, int blocks,
            int buildOrderSeed, float producerWatts)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build("ship", blocks));
            builder.ReorderPlacement(buildOrderSeed);

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.RebuildAll();

            int nodes = simulation.Solver.Nodes.Count;
            if (nodes < blocks / 2)
            {
                throw new InvalidOperationException(
                    "the census hull built " + nodes + " nodes for " + blocks + " blocks asked for");
            }

            if (Census.DriveCensus(simulation, producerWatts) <= 0)
            {
                throw new InvalidOperationException(
                    "the census hull has no heat producer, so waste heat is not in play");
            }

            LoadBenchmarks.SeedSpread(simulation);
            RequireSpread(simulation);
            return simulation;
        }

/// <summary>RequireSpread operation.</summary>
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

/// <summary>Driven operation.</summary>
        public static ThermalSimulation Driven()
        {
            return Driven(Uncapped());
        }
    }
}
