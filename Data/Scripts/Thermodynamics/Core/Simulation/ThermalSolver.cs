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
        private float[] nodeConductanceTotal = new float[0];
        private float[] loopWatts = new float[0];

        private readonly List<OverheatEvent> overheats = new List<OverheatEvent>();
        private readonly int[] exposureScratch = new int[Face.Count];
        private readonly List<BlockInstance> neighbourScratch = new List<BlockInstance>();

        private IBlockAdjacency adjacency;

        private bool linksDirty = true;

        public ThermalSolver(ThermalSettings settings, GridModel grid, SurfaceMap surfaces)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            if (grid == null) throw new ArgumentNullException("grid");
            if (surfaces == null) throw new ArgumentNullException("surfaces");

            this.settings = settings;
            this.grid = grid;
            this.surfaces = surfaces;
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

            ThermalNode node = new ThermalNode(block, grid.GridSize, initialTemperature);
            node.Index = nodes.Count;
            nodes.Add(node);
            nodesByKey[block.Key] = node;
            linksDirty = true;
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
                nodes[i].LinkIndices.Clear();
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

                    int linkIndex = links.Count;
                    links.Add(new ThermalLink(a.Index, b.Index, conductance, contacts));
                    a.LinkIndices.Add(linkIndex);
                    b.LinkIndices.Add(linkIndex);
                }
            }

            linksDirty = false;
            EnsureBuffers();
            RecomputeConductanceTotals();
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

            Environment = environment;
            overheats.Clear();

            int substeps = ChooseSubsteps(deltaSeconds);
            LastSubsteps = substeps;
            LastStepWasClamped = substeps >= MaxSubsteps && RequiredSubsteps(deltaSeconds) > MaxSubsteps;

            float h = deltaSeconds / substeps;
            for (int s = 0; s < substeps; s++)
            {
                Substep(h, ref environment);
            }

            StepCount++;
        }

        private void Substep(float h, ref EnvironmentState env)
        {
            int nodeCount = nodes.Count;

            for (int i = 0; i < nodeCount; i++)
            {
                nodeWatts[i] = 0f;
                nodeTemperatures[i] = nodes[i].Temperature;
            }
            for (int i = 0; i < loops.Count; i++)
            {
                loopWatts[i] = 0f;
            }

            AccumulateEnvironment(ref env);
            AccumulateConduction(h);
            AccumulateLoops(h);

            ApplyWatts(h);
        }

        private void AccumulateEnvironment(ref EnvironmentState env)
        {
            bool environmentEnabled = settings.EnableEnvironment;
            bool solarEnabled = settings.EnableSolarHeat && !env.IsSolarOccluded && env.SolarEnergy > 0f;
            bool frictionEnabled = env.FrictionActive;

            for (int i = 0; i < nodes.Count; i++)
            {
                ThermalNode node = nodes[i];
                float temperature = nodeTemperatures[i];
                float watts = 0f;

                node.LastRadiationWatts = 0f;
                node.LastConvectionWatts = 0f;
                node.LastSolarWatts = 0f;
                node.LastFrictionWatts = 0f;

                if (environmentEnabled && node.TotalExposedFaces > 0)
                {
                    float squared = temperature * temperature;
                    float radiation = -node.RadiationCoefficient * ((squared * squared) - env.AmbientTemperaturePow4);

                    float convection = 0f;
                    if (env.AtmosphereFactor > 0f && env.ConvectionCoefficient > 0f)
                    {
                        float windFactor = 1f;
                        if (env.WindSpeed > 0f)
                        {
                            Vector3 wind = env.WindDirectionLocal;
                            // A face in the airflow sheds more heat, but still air convects too.
                            windFactor = 0.5f + (0.5f * node.DirectionalIntensity(ref wind));
                        }

                        convection = -env.ConvectionCoefficient * node.ExposedArea * windFactor
                            * (temperature - env.AmbientTemperature);
                    }

                    float blended = ((1f - env.AtmosphereFactor) * radiation) + (env.AtmosphereFactor * convection);
                    node.LastRadiationWatts = (1f - env.AtmosphereFactor) * radiation;
                    node.LastConvectionWatts = env.AtmosphereFactor * convection;
                    watts += blended;
                }

                if (solarEnabled && node.TotalExposedFaces > 0)
                {
                    Vector3 sun = env.SunDirectionLocal;
                    float intensity = node.DirectionalIntensity(ref sun);
                    float solar = env.SolarEnergy * node.Thermal.Emissivity * intensity * node.ExposedArea;
                    node.LastSolarWatts = solar;
                    watts += solar;
                }

                if (frictionEnabled && node.TotalExposedFaces > 0)
                {
                    Vector3 wind = env.WindDirectionLocal;
                    float directional = node.DirectionalIntensity(ref wind);
                    float v = env.WindSpeed;
                    float friction = settings.FrictionScale * env.AirDensity * (v * v * v)
                        * node.ExposedArea * directional;
                    node.LastFrictionWatts = friction;
                    watts += friction;
                }

                watts += node.HeatGenerationWatts;
                nodeWatts[i] += watts;
            }
        }

        private void AccumulateConduction(float h)
        {
            bool clamp = settings.ClampConductionOvershoot;

            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i].LastConductionWatts = 0f;
            }

            for (int i = 0; i < links.Count; i++)
            {
                ThermalLink link = links[i];
                float difference = nodeTemperatures[link.NodeB] - nodeTemperatures[link.NodeA];
                if (difference == 0f) continue;

                // One conductance, applied equally and oppositely: what leaves A enters B.
                float watts = link.Conductance * difference;

                if (clamp)
                {
                    watts = ClampExchange(
                        watts, h, difference,
                        nodes[link.NodeA].ThermalMass,
                        nodes[link.NodeB].ThermalMass);
                }

                nodeWatts[link.NodeA] += watts;
                nodeWatts[link.NodeB] -= watts;

                nodes[link.NodeA].LastConductionWatts += watts;
                nodes[link.NodeB].LastConductionWatts -= watts;
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
                            nodes[link.NodeIndex].ThermalMass);
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

            for (int i = 0; i < nodes.Count; i++)
            {
                ThermalNode node = nodes[i];

                float delta = nodeWatts[i] * h / node.ThermalMass;
                float updated = node.Temperature + delta;
                if (updated < ThermalConstants.MinimumTemperature)
                {
                    updated = ThermalConstants.MinimumTemperature;
                }

                node.LastDeltaTemperature = updated - node.Temperature;
                node.Temperature = updated;

                if (damageEnabled)
                {
                    float critical = node.Thermal.CriticalTemperature;
                    if (critical > 0f && updated > critical)
                    {
                        float overshoot = updated - critical;
                        float damage = overshoot * node.Thermal.CriticalTemperatureScaler;
                        if (settings.DamageIsPerSecond) damage *= h;
                        if (damage > 0f)
                        {
                            overheats.Add(new OverheatEvent(node.Block, updated, damage));
                        }
                    }
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
        /// </summary>
        public float RequiredSubsteps(float deltaSeconds)
        {
            float worst = 0f;

            for (int i = 0; i < nodes.Count; i++)
            {
                ThermalNode node = nodes[i];
                float rate = nodeConductanceTotal[i];

                // Linearised environment coupling: d(radiated watts)/dT = 4 e s A T^3
                if (settings.EnableEnvironment && node.TotalExposedFaces > 0)
                {
                    float t = node.Temperature;
                    rate += 4f * node.RadiationCoefficient * t * t * t;
                    rate += Environment.ConvectionCoefficient * node.ExposedArea * Environment.AtmosphereFactor;
                }

                float perNode = rate / node.ThermalMass;
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

        private int ChooseSubsteps(float deltaSeconds)
        {
            float required = RequiredSubsteps(deltaSeconds);
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
                nodeConductanceTotal = new float[size];
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
