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


            public End(string setting, float faithful, string means)
            {
                Setting = setting;
                Faithful = faithful;
                Means = means;
            }
        }

        public static readonly End[] All =
        {

            new End("MaxSubstepsPerBlock", 0f, "0, which leaves every block's real capacity in place"),

            new End("MaxSubsteps", 64f, "the ceiling, which grants the stability estimate whatever it asks"),

            new End("MaxElementVisitsPerStep", 0f, "0, which removes the bound on a step's work"),

            new End("Frequency", 60f, "the highest rate, which is the finest step"),
        };


        public static End? For(string setting)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (string.Equals(All[i].Setting, setting, StringComparison.Ordinal)) return All[i];
            }

            return null;
        }


        public static string Sentence(string setting)
        {

            End? end = For(setting);
            return end == null ? string.Empty : " Most faithful at " + end.Value.Means + ".";
        }
    }
}
