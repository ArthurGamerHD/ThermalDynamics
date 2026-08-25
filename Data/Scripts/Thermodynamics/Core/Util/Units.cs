using System;
using System.Globalization;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Quantities written the way a person reads them: one convention, in one place, because four
    /// copies of the watt formatter drifted into three and a heat balance read differently depending
    /// on which readout a player opened.
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
        ///
        /// <para>
        /// **Typed as <c>CultureInfo</c> rather than as <c>IFormatProvider</c>, which is what the
        /// interface would ordinarily be for.** `IFormatProvider` is not on the game's script
        /// whitelist, so a mod naming it does not compile in a session — and the mod project
        /// building against the installed assemblies does not catch that, because the whitelist is
        /// a Roslyn analyzer the game applies and not a property of the assemblies. See
        /// known-issues.md, The whitelist is not the
        /// assemblies.
        /// </para>
        /// </param>
        public static string Watts(float watts, int decimals = 1, CultureInfo culture = null)
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
