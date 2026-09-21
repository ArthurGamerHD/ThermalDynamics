using Thermodynamics.Core;
using VRage.Game;
using VRageMath;

namespace Thermodynamics
{
    public class BlockTypeTelemetry
    {
        public readonly MyDefinitionId DefinitionId;
        public readonly string Name;

        public BlockThermalProperties Definition;

        public Vector3ITriple Size;

        public long Placed;
        public long Removed;
        public long Live;
        public long PeakLive;

        public long SampledUpdates;

        public long TotalUpdates;

/// <summary>RunningStat operation.</summary>
        public readonly RunningStat Temperature = new RunningStat();
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat DeltaTemperature = new RunningStat();
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat ConductionWatts = new RunningStat();
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat RadiationWatts = new RunningStat();
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat ConvectionWatts = new RunningStat();
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat SolarWatts = new RunningStat();
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat FrictionWatts = new RunningStat();
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat HeatGeneration = new RunningStat();
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat EnergyProduction = new RunningStat();
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat EnergyConsumption = new RunningStat();
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat ThrustConsumption = new RunningStat();

/// <summary>RunningStat operation.</summary>
        public readonly RunningStat Mass = new RunningStat();
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat ThermalMass = new RunningStat();
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat ExposedSurfaces = new RunningStat();
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat ExposedSurfaceArea = new RunningStat();

/// <summary>Histogram operation.</summary>
        public readonly Histogram FinalTemperatures = new Histogram(Histogram.TemperatureEdges());
/// <summary>Histogram operation.</summary>
        public readonly Histogram SampledTemperatures = new Histogram(Histogram.TemperatureEdges());

        public float PeakTemperature = float.MinValue;
        public long PeakTemperatureGrid;

/// <summary>RunningStat operation.</summary>
        public readonly RunningStat SubstepDemand = new RunningStat();

        public float PeakSubstepDemand;
        public long PeakSubstepDemandGrid;
        public string PeakSubstepDemandPosition = "";

        public float[] SealFractionByFace;

        public float[] MountFractionByFace;

        public long UnsealedByDoorState;

        public bool HasSurfaceProfile
        {
            get { return SealFractionByFace != null; }
        }

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

        public long CriticalUpdates;
        public double TotalDamage;

/// <summary>BlockTypeTelemetry operation.</summary>
        public BlockTypeTelemetry(MyDefinitionId id)
        {
            DefinitionId = id;
            Name = id.SubtypeName;
            if (string.IsNullOrEmpty(Name)) Name = id.TypeId.ToString();
        }

/// <summary>OnPlaced operation.</summary>
        public void OnPlaced(ThermalBlock block)
        {
            System.Threading.Interlocked.Increment(ref Placed);
            long live = System.Threading.Interlocked.Increment(ref Live);

            long peak = System.Threading.Interlocked.Read(ref PeakLive);
            while (live > peak)
            {
                long seen = System.Threading.Interlocked.CompareExchange(ref PeakLive, live, peak);
                if (seen == peak) break;
                peak = seen;
            }

            if (Definition == null && block.Instance != null)
            {
                Definition = block.Instance.Thermal;
                Vector3I size = block.Instance.Model.Size;
/// <summary>Vector3ITriple operation.</summary>
                Size = new Vector3ITriple(size.X, size.Y, size.Z);
                CaptureSurfaceProfile(block.Instance.Model);
            }

            Mass.Add(block.Instance.Mass);
        }

/// <summary>OnRemoved operation.</summary>
        public void OnRemoved()
        {
            System.Threading.Interlocked.Increment(ref Removed);

            if (System.Threading.Interlocked.Decrement(ref Live) < 0)
            {
                System.Threading.Interlocked.Increment(ref Live);
            }
        }

/// <summary>OnUpdate operation.</summary>
        public void OnUpdate(ThermalNode node, long gridId)
        {
            TotalUpdates++;
            NotePeak(node.Temperature, gridId);
        }

/// <summary>NotePeak operation.</summary>
        public void NotePeak(float temperature, long gridId)
        {
            if (temperature <= PeakTemperature) return;

            PeakTemperature = temperature;
            PeakTemperatureGrid = gridId;
        }

/// <summary>SampleSubstepDemand operation.</summary>
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

/// <summary>Sample operation.</summary>
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

/// <summary>CaptureSurfaceProfile operation.</summary>
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

/// <summary>OnCriticalDamage operation.</summary>
        public void OnCriticalDamage(float damage)
        {
            CriticalUpdates++;
            TotalDamage += damage;
        }

/// <summary>OnFinalTemperature operation.</summary>
        public void OnFinalTemperature(float temperature)
        {
            FinalTemperatures.Add(temperature);
        }
    }

    public struct Vector3ITriple
    {
        public int X;
        public int Y;
        public int Z;

/// <summary>Vector3ITriple operation.</summary>
        public Vector3ITriple(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

/// <summary>ToString operation.</summary>
        public override string ToString()
        {
            return X + "x" + Y + "x" + Z;
        }
    }
}
