using Thermodynamics.Core;
using VRage.Game;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// Everything observed about one block definition across the whole session, on every grid.
    ///
    /// This is the balance-facing half of the telemetry: it answers "what temperature does a
    /// battery actually sit at", "which block reaches its critical temperature and how often",
    /// and "is this definition's SpecificHeat doing anything" — the questions a retune of
    /// Cubes.xml needs answered with numbers instead of guesses.
    /// </summary>
    public class BlockTypeTelemetry
    {
        public readonly MyDefinitionId DefinitionId;
        public readonly string Name;

        /// <summary>
        /// The thermal properties in force for this block type, captured from the first block
        /// created. Written into the report so a run's numbers can be read against the values
        /// that produced them.
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
        /// How much of each of the definition's six faces seals, read once from the block model.
        ///
        /// This is what the room mapper walks, so it is the first thing to look at when a hull
        /// that is plainly airtight in game maps as open space: a definition the adapter could not
        /// read the pressurisation of shows up here as zeroes.
        /// </summary>
        public float[] SealFractionByFace;

        /// <summary>The same, for mount surfaces, which is what conduction walks.</summary>
        public float[] MountFractionByFace;

        /// <summary>Observations where the block's own state had suppressed its sealing: an open door.</summary>
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

        /// <summary>Faces of the definition that carry any mount surface at all.</summary>
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
        /// Cheap per-observation path: a compare and a couple of increments. The wide stat set
        /// is in <see cref="Sample"/>.
        /// </summary>
        public void OnUpdate(ThermalNode node, long gridId)
        {
            TotalUpdates++;

            if (node.Temperature > PeakTemperature)
            {
                PeakTemperature = node.Temperature;
                PeakTemperatureGrid = gridId;
            }
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
        /// Reads the definition's per-face sealing and mounting out of the shared block model.
        /// Once per type, from the first block placed: the model is immutable, so a second read
        /// could only produce the same answer.
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
    /// A block size, stored without depending on VRageMath so the report formatting stays in the
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
