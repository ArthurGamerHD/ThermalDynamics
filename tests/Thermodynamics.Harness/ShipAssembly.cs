using System;
using System.Collections.Generic;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// A whole blueprint, running: every grid it contains, stepped together, with heat crossing the
    /// rotor and piston joints between them.
    ///
    /// <para>
    /// **A blueprint is one machine, not a pile of separate ones.** A turret is a small grid on a
    /// rotor bolted to the hull; a drilling rig is a piston stack; a hangar door is a set of
    /// advanced rotors. Running each of those as its own ship measures something that does not
    /// exist — a turret floating in space with its own heat budget — and it gets both halves wrong
    /// at once: the subgrid has no hull to dump into, and the hull has no subgrid to be warmed by.
    /// </para>
    ///
    /// <para>
    /// The game connects them with <c>ThermalBridges</c>: one conductance between the mechanical
    /// base and the head that sits on it, applied after the grids have stepped. This mirrors that
    /// exactly, including the clamp, so an assembly here behaves as the same ship does in a session.
    /// </para>
    /// </summary>
    public class ShipAssembly
    {
        /// <summary>One simulation per grid in the blueprint, parent first.</summary>
        public readonly List<ThermalSimulation> Simulations = new List<ThermalSimulation>();

        /// <summary>One joint between two grids.</summary>
        public class Bridge
        {
            public ThermalNode A;
            public ThermalNode B;
            public float Conductance;
        }

        public readonly List<Bridge> Bridges = new List<Bridge>();

        /// <summary>Every node across every grid. The assembly's blocks, in one list.</summary>
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

        /// <summary>
        /// Thermal links across every grid — block touching block, which is what a substep's
        /// conduction pass visits.
        ///
        /// **Not <see cref="Bridges"/>.** A bridge is a rotor or a piston between two grids and
        /// there are usually none; a link is a face two blocks share and there are one to three per
        /// block. `G6`'s cost half was scored with the joint count standing in for the link count,
        /// which left the whole conduction half of a substep out of the figure.
        /// </summary>
        public int LinkCount
        {
            get
            {
                int total = 0;
                for (int i = 0; i < Simulations.Count; i++) total += Simulations[i].Solver.LinkCount;
                return total;
            }
        }

        /// <summary>
        /// What one substep costs the grid that costs the most, in the unit
        /// <c>MaxElementVisitsPerStep</c> is spent in.
        ///
        /// **Per grid, because the allowance is per grid.** A blueprint with two hulls on a rotor is
        /// two simulations, each bounded on its own, so summing them scores a ship the bound never
        /// sees. On the 8,144-ship corpus that distinction is small — most blueprints are one grid —
        /// and it is the difference between scoring the mechanism and scoring a number near it.
        /// </summary>
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

        public void CollectDiagnostics(bool on)
        {
            for (int i = 0; i < Simulations.Count; i++) Simulations[i].Solver.CollectDiagnostics = on;
        }

        /// <summary>
        /// Steps every grid once and then exchanges heat across the joints.
        ///
        /// The order is the game's: grids step independently, then bridges move heat between the
        /// results. Doing it the other way round would let a bridge move energy the step then
        /// overwrote.
        /// </summary>
        public void Step(EnvironmentSample environment, float seconds)
        {
            for (int i = 0; i < Simulations.Count; i++)
            {
                Simulations[i].StepExact(1, environment);
            }

            Exchange(seconds);
        }

        /// <summary>
        /// Moves heat across every joint, bounded by the same clamp the solver uses on its own
        /// links: an exchange may not carry more energy than would equalise the pair it connects.
        /// </summary>
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

        /// <summary>Substeps the stiffest grid in the assembly demanded on its last step.</summary>
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

        /// <summary>Watts every grid is making, and shedding, added up.</summary>
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

        /// <summary>
        /// The temperature of the assembly as one lump: every node averaged, weighted by what it
        /// takes to heat it.
        ///
        /// The hottest block is the wrong thing to ask whether a ship has finished moving. A
        /// reactor reaches its working temperature in minutes while the thousands of tonnes of
        /// armour around it are still shedding what they started with, and the difference is not
        /// small — it is the whole balance between what a ship makes and what it vents. Weighting
        /// by thermal mass is what makes this the stored heat rather than a count of blocks: a
        /// heavy armour cube and an interior light both move the plain mean by the same amount, and
        /// only one of them holds any of the energy.
        /// </summary>
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

        /// <summary>Sealed compartments across every grid.</summary>
        public int RoomCount
        {
            get
            {
                int total = 0;
                for (int i = 0; i < Simulations.Count; i++) total += Simulations[i].Rooms.Map.RoomCount;
                return total;
            }
        }

        /// <summary>The hottest node anywhere in the assembly, or null.</summary>
        public ThermalNode Hottest()
        {
            ThermalNode hottest = null;

            foreach (ThermalNode node in Nodes)
            {
                if (hottest == null || node.Temperature > hottest.Temperature) hottest = node;
            }

            return hottest;
        }

        /// <summary>
        /// Builds the bridge between a mechanical base and the head it carries.
        ///
        /// One cell face of contact along the head's axis: a rotor joint is a single mounting plate
        /// whatever the size of the blocks on either side, and the lattice is the smaller of the two
        /// grids because that is the plate they share.
        /// </summary>
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

    /// <summary>
    /// Runs a whole assembly forward, the way <see cref="ScenarioRunner"/> runs one grid.
    ///
    /// Kept separate rather than folded into the scenario runner because the two answer different
    /// questions: that one is for a rig this repository builds, this one for a ship somebody else
    /// did, which comes with subgrids attached.
    /// </summary>
    public class AssemblyRunner
    {
        private readonly ShipAssembly assembly;

        public AssemblyRunner(ShipAssembly assembly)
        {
            this.assembly = assembly;
        }

        public Func<float, EnvironmentSample> Environment = t => Worlds.Shadow();

        public float ElapsedSeconds { get; private set; }

        /// <summary>Hottest temperature seen at the end of each sample, in order.</summary>
        public readonly List<float> Hottest = new List<float>();

        /// <summary>
        /// <see cref="ShipAssembly.BulkKelvin"/> at the end of each sample, in order — the same
        /// history as <see cref="Hottest"/> for the ship as a whole rather than its worst block.
        /// </summary>
        public readonly List<float> Bulk = new List<float>();

        /// <summary>Whether any grid reported a block above its critical temperature.</summary>
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

        /// <summary>Simulated seconds at which a block first went critical, or -1.</summary>
        public float SecondsToCritical = -1f;

        /// <summary>
        /// Hit points a block of a given subtype has, or zero where the caller does not know —
        /// <see cref="GameBlocks.IntegrityOf"/> is what a corpus run passes. Left null, the runner
        /// does no damage bookkeeping at all and <see cref="SecondsToFirstLoss"/> stays at -1.
        /// </summary>
        public Func<string, float> Integrity;

        /// <summary>
        /// Simulated seconds at which the first block would have been destroyed, or -1.
        ///
        /// <para>
        /// **The crossing and the loss are different events.** A block crosses its critical
        /// temperature while taking zero damage — the solver's rate is <c>(T - critical) x
        /// OverheatDamagePerKelvin</c> — and only survives as long as its own hit points last. This
        /// is the second event, accumulated from the same <see cref="OverheatEvent"/> stream the
        /// mod hands to <c>DoDamage</c>.
        /// </para>
        ///
        /// <para>
        /// **Only the first loss is measured, because only the first is exact.** Nothing is removed
        /// from the assembly when its integrity runs out, so after that moment the run is
        /// simulating a ship the game would no longer have — one block still making heat and still
        /// conducting. Up to it, the two agree exactly.
        /// </para>
        /// </summary>
        public float SecondsToFirstLoss = -1f;

        private readonly Dictionary<long, float> damageTaken = new Dictionary<long, float>();

        public void Run(float seconds)
        {
            if (assembly.Simulations.Count == 0) return;

            float step = assembly.Simulations[0].Settings.StepSeconds;
            int steps = (int)Math.Round(seconds / step);

            for (int i = 0; i < steps; i++)
            {
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

        /// <summary>
        /// Adds the step's overheat damage to the running total for each block, and files the first
        /// one whose hit points are gone.
        ///
        /// A block with no integrity the caller can price is skipped rather than treated as
        /// weightless: an install this harness could not read must not make every ship lose its
        /// first block on the step it crosses.
        /// </summary>
        private void AccumulateDamage()
        {
            for (int i = 0; i < assembly.Simulations.Count; i++)
            {
                IList<OverheatEvent> events = assembly.Simulations[i].Overheats;

                for (int e = 0; e < events.Count; e++)
                {
                    BlockInstance block = events[e].Block;
                    if (block == null || block.Model == null) continue;

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
