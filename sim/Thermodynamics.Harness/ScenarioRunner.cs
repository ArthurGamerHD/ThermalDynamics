using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>One row of recorded simulation state.</summary>
    public class Sample
    {
        public float TimeSeconds;
        public float AmbientTemperature;
        public float HottestTemperature;
        public float MeanTemperature;
        public float TotalEnergy;
        public int Substeps;
        public int OverheatingBlocks;
        public readonly Dictionary<string, float> Tracked = new Dictionary<string, float>();
    }

    /// <summary>
    /// Runs a simulation forward in deterministic simulated time and records what happened.
    ///
    /// Nothing here reads a clock, so a scenario produces byte-identical output on every run and
    /// on every machine — which is what makes these usable as regression tests.
    /// </summary>
    public class ScenarioRunner
    {
        private readonly ThermalSimulation simulation;
        private readonly List<Sample> samples = new List<Sample>();
        private readonly Dictionary<string, ThermalNode> tracked = new Dictionary<string, ThermalNode>();
        private readonly Dictionary<string, CoolantLoop> trackedLoops = new Dictionary<string, CoolantLoop>();

        /// <summary>Environment as a function of elapsed simulated seconds.</summary>
        public Func<float, EnvironmentSample> Environment = t => Worlds.Shadow();

        /// <summary>
        /// Called after every solver step, before any sample is recorded. Use it to collect
        /// per-step output such as overheat events, which are cleared on the next step.
        /// </summary>
        public Action<ThermalSimulation> AfterStep;

        public ScenarioRunner(ThermalSimulation simulation)
        {
            if (simulation == null) throw new ArgumentNullException("simulation");
            this.simulation = simulation;
        }

        public ThermalSimulation Simulation
        {
            get { return simulation; }
        }

        public IList<Sample> Samples
        {
            get { return samples; }
        }

        public float ElapsedSeconds { get; private set; }

        /// <summary>Records a named block's temperature in every sample.</summary>
        public ScenarioRunner Track(string name, BlockInstance block)
        {
            ThermalNode node = simulation.Solver.GetNode(block);
            if (node != null) tracked[name] = node;
            return this;
        }

        public ScenarioRunner TrackLoop(string name, CoolantLoop loop)
        {
            if (loop != null) trackedLoops[name] = loop;
            return this;
        }

        /// <summary>
        /// Advances the simulation, recording a sample every
        /// <paramref name="sampleIntervalSeconds"/> of simulated time.
        /// </summary>
        public ScenarioRunner Run(float seconds, float sampleIntervalSeconds = 1f)
        {
            float step = simulation.Settings.StepSeconds;
            int totalSteps = (int)Math.Round(seconds / step);
            int stepsPerSample = Math.Max(1, (int)Math.Round(sampleIntervalSeconds / step));

            if (samples.Count == 0) Record();

            for (int i = 1; i <= totalSteps; i++)
            {
                EnvironmentSample environment = Environment(ElapsedSeconds);
                simulation.StepExact(1, environment);
                ElapsedSeconds += step;

                if (AfterStep != null) AfterStep(simulation);
                if (i % stepsPerSample == 0) Record();
            }

            return this;
        }

        private void Record()
        {
            Sample sample = new Sample();
            sample.TimeSeconds = ElapsedSeconds;
            sample.AmbientTemperature = simulation.Solver.Environment.AmbientTemperature;
            sample.TotalEnergy = simulation.Solver.TotalEnergy;
            sample.Substeps = simulation.Solver.LastSubsteps;
            sample.OverheatingBlocks = simulation.Solver.Overheats.Count;

            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            float hottest = 0f;
            double total = 0d;
            for (int i = 0; i < nodes.Count; i++)
            {
                float temperature = nodes[i].Temperature;
                if (temperature > hottest) hottest = temperature;
                total += temperature;
            }

            sample.HottestTemperature = hottest;
            sample.MeanTemperature = nodes.Count == 0 ? 0f : (float)(total / nodes.Count);

            foreach (KeyValuePair<string, ThermalNode> entry in tracked)
            {
                sample.Tracked[entry.Key] = entry.Value.Temperature;
            }
            foreach (KeyValuePair<string, CoolantLoop> entry in trackedLoops)
            {
                sample.Tracked[entry.Key] = entry.Value.Temperature;
            }

            samples.Add(sample);
        }

        public Sample Final
        {
            get { return samples.Count == 0 ? null : samples[samples.Count - 1]; }
        }

        /// <summary>Comma-separated values, temperatures in Celsius for readability.</summary>
        public string ToCsv()
        {
            StringBuilder sb = new StringBuilder();
            List<string> columns = new List<string>();
            if (samples.Count > 0)
            {
                foreach (string key in samples[0].Tracked.Keys) columns.Add(key);
            }

            sb.Append("time_s,ambient_C,hottest_C,mean_C,energy_J,substeps,overheating");
            for (int i = 0; i < columns.Count; i++)
            {
                sb.Append(',').Append(columns[i]).Append("_C");
            }
            sb.AppendLine();

            for (int i = 0; i < samples.Count; i++)
            {
                Sample s = samples[i];
                sb.Append(F(s.TimeSeconds)).Append(',')
                  .Append(F(ThermalConstants.KelvinToCelsius(s.AmbientTemperature))).Append(',')
                  .Append(F(ThermalConstants.KelvinToCelsius(s.HottestTemperature))).Append(',')
                  .Append(F(ThermalConstants.KelvinToCelsius(s.MeanTemperature))).Append(',')
                  .Append(F(s.TotalEnergy)).Append(',')
                  .Append(s.Substeps).Append(',')
                  .Append(s.OverheatingBlocks);

                for (int c = 0; c < columns.Count; c++)
                {
                    float value;
                    s.Tracked.TryGetValue(columns[c], out value);
                    sb.Append(',').Append(F(ThermalConstants.KelvinToCelsius(value)));
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string F(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
