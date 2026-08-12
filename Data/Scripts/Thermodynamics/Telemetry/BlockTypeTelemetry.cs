using VRage.Game;

namespace Thermodynamics
{
    /// <summary>
    /// Everything observed about one block definition across the whole session, on every grid.
    ///
    /// This is the balance-facing half of the telemetry: it answers "what temperature does a
    /// battery actually sit at", "which block reaches its critical temperature and how often",
    /// and "is this definition's SpecificHeat doing anything" — the questions the retune of
    /// Cubes.xml needs answered with numbers instead of guesses.
    /// </summary>
    public class BlockTypeTelemetry
    {
        public readonly MyDefinitionId DefinitionId;
        public readonly string Name;

        /// <summary>
        /// The thermal definition in force for this block type, captured from the first cell
        /// created. Written into the report so a run's numbers can be read against the values
        /// that produced them.
        /// </summary>
        public ThermalCellDefinition Definition;

        public long Placed;
        public long Removed;
        public long Live;
        public long PeakLive;

        /// <summary>Cell updates that fed the sampled stats below. Strided; see Telemetry.SampleStride.</summary>
        public long SampledUpdates;
        /// <summary>Every cell update, whether sampled or not.</summary>
        public long TotalUpdates;

        public readonly RunningStat Temperature = new RunningStat();
        public readonly RunningStat DeltaTemperature = new RunningStat();
        public readonly RunningStat DeltaRadiation = new RunningStat();
        public readonly RunningStat DeltaFriction = new RunningStat();
        public readonly RunningStat HeatGeneration = new RunningStat();
        public readonly RunningStat EnergyProduction = new RunningStat();
        public readonly RunningStat EnergyConsumption = new RunningStat();
        public readonly RunningStat ThrustConsumption = new RunningStat();
        public readonly RunningStat SolarIntensity = new RunningStat();

        public readonly RunningStat Mass = new RunningStat();
        public readonly RunningStat Neighbors = new RunningStat();
        public readonly RunningStat ExposedSurfaces = new RunningStat();
        public readonly RunningStat ExposedSurfaceArea = new RunningStat();
        public readonly RunningStat Conductance = new RunningStat();

        /// <summary>Final temperature of every live cell of this type, taken in one pass at shutdown.</summary>
        public readonly Histogram FinalTemperatures = new Histogram(Histogram.TemperatureEdges());
        public readonly Histogram SampledTemperatures = new Histogram(Histogram.TemperatureEdges());

        public float PeakTemperature = float.MinValue;
        public long PeakTemperatureGrid;

        /// <summary>Cell updates that ran the critical-temperature path, i.e. dealt damage.</summary>
        public long CriticalUpdates;
        public double TotalDamage;

        public BlockTypeTelemetry(MyDefinitionId id)
        {
            DefinitionId = id;
            Name = id.SubtypeName;
            if (string.IsNullOrEmpty(Name)) Name = id.TypeId.ToString();
        }

        public void OnPlaced(ThermalCell cell)
        {
            Placed++;
            Live++;
            if (Live > PeakLive) PeakLive = Live;

            if (Definition == null) Definition = cell.Definition;

            Mass.Add(cell.Mass);
        }

        public void OnRemoved()
        {
            Removed++;
            if (Live > 0) Live--;
        }

        /// <summary>
        /// Cheap per-update path. Runs on every cell update, so it stays to a compare and a
        /// couple of increments; the wide stat set is behind the stride in <see cref="Sample"/>.
        /// </summary>
        public void OnUpdate(ThermalCell cell)
        {
            TotalUpdates++;

            if (cell.Temperature > PeakTemperature)
            {
                PeakTemperature = cell.Temperature;
                PeakTemperatureGrid = cell.Grid.Grid.EntityId;
            }
        }

        public void Sample(ThermalCell cell)
        {
            SampledUpdates++;

            Temperature.Add(cell.Temperature);
            SampledTemperatures.Add(cell.Temperature);
            DeltaTemperature.Add(cell.DeltaTemperature);
            DeltaRadiation.Add(cell.DeltaRadiation);
            DeltaFriction.Add(cell.DeltaFriction);
            HeatGeneration.Add(cell.HeatGeneration);
            EnergyProduction.Add(cell.EnergyProduction);
            EnergyConsumption.Add(cell.EnergyConsumption);
            ThrustConsumption.Add(cell.ThrustEnergyConsumption);
            SolarIntensity.Add(cell.IntensityDebug);

            Neighbors.Add(cell.Neighbors.Count);
            ExposedSurfaces.Add(cell.ExposedSurfaces);
            ExposedSurfaceArea.Add(cell.ExposedSurfaceArea);

            if (cell.kA != null)
            {
                float total = 0;
                for (int i = 0; i < cell.kA.Length; i++) total += cell.kA[i];
                Conductance.Add(total);
            }
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
}
