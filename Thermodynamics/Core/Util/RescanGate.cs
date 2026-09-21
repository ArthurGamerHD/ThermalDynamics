namespace Thermodynamics.Core
{
    public class RescanGate
    {
        public int Interval;

        public int Scans;
        public int Skipped;

        private int steps = int.MaxValue;
        private int scannedVersion;
        private bool scanned;

/// <summary>RescanGate operation.</summary>
        public RescanGate(int interval)
        {
            Interval = interval;
        }

        public bool HasScanned
        {
            get { return scanned; }
        }

/// <summary>Idle operation.</summary>
        public void Idle()
        {
            steps = int.MaxValue;
            scanned = false;
        }

/// <summary>Due operation.</summary>
        public bool Due(int version, int steps)
        {
            if (this.steps < Interval)
            {
                long advanced = (long)this.steps + (steps > 0 ? steps : 1);
                this.steps = advanced >= Interval ? Interval : (int)advanced;

                if (this.steps < Interval) return false;
            }

            this.steps = 0;

            if (scanned && version == scannedVersion)
            {
                Skipped++;
                return false;
            }

            Scans++;
            Mark(version);
            return true;
        }

/// <summary>Mark operation.</summary>
        public void Mark(int version)
        {
            scanned = true;
            scannedVersion = version;
            steps = 0;
        }
    }
}
