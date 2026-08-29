using System;

namespace Thermodynamics.Core
{
    /// <summary>
    /// **Which end of each integration dial is the faithful one.**
    ///
    /// <para>
    /// `C15` says a mechanism is configured as a list from `off` to `realistic` with `realistic`
    /// the default. The four integration dials are the exception the rule needs stating for: they
    /// are not a feature and cannot be switched off, and **they run in three different directions**
    /// — `MaxSubstepsPerBlock` and `MaxElementVisitsPerStep` are faithful at zero, `MaxSubsteps` and
    /// `Frequency` are faithful at their ceiling. Until 2026-08-28 nothing said which way any of
    /// them pointed except configuration.md, so a player in the menu had four differently worded
    /// tips and no rule (backlog.md `B31`).
    /// </para>
    ///
    /// <para>
    /// **Two of the four do not ship at their faithful end, and that is deliberate rather than a
    /// defect.** `Frequency` ships at 4 because a quarter-second step is the basis every substep
    /// figure in this repository is quoted on, and `MaxElementVisitsPerStep` ships at four million
    /// because `G6` asks a grid to keep up with real time. Saying so is the point: a dial whose
    /// default is not its faithful end is exactly the dial a reader needs told about.
    /// </para>
    ///
    /// <para>
    /// One table, two readers — the menu renders it into every tip and `FidelityEndTests` holds it
    /// against what configuration.md claims, so the page and the surface cannot drift (`D3`).
    /// </para>
    /// </summary>
    public static class FidelityEnds
    {
        /// <summary>One dial, and where its most faithful setting is.</summary>
        public struct End
        {
            /// <summary>The setting's name, as `Settings.GetValue` spells it.</summary>
            public readonly string Setting;

            /// <summary>The value that models the physics most closely, whatever it costs.</summary>
            public readonly float Faithful;

            /// <summary>What that value means in words, for a tip that has to read as a sentence.</summary>
            public readonly string Means;

            public End(string setting, float faithful, string means)
            {
                Setting = setting;
                Faithful = faithful;
                Means = means;
            }
        }

        /// <summary>
        /// The four, in the order configuration.md lists them.
        ///
        /// <para>
        /// Only the integration dials are here. Every other fidelity setting is a switch whose
        /// faithful end is *on*, which needs no table and would make this one longer without
        /// saying anything — and the balance dials are not fidelity at all: `FrictionScale` changes
        /// how much friction there is, not how well it is modelled.
        /// </para>
        /// </summary>
        public static readonly End[] All =
        {
            new End("MaxSubstepsPerBlock", 0f, "0, which leaves every block's real capacity in place"),
            new End("MaxSubsteps", 64f, "the ceiling, which grants the stability estimate whatever it asks"),
            new End("MaxElementVisitsPerStep", 0f, "0, which removes the bound on a step's work"),
            new End("Frequency", 60f, "the highest rate, which is the finest step"),
        };

        /// <summary>The faithful end of one dial, or null where it has none in this table.</summary>
        public static End? For(string setting)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (string.Equals(All[i].Setting, setting, StringComparison.Ordinal)) return All[i];
            }

            return null;
        }

        /// <summary>
        /// The sentence a tip carries, or the empty string for a setting with no faithful end.
        ///
        /// <para>
        /// Said the same way every time, which is the whole point: four dials described in four
        /// voices is what a reader has to learn rather than read.
        /// </para>
        /// </summary>
        public static string Sentence(string setting)
        {
            End? end = For(setting);
            return end == null ? string.Empty : " Most faithful at " + end.Value.Means + ".";
        }
    }
}
