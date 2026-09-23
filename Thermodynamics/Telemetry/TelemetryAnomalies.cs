using System.Collections.Generic;
using System.Text;

namespace Thermodynamics
{
    public enum TelemetryAnomalyKind
    {
        None = 0,

        NotANumber,

        Infinite,

        Implausible,

        ClampedToZero
    }

    public static class TelemetryAnomalies
    {

        public static TelemetryAnomalyKind Classify(float temperature, float lastTemperature, float implausible)
        {
            if (float.IsNaN(temperature)) return TelemetryAnomalyKind.NotANumber;
            if (float.IsInfinity(temperature)) return TelemetryAnomalyKind.Infinite;
            if (temperature > implausible) return TelemetryAnomalyKind.Implausible;

            if (temperature == 0f && lastTemperature > 0f) return TelemetryAnomalyKind.ClampedToZero;

            return TelemetryAnomalyKind.None;
        }


        public static TelemetryAnomalyKind ClassifyGrid(float environmentWatts, float heatGainWatts,
            float hottestTemperature, float implausible)
        {
            if (float.IsNaN(environmentWatts) || float.IsNaN(heatGainWatts) || float.IsNaN(hottestTemperature))
                return TelemetryAnomalyKind.NotANumber;

            if (float.IsInfinity(environmentWatts) || float.IsInfinity(heatGainWatts) || float.IsInfinity(hottestTemperature))
                return TelemetryAnomalyKind.Infinite;

            if (hottestTemperature > implausible) return TelemetryAnomalyKind.Implausible;

            return TelemetryAnomalyKind.None;
        }


        public static string GridName(TelemetryAnomalyKind kind, float implausible)
        {
            switch (kind)
            {
                case TelemetryAnomalyKind.NotANumber: return "grid went NaN";
                case TelemetryAnomalyKind.Infinite: return "grid went infinite";
                case TelemetryAnomalyKind.Implausible: return "grid above " + TelemetryFormat.Number(implausible) + "K";
                default: return "none";
            }
        }


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

    public class AnomalyRecord
    {
        public string Kind;
        public long Count;
        public double FirstSeconds;
        public double LastSeconds;
        public string FirstExample;
        public string LastExample;

        public bool IsFault;
    }

    public class AnomalyRegistry
    {
        private readonly Dictionary<string, AnomalyRecord> records = new Dictionary<string, AnomalyRecord>();
        private readonly int maxKinds;

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
