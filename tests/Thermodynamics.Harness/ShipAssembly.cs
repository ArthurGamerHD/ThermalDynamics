using System;
using System.Collections.Generic;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    public class ShipAssembly
    {
/// <summary>List operation.</summary>
        public readonly List<ThermalSimulation> Simulations = new List<ThermalSimulation>();

        public class Bridge
        {
            public ThermalNode A;
            public ThermalNode B;
            public float Conductance;
        }

/// <summary>List operation.</summary>
        public readonly List<Bridge> Bridges = new List<Bridge>();

        public IEnumerable<ThermalNode> Nodes
        {
            get
            {
                for (int i = 0; i < Simulations.Count; i++)
                {
                    IList<ThermalNode> nodes = Simulations[i].Solver.Nodes;
                    for (int j = 0; j < nodes.Count; j++) yield return nodes[j];
                }
            }
        }

        public int NodeCount
        {
            get
            {
                int total = 0;
                for (int i = 0; i < Simulations.Count; i++) total += Simulations[i].Solver.Nodes.Count;
                return total;
            }
        }

        public int LinkCount
        {
            get
            {
                int total = 0;
                for (int i = 0; i < Simulations.Count; i++) total += Simulations[i].Solver.LinkCount;
                return total;
            }
        }

        public int FlooredNodes
        {
            get
            {
                int total = 0;
                for (int i = 0; i < Simulations.Count; i++)
                {
                    total += Simulations[i].Solver.FlooredNodes;
                }

                return total;
            }
        }

        public long WorstGridSubstepCost
        {
            get
            {
                long worst = 0;
                for (int i = 0; i < Simulations.Count; i++)
                {
                    long cost = Simulations[i].SubstepCost;
                    if (cost > worst) worst = cost;
                }

                return worst;
            }
        }

/// <summary>CollectDiagnostics operation.</summary>
        public void CollectDiagnostics(bool on)
        {
            for (int i = 0; i < Simulations.Count; i++) Simulations[i].Solver.CollectDiagnostics = on;
        }

/// <summary>Step operation.</summary>
        public void Step(EnvironmentSample environment, float seconds)
        {
            for (int i = 0; i < Simulations.Count; i++)
            {
                Simulations[i].StepExact(1, environment);
            }

            Exchange(seconds);
        }

/// <summary>Exchange operation.</summary>
        public void Exchange(float seconds)
        {
            if (seconds <= 0f) return;

            for (int i = 0; i < Bridges.Count; i++)
            {
                Bridge bridge = Bridges[i];

                float difference = bridge.B.Temperature - bridge.A.Temperature;
                if (difference == 0f) continue;

                float watts = ThermalSolver.ClampExchange(bridge.Conductance * difference,
                    seconds, difference, bridge.A.ThermalMass, bridge.B.ThermalMass);

                float energy = watts * seconds;

                bridge.A.Temperature = Math.Max(ThermalConstants.MinimumTemperature,
                    bridge.A.Temperature + (energy / bridge.A.ThermalMass));
                bridge.B.Temperature = Math.Max(ThermalConstants.MinimumTemperature,
                    bridge.B.Temperature - (energy / bridge.B.ThermalMass));
            }
        }

        public float RequiredSubsteps
        {
            get
            {
                float peak = 0f;
                for (int i = 0; i < Simulations.Count; i++)
                {
                    float required = Simulations[i].Solver.LastRequiredSubsteps;
                    if (required > peak) peak = required;
                }
                return peak;
            }
        }

        public int GrantedSubsteps
        {
            get
            {
                int peak = 0;
                for (int i = 0; i < Simulations.Count; i++)
                {
                    int granted = Simulations[i].Solver.LastSubsteps;
                    if (granted > peak) peak = granted;
                }
                return peak;
            }
        }

        public float HeatGainWatts
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < Simulations.Count; i++) total += Simulations[i].HeatGainWatts;
                return total;
            }
        }

        public float VentedWatts
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < Simulations.Count; i++) total += Simulations[i].VentedWatts;
                return total;
            }
        }

        public float BulkKelvin
        {
            get
            {
                float energy = 0f;
                float mass = 0f;

                foreach (ThermalNode node in Nodes)
                {
                    energy += node.Energy;
                    mass += node.ThermalMass;
                }

                return mass > 0f ? energy / mass : 0f;
            }
        }

        public int RoomCount
        {
            get
            {
                int total = 0;
                for (int i = 0; i < Simulations.Count; i++) total += Simulations[i].Rooms.Map.RoomCount;
                return total;
            }
        }

/// <summary>Hottest operation.</summary>
        public ThermalNode Hottest()
        {
            ThermalNode hottest = null;

            foreach (ThermalNode node in Nodes)
            {
                if (hottest == null || node.Temperature > hottest.Temperature) hottest = node;
            }

            return hottest;
        }

/// <summary>Bridge2 operation.</summary>
        public void Bridge2(ThermalSimulation baseGrid, ThermalNode baseNode,
            ThermalSimulation topGrid, ThermalNode topNode)
        {
            if (baseNode == null || topNode == null || baseNode == topNode) return;

            float lattice = Math.Min(baseGrid.Grid.GridSize, topGrid.Grid.GridSize);

            float conductance = ConductionBuilder.Conductance(
                lattice, baseNode.Block, topNode.Block, 1, Face.Axis(Face.Up));

            if (conductance <= 0f) return;

            Bridges.Add(new Bridge { A = baseNode, B = topNode, Conductance = conductance });
        }
    }

    public class AssemblyRunner
    {
        private readonly ShipAssembly assembly;

/// <summary>AssemblyRunner operation.</summary>
        public AssemblyRunner(ShipAssembly assembly)
        {
            this.assembly = assembly;
        }

        public Func<float, EnvironmentSample> Environment = t => Worlds.Shadow();

        public float ElapsedSeconds { get; private set; }

/// <summary>List operation.</summary>
        public readonly List<float> Hottest = new List<float>();

/// <summary>List operation.</summary>
        public readonly List<float> Bulk = new List<float>();

        public bool AnyOverheating
        {
            get
            {
                for (int i = 0; i < assembly.Simulations.Count; i++)
                {
                    if (assembly.Simulations[i].Solver.Overheats.Count > 0) return true;
                }
                return false;
            }
        }

        public float SecondsToCritical = -1f;

        public Func<string, float> Integrity;

        public float SecondsToFirstLoss = -1f;

        private readonly Dictionary<long, float> damageTaken = new Dictionary<long, float>();

/// <summary>Run operation.</summary>
        public void Run(float seconds)
        {
            if (assembly.Simulations.Count == 0) return;

            float step = assembly.Simulations[0].Settings.StepSeconds;
            int steps = (int)Math.Round(seconds / step);

            for (int i = 0; i < steps; i++)
            {
/// <summary>Environment operation.</summary>
                EnvironmentSample environment = Environment(ElapsedSeconds);
                assembly.Step(environment, step);
                ElapsedSeconds += step;

                if (SecondsToCritical < 0f && AnyOverheating) SecondsToCritical = ElapsedSeconds;
                if (SecondsToFirstLoss < 0f && Integrity != null) AccumulateDamage();
            }

            ThermalNode hottest = assembly.Hottest();
            Hottest.Add(hottest == null ? 0f : hottest.Temperature);
            Bulk.Add(assembly.BulkKelvin);
        }

/// <summary>AccumulateDamage operation.</summary>
        private void AccumulateDamage()
        {
            for (int i = 0; i < assembly.Simulations.Count; i++)
            {
                IList<OverheatEvent> events = assembly.Simulations[i].Overheats;

                for (int e = 0; e < events.Count; e++)
                {
                    BlockInstance block = events[e].Block;
                    if (block == null || block.Model == null) continue;

/// <summary>Integrity operation.</summary>
                    float integrity = Integrity(block.Model.Name);
                    if (integrity <= 0f) continue;

                    float taken;
                    damageTaken.TryGetValue(block.Key, out taken);
                    taken += events[e].Damage;
                    damageTaken[block.Key] = taken;

                    if (taken >= integrity)
                    {
                        SecondsToFirstLoss = ElapsedSeconds;
                        return;
                    }
                }
            }
        }
    }
}
