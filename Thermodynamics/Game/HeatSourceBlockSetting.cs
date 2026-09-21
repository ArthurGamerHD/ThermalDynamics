using System;
using System.Globalization;
using Thermodynamics.Core;

namespace Thermodynamics
{
    public struct HeatSourceBlockSetting
    {
        public const float MinWatts = 0f;

        public const float MaxWatts = 1000000000f;

        private const double Decades = 6.0;

        private const double Floor = MaxWatts / 999999.0;

        public const float DefaultWatts = 5000000f;

        public const float MinRange = 10f;

        public const float MaxRange = 1000f;

        public const float DefaultRange = HeatSourceCommand.DefaultRange;

        public float Watts;

        public float Range;

/// <summary>HeatSourceBlockSetting operation.</summary>
        public HeatSourceBlockSetting(float watts, float range)
        {
            Watts = watts;
            Range = range;
        }

/// <summary>Default operation.</summary>
        public static HeatSourceBlockSetting Default()
        {
            return new HeatSourceBlockSetting(DefaultWatts, DefaultRange);
        }

/// <summary>Clamped operation.</summary>
        public HeatSourceBlockSetting Clamped()
        {
            return new HeatSourceBlockSetting(
                Clamp(Watts, MinWatts, MaxWatts),
                Clamp(Range, MinRange, MaxRange));
        }

/// <summary>Clamp operation.</summary>
        private static float Clamp(float value, float low, float high)
        {
            if (!(value > low)) return low;
            return value > high ? high : value;
        }

/// <summary>Save operation.</summary>
        public string Save()
        {
            return "1|" + Watts.ToString("G9", CultureInfo.InvariantCulture)
                + "|" + Range.ToString("G9", CultureInfo.InvariantCulture);
        }

/// <summary>TryLoad operation.</summary>
        public static bool TryLoad(string text, out HeatSourceBlockSetting setting)
        {
/// <summary>Default operation.</summary>
            setting = Default();
            if (string.IsNullOrEmpty(text)) return false;

            string[] parts = text.Split('|');
            if (parts.Length < 3 || parts[0] != "1") return false;

            float watts;
            float range;
            if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out watts)
                || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out range))
            {
                return false;
            }

/// <summary>HeatSourceBlockSetting operation.</summary>
            setting = new HeatSourceBlockSetting(watts, range).Clamped();
            return true;
        }

        public bool HasOutput
        {
            get { return Watts > 0f; }
        }

/// <summary>WattsAtPosition operation.</summary>
        public static float WattsAtPosition(float position)
        {
            if (!(position > 0f)) return 0f;              // rejects NaN as well as the low end
            if (position >= 1f) return MaxWatts;

            double watts = Floor * (Math.Pow(10.0, position * Decades) - 1.0);
            return (float)Math.Round(watts);
        }

/// <summary>PositionOfWatts operation.</summary>
        public static float PositionOfWatts(float watts)
        {
            if (!(watts > 0f)) return 0f;
            if (watts >= MaxWatts) return 1f;

            return (float)(Math.Log10((watts / Floor) + 1.0) / Decades);
        }

/// <summary>Describe operation.</summary>
        public string Describe()
        {
            return Units.Watts(Watts, 2, CultureInfo.InvariantCulture) + " out to "
                + Range.ToString("n0", CultureInfo.InvariantCulture) + " m";
        }

/// <summary>IrradianceAt operation.</summary>
        public float IrradianceAt(float metres)
        {
            return HeatSourceMath.Irradiance(
                new VRageMath.Vector3D(0.0, 0.0, 0.0),
                Watts,
                Range,
                new VRageMath.Vector3D(metres, 0.0, 0.0));
        }
    }
}
