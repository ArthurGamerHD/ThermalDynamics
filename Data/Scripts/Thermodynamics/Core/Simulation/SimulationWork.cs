namespace Thermodynamics.Core
{
    /// <summary>
    /// How much work the one-shot stages did, counted rather than timed: a millisecond threshold is a
    /// claim about the machine, and "placing one block must not visit a hundred thousand nodes" is a
    /// claim about the algorithm. Nothing is incremented per link per substep.
    /// See load-and-hitching.md, Catching it again.
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
        /// Of those visits, the ones that found a face count that had actually moved.
        ///
        /// <para>
        /// The gap between this and <see cref="ExposureNodeVisits"/> is the whole of what Pass 9's
        /// seventh iteration removed: a refresh over an unchanged hull visits every node and writes
        /// none of them. It is counted rather than inferred because *the gate engaged* is the claim
        /// a skip's A/B rests on — two runs of a gate that never fires agree perfectly (`E8`).
        /// </para>
        /// </summary>
        public long ExposureNodeWrites;

        /// <summary>
        /// Passes over every link recomputing the conductance each node sees. Structural changes mark
        /// it stale and it is recomputed once before it is next read, so this should stay well below
        /// the number of changes that dirtied it.
        /// </summary>
        public long ConductanceRecomputes;

        /// <summary>Room air rebuilds, and rooms walked by them.</summary>
        public long RoomAirRebuilds;
        public long RoomAirRoomVisits;

        /// <summary>
        /// Faces the air rebuild asked the grid about, and the ones that found a block. The two
        /// together are what says whether that loop is bound by the answers it uses or by the ones
        /// it discards. See performance.md, Pass 4, Iteration 6.
        /// </summary>
        public long RoomAirFaceProbes;
        public long RoomAirFaceHits;

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
        /// Per-step passes over every node that are not substeps: the node mirror, and the stability
        /// estimate. A step that runs either twice is doing a full node walk for an answer it already
        /// had, which on a three-substep grid is half the step.
        /// </summary>
        public long NodeStateSyncs;
        public long StabilityEstimates;

        /// <summary>
        /// Node walks that filled the per-step environment rows. One per step is correct, and the count
        /// is what says so structurally where a timing only says the first substep is dearer.
        /// </summary>
        public long EnvironmentRowFills;

        /// <summary>
        /// Calls into the resumable stage machine that did any work.
        ///
        /// A step is spread over the frames of its window, so this is how many pieces it arrived
        /// in. Each piece re-enters the machine and dispatches a stage, and on a small grid that
        /// entry costs more than the element visits it carries.
        /// </summary>
        public long StepAdvances;

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
            ExposureNodeWrites = 0;
            ConductanceRecomputes = 0;
            RoomAirRebuilds = 0;
            RoomAirRoomVisits = 0;
            RoomAirFaceProbes = 0;
            RoomAirFaceHits = 0;
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
            EnvironmentRowFills = 0;
            StepAdvances = 0;
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
            copy.ExposureNodeWrites = ExposureNodeWrites;
            copy.ConductanceRecomputes = ConductanceRecomputes;
            copy.RoomAirRebuilds = RoomAirRebuilds;
            copy.RoomAirRoomVisits = RoomAirRoomVisits;
            copy.RoomAirFaceProbes = RoomAirFaceProbes;
            copy.RoomAirFaceHits = RoomAirFaceHits;
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
            copy.EnvironmentRowFills = EnvironmentRowFills;
            copy.StabilityEstimates = StabilityEstimates;
            copy.StepAdvances = StepAdvances;
            return copy;
        }
    }
}
