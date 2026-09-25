using System;
using System.Globalization;

namespace Thermodynamics.Core
{
    public static class Units
    {

        public static string Watts(float watts, int decimals = 1, CultureInfo culture = null)
        {
            if (culture == null) culture = CultureInfo.CurrentCulture;

            string format = "n" + (decimals < 0 ? 0 : decimals).ToString(CultureInfo.InvariantCulture);

            float magnitude = Math.Abs(watts);

            if (magnitude >= 1e9f) return (watts / 1e9f).ToString(format, culture) + " GW";
            if (magnitude >= 1e6f) return (watts / 1e6f).ToString(format, culture) + " MW";
            if (magnitude >= 1e3f) return (watts / 1e3f).ToString(format, culture) + " kW";

            return watts.ToString("n0", culture) + " W";
        }
    }
}
