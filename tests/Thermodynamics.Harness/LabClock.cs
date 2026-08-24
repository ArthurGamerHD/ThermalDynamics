using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// **A rig's run length is a length of thermal time, and the clock decides how many seconds
    /// that is.**
    ///
    /// <para>
    /// `HeatTimeScale` is how much faster than real physics heat moves, so a rig cut to 300 s
    /// covers two and a half times as much thermal ground at a clock of 225 as at 90. Every rig in
    /// this harness whose length was chosen by trying it — long enough for a hull to cross
    /// critical, for a client's error to decay, for a stack to saturate — was chosen against
    /// whatever the clock was that day, and `C24` moved it from 225 to 90. What that does to a
    /// rig is not a wrong number: it is a rig that stops reaching the state it exists to measure,
    /// and reports "no difference" in exactly the same shape as a real null result (`E8`).
    /// </para>
    ///
    /// <para>
    /// So a rig states the length it needs at the clock it was tuned at, and this converts. The
    /// same reasoning as <see cref="ScenarioRunner.DurationScale"/>, which does it for scenarios,
    /// and [backlog.md](../../docs/backlog.md) `C8`.
    /// </para>
    /// </summary>
    public static class LabClock
    {
        /// <summary>The clock the rigs here were originally cut against.</summary>
        public const float TunedAt = 225f;

        /// <summary>The clock in force, which is what a default world runs.</summary>
        public static float Shipped
        {
            get
            {
                float clock = new ThermalSettings().HeatTimeScale;
                return clock > 0f ? clock : 1f;
            }
        }

        /// <summary>How much longer a run has to be to cover the same thermal ground.</summary>
        public static float Stretch
        {
            get { return TunedAt / Shipped; }
        }

        /// <summary>Seconds at the shipped clock that cover <paramref name="seconds"/> at 225.</summary>
        public static float Seconds(float seconds)
        {
            return seconds * Stretch;
        }

        /// <summary>The same, for a rig that counts steps or samples rather than seconds.</summary>
        public static int Steps(int steps)
        {
            int stretched = (int)(steps * Stretch);
            return stretched < 1 ? 1 : stretched;
        }
    }
}
