namespace Thermodynamics.Core
{
    /// <summary>
    /// How much work the one-shot stages did, counted rather than timed.
    ///
    /// The stage timings in <see cref="ISimulationProfiler"/> answer "how long did it take",
    /// which is the question a player's stutter poses but the wrong question for a test: a
    /// millisecond figure depends on the machine, the build and what else was running, so a test
    /// written against one either has a threshold so loose it catches nothing or so tight it
    /// fails on someone else's laptop.
    ///
    /// These counters answer "how many things did it touch", which is deterministic. That makes
    /// them the assertion a load test wants — <em>placing one block must not visit a hundred
    /// thousand nodes</em> is a claim about the algorithm, and it holds or fails identically
    /// everywhere. They also make a telemetry report explain itself: a slow topology stage with
    /// a node-visit count equal to the grid size is a global rebuild, and the same stage with a
    /// count of forty is a slow machine.
    ///
    /// Everything here is incremented once per pass or once per node inside a loop that already
    /// walks every node. Nothing is incremented per link per substep, so the innermost loop in
    /// the mod is untouched.
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
        /// Every counter, back to zero. A test measures a window by clearing before it and
        /// reading after, rather than by subtracting two snapshots.
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
            return copy;
        }

        /// <summary>
        /// Everything a rebuild touched, as one figure. The stages are separate because they are
        /// fixed separately, but what lands in a single frame is their sum.
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
