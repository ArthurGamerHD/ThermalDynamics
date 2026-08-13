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
    public class ThermalSolver
    {
        /// <summary>Fraction of the theoretical stability limit a substep is allowed to use.</summary>
        public const float StabilitySafetyFactor = 0.5f;

        /// <summary>Upper bound on substeps per call, so a pathological grid cannot stall a frame.</summary>
        public int MaxSubsteps = 16;

        private readonly ThermalSettings settings;
        private readonly GridModel grid;
        private readonly SurfaceMap surfaces;

        private readonly List<ThermalNode> nodes = new List<ThermalNode>();
        private readonly Dictionary<long, ThermalNode> nodesByKey = new Dictionary<long, ThermalNode>();
        private readonly List<ThermalLink> links = new List<ThermalLink>();
        private readonly List<CoolantLoop> loops = new List<CoolantLoop>();

        private float[] nodeWatts = new float[0];
        private float[] nodeTemperatures = new float[0];

        /// <summary>
        /// Temperature at the top of the step, kept so the reported change is the change over the
        /// whole step rather than over its last substep.
        /// </summary>
        private float[] nodeStepStart = new float[0];

        private float[] nodeConductanceTotal = new float[0];
        private float[] loopWatts = new float[0];

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
        /// Reduced thermal mass of each link, <c>mA*mB/(mA+mB)</c>. This is the only part of the
        /// overshoot clamp that depends on anything but the current temperatures, and it changes
        /// only when a block's mass does, so it is cached rather than divided out per link per
        /// substep.
        /// </summary>
        private float[] linkMassFactor = new float[0];
        private bool linkMassFactorDirty = true;

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

        /// <summary>
        /// Record every mechanism's contribution to every node, for the debug readout and the
        /// telemetry report.
        ///
        /// Off by default. The figures are five floats per node per substep that nothing in the
        /// simulation itself reads, so a server nobody is watching should not be writing them.
        /// </summary>
        public bool CollectDiagnostics;

        private readonly List<OverheatEvent> overheats = new List<OverheatEvent>();
        private readonly int[] exposureScratch = new int[Face.Count];
        private readonly List<BlockInstance> neighbourScratch = new List<BlockInstance>();

        private IBlockAdjacency adjacency;

        private bool linksDirty = true;

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

        public IList<ThermalLink> Links
        {
            get { RebuildLinksIfNeeded(); return links; }
        }

        public IList<CoolantLoop> Loops
        {
            get { return loops; }
        }

        /// <summary>Blocks that took heat damage during the last <see cref="Step"/>.</summary>
        public IList<OverheatEvent> Overheats
        {
            get { return overheats; }
        }

        /// <summary>Substeps the last <see cref="Step"/> needed for stability.</summary>
        public int LastSubsteps { get; private set; }

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
            linksDirty = true;
            resyncAll = true;
            return node;
        }

        public bool RemoveBlock(BlockInstance block)
        {
            if (block == null) return false;

            ThermalNode node;
            if (!nodesByKey.TryGetValue(block.Key, out node)) return false;

            nodesByKey.Remove(block.Key);
            nodes.RemoveAt(node.Index);
            for (int i = node.Index; i < nodes.Count; i++)
            {
                nodes[i].Index = i;
            }

            linksDirty = true;
            resyncAll = true;
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

        private void RebuildLinksIfNeeded()
        {
            if (!linksDirty) return;
            RebuildLinks();
        }

        /// <summary>
        /// Rebuilds every conduction link. O(cells * 6); called when the block layout changes,
        /// not per step.
        /// </summary>
        public void RebuildLinks()
        {
            links.Clear();
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
                    a.LinkCount++;
                    b.LinkCount++;
                }
            }

            linksDirty = false;
            linkMassFactorDirty = true;
            SyncLinkArrays();
            EnsureBuffers();
            RecomputeConductanceTotals();
        }

        /// <summary>Copies the link fields the substep loop reads into flat arrays.</summary>
        private void SyncLinkArrays()
        {
            if (linkA.Length < links.Count)
            {
                int size = Math.Max(16, links.Count * 2);
                linkA = new int[size];
                linkB = new int[size];
                linkConductance = new float[size];
            }

            for (int i = 0; i < links.Count; i++)
            {
                ThermalLink link = links[i];
                linkA[i] = link.NodeA;
                linkB[i] = link.NodeB;
                linkConductance[i] = link.Conductance;
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
            RecomputeConductanceTotals();
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
            for (int i = 0; i < nodes.Count; i++)
            {
                ThermalNode node = nodes[i];
                surfaces.GetExposedFaces(node.Block, rooms, exposureScratch);
                for (int f = 0; f < Face.Count; f++)
                {
                    node.ExposedFaces[f] = exposureScratch[f];
                }
                node.RefreshExposure();
            }
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
        public void Step(float deltaSeconds, EnvironmentState environment)
        {
            if (deltaSeconds <= 0f) return;

            RebuildLinksIfNeeded();
            EnsureBuffers();
            SyncNodeState();
            RefreshLinkMassFactors();

            Environment = environment;
            overheats.Clear();

            // One estimate, used for both answers. It walks every node cubing a temperature, so
            // asking twice per step doubled the cost of the cheapest thing the solver does for no
            // new information.
            float required = RequiredSubstepsFromState(deltaSeconds);
            int substeps = ClampSubsteps(required);
            LastSubsteps = substeps;
            LastStepWasClamped = required > MaxSubsteps;

            float h = deltaSeconds / substeps;
            for (int s = 0; s < substeps; s++)
            {
                Substep(h, ref environment);
            }

            // The node objects stay the public face of the simulation, so they are brought back
            // into agreement with the arrays once per step rather than once per substep.
            //
            // The reported change is measured against the top of the step, not against the last
            // substep. Everything that reads it — the HUD's rate of change, the anomaly
            // classifier recovering the previous temperature, the per-type distribution — is
            // describing one step; a six-substep grid otherwise reports roughly a sixth of the
            // movement it actually made.
            for (int i = 0; i < nodes.Count; i++)
            {
                float updated = nodeTemperatures[i];
                ThermalNode node = nodes[i];
                node.Temperature = updated;
                node.LastDeltaTemperature = updated - nodeStepStart[i];
            }

            StepCount++;
        }

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
                linkMassFactorDirty = true;

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

        /// <summary>Recomputes the cached per-link reduced mass after any mass change.</summary>
        private void RefreshLinkMassFactors()
        {
            if (!linkMassFactorDirty) return;
            linkMassFactorDirty = false;

            if (linkMassFactor.Length < links.Count)
            {
                linkMassFactor = new float[Math.Max(16, links.Count * 2)];
            }

            for (int i = 0; i < links.Count; i++)
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

            AccumulateEnvironment(ref env);
            AccumulateConduction(h);
            AccumulateLoops(h);

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

        private void AccumulateEnvironment(ref EnvironmentState env)
        {
            bool environmentEnabled = settings.EnableEnvironment;
            bool solarEnabled = settings.EnableSolarHeat && !env.IsSolarOccluded && env.SolarEnergy > 0f;
            bool frictionEnabled = env.FrictionActive;
            bool diagnostics = CollectDiagnostics;

            if (!environmentEnabled && !solarEnabled && !frictionEnabled)
            {
                // Nothing but waste heat to add, so the whole exposure pass is skipped.
                for (int i = 0; i < nodes.Count; i++)
                {
                    nodeWatts[i] += nodeGeneration[i];
                }
                if (diagnostics) ClearEnvironmentDiagnostics();
                return;
            }

            bool convecting = environmentEnabled && env.AtmosphereFactor > 0f && env.ConvectionCoefficient > 0f;
            bool windy = convecting && env.WindSpeed > 0f;

            float radiationShare = 1f - env.AtmosphereFactor;
            float airCubed = env.WindSpeed * env.WindSpeed * env.WindSpeed;
            float frictionScale = settings.FrictionScale * env.AirDensity * airCubed;

            // The two directions a node can be weighted against are the same for the whole grid,
            // so they are resolved once here rather than per node.
            if (windy || frictionEnabled)
            {
                Vector3 wind = env.WindDirectionLocal;
                ResolveDirection(ref wind, windWeights);
            }

            if (solarEnabled)
            {
                Vector3 sun = env.SunDirectionLocal;
                ResolveDirection(ref sun, sunWeights);
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                float generation = nodeGeneration[i];

                if (nodeExposedFaces[i] <= 0)
                {
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

                if (environmentEnabled)
                {
                    float squared = temperature * temperature;
                    float radiation = -nodeRadiation[i] * ((squared * squared) - env.AmbientTemperaturePow4);

                    float convection = 0f;
                    if (convecting)
                    {
                        // A face in the airflow sheds more heat, but still air convects too.
                        float windFactor = windy
                            ? 0.5f + (0.5f * Weighted(i, windWeights))
                            : 1f;

                        convection = -env.ConvectionCoefficient * area * windFactor
                            * (temperature - env.AmbientTemperature);
                    }

                    radiationWatts = radiationShare * radiation;
                    convectionWatts = env.AtmosphereFactor * convection;
                    watts += radiationWatts + convectionWatts;
                }

                if (solarEnabled)
                {
                    solarWatts = env.SolarEnergy * nodeEmissivity[i] * Weighted(i, sunWeights) * area;
                    watts += solarWatts;
                }

                if (frictionEnabled)
                {
                    frictionWatts = frictionScale * area * Weighted(i, windWeights);
                    watts += frictionWatts;
                }

                nodeWatts[i] += watts;

                if (!diagnostics) continue;

                ThermalNode node = nodes[i];
                node.LastRadiationWatts = radiationWatts;
                node.LastConvectionWatts = convectionWatts;
                node.LastSolarWatts = solarWatts;
                node.LastFrictionWatts = frictionWatts;
            }
        }

        /// <summary>Intensity against a direction resolved into an explicit weight array.</summary>
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
        }

        private void AccumulateConduction(float h)
        {
            bool clamp = settings.ClampConductionOvershoot;
            bool diagnostics = CollectDiagnostics;

            if (diagnostics)
            {
                for (int i = 0; i < nodes.Count; i++)
                {
                    nodes[i].LastConductionWatts = 0f;
                }
            }

            float inverseH = h > 0f ? 1f / h : 0f;

            // Hoisted so the loop reads locals rather than fields, and so the JIT can see the
            // lengths are loop-invariant.
            int count = links.Count;
            int[] fromIndex = linkA;
            int[] toIndex = linkB;
            float[] conductance = linkConductance;
            float[] massFactor = linkMassFactor;
            float[] temperatures = nodeTemperatures;
            float[] watts = nodeWatts;

            for (int i = 0; i < count; i++)
            {
                int a = fromIndex[i];
                int b = toIndex[i];

                float difference = temperatures[b] - temperatures[a];
                if (difference == 0f) continue;

                // One conductance, applied equally and oppositely: what leaves A enters B.
                float exchange = conductance[i] * difference;

                if (clamp)
                {
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
        /// Limits a pairwise exchange to the energy that brings both sides to their shared
        /// equilibrium, so neither can overshoot the other however large the step is.
        ///
        /// This is what makes the integrator unconditionally bounded: substepping keeps the
        /// result accurate, and this keeps it sane when substepping alone is not enough.
        /// </summary>
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
            bool damageEnabled = settings.EnableDamage;

            bool perSecond = settings.DamageIsPerSecond;

            for (int i = 0; i < nodes.Count; i++)
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

            for (int l = 0; l < loops.Count; l++)
            {
                CoolantLoop loop = loops[l];
                float delta = loopWatts[l] * h / loop.ThermalMass;
                float updated = loop.Temperature + delta;
                loop.Temperature = updated < ThermalConstants.MinimumTemperature
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

            return RequiredSubstepsFromState(deltaSeconds);
        }

        /// <summary>
        /// The estimate itself, over state the caller has already synchronised. <see cref="Step"/>
        /// uses this so that one step does not sync twice.
        /// </summary>
        private float RequiredSubstepsFromState(float deltaSeconds)
        {
            float worst = 0f;

            bool environmentEnabled = settings.EnableEnvironment;
            float convection = Environment.ConvectionCoefficient * Environment.AtmosphereFactor;

            for (int i = 0; i < nodes.Count; i++)
            {
                float rate = nodeConductanceTotal[i];

                // Linearised environment coupling: d(radiated watts)/dT = 4 e s A T^3
                if (environmentEnabled && nodeExposedFaces[i] > 0)
                {
                    float t = nodeTemperatures[i];
                    rate += 4f * nodeRadiation[i] * t * t * t;
                    rate += convection * nodeExposedArea[i];
                }

                float perNode = rate / nodeThermalMass[i];
                if (perNode > worst) worst = perNode;
            }

            for (int l = 0; l < loops.Count; l++)
            {
                CoolantLoop loop = loops[l];
                float total = 0f;
                for (int i = 0; i < loop.Links.Count; i++)
                {
                    total += loop.Links[i].Conductance;
                }
                float perLoop = total / loop.ThermalMass;
                if (perLoop > worst) worst = perLoop;
            }

            if (worst <= 0f) return 1f;
            return (deltaSeconds * worst) / StabilitySafetyFactor;
        }

        /// <summary>Rounds a substep estimate up into the allowed range.</summary>
        private int ClampSubsteps(float required)
        {
            if (required <= 1f) return 1;

            int substeps = (int)Math.Ceiling(required);
            if (substeps > MaxSubsteps) substeps = MaxSubsteps;
            return substeps;
        }

        private void RecomputeConductanceTotals()
        {
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
        }

        private void EnsureBuffers()
        {
            if (nodeWatts.Length < nodes.Count)
            {
                int size = Math.Max(16, nodes.Count * 2);
                nodeWatts = new float[size];
                nodeTemperatures = new float[size];
                nodeStepStart = new float[size];
                nodeConductanceTotal = new float[size];
                resyncAll = true;
                nodeThermalMass = new float[size];
                nodeRadiation = new float[size];
                nodeGeneration = new float[size];
                nodeExposedArea = new float[size];
                nodeEmissivity = new float[size];
                nodeExposedFaces = new int[size];
                nodeFaceWeights = new float[size * Face.Count];
            }
            if (loopWatts.Length < loops.Count)
            {
                loopWatts = new float[Math.Max(4, loops.Count * 2)];
            }
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
                return total;
            }
        }

        /// <summary>The hottest node, or null when the grid has none.</summary>
        public ThermalNode HottestNode()
        {
            ThermalNode hottest = null;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (hottest == null || nodes[i].Temperature > hottest.Temperature)
                {
                    hottest = nodes[i];
                }
            }
            return hottest;
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
