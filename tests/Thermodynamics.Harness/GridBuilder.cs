using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    public class GridBuilder
    {
        private readonly GridModel grid;
/// <summary>List operation.</summary>
        private readonly List<BlockInstance> placed = new List<BlockInstance>();

/// <summary>GridBuilder operation.</summary>
        public GridBuilder(float gridSize = Catalog.LargeGridSize)
        {
/// <summary>GridModel operation.</summary>
            grid = new GridModel(gridSize);
        }

/// <summary>Large operation.</summary>
        public static GridBuilder Large()
        {
            return new GridBuilder(Catalog.LargeGridSize);
        }

/// <summary>Small operation.</summary>
        public static GridBuilder Small()
        {
            return new GridBuilder(Catalog.SmallGridSize);
        }

        public GridModel Grid
        {
            get { return grid; }
        }

        public IList<BlockInstance> Placed
        {
            get { return placed; }
        }

        public BlockInstance Last
        {
            get { return placed.Count == 0 ? null : placed[placed.Count - 1]; }
        }

/// <summary>Place operation.</summary>
        public GridBuilder Place(BlockModel model, Vector3I at)
        {
/// <summary>Place operation.</summary>
            return Place(model, at, BlockOrientation.Identity);
        }

/// <summary>Place operation.</summary>
        public GridBuilder Place(BlockModel model, Vector3I at, BlockOrientation orientation)
        {
/// <summary>BlockInstance operation.</summary>
            BlockInstance block = new BlockInstance(model, at, orientation);
            grid.Add(block);
            placed.Add(block);
            return this;
        }

/// <summary>Place operation.</summary>
        public GridBuilder Place(BlockModel model, int x, int y, int z)
        {
            return Place(model, new Vector3I(x, y, z));
        }

/// <summary>Fill operation.</summary>
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

/// <summary>Shell operation.</summary>
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

/// <summary>Producing operation.</summary>
        public GridBuilder Producing(float watts)
        {
            if (Last != null) Last.PowerProducedWatts = watts;
            return this;
        }

/// <summary>Wasting operation.</summary>
        public GridBuilder Wasting(float watts)
        {
            if (Last == null) return this;

            float fraction = Last.Model.Thermal.ProducerWasteEnergy;
            if (fraction <= 0f)
            {
                throw new InvalidOperationException(
                    Last.Model.Name + " wastes none of what it produces, so it cannot be a source"
                    + " of " + watts.ToString("n0") + " W — place a block that does");
            }

            Last.PowerProducedWatts = watts / fraction;
            return this;
        }

/// <summary>Consuming operation.</summary>
        public GridBuilder Consuming(float watts)
        {
            if (Last != null) Last.PowerConsumedWatts = watts;
            return this;
        }

/// <summary>Thrusting operation.</summary>
        public GridBuilder Thrusting(float watts)
        {
            if (Last != null) Last.ThrustWatts = watts;
            return this;
        }

/// <summary>Removes the .</summary>
        public GridBuilder Remove(Vector3I cell)
        {
            BlockInstance block = grid.GetAtCell(cell);
            if (block == null) return this;

            grid.Remove(block);
            placed.Remove(block);
            return this;
        }

        [ThreadStatic]
        public static Func<ThermalSettings, ThermalSettings> SettingsOverride;

/// <summary>ReorderPlacement operation.</summary>
        public GridBuilder ReorderPlacement(int seed)
        {
            if (seed == 0 || placed.Count < 2) return this;

            uint state = (uint)seed;
            if (state == 0u) state = 0x9E3779B9u;

            for (int i = placed.Count - 1; i > 0; i--)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;

                int j = (int)(state % (uint)(i + 1));
                BlockInstance swap = placed[i];
                placed[i] = placed[j];
                placed[j] = swap;
            }

            return this;
        }

/// <summary>Builds the API method table.</summary>
        public ThermalSimulation BuildSimulation(ThermalSettings settings = null, float initialTemperature = 293.15f)
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings effective = settings ?? new ThermalSettings();
            if (SettingsOverride != null) effective = SettingsOverride(effective);
/// <summary>ThermalSimulation operation.</summary>
            ThermalSimulation simulation = new ThermalSimulation(effective, grid);
            simulation.DefaultTemperature = initialTemperature;

            simulation.EnsureCapacity(placed.Count);

            for (int i = 0; i < placed.Count; i++)
            {
                simulation.Solver.AddBlock(placed[i], initialTemperature);
            }

            simulation.RebuildAll();
            return simulation;
        }
    }
}
