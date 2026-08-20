namespace Thermodynamics.Core
{
    /// <summary>
    /// How much work the one-shot stages did, counted rather than timed.
    ///
    /// The stage timings in <see cref="ISimulationProfiler"/> measure elapsed time, which depends on
    /// the machine, the build and the rest of the load, so a test asserting on milliseconds is
    /// either too loose to catch anything or fails on other hardware.
    ///
    /// These counters measure elements visited, which is deterministic and so testable: that placing
    /// one block must not visit a hundred thousand nodes is a claim about the algorithm. They also
    /// disambiguate a telemetry report — a slow topology stage with a node-visit count equal to the
    /// grid size is a full rebuild, while the same stage with a count of forty is a slow machine.
    ///
    /// Every counter is incremented once per pass, or once per node inside a loop that already walks
    /// every node. None is incremented per link per substep, so the innermost loop is untouched.
    /// </summary>
    public class SimulationWork
    {
        /// <summary>Conduction graph rebuilds, and nodes walked by them.</summary>
        public long TopologyRebuilds;
        public long TopologyNodeVisits;
        public long LinksBuilt;

        /// <summary>Nodes taken out incrementally, and the links unpicked with them.</summary>
        public long NodesRemoved;
        public long LinksRemoved;

        /// <summary>Exposure refreshes, and nodes whose exposed faces were recomputed.</summary>
        public long ExposureRefreshes;
        public long ExposureNodeVisits;

        /// <summary>
        /// Passes over every link recomputing the conductance each node sees. Structural changes mark
        /// it stale and it is recomputed once before it is next read, so this should stay well below
        /// the number of changes that dirtied it.
        /// </summary>
        public long ConductanceRecomputes;

        /// <summary>Room air rebuilds, and rooms walked by them.</summary>
        public long RoomAirRebuilds;
        public long RoomAirRoomVisits;

        /// <summary>Coolant loop searches, and cells they scanned.</summary>
        public long LoopSearches;
        public long LoopSearchCells;

        /// <summary>Heat pump rebuilds, and nodes walked by them.</summary>
        public long HeatPumpRebuilds;
        public long HeatPumpNodeVisits;

        /// <summary>Room mapping passes begun and completed, and cells the flood fill classified.</summary>
        public long RoomPassesBegun;
        public long RoomPassesCompleted;
        public long RoomCellsVisited;

        /// <summary>Solver steps and substeps run.</summary>
        public long SolverSteps;
        public long SolverSubsteps;

        /// <summary>
        /// Per-step passes over every node that are not substeps: mirroring the node objects into
        /// the flat rows, and the stability estimate that sets the substep count.
        ///
        /// Both are fixed costs of a step rather than of the physics in it, and a step that runs
        /// either of them twice is doing a full node walk for an answer it already had. On a grid
        /// taking three substeps the fixed part is roughly half the step, so the count matters as
        /// much as the timing does.
        /// </summary>
        public long NodeStateSyncs;
        public long StabilityEstimates;

        /// <summary>
        /// Resets every counter to zero. A test measures a window by clearing before it and reading
        /// after, rather than by subtracting two snapshots.
        /// </summary>
        public void Reset()
        {
            TopologyRebuilds = 0;
            TopologyNodeVisits = 0;
            LinksBuilt = 0;
            NodesRemoved = 0;
            LinksRemoved = 0;
            ExposureRefreshes = 0;
            ExposureNodeVisits = 0;
            ConductanceRecomputes = 0;
            RoomAirRebuilds = 0;
            RoomAirRoomVisits = 0;
            LoopSearches = 0;
            LoopSearchCells = 0;
            HeatPumpRebuilds = 0;
            HeatPumpNodeVisits = 0;
            RoomPassesBegun = 0;
            RoomPassesCompleted = 0;
            RoomCellsVisited = 0;
            SolverSteps = 0;
            SolverSubsteps = 0;
            NodeStateSyncs = 0;
            StabilityEstimates = 0;
        }

        /// <summary>A copy of the current counts, so a caller can keep one window's figures.</summary>
        public SimulationWork Snapshot()
        {
            SimulationWork copy = new SimulationWork();
            copy.TopologyRebuilds = TopologyRebuilds;
            copy.TopologyNodeVisits = TopologyNodeVisits;
            copy.LinksBuilt = LinksBuilt;
            copy.NodesRemoved = NodesRemoved;
            copy.LinksRemoved = LinksRemoved;
            copy.ExposureRefreshes = ExposureRefreshes;
            copy.ExposureNodeVisits = ExposureNodeVisits;
            copy.ConductanceRecomputes = ConductanceRecomputes;
            copy.RoomAirRebuilds = RoomAirRebuilds;
            copy.RoomAirRoomVisits = RoomAirRoomVisits;
            copy.LoopSearches = LoopSearches;
            copy.LoopSearchCells = LoopSearchCells;
            copy.HeatPumpRebuilds = HeatPumpRebuilds;
            copy.HeatPumpNodeVisits = HeatPumpNodeVisits;
            copy.RoomPassesBegun = RoomPassesBegun;
            copy.RoomPassesCompleted = RoomPassesCompleted;
            copy.RoomCellsVisited = RoomCellsVisited;
            copy.SolverSteps = SolverSteps;
            copy.SolverSubsteps = SolverSubsteps;
            copy.NodeStateSyncs = NodeStateSyncs;
            copy.StabilityEstimates = StabilityEstimates;
            return copy;
        }

        /// <summary>
        /// Total elements a rebuild touched across every stage. The stages are counted separately
        /// because they are addressed separately, but a single frame pays their sum.
        /// </summary>
        public long TotalRebuildVisits
        {
            get
            {
                return TopologyNodeVisits + ExposureNodeVisits + RoomAirRoomVisits
                    + LoopSearchCells + HeatPumpNodeVisits + RoomCellsVisited;
            }
        }
    }
}
