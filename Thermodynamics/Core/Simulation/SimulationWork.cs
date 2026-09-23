namespace Thermodynamics.Core
{
    public class SimulationWork
    {
        public long TopologyRebuilds;
        public long TopologyNodeVisits;
        public long LinksBuilt;

        public long NodesRemoved;
        public long LinksRemoved;

        public long ExposureRefreshes;
        public long ExposureNodeVisits;

        public long ExposureNodeWrites;

        public long ConductanceRecomputes;

        public long RoomAirRebuilds;
        public long RoomAirRoomVisits;

        public long RoomAirFaceProbes;
        public long RoomAirFaceHits;

        public long LoopSearches;
        public long LoopSearchCells;

        public long HeatPumpRebuilds;
        public long HeatPumpNodeVisits;

        public long RoomPassesBegun;
        public long RoomPassesCompleted;
        public long RoomCellsVisited;

        public long SolverSteps;
        public long SolverSubsteps;

        public long NodeStateSyncs;

        public long FullNodeResyncs;
        public long StabilityEstimates;

        public long EnvironmentRowFills;

        public long StepAdvances;


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
            FullNodeResyncs = 0;
            StabilityEstimates = 0;
            EnvironmentRowFills = 0;
            StepAdvances = 0;
        }


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
            copy.FullNodeResyncs = FullNodeResyncs;
            copy.EnvironmentRowFills = EnvironmentRowFills;
            copy.StabilityEstimates = StabilityEstimates;
            copy.StepAdvances = StepAdvances;
            return copy;
        }
    }
}
