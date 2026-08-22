using System;
using System.Globalization;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Quantities written the way a person reads them.
    ///
    /// <para>
    /// One convention, in one place. There were four copies of the watt formatter — the cockpit
    /// panel, the settings menu, the debug panel and the chat command — and they had drifted into
    /// three different conventions: one decimal place or two, a gigawatt tier or none, the current
    /// culture or the invariant one. A ship's heat balance was therefore quoted at a different
    /// precision depending on which readout a player happened to open, and above a gigawatt one of
    /// them said "1,500.0 MW" where another said "1.50 GW".
    /// </para>
    /// </summary>
    public static class Units
    {
        /// <summary>
        /// Watts at a readable magnitude, from a hand tool's few hundred to a capital ship's
        /// gigawatt. <paramref name="decimals"/> applies to the scaled tiers; whole watts are
        /// always written whole, since a tenth of a watt is never the interesting part of a
        /// figure small enough to be shown in watts.
        /// </summary>
        /// <param name="culture">
        /// Pass the invariant culture where the text is parsed again or compared — a chat command
        /// echoing a value it parsed — and leave it null for a readout, where a player expects
        /// their own decimal separator.
        /// </param>
        public static string Watts(float watts, int decimals = 1, IFormatProvider culture = null)
        {
            if (culture == null) culture = CultureInfo.CurrentCulture;

            string format = "n" + (decimals < 0 ? 0 : decimals).ToString(CultureInfo.InvariantCulture);

            // On magnitude, so that a negative figure — a grid shedding more than it makes — picks
            // the same tier as the positive one of the same size rather than falling through to
            // raw watts.
            float magnitude = Math.Abs(watts);

            if (magnitude >= 1e9f) return (watts / 1e9f).ToString(format, culture) + " GW";
            if (magnitude >= 1e6f) return (watts / 1e6f).ToString(format, culture) + " MW";
            if (magnitude >= 1e3f) return (watts / 1e3f).ToString(format, culture) + " kW";

            return watts.ToString("n0", culture) + " W";
        }
    }
}
