using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Builder class for discovering and constructing coolant loops in the thermal simulation.
    /// Traverses the grid to find connected sequences of coolant pipes that form closed loops,
    /// then configures them with appropriate thermal properties and pump configurations.
    /// </summary>
    /// <remarks>
    /// A valid coolant loop must:
    /// 1. Be a closed ring (no open ends)
    /// 2. Have at least 2 coolant port connections per block
    /// 3. Not branch or cross itself
    /// 4. Have matching ports between adjacent blocks
    /// 
    /// The algorithm uses graph traversal on the block adjacency graph,
    /// following coolant ports to trace the loop path.
    /// </remarks>
    public static class CoolantLoopBuilder
    {
        /// <summary>
        /// Finds all coolant loops in a grid using default tracing.
        /// Convenience overload for basic loop discovery.
        /// </summary>
        /// <param name="grid">The grid model to search.</param>
        /// <param name="properties">Thermal properties for the coolant fluid and pipes.</param>
        /// <param name="initialTemperature">Starting temperature for all loop segments.</param>
        /// <returns>List of discovered CoolantLoop instances.</returns>
        public static List<CoolantLoop> FindLoops(GridModel grid, LoopThermalProperties properties, float initialTemperature)
        {
            return FindLoops(grid, properties, initialTemperature, null);
        }


        /// <summary>
        /// Finds all coolant loops in a grid with optional work tracking.
        /// </summary>
        /// <param name="grid">The grid model to search.</param>
        /// <param name="properties">Thermal properties for the coolant fluid and pipes.</param>
        /// <param name="initialTemperature">Starting temperature for all loop segments.</param>
        /// <param name="work">Optional SimulationWork instance to track loop search statistics.</param>
        /// <returns>List of discovered CoolantLoop instances.</returns>
        public static List<CoolantLoop> FindLoops(GridModel grid, LoopThermalProperties properties,
            float initialTemperature, SimulationWork work)
        {
            return FindLoops(grid, properties, initialTemperature, work, null);
        }


        /// <summary>
        /// Finds all coolant loops in a grid with diagnostics support.
        /// Traverses the block adjacency graph to discover closed-loop coolant paths.
        /// Each discovered loop is configured with thermal properties and pump information.
        /// </summary>
        /// <param name="grid">The grid model to search.</param>
        /// <param name="properties">Thermal properties for the coolant fluid and pipes.</param>
        /// <param name="initialTemperature">Starting temperature for all loop segments.</param>
        /// <param name="work">Optional SimulationWork instance to track loop search statistics.</param>
        /// <param name="diagnostics">Optional CoolantLoopDiagnostics for fault reporting.</param>
        /// <returns>List of discovered CoolantLoop instances.</returns>
        /// <remarks>
        /// The algorithm:
        /// 1. Iterate through all blocks in the grid
        /// 2. Skip blocks without coolant or with insufficient ports
        /// 3. Skip blocks already claimed (part of a previous loop)
        /// 4. Trace a ring starting from this block
        /// 5. If successful, create a CoolantLoop and add pumps
        /// 6. Mark all blocks in the ring as claimed
        /// 7. Report diagnostics for unclaimed coolant blocks
        /// </remarks>
        public static List<CoolantLoop> FindLoops(GridModel grid, LoopThermalProperties properties,
            float initialTemperature, SimulationWork work, CoolantLoopDiagnostics diagnostics)
        {
            List<CoolantLoop> loops = new List<CoolantLoop>();
            if (grid == null) return loops;

            if (work != null) work.LoopSearches++;

            // Early exit if no coolant blocks exist
            if (grid.CoolantBlockCount == 0) return loops;

            if (work != null) work.LoopSearchCells += grid.Blocks.Count;


            // Track which blocks have been assigned to loops to avoid duplicates
            HashSet<long> claimed = new HashSet<long>();

            IList<BlockInstance> blocks = grid.Blocks;
            for (int i = 0; i < blocks.Count; i++)
            {
                BlockInstance start = blocks[i];
                // Skip blocks without coolant configuration
                if (start.Model.Coolant == null) continue;
                // Must have at least 2 ports (input and output)
                if (start.Model.Coolant.LinkPorts.Length < 2) continue;
                // Skip if already part of another loop
                if (claimed.Contains(start.Key)) continue;


                // Try to trace a complete ring from this starting point
                List<BlockInstance> ring = TraceRing(grid, start);
                if (ring == null) continue;


                // Create the loop configuration
                CoolantLoop loop = new CoolantLoop(properties, initialTemperature);
                for (int r = 0; r < ring.Count; r++)
                {
                    loop.Pipes.Add(ring[r]);
                    claimed.Add(ring[r].Key);

                    // Check if this block is a pump
                    if (ring[r].Model.Coolant == null || !ring[r].Model.Coolant.IsPump) continue;


                    // Configure pump in the loop
                    CoolantPump pump = new CoolantPump();
                    pump.Block = ring[r];

                    // Determine pump direction relative to loop flow
                    pump.Direction = PumpDirection(grid, ring, r);
                    pump.MaxPowerWatts = ring[r].Model.Coolant.MaxPowerWatts;
                    loop.Pumps.Add(pump);
                }

                // Mark loop as having pump support
                loop.HasPump = loop.Pumps.Count > 0;
                // Set pipe length for flow calculations
                loop.ParcelLengthMetres = grid.GridSize;
                // Compute loop signature for identification
                loop.RefreshSignature();
                // Calculate thermal mass from coolant properties
                loop.RefreshThermalMass();
                // Calculate flow characteristics
                loop.RefreshFlow();
                loops.Add(loop);
            }

            // Generate diagnostics for unclaimed coolant blocks
            if (diagnostics != null) Diagnose(grid, claimed, loops, diagnostics);

            return loops;
        }


        /// <summary>
        /// Generates diagnostics for coolant loop discovery.
        /// Reports statistics about discovered loops and identifies unclaimed coolant blocks
        /// with their fault types (open ends, branches, etc.).
        /// </summary>
        /// <param name="grid">The grid model being analyzed.</param>
        /// <param name="claimed">Set of blocks that are part of discovered loops.</param>
        /// <param name="loops">List of discovered CoolantLoop instances.</param>
        /// <param name="diagnostics">Diagnostics object to populate.</param>
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
        /// Determines the direction of flow through a pump block within a loop.
        /// Compares the pump's hot/cold port configuration with the loop's flow direction.
        /// </summary>
        /// <param name="grid">The grid model containing blocks.</param>
        /// <param name="ring">List of blocks forming the loop in order.</param>
        /// <param name="pumpIndex">Index of the pump block in the ring.</param>
        /// <returns>1 if pump assists loop flow, -1 if opposes, 0 if unknown.</returns>
        private static int PumpDirection(GridModel grid, List<BlockInstance> ring, int pumpIndex)
        {
            if (ring.Count < 2) return 1;
            if (pumpIndex < 0 || pumpIndex >= ring.Count) return 1;

            // Get the two neighboring blocks in the loop
            int prevIndex = (pumpIndex - 1 + ring.Count) % ring.Count;
            int nextIndex = (pumpIndex + 1) % ring.Count;

            BlockInstance prev = ring[prevIndex];
            BlockInstance next = ring[nextIndex];

            // Get pump's port configuration
            HeatPumpShape pumpShape = ring[pumpIndex].Model.HeatPump;
            if (pumpShape == null) return 1;

            // Determine if pump flow aligns with loop flow
            // (simplified: compare neighbor positions to pump direction)
            Vector3I pumpDirection = pumpShape.HotDirection;
            Vector3I prevToPump = ring[pumpIndex].Min - prev.Min;
            Vector3I nextToPump = next.Min - ring[pumpIndex].Min;

            // If pump's hot side faces the "next" block, it's assisting flow
            if (Vector3I.Dot(pumpDirection, nextToPump) > 0) return 1;
            if (Vector3I.Dot(pumpDirection, prevToPump) > 0) return -1;

            return 1; // Default to assisting
        }


        /// <summary>
        /// Traces a closed loop starting from a given block.
        /// Follows coolant ports through the grid to find connected pipes.
        /// Returns a list of blocks forming the closed ring, or null if invalid.
        /// </summary>
        /// <param name="grid">The grid model to traverse.</param>
        /// <param name="start">Starting block instance.</param>
        /// <returns>List of blocks forming the closed loop, or null if not a valid ring.</returns>
        /// <remarks>
        /// The tracing algorithm uses a simple graph traversal:
        /// 1. Start at the given block with its first output port
        /// 2. Follow the port to the adjacent block
        /// 3. From that block, exit via the other port (not the one we came in on)
        /// 4. Continue until we return to the starting block
        /// 5. Check for validity conditions (no branches, no open ends)
        /// 
        /// Guard counter prevents infinite loops on invalid configurations.
        /// </remarks>
        private static List<BlockInstance> TraceRing(GridModel grid, BlockInstance start)
        {
            CoolantFault fault;
            return TraceRing(grid, start, out fault);
        }


        /// <summary>
        /// Traces a closed loop starting from a given block, reporting faults on failure.
        /// See TraceRing(GridModel, BlockInstance) for general description.
        /// </summary>
        /// <param name="grid">The grid model to traverse.</param>
        /// <param name="start">Starting block instance.</param>
        /// <param name="fault">Outputs the type of fault if tracing fails.</param>
        /// <returns>List of blocks forming the closed loop, or null if not valid.</returns>
        private static List<BlockInstance> TraceRing(GridModel grid, BlockInstance start,
            out CoolantFault fault)
        {
            fault = CoolantFault.None;

            // Get the start block's coolant configuration
            CoolantShape startShape = start.Model.Coolant;
            if (startShape == null || startShape.LinkPorts.Length < 2)
            {
                fault = CoolantFault.OpenEnd;
                return null;
            }

            // Use first port as entry point
            List<BlockInstance> ring = new List<BlockInstance>();
            HashSet<long> visited = new HashSet<long>();

            visited.Add(start.Key);
            ring.Add(start);

            BlockInstance current = start;
            GridPort exit = startShape.LinkPorts[0];

            int guard = grid.BlockCount + 1;

            // Trace loop until we either return to start or hit a fault
            while (guard-- > 0)
            {
                // Move to next block via the exit port
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

                // Find the entry port on the next block
                GridPort entry;
                if (!TryFindPortFacing(next, exit.Target, -exit.Direction, out entry))
                {
                    fault = CoolantFault.PortsDoNotMeet;
                    return null;
                }

                // Check if we've returned to the start block
                if (next == start)
                {
                    // Must enter via same port we started with to close the loop properly
                    if (!SamePort(entry, startShape.LinkPorts[0])) return ring;

                    fault = CoolantFault.DoubledBack;
                    return null;
                }

                // Check for branches (already visited this block)
                if (visited.Contains(next.Key))
                {
                    fault = CoolantFault.BranchOrCrossing;
                    return null;
                }

                // Find the other port on this block to continue the loop
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

            // Guard expired - likely a very long loop or invalid configuration
            fault = CoolantFault.BranchOrCrossing;
            return null;
        }


        /// <summary>
        /// Finds a coolant port on a block that faces a specific cell and direction.
        /// Used to match ports between adjacent blocks in a loop.
        /// </summary>
        /// <param name="block">The block to search.</param>
        /// <param name="cell">Target cell position of the port.</param>
        /// <param name="direction">Port direction vector.</param>
        /// <param name="port">Outputs the matching port if found.</param>
        /// <returns>True if a matching port was found.</returns>
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


        /// <summary>
        /// Finds the second coolant port on a block (opposite the entry port).
        /// Used to determine the exit direction when traversing a pipe in a loop.
        /// </summary>
        /// <param name="block">The block with coolant ports.</param>
        /// <param name="entry">The entry port we came from.</param>
        /// <param name="other">Outputs the other port (exit direction).</param>
        /// <returns>True if a second port was found.</returns>
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


        /// <summary>
        /// Compares two GridPort instances for equality.
        /// Ports are equal if they have the same cell position and direction.
        /// </summary>
        private static bool SamePort(GridPort a, GridPort b)
        {
            return a.Cell == b.Cell && a.Direction == b.Direction;
        }


        /// <summary>
        /// Calculates thermal conductance between adjacent pipe segments in a coolant loop.
        /// Represents the rate of heat transfer between connected pipes.
        /// </summary>
        /// <param name="grid">The grid model containing blocks.</param>
        /// <param name="pipe">The pipe block for which to calculate conductance.</param>
        /// <param name="properties">Loop thermal properties including heat transfer coefficient.</param>
        /// <returns>Thermal conductance in W/K.</returns>
        /// <remarks>
        /// Conductance = HeatTransferCoefficient * ContactArea
        /// ContactArea = CellFaceArea * PipeContactMultiplier
        /// </remarks>
        public static float PipeConductance(GridModel grid, BlockInstance pipe, LoopThermalProperties properties)
        {
            float area = grid.CellFaceArea * properties.PipeContactMultiplier;
            return properties.HeatTransferCoefficient * area;
        }


        /// <summary>
        /// Calculates thermal conductance between a pipe and its heat sink (e.g., block surface).
        /// Used for heat exchange between the coolant loop and surrounding blocks.
        /// </summary>
        /// <param name="grid">The grid model containing blocks.</param>
        /// <param name="properties">Loop thermal properties including heat transfer coefficient.</param>
        /// <returns>Thermal conductance in W/K.</returns>
        public static float PlateConductance(GridModel grid, LoopThermalProperties properties)
        {
            float area = grid.CellFaceArea * properties.SinkContactMultiplier;
            return properties.HeatTransferCoefficient * area;
        }
    }
}
