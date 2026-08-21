using Thermodynamics.Core;
using VRage.Game;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// Everything observed about one block definition across the whole session, on every grid.
    ///
    /// The balance-facing half of the telemetry: what temperature a definition settles at, how
    /// often it reaches its critical temperature, and what effect its declared properties have.
    /// </summary>
    public class BlockTypeTelemetry
    {
        public readonly MyDefinitionId DefinitionId;
        public readonly string Name;

        /// <summary>
        /// The thermal properties in force for this block type, captured from the first block created.
        /// Written into the report so a run's figures can be read against the values that produced
        /// them.
        /// </summary>
        public BlockThermalProperties Definition;

        /// <summary>Size in cells, from the block definition.</summary>
        public Vector3ITriple Size;

        public long Placed;
        public long Removed;
        public long Live;
        public long PeakLive;

        /// <summary>Node observations that fed the sampled stats below. Strided.</summary>
        public long SampledUpdates;

        /// <summary>Every node observation, whether it fed the wide stats or not.</summary>
        public long TotalUpdates;

        public readonly RunningStat Temperature = new RunningStat();
        public readonly RunningStat DeltaTemperature = new RunningStat();
        public readonly RunningStat ConductionWatts = new RunningStat();
        public readonly RunningStat RadiationWatts = new RunningStat();
        public readonly RunningStat ConvectionWatts = new RunningStat();
        public readonly RunningStat SolarWatts = new RunningStat();
        public readonly RunningStat FrictionWatts = new RunningStat();
        public readonly RunningStat HeatGeneration = new RunningStat();
        public readonly RunningStat EnergyProduction = new RunningStat();
        public readonly RunningStat EnergyConsumption = new RunningStat();
        public readonly RunningStat ThrustConsumption = new RunningStat();

        public readonly RunningStat Mass = new RunningStat();
        public readonly RunningStat ThermalMass = new RunningStat();
        public readonly RunningStat ExposedSurfaces = new RunningStat();
        public readonly RunningStat ExposedSurfaceArea = new RunningStat();

        /// <summary>Final temperature of every live block of this type, in one pass at shutdown.</summary>
        public readonly Histogram FinalTemperatures = new Histogram(Histogram.TemperatureEdges());
        public readonly Histogram SampledTemperatures = new Histogram(Histogram.TemperatureEdges());

        public float PeakTemperature = float.MinValue;
        public long PeakTemperatureGrid;

        /// <summary>
        /// Substeps one block of this type would need for a full step on its own, from its real heat
        /// capacity and everything it is coupled to.
        ///
        /// A grid takes as many substeps as its stiffest block demands, so this identifies which
        /// definitions are expensive to place. Demand is not a property of the definition alone — it
        /// depends on what the block is mounted to and whether it is exposed — so it is recorded as a
        /// distribution, of which the maximum sets the grid.
        /// </summary>
        public readonly RunningStat SubstepDemand = new RunningStat();

        public float PeakSubstepDemand;
        public long PeakSubstepDemandGrid;
        public string PeakSubstepDemandPosition = "";

        /// <summary>
        /// How much of each of the definition's six faces seals, read once from the block model.
        ///
        /// This is what the room mapper walks, and so the first figure to check when a hull that is
        /// airtight in game maps as open space: a definition whose pressurisation could not be read
        /// appears here as zeroes.
        /// </summary>
        public float[] SealFractionByFace;

        /// <summary>The same, for mount surfaces, which is what conduction walks.</summary>
        public float[] MountFractionByFace;

        /// <summary>Observations where the block's own state suppressed its sealing, such as an open door.</summary>
        public long UnsealedByDoorState;

        public bool HasSurfaceProfile
        {
            get { return SealFractionByFace != null; }
        }

        /// <summary>Faces of the definition that seal completely.</summary>
        public int FullySealingFaces
        {
            get
            {
                if (SealFractionByFace == null) return 0;
                int count = 0;
                for (int i = 0; i < SealFractionByFace.Length; i++)
                {
                    if (SealFractionByFace[i] >= 1f) count++;
                }
                return count;
            }
        }

        /// <summary>Faces of the definition carrying any mount surface.</summary>
        public int MountingFaces
        {
            get
            {
                if (MountFractionByFace == null) return 0;
                int count = 0;
                for (int i = 0; i < MountFractionByFace.Length; i++)
                {
                    if (MountFractionByFace[i] > 0f) count++;
                }
                return count;
            }
        }

        /// <summary>Overheat events, i.e. observations that dealt heat damage.</summary>
        public long CriticalUpdates;
        public double TotalDamage;

        public BlockTypeTelemetry(MyDefinitionId id)
        {
            DefinitionId = id;
            Name = id.SubtypeName;
            if (string.IsNullOrEmpty(Name)) Name = id.TypeId.ToString();
        }

        public void OnPlaced(ThermalBlock block)
        {
            Placed++;
            Live++;
            if (Live > PeakLive) PeakLive = Live;

            if (Definition == null && block.Instance != null)
            {
                Definition = block.Instance.Thermal;
                Vector3I size = block.Instance.Model.Size;
                Size = new Vector3ITriple(size.X, size.Y, size.Z);
                CaptureSurfaceProfile(block.Instance.Model);
            }

            Mass.Add(block.Instance.Mass);
        }

        public void OnRemoved()
        {
            Removed++;
            if (Live > 0) Live--;
        }

        /// <summary>
        /// The cheap per-observation path: a compare and two increments. The full statistics are in
        /// <see cref="Sample"/>.
        /// </summary>
        public void OnUpdate(ThermalNode node, long gridId)
        {
            TotalUpdates++;
            NotePeak(node.Temperature, gridId);
        }

        /// <summary>
        /// The hottest this type has been, without counting an update.
        ///
        /// The peak and the temperature range are fed by different passes: the peak by the strided
        /// sampler, the range by that sampler *and* by the end-of-session sweep that guarantees one
        /// observation of every block on every grid. A type the sampler never reached therefore
        /// reported a maximum above its own peak — 327 of 815 types in the 2026-08-20 fleet dump,
        /// most of them at the 293.15 K blocks are created at.
        /// </summary>
        public void NotePeak(float temperature, long gridId)
        {
            if (temperature <= PeakTemperature) return;

            PeakTemperature = temperature;
            PeakTemperatureGrid = gridId;
        }

        /// <summary>
        /// Records what one block of this type demanded of its grid's step.
        ///
        /// Separate from <see cref="Sample"/> because it reads the solver rather than the node: the
        /// coupling that makes a block stiff lives in the conduction graph.
        /// </summary>
        public void SampleSubstepDemand(float demand, ThermalNode node, long gridId)
        {
            if (demand <= 0f) return;

            SubstepDemand.Add(demand);

            if (demand <= PeakSubstepDemand) return;

            PeakSubstepDemand = demand;
            PeakSubstepDemandGrid = gridId;
            PeakSubstepDemandPosition = node == null || node.Block == null
                ? ""
                : node.Block.Position.ToString();
        }

        public void Sample(ThermalNode node)
        {
            SampledUpdates++;

            Temperature.Add(node.Temperature);
            SampledTemperatures.Add(node.Temperature);
            DeltaTemperature.Add(node.LastDeltaTemperature);
            ConductionWatts.Add(node.LastConductionWatts);
            RadiationWatts.Add(node.LastRadiationWatts);
            ConvectionWatts.Add(node.LastConvectionWatts);
            SolarWatts.Add(node.LastSolarWatts);
            FrictionWatts.Add(node.LastFrictionWatts);
            HeatGeneration.Add(node.HeatGenerationWatts);

            BlockInstance block = node.Block;
            EnergyProduction.Add(block.PowerProducedWatts);
            EnergyConsumption.Add(block.PowerConsumedWatts);
            ThrustConsumption.Add(block.ThrustWatts);
            Mass.Add(block.Mass);

            ThermalMass.Add(node.ThermalMass);
            ExposedSurfaces.Add(node.TotalExposedFaces);
            ExposedSurfaceArea.Add(node.ExposedArea);

            if (!block.IsSealedByDoorState) UnsealedByDoorState++;
        }

        /// <summary>
        /// Reads the definition's per-face sealing and mounting from the shared block model. Once per
        /// type, from the first block placed; the model is immutable, so a second read would return
        /// the same values.
        /// </summary>
        private void CaptureSurfaceProfile(BlockModel model)
        {
            if (model == null) return;

            float[] seal = new float[Face.Count];
            float[] mount = new float[Face.Count];
            for (int face = 0; face < Face.Count; face++)
            {
                seal[face] = model.LocalFaceSealFraction(face);
                mount[face] = model.LocalFaceMountFraction(face);
            }

            MountFractionByFace = mount;
            SealFractionByFace = seal;
        }

        public void OnCriticalDamage(float damage)
        {
            CriticalUpdates++;
            TotalDamage += damage;
        }

        public void OnFinalTemperature(float temperature)
        {
            FinalTemperatures.Add(temperature);
        }
    }

    /// <summary>
    /// A block size, held without a dependency on VRageMath so the report formatting stays in the
    /// game-free half of the module.
    /// </summary>
    public struct Vector3ITriple
    {
        public int X;
        public int Y;
        public int Z;

        public Vector3ITriple(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public override string ToString()
        {
            return X + "x" + Y + "x" + Z;
        }
    }
}
