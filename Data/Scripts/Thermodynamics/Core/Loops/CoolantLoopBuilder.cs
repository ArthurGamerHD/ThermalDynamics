using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Finds closed coolant rings in a grid.
    ///
    /// The walk is driven entirely by the ports declared on each block's
    /// <see cref="CoolantShape"/>, so blocks of any size or orientation work without subtype-name
    /// special cases. Every ring is discovered once whichever pipe the search starts from, because
    /// rings are keyed by their member set.
    /// </summary>
    public static class CoolantLoopBuilder
    {
        /// <summary>Finds every closed ring containing at least one pump.</summary>
        public static List<CoolantLoop> FindLoops(GridModel grid, LoopThermalProperties properties, float initialTemperature)
        {
            return FindLoops(grid, properties, initialTemperature, null);
        }

        /// <param name="work">
        /// Optional work counters. The search walks every block on the grid to find the few carrying
        /// coolant ports, so its cost scales with grid size rather than with the amount of plumbing.
        /// </param>
        public static List<CoolantLoop> FindLoops(GridModel grid, LoopThermalProperties properties,
            float initialTemperature, SimulationWork work)
        {
            return FindLoops(grid, properties, initialTemperature, work, null);
        }

        /// <param name="diagnostics">
        /// Optional. When supplied, records why each coolant block that ended up in no loop did not.
        /// Costs one extra walk per unclaimed run and nothing at all when null, so an ordinary
        /// rebuild does not pay for a readout nobody opened.
        /// </param>
        public static List<CoolantLoop> FindLoops(GridModel grid, LoopThermalProperties properties,
            float initialTemperature, SimulationWork work, CoolantLoopDiagnostics diagnostics)
        {
            List<CoolantLoop> loops = new List<CoolantLoop>();
            if (grid == null) return loops;

            // The call is counted unconditionally but the cells only when the search walks them, so
            // the two counters separate how often the search runs from how much it costs.
            if (work != null) work.LoopSearches++;

            // A ring needs pipe and most grids have none. The grid's coolant block count is one
            // integer read, against a pass over every block on the grid.
            if (grid.CoolantBlockCount == 0) return loops;

            if (work != null) work.LoopSearchCells += grid.Blocks.Count;

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

                // A closed ring is a loop whether or not it holds a pump. It used to need one, which
                // meant destroying the pump deleted the loop and silently deleted every joule its
                // coolant was holding — a ship could dump heat by grinding its own pump. A pumpless
                // ring is now a loop that circulates nothing: it keeps its coolant and its heat, and
                // transports neither.
                CoolantLoop loop = new CoolantLoop(properties, initialTemperature);
                for (int r = 0; r < ring.Count; r++)
                {
                    loop.Pipes.Add(ring[r]);
                    claimed.Add(ring[r].Key);

                    if (ring[r].Model.Coolant == null || !ring[r].Model.Coolant.IsPump) continue;

                    CoolantPump pump = new CoolantPump();
                    pump.Block = ring[r];
                    loop.Pumps.Add(pump);
                }

                loop.HasPump = loop.Pumps.Count > 0;
                loop.RefreshSignature();
                loop.RefreshThermalMass();
                loop.RefreshFlow();
                loops.Add(loop);
            }

            if (diagnostics != null) Diagnose(grid, claimed, loops, diagnostics);

            return loops;
        }

        /// <summary>
        /// Names the fault on every coolant block no loop claimed.
        ///
        /// A closed pumpless ring is reported against every block in it, because the fix — add a
        /// pump — applies to the ring rather than to one cell. The walk is repeated here rather than
        /// remembered from the search above so that the search stays free when nothing is asking.
        /// </summary>
        private static void Diagnose(GridModel grid, HashSet<long> claimed,
            List<CoolantLoop> loops, CoolantLoopDiagnostics diagnostics)
        {
            diagnostics.Loops = loops.Count;
            diagnostics.PipesInLoops = claimed.Count;

            IList<BlockInstance> blocks = grid.Blocks;
            for (int i = 0; i < blocks.Count; i++)
            {
                BlockInstance block = blocks[i];
                if (block.Model.Coolant == null) continue;
                if (claimed.Contains(block.Key)) continue;

                CoolantFault fault;
                TraceRing(grid, block, out fault);
                diagnostics.Record(block, fault);
            }
        }

        /// <summary>
        /// Walks from <paramref name="start"/> through connected ports until it either returns
        /// to the start (a ring) or runs out of pipe (a dead end).
        /// </summary>
        /// <returns>The ring's blocks in walk order, or null when the run is not closed.</returns>
        public static List<BlockInstance> TraceRing(GridModel grid, BlockInstance start)
        {
            CoolantFault fault;
            return TraceRing(grid, start, out fault);
        }

        /// <summary>
        /// As <see cref="TraceRing(GridModel,BlockInstance)"/>, and reports why a run is not closed.
        ///
        /// The reason is what a player needs and the loop list cannot carry: the symptom of a broken
        /// ring is that it is absent. Every early return below names the fault it returns on rather
        /// than collapsing them all into null.
        /// </summary>
        public static List<BlockInstance> TraceRing(GridModel grid, BlockInstance start, out CoolantFault fault)
        {
            fault = CoolantFault.None;
            if (grid == null || start == null || start.Model.Coolant == null) return null;

            List<GridPort> startPorts = start.CoolantLinkPorts();
            if (startPorts.Count < 2)
            {
                fault = CoolantFault.OpenEnd;
                return null;
            }

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
                if (next == null)
                {
                    fault = CoolantFault.OpenEnd;
                    return null;
                }
                if (next.Model.Coolant == null)
                {
                    fault = CoolantFault.BlockedByNonCoolant;
                    return null;
                }

                GridPort entry;
                if (!TryFindPortFacing(next, exit.Target, -exit.Direction, out entry))
                {
                    fault = CoolantFault.PortsDoNotMeet;
                    return null;
                }

                if (next == start)
                {
                    // The ring closes only if the walk re-entered through a different port.
                    if (!SamePort(entry, startPorts[0])) return ring;

                    fault = CoolantFault.DoubledBack;
                    return null;
                }

                if (visited.Contains(next.Key))
                {
                    fault = CoolantFault.BranchOrCrossing;
                    return null;
                }

                GridPort onward;
                if (!TryFindOtherPort(next, entry, out onward))
                {
                    fault = CoolantFault.OpenEnd;
                    return null;
                }

                ring.Add(next);
                visited.Add(next.Key);

                current = next;
                exit = onward;
            }

            fault = CoolantFault.BranchOrCrossing;
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
