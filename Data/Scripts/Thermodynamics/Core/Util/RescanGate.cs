namespace Thermodynamics.Core
{
    /// <summary>
    /// A periodic scan that runs only when its subject has changed since the last one. The version is
    /// whatever the caller counts with — a completed-pass count, a build number, a revision — and
    /// equal means there is nothing to find.
    /// </summary>
    public class RescanGate
    {
        /// <summary>Steps between checks. A change is not acted on before the next one.</summary>
        public int Interval;

        /// <summary>Scans this gate allowed, and checks it turned away because nothing had changed.</summary>
        public int Scans;
        public int Skipped;

        private int steps = int.MaxValue;
        private int scannedVersion;
        private bool scanned;

        public RescanGate(int interval)
        {
            Interval = interval;
        }

        /// <summary>Whether a scan has ever been allowed. Distinguishes "nothing found" from "not looked".</summary>
        public bool HasScanned
        {
            get { return scanned; }
        }

        /// <summary>
        /// Nothing is reading the result. The next reader gets a scan immediately rather than
        /// waiting out an interval that elapsed while nobody was looking.
        /// </summary>
        public void Idle()
        {
            steps = int.MaxValue;
            scanned = false;
        }

        /// <summary>
        /// Advances by <paramref name="steps"/> and answers whether to scan now.
        ///
        /// The interval is spent whether or not the version moved, so a subject that changes every
        /// step is still scanned at the cadence rather than continuously.
        /// </summary>
        public bool Due(int version, int steps)
        {
            if (this.steps < Interval)
            {
                // Saturated at the interval rather than accumulated, so the idle sentinel and a
                // long-running session cannot overflow the counter.
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

        /// <summary>
        /// Records a scan the caller ran on its own — a report forcing one at dump time — so the
        /// cadence does not immediately repeat work that has just been done.
        /// </summary>
        public void Mark(int version)
        {
            scanned = true;
            scannedVersion = version;
            steps = 0;
        }
    }
}
