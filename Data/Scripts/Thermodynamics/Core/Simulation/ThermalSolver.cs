using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>A block that exceeded its critical temperature during a step.</summary>
    public struct OverheatEvent
    {
        public BlockInstance Block;
        public float Temperature;
        public float Damage;

        public OverheatEvent(BlockInstance block, float temperature, float damage)
        {
            Block = block;
            Temperature = temperature;
            Damage = damage;
        }
    }

    /// <summary>
    /// The thermal simulation for one grid.
    ///
    /// Integration is explicit, energy-conserving and order-independent: every exchange is
    /// computed from the temperatures at the start of a substep, accumulated as watts per node,
    /// and applied at the end. Results do not depend on node iteration order, so the passes may
    /// be split across frames or parallelised.
    /// </summary>
    public partial class ThermalSolver
    {
        /// <summary>Fraction of the theoretical stability limit a substep is allowed to use.</summary>
        public const float StabilitySafetyFactor = 0.5f;

        /// <summary>
        /// Upper bound on substeps per call, so a pathological grid cannot stall a frame.
        /// Read-only view of <see cref="ThermalSettings.MaxSubsteps"/>.
        /// </summary>
        public int MaxSubsteps
        {
            get { return settings.MaxSubsteps; }
        }

        private readonly ThermalSettings settings;
        private readonly GridModel grid;
        private readonly SurfaceMap surfaces;

        private readonly List<ThermalNode> nodes = new List<ThermalNode>();
        private readonly Dictionary<long, ThermalNode> nodesByKey = new Dictionary<long, ThermalNode>();
        private readonly List<ThermalLink> links = new List<ThermalLink>();
        private readonly List<CoolantLoop> loops = new List<CoolantLoop>();
        private readonly List<RoomAirNode> roomAir = new List<RoomAirNode>();
        private readonly List<HeatPumpDevice> heatPumps = new List<HeatPumpDevice>();

        private float[] nodeWatts = new float[0];
        private float[] nodeTemperatures = new float[0];

        /// <summary>
        /// Temperature at the top of the step, so the reported change spans the whole step rather
        /// than its last substep.
        /// </summary>
        private float[] nodeStepStart = new float[0];

        private float[] nodeConductanceTotal = new float[0];
        private float[] roomWatts = new float[0];

        /// <summary>
        /// Heat capacities the integrator uses for coupled elements: the real capacities unless
        /// <c>MaxSubstepsPerBlock</c> has raised them.
        ///
        /// Coolant loops and room air are capped alongside blocks because they are often the
        /// stiffest elements on the grid — room air has low capacity and large contact area, so a
        /// small compartment can demand more substeps than any block around it, and capping only
        /// blocks would leave the setting unable to reach its advertised value.
        /// </summary>
        private float[] loopEffectiveMass = new float[0];
        private float[] roomEffectiveMass = new float[0];

        // Node state the substep loop reads, copied out of the node objects once per step rather
        // than dereferenced once per node per substep.
        private float[] nodeThermalMass = new float[0];
        private float[] nodeRadiation = new float[0];
        private float[] nodeGeneration = new float[0];
        private float[] nodeExposedArea = new float[0];
        private float[] nodeEmissivity = new float[0];
        private int[] nodeExposedFaces = new int[0];

        /// <summary>
        /// Each node's critical temperature, or zero where the block has none.
        ///
        /// Mirrored for the same reason as the rows above it, and with a sharper payoff: the apply
        /// pass tests it once per node per substep, and reaching it through the node object is
        /// three dependent loads — <c>nodes[i]</c>, its block, its model — into memory scattered
        /// across the heap, to read a float that almost never fires. The block is dereferenced
        /// only once a node is actually over its limit.
        /// </summary>
        private float[] nodeCritical = new float[0];

        /// <summary>Exposed faces as a fraction of the node's total, six per node.</summary>
        private float[] nodeFaceWeights = new float[0];

        /// <summary>
        /// Fraction of a node's exchanges it may take this substep, 0..1.
        ///
        /// One in the ordinary case. Below one when the substep is longer than the node is stable
        /// over (<c>h * conductance &gt; mass</c>), which occurs only when the substep count the
        /// stability estimate asked for was refused by <see cref="MaxSubsteps"/>.
        ///
        /// Required in addition to the per-link clamp. That clamp bounds each exchange at the
        /// energy equalising a single pair, which is sufficient for a node with one neighbour but
        /// not for one with six: each neighbour may independently move it the whole way, so the
        /// node overshoots by up to its neighbour count and diverges. This factor bounds the sum.
        /// </summary>
        private float[] nodeRelaxation = new float[0];

        /// <summary>
        /// Per-parcel conductance totals, reused across loops and substeps.
        ///
        /// Sized to the largest ring seen; a ring's parcel count is its pipe count, so this stays
        /// small even on a heavily plumbed grid.
        /// </summary>
        private float[] parcelConductanceTotal = new float[0];

        /// <summary>
        /// Per-node environment terms that are constant across the substeps of one step.
        ///
        /// <para>
        /// Solar gain, friction and the convection coefficient depend only on emissivity, exposed
        /// area, sun incidence and lit fraction — none of which a substep changes. Only the
        /// <c>(T - ambient)</c> factor convection multiplies varies. The relaxation factor
        /// <c>mass / (h * conductance)</c> is likewise fixed for the whole step.
        /// </para>
        ///
        /// <para>
        /// Filled by the first substep's environment pass rather than by a pass of its own, so
        /// work accounting and pacing are unchanged and later substeps only read. Results are
        /// bit-identical to recomputing; <c>PrecomputedEnvironmentTests</c> asserts this.
        /// </para>
        /// </summary>
        private float[] nodeSolarRow = new float[0];
        private float[] nodeFrictionRow = new float[0];
        private float[] nodeConvectionRow = new float[0];

        /// <summary>
        /// Waste heat, solar and friction summed: everything a node gains that does not depend on
        /// its own temperature, and so is fixed for the whole step.
        ///
        /// Carried as its own row rather than summed per substep because the environment pass is
        /// bound by how many node arrays it streams, not by its arithmetic. The three terms are
        /// still kept apart above, for the diagnostics substep and for nothing else.
        /// </summary>
        private float[] nodeSourceRow = new float[0];

        /// <summary>
        /// False when the rows above must be recomputed rather than read.
        ///
        /// Cleared at the top of every step, and again whenever the self-shadow pass publishes new
        /// lit fractions — the one row input that can change mid-step, since that pass runs on a
        /// budget of its own.
        /// </summary>
        private bool environmentRowsValid;

        /// <summary>Forces the per-step environment rows to be recomputed on the next pass.</summary>
        private void InvalidateEnvironmentRows()
        {
            environmentRowsValid = false;
        }

        /// <summary>
        /// Set false to recompute the per-step environment terms on every substep instead of
        /// caching them. Test hook: <c>PrecomputedEnvironmentTests</c> runs the same grid both
        /// ways and compares the results bit for bit.
        /// </summary>
        public bool PrecomputeEnvironment = true;

        /// <summary>
        /// Fraction of each node face the sun reaches, six per node, 0..1. All ones when
        /// self-shadowing is off, so the cheap path costs nothing extra.
        ///
        /// Tracked per face rather than per block: a block buried along the sun's axis can still
        /// have side faces fully exposed, and a per-block figure would shadow them incorrectly.
        /// </summary>
        private float[] nodeSunLit = new float[0];

        /// <summary>The grid's own shadow, rebuilt when the sun has moved far enough to matter.</summary>
        private readonly SunShadowMap sunShadow = new SunShadowMap();

        /// <summary>Other grids whose shadows fall on this one. Filled by the host; usually empty.</summary>
        public readonly List<SunShadowMap.Occluder> SunOccluders = new List<SunShadowMap.Occluder>();

        /// <summary>
        /// Marks <see cref="SunOccluders"/> as moved or changed so the next pass rebuilds against
        /// them. Called by the host, which owns the judgement of when an occluder has shifted.
        /// </summary>
        public void MarkSunOccludersChanged()
        {
            sunLitDirty = true;
        }

        /// <summary>
        /// How far the sun may move before a new shadow pass starts, as cos(2°). A two-degree lag
        /// displaces a shadow edge by a fraction of a cell; a tighter threshold would restart the
        /// pass faster than it can complete under a slowly moving sun.
        /// </summary>
        private const float SunRebuildCosine = 0.99939f;

        /// <summary>
        /// Cells walked per environment pass. The walk is exact and so is budgeted like the room
        /// mapper's flood fill: a slice per tick, with the previous result readable until the new
        /// one completes.
        /// </summary>
        public int SunShadowBudget = 2048;

        /// <summary>
        /// Nodes whose lit fraction is refreshed per step once a shadow pass completes.
        ///
        /// Publishing a completed pass is six shadow lookups per node, which on a large grid costs
        /// more than the step it runs inside, so it is budgeted like the pass itself. The cost is
        /// that some faces read the previous shadow for a few steps — within the two-degree lag
        /// the map already tolerates.
        /// </summary>
        public int SunLitBudget = 4096;

        /// <summary>Where a lit-fraction refresh has got to, and whether one is running.</summary>
        private int sunLitCursor;
        private bool sunLitPending;

        /// <summary>
        /// Constant to fill every face with instead of reading the shadow map, or negative when the
        /// map is the source. Used when self-shadowing is disabled and no map exists.
        /// </summary>
        private float sunLitFill = -1f;

        /// <summary>True when <see cref="nodeSunLit"/> no longer matches the nodes or the map.</summary>
        private bool sunLitDirty = true;

        /// <summary>
        /// Reduced thermal mass of each link, <c>mA*mB/(mA+mB)</c>. The only part of the overshoot
        /// clamp not derived from current temperatures; it changes only when a block's mass does,
        /// so it is cached rather than recomputed per link per substep.
        /// </summary>
        private float[] linkMassFactor = new float[0];

        /// <summary>
        /// First link whose cached reduced mass is stale, or <see cref="int.MaxValue"/> when none
        /// are. An index rather than a flag so appending links for a newly placed block
        /// invalidates only the appended rows; a mass change invalidates from zero.
        /// </summary>
        private int linkMassFactorFrom;

        // The conduction loop mirrored into flat arrays. This is the innermost loop in the mod —
        // a large grid visits every link several hundred thousand times per simulated second —
        // and indexing List<ThermalLink> copies the whole struct to use three of its fields.
        // ContactFaces is diagnostic only and stays in the list.
        private int[] linkA = new int[0];
        private int[] linkB = new int[0];
        private float[] linkConductance = new float[0];

        /// <summary>Per-face weights of the sun and the airflow, resolved once per step.</summary>
        private readonly float[] sunWeights = new float[Face.Count];
        private readonly float[] windWeights = new float[Face.Count];

        /// <summary>Reused per registered point source, one source at a time.</summary>
        private readonly float[] sourceWeights = new float[Face.Count];

        /// <summary>
        /// Record each mechanism's contribution to each node, for the debug readout and the
        /// telemetry report. Off by default: five floats per node per substep that the simulation
        /// itself never reads.
        /// </summary>
        public bool CollectDiagnostics;

        /// <summary>
        /// Net watts the environment exchanged with the whole grid on the last substep: radiation
        /// plus convection, negative when the grid is losing heat to its surroundings.
        ///
        /// Accumulated on the hot path rather than behind <see cref="CollectDiagnostics"/>,
        /// because the question it answers — is this ship shedding more heat than it makes — is
        /// one a player asks of a working ship rather than of an instrumented one. It costs one
        /// add per node per substep against a pass that already reads both figures.
        /// </summary>
        public float LastEnvironmentWatts { get; private set; }

        /// <summary>
        /// Watts the grid vented on the last substep: the losing half of
        /// <see cref="LastEnvironmentWatts"/>, as a positive number. Zero while a grid is net
        /// absorbing, which a hull in sunlight or in hot atmosphere can be.
        /// </summary>
        public float LastVentedWatts
        {
            get { return LastEnvironmentWatts < 0f ? -LastEnvironmentWatts : 0f; }
        }

        /// <summary>
        /// Watts the grid put into itself on the last substep: waste heat, solar gain and
        /// aerodynamic friction. The figure to read <see cref="LastVentedWatts"/> against —
        /// venting alone says nothing about whether a ship is coping.
        /// </summary>
        public float LastHeatGainWatts { get; private set; }

        private float environmentWattsAccumulator;
        private float heatGainAccumulator;

        /// <summary>
        /// Starts a substep's heat totals. Called from both stepping paths: the direct one, which
        /// runs a whole substep in a call, and the spread one, which cuts each substep into
        /// budgeted slices across frames. Publishing from only one of them is how the first
        /// version of this reported zero on every real grid — the spread path is the one the game
        /// takes.
        /// </summary>
        private void ResetEnvironmentTotals()
        {
            environmentWattsAccumulator = 0f;
            heatGainAccumulator = 0f;
        }

        /// <summary>
        /// Publishes a completed substep's heat totals, so the figures are an instantaneous rate
        /// from the most recent pass rather than a sum that grows with the session. This matches
        /// the per-node LastRadiationWatts beside them, which are also last-substep values.
        /// </summary>
        private void PublishEnvironmentTotals()
        {
            LastEnvironmentWatts = environmentWattsAccumulator;
            LastHeatGainWatts = heatGainAccumulator;
        }

        private readonly List<OverheatEvent> overheats = new List<OverheatEvent>();

        private readonly ThermalThresholds thresholds = new ThermalThresholds();
        private readonly List<ThresholdCrossing> crossings = new List<ThresholdCrossing>();
        private readonly int[] exposureScratch = new int[Face.Count];
        private readonly List<BlockInstance> neighbourScratch = new List<BlockInstance>();

        private IBlockAdjacency adjacency;

        /// <summary>
        /// Work counters for the one-shot stages. Never null. The host may replace it with its own
        /// instance to share counters with the room mapper.
        /// </summary>
        public SimulationWork Work = new SimulationWork();

        private bool linksDirty = true;

        /// <summary>
        /// Nodes placed since the graph was last built, whose links have not been made yet.
        ///
        /// Placement is the only topology change that cannot invalidate an existing link, so its
        /// links are appended and the rest of the graph left alone. This keeps welding, blueprint
        /// pasting and projector builds proportional to the blocks added rather than the grid.
        /// Removal, a changed mount and a new adjacency source all take the full rebuild path.
        /// </summary>
        private readonly List<ThermalNode> pendingLinkNodes = new List<ThermalNode>();

        /// <summary>How many links have been mirrored into the flat arrays.</summary>
        private int syncedLinks;

        /// <summary>Set when node indices move, which invalidates every mirrored row.</summary>
        private bool resyncAll = true;

        public ThermalSolver(ThermalSettings settings, GridModel grid, SurfaceMap surfaces)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            if (grid == null) throw new ArgumentNullException("grid");
            if (surfaces == null) throw new ArgumentNullException("surfaces");

            this.settings = settings;
            this.grid = grid;
            this.surfaces = surfaces;

            // Until the first step derives one, the environment must read as vacuum rather than
            // 0 K. RequiredSubsteps, the HUD and the host's pre-step telemetry sample all read it
            // before any step has run.
            Environment = EnvironmentState.Vacuum(settings.VacuumTemperature);
        }

        public ThermalSettings Settings
        {
            get { return settings; }
        }

        public GridModel Grid
        {
            get { return grid; }
        }

        /// <summary>
        /// Source of block adjacency for the conduction graph. Defaults to the
        /// <see cref="GridModel"/>, which resolves adjacency from its own cell map. A host that
        /// already maintains a face connectivity graph can supply it here. Assigning invalidates
        /// the existing links.
        /// </summary>
        public IBlockAdjacency Adjacency
        {
            get { return adjacency ?? grid; }
            set
            {
                adjacency = value;
                linksDirty = true;
            }
        }

        public IList<ThermalNode> Nodes
        {
            get { return nodes; }
        }

        /// <summary>
        /// The conduction graph, rebuilt first if the layout has changed since it was last
        /// built. Use this when the answer has to be current.
        /// </summary>
        public IList<ThermalLink> Links
        {
            get { RebuildLinksIfNeeded(); return links; }
        }

        /// <summary>
        /// Links the graph holds now, without rebuilding it. Reading <see cref="Links"/> instead
        /// would trigger a full rebuild outside any stage bracket, so diagnostics use this and
        /// accept a count from before a pending layout change.
        /// </summary>
        public int LinkCount
        {
            get { return links.Count; }
        }

        public IList<CoolantLoop> Loops
        {
            get { return loops; }
        }

        /// <summary>
        /// Air masses of the grid's sealed rooms, one entry per room still holding pressure. A
        /// vented room has no entry; its faces exchange with the outdoors instead.
        /// </summary>
        public IList<RoomAirNode> RoomAir
        {
            get { return roomAir; }
        }

        /// <summary>
        /// The grid's heat pumps, connected or not. An unconnected pump is still listed so a
        /// readout can report why it is idle.
        /// </summary>
        public IList<HeatPumpDevice> HeatPumps
        {
            get { return heatPumps; }
        }

        /// <summary>Blocks that took heat damage during the last <see cref="Step"/>.</summary>
        public IList<OverheatEvent> Overheats
        {
            get { return overheats; }
        }

        /// <summary>Temperatures being watched on this grid. Empty by default.</summary>
        public ThermalThresholds Thresholds
        {
            get { return thresholds; }
        }

        /// <summary>Threshold crossings during the last <see cref="Step"/>.</summary>
        public IList<ThresholdCrossing> Crossings
        {
            get { return crossings; }
        }

        /// <summary>Substeps the last <see cref="Step"/> needed for stability.</summary>
        public int LastSubsteps { get; private set; }

        /// <summary>
        /// Substeps a full step would have needed, before <see cref="MaxSubsteps"/> clamped the
        /// count and before rounding up to a whole number.
        ///
        /// <see cref="LastSubsteps"/> reports what was granted, and so what the step cost; this
        /// reports what was demanded, and keeps moving after the budget has bound. Scaled to a
        /// full step because the estimate is proportional to step length: measured on a step
        /// already shortened to fit <c>MaxElementVisitsPerStep</c>, it would duplicate the granted
        /// figure.
        /// </summary>
        public float LastRequiredSubsteps { get; private set; }

        /// <summary>
        /// Substeps one node alone would need for a full step, from its real heat capacity.
        /// <see cref="LastRequiredSubsteps"/> is the maximum of this over all nodes. Public so
        /// <summary>
        /// W/K out of one node through every link it has.
        ///
        /// The other half of why a block is hot. A block generating heat sheds it through its own
        /// exposed faces and through this, and a buried block has only this — so a large generation
        /// against a small conductance is a block that must run a wide gradient to get rid of what
        /// it makes, however healthy the grid around it looks.
        /// </summary>
        public float NodeConductanceTotal(int index)
        {
            if (index < 0 || index >= nodes.Count) return 0f;
            if (index >= nodeConductanceTotal.Length) return 0f;

            return nodeConductanceTotal[index];
        }

        /// per-block-type telemetry can attribute a grid's substep count to specific definitions.
        /// </summary>
        public float NodeSubstepDemand(int index)
        {
            if (index < 0 || index >= nodes.Count) return 0f;

            // Same guard as the profile: a node appended during a step has no mirrored row yet.
            if (index >= nodeConductanceTotal.Length) return 0f;

            float capacity = nodes[index].ThermalMass;
            if (capacity <= 0f) return 0f;

            StabilityTerms terms = StabilityEnvironment();
            return (NodeStabilityRate(index, ref terms) / capacity)
                * (settings.StepSeconds / StabilitySafetyFactor);
        }

        /// <summary>True when the last step hit <see cref="MaxSubsteps"/> and had to clamp.</summary>
        public bool LastStepWasClamped { get; private set; }

        public long StepCount { get; private set; }

        /// <summary>The environment used by the most recent step.</summary>
        public EnvironmentState Environment { get; private set; }

        // ---- topology ----------------------------------------------------------------------

        /// <summary>
        /// Registers a block and returns its node, or null if the block is marked
        /// <c>ExcludeFromSimulation</c>. Returns the existing node if the block is already registered.
        /// </summary>
        public ThermalNode AddBlock(BlockInstance block, float initialTemperature)
        {
            if (block == null) throw new ArgumentNullException("block");
            if (block.Thermal.ExcludeFromSimulation) return null;
            if (nodesByKey.ContainsKey(block.Key)) return nodesByKey[block.Key];

            ThermalNode node = new ThermalNode(block, grid.GridSize, initialTemperature, settings.HeatTimeScale);
            node.Index = nodes.Count;
            nodes.Add(node);
            nodesByKey[block.Key] = node;

            // Appending moves no existing index and invalidates no existing link, so the node is
            // queued for incremental linking rather than dirtying the whole graph.
            node.PendingLinks = true;
            pendingLinkNodes.Add(node);
            EnsureNodeChainCapacity(nodes.Count);
            nodeFirstLink[node.Index] = -1;

            sunLitDirty = true;
            return node;
        }

        public bool RemoveBlock(BlockInstance block)
        {
            if (block == null) return false;

            ThermalNode node;
            if (!nodesByKey.TryGetValue(block.Key, out node)) return false;

            // A removal moves another node into the hole, so a step in flight would be summing
            // watts against indices that no longer refer to the same blocks.
            AbandonStep();

            nodesByKey.Remove(block.Key);

            // A node still waiting to be linked has no links and no chain entry, so it is dropped
            // from the queue rather than routed through the incremental removal path.
            if (node.PendingLinks)
            {
                node.PendingLinks = false;
                pendingLinkNodes.Remove(node);
            }

            if (linksDirty)
            {
                // A full rebuild is already due, so unpicking this node's links would be wasted.
                // Remove it directly and let the rebuild reconstruct the graph.
                nodes.RemoveAt(node.Index);
                for (int i = node.Index; i < nodes.Count; i++)
                {
                    nodes[i].Index = i;
                }
                resyncAll = true;
            }
            else
            {
                RemoveNodeIncremental(node);
            }

            sunLitDirty = true;

            // The cached hottest index refers to a slot, and a removal moves the last node into
            // another slot. Cleared rather than repaired: the next step's write-back rebuilds it,
            // and until then callers fall back to a linear scan.
            hottestNode = -1;

            return true;
        }

        /// <summary>
        /// Rebuilds the conduction links of one block whose geometry or mounting changed.
        ///
        /// Contact area is a product of both blocks' mount fractions, so a change to one end
        /// changes the conductance of every link touching it — the links are wrong rather than
        /// merely stale. They are dropped and the node requeued for the same incremental link
        /// build a freshly placed block takes, which costs the node's degree rather than the
        /// grid.
        ///
        /// Exposure and room membership are not touched here: they follow from the surface map
        /// and the room map, which the caller owns.
        /// </summary>
        /// <returns>False when the block has no node.</returns>
        public bool RefreshBlockLinks(BlockInstance block)
        {
            if (block == null) return false;

            ThermalNode node;
            if (!nodesByKey.TryGetValue(block.Key, out node)) return false;

            // A full rebuild is already due, or the node has never been linked: either way the
            // links this would unpick do not exist yet.
            if (linksDirty || node.PendingLinks) return true;

            // Link indices move, so a step in flight would be summing watts against links that no
            // longer mean what it read.
            AbandonStep();

            EnsureBuffers();
            EnsureNodeChainCapacity(nodes.Count);
            DropLinksOf(node);

            node.PendingLinks = true;
            pendingLinkNodes.Add(node);
            return true;
        }

        /// <summary>
        /// Recounts one block's exposed faces. The cheapest unit of exposure work there is: a
        /// block whose own surfaces changed needs this even when no room around it moved.
        /// </summary>
        public void RefreshExposureOf(BlockInstance block, RoomMap rooms)
        {
            if (block == null) return;

            ThermalNode node = GetNode(block);
            if (node == null) return;

            Work.ExposureRefreshes++;
            Work.ExposureNodeVisits++;

            surfaces.GetExposedFaces(block, rooms, exposureScratch);
            for (int f = 0; f < Face.Count; f++)
            {
                node.ExposedFaces[f] = exposureScratch[f];
            }
            node.RefreshExposure();
        }

        public ThermalNode GetNode(BlockInstance block)
        {
            if (block == null) return null;
            ThermalNode node;
            return nodesByKey.TryGetValue(block.Key, out node) ? node : null;
        }

        public ThermalNode GetNodeAt(Vector3I cell)
        {
            return GetNode(grid.GetAtCell(cell));
        }

        /// <summary>Registers every block on the grid at one starting temperature.</summary>
        public void AddAllBlocks(float initialTemperature)
        {
            IList<BlockInstance> blocks = grid.Blocks;
            for (int i = 0; i < blocks.Count; i++)
            {
                AddBlock(blocks[i], initialTemperature);
            }
        }

        /// <summary>Forces the conduction graph to be rebuilt before the next step.</summary>
        public void InvalidateLinks()
        {
            linksDirty = true;
        }

        /// <summary>
        /// Brings the conduction graph up to date, incrementally when only blocks have been placed
        /// and by full rebuild otherwise. No-op when the graph already matches the layout.
        /// Public so the host can run it inside its own topology stage, where it is timed as such,
        /// rather than having the first step absorb the cost.
        /// </summary>
        public void BuildLinksIfNeeded()
        {
            RebuildLinksIfNeeded();
        }

        private void RebuildLinksIfNeeded()
        {
            if (linksDirty)
            {
                RebuildLinks();
                return;
            }

            if (pendingLinkNodes.Count > 0) LinkPendingNodes();
        }

        /// <summary>
        /// Rebuilds every conduction link. O(cells * 6); called when the block layout changes,
        /// not per step.
        /// </summary>
        public void RebuildLinks()
        {
            // Every link index changes, invalidating anything a step in flight has accumulated.
            AbandonStep();

            Work.TopologyRebuilds++;
            Work.TopologyNodeVisits += nodes.Count;

            // A full build makes every link, including those the pending queue was holding.
            for (int i = 0; i < pendingLinkNodes.Count; i++) pendingLinkNodes[i].PendingLinks = false;
            pendingLinkNodes.Clear();

            links.Clear();
            syncedLinks = 0;
            EnsureBuffers();
            ResetLinkChains();
            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i].LinkCount = 0;
            }

            IBlockAdjacency adjacency = Adjacency;

            for (int i = 0; i < nodes.Count; i++)
            {
                ThermalNode a = nodes[i];

                neighbourScratch.Clear();
                adjacency.GetNeighbours(a.Block, neighbourScratch);

                for (int n = 0; n < neighbourScratch.Count; n++)
                {
                    ThermalNode b = GetNode(neighbourScratch[n]);
                    if (b == null) continue;

                    // Visit each pair once.
                    if (b.Index <= a.Index) continue;

                    int face = ConductionBuilder.ContactFace(a.Block, b.Block);
                    if (face < 0) continue;

                    int contacts = ConductionBuilder.CountContactFaces(a.Block, b.Block);
                    if (contacts <= 0) continue;

                    float conductance = ConductionBuilder.Conductance(
                        grid.GridSize, a.Block, b.Block, contacts, Face.Axis(face));
                    if (conductance <= 0f) continue;

                    links.Add(new ThermalLink(a.Index, b.Index, conductance, contacts));
                    ChainLink(links.Count - 1);
                    a.LinkCount++;
                    b.LinkCount++;
                }
            }

            Work.LinksBuilt += links.Count;

            linksDirty = false;
            linkMassFactorFrom = 0;
            SyncLinkArrays();
            EnsureBuffers();
            RecomputeConductanceTotals();
        }

        /// <summary>
        /// Copies the link fields the substep loop reads into flat arrays.
        ///
        /// Writes only rows not already mirrored: a full rebuild resets the mark and copies
        /// everything, an incremental one appends. Arrays grow by doubling and retain their
        /// contents, since rows below the appended ones remain valid.
        /// </summary>
        private void SyncLinkArrays()
        {
            if (linkA.Length < links.Count)
            {
                int size = Math.Max(16, links.Count * 2);
                Array.Resize(ref linkA, size);
                Array.Resize(ref linkB, size);
                Array.Resize(ref linkConductance, size);
            }

            for (int i = syncedLinks; i < links.Count; i++)
            {
                ThermalLink link = links[i];
                linkA[i] = link.NodeA;
                linkB[i] = link.NodeB;
                linkConductance[i] = link.Conductance;
            }

            syncedLinks = links.Count;
        }

        /// <summary>
        /// Builds the links for blocks placed since the last build, and nothing else.
        ///
        /// A newly placed block has no links and nothing existing links to it, so every existing
        /// link, mirrored row and conductance total below the new ones remains valid and the work
        /// is the new block's six faces.
        ///
        /// Two placed blocks that touch each other each find the other as a neighbour, so the pair
        /// is added by the lower index and skipped by the higher — the same rule the full build
        /// uses, which is why both loops compare indices rather than track a visited set.
        /// </summary>
        private void LinkPendingNodes()
        {
            Work.TopologyRebuilds++;
            Work.TopologyNodeVisits += pendingLinkNodes.Count;

            bool buffersGrew = EnsureBuffers();

            IBlockAdjacency adjacency = Adjacency;
            int firstNewLink = links.Count;

            for (int p = 0; p < pendingLinkNodes.Count; p++)
            {
                ThermalNode a = pendingLinkNodes[p];

                // A node placed and removed again before any step ran.
                if (a.Index < 0 || a.Index >= nodes.Count || nodes[a.Index] != a) continue;

                neighbourScratch.Clear();
                adjacency.GetNeighbours(a.Block, neighbourScratch);

                for (int n = 0; n < neighbourScratch.Count; n++)
                {
                    ThermalNode b = GetNode(neighbourScratch[n]);
                    if (b == null) continue;

                    // Skip only when both ends are pending and the other has the lower index, so
                    // a pair of new neighbours is added once. A pre-existing neighbour is never
                    // pending, so its links are never skipped here.
                    if (b.PendingLinks && b.Index <= a.Index) continue;

                    int face = ConductionBuilder.ContactFace(a.Block, b.Block);
                    if (face < 0) continue;

                    int contacts = ConductionBuilder.CountContactFaces(a.Block, b.Block);
                    if (contacts <= 0) continue;

                    float conductance = ConductionBuilder.Conductance(
                        grid.GridSize, a.Block, b.Block, contacts, Face.Axis(face));
                    if (conductance <= 0f) continue;

                    links.Add(new ThermalLink(a.Index, b.Index, conductance, contacts));
                    ChainLink(links.Count - 1);
                    a.LinkCount++;
                    b.LinkCount++;
                }
            }

            for (int p = 0; p < pendingLinkNodes.Count; p++)
            {
                pendingLinkNodes[p].PendingLinks = false;
            }
            pendingLinkNodes.Clear();

            Work.LinksBuilt += links.Count - firstNewLink;

            SyncLinkArrays();

            // Growing the buffers zeroed the totals and marked them for recompute, so adding to
            // them here would be discarded.
            if (!buffersGrew) AddConductanceOfNewLinks(firstNewLink);

            // Marked rather than filled: a link's reduced mass derives from the mirrored node
            // masses, and the new node's row is not copied in until later in the same step.
            if (firstNewLink < linkMassFactorFrom) linkMassFactorFrom = firstNewLink;
        }

        /// <summary>
        /// Adds the conductance of newly built links to their endpoints' totals.
        ///
        /// The totals are the numerator of the substep estimate and also carry coolant loop and
        /// room air contributions, so adding is the correct incremental operation; recomputing
        /// would require walking every link, loop and room.
        /// </summary>
        private void AddConductanceOfNewLinks(int firstNewLink)
        {
            for (int i = firstNewLink; i < links.Count; i++)
            {
                ThermalLink link = links[i];
                nodeConductanceTotal[link.NodeA] += link.Conductance;
                nodeConductanceTotal[link.NodeB] += link.Conductance;
            }
        }

        /// <summary>
        /// Replaces the coolant loops, preserving the temperature of any loop whose signature
        /// survives the rebuild.
        /// </summary>
        /// <summary>
        /// Pours the coolant of every loop that is about to disappear into the pipe blocks that were
        /// carrying it.
        ///
        /// A loop survives a rebuild when its signature comes back, so only the ones with no successor
        /// spill. The heat goes to the ring's own pipes in proportion to their capacity — the metal
        /// the fluid was in contact with — and a pipe that was destroyed along with the ring simply
        /// is not there to take a share, which is right: that coolant left with the block.
        /// </summary>
        private void SpillDissolvedLoops(List<CoolantLoop> newLoops)
        {
            if (loops.Count == 0) return;

            for (int i = 0; i < loops.Count; i++)
            {
                CoolantLoop dying = loops[i];

                bool survives = false;
                if (newLoops != null)
                {
                    for (int n = 0; n < newLoops.Count; n++)
                    {
                        if (newLoops[n].Signature != dying.Signature) continue;
                        survives = true;
                        break;
                    }
                }
                if (survives) continue;

                // Each pipe takes the parcel that was inside it and comes to one temperature with it,
                // rather than the ring being averaged first. Local, exact, and it needs no decision
                // about where a destroyed pipe's coolant went — it went with the block.
                float segmentMass = dying.SegmentThermalMass;

                for (int p = 0; p < dying.Pipes.Count; p++)
                {
                    ThermalNode node = GetNode(dying.Pipes[p]);
                    if (node == null) continue;

                    float nodeMass = node.ThermalMass;
                    float combined = nodeMass + segmentMass;
                    if (combined <= 0f) continue;

                    float mixed = ((node.Temperature * nodeMass)
                                 + (dying.SegmentTemperature(p) * segmentMass)) / combined;

                    node.Temperature = mixed < ThermalConstants.MinimumTemperature
                        ? ThermalConstants.MinimumTemperature
                        : mixed;
                }
            }
        }

        public void SetLoops(List<CoolantLoop> newLoops)
        {
            Dictionary<long, float> previous = new Dictionary<long, float>();
            for (int i = 0; i < loops.Count; i++)
            {
                previous[loops[i].Signature] = loops[i].Temperature;
            }

            // Pump settings are the player's, so they survive any rebuild the layout provokes. Keyed
            // by block rather than by loop: splitting a ring in two must leave each pump where the
            // player left it.
            Dictionary<long, CoolantPump> previousPumps = new Dictionary<long, CoolantPump>();
            for (int i = 0; i < loops.Count; i++)
            {
                IList<CoolantPump> pumps = loops[i].Pumps;
                for (int p = 0; p < pumps.Count; p++)
                {
                    if (pumps[p].Block == null) continue;
                    previousPumps[pumps[p].Block.Key] = pumps[p];
                }
            }

            // Heat in a ring that is about to stop existing has to go somewhere. Breaking a ring —
            // grinding out a pipe, or the pump, which is a ring member itself — used to delete the
            // loop and silently delete every joule its coolant was holding with it: 190 MJ in one
            // measured case, and a ship close to overheating could dump heat on demand by grinding
            // its own pump and rebuilding the ring cold.
            SpillDissolvedLoops(newLoops);

            loops.Clear();
            if (newLoops != null)
            {
                for (int i = 0; i < newLoops.Count; i++)
                {
                    CoolantLoop loop = newLoops[i];
                    float carried;
                    if (previous.TryGetValue(loop.Signature, out carried))
                    {
                        loop.Temperature = carried;
                    }

                    // Coolant must run on the same clock as the blocks it exchanges with, so the
                    // solver imposes the scale rather than trusting the loop builder.
                    loop.HeatTimeScale = settings.HeatTimeScale;
                    loop.WellMixed = settings.WellMixedCoolant;

                    for (int p = 0; p < loop.Pumps.Count; p++)
                    {
                        CoolantPump fresh = loop.Pumps[p];
                        if (fresh.Block == null) continue;

                        CoolantPump kept;
                        if (!previousPumps.TryGetValue(fresh.Block.Key, out kept)) continue;

                        fresh.Speed = kept.Speed;
                        fresh.Enabled = kept.Enabled;
                        fresh.PowerAvailable = kept.PowerAvailable;
                        fresh.MaxPowerWatts = kept.MaxPowerWatts;
                    }

                    loop.RefreshFlow();
                    BuildLoopLinks(loop);
                    loops.Add(loop);
                }
            }

            EnsureBuffers();
            conductanceTotalsDirty = true;
        }

        private void BuildLoopLinks(CoolantLoop loop)
        {
            loop.Links.Clear();

            for (int i = 0; i < loop.Pipes.Count; i++)
            {
                BlockInstance pipe = loop.Pipes[i];

                ThermalNode pipeNode = GetNode(pipe);
                if (pipeNode != null)
                {
                    loop.Links.Add(new LoopLink(
                        pipeNode.Index,
                        CoolantLoopBuilder.PipeConductance(grid, pipe, loop.Properties),
                        i));
                }

                List<GridPort> sinks = pipe.CoolantSinkPorts();
                for (int s = 0; s < sinks.Count; s++)
                {
                    BlockInstance target = grid.GetAtCell(sinks[s].Target);
                    if (target == null || target == pipe) continue;

                    ThermalNode targetNode = GetNode(target);
                    if (targetNode == null) continue;

                    // Bound to the parcel inside this pipe, not to the ring: a sink face draws from
                    // the coolant actually touching it.
                    loop.Links.Add(new LoopLink(
                        targetNode.Index,
                        CoolantLoopBuilder.PlateConductance(grid, loop.Properties),
                        i));
                }
            }
        }

        // ---- exposure ----------------------------------------------------------------------

        /// <summary>
        /// Refreshes every node's exposed-face counts from the surface map and a room map.
        /// Call when a room mapping pass completes or the block layout changes.
        /// </summary>
        public void RefreshExposure(RoomMap rooms)
        {
            BeginExposureRefresh(rooms);
            while (StepExposureRefresh(int.MaxValue)) { }
        }

        /// <summary>Map the current exposure pass is reading, or null when no pass is running.</summary>
        private RoomMap exposureMap;
        private int exposureCursor;

        /// <summary>True while an exposure pass has nodes left to visit.</summary>
        public bool ExposureRefreshPending
        {
            get { return exposureMap != null; }
        }

        /// <summary>
        /// Starts a pass that recomputes every node's exposed faces against a room map.
        ///
        /// Resumable because the cost is proportional to the grid and would otherwise land on the
        /// single tick that publishes a completed room map, alongside that tick's room stage.
        /// Slicing leaves some nodes reading the previous map for a few ticks, which is the map
        /// they read for the whole time the room pass was building.
        /// </summary>
        public void BeginExposureRefresh(RoomMap rooms)
        {
            Work.ExposureRefreshes++;
            exposureMap = rooms;
            exposureCursor = 0;
        }

        /// <summary>
        /// Advances a pass by at most <paramref name="nodeBudget"/> nodes.
        /// </summary>
        /// <returns>True while the pass still has nodes left.</returns>
        public bool StepExposureRefresh(int nodeBudget)
        {
            if (exposureMap == null) return false;
            if (nodeBudget <= 0) return true;

            int end = exposureCursor + nodeBudget;
            if (end > nodes.Count) end = nodes.Count;

            Work.ExposureNodeVisits += end - exposureCursor;

            for (int i = exposureCursor; i < end; i++)
            {
                ThermalNode node = nodes[i];
                surfaces.GetExposedFaces(node.Block, exposureMap, exposureScratch);
                for (int f = 0; f < Face.Count; f++)
                {
                    node.ExposedFaces[f] = exposureScratch[f];
                }
                node.RefreshExposure();
            }

            exposureCursor = end;

            if (exposureCursor < nodes.Count) return true;

            exposureMap = null;
            exposureCursor = 0;
            return false;
        }

        /// <summary>
        /// Refreshes only the nodes with a face onto one of the given rooms.
        ///
        /// A door opening changes exposure for one room's walls and nothing else, so the affected
        /// set is derived from the rooms' own cells rather than by scanning every node.
        /// </summary>
        public void RefreshExposureAround(RoomMap rooms, IList<int> roomIndices)
        {
            if (rooms == null || roomIndices == null) return;

            if (affected == null) affected = new HashSet<BlockInstance>();
            affected.Clear();

            for (int r = 0; r < roomIndices.Count; r++)
            {
                int index = roomIndices[r];
                if (index < 0 || index >= rooms.Rooms.Count) continue;

                foreach (Vector3I cell in rooms.Rooms[index])
                {
                    for (int face = 0; face < Face.Count; face++)
                    {
                        BlockInstance block = grid.GetAtCell(cell + Face.Offsets[face]);
                        if (block != null) affected.Add(block);
                    }

                    // The cell itself may hold a block, such as a door standing in the room.
                    BlockInstance occupant = grid.GetAtCell(cell);
                    if (occupant != null) affected.Add(occupant);
                }
            }

            Work.ExposureRefreshes++;
            Work.ExposureNodeVisits += affected.Count;

            foreach (BlockInstance block in affected)
            {
                ThermalNode node = GetNode(block);
                if (node == null) continue;

                surfaces.GetExposedFaces(block, rooms, exposureScratch);
                for (int f = 0; f < Face.Count; f++)
                {
                    node.ExposedFaces[f] = exposureScratch[f];
                }
                node.RefreshExposure();
            }
        }

        /// <summary>Reused scratch set for <see cref="RefreshExposureAround"/>.</summary>
        private HashSet<BlockInstance> affected;

        // ---- room air ----------------------------------------------------------------------

        /// <summary>Air temperature and pressure of rooms seen before the last rebuild.</summary>
        private readonly Dictionary<Vector3I, RoomAirNode> rememberedAir =
            new Dictionary<Vector3I, RoomAirNode>(Vector3I.Comparer);

        private readonly Dictionary<int, int> roomContactScratch = new Dictionary<int, int>();

        /// <summary>
        /// Rebuilds the air masses of every sealed room from a room map.
        ///
        /// Air survives a rebuild when the room does: rooms are re-found identically when the map
        /// is remade for a change elsewhere, so matching on a room's lowest cell carries
        /// temperature and pressure across. A room whose shape changed is treated as a new room
        /// and starts at ambient with no air.
        /// </summary>
        public void RebuildRoomAir(RoomMap rooms)
        {
            Work.RoomAirRebuilds++;
            if (rooms != null) Work.RoomAirRoomVisits += rooms.Rooms.Count;

            rememberedAir.Clear();
            for (int i = 0; i < roomAir.Count; i++)
            {
                rememberedAir[roomAir[i].Anchor] = roomAir[i];
            }

            roomAir.Clear();

            if (settings.EnableRoomAir && rooms != null)
            {
                float cellVolume = grid.GridSize * grid.GridSize * grid.GridSize;

                for (int r = 0; r < rooms.Rooms.Count; r++)
                {
                    if (rooms.IsVented(r)) continue;

                    HashSet<Vector3I> cells = rooms.Rooms[r];
                    if (cells.Count == 0) continue;

                    RoomAirNode air = new RoomAirNode();
                    air.RoomIndex = r;
                    air.Anchor = LowestCell(cells);
                    air.CellCount = cells.Count;
                    air.Volume = cells.Count * cellVolume;

                    RoomAirNode previous;
                    if (rememberedAir.TryGetValue(air.Anchor, out previous))
                    {
                        air.Temperature = previous.Temperature;
                        air.Pressure = previous.Pressure;
                        air.Initialised = previous.Initialised;
                    }
                    else
                    {
                        air.Temperature = Environment.AmbientTemperature;
                        air.Pressure = 0f;
                    }

                    air.AirDensity = settings.RoomAirDensity;
                    air.HeatTimeScale = settings.HeatTimeScale;

                    roomAir.Add(air);
                    BuildRoomLinks(air, rooms);
                }
            }

            rememberedAir.Clear();
            EnsureBuffers();
            conductanceTotalsDirty = true;
        }

        /// <summary>
        /// Links one room's air to every block bounding it. Walks the room's own cells rather than
        /// the grid's blocks, so cost is proportional to the room rather than the ship.
        /// </summary>
        private void BuildRoomLinks(RoomAirNode air, RoomMap rooms)
        {
            air.Links.Clear();
            if (!air.HasAir) return;
            if (air.RoomIndex < 0 || air.RoomIndex >= rooms.Rooms.Count) return;

            roomContactScratch.Clear();

            foreach (Vector3I cell in rooms.Rooms[air.RoomIndex])
            {
                for (int face = 0; face < Face.Count; face++)
                {
                    BlockInstance block = grid.GetAtCell(cell + Face.Offsets[face]);
                    if (block == null) continue;

                    ThermalNode node = GetNode(block);
                    if (node == null) continue;

                    int faces;
                    roomContactScratch.TryGetValue(node.Index, out faces);
                    roomContactScratch[node.Index] = faces + 1;
                }
            }

            float surfaceSum = 0f;
            int surfaceCount = 0;

            foreach (KeyValuePair<int, int> contact in roomContactScratch)
            {
                surfaceSum += nodes[contact.Key].Temperature;
                surfaceCount++;

                float area = contact.Value * nodes[contact.Key].CellFaceArea;
                float conductance = settings.RoomConvectionCoefficient * area;
                if (conductance <= 0f) continue;

                air.Links.Add(new RoomLink(contact.Key, conductance));
            }

            // Air appearing in a room for the first time starts at the mean temperature of the
            // walls bounding it.
            if (!air.Initialised && surfaceCount > 0)
            {
                air.Temperature = surfaceSum / surfaceCount;
                air.Initialised = true;
            }
        }

        /// <summary>
        /// Restores saved air temperatures onto the rooms they were saved from, matched by anchor
        /// cell — the same key that carries air across a rebuild.
        ///
        /// Also marks the air initialised. A restored room is usually still at zero pressure here,
        /// since pressurisation comes from the later vent sweep; without the flag that sweep would
        /// overwrite the saved temperature with the average of the room's walls.
        /// </summary>
        /// <returns>Number of rooms that took a saved temperature.</returns>
        public int RestoreRoomAir(IList<StoredRoom> stored)
        {
            if (stored == null || stored.Count == 0 || roomAir.Count == 0) return 0;

            Dictionary<Vector3I, float> byAnchor = new Dictionary<Vector3I, float>(stored.Count, Vector3I.Comparer);
            for (int i = 0; i < stored.Count; i++)
            {
                byAnchor[stored[i].Anchor] = stored[i].Temperature;
            }

            int restored = 0;
            for (int i = 0; i < roomAir.Count; i++)
            {
                float temperature;
                if (!byAnchor.TryGetValue(roomAir[i].Anchor, out temperature)) continue;

                roomAir[i].Temperature = Math.Max(ThermalConstants.MinimumTemperature, temperature);
                roomAir[i].Initialised = true;
                restored++;
            }

            return restored;
        }

        private static Vector3I LowestCell(HashSet<Vector3I> cells)
        {
            bool first = true;
            Vector3I lowest = Vector3I.Zero;

            foreach (Vector3I cell in cells)
            {
                if (first)
                {
                    lowest = cell;
                    first = false;
                    continue;
                }

                if (cell.Z < lowest.Z
                    || (cell.Z == lowest.Z && cell.Y < lowest.Y)
                    || (cell.Z == lowest.Z && cell.Y == lowest.Y && cell.X < lowest.X))
                {
                    lowest = cell;
                }
            }

            return lowest;
        }

        // ---- heat pumps --------------------------------------------------------------------

        /// <summary>
        /// Rebuilds the grid's heat pumps from the blocks currently placed.
        ///
        /// A pump is bound to the nodes on either side of it, so any change to what is mounted on
        /// its faces requires a rebuild. Enabled state and power fraction are carried across by
        /// block key, since they belong to the block rather than to this table.
        /// </summary>
        public void RebuildHeatPumps()
        {
            Work.HeatPumpRebuilds++;

            // A grid with no pump blocks should not walk its blocks to establish that. The list
            // must be cleared before returning: grinding off the last pump is exactly the case
            // where the count reaches zero and the stale device must go with it.
            if (grid.HeatPumpBlockCount == 0)
            {
                heatPumps.Clear();
                return;
            }

            Work.HeatPumpNodeVisits += grid.Blocks.Count;

            Dictionary<long, HeatPumpDevice> previous = new Dictionary<long, HeatPumpDevice>();
            for (int i = 0; i < heatPumps.Count; i++)
            {
                if (heatPumps[i].Block == null) continue;
                previous[heatPumps[i].Block.Key] = heatPumps[i];
            }

            heatPumps.Clear();

            IList<BlockInstance> blocks = grid.Blocks;
            for (int i = 0; i < blocks.Count; i++)
            {
                BlockInstance block = blocks[i];
                HeatPumpShape shape = block.Model.HeatPump;
                if (shape == null) continue;

                Vector3I coldCell, hotCell;
                if (!block.TryHeatPumpCells(out coldCell, out hotCell)) continue;

                HeatPumpDevice device = new HeatPumpDevice();
                device.Block = block;
                device.RatedWatts = shape.RatedWatts;
                device.MaxPowerWatts = shape.MaxPowerWatts;

                HeatPumpDevice carried;
                if (previous.TryGetValue(block.Key, out carried))
                {
                    device.Enabled = carried.Enabled;
                    device.PowerAvailable = carried.PowerAvailable;
                }

                device.ColdNodeIndex = NodeIndexAt(coldCell);
                device.HotNodeIndex = NodeIndexAt(hotCell);

                heatPumps.Add(device);
            }
        }

        /// <summary>Index of the node occupying a cell, or -1 when the cell is empty.</summary>
        private int NodeIndexAt(Vector3I cell)
        {
            BlockInstance block = grid.GetAtCell(cell);
            if (block == null) return -1;

            ThermalNode node = GetNode(block);
            return node == null ? -1 : node.Index;
        }

        /// <summary>The heat pump bound to a block, or null when the block is not a pump.</summary>
        public HeatPumpDevice GetHeatPump(BlockInstance block)
        {
            if (block == null) return null;

            for (int i = 0; i < heatPumps.Count; i++)
            {
                if (heatPumps[i].Block == block) return heatPumps[i];
            }
            return null;
        }

        /// <summary>
        /// Moves heat from each pump's cold side to its hot side, charging the electrical work to
        /// the hot side as well.
        ///
        /// Three limits apply in turn: the Carnot coefficient of performance sets the electrical
        /// cost per watt lifted, the pump's rating caps the electrical draw, and the cold node's
        /// remaining heat caps what can be taken. Across a small temperature difference the rating
        /// binds; across a large one the Carnot cost binds, which is what makes absolute zero
        /// unreachable.
        /// </summary>
        private void AccumulateHeatPumps(float h)
        {
            if (!settings.EnableHeatPumps) return;

            float fraction = settings.HeatPumpCarnotFraction;
            float ceiling = settings.HeatPumpMaxCoefficient;

            for (int p = 0; p < heatPumps.Count; p++)
            {
                HeatPumpDevice pump = heatPumps[p];
                if (!pump.Enabled || !pump.IsConnected) continue;

                int cold = pump.ColdNodeIndex;
                int hot = pump.HotNodeIndex;
                if (cold >= nodes.Count || hot >= nodes.Count) continue;

                float coldTemperature = nodeTemperatures[cold];
                float hotTemperature = nodeTemperatures[hot];

                float coefficient = HeatPumpDevice.Coefficient(
                    coldTemperature, hotTemperature, fraction, ceiling);
                if (coefficient <= 0f) continue;

                // Never take more heat from the cold node than it holds above absolute zero.
                float headroom = (coldTemperature - ThermalConstants.MinimumTemperature)
                    * nodeThermalMass[cold] / h;
                if (headroom <= 0f) continue;

                // Demand is recorded at the setting the player chose regardless of what the grid
                // supplied, so a request never shrinks merely because it was refused.
                float settable = pump.SettablePowerWatts;

                // How far the gap is from the widest one this pump could still saturate at. Recorded
                // before the early exits so a throttled or starved pump still reports what its
                // conditions would allow.
                pump.LastOptimalMarginKelvin = pump.RatedWatts > 0f
                    ? ((fraction * coldTemperature * settable) / pump.RatedWatts)
                        - (hotTemperature - coldTemperature)
                    : 0f;

                if (settable <= 0f) continue;

                float wanted = Limit(coefficient * settable, pump.RatedWatts, headroom);
                pump.DemandEnergy += (wanted / coefficient) * h;

                float available = settable * Clamp01(pump.PowerAvailable);
                if (available <= 0f) continue;

                // The smallest of: what the available power can pay for, the pump's rating, and
                // the heat remaining in the cold node.
                float lift = Limit(coefficient * available, pump.RatedWatts, headroom);
                float work = lift / coefficient;

                nodeWatts[cold] -= lift;
                nodeWatts[hot] += lift + work;

                pump.LiftedEnergy += lift * h;
                pump.PowerEnergy += work * h;
                pump.RejectedEnergy += (lift + work) * h;
            }
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        /// <summary>Smallest of the three limits on what a pump may move this substep.</summary>
        private static float Limit(float watts, float rating, float headroom)
        {
            if (watts > rating) watts = rating;
            if (watts > headroom) watts = headroom;
            return watts;
        }

        /// <summary>Air node of the room containing a cell, or null when that room holds no air.</summary>
        public RoomAirNode GetRoomAir(RoomMap rooms, Vector3I cell)
        {
            if (rooms == null) return null;

            int room = rooms.RoomIndexOf(cell);
            if (room < 0) return null;

            for (int i = 0; i < roomAir.Count; i++)
            {
                if (roomAir[i].RoomIndex == room) return roomAir[i];
            }
            return null;
        }

        /// <summary>
        /// Sets a room's air fill fraction, 0..1. Owned by the host, which is the only source of
        /// pressurisation state. Crossing between zero and non-zero rebuilds the room's links,
        /// since a room at zero pressure has none.
        /// </summary>
        /// <returns>True when a room took the value.</returns>
        public bool SetRoomPressure(RoomMap rooms, Vector3I cell, float pressure)
        {
            RoomAirNode air = GetRoomAir(rooms, cell);
            if (air == null) return false;

            if (pressure < 0f) pressure = 0f;
            if (pressure > 1f) pressure = 1f;
            if (air.Pressure == pressure) return true;

            bool hadAir = air.HasAir;
            air.Pressure = pressure;
            air.RefreshThermalMass();

            if (hadAir != air.HasAir)
            {
                BuildRoomLinks(air, rooms);
                conductanceTotalsDirty = true;
            }

            return true;
        }

        /// <summary>Recomputes waste-heat generation for every node.</summary>
        public void RefreshHeatGeneration()
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i].RefreshHeatGeneration();
            }
        }

        // ---- integration -------------------------------------------------------------------

        /// <summary>
        /// Copies the node state the substep loop needs into flat arrays. Every value here is
        /// constant across a step: it changes when a block is built, damaged, exposed or
        /// re-powered, never between substeps.
        /// </summary>
        private void SyncNodeState()
        {
            Work.NodeStateSyncs++;

            // A node index changes when the block list does, which invalidates every row.
            bool all = resyncAll;
            resyncAll = false;

            for (int i = 0; i < nodes.Count; i++)
            {
                ThermalNode node = nodes[i];

                // Temperature is the one value the host can change from outside a step (loading a
                // save, a grid split, conduction across a rotor), so it is always re-read.
                float temperature = node.Temperature;
                nodeTemperatures[i] = temperature;
                nodeStepStart[i] = temperature;

                if (!all && !node.StateDirty) continue;
                node.StateDirty = false;

                // Only a mass change invalidates the cached reduced masses. Exposure and heat
                // generation changes leave every link factor on the grid correct.
                if (nodeThermalMass[i] != node.ThermalMass) linkMassFactorFrom = 0;

                nodeThermalMass[i] = node.ThermalMass;
                nodeRadiation[i] = node.RadiationCoefficient;
                nodeGeneration[i] = node.HeatGenerationWatts;
                nodeExposedArea[i] = node.ExposedArea;
                nodeEmissivity[i] = node.Thermal.Emissivity;
                nodeCritical[i] = node.Thermal.CriticalTemperature;

                int total = node.TotalExposedFaces;
                nodeExposedFaces[i] = total;

                int b = i * Face.Count;
                if (total <= 0)
                {
                    for (int f = 0; f < Face.Count; f++) nodeFaceWeights[b + f] = 0f;
                    continue;
                }

                float inverse = 1f / total;
                for (int f = 0; f < Face.Count; f++)
                {
                    nodeFaceWeights[b + f] = node.ExposedFaces[f] * inverse;
                }
            }
        }

        /// <summary>
        /// How close to its stability limit an element must be before the overshoot clamp is
        /// treated as live.
        ///
        /// The clamp binds when <c>h * G &gt;= C</c>. This margin asks for the strict inequality
        /// with a hundred parts per million of headroom, which is three orders of magnitude above
        /// the rounding in the single-precision comparison the clamp itself makes. Below the
        /// margin, the clamped and unclamped loops cannot produce different floats.
        /// </summary>
        private const float ClampBindingMargin = 0.9999f;

        /// <summary>
        /// Whether the conduction overshoot clamp can change any exchange this substep.
        ///
        /// <para>
        /// The clamp has two halves and both are the same stability test. A node under-relaxes only
        /// when <c>h * G &gt; C</c> for that node; a link's exchange is capped only when
        /// <c>h * conductance &gt; </c> its reduced mass. Neither depends on a temperature, so both
        /// can be settled once for the whole grid before the substeps run.
        /// </para>
        ///
        /// <para>
        /// On a grid granted the substeps it demands, neither holds anywhere — which is what the
        /// substep count is chosen to guarantee — and every clamped branch in the conduction loop is
        /// arithmetic whose result is discarded. That is not a small share of the pass: switching
        /// the clamp off measured 4.17 ms of an 8.52 ms step on a 32,000-block ship, against
        /// 5.36 ms for conduction itself.
        /// </para>
        ///
        /// <para>
        /// One pass over the nodes and links per step, against the clamp's cost over every element
        /// on every substep of it. It returns on the first element that can bind, so a stiff grid —
        /// the case where the answer is yes and nothing is saved — pays almost nothing to find out.
        /// </para>
        /// </summary>
        private bool ClampCanBind(float h)
        {
            if (h <= 0f) return false;

            int nodeCount = nodes.Count;
            if (nodeCount > nodeConductanceTotal.Length) nodeCount = nodeConductanceTotal.Length;
            if (nodeCount > nodeThermalMass.Length) nodeCount = nodeThermalMass.Length;

            for (int i = 0; i < nodeCount; i++)
            {
                if (h * nodeConductanceTotal[i] >= ClampBindingMargin * nodeThermalMass[i]) return true;
            }

            int linkCount = links.Count;
            if (linkCount > linkConductance.Length) linkCount = linkConductance.Length;
            if (linkCount > linkMassFactor.Length) linkCount = linkMassFactor.Length;

            for (int i = 0; i < linkCount; i++)
            {
                if (h * linkConductance[i] >= ClampBindingMargin * linkMassFactor[i]) return true;
            }

            return false;
        }

        /// <summary>Recomputes the cached per-link reduced mass from the first stale row on.</summary>
        private void RefreshLinkMassFactors()
        {
            int from = linkMassFactorFrom;
            if (from >= links.Count) return;
            linkMassFactorFrom = int.MaxValue;

            if (linkMassFactor.Length < links.Count)
            {
                Array.Resize(ref linkMassFactor, Math.Max(16, links.Count * 2));
            }

            for (int i = from; i < links.Count; i++)
            {
                float massA = nodeThermalMass[links[i].NodeA];
                float massB = nodeThermalMass[links[i].NodeB];
                float combined = massA + massB;
                linkMassFactor[i] = combined <= 0f ? 0f : (massA * massB) / combined;
            }
        }

        /// <summary>
        /// Resolves a direction into the six per-face weights the exposure maths multiplies by.
        /// Done once per step for the whole grid, rather than six dot products per node.
        /// </summary>
        private static void ResolveDirection(ref Vector3 direction, float[] weights)
        {
            for (int f = 0; f < Face.Count; f++)
            {
                float dot = Vector3.Dot(Face.Normals[f], direction);
                weights[f] = dot > 0f ? dot : 0f;
            }
        }

        /// <summary>
        /// The parts of an environment pass that are identical for every node: which mechanisms are
        /// enabled, the scalars they need, and the sun and wind directions resolved to per-face
        /// weights.
        ///
        /// Held separately from the per-node work because a step is spread across the frames of its
        /// simulation window, so the node loop is entered many times per substep. This must be
        /// computed once per substep rather than once per slice, both for cost and because
        /// <see cref="RefreshSunShadow"/> advances a budget that would otherwise run too fast.
        /// </summary>
        private struct EnvironmentPlan
        {
            public bool EnvironmentEnabled;
            public bool Radiating;
            public bool Convecting;
            public bool Windy;
            public bool SolarEnabled;
            public bool FrictionEnabled;
            public bool SourcesEnabled;
            public bool Generating;
            public bool Diagnostics;

            /// <summary>True when waste heat is the only active contribution.</summary>
            public bool GenerationOnly;

            public float RadiationShare;
            public float FrictionScale;
        }

        /// <summary>
        /// Fraction of its exchanges a node may take over a substep of <paramref name="h"/>.
        ///
        /// A node is stable over a substep when <c>h * conductance &lt;= mass</c>, the condition
        /// the substep estimate is derived from. Where it holds this returns one. Where it does
        /// not, the ratio under-relaxes the node so the substep behaves as if it were short enough.
        /// </summary>
        private float RelaxationFactor(int node, float h)
        {
            // Not just the setting: when the step's substeps are short enough that no node can
            // overshoot, every answer below is one, and the divide that proves it is a divide per
            // node per step. ClampCanBind has already established that with a margin.
            if (!ConductionClampLive || h <= 0f) return 1f;

            float conductance = nodeConductanceTotal[node];
            if (conductance <= 0f) return 1f;

            float mass = nodeThermalMass[node];
            if (mass <= 0f) return 1f;

            float stable = mass / (h * conductance);
            return stable >= 1f ? 1f : stable;
        }

        private EnvironmentPlan PlanEnvironment(ref EnvironmentState env)
        {
            EnvironmentPlan plan = new EnvironmentPlan();

            plan.Radiating = settings.EnableEnvironment && settings.EnableRadiation;
            plan.EnvironmentEnabled = plan.Radiating;
            plan.SolarEnabled = settings.EnableSolarHeat && !env.IsSolarOccluded && env.SolarEnergy > 0f;
            plan.SourcesEnabled = settings.EnableHeatSources && env.HeatSourceCount > 0;
            plan.FrictionEnabled = env.FrictionActive;
            plan.Generating = settings.EnableWasteHeat;
            plan.Diagnostics = diagnosticsSubstep;

            plan.Convecting = settings.EnableEnvironment && settings.EnableConvection
                && env.AtmosphereFactor > 0f && env.ConvectionCoefficient > 0f;

            if (plan.Convecting) plan.EnvironmentEnabled = true;

            plan.GenerationOnly = !plan.EnvironmentEnabled && !plan.SolarEnabled
                && !plan.FrictionEnabled && !plan.SourcesEnabled;

            if (plan.GenerationOnly) return plan;

            plan.Windy = plan.Convecting && env.WindSpeed > 0f;
            plan.RadiationShare = 1f - env.AtmosphereFactor;

            float airCubed = env.WindSpeed * env.WindSpeed * env.WindSpeed;
            plan.FrictionScale = settings.FrictionScale * env.AirDensity * airCubed;

            // The two directions a node can be weighted against are the same for the whole grid,
            // so they are resolved once here rather than per node.
            if (plan.Windy || plan.FrictionEnabled)
            {
                Vector3 wind = env.WindDirectionLocal;
                ResolveDirection(ref wind, windWeights);
            }

            if (plan.SolarEnabled)
            {
                Vector3 sun = env.SunDirectionLocal;
                ResolveDirection(ref sun, sunWeights);

                // The one input to the precomputed rows that can change part way through a step.
                if (RefreshSunShadow(ref sun)) environmentRowsValid = false;
            }

            return plan;
        }

        /// <summary>Runs the environment pass over nodes <paramref name="from"/> to <paramref name="to"/>.</summary>
        private void AccumulateEnvironmentRange(ref EnvironmentState env, ref EnvironmentPlan plan,
            float h, int from, int to)
        {
            bool diagnostics = plan.Diagnostics;

            if (plan.GenerationOnly)
            {
                // Only the clamped conduction loop reads the relaxation row, and only while the
                // clamp is live. Filling it otherwise writes a one per node per step that nothing
                // will look at.
                if (ConductionClampLive && (!environmentRowsValid || !PrecomputeEnvironment))
                {
                    for (int i = from; i < to; i++)
                    {
                        nodeRelaxation[i] = RelaxationFactor(i, h);
                    }
                }

                if (plan.Generating)
                {
                    float generated = 0f;
                    for (int i = from; i < to; i++)
                    {
                        float generation = nodeGeneration[i];
                        nodeWatts[i] += generation;
                        generated += generation;
                    }
                    heatGainAccumulator += generated;
                }
                if (diagnostics)
                {
                    for (int i = from; i < to; i++) ClearEnvironmentDiagnostics(i);
                }

                if (PrecomputeEnvironment && to >= nodes.Count) environmentRowsValid = true;
                return;
            }

            bool generating = plan.Generating;
            bool clampRelaxation = settings.ClampEnvironmentOvershoot && h > 0f;
            float inverseH = h > 0f ? 1f / h : 0f;
            bool environmentEnabled = plan.EnvironmentEnabled;
            bool radiating = plan.Radiating;
            bool convecting = plan.Convecting;
            bool windy = plan.Windy;
            bool solarEnabled = plan.SolarEnabled;
            bool frictionEnabled = plan.FrictionEnabled;
            float radiationShare = plan.RadiationShare;
            float frictionScale = plan.FrictionScale;

            // The first substep of a step fills the rows below; later substeps read them. Their
            // inputs (exposure, area, emissivity, sun and wind directions, step length) are fixed
            // for the whole step, so both paths compute the same values.
            bool fill = !environmentRowsValid || !PrecomputeEnvironment;

            // Computed for every node, exposed or buried, because the clamped conduction loop
            // reads it — and for none of them when that loop is not clamping, which is every step
            // on a grid granted the substeps it asked for.
            bool fillRelaxation = fill && ConductionClampLive;

            for (int i = from; i < to; i++)
            {
                // Folded into this loop rather than given a pass of its own: this loop already
                // walks every node once per substep, and the conduction pass runs strictly after
                // it.
                if (fillRelaxation) nodeRelaxation[i] = RelaxationFactor(i, h);

                if (nodeExposedFaces[i] <= 0)
                {
                    if (fill)
                    {
                        nodeSolarRow[i] = 0f;
                        nodeFrictionRow[i] = 0f;
                        nodeConvectionRow[i] = 0f;
                        nodeSourceRow[i] = generating ? nodeGeneration[i] : 0f;
                    }

                    float buried = nodeSourceRow[i];
                    nodeWatts[i] += buried;
                    heatGainAccumulator += buried;
                    if (diagnostics) ClearEnvironmentDiagnostics(i);
                    continue;
                }

                float temperature = nodeTemperatures[i];

                float radiationWatts = 0f;
                float convectionWatts = 0f;

                if (fill)
                {
                    float area = nodeExposedArea[i];

                    // One weighting against the wind, read by both the terms that want it. The
                    // convection factor and the friction row asked for the same six-face sum
                    // separately, and in air at speed both of them are live.
                    float wind = windy || frictionEnabled ? Weighted(i, windWeights) : 0f;

                    // A face in the airflow sheds more heat, but still air convects as well, so
                    // the factor spans 0.5..1. It depends on geometry and wind, not temperature.
                    float windFactor = windy ? 0.5f + (0.5f * wind) : 1f;

                    nodeConvectionRow[i] = convecting
                        ? -env.ConvectionCoefficient * area * windFactor
                        : 0f;

                    // Per face, weighted by both incidence against the sun and the fraction of
                    // the face the grid's own shadow leaves lit.
                    float solar = solarEnabled
                        ? env.SolarEnergy * nodeEmissivity[i] * WeightedLit(i, sunWeights) * area
                        : 0f;
                    nodeSolarRow[i] = solar;

                    float friction = frictionEnabled ? frictionScale * area * wind : 0f;
                    nodeFrictionRow[i] = friction;

                    nodeSourceRow[i] = (generating ? nodeGeneration[i] : 0f) + solar + friction;
                }

                float watts = nodeSourceRow[i];

                if (environmentEnabled)
                {
                    float radiation = 0f;
                    if (radiating)
                    {
                        float squared = temperature * temperature;
                        radiation = -nodeRadiation[i] * ((squared * squared) - env.AmbientTemperaturePow4);
                    }

                    float convection = 0f;
                    if (convecting)
                    {
                        convection = nodeConvectionRow[i] * (temperature - env.AmbientTemperature);
                    }

                    radiationWatts = radiationShare * radiation;
                    convectionWatts = env.AtmosphereFactor * convection;

                    float relaxation = radiationWatts + convectionWatts;

                    if (clampRelaxation)
                    {
                        // Radiation and convection both drive the node towards ambient, so the
                        // most either may do in one substep is reach it; beyond that the exchange
                        // would have reversed. Capping there bounds the explicit step when the
                        // requested substep count was refused. Solar, friction and waste heat are
                        // sources rather than relaxation and are excluded.
                        float capped = ClampRelaxation(relaxation,
                            (env.AmbientTemperature - temperature) * nodeThermalMass[i] * inverseH);

                        if (capped != relaxation)
                        {
                            // The per-mechanism figures report what was applied, so they are
                            // scaled to match the clamp rather than left at the unclamped values.
                            float scale = relaxation == 0f ? 0f : capped / relaxation;
                            radiationWatts *= scale;
                            convectionWatts *= scale;
                            relaxation = capped;
                        }
                    }

                    watts += relaxation;
                }

                nodeWatts[i] += watts;

                // Two adds against a pass that has already computed all four figures. The
                // environment half is signed, so a grid absorbing more than it sheds reads
                // positive and the venting figure derived from it reads zero.
                environmentWattsAccumulator += radiationWatts + convectionWatts;
                heatGainAccumulator += nodeSourceRow[i];

                if (!diagnostics) continue;

                ThermalNode node = nodes[i];
                node.LastRadiationWatts = radiationWatts;
                node.LastConvectionWatts = convectionWatts;
                node.LastSolarWatts = nodeSolarRow[i];
                node.LastFrictionWatts = nodeFrictionRow[i];
                node.LastHeatSourceWatts = 0f;
            }

            // Only once the whole grid has been covered: the pass is sliced across frames, and a
            // partly filled row must not be read by a later substep. Counted here rather than at
            // the top of the loop for the same reason — a fill that spans three frames is one
            // fill, not three.
            if (fill && to >= nodes.Count) Work.EnvironmentRowFills++;
            if (PrecomputeEnvironment && to >= nodes.Count) environmentRowsValid = true;
        }

        private void AccumulateHeatSources(ref EnvironmentState env, bool diagnostics)
        {
            for (int s = 0; s < env.HeatSourceCount; s++)
            {
                HeatSourceState source = env.HeatSources[s];
                if (source.Irradiance <= 0f) continue;

                Vector3 direction = source.DirectionLocal;
                ResolveDirection(ref direction, sourceWeights);

                for (int i = 0; i < nodes.Count; i++)
                {
                    if (nodeExposedFaces[i] <= 0) continue;

                    float watts = source.Irradiance * nodeEmissivity[i]
                        * Weighted(i, sourceWeights) * nodeExposedArea[i];
                    if (watts == 0f) continue;

                    nodeWatts[i] += watts;
                    heatGainAccumulator += watts;
                    if (diagnostics) nodes[i].LastHeatSourceWatts += watts;
                }
            }
        }

        /// <summary>
        /// Keeps the grid's self-shadow current, and the per-node lit fractions with it.
        ///
        /// Both rebuild on the same trigger, since both depend on the sun direction and the grid
        /// layout. Between triggers the call costs one dot product.
        /// </summary>
        /// <returns>
        /// True when the lit fractions moved, which invalidates the precomputed solar row.
        /// </returns>
        private bool RefreshSunShadow(ref Vector3 sunLocal)
        {
            if (!settings.SolarSelfShadowing)
            {
                if (sunShadow.IsBuilt || sunShadow.IsRunning || sunLitDirty)
                {
                    sunShadow.Clear();
                    BeginSunLit(1f);
                    sunLitDirty = false;
                }

                return StepSunLit(SunLitBudget);
            }

            // A block added or removed invalidates a pass in flight as well as its result, since
            // the walk reads the grid it started against.
            if (sunLitDirty || sunShadow.NeedsRestart(ref sunLocal, SunRebuildCosine))
            {
                sunShadow.Restart(grid, sunLocal, SunOccluders);
                sunLitDirty = false;
            }

            // Only a completed pass changes any result, so lit fractions are refreshed when one
            // completes and left alone otherwise.
            if (sunShadow.Step(SunShadowBudget)) BeginSunLit(-1f);

            // Sliced here rather than at the top of the step, so a grid small enough for one
            // budget completes inside the same substep that completed the shadow pass.
            return StepSunLit(SunLitBudget);
        }

        /// <summary>Starts a lit-fraction refresh, from the shadow map or from a fixed value.</summary>
        private void BeginSunLit(float fill)
        {
            sunLitFill = fill;
            sunLitCursor = 0;
            sunLitPending = true;
        }

        /// <summary>
        /// Advances a lit-fraction refresh by at most <paramref name="nodeBudget"/> nodes.
        /// </summary>
        /// <returns>True when it wrote any lit fraction.</returns>
        private bool StepSunLit(int nodeBudget)
        {
            if (!sunLitPending) return false;

            if (sunLitCursor >= nodes.Count)
            {
                sunLitPending = false;
                return false;
            }

            int end = sunLitCursor + nodeBudget;
            if (end > nodes.Count) end = nodes.Count;

            if (sunLitFill >= 0f)
            {
                for (int i = sunLitCursor; i < end; i++)
                {
                    int b = i * Face.Count;
                    for (int f = 0; f < Face.Count; f++) nodeSunLit[b + f] = sunLitFill;
                }
            }
            else
            {
                for (int i = sunLitCursor; i < end; i++)
                {
                    int b = i * Face.Count;
                    for (int f = 0; f < Face.Count; f++)
                    {
                        nodeSunLit[b + f] = sunShadow.FaceLitFraction(nodes[i].Block, f);
                    }
                }
            }

            sunLitCursor = end;
            if (sunLitCursor >= nodes.Count) sunLitPending = false;
            return true;
        }

        /// <summary>Runs a pending lit-fraction refresh to completion. For tests and one-shot rebuilds.</summary>
        public void FinishSunLit()
        {
            while (sunLitPending) StepSunLit(int.MaxValue);
        }

        /// <summary>True while a lit-fraction refresh has nodes left to visit.</summary>
        public bool SunLitRefreshPending
        {
            get { return sunLitPending; }
        }

        /// <summary>
        /// The grid's self-shadow, for anything that wants to draw it. Empty when the setting is
        /// off or the sun has never been resolved.
        /// </summary>
        public SunShadowMap SunShadow { get { return sunShadow; } }

        /// <summary>Fraction of one face of a node the sun reaches, 0..1.</summary>
        public float SunLitFraction(int node, int face)
        {
            int index = (node * Face.Count) + face;
            return index >= 0 && index < nodeSunLit.Length ? nodeSunLit[index] : 1f;
        }

        /// <summary>
        /// <see cref="Weighted"/> with each face's share scaled by the fraction of that face the
        /// sun reaches.
        /// </summary>
        private float WeightedLit(int node, float[] weights)
        {
            int b = node * Face.Count;
            return (nodeFaceWeights[b] * nodeSunLit[b] * weights[0])
                + (nodeFaceWeights[b + 1] * nodeSunLit[b + 1] * weights[1])
                + (nodeFaceWeights[b + 2] * nodeSunLit[b + 2] * weights[2])
                + (nodeFaceWeights[b + 3] * nodeSunLit[b + 3] * weights[3])
                + (nodeFaceWeights[b + 4] * nodeSunLit[b + 4] * weights[4])
                + (nodeFaceWeights[b + 5] * nodeSunLit[b + 5] * weights[5]);
        }

        /// <summary>
        /// A direction's intensity on this node: the per-face weights of a resolved direction,
        /// each scaled by that face's share of the node's exposed area.
        /// </summary>
        private float Weighted(int node, float[] weights)
        {
            int b = node * Face.Count;
            return (nodeFaceWeights[b] * weights[0])
                + (nodeFaceWeights[b + 1] * weights[1])
                + (nodeFaceWeights[b + 2] * weights[2])
                + (nodeFaceWeights[b + 3] * weights[3])
                + (nodeFaceWeights[b + 4] * weights[4])
                + (nodeFaceWeights[b + 5] * weights[5]);
        }

        private void ClearEnvironmentDiagnostics()
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                ClearEnvironmentDiagnostics(i);
            }
        }

        private void ClearEnvironmentDiagnostics(int i)
        {
            ThermalNode node = nodes[i];
            node.LastRadiationWatts = 0f;
            node.LastConvectionWatts = 0f;
            node.LastSolarWatts = 0f;
            node.LastFrictionWatts = 0f;
            node.LastHeatSourceWatts = 0f;
        }

        /// <summary>
        /// Zeroes the per-node conduction diagnostic before a substep accumulates into it.
        ///
        /// Kept out of the pass itself: the pass is entered once per slice, and clearing inside it
        /// would discard what earlier slices added and leave the readout showing only the last.
        /// </summary>
        private void ClearConductionDiagnostics()
        {
            if (!diagnosticsSubstep) return;

            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i].LastConductionWatts = 0f;
            }
        }

        /// <summary>Runs the conduction pass over links <paramref name="from"/> to <paramref name="to"/>.</summary>
        private void AccumulateConductionRange(float h, int from, int to)
        {
            bool clamp = ConductionClampLive;
            bool diagnostics = diagnosticsSubstep;

            if (!settings.EnableConduction) return;

            float inverseH = h > 0f ? 1f / h : 0f;

            // Hoisted so the loop reads locals rather than fields and the array lengths are
            // visibly loop-invariant.
            if (to > links.Count) to = links.Count;
            int[] fromIndex = linkA;
            int[] toIndex = linkB;
            float[] conductance = linkConductance;
            float[] massFactor = linkMassFactor;
            float[] temperatures = nodeTemperatures;
            float[] watts = nodeWatts;
            float[] relaxation = nodeRelaxation;

            for (int i = from; i < to; i++)
            {
                int a = fromIndex[i];
                int b = toIndex[i];

                float difference = temperatures[b] - temperatures[a];
                if (difference == 0f) continue;

                // One conductance applied equally and oppositely: what leaves A enters B.
                float exchange = conductance[i] * difference;

                if (clamp)
                {
                    // The stricter of the two ends. Taking the smaller keeps the exchange equal
                    // and opposite; scaling each node's own total instead would bound the node but
                    // stop conserving energy.
                    float scale = relaxation[a] < relaxation[b] ? relaxation[a] : relaxation[b];
                    if (scale < 1f) exchange *= scale;

                    // Cap the exchange at the energy that brings the pair to equilibrium.
                    float maxWatts = difference * massFactor[i] * inverseH;
                    if (exchange > 0f)
                    {
                        if (exchange > maxWatts) exchange = maxWatts;
                    }
                    else if (exchange < maxWatts)
                    {
                        exchange = maxWatts;
                    }
                }

                watts[a] += exchange;
                watts[b] -= exchange;

                if (!diagnostics) continue;

                nodes[a].LastConductionWatts += exchange;
                nodes[b].LastConductionWatts -= exchange;
            }
        }

        private void AccumulateLoops(float h)
        {
            if (!settings.EnableCoolantLoops) return;

            bool clamp = settings.ClampConductionOvershoot;

            for (int l = 0; l < loops.Count; l++)
            {
                CoolantLoop loop = loops[l];
                float[] watts = loop.SegmentWatts;

                // How hard every link on a parcel pulls, together.
                //
                // ClampExchange bounds one exchange to the energy that would equalise that pair,
                // which is right for a pair and wrong for a parcel with more than one link on it:
                // two links each allowed to equalise deliver twice the energy that equalising
                // takes, the parcel overshoots past its neighbours, and the overshoot grows. A
                // pipe carrying a sink face has exactly that shape — its own link plus the sink's
                // — and a well-mixed ring has one parcel carrying every link in the ring.
                //
                // Below the clamp threshold this changes nothing, which is why it went unnoticed:
                // it only bites once the exchanges are large enough to saturate, and conductance
                // is what decides that. Brass stayed under it and copper did not.
                float relaxation = 1f;
                if (clamp && h > 0f)
                {
                    if (parcelConductanceTotal.Length < loop.PipeCount)
                    {
                        parcelConductanceTotal = new float[loop.PipeCount];
                    }
                    for (int i = 0; i < loop.PipeCount; i++) parcelConductanceTotal[i] = 0f;

                    for (int i = 0; i < loop.Links.Count; i++)
                    {
                        LoopLink probe = loop.Links[i];
                        if (probe.SegmentIndex < 0 || probe.SegmentIndex >= loop.PipeCount) continue;
                        parcelConductanceTotal[loop.ParcelOf(probe.SegmentIndex)] += probe.Conductance;
                    }

                    // The worst parcel sets the factor for the ring: a per-link factor would let a
                    // lightly loaded parcel run ahead of a saturated one and reintroduce the same
                    // imbalance between parcels instead of within one.
                    float mass = loop.SegmentThermalMass;
                    for (int i = 0; i < loop.PipeCount; i++)
                    {
                        float total = parcelConductanceTotal[i];
                        if (total <= 0f || mass <= 0f) continue;

                        float stable = mass / (h * total);
                        if (stable < relaxation) relaxation = stable;
                    }
                }

                for (int i = 0; i < loop.Links.Count; i++)
                {
                    LoopLink link = loop.Links[i];
                    if (link.NodeIndex < 0 || link.NodeIndex >= nodes.Count) continue;
                    if (link.SegmentIndex < 0 || link.SegmentIndex >= loop.PipeCount) continue;

                    // Pipe to parcel: which coolant is in this pipe right now. Eight pipes resolve to
                    // one parcel when the ring is well mixed, which is the whole of that model.
                    int parcel = loop.ParcelOf(link.SegmentIndex);
                    if (parcel >= watts.Length) continue;

                    // The parcel's own temperature, not the ring's mean. This is what makes a stopped
                    // pump behave like a stopped pump: the coolant beside a reactor saturates and
                    // stops drawing, while the coolant at a radiator never learns the reactor is hot.
                    float difference = loop.SegmentTemperature(link.SegmentIndex)
                                     - nodeTemperatures[link.NodeIndex];
                    float exchange = link.Conductance * difference;

                    if (clamp)
                    {
                        exchange = ClampExchange(
                            exchange, h, difference,
                            loop.SegmentThermalMass,
                            nodeThermalMass[link.NodeIndex]);

                        // Then the ring's own limit, which the pairwise bound cannot see.
                        exchange *= relaxation;
                    }

                    nodeWatts[link.NodeIndex] += exchange;
                    watts[parcel] -= exchange;

                    // Signed by which way the heat went, so a loop drawing off a reactor at one
                    // sink and shedding into a radiator at another reports both rather than their
                    // difference. See CoolantLoop.LastWattsAbsorbed.
                    if (exchange < 0f) loop.AbsorbedEnergy -= exchange * h;
                    else loop.RejectedEnergy += exchange * h;
                }
            }
        }

        /// <summary>
        /// Exchanges heat between each sealed room's air and the surfaces bounding it.
        ///
        /// Same form as <see cref="AccumulateLoops"/>: a lumped mass against a set of nodes. The
        /// mass differs in origin — a coolant loop carries the fluid its definition declares, a
        /// room carries the air its volume holds at the pressure the host reports.
        /// </summary>
        private void AccumulateRoomAir(float h)
        {
            if (!settings.EnableRoomAir) return;

            bool clamp = settings.ClampConductionOvershoot;
            bool diagnostics = CollectDiagnostics;

            if (diagnostics)
            {
                for (int i = 0; i < nodes.Count; i++)
                {
                    nodes[i].LastRoomWatts = 0f;
                }
            }

            for (int r = 0; r < roomAir.Count; r++)
            {
                RoomAirNode air = roomAir[r];
                if (!air.HasAir) continue;

                float airTemperature = air.Temperature;

                for (int i = 0; i < air.Links.Count; i++)
                {
                    RoomLink link = air.Links[i];
                    if (link.NodeIndex < 0 || link.NodeIndex >= nodes.Count) continue;

                    float difference = airTemperature - nodeTemperatures[link.NodeIndex];
                    float watts = link.Conductance * difference;

                    if (clamp)
                    {
                        watts = ClampExchange(
                            watts, h, difference,
                            air.ThermalMass,
                            nodeThermalMass[link.NodeIndex]);
                    }

                    nodeWatts[link.NodeIndex] += watts;
                    roomWatts[r] -= watts;

                    if (diagnostics) nodes[link.NodeIndex].LastRoomWatts += watts;
                }
            }
        }

        /// <summary>
        /// Caps a relaxation towards equilibrium at the watts that would exactly reach it.
        ///
        /// Both arguments carry their sign and a cap applies only when the two agree: a node being
        /// warmed cannot be capped by a cooling limit. Where they disagree the exchange is already
        /// heading away from the limit and there is nothing to cap.
        /// </summary>
        public static float ClampRelaxation(float watts, float wattsToEquilibrium)
        {
            if (watts > 0f)
            {
                if (wattsToEquilibrium <= 0f) return 0f;
                return watts > wattsToEquilibrium ? wattsToEquilibrium : watts;
            }

            if (watts < 0f)
            {
                if (wattsToEquilibrium >= 0f) return 0f;
                return watts < wattsToEquilibrium ? wattsToEquilibrium : watts;
            }

            return 0f;
        }

        /// <summary>
        /// Limits a pairwise exchange to the energy that brings both sides to their shared
        /// equilibrium, so neither can overshoot the other however long the substep is.
        ///
        /// Together with the substep count this makes the integrator unconditionally bounded:
        /// substepping keeps the result accurate, this keeps it finite when substepping is capped.
        /// </summary>
        public static float ClampExchange(float watts, float h, float difference, float massA, float massB)
        {
            if (h <= 0f) return watts;

            // Energy that would equalise the pair exactly.
            float combined = massA + massB;
            if (combined <= 0f) return watts;

            float maxEnergy = difference * (massA * massB / combined);
            float energy = watts * h;

            if (Math.Abs(energy) <= Math.Abs(maxEnergy)) return watts;
            return maxEnergy / h;
        }

        /// <summary>Applies accumulated watts to nodes <paramref name="from"/> to <paramref name="to"/>.</summary>
        private void ApplyNodeWattsRange(float h, int from, int to)
        {
            bool damageEnabled = settings.EnableDamage;

            bool perSecond = settings.DamageIsPerSecond;

            for (int i = from; i < to; i++)
            {
                float previous = nodeTemperatures[i];
                float updated = previous + (nodeWatts[i] * h / nodeThermalMass[i]);
                if (updated < ThermalConstants.MinimumTemperature)
                {
                    updated = ThermalConstants.MinimumTemperature;
                }

                nodeTemperatures[i] = updated;

                if (!damageEnabled) continue;

                // Only an overheating node dereferences its definition, so the ordinary case
                // stays within the flat arrays — including the test itself, which is what decides
                // whether the block is reached at all.
                float critical = nodeCritical[i];
                if (critical <= 0f || updated <= critical) continue;

                ThermalNode node = nodes[i];
                float damage = (updated - critical) * node.Thermal.OverheatDamagePerKelvin;
                if (perSecond) damage *= h;
                if (damage > 0f)
                {
                    overheats.Add(new OverheatEvent(node.Block, updated, damage));
                }
            }
        }

        /// <summary>
        /// Applies accumulated watts to the coolant loops and the room air. Run whole rather than
        /// sliced: there are few of each against tens of thousands of nodes, so slicing would cost
        /// more bookkeeping than the work it spread.
        /// </summary>
        private void ApplyCoupledWatts(float h)
        {
            for (int l = 0; l < loops.Count; l++)
            {
                CoolantLoop loop = loops[l];
                float mass = EffectiveLoopMass(l);
                float[] watts = loop.SegmentWatts;

                for (int i = 0; i < watts.Length; i++)
                {
                    loop.ApplyParcelWatts(i, watts[i], h, mass);
                }

                // Exchange first, then carry: a parcel takes heat where it is and then moves on,
                // which is the order that lets a sink face reach a radiator on the far side.
                loop.Advect(h);
            }

            for (int r = 0; r < roomAir.Count; r++)
            {
                RoomAirNode air = roomAir[r];
                if (!air.HasAir) continue;

                float updated = air.Temperature + (roomWatts[r] * h / EffectiveRoomMass(r));
                air.Temperature = updated < ThermalConstants.MinimumTemperature
                    ? ThermalConstants.MinimumTemperature
                    : updated;
            }
        }

        // ---- stability ---------------------------------------------------------------------

        /// <summary>
        /// Substeps needed to keep the explicit integrator stable over
        /// <paramref name="deltaSeconds"/>, before the <see cref="MaxSubsteps"/> cap.
        ///
        /// Callers ask this before deciding whether to step, so it synchronises the mirrored node
        /// state itself rather than assuming a step has already done so. On a solver that has never
        /// stepped, unsynchronised state would divide conductance by a zero thermal mass.
        /// </summary>
        public float RequiredSubsteps(float deltaSeconds)
        {
            PrepareStepState();
            return RequiredSubstepsFromState(deltaSeconds);
        }

        /// <summary>
        /// The same estimate against the environment the step is about to run in, rather than
        /// against the one the last step left behind.
        ///
        /// Both the estimate and the mass floor read the environment's convection coefficient, so
        /// a caller deciding how long a step to take wants the sample it is about to hand
        /// <see cref="BeginStep"/> — and <see cref="BeginStep"/> can then be handed the answer
        /// rather than walking every node again for it.
        /// </summary>
        public float RequiredSubsteps(float deltaSeconds, EnvironmentState environment)
        {
            Environment = environment;
            return RequiredSubsteps(deltaSeconds);
        }

        /// <summary>
        /// Everything a step needs mirrored before either the estimate or the substeps read it.
        ///
        /// Order matters: the conductance totals, then the mass floor that reads them. The link
        /// mass factors the floor invalidates are refreshed by <see cref="BeginStep"/>, which is
        /// the only caller that integrates.
        /// </summary>
        private void PrepareStepState()
        {
            RebuildLinksIfNeeded();
            EnsureBuffers();
            SyncNodeState();
            RecomputeConductanceTotalsIfNeeded();
            ApplyThermalMassFloor();
        }

        /// <summary>
        /// The environment half of a node's stability demand, resolved once for the whole grid.
        ///
        /// Shared by the substep estimate, the thermal mass floor that bounds it, and the profile
        /// the telemetry reports; a floor computed from a different rate than the estimate reads
        /// would cap the wrong figure.
        /// </summary>
        private struct StabilityTerms
        {
            public bool Exposed;
            public bool Radiating;
            public float Convection;
        }

        private StabilityTerms StabilityEnvironment()
        {
            StabilityTerms terms = new StabilityTerms();

            terms.Radiating = settings.EnableEnvironment && settings.EnableRadiation;
            bool convecting = settings.EnableEnvironment && settings.EnableConvection;
            terms.Convection = convecting
                ? Environment.ConvectionCoefficient * Environment.AtmosphereFactor
                : 0f;
            terms.Exposed = terms.Radiating || convecting;
            return terms;
        }

        /// <summary>
        /// Conductance a node sees per second, in W/K: links, coolant loops and room air, plus the
        /// linearised environment coupling. Divided by heat capacity this is <c>1/tau</c>, and
        /// <c>tau</c> is what the step has to stay inside.
        /// </summary>
        private float NodeStabilityRate(int i, ref StabilityTerms terms)
        {
            // Conductance totals cover links, coolant loops and room air alike and are not reduced
            // when a mechanism is switched off: over-estimating stiffness costs a substep,
            // under-estimating it costs stability.
            float rate = nodeConductanceTotal[i];

            if (!terms.Exposed || nodeExposedFaces[i] <= 0) return rate;

            // Linearised environment coupling: d(radiated watts)/dT = 4 e s A T^3
            if (terms.Radiating)
            {
                float t = nodeTemperatures[i];
                rate += 4f * nodeRadiation[i] * t * t * t;
            }

            return rate + (terms.Convection * nodeExposedArea[i]);
        }

        /// <summary>
        /// The substep estimate over state the caller has already synchronised. <see cref="Step"/>
        /// uses this so a single step does not synchronise twice.
        /// </summary>
        private float RequiredSubstepsFromState(float deltaSeconds)
        {
            Work.StabilityEstimates++;

            float worst = 0f;

            StabilityTerms terms = StabilityEnvironment();

            for (int i = 0; i < nodes.Count; i++)
            {
                float perNode = NodeStabilityRate(i, ref terms) / nodeThermalMass[i];
                if (perNode > worst) worst = perNode;
            }

            for (int l = 0; l < loops.Count; l++)
            {
                // Per parcel, not per ring: splitting the fluid divides capacity and links by the
                // same count, so this is unchanged from the well-mixed model — but only because both
                // halves were divided. Comparing a ring's total conductance against one parcel's
                // capacity would over-report by the ring's length.
                float perLoop = SegmentConductance(l) / EffectiveLoopMass(l);
                if (perLoop > worst) worst = perLoop;

                // Flow imposes no limit of its own. Carrying the fluid is a rotation of which parcel
                // sits in which pipe, which is exact at any speed — so a fast pump costs substeps
                // nowhere, and the flow rate is free to be set for how the game should feel rather
                // than for what the integrator will tolerate. Blending each parcel into the next,
                // which this replaced, was stable only below one parcel per substep.
            }

            // Room air has the lowest capacity and the largest contact area on the grid, so it
            // usually sets the substep count once a ship is pressurised.
            for (int r = 0; r < roomAir.Count; r++)
            {
                if (!roomAir[r].HasAir) continue;

                float perRoom = RoomConductance(r) / EffectiveRoomMass(r);
                if (perRoom > worst) worst = perRoom;
            }

            if (worst <= 0f) return 1f;
            return (deltaSeconds * worst) / StabilitySafetyFactor;
        }

        /// <summary>
        /// A grid's substep demand broken down for reporting, gathered in one walk.
        ///
        /// <para>
        /// The solver acts on a single maximum over every element, which does not say which element
        /// set it. This reports the responsible element, the distribution behind it, and the effect
        /// each of several caps would have on both cost and error.
        /// </para>
        ///
        /// <para>
        /// Demands are computed from real heat capacities rather than the floored ones the solver
        /// may be integrating with, so a profile describes the grid rather than the settings in
        /// force and two configurations are comparable.
        /// </para>
        ///
        /// <para>O(nodes): intended for reports and diagnostics, not for a step.</para>
        /// </summary>
        public class SubstepProfile
        {
            /// <summary>Demand thresholds the node histogram counts against.</summary>
            public static readonly float[] DemandEdges =
                { 0.5f, 1f, 2f, 4f, 8f, 16f, 32f, 64f, 128f, 256f };

            /// <summary>
            /// Caps the projection evaluates.
            ///
            /// Dense at the low end, where the choice is made. Cost falls linearly with the cap,
            /// but the share of blocks the cap reaches does not, so the useful reading is where
            /// the knee falls and a sparser list would step over it.
            /// </summary>
            public static readonly int[] ProjectedCaps = { 32, 16, 8, 6, 4, 3, 2, 1 };

            public float StepSeconds;
            public int Nodes;
            public int Links;

            /// <summary>Substeps a full step would need, from real capacities and no cap.</summary>
            public float RequiredSubsteps;

            /// <summary>Substeps a full step needs under the settings in force, cap included.</summary>
            public float RequiredSubstepsInForce;

            public float WorstNodeDemand;
            public int WorstNodeIndex = -1;

            /// <summary>Share of the worst node's stability rate that is conduction rather than environment.</summary>
            public float WorstNodeConductionShare;

            /// <summary>Stiffest room air and coolant loop; neither is reachable by the block cap.</summary>
            public float WorstRoomAirDemand;
            public int WorstRoomAirIndex = -1;
            public float WorstLoopDemand;
            public int WorstLoopIndex = -1;

            /// <summary>Nodes per demand bucket, one longer than the edges for the overflow.</summary>
            public readonly long[] Buckets = new long[DemandEdges.Length + 1];

            /// <summary>Per projected cap: nodes it would raise, and what the estimate would become.</summary>
            public readonly long[] CapNodesFloored = new long[ProjectedCaps.Length];
            public readonly float[] CapRequiredSubsteps = new float[ProjectedCaps.Length];

            /// <summary>Nodes whose demand is entirely environment, which a conduction-only floor would miss.</summary>
            public long EnvironmentDominatedNodes;
        }

        /// <summary>
        /// Walks every node, room and loop once and reports what sets the substep count.
        /// </summary>
        public SubstepProfile ProfileSubsteps()
        {
            // The mirrored state must not be refreshed while a step is in flight: a step spans
            // many frames, and SyncNodeState rewrites the row the publish stage measures its
            // change against. Mid-step the profile reads the state the step is already using.
            if (!StepInFlight)
            {
                RebuildLinksIfNeeded();
                EnsureBuffers();
                SyncNodeState();
                RecomputeConductanceTotalsIfNeeded();
                ApplyThermalMassFloor();
            }

            SubstepProfile profile = new SubstepProfile();
            profile.StepSeconds = settings.StepSeconds;
            profile.Links = links.Count;

            float scale = settings.StepSeconds / StabilitySafetyFactor;
            StabilityTerms terms = StabilityEnvironment();

            float[] edges = SubstepProfile.DemandEdges;
            int[] caps = SubstepProfile.ProjectedCaps;

            // The block cap cannot reach room air or coolant loops, so their demand is a lower
            // bound on every projection below.
            for (int l = 0; l < loops.Count; l++)
            {
                CoolantLoop loop = loops[l];
                float demand = loop.ThermalMass <= 0f
                    ? 0f
                    : (LoopConductance(l) / loop.ThermalMass) * scale;

                if (demand > profile.WorstLoopDemand)
                {
                    profile.WorstLoopDemand = demand;
                    profile.WorstLoopIndex = l;
                }
            }

            for (int r = 0; r < roomAir.Count; r++)
            {
                RoomAirNode air = roomAir[r];
                if (!air.HasAir) continue;

                float demand = air.ThermalMass <= 0f
                    ? 0f
                    : (RoomConductance(r) / air.ThermalMass) * scale;

                if (demand > profile.WorstRoomAirDemand)
                {
                    profile.WorstRoomAirDemand = demand;
                    profile.WorstRoomAirIndex = r;
                }
            }

            float coupled = profile.WorstLoopDemand > profile.WorstRoomAirDemand
                ? profile.WorstLoopDemand
                : profile.WorstRoomAirDemand;

            for (int c = 0; c < caps.Length; c++)
            {
                profile.CapNodesFloored[c] = 0;
                profile.CapRequiredSubsteps[c] = coupled > caps[c] ? caps[c] : coupled;
            }

            // A node appended during a step in flight has no mirrored row yet and joins the next
            // step; reading past the arrays would return another block's conductance.
            int count = nodes.Count;
            if (count > nodeConductanceTotal.Length) count = nodeConductanceTotal.Length;
            profile.Nodes = count;

            for (int i = 0; i < count; i++)
            {
                float rate = NodeStabilityRate(i, ref terms);
                float conduction = nodeConductanceTotal[i];

                // The block's real capacity, not the floored one the solver may be integrating
                // with, so the profile describes the grid rather than the settings.
                float capacity = nodes[i].ThermalMass;
                float demand = capacity <= 0f ? 0f : (rate / capacity) * scale;

                if (demand > profile.WorstNodeDemand)
                {
                    profile.WorstNodeDemand = demand;
                    profile.WorstNodeIndex = i;
                    profile.WorstNodeConductionShare = rate <= 0f ? 0f : conduction / rate;
                }

                if (rate > 0f && conduction < rate * 0.5f && demand > 1f)
                {
                    profile.EnvironmentDominatedNodes++;
                }

                int bucket = edges.Length;
                for (int e = 0; e < edges.Length; e++)
                {
                    if (demand < edges[e]) { bucket = e; break; }
                }
                profile.Buckets[bucket]++;

                for (int c = 0; c < caps.Length; c++)
                {
                    float capped = demand > caps[c] ? caps[c] : demand;
                    if (demand > caps[c]) profile.CapNodesFloored[c]++;
                    if (capped > profile.CapRequiredSubsteps[c]) profile.CapRequiredSubsteps[c] = capped;
                }
            }

            float worst = profile.WorstNodeDemand > coupled ? profile.WorstNodeDemand : coupled;
            profile.RequiredSubsteps = worst <= 0f ? 1f : worst;

            // The private estimate: the public entry point synchronises first, which must not
            // happen mid-step.
            profile.RequiredSubstepsInForce = RequiredSubstepsFromState(settings.StepSeconds);

            return profile;
        }

        /// <summary>Rounds a substep estimate up into the allowed range.</summary>
        private int ClampSubsteps(float required)
        {
            if (required <= 1f) return 1;

            int substeps = (int)Math.Ceiling(required);
            if (substeps > MaxSubsteps) substeps = MaxSubsteps;
            return substeps;
        }

        /// <summary>
        /// Set when a structural change altered the conductance a node sees, so the totals are
        /// recomputed once before they are next read rather than once per change.
        ///
        /// Such changes arrive in bursts — a ship pressurising is every room gaining air within a
        /// second or two — and each pass costs a walk over every link, loop and room on the grid.
        /// </summary>
        private bool conductanceTotalsDirty = true;

        private void RecomputeConductanceTotalsIfNeeded()
        {
            if (!conductanceTotalsDirty) return;
            RecomputeConductanceTotals();
        }

        /// <summary>
        /// Raises the mirrored heat capacity of any node that would otherwise demand more than
        /// <c>MaxSubstepsPerBlock</c> substeps of the whole grid.
        ///
        /// <para>
        /// A node is stable over a substep of <c>h</c> while <c>h * G &lt;= C</c>, so a step of
        /// <c>dt</c> demands <c>dt * G / (C * safety)</c> substeps. Holding that at or below the
        /// cap rearranges to <c>C &gt;= G * dt / (safety * cap)</c>. Nodes already at or above the
        /// floor are untouched.
        /// </para>
        ///
        /// <para>
        /// Only the mirrored row moves. <c>ThermalNode.ThermalMass</c> keeps the block's real heat
        /// capacity, so terminal readouts, energy figures and host queries still describe the block
        /// rather than the approximation used to integrate it.
        /// </para>
        ///
        /// <para>
        /// Applied after the conductance totals and before the link mass factors: it reads the
        /// first and invalidates the second.
        /// </para>
        /// </summary>
        private void ApplyThermalMassFloor()
        {
            FlooredNodes = 0;

            int cap = settings.MaxSubstepsPerBlock;
            if (cap <= 0) return;

            float step = settings.StepSeconds;
            if (step <= 0f) return;

            float perRate = step / (StabilitySafetyFactor * cap);
            bool moved = false;

            StabilityTerms terms = StabilityEnvironment();

            int count = nodes.Count;
            for (int i = 0; i < count; i++)
            {
                // Based on the block's real capacity, as the loop and room passes below are, and
                // not on the mirrored row. SyncNodeState only refreshes a row whose node is dirty,
                // so the row still holds whatever this pass wrote last step: reading it back would
                // make the floor a high-water mark that never falls. A block that was briefly hot
                // would stay damped for the rest of the session, and the grid's behaviour would
                // depend on the hottest moment in its history.
                float real = nodes[i].ThermalMass;
                float floor = NodeStabilityRate(i, ref terms) * perRate;
                float wanted = real < floor ? floor : real;

                if (nodeThermalMass[i] != wanted)
                {
                    nodeThermalMass[i] = wanted;
                    moved = true;
                }

                // Counts the nodes standing above their real capacity, not the ones this pass
                // happened to move. An event count reads zero on every step after the first, which
                // is precisely when the floor is doing all of its work.
                if (wanted > real) FlooredNodes++;
            }

            // A link's reduced mass is a function of the two capacities either side of it.
            if (moved) linkMassFactorFrom = 0;

            for (int l = 0; l < loops.Count; l++)
            {
                CoolantLoop loop = loops[l];
                float floor = SegmentConductance(l) * perRate;
                loopEffectiveMass[l] = loop.SegmentThermalMass < floor ? floor : loop.SegmentThermalMass;
            }

            for (int r = 0; r < roomAir.Count; r++)
            {
                RoomAirNode air = roomAir[r];
                float floor = RoomConductance(r) * perRate;
                roomEffectiveMass[r] = air.ThermalMass < floor ? floor : air.ThermalMass;
            }
        }

        /// <summary>
        /// Total conductance into each coolant loop and each room's air, summed once per rebuild
        /// rather than once per caller.
        ///
        /// <para>
        /// The stability estimate and the thermal mass floor both read these, and both run twice
        /// per step — once for <c>AffordableStepSeconds</c> and once inside <c>BeginStep</c> — so
        /// without the cache a step re-sums every room link four times for four identical answers.
        /// </para>
        ///
        /// <para>
        /// Filled by the pass that recomputes the node conductance totals and invalidated by the
        /// same flag: they go stale for the same reasons — a loop rebuilt, a room re-derived, a
        /// block placed or removed.
        /// </para>
        ///
        /// <para>
        /// The saving is small in absolute terms (measured at roughly two parts in a thousand of
        /// a step) but it removes work that grows with the wall area of every pressurised
        /// compartment.
        /// </para>
        /// </summary>
        private float[] loopConductanceTotal = new float[0];
        private float[] roomConductanceTotal = new float[0];

        private float LoopConductance(int index)
        {
            return index >= 0 && index < loopConductanceTotal.Length ? loopConductanceTotal[index] : 0f;
        }

        /// <summary>
        /// The largest conductance any single parcel of a loop carries, W/K.
        ///
        /// The stability question is about one parcel, and parcels are not alike: a pipe with two sink
        /// faces carries three links while a plain length of pipe carries one. Taking the worst is
        /// what keeps the busiest parcel stable rather than the average one.
        /// </summary>
        private float SegmentConductance(int index)
        {
            if (index < 0 || index >= loops.Count) return 0f;

            CoolantLoop loop = loops[index];
            int count = loop.ParcelCount;
            if (count <= 0) return 0f;

            if (segmentConductanceScratch.Length < count)
            {
                segmentConductanceScratch = new float[Math.Max(16, count * 2)];
            }
            Array.Clear(segmentConductanceScratch, 0, count);

            for (int i = 0; i < loop.Links.Count; i++)
            {
                LoopLink link = loop.Links[i];
                if (link.SegmentIndex < 0 || link.SegmentIndex >= loop.PipeCount) continue;

                int parcel = loop.ParcelOf(link.SegmentIndex);
                if (parcel < 0 || parcel >= count) continue;

                segmentConductanceScratch[parcel] += link.Conductance;
            }

            float worst = 0f;
            for (int i = 0; i < count; i++)
            {
                if (segmentConductanceScratch[i] > worst) worst = segmentConductanceScratch[i];
            }
            return worst;
        }

        private float[] segmentConductanceScratch = new float[0];

        private float RoomConductance(int index)
        {
            return index >= 0 && index < roomConductanceTotal.Length ? roomConductanceTotal[index] : 0f;
        }

        /// <summary>Re-sums the coupled totals. Called only where the node totals are recomputed.</summary>
        private void RecomputeCoupledConductance()
        {
            if (loopConductanceTotal.Length < loops.Count)
            {
                loopConductanceTotal = new float[Math.Max(4, loops.Count * 2)];
            }

            if (roomConductanceTotal.Length < roomAir.Count)
            {
                roomConductanceTotal = new float[Math.Max(4, roomAir.Count * 2)];
            }

            for (int l = 0; l < loops.Count; l++)
            {
                CoolantLoop loop = loops[l];
                float total = 0f;
                for (int i = 0; i < loop.Links.Count; i++) total += loop.Links[i].Conductance;
                loopConductanceTotal[l] = total;
            }

            for (int r = 0; r < roomAir.Count; r++)
            {
                RoomAirNode air = roomAir[r];
                if (!air.HasAir)
                {
                    roomConductanceTotal[r] = 0f;
                    continue;
                }

                float total = 0f;
                for (int i = 0; i < air.Links.Count; i++) total += air.Links[i].Conductance;
                roomConductanceTotal[r] = total;
            }
        }

        /// <summary>
        /// Capacity the integrator uses for a loop: the loop's own unless the cap raised it. Falls
        /// back to the real capacity rather than zero, since the effective row is filled only while
        /// the cap is on and a zero divisor would yield an infinite temperature.
        /// </summary>
        private float EffectiveLoopMass(int index)
        {
            if (settings.MaxSubstepsPerBlock <= 0 || index >= loopEffectiveMass.Length
                || loopEffectiveMass[index] <= 0f)
            {
                return loops[index].SegmentThermalMass;
            }

            return loopEffectiveMass[index];
        }

        /// <summary>
        /// Capacity the integrator uses for a room's air. Same fallback as
        /// <see cref="EffectiveLoopMass"/>.
        /// </summary>
        private float EffectiveRoomMass(int index)
        {
            if (settings.MaxSubstepsPerBlock <= 0 || index >= roomEffectiveMass.Length
                || roomEffectiveMass[index] <= 0f)
            {
                return roomAir[index].ThermalMass;
            }

            return roomEffectiveMass[index];
        }

        /// <summary>Nodes whose capacity the floor raised on the last step. Zero when the cap is off.</summary>
        public int FlooredNodes { get; private set; }

        private void RecomputeConductanceTotals()
        {
            Work.ConductanceRecomputes++;
            conductanceTotalsDirty = false;
            EnsureBuffers();
            RecomputeCoupledConductance();
            for (int i = 0; i < nodes.Count; i++)
            {
                nodeConductanceTotal[i] = 0f;
            }

            for (int i = 0; i < links.Count; i++)
            {
                ThermalLink link = links[i];
                nodeConductanceTotal[link.NodeA] += link.Conductance;
                nodeConductanceTotal[link.NodeB] += link.Conductance;
            }

            for (int l = 0; l < loops.Count; l++)
            {
                CoolantLoop loop = loops[l];
                for (int i = 0; i < loop.Links.Count; i++)
                {
                    LoopLink link = loop.Links[i];
                    if (link.NodeIndex >= 0 && link.NodeIndex < nodeConductanceTotal.Length)
                    {
                        nodeConductanceTotal[link.NodeIndex] += link.Conductance;
                    }
                }
            }

            for (int r = 0; r < roomAir.Count; r++)
            {
                RoomAirNode air = roomAir[r];
                if (!air.HasAir) continue;

                for (int i = 0; i < air.Links.Count; i++)
                {
                    RoomLink link = air.Links[i];
                    if (link.NodeIndex >= 0 && link.NodeIndex < nodeConductanceTotal.Length)
                    {
                        nodeConductanceTotal[link.NodeIndex] += link.Conductance;
                    }
                }
            }
        }

        /// <summary>
        /// Grows the per-node, per-loop and per-room buffers to cover the current element counts.
        /// </summary>
        /// <returns>
        /// True when the node buffers were reallocated, discarding anything accumulated in them.
        /// Only <see cref="nodeConductanceTotal"/> accumulates across calls rather than being
        /// rewritten each step, so incremental callers must check this and rebuild it.
        /// </returns>
        private bool EnsureBuffers()
        {
            bool grew = false;

            if (nodeWatts.Length < nodes.Count)
            {
                grew = true;

                // Every row below is about to be replaced by a zeroed one and refilled at the next
                // SyncNodeState, which does not run until the next step begins. A step in flight
                // would carry on over those zeros — dividing watts by a heat capacity of zero and
                // publishing the result — so it is abandoned here, exactly as it is for a removal
                // or a rebuild. Nothing is lost but the watts this substep had accumulated.
                AbandonStep();

                // Reallocating zeroes the conductance totals, the one node array accumulated
                // across calls rather than rewritten each step. The resync below refills the rest,
                // so this must be marked for recompute or every surviving node loses the
                // conductance it sees, and the substep estimate with it.
                conductanceTotalsDirty = true;

                // Grow by a quarter rather than doubling. Around fourteen arrays are indexed by
                // node, so doubling leaves a settled grid holding a spare copy of each for its
                // lifetime. These grow when a block is placed, not in a tight append loop.
                int size = Math.Max(16, nodes.Count + (nodes.Count / 4) + 16);
                nodeWatts = new float[size];
                nodeTemperatures = new float[size];
                nodeStepStart = new float[size];
                nodeConductanceTotal = new float[size];
                resyncAll = true;
                sunLitDirty = true;
                nodeThermalMass = new float[size];
                nodeRadiation = new float[size];
                nodeGeneration = new float[size];
                nodeExposedArea = new float[size];
                nodeEmissivity = new float[size];
                nodeCritical = new float[size];
                nodeExposedFaces = new int[size];
                nodeRelaxation = new float[size];
                nodeSolarRow = new float[size];
                nodeFrictionRow = new float[size];
                nodeConvectionRow = new float[size];
                nodeSourceRow = new float[size];
                environmentRowsValid = false;
                nodeFaceWeights = new float[size * Face.Count];
                nodeSunLit = new float[size * Face.Count];
            }
            if (loopEffectiveMass.Length < loops.Count)
            {
                // One capacity per loop, not per parcel: every parcel in a ring has the same mass, so
                // the floor that applies to one applies to all of them.
                loopEffectiveMass = new float[Math.Max(4, loops.Count * 2)];
            }
            if (roomWatts.Length < roomAir.Count)
            {
                roomWatts = new float[Math.Max(4, roomAir.Count * 2)];
                roomEffectiveMass = new float[roomWatts.Length];
            }

            return grew;
        }

        // ---- diagnostics -------------------------------------------------------------------

        /// <summary>Total thermal energy on the grid, J. Constant in a closed system.</summary>
        public float TotalEnergy
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < nodes.Count; i++)
                {
                    total += nodes[i].Energy;
                }
                for (int i = 0; i < loops.Count; i++)
                {
                    total += loops[i].Energy;
                }
                for (int i = 0; i < roomAir.Count; i++)
                {
                    if (roomAir[i].HasAir) total += roomAir[i].Energy;
                }
                return total;
            }
        }

        /// <summary>Index of the hottest node as of the last step, set by the step's write-back.</summary>
        private int hottestNode = -1;

        /// <summary>
        /// The hottest node on the grid, or null when the grid has none.
        ///
        /// Answered from the index the last step recorded rather than by a fresh pass; the figure
        /// feeds the cockpit summary, the crosshair readout and the telemetry report, all of which
        /// sample on a cadence. A temperature changed from outside a step — a save load, a grid
        /// split — is reflected one step later. Falls back to a linear scan when the index is
        /// stale.
        /// </summary>
        public ThermalNode HottestNode()
        {
            if (hottestNode < 0 || hottestNode >= nodes.Count)
            {
                // Nothing has stepped, or the node list shrank under the cached index.
                ThermalNode found = null;
                for (int i = 0; i < nodes.Count; i++)
                {
                    if (found == null || nodes[i].Temperature > found.Temperature) found = nodes[i];
                }
                return found;
            }

            return nodes[hottestNode];
        }

        /// <summary>Sets every node and coolant loop to one temperature. Test and load helper.</summary>
        public void SetAllTemperatures(float kelvin)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i].Temperature = kelvin;
            }
            for (int i = 0; i < loops.Count; i++)
            {
                loops[i].Temperature = kelvin;
            }
        }
    }
}
