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
    /// Integration is explicit but energy-conserving and order-independent: every exchange is
    /// computed from the temperatures at the start of a substep, accumulated as watts per node,
    /// and applied at the end. Nothing depends on the order nodes appear in, so there is no need
    /// for the alternating sweep direction the original used to hide its order dependence, and
    /// the pass is safe to parallelise later.
    /// </summary>
    public partial class ThermalSolver
    {
        /// <summary>Fraction of the theoretical stability limit a substep is allowed to use.</summary>
        public const float StabilitySafetyFactor = 0.5f;

        /// <summary>
        /// Upper bound on substeps per call, so a pathological grid cannot stall a frame.
        ///
        /// Read from the settings, so a world can choose where it sits between accuracy and cost;
        /// see <see cref="ThermalSettings.MaxSubsteps"/>. Kept as a property rather than a field
        /// because it was a field, and a host or a test that had written to it should stop
        /// compiling rather than quietly have no effect.
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
        /// Temperature at the top of the step, kept so the reported change is the change over the
        /// whole step rather than over its last substep.
        /// </summary>
        private float[] nodeStepStart = new float[0];

        private float[] nodeConductanceTotal = new float[0];
        private float[] loopWatts = new float[0];
        private float[] roomWatts = new float[0];

        /// <summary>
        /// Heat capacities the integrator uses for the coupled elements, which are the real ones
        /// unless <c>MaxSubstepsPerBlock</c> has raised them.
        ///
        /// Room air is the lightest thing on a ship and touches the most surface, so a small
        /// pressurised compartment is routinely stiffer than any block bolted around it — a field
        /// dump found a capital ship still needing two substeps after every one of its blocks had
        /// been capped at one, because a two-cell room demanded 1.75 on its own. Capping the
        /// blocks and not the air would leave the setting unable to reach the value it advertises.
        /// </summary>
        private float[] loopEffectiveMass = new float[0];
        private float[] roomEffectiveMass = new float[0];

        // Node state the substep loop reads, copied out of the node objects once per step
        // instead of being chased through them once per node per substep. A step with sixteen
        // substeps used to walk eight thousand heap objects sixteen times over.
        private float[] nodeThermalMass = new float[0];
        private float[] nodeRadiation = new float[0];
        private float[] nodeGeneration = new float[0];
        private float[] nodeExposedArea = new float[0];
        private float[] nodeEmissivity = new float[0];
        private int[] nodeExposedFaces = new int[0];

        /// <summary>Exposed faces as a fraction of the node's total, six per node.</summary>
        private float[] nodeFaceWeights = new float[0];

        /// <summary>
        /// How much of a node's exchanges it may take this substep, 0..1.
        ///
        /// One is the ordinary case and costs a multiply. Below one means the substep is longer
        /// than this node is stable over — <c>h * conductance &gt; mass</c> — which happens only
        /// when the substep count the stability estimate asked for was refused.
        ///
        /// The per-link clamp is not enough on its own, and that is not obvious. It caps each
        /// exchange at the energy that equalises <em>that pair</em>, which bounds a node with one
        /// neighbour perfectly and a node with six not at all: each of the six is separately
        /// entitled to move it the whole way, so it lands six times past where it should and comes
        /// back further still. Measured on a hull at <c>HeatTimeScale 3600</c> with one substep,
        /// that reached 1.5e22 K; the same settings on a stick of blocks, where no node has more
        /// than two neighbours, stayed perfectly well behaved. The clamp was not wrong, it was
        /// local.
        /// </summary>
        private float[] nodeRelaxation = new float[0];

        /// <summary>
        /// Per-node environment terms that do not change between the substeps of one step.
        ///
        /// <para>
        /// A substep recomputed all of these, sixteen times a step, from inputs that had not
        /// moved. Solar gain is emissivity times exposed area times how square the node's faces
        /// are to the sun and how much of each is lit — and none of those depend on temperature,
        /// which is the only thing a substep changes. Friction is the same shape. Convection's
        /// coefficient is too; only the <c>(T - ambient)</c> it multiplies varies. And the
        /// relaxation factor is <c>mass / (h * conductance)</c>, whose three inputs are all fixed
        /// for the whole step — a float divide per node per substep for an answer that was
        /// already on the previous line.
        /// </para>
        ///
        /// <para>
        /// They are filled by the first substep's own environment pass rather than by a pass of
        /// their own, so the work is charged and paced exactly as it was and the later substeps
        /// simply read. The result is arithmetically identical, which
        /// <c>PrecomputedEnvironmentTests</c> asserts bit for bit.
        /// </para>
        /// </summary>
        private float[] nodeSolarRow = new float[0];
        private float[] nodeFrictionRow = new float[0];
        private float[] nodeConvectionRow = new float[0];

        /// <summary>
        /// False when the rows above have to be recomputed rather than read.
        ///
        /// Cleared at the top of every step, and again whenever the self-shadow pass publishes new
        /// lit fractions — which it can do part way through a step, since it runs on a budget of
        /// its own. That is the one input to the rows that moves under them.
        /// </summary>
        private bool environmentRowsValid;

        /// <summary>Forces the per-step environment rows to be recomputed on the next pass.</summary>
        private void InvalidateEnvironmentRows()
        {
            environmentRowsValid = false;
        }

        /// <summary>
        /// Set false to recompute the per-step environment terms on every substep, as the solver
        /// did before they were cached.
        ///
        /// Exists so the claim behind the cache can be tested rather than argued: reusing a value
        /// is only safe if recomputing it would have produced the same bits, and
        /// <c>PrecomputedEnvironmentTests</c> runs the same grid both ways and compares. A
        /// property that is asserted by an optimisation's own presence is not asserted at all.
        /// </summary>
        public bool PrecomputeEnvironment = true;

        /// <summary>
        /// Fraction of each node's <em>face</em> the sun reaches, six per node, 0..1. All ones when
        /// self-shadowing is off, which is what makes the cheap path free rather than cheaper.
        ///
        /// Per face because shadow belongs to a surface. The second layer of a two-cell wall is
        /// buried from the sun's direction and its side faces are still out in the open, on the
        /// same flank of the same ship; a per-block figure lights a hull along one row of blocks
        /// and calls the rest of it shadowed.
        /// </summary>
        private float[] nodeSunLit = new float[0];

        /// <summary>The grid's own shadow, rebuilt when the sun has moved far enough to matter.</summary>
        private readonly SunShadowMap sunShadow = new SunShadowMap();

        /// <summary>
        /// Other grids whose shadows fall on this one, filled by the host. Empty in the model's own
        /// tests, and empty in a world with nothing parked nearby.
        /// </summary>
        public readonly List<SunShadowMap.Occluder> SunOccluders = new List<SunShadowMap.Occluder>();

        /// <summary>
        /// Tells the solver its occluders have moved or changed, so the next pass rebuilds against
        /// them. The host owns that judgement: only it knows a station has drifted a metre.
        /// </summary>
        public void MarkSunOccludersChanged()
        {
            sunLitDirty = true;
        }

        /// <summary>
        /// How far the sun may move before a new shadow pass starts. cos(2°): a shadow edge that
        /// lags the sun by two degrees is a fraction of a cell on any ship, and restarting for less
        /// would spend a walk over the grid on a picture nobody can tell apart — and on a slowly
        /// creeping sun, would restart the pass forever without ever finishing one.
        /// </summary>
        private const float SunRebuildCosine = 0.99939f;

        /// <summary>
        /// Cells walked per environment pass. The walk is exact and therefore not free, so it is
        /// budgeted like the room mapper's flood fill: a slice per tick, with the previous answer
        /// still readable until the new one is complete.
        /// </summary>
        public int SunShadowBudget = 2048;

        /// <summary>
        /// Nodes whose lit fraction is refreshed per step once a shadow pass completes.
        ///
        /// The walk itself was budgeted; publishing its answer was not. A completed pass called a
        /// loop over every node on the grid, six faces each, from inside the step — 760,000 shadow
        /// lookups on a 127k hull, which measured as a 69 ms step against a 12 ms median with the
        /// conduction loop accounting for only a fifth of it. That is the same shape of mistake as
        /// the room mapper publishing its map: the expensive part was not the pass, it was the
        /// moment the pass finished.
        ///
        /// Spreading it leaves some faces reading the previous shadow for a few steps, which is
        /// what the shadow map already does by design — it is allowed to lag the sun by two
        /// degrees before a rebuild is worth starting.
        /// </summary>
        public int SunLitBudget = 4096;

        /// <summary>Where a lit-fraction refresh has got to, and whether one is running.</summary>
        private int sunLitCursor;
        private bool sunLitPending;

        /// <summary>
        /// Value to fill every face with instead of reading the shadow map, or a negative number
        /// when the map is the source. Used by the switched-off path, which has no map to read.
        /// </summary>
        private float sunLitFill = -1f;

        /// <summary>True when <see cref="nodeSunLit"/> no longer matches the nodes or the map.</summary>
        private bool sunLitDirty = true;

        /// <summary>
        /// Reduced thermal mass of each link, <c>mA*mB/(mA+mB)</c>. This is the only part of the
        /// overshoot clamp that depends on anything but the current temperatures, and it changes
        /// only when a block's mass does, so it is cached rather than divided out per link per
        /// substep.
        /// </summary>
        private float[] linkMassFactor = new float[0];

        /// <summary>
        /// First link whose cached reduced mass is stale. <see cref="int.MaxValue"/> when none
        /// are.
        ///
        /// An index rather than a flag because the two things that make a factor stale are not
        /// the same size of event. A block's mass changing — welding progress, damage — could be
        /// any link on the grid, so it invalidates from zero. Links being appended for a block
        /// just placed invalidates only the appended rows, and that is the case this needs to
        /// stay cheap.
        /// </summary>
        private int linkMassFactorFrom;

        // The conduction loop mirrored into flat arrays, for the same reason the node state is:
        // this is the innermost loop in the whole mod. A capital ship in the field run carried
        // 106,644 links and needed six substeps a step, so every link is visited 640,000 times a
        // second of simulated time — and reading a link out of a List<ThermalLink> copies a
        // sixteen-byte struct through a bounds-checked indexer to use twelve bytes of it.
        // ContactFaces is diagnostic and stays behind in the list.
        private int[] linkA = new int[0];
        private int[] linkB = new int[0];
        private float[] linkConductance = new float[0];

        /// <summary>Per-face weights of the sun and the airflow, resolved once per step.</summary>
        private readonly float[] sunWeights = new float[Face.Count];
        private readonly float[] windWeights = new float[Face.Count];

        /// <summary>Reused per registered point source, one source at a time.</summary>
        private readonly float[] sourceWeights = new float[Face.Count];

        /// <summary>
        /// Record every mechanism's contribution to every node, for the debug readout and the
        /// telemetry report.
        ///
        /// Off by default. The figures are five floats per node per substep that nothing in the
        /// simulation itself reads, so a server nobody is watching should not be writing them.
        /// </summary>
        public bool CollectDiagnostics;

        private readonly List<OverheatEvent> overheats = new List<OverheatEvent>();

        private readonly ThermalThresholds thresholds = new ThermalThresholds();
        private readonly List<ThresholdCrossing> crossings = new List<ThresholdCrossing>();
        private readonly int[] exposureScratch = new int[Face.Count];
        private readonly List<BlockInstance> neighbourScratch = new List<BlockInstance>();

        private IBlockAdjacency adjacency;

        /// <summary>
        /// How much work the one-shot stages did. Never null, so no call site needs a guard;
        /// the host replaces it with its own instance when it wants to read the same counters
        /// the room mapper writes to.
        /// </summary>
        public SimulationWork Work = new SimulationWork();

        private bool linksDirty = true;

        /// <summary>
        /// Nodes placed since the graph was last built, whose links have not been made yet.
        ///
        /// A block arriving is the one topology change that needs no demolition: nothing links
        /// to a block that was not there, so its links can simply be appended and every existing
        /// link left exactly as it was. That is what makes welding, pasting a blueprint and a
        /// projector building a ship cost the block rather than the grid.
        ///
        /// Anything else — a block removed, a block whose mounting changed, a new adjacency
        /// source — can invalidate links that already exist, and takes the global path.
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

            // Until the first step derives one, the environment has to read as empty space and
            // not as absolute zero. Anything that asks before then — RequiredSubsteps, the HUD,
            // the telemetry sample the host takes at the top of a step — otherwise sees a 0 K
            // sky, which is both wrong and the coldest reading the whole session will report.
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
        /// Where the conduction graph gets its "who touches whom" answers. Defaults to the
        /// <see cref="GridModel"/>, which resolves them from its own cell map.
        ///
        /// A host with a better index — Space Engineers 2's block octree maintains a face
        /// connectivity graph already — sets this instead of having the mod rebuild one.
        /// Setting it invalidates the existing links.
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
        /// How many links the graph holds <em>now</em>, without rebuilding it.
        ///
        /// The distinction matters more than it looks. Reading <see cref="Links"/> on a grid
        /// whose layout has changed runs a full rebuild, outside any stage bracket and on
        /// whatever thread asked — so a telemetry sample taken while a ship was being welded
        /// spent a rebuild's worth of a million nodes to report an integer, and reported it as
        /// nothing at all because no stage was timing it. An observer must not change what it
        /// observes; a diagnostic wants a count, not a graph, and is content with the count from
        /// before the change.
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
        /// The air masses of the grid's sealed rooms. One entry per room that is holding its air
        /// in; a vented room has none, because what faces it faces outdoors.
        /// </summary>
        public IList<RoomAirNode> RoomAir
        {
            get { return roomAir; }
        }

        /// <summary>
        /// The grid's heat pumps, connected or not. A pump with nothing bolted to one of its faces
        /// is still listed, so a readout can say that is why it is doing nothing.
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
        /// Substeps a <em>full</em> step would have needed, before <see cref="MaxSubsteps"/>
        /// refused any of them and before the rounding up to a whole number.
        ///
        /// Reported separately from <see cref="LastSubsteps"/> because the two answer different
        /// questions. What was granted says what the step cost; what was asked for says how far
        /// the grid is from affording its own stiffness, and it is the only one of the pair that
        /// keeps moving once the budget has bound.
        ///
        /// Scaled up to a full step for exactly that reason. The estimate is proportional to the
        /// step length, so a step already shortened to fit <c>MaxLinkVisitsPerStep</c> asks for
        /// about what it was granted — measuring that gives a column identical to the granted one
        /// and answers nothing.
        /// </summary>
        public float LastRequiredSubsteps { get; private set; }

        /// <summary>
        /// Substeps one element alone would need for a full step, from its real heat capacity.
        ///
        /// The per-node figure behind <see cref="LastRequiredSubsteps"/>, which is its maximum.
        /// Public so per-block-type telemetry can attribute the substep count to the definitions
        /// responsible for it rather than leaving it as a per-grid total.
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

        /// <summary>Registers a block. Blocks marked <c>IgnoreThermals</c> are skipped.</summary>
        public ThermalNode AddBlock(BlockInstance block, float initialTemperature)
        {
            if (block == null) throw new ArgumentNullException("block");
            if (block.Thermal.IgnoreThermals) return null;
            if (nodesByKey.ContainsKey(block.Key)) return nodesByKey[block.Key];

            ThermalNode node = new ThermalNode(block, grid.GridSize, initialTemperature, settings.HeatTimeScale);
            node.Index = nodes.Count;
            nodes.Add(node);
            nodesByKey[block.Key] = node;

            // Appended, so no existing index moved and no existing link became wrong. The node
            // is queued for linking rather than the whole graph being thrown away; only if the
            // graph was already due a full rebuild does that stand.
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

            // A removal moves another node into the hole, so a step part way through would be
            // summing watts for blocks that are no longer where its buffers say they are.
            AbandonStep();

            nodesByKey.Remove(block.Key);

            // A node still waiting to be linked has no links to unpick and no chain entry, so it
            // is dropped from the queue rather than routed through the removal path.
            if (node.PendingLinks)
            {
                node.PendingLinks = false;
                pendingLinkNodes.Remove(node);
            }

            if (linksDirty)
            {
                // The graph is already due a full rebuild, so unpicking one node's links would be
                // work thrown away. Take it out the plain way and let the rebuild sort the rest.
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

            // The cached hottest index refers to a slot, and a removal moves whoever was last
            // into some other slot. Forgotten rather than repaired: it is rebuilt by the next
            // step's write-back anyway, and until then a walk is correct.
            hottestNode = -1;

            return true;
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
        /// Brings the conduction graph up to date by whichever route is valid: the incremental
        /// one when only blocks have been placed, the global one otherwise. Does nothing when
        /// the graph already matches the layout.
        ///
        /// Public so the host can do it inside its topology stage rather than leaving it to be
        /// discovered by the first step, which billed it to the solver.
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
            // Every link index changes, so nothing a step in flight has accumulated still means
            // what it meant.
            AbandonStep();

            Work.TopologyRebuilds++;
            Work.TopologyNodeVisits += nodes.Count;

            // A global build makes every link, including the ones the queue was holding.
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

                    // add each pair once
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
        /// Only the rows that are not already mirrored are written. A global rebuild resets the
        /// mark and copies everything; an incremental one appends, which is what keeps the cost
        /// of placing a block proportional to the block. The arrays grow by doubling and keep
        /// what they held, because the rows below the new ones are still correct.
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
        /// The whole saving rests on one fact: a block that has just arrived has no links, and
        /// nothing that already exists links to it. So there is nothing to demolish — every
        /// existing link, every mirrored row and every conductance total below the new ones is
        /// still exactly right, and the work is the new block's own six faces.
        ///
        /// Two placed blocks that touch each other are the case worth being careful about. Each
        /// finds the other as a neighbour, so the pair is added by whichever of them has the
        /// lower index and skipped by the other — the same rule the global build uses, and the
        /// reason both loops test the index rather than a visited set.
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

                // A node placed and then taken away again before anything stepped.
                if (a.Index < 0 || a.Index >= nodes.Count || nodes[a.Index] != a) continue;

                neighbourScratch.Clear();
                adjacency.GetNeighbours(a.Block, neighbourScratch);

                for (int n = 0; n < neighbourScratch.Count; n++)
                {
                    ThermalNode b = GetNode(neighbourScratch[n]);
                    if (b == null) continue;

                    // The pair belongs to whichever end is also pending and lower, so a pair of
                    // new neighbours is added once. A neighbour that was already on the grid is
                    // never pending, so the test can only skip a pair both ends of which are.
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

            // Growing the buffers zeroed the totals and marked them for a recompute, so adding to
            // them would be adding to something about to be thrown away.
            if (!buffersGrew) AddConductanceOfNewLinks(firstNewLink);

            // Marked, not filled: the reduced mass of a link is computed from the mirrored node
            // masses, and the new node's row is not copied in until later in the same step.
            if (firstNewLink < linkMassFactorFrom) linkMassFactorFrom = firstNewLink;
        }

        /// <summary>
        /// Adds the conductance of newly built links to their endpoints' totals.
        ///
        /// The totals are what the substep estimate divides by thermal mass, and they also carry
        /// contributions from coolant loops and room air. Adding to them is therefore the correct
        /// incremental operation; recomputing them would mean walking every link, every loop and
        /// every room to learn what a handful of new links changed.
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
        public void SetLoops(List<CoolantLoop> newLoops)
        {
            Dictionary<long, float> previous = new Dictionary<long, float>();
            for (int i = 0; i < loops.Count; i++)
            {
                previous[loops[i].Signature] = loops[i].Temperature;
            }

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

                    // The coolant has to run on the same clock as the blocks it exchanges with,
                    // so the solver imposes it rather than trusting whoever built the loop.
                    loop.HeatTimeScale = settings.HeatTimeScale;

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
                        CoolantLoopBuilder.PipeConductance(grid, pipe, loop.Properties)));
                }

                List<GridPort> sinks = pipe.CoolantSinkPorts();
                for (int s = 0; s < sinks.Count; s++)
                {
                    BlockInstance target = grid.GetAtCell(sinks[s].Target);
                    if (target == null || target == pipe) continue;

                    ThermalNode targetNode = GetNode(target);
                    if (targetNode == null) continue;

                    loop.Links.Add(new LoopLink(
                        targetNode.Index,
                        CoolantLoopBuilder.PlateConductance(grid, loop.Properties)));
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

        /// <summary>The map the current exposure pass is reading, or null when none is running.</summary>
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
        /// Resumable for the same reason the flood fill is: it is proportional to the grid and
        /// it lands in one tick. A room pass completing on a 127k ship cost 30 ms here, all of
        /// it on the tick that published the map — on top of that tick's room stage, which is
        /// how a single tick came to cost a hundred milliseconds.
        ///
        /// Running it in slices leaves some nodes reading the previous map for a few ticks. That
        /// is not a new inaccuracy: the map they were reading is the one they had been reading
        /// for the hundreds of ticks the pass took to build, and a wall's exposure changing a
        /// fraction of a second late is invisible against a thermal clock measured in minutes.
        /// A stutter is not.
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
        /// Refreshes only the nodes that face the given rooms.
        ///
        /// A door opening changes what one room's walls can see and nothing else, so walking
        /// every node on a forty-thousand block ship to find the eight that changed is the
        /// expensive way to do nothing. The rooms carry their own cells, and a block can only be
        /// affected if it has a face onto one of them.
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

                    // The cell itself may hold a block — a door standing in the room.
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

        /// <summary>Scratch for <see cref="RefreshExposureAround"/>; kept so it is not reallocated.</summary>
        private HashSet<BlockInstance> affected;

        // ---- room air ----------------------------------------------------------------------

        /// <summary>Air temperature and pressure of rooms seen before the last rebuild.</summary>
        private readonly Dictionary<Vector3I, RoomAirNode> rememberedAir =
            new Dictionary<Vector3I, RoomAirNode>(Vector3I.Comparer);

        private readonly Dictionary<int, int> roomContactScratch = new Dictionary<int, int>();

        /// <summary>
        /// Rebuilds the air masses of every sealed room from a room map.
        ///
        /// A room's air survives a rebuild if the room does: rooms are re-found identically
        /// whenever the map is remade for a change somewhere else on the ship, so matching on the
        /// room's lowest cell carries temperature and pressure across. A room whose shape actually
        /// changed is a different room, and starts at ambient with no air until the host says
        /// otherwise.
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
        /// Links one room's air to every block bounding it.
        ///
        /// Walked over the room's own cells rather than over the grid's blocks, so the cost is the
        /// size of the room and not the size of the ship — the same reason
        /// <see cref="RefreshExposureAround"/> exists.
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

            // Air appearing in a room for the first time starts at the temperature of the walls
            // holding it, which is the only defensible answer: it has been in there with them.
            if (!air.Initialised && surfaceCount > 0)
            {
                air.Temperature = surfaceSum / surfaceCount;
                air.Initialised = true;
            }
        }

        /// <summary>
        /// Puts saved air temperatures back onto the rooms they were saved from, matched by anchor
        /// cell — the same key that carries a room's air across a rebuild.
        ///
        /// Marking the air initialised is the point of the exercise. A restored room is almost
        /// always still at zero pressure when this runs, because pressurisation comes from the vent
        /// sweep and that has not happened yet; without the flag the first sweep would take the
        /// room's temperature from the average of its walls and throw the saved figure away.
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
        /// A pump is bound to the two nodes either side of it, so anything that changes what is
        /// bolted to its faces has to run this again. The host switch and the power fraction are
        /// carried across by block key: they belong to the block, not to this table, and a pump
        /// must not silently switch itself on because a wall was welded somewhere else.
        /// </summary>
        public void RebuildHeatPumps()
        {
            Work.HeatPumpRebuilds++;

            // Same reasoning as the coolant search: a grid with no pumps on it should not walk
            // its blocks to find that out. The early return has to come after the list is
            // cleared, because the last pump on a grid being ground off is exactly the case
            // where the count reaches zero and the device must go with it.
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

        /// <summary>The heat pump bound to a block, or null when that block is not one.</summary>
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
        /// Moves heat from each pump's cold side to its hot side, and charges the work to its
        /// hot side as well.
        ///
        /// Three limits apply in turn, and which one binds is the whole behaviour of the block.
        /// Carnot sets the price of a kelvin; the pump's electrical rating caps what it can pay;
        /// and the cold node's remaining heat caps what there is to take. Against a small
        /// difference the rating binds and the pump runs flat out. Against a large one the price
        /// binds, the pump lifts a trickle, and pushing further costs more than the block can
        /// draw — which is what makes absolute zero unreachable rather than merely discouraged.
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

                // Never take more heat out of the cold node than it has above absolute zero: the
                // pump is bounded by what is there, not only by what it can afford.
                float headroom = (coldTemperature - ThermalConstants.MinimumTemperature)
                    * nodeThermalMass[cold] / h;
                if (headroom <= 0f) continue;

                // What it would draw on a healthy grid, recorded whether or not it got it — a
                // request that shrinks because it was refused never recovers.
                float wanted = Limit(coefficient * pump.MaxPowerWatts, pump.RatedWatts, headroom);
                pump.DemandEnergy += (wanted / coefficient) * h;

                float available = pump.MaxPowerWatts * Clamp01(pump.PowerAvailable);
                if (available <= 0f) continue;

                // What the compressor could pay for, against what the machine is rated to move,
                // against what there is left to take.
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

        /// <summary>The smallest of three limits on what a pump may move this substep.</summary>
        private static float Limit(float watts, float rating, float headroom)
        {
            if (watts > rating) watts = rating;
            if (watts > headroom) watts = headroom;
            return watts;
        }

        /// <summary>The air of the room a cell belongs to, or null when that room holds none.</summary>
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
        /// Sets how full of air a room is. The host owns this figure — the simulation has no way
        /// to know whether a compartment is pressurised — and setting it rebuilds the room's links,
        /// because a room at zero pressure has none.
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
        /// Advances the whole grid by <paramref name="deltaSeconds"/> of simulated time.
        /// </summary>
        /// <summary>
        /// Copies the node state the substep loop needs into flat arrays. Everything here is
        /// constant across a step: it changes when a block is built, damaged, exposed or
        /// re-powered, never between substeps.
        /// </summary>
        private void SyncNodeState()
        {
            // A node's index changes when the block list does, which invalidates every row.
            bool all = resyncAll;
            resyncAll = false;

            for (int i = 0; i < nodes.Count; i++)
            {
                ThermalNode node = nodes[i];

                // Temperature is the one value a host can change from outside a step — loading a
                // save, a grid split, conduction across a rotor — so it is always re-read.
                float temperature = node.Temperature;
                nodeTemperatures[i] = temperature;
                nodeStepStart[i] = temperature;

                if (!all && !node.StateDirty) continue;
                node.StateDirty = false;

                // Only a change of mass makes a cached reduced mass wrong, and only that forces
                // the pass over every link. A block that merely became exposed, or started
                // producing heat, leaves every factor on the grid correct.
                if (nodeThermalMass[i] != node.ThermalMass) linkMassFactorFrom = 0;

                nodeThermalMass[i] = node.ThermalMass;
                nodeRadiation[i] = node.RadiationCoefficient;
                nodeGeneration[i] = node.HeatGenerationWatts;
                nodeExposedArea[i] = node.ExposedArea;
                nodeEmissivity[i] = node.Thermal.Emissivity;

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

        private void Substep(float h, ref EnvironmentState env)
        {
            int nodeCount = nodes.Count;

            Array.Clear(nodeWatts, 0, nodeCount);
            for (int i = 0; i < loops.Count; i++)
            {
                loopWatts[i] = 0f;
            }
            for (int i = 0; i < roomAir.Count; i++)
            {
                roomWatts[i] = 0f;
            }

            AccumulateEnvironment(ref env, h);
            AccumulateConduction(h);
            AccumulateLoops(h);
            AccumulateRoomAir(h);
            AccumulateHeatPumps(h);

            ApplyWatts(h);
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
        /// Everything about an environment pass that is the same for every node: which mechanisms
        /// are switched on, the scalars they need, and the two directions resolved into per-face
        /// weights.
        ///
        /// Separated from the per-node work because the pass is no longer run in one go. A step is
        /// spread across the frames of its simulation window, so the node loop is entered many
        /// times per substep and this must be computed once per substep rather than once per
        /// slice — both for the cost and because <see cref="RefreshSunShadow"/> advances a budget
        /// of its own and would run many times faster than intended.
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

            /// <summary>True when nothing but waste heat has anything to add.</summary>
            public bool GenerationOnly;

            public float RadiationShare;
            public float FrictionScale;
        }

        /// <summary>
        /// The share of its exchanges a node may take over a substep of <paramref name="h"/>.
        ///
        /// A node is stable over a substep when <c>h * conductance &lt;= mass</c> — that is the
        /// condition the substep estimate is derived from. Where it holds this is one and nothing
        /// changes. Where it does not, scaling by the ratio is exactly the under-relaxation that
        /// makes the step behave as if it were short enough.
        /// </summary>
        private float RelaxationFactor(int node, float h)
        {
            if (!settings.ClampConductionOvershoot || h <= 0f) return 1f;

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
            plan.Diagnostics = CollectDiagnostics;

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

                // The one input to the precomputed rows that can move part way through a step.
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
                if (!environmentRowsValid || !PrecomputeEnvironment)
                {
                    for (int i = from; i < to; i++)
                    {
                        nodeRelaxation[i] = RelaxationFactor(i, h);
                    }
                }

                if (plan.Generating)
                {
                    for (int i = from; i < to; i++)
                    {
                        nodeWatts[i] += nodeGeneration[i];
                    }
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

            // The first substep of a step fills the rows below; every later one reads them. The
            // inputs — exposure, area, emissivity, the sun and wind directions, the step length —
            // are all fixed for the length of a step, so the two paths are the same arithmetic.
            bool fill = !environmentRowsValid || !PrecomputeEnvironment;

            for (int i = from; i < to; i++)
            {
                // Every node, exposed or buried: conduction needs this and conduction does not
                // care whether a block can see the sky. Computed here rather than in a pass of its
                // own because this loop already walks every node once per substep, and the
                // conduction pass that reads it does not start until this one has finished.
                if (fill) nodeRelaxation[i] = RelaxationFactor(i, h);

                float generation = generating ? nodeGeneration[i] : 0f;

                if (nodeExposedFaces[i] <= 0)
                {
                    if (fill)
                    {
                        nodeSolarRow[i] = 0f;
                        nodeFrictionRow[i] = 0f;
                        nodeConvectionRow[i] = 0f;
                    }

                    nodeWatts[i] += generation;
                    if (diagnostics) ClearEnvironmentDiagnostics(i);
                    continue;
                }

                float temperature = nodeTemperatures[i];
                float area = nodeExposedArea[i];
                float watts = generation;

                float radiationWatts = 0f;
                float convectionWatts = 0f;
                float solarWatts = 0f;
                float frictionWatts = 0f;

                if (fill)
                {
                    // A face in the airflow sheds more heat, but still air convects too. The
                    // factor is a property of the geometry and the wind, not of the temperature.
                    float windFactor = windy
                        ? 0.5f + (0.5f * Weighted(i, windWeights))
                        : 1f;

                    nodeConvectionRow[i] = convecting
                        ? -env.ConvectionCoefficient * area * windFactor
                        : 0f;

                    // Per face, and both terms are needed: how square the face is to the sun, and
                    // whether the ship is standing in front of that face.
                    nodeSolarRow[i] = solarEnabled
                        ? env.SolarEnergy * nodeEmissivity[i] * WeightedLit(i, sunWeights) * area
                        : 0f;

                    nodeFrictionRow[i] = frictionEnabled
                        ? frictionScale * area * Weighted(i, windWeights)
                        : 0f;
                }

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
                        // Radiation and convection both drive this node towards ambient, and the
                        // most they can legitimately do in one substep is arrive there — past it
                        // the exchange would have reversed. Capping at that is what keeps an
                        // explicit step bounded when the substep count has been refused; solar,
                        // friction and waste heat are sources rather than relaxation and are
                        // deliberately left out of it.
                        float capped = ClampRelaxation(relaxation,
                            (env.AmbientTemperature - temperature) * nodeThermalMass[i] * inverseH);

                        if (capped != relaxation)
                        {
                            // The per-mechanism figures are a readout of what actually happened,
                            // so they are scaled rather than left describing the unclamped want.
                            float scale = relaxation == 0f ? 0f : capped / relaxation;
                            radiationWatts *= scale;
                            convectionWatts *= scale;
                            relaxation = capped;
                        }
                    }

                    watts += relaxation;
                }

                if (solarEnabled)
                {
                    solarWatts = nodeSolarRow[i];
                    watts += solarWatts;
                }

                if (frictionEnabled)
                {
                    frictionWatts = nodeFrictionRow[i];
                    watts += frictionWatts;
                }

                nodeWatts[i] += watts;

                if (!diagnostics) continue;

                ThermalNode node = nodes[i];
                node.LastRadiationWatts = radiationWatts;
                node.LastConvectionWatts = convectionWatts;
                node.LastSolarWatts = solarWatts;
                node.LastFrictionWatts = frictionWatts;
                node.LastHeatSourceWatts = 0f;
            }

            // Only once the whole grid has been covered: the pass is sliced across frames, and a
            // partly filled row is not one a later substep may read.
            if (PrecomputeEnvironment && to >= nodes.Count) environmentRowsValid = true;
        }

        private void AccumulateEnvironment(ref EnvironmentState env, float h)
        {
            EnvironmentPlan plan = PlanEnvironment(ref env);
            AccumulateEnvironmentRange(ref env, ref plan, h, 0, nodes.Count);
            if (plan.SourcesEnabled) AccumulateHeatSources(ref env, plan.Diagnostics);
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
                    if (diagnostics) nodes[i].LastHeatSourceWatts += watts;
                }
            }
        }

        /// <summary>Intensity against a direction resolved into an explicit weight array.</summary>
        /// <summary>
        /// Keeps the grid's self-shadow current, and the per-node lit fractions with it.
        ///
        /// Both are rebuilt on the same trigger, because both depend on the same two things: where
        /// the sun is, and what the grid is made of. Between triggers this costs one dot product.
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

            // A block added or removed invalidates a pass in flight as much as it invalidates the
            // answer, because the walk reads the grid it started against.
            if (sunLitDirty || sunShadow.NeedsRestart(ref sunLocal, SunRebuildCosine))
            {
                sunShadow.Restart(grid, sunLocal, SunOccluders);
                sunLitDirty = false;
            }

            // Only a completed pass changes any answer, so the lit fractions are refreshed after
            // one completes and left alone otherwise.
            if (sunShadow.Step(SunShadowBudget)) BeginSunLit(-1f);

            // A slice here rather than at the top of the step, so a grid small enough for the
            // budget to cover it in one go finishes inside the same substep that completed the
            // pass — which is what it did before there was a budget, and what every test of the
            // shadow model asserts.
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
        /// <see cref="Weighted"/>, with each face's share scaled by how much of that face the sun
        /// actually reaches.
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

        private void AccumulateConduction(float h)
        {
            ClearConductionDiagnostics();
            AccumulateConductionRange(h, 0, links.Count);
        }

        /// <summary>
        /// Zeroes the per-node conduction figure before a substep accumulates into it.
        ///
        /// Hoisted out of the pass itself because the pass is now entered many times per substep,
        /// once per slice — clearing inside it would wipe what earlier slices had added and leave
        /// the readout showing only the last slice's links.
        /// </summary>
        private void ClearConductionDiagnostics()
        {
            if (!CollectDiagnostics) return;

            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i].LastConductionWatts = 0f;
            }
        }

        /// <summary>Runs the conduction pass over links <paramref name="from"/> to <paramref name="to"/>.</summary>
        private void AccumulateConductionRange(float h, int from, int to)
        {
            bool clamp = settings.ClampConductionOvershoot;
            bool diagnostics = CollectDiagnostics;

            if (!settings.EnableConduction) return;

            float inverseH = h > 0f ? 1f / h : 0f;

            // Hoisted so the loop reads locals rather than fields, and so the JIT can see the
            // lengths are loop-invariant.
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

                // One conductance, applied equally and oppositely: what leaves A enters B.
                float exchange = conductance[i] * difference;

                if (clamp)
                {
                    // The stricter of the two ends. Taking the smaller keeps the exchange equal
                    // and opposite — scaling a node's own total instead would bound it and stop
                    // conserving energy, which is the worse trade of the two.
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
                float loopTemperature = loop.Temperature;

                for (int i = 0; i < loop.Links.Count; i++)
                {
                    LoopLink link = loop.Links[i];
                    if (link.NodeIndex < 0 || link.NodeIndex >= nodes.Count) continue;

                    float difference = loopTemperature - nodeTemperatures[link.NodeIndex];
                    float watts = link.Conductance * difference;

                    if (clamp)
                    {
                        watts = ClampExchange(
                            watts, h, difference,
                            loop.ThermalMass,
                            nodeThermalMass[link.NodeIndex]);
                    }

                    nodeWatts[link.NodeIndex] += watts;
                    loopWatts[l] -= watts;
                }
            }
        }

        /// <summary>
        /// Exchanges heat between each sealed room's air and the surfaces bounding it.
        ///
        /// Identical in form to <see cref="AccumulateLoops"/> — a lumped mass against a set of
        /// nodes — because that is what it is. What differs is where the mass comes from: a
        /// coolant loop carries the fluid its definition declares, and a room carries however much
        /// air fits in it at the pressure the host reports.
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
        /// Limits a pairwise exchange to the energy that brings both sides to their shared
        /// equilibrium, so neither can overshoot the other however large the step is.
        ///
        /// This is what makes the integrator unconditionally bounded: substepping keeps the
        /// result accurate, and this keeps it sane when substepping alone is not enough.
        /// </summary>
        /// <summary>
        /// Caps a relaxation towards equilibrium at the watts that would exactly reach it.
        ///
        /// Both arguments carry their sign, and a cap only applies when the two agree: a node
        /// being warmed cannot be capped by a cooling limit. Where they disagree the exchange is
        /// already heading away from the limit and there is nothing to cap.
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

        public static float ClampExchange(float watts, float h, float difference, float massA, float massB)
        {
            if (h <= 0f) return watts;

            // energy that would equalise the pair exactly
            float combined = massA + massB;
            if (combined <= 0f) return watts;

            float maxEnergy = difference * (massA * massB / combined);
            float energy = watts * h;

            if (Math.Abs(energy) <= Math.Abs(maxEnergy)) return watts;
            return maxEnergy / h;
        }

        private void ApplyWatts(float h)
        {
            ApplyNodeWattsRange(h, 0, nodes.Count);
            ApplyCoupledWatts(h);
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

                // Only a node that is actually overheating touches its definition, so the
                // ordinary case stays inside the arrays.
                ThermalNode node = nodes[i];
                float critical = node.Thermal.CriticalTemperature;
                if (critical <= 0f || updated <= critical) continue;

                float damage = (updated - critical) * node.Thermal.CriticalTemperatureScaler;
                if (perSecond) damage *= h;
                if (damage > 0f)
                {
                    overheats.Add(new OverheatEvent(node.Block, updated, damage));
                }
            }
        }

        /// <summary>
        /// Applies accumulated watts to the coolant loops and the room air.
        ///
        /// Kept whole rather than sliced: there are a handful of each against tens of thousands of
        /// nodes, so slicing them would cost more bookkeeping than the work it spread.
        /// </summary>
        private void ApplyCoupledWatts(float h)
        {
            for (int l = 0; l < loops.Count; l++)
            {
                CoolantLoop loop = loops[l];
                float delta = loopWatts[l] * h / EffectiveLoopMass(l);
                float updated = loop.Temperature + delta;
                loop.Temperature = updated < ThermalConstants.MinimumTemperature
                    ? ThermalConstants.MinimumTemperature
                    : updated;
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
        /// <para>
        /// This is a question a host asks *before* deciding to step — whether the grid is
        /// affordable this frame, what the scheduler should skip — so it brings the mirrored node
        /// state up to date itself rather than assuming a step has already done it. Reading it
        /// straight off a solver that has never stepped divided conductance by a thermal mass of
        /// zero and answered infinity, which is the one answer a caller cannot act on.
        /// </para>
        /// </summary>
        public float RequiredSubsteps(float deltaSeconds)
        {
            RebuildLinksIfNeeded();
            EnsureBuffers();
            SyncNodeState();
            RecomputeConductanceTotalsIfNeeded();
            ApplyThermalMassFloor();

            return RequiredSubstepsFromState(deltaSeconds);
        }

        /// <summary>
        /// The estimate itself, over state the caller has already synchronised. <see cref="Step"/>
        /// uses this so that one step does not sync twice.
        /// </summary>
        /// <summary>
        /// The environment half of a node's stability demand, resolved once for the whole grid.
        ///
        /// Split out because three things now need it and they must not drift: the substep
        /// estimate, the thermal mass floor that bounds what that estimate can ask for, and the
        /// profile the telemetry reports. A floor computed from a different rate than the estimate
        /// reads would cap a number nobody is looking at.
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
            // Conductance totals cover links, coolant loops and room air alike, and are not
            // reduced when a mechanism is switched off: over-estimating stiffness costs a
            // substep, under-estimating it costs stability.
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

        private float RequiredSubstepsFromState(float deltaSeconds)
        {
            float worst = 0f;

            StabilityTerms terms = StabilityEnvironment();

            for (int i = 0; i < nodes.Count; i++)
            {
                float perNode = NodeStabilityRate(i, ref terms) / nodeThermalMass[i];
                if (perNode > worst) worst = perNode;
            }

            for (int l = 0; l < loops.Count; l++)
            {
                float perLoop = LoopConductance(l) / EffectiveLoopMass(l);
                if (perLoop > worst) worst = perLoop;
            }

            // Room air is the lightest mass on the grid and touches the most surface, so it is
            // usually what sets the substep count once a ship is pressurised.
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
        /// Everything about a grid's substep demand that a report might want, taken in one walk.
        ///
        /// <para>
        /// The substep count is a maximum over every element, so the single number the solver acts
        /// on says nothing about <em>why</em>. This says why: which element sets it, what the
        /// distribution behind it looks like, and what capping the demand at each of several
        /// values would do to both. It is the measurement that turns "set it to four" from a guess
        /// into a reading.
        /// </para>
        ///
        /// <para>
        /// Every demand here is computed from the block's <em>real</em> heat capacity, not from the
        /// floored one the solver may be integrating with, so a profile describes the grid rather
        /// than the settings in force and two configurations can be compared against each other.
        /// </para>
        ///
        /// <para>
        /// O(nodes), so it is meant for a report or a diagnostic rather than for a step.
        /// </para>
        /// </summary>
        public class SubstepProfile
        {
            /// <summary>Demand thresholds the node histogram counts against.</summary>
            public static readonly float[] DemandEdges =
                { 0.5f, 1f, 2f, 4f, 8f, 16f, 32f, 64f, 128f, 256f };

            /// <summary>
            /// Caps the projection evaluates.
            ///
            /// Dense at the bottom because that is where the decision is. Cost is linear in the
            /// cap, so every step down the list is worth as much again — but the population it
            /// reaches is not linear at all: on a field ship 4 raised six per cent of the blocks
            /// and 2 raised twenty-four, so the interesting question is which value sits on the
            /// knee, and it cannot be answered by a list that jumps over it.
            /// </summary>
            public static readonly int[] ProjectedCaps = { 32, 16, 8, 6, 4, 3, 2, 1 };

            public float StepSeconds;
            public int Nodes;
            public int Links;

            /// <summary>Substeps a full step would need, from real capacities and no cap.</summary>
            public float RequiredSubsteps;

            /// <summary>Substeps a full step needs as things actually stand, cap included.</summary>
            public float RequiredSubstepsInForce;

            public float WorstNodeDemand;
            public int WorstNodeIndex = -1;

            /// <summary>Share of the worst node's stability rate that is conduction rather than environment.</summary>
            public float WorstNodeConductionShare;

            /// <summary>The stiffest room air and coolant loop, which the block cap cannot reach.</summary>
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
            // Bringing the mirrored state up to date is exactly what must not happen while a step
            // is part way through one: a step spans many frames now, and SyncNodeState rewrites
            // the row the publish stage measures its change against. Observing the simulation has
            // to leave it where it found it, so mid-step the profile reads what the step is
            // already using rather than refreshing it.
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

            // The floor cannot reach room air or coolant loops, so whatever they demand is a
            // floor under every projection below.
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

            // A node appended during a step in flight has no mirrored row yet — it joins the next
            // step. Walking past the arrays for it would read another block's conductance.
            int count = nodes.Count;
            if (count > nodeConductanceTotal.Length) count = nodeConductanceTotal.Length;
            profile.Nodes = count;

            for (int i = 0; i < count; i++)
            {
                float rate = NodeStabilityRate(i, ref terms);
                float conduction = nodeConductanceTotal[i];

                // The block's own capacity, not the one the solver may be integrating with, so
                // the profile describes the ship rather than the settings.
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

            // The private estimate, not the public one: the public entry point synchronises first,
            // which is the thing this method must not do mid-step.
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
        /// Set when something structural changed the conductance a node sees, so the totals are
        /// recomputed once before they are next read rather than once per change.
        ///
        /// The distinction matters because the changes arrive in bursts. A ship pressurising is
        /// every room gaining air within a second or two, and each one used to drive its own pass
        /// over every link, every coolant loop and every room on the grid — so a station with two
        /// thousand compartments paid two thousand passes over two million links to learn what one
        /// pass would have told it.
        /// </summary>
        private bool conductanceTotalsDirty = true;

        private void RecomputeConductanceTotalsIfNeeded()
        {
            if (!conductanceTotalsDirty) return;
            RecomputeConductanceTotals();
        }

        /// <summary>
        /// Raises the mirrored heat capacity of any node whose conduction alone would demand more
        /// than <c>MaxSubstepsPerBlock</c> substeps of the whole grid.
        ///
        /// <para>
        /// A node is stable over a substep of <c>h</c> while <c>h * G &lt;= C</c>, so the substeps
        /// a node asks of a step of <c>dt</c> are <c>dt * G / (C * safety)</c>. Holding that at or
        /// below the cap is one rearrangement: <c>C &gt;= G * dt / (safety * cap)</c>. Nodes at or
        /// above it are untouched, which on a real hull is nearly all of them.
        /// </para>
        ///
        /// <para>
        /// Only the mirrored row moves. <c>ThermalNode.ThermalMass</c> keeps the block's real heat
        /// capacity, so the terminal readout, the energy figure and anything a host asks still
        /// describe the block rather than the approximation used to integrate it.
        /// </para>
        ///
        /// <para>
        /// Applied after the conductance totals and before the link mass factors, because it
        /// needs the first and invalidates the second.
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
                float floor = NodeStabilityRate(i, ref terms) * perRate;
                if (nodeThermalMass[i] >= floor) continue;

                nodeThermalMass[i] = floor;
                moved = true;
                FlooredNodes++;
            }

            // Every link's reduced mass is a function of the two capacities either side of it.
            if (moved) linkMassFactorFrom = 0;

            for (int l = 0; l < loops.Count; l++)
            {
                CoolantLoop loop = loops[l];
                float floor = LoopConductance(l) * perRate;
                loopEffectiveMass[l] = loop.ThermalMass < floor ? floor : loop.ThermalMass;
            }

            for (int r = 0; r < roomAir.Count; r++)
            {
                RoomAirNode air = roomAir[r];
                float floor = RoomConductance(r) * perRate;
                roomEffectiveMass[r] = air.ThermalMass < floor ? floor : air.ThermalMass;
            }
        }

        private float LoopConductance(int index)
        {
            CoolantLoop loop = loops[index];
            float total = 0f;
            for (int i = 0; i < loop.Links.Count; i++) total += loop.Links[i].Conductance;
            return total;
        }

        private float RoomConductance(int index)
        {
            RoomAirNode air = roomAir[index];
            if (!air.HasAir) return 0f;

            float total = 0f;
            for (int i = 0; i < air.Links.Count; i++) total += air.Links[i].Conductance;
            return total;
        }

        /// <summary>
        /// The capacity the integrator should use for a loop: its own, unless the cap raised it.
        ///
        /// Falls back to the real capacity rather than to zero, because the effective row is only
        /// filled while the cap is on and dividing by zero here would produce an infinite
        /// temperature rather than an obviously wrong one.
        /// </summary>
        private float EffectiveLoopMass(int index)
        {
            if (settings.MaxSubstepsPerBlock <= 0 || index >= loopEffectiveMass.Length
                || loopEffectiveMass[index] <= 0f)
            {
                return loops[index].ThermalMass;
            }

            return loopEffectiveMass[index];
        }

        private float EffectiveRoomMass(int index)
        {
            if (settings.MaxSubstepsPerBlock <= 0 || index >= roomEffectiveMass.Length
                || roomEffectiveMass[index] <= 0f)
            {
                return roomAir[index].ThermalMass;
            }

            return roomEffectiveMass[index];
        }

        /// <summary>Nodes the floor raised on the last step. Zero when the cap is off.</summary>
        public int FlooredNodes { get; private set; }

        private void RecomputeConductanceTotals()
        {
            Work.ConductanceRecomputes++;
            conductanceTotalsDirty = false;
            EnsureBuffers();
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

        /// <returns>
        /// True when the node buffers were reallocated, which throws away anything accumulated
        /// in them. Only <see cref="nodeConductanceTotal"/> is accumulated rather than rewritten
        /// each step, so only that has to be rebuilt — but it has to be, and silently returning
        /// void made an incremental caller unable to know.
        /// </returns>
        private bool EnsureBuffers()
        {
            bool grew = false;

            if (nodeWatts.Length < nodes.Count)
            {
                grew = true;

                // Growing reallocates, which zeroes the conductance totals — the one node array
                // that is accumulated across calls rather than rewritten every step. Everything
                // else here is refilled from the nodes by the resync below; this is not, so it
                // has to be marked for a recompute or every surviving node silently loses the
                // conductance it sees, and the substep estimate with it.
                conductanceTotalsDirty = true;
                // A quarter more, not double. Around fourteen arrays are indexed by node, so
                // doubling leaves a settled grid carrying a whole spare copy of each — about ten
                // megabytes on a 127,000-block ship, held for as long as the grid exists.
                // Doubling is the right policy for something appended to in a tight loop; these
                // grow when a block is placed, which is not that.
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
                nodeExposedFaces = new int[size];
                nodeRelaxation = new float[size];
                nodeSolarRow = new float[size];
                nodeFrictionRow = new float[size];
                nodeConvectionRow = new float[size];
                environmentRowsValid = false;
                nodeFaceWeights = new float[size * Face.Count];
                nodeSunLit = new float[size * Face.Count];
            }
            if (loopWatts.Length < loops.Count)
            {
                loopWatts = new float[Math.Max(4, loops.Count * 2)];
                loopEffectiveMass = new float[loopWatts.Length];
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

        /// <summary>The hottest node, or null when the grid has none.</summary>
        /// <summary>The hottest node as of the last step, kept by the step's own write-back.</summary>
        private int hottestNode = -1;

        /// <summary>
        /// The hottest block on the grid, or null when nothing has stepped yet.
        ///
        /// Answered from what the last step already worked out rather than by a fresh pass. The
        /// figure feeds the cockpit summary, the crosshair readout and the telemetry report; it
        /// was being recomputed every four steps for every client, which on a large grid is a
        /// walk over every node to answer a question the step had the numbers for.
        ///
        /// A host that changes a temperature from outside a step — loading a save, a grid split —
        /// gets an answer one step out of date, which is what every consumer of it already
        /// tolerates: it is refreshed on a cadence and displayed to a human.
        /// </summary>
        public ThermalNode HottestNode()
        {
            if (hottestNode < 0 || hottestNode >= nodes.Count)
            {
                // Nothing has stepped, or the node list shrank under it. Fall back to a walk so
                // the answer is never simply wrong.
                ThermalNode found = null;
                for (int i = 0; i < nodes.Count; i++)
                {
                    if (found == null || nodes[i].Temperature > found.Temperature) found = nodes[i];
                }
                return found;
            }

            return nodes[hottestNode];
        }

        /// <summary>Sets every node to one temperature. Test and load helper.</summary>
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
