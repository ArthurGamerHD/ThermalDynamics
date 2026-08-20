using System.Collections.Generic;
using System.Text;

namespace Thermodynamics
{
    /// <summary>A class of value the simulation should not have produced.</summary>
    public enum TelemetryAnomalyKind
    {
        None = 0,

        /// <summary>A temperature that is not a number at all.</summary>
        NotANumber,

        /// <summary>A temperature that has run away to infinity.</summary>
        Infinite,

        /// <summary>Finite, but far above anything the model should reach.</summary>
        Implausible,

        /// <summary>
        /// The solver's floor at zero absorbed a negative excursion. The signature of an unstable
        /// step: without the clamp the value would go negative and then oscillate with growing
        /// amplitude.
        /// </summary>
        ClampedToZero
    }

    /// <summary>
    /// Classifies a cell's post-update state as an anomaly or not.
    ///
    /// Free of any Space Engineers type, so the classification can be tested outside the game, on
    /// the same boundary the simulation core draws.
    /// </summary>
    public static class TelemetryAnomalies
    {
        /// <param name="temperature">The cell temperature after the update and after the clamp.</param>
        /// <param name="lastTemperature">Its temperature at the start of the update.</param>
        /// <param name="implausible">The threshold above which a finite value is suspect.</param>
        public static TelemetryAnomalyKind Classify(float temperature, float lastTemperature, float implausible)
        {
            if (float.IsNaN(temperature)) return TelemetryAnomalyKind.NotANumber;
            if (float.IsInfinity(temperature)) return TelemetryAnomalyKind.Infinite;
            if (temperature > implausible) return TelemetryAnomalyKind.Implausible;

            // Only reported when the cell had heat to lose: a cell already at zero that stayed there
            // is the normal state of an unsimulated block.
            if (temperature == 0f && lastTemperature > 0f) return TelemetryAnomalyKind.ClampedToZero;

            return TelemetryAnomalyKind.None;
        }

        /// <summary>
        /// The name a kind is aggregated under in the report. Stable, since it is the dictionary key
        /// grouping every occurrence of the same problem.
        /// </summary>
        public static string Name(TelemetryAnomalyKind kind, float implausible)
        {
            switch (kind)
            {
                case TelemetryAnomalyKind.NotANumber: return "temperature is NaN";
                case TelemetryAnomalyKind.Infinite: return "temperature is infinite";
                case TelemetryAnomalyKind.Implausible: return "temperature above " + TelemetryFormat.Number(implausible) + "K";
                case TelemetryAnomalyKind.ClampedToZero: return "temperature clamped to zero";
                default: return "none";
            }
        }
    }

    /// <summary>
    /// Admits one call in every N, so the expensive half of the per-cell data collection runs on
    /// a fraction of updates. The first call is always admitted, so a short session still
    /// produces samples.
    /// </summary>
    public class SampleGate
    {
        private int _stride = 1;
        private int _countdown;

        public int Stride
        {
            get { return _stride; }
            set { _stride = value < 1 ? 1 : value; }
        }

        public bool Admit()
        {
            if (--_countdown > 0) return false;

            _countdown = _stride;
            return true;
        }

        public void Reset()
        {
            _countdown = 0;
        }
    }

    /// <summary>One occurrence class of something the simulation should not have produced.</summary>
    public class AnomalyRecord
    {
        public string Kind;
        public long Count;
        public double FirstSeconds;
        public double LastSeconds;
        public string FirstExample;
        public string LastExample;

        /// <summary>
        /// True when this is a caught exception rather than a suspect measurement. Faults are
        /// recorded whether or not collection is running, and are the only thing in the module
        /// that reaches the game log by itself.
        /// </summary>
        public bool IsFault;
    }

    /// <summary>
    /// Every anomaly and fault the session has seen, one record per kind.
    ///
    /// The rule this exists to hold is the one that was wrong for a long time: **an observation is
    /// recorded only while collection is running, and a fault is recorded always.** Collection is a
    /// cost paid on healthy frames and is rightly opt-in; a caught exception costs nothing until the
    /// mod has already failed, and telemetry being off is exactly the state a player's world is in
    /// when it does. Gating the two together meant every <c>catch</c> in the simulation adapter
    /// discarded its exception in an ordinary world.
    ///
    /// Free of any Space Engineers type, like the classifier above it, so the rule can be tested
    /// outside the game rather than argued about.
    /// </summary>
    public class AnomalyRegistry
    {
        private readonly Dictionary<string, AnomalyRecord> records = new Dictionary<string, AnomalyRecord>();
        private readonly int maxKinds;

        /// <summary>Kinds refused because the registry was full. Counted so the report can say so.</summary>
        public long KindsDropped;

        public AnomalyRegistry(int maxKinds)
        {
            this.maxKinds = maxKinds < 1 ? 1 : maxKinds;
        }

        public Dictionary<string, AnomalyRecord> Records
        {
            get { return records; }
        }

        public int Count
        {
            get { return records.Count; }
        }

        /// <summary>
        /// Files one occurrence.
        /// </summary>
        /// <param name="fault">A caught exception, rather than a suspect measurement.</param>
        /// <param name="collecting">Whether telemetry collection is running.</param>
        /// <returns>
        /// True when the caller should write this to the game log: the first occurrence of a fault
        /// kind, and nothing else. A throw inside the step runs once per grid per frame, so logging
        /// every one would bury the rest of the log in the same six stack frames — the count still
        /// accumulates, and both the report and the closing summary carry it.
        /// </returns>
        public bool Record(string kind, string example, bool fault, bool collecting, double seconds)
        {
            if (!collecting && !fault) return false;
            if (kind == null) return false;

            AnomalyRecord record;
            if (!records.TryGetValue(kind, out record))
            {
                if (records.Count >= maxKinds)
                {
                    KindsDropped++;
                    return false;
                }

                record = new AnomalyRecord
                {
                    Kind = kind,
                    FirstSeconds = seconds,
                    FirstExample = example,
                    IsFault = fault,
                };
                records.Add(kind, record);

                record.Count = 1;
                record.LastSeconds = seconds;
                record.LastExample = example;
                return fault;
            }

            record.IsFault |= fault;
            record.Count++;
            record.LastSeconds = seconds;
            record.LastExample = example;
            return false;
        }

        /// <summary>The fault records, in the order they were first seen.</summary>
        public List<AnomalyRecord> Faults()
        {
            List<AnomalyRecord> faults = new List<AnomalyRecord>();

            foreach (AnomalyRecord record in records.Values)
            {
                if (record.IsFault) faults.Add(record);
            }

            faults.Sort(delegate (AnomalyRecord a, AnomalyRecord b)
            {
                return a.FirstSeconds.CompareTo(b.FirstSeconds);
            });

            return faults;
        }

        /// <summary>
        /// One block of text naming every fault of the session and how often each fired, or null
        /// when there were none.
        ///
        /// Without it a fault that fired ten thousand times reads in the log exactly like one that
        /// fired once, since only the first of each kind is logged as it happens. On a world that
        /// never turned collection on — every world, by default — this is the only output there is.
        /// </summary>
        public string FaultSummary(bool collecting)
        {
            List<AnomalyRecord> faults = Faults();
            if (faults.Count == 0) return null;

            StringBuilder sb = new StringBuilder();
            sb.Append(faults.Count).Append(" fault kind(s) this session");

            if (!collecting)
            {
                sb.Append("; telemetry was off, so there is no report. Set EnableTelemetry true")
                    .Append(" and reproduce for the full picture.");
            }

            for (int i = 0; i < faults.Count; i++)
            {
                AnomalyRecord fault = faults[i];
                sb.Append("\n    ").Append(fault.Count).Append("x ").Append(fault.Kind)
                    .Append(" (first at ").Append(fault.FirstSeconds.ToString("n1"))
                    .Append("s, last at ").Append(fault.LastSeconds.ToString("n1")).Append("s)");
            }

            return sb.ToString();
        }

        public void Clear()
        {
            records.Clear();
            KindsDropped = 0;
        }
    }
}
