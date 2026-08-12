using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// Builds synthetic grids for scenarios and tests.
    /// </summary>
    public class GridBuilder
    {
        private readonly GridModel grid;
        private readonly List<BlockInstance> placed = new List<BlockInstance>();

        public GridBuilder(float gridSize = Catalog.LargeGridSize)
        {
            grid = new GridModel(gridSize);
        }

        public static GridBuilder Large()
        {
            return new GridBuilder(Catalog.LargeGridSize);
        }

        public static GridBuilder Small()
        {
            return new GridBuilder(Catalog.SmallGridSize);
        }

        public GridModel Grid
        {
            get { return grid; }
        }

        /// <summary>Blocks in placement order.</summary>
        public IList<BlockInstance> Placed
        {
            get { return placed; }
        }

        /// <summary>The most recently placed block.</summary>
        public BlockInstance Last
        {
            get { return placed.Count == 0 ? null : placed[placed.Count - 1]; }
        }

        public GridBuilder Place(BlockModel model, Vector3I at)
        {
            return Place(model, at, BlockOrientation.Identity);
        }

        public GridBuilder Place(BlockModel model, Vector3I at, BlockOrientation orientation)
        {
            BlockInstance block = new BlockInstance(model, at, orientation);
            grid.Add(block);
            placed.Add(block);
            return this;
        }

        public GridBuilder Place(BlockModel model, int x, int y, int z)
        {
            return Place(model, new Vector3I(x, y, z));
        }

        /// <summary>Fills a solid box, inclusive minimum and exclusive maximum.</summary>
        public GridBuilder Fill(BlockModel model, Vector3I min, Vector3I maxExclusive)
        {
            for (int z = min.Z; z < maxExclusive.Z; z++)
            {
                for (int y = min.Y; y < maxExclusive.Y; y++)
                {
                    for (int x = min.X; x < maxExclusive.X; x++)
                    {
                        Place(model, new Vector3I(x, y, z));
                    }
                }
            }
            return this;
        }

        /// <summary>Builds a hollow shell: every cell on the surface of the box, nothing inside.</summary>
        public GridBuilder Shell(BlockModel model, Vector3I min, Vector3I maxExclusive)
        {
            for (int z = min.Z; z < maxExclusive.Z; z++)
            {
                for (int y = min.Y; y < maxExclusive.Y; y++)
                {
                    for (int x = min.X; x < maxExclusive.X; x++)
                    {
                        bool onSurface =
                            x == min.X || x == maxExclusive.X - 1 ||
                            y == min.Y || y == maxExclusive.Y - 1 ||
                            z == min.Z || z == maxExclusive.Z - 1;

                        if (onSurface) Place(model, new Vector3I(x, y, z));
                    }
                }
            }
            return this;
        }

        /// <summary>Sets the power figures on the most recently placed block.</summary>
        public GridBuilder Producing(float watts)
        {
            if (Last != null) Last.PowerProducedWatts = watts;
            return this;
        }

        public GridBuilder Consuming(float watts)
        {
            if (Last != null) Last.PowerConsumedWatts = watts;
            return this;
        }

        public GridBuilder Thrusting(float watts)
        {
            if (Last != null) Last.ThrustWatts = watts;
            return this;
        }

        /// <summary>
        /// Wraps the grid in a fully built simulation with every block registered and every
        /// derived structure computed.
        /// </summary>
        public ThermalSimulation BuildSimulation(ThermalSettings settings = null, float initialTemperature = 293.15f)
        {
            ThermalSettings effective = settings ?? new ThermalSettings();
            ThermalSimulation simulation = new ThermalSimulation(effective, grid);
            simulation.DefaultTemperature = initialTemperature;

            for (int i = 0; i < placed.Count; i++)
            {
                simulation.Solver.AddBlock(placed[i], initialTemperature);
            }

            simulation.RebuildAll();
            return simulation;
        }
    }
}
