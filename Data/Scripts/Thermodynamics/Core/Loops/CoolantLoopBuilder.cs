using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Finds closed coolant rings in a grid.
    ///
    /// The walk is driven entirely by the ports declared on each block's
    /// <see cref="CoolantShape"/>, so blocks of any size or orientation work without the
    /// subtype-name special cases the original crawler needed. Every ring is discovered once,
    /// no matter which pipe the search starts from, because rings are keyed by their member set.
    /// </summary>
    public static class CoolantLoopBuilder
    {
        /// <summary>
        /// Finds every closed ring containing at least one pump.
        /// </summary>
        public static List<CoolantLoop> FindLoops(GridModel grid, LoopThermalProperties properties, float initialTemperature)
        {
            return FindLoops(grid, properties, initialTemperature, null);
        }

        /// <param name="work">
        /// Optional work counters. The search walks every block on the grid looking for the
        /// handful that carry coolant ports, so what it costs is a function of grid size and not
        /// of how much plumbing there is — which is exactly the sort of thing a load test needs
        /// to be able to assert about.
        /// </param>
        public static List<CoolantLoop> FindLoops(GridModel grid, LoopThermalProperties properties,
            float initialTemperature, SimulationWork work)
        {
            List<CoolantLoop> loops = new List<CoolantLoop>();
            if (grid == null) return loops;

            if (work != null)
            {
                work.LoopSearches++;
                work.LoopSearchCells += grid.Blocks.Count;
            }

            HashSet<long> claimed = new HashSet<long>();

            IList<BlockInstance> blocks = grid.Blocks;
            for (int i = 0; i < blocks.Count; i++)
            {
                BlockInstance start = blocks[i];
                if (start.Model.Coolant == null) continue;
                if (start.Model.Coolant.LinkPorts.Length < 2) continue;
                if (claimed.Contains(start.Key)) continue;

                List<BlockInstance> ring = TraceRing(grid, start);
                if (ring == null) continue;

                bool hasPump = false;
                for (int r = 0; r < ring.Count; r++)
                {
                    if (ring[r].Model.Coolant != null && ring[r].Model.Coolant.IsPump)
                    {
                        hasPump = true;
                        break;
                    }
                }
                if (!hasPump) continue;

                CoolantLoop loop = new CoolantLoop(properties, initialTemperature);
                for (int r = 0; r < ring.Count; r++)
                {
                    loop.Pipes.Add(ring[r]);
                    claimed.Add(ring[r].Key);
                }
                loop.HasPump = true;
                loop.RefreshSignature();
                loops.Add(loop);
            }

            return loops;
        }

        /// <summary>
        /// Walks from <paramref name="start"/> through connected ports until it either returns
        /// to the start (a ring) or runs out of pipe (a dead end).
        /// </summary>
        /// <returns>The ring's blocks in walk order, or null when the run is not closed.</returns>
        public static List<BlockInstance> TraceRing(GridModel grid, BlockInstance start)
        {
            if (grid == null || start == null || start.Model.Coolant == null) return null;

            List<GridPort> startPorts = start.CoolantLinkPorts();
            if (startPorts.Count < 2) return null;

            List<BlockInstance> ring = new List<BlockInstance>();
            HashSet<long> visited = new HashSet<long>();

            ring.Add(start);
            visited.Add(start.Key);

            BlockInstance current = start;
            GridPort exit = startPorts[0];

            // A ring can be no longer than the number of blocks on the grid.
            int guard = grid.BlockCount + 1;

            while (guard-- > 0)
            {
                BlockInstance next = grid.GetAtCell(exit.Target);
                if (next == null) return null;
                if (next.Model.Coolant == null) return null;

                GridPort entry;
                if (!TryFindPortFacing(next, exit.Target, -exit.Direction, out entry)) return null;

                if (next == start)
                {
                    // Closed the ring, but only if we came back in through a different port.
                    return SamePort(entry, startPorts[0]) ? null : ring;
                }

                if (visited.Contains(next.Key)) return null;

                GridPort onward;
                if (!TryFindOtherPort(next, entry, out onward)) return null;

                ring.Add(next);
                visited.Add(next.Key);

                current = next;
                exit = onward;
            }

            return null;
        }

        /// <summary>
        /// Finds a port on <paramref name="block"/> that sits on <paramref name="cell"/> and
        /// faces <paramref name="direction"/>.
        /// </summary>
        private static bool TryFindPortFacing(BlockInstance block, Vector3I cell, Vector3I direction, out GridPort port)
        {
            List<GridPort> ports = block.CoolantLinkPorts();
            for (int i = 0; i < ports.Count; i++)
            {
                if (ports[i].Cell == cell && ports[i].Direction == direction)
                {
                    port = ports[i];
                    return true;
                }
            }
            port = default(GridPort);
            return false;
        }

        private static bool TryFindOtherPort(BlockInstance block, GridPort entry, out GridPort other)
        {
            List<GridPort> ports = block.CoolantLinkPorts();
            for (int i = 0; i < ports.Count; i++)
            {
                if (SamePort(ports[i], entry)) continue;
                other = ports[i];
                return true;
            }
            other = default(GridPort);
            return false;
        }

        private static bool SamePort(GridPort a, GridPort b)
        {
            return a.Cell == b.Cell && a.Direction == b.Direction;
        }

        /// <summary>
        /// Conductance between the coolant and one pipe block it runs through, W/K.
        /// </summary>
        public static float PipeConductance(GridModel grid, BlockInstance pipe, LoopThermalProperties properties)
        {
            float k = properties.Conductivity * ThermalConstants.ReferenceConductivity;
            float area = grid.CellFaceArea * properties.PipeSurfaceAreaScaler;
            float length = grid.GridSize * 0.5f;
            if (length <= 0f) return 0f;
            return k * area / length;
        }

        /// <summary>
        /// Conductance between the coolant and a block bolted to a sink face, W/K.
        /// </summary>
        public static float PlateConductance(GridModel grid, LoopThermalProperties properties)
        {
            float k = properties.Conductivity * ThermalConstants.ReferenceConductivity;
            float area = grid.CellFaceArea * properties.PlateSurfaceAreaScaler;
            float length = grid.GridSize * 0.5f;
            if (length <= 0f) return 0f;
            return k * area / length;
        }
    }
}
