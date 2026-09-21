using System;

namespace Thermodynamics.Core
{
    public static class FidelityEnds
    {
        public struct End
        {
            public readonly string Setting;

            public readonly float Faithful;

            public readonly string Means;

/// <summary>End operation.</summary>
            public End(string setting, float faithful, string means)
            {
                Setting = setting;
                Faithful = faithful;
                Means = means;
            }
        }

        public static readonly End[] All =
        {
/// <summary>End operation.</summary>
            new End("MaxSubstepsPerBlock", 0f, "0, which leaves every block's real capacity in place"),
/// <summary>End operation.</summary>
            new End("MaxSubsteps", 64f, "the ceiling, which grants the stability estimate whatever it asks"),
/// <summary>End operation.</summary>
            new End("MaxElementVisitsPerStep", 0f, "0, which removes the bound on a step's work"),
/// <summary>End operation.</summary>
            new End("Frequency", 60f, "the highest rate, which is the finest step"),
        };

/// <summary>For operation.</summary>
        public static End? For(string setting)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (string.Equals(All[i].Setting, setting, StringComparison.Ordinal)) return All[i];
            }

            return null;
        }

/// <summary>Sentence operation.</summary>
        public static string Sentence(string setting)
        {
/// <summary>For operation.</summary>
            End? end = For(setting);
            return end == null ? string.Empty : " Most faithful at " + end.Value.Means + ".";
        }
    }
}
