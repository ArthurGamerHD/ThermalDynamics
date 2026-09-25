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


        public readonly Histogram FinalTemperatures = new Histogram(Histogram.TemperatureEdges());

        public readonly Histogram SampledTemperatures = new Histogram(Histogram.TemperatureEdges());

        public float PeakTemperature = float.MinValue;
        public long PeakTemperatureGrid;


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


        public BlockTypeTelemetry(MyDefinitionId id)
        {
            DefinitionId = id;
            Name = id.SubtypeName;
            if (string.IsNullOrEmpty(Name)) Name = id.TypeId.ToString();
        }


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

                Size = new Vector3ITriple(size.X, size.Y, size.Z);
                CaptureSurfaceProfile(block.Instance.Model);
            }

            Mass.Add(block.Instance.Mass);
        }


        public void OnRemoved()
        {
            System.Threading.Interlocked.Increment(ref Removed);

            if (System.Threading.Interlocked.Decrement(ref Live) < 0)
            {
                System.Threading.Interlocked.Increment(ref Live);
            }
        }


        public void OnUpdate(ThermalNode node, long gridId)
        {
            TotalUpdates++;
            NotePeak(node.Temperature, gridId);
        }


        public void NotePeak(float temperature, long gridId)
        {
            if (temperature <= PeakTemperature) return;

            PeakTemperature = temperature;
            PeakTemperatureGrid = gridId;
        }


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
