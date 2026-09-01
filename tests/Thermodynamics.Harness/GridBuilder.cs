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

        /// <summary>
        /// Drives the last placed block to put <paramref name="watts"/> of heat into the hull,
        /// whatever fraction of its power that block wastes.
        ///
        /// <para>
        /// **A rig that wants a heat source should say so in watts of heat.** Saying it in watts of
        /// *output* couples the rig to a block's efficiency, which is how every scenario in this
        /// repository came to be quoted off a reactor wasting a quarter of its output where the
        /// shipped one wastes a hundredth (backlog.md `C4`). A rig that is
        /// *about* a reactor still drives it at a real rating through <see cref="Producing"/>; this
        /// is for the ones where the block is only a place to put watts.
        /// </para>
        /// </summary>
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
        /// Takes a block back out, of the model and of the builder's own list.
        ///
        /// Removing straight from <see cref="Grid"/> is not enough and is quietly wrong:
        /// <see cref="BuildSimulation"/> adds every block this builder has ever placed, so a block
        /// removed from the model alone comes back as a node with no cells in the grid — and every
        /// per-block figure computed afterwards silently includes it.
        /// </summary>
        public GridBuilder Remove(Vector3I cell)
        {
            BlockInstance block = grid.GetAtCell(cell);
            if (block == null) return this;

            grid.Remove(block);
            placed.Remove(block);
            return this;
        }

        /// <summary>
        /// Applied to every settings object this builder is handed, when set.
        ///
        /// The scenario library constructs its own <see cref="ThermalSettings"/> in forty-one
        /// places, each tuned to the shipped defaults. This is the one seam that lets a profile
        /// sweep run all of them without editing any: set it, run, clear it. Null by default, so
        /// an ordinary scenario run is byte-identical to what it was.
        /// </summary>
        /// <remarks>
        /// Thread-local. xUnit runs test classes in parallel, and a plain static here
        /// leaked one test's profile into every other test running at that moment —
        /// sixty-six unrelated failures, none of them reproducible alone. The sweep is
        /// single-threaded, so it is unaffected.
        /// </remarks>
        [ThreadStatic]
        public static Func<ThermalSettings, ThermalSettings> SettingsOverride;

        /// <summary>
        /// Permutes the order the placed blocks will be handed to the simulation in, moving none of
        /// them.
        ///
        /// <para>
        /// **The same ship with a different index space.** A node's index comes from the order
        /// blocks were added, and two machines do not build a grid in the same order — a client
        /// receives blocks in whatever order the engine streams them, a server has them in the
        /// order they were welded or pasted. Every block stays at the cell it was placed at and
        /// keeps the model it was placed with, so the conduction graph, the surfaces, the rooms and
        /// the physics are identical: only the indices differ. That is what makes it the clean test
        /// of a correction that is keyed on position rather than on index
        /// (backlog.md `F22`).
        /// </para>
        ///
        /// <para>
        /// Deterministic, so two runs at one seed build the same permutation; a seed of zero is a
        /// no-op, which is what lets a caller pass the knob straight through.
        /// </para>
        /// </summary>
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

        public ThermalSimulation BuildSimulation(ThermalSettings settings = null, float initialTemperature = 293.15f)
        {
            ThermalSettings effective = settings ?? new ThermalSettings();
            if (SettingsOverride != null) effective = SettingsOverride(effective);
            ThermalSimulation simulation = new ThermalSimulation(effective, grid);
            simulation.DefaultTemperature = initialTemperature;

            // **The count is known here and the solver's lists would otherwise grow into it.** Every
            // harness build comes through this method — the corpus walks, the labs and the suite —
            // so the hint `ThermalGrid` gives the game's own load path belongs here too. The grid's
            // cell table is already full by now, because a builder is filled before it is built; a
            // blueprint is parsed a block at a time and never knows its own count in advance.
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
