using System;

namespace Thermodynamics.Core
{
    /// <summary>
    /// **The line the mod puts on screen as a matter of course**, so that a player who has added it
    /// to a healthy world can tell it is there without waiting for something to go wrong.
    ///
    /// <para>
    /// document-of-intent.md decides this and states the bar: the mod's job
    /// is to *show* clearly, never to diagnose, advise or name the fix — and *clear* is a higher bar
    /// than *present*, the test being whether somebody who has never read this repository can look
    /// at the screen and tell their ship is getting hotter. Everything else this mod draws is a
    /// warning that fires once something is already wrong: the glow starts a hundred kelvin below a
    /// block's rating, the cue fires as one approaches it, the terminal panel wants a terminal, and
    /// the one always-available figure lived on a performance panel behind a chat command that
    /// ships off. See backlog.md `B41`.
    /// </para>
    ///
    /// <para>
    /// **The words are anchored on the glow's own threshold rather than chosen.** `hot` begins
    /// exactly where <see cref="Incandescence.GlowStartKelvin"/> begins, so the sentence and the
    /// block a player is looking at cannot disagree about what counts as hot — two readouts of one
    /// quantity drift, and this is the one place to stop it (`D3`). `warm` is the half of that
    /// climb, measured from the air rather than from absolute zero, because a hull sitting in a
    /// furnace is not warming up.
    /// </para>
    ///
    /// <para>
    /// Game-free and in `Core` on purpose: what it says is a decision worth pinning, and the
    /// drawing is a thin shell over it that no test outside a session can reach.
    /// </para>
    /// </summary>
    public static class EnvironmentReadout
    {
        /// <summary>How hot a hull is, in the three words a player is given.</summary>
        public enum Heat
        {
            /// <summary>Nearer the air than the glow. Nothing is happening.</summary>
            Cool,

            /// <summary>Past halfway from the air to where a block starts to glow.</summary>
            Warm,

            /// <summary>At or past the glow's own start, which is what the player can already see.</summary>
            Hot,
        }

        /// <summary>Share of the climb from the air to the glow at which <see cref="Heat.Warm"/> begins.</summary>
        public const float WarmShare = 0.5f;

        /// <summary>
        /// Where a hull sits between the air around it and the temperature its own worst block
        /// starts to glow at.
        ///
        /// <para>
        /// Zero at ambient and one at the glow's start, so it is a share of the only climb that
        /// matters. Above one it keeps climbing rather than clamping: a caller wanting a bar can
        /// clamp, and a caller wanting to know how far past the glow a hull is cannot recover what
        /// was clamped away.
        /// </para>
        /// </summary>
        /// <returns>Zero where there is no climb to measure — no rating, or air already past the glow.</returns>
        public static float Climb(float ambientKelvin, float hottestKelvin, float criticalKelvin)
        {
            if (float.IsNaN(ambientKelvin) || float.IsNaN(hottestKelvin)
                || float.IsNaN(criticalKelvin) || criticalKelvin <= 0f)
            {
                return 0f;
            }

            float glow = Incandescence.GlowStartKelvin(criticalKelvin);

            // A world hotter than the glow leaves no climb to be part of the way up. Nothing a hull
            // does in it is *warming*, so the share is undefined rather than infinite.
            float span = glow - ambientKelvin;
            if (span <= 0f) return 0f;

            float above = hottestKelvin - ambientKelvin;
            return above <= 0f ? 0f : above / span;
        }

        /// <summary>The word for that climb.</summary>
        public static Heat State(float ambientKelvin, float hottestKelvin, float criticalKelvin)
        {
            float climb = Climb(ambientKelvin, hottestKelvin, criticalKelvin);

            if (climb >= 1f) return Heat.Hot;
            return climb >= WarmShare ? Heat.Warm : Heat.Cool;
        }

        /// <summary>The word as the player reads it.</summary>
        public static string Word(Heat heat)
        {
            switch (heat)
            {
                case Heat.Hot: return "hot";
                case Heat.Warm: return "warm";
                default: return "cool";
            }
        }

        /// <summary>
        /// The whole line: what the air is doing, and what the ship is doing about it.
        ///
        /// <para>
        /// Two facts and no advice. The air is the figure that is always available and always true
        /// — it is what the mod knows that the game does not show — and the word is the ship
        /// against its own rating, which is the half that answers *is this getting worse*.
        /// </para>
        ///
        /// <para>
        /// The ship's half is dropped rather than guessed at where there is no rating to measure
        /// against, so a line never carries a word derived from nothing (`E8`).
        /// </para>
        /// </summary>
        public static string Line(float ambientKelvin, float hottestKelvin, float criticalKelvin)
        {
            string air = TemperatureScale.ToCelsiusString(ambientKelvin);

            if (criticalKelvin <= 0f || float.IsNaN(hottestKelvin) || float.IsNaN(criticalKelvin))
            {
                return air;
            }

            return air + "   hull " + Word(State(ambientKelvin, hottestKelvin, criticalKelvin));
        }
    }
}
