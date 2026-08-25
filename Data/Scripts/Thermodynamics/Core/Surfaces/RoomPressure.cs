namespace Thermodynamics.Core
{
    /// <summary>
    /// How full of air a room is, combined from the game's answers rather than decided by this model.
    /// Each of the game's answers can veto air and none can require it, because the game owns
    /// pressurisation and this model has no standing to overrule it. **Silence is not one of those
    /// answers.** See thermal-model.md, Room air, and rules.md `C9`.
    /// </summary>
    public static class RoomPressure
    {
        /// <summary>How full of air a room is, 0..1.</summary>
        /// <param name="worldPressurised">
        /// Whether the world has oxygen and pressurisation enabled. With either off, no room holds
        /// air however sealed it is or whatever a vent reports.
        /// </param>
        /// <param name="sealedByGame">
        /// Whether the game calls this room airtight. The two models agree on ordinary hulls and
        /// disagree about sloped blocks, half blocks and anything whose shape is finer than a cell.
        /// </param>
        /// <param name="reportedLevel">
        /// How full the room is reported to be, 0..1, from the gas system or from a vent. Negative
        /// when nothing reported, which is distinct from something reporting empty — and was
        /// treated identically to it until 2026-08-24 despite this line saying otherwise.
        /// </param>
        /// <remarks>
        /// **Silence is not a veto** (`C22`). The three answers that empty a room are the world's
        /// settings, the game's own sealing test and a reported level, and each of them is somebody
        /// *saying* no. Nothing reporting is not a fourth: it is a compartment the game calls
        /// airtight, on a world where pressurisation is on, that no lookup found a level for —
        /// which is a lookup that missed rather than an answer of empty. This model's rooms are
        /// pieces of the game's and its cells are coarser than a sloped block, so a miss is exactly
        /// what a mismatch between the two produces.
        ///
        /// <para>
        /// The old behaviour was justified by the two errors being different sizes, and that was
        /// measured false: a link's conductance carries no pressure term, so room air is a *mixer*
        /// rather than a sink, and on a settled 2,000-block census hull the hottest block reads
        /// 1,502.83 K at every pressure from 0.2 to 1.0 against 1,706.08 K with the air gone
        /// (`F21`). The whole 203 K lands on the block overheat damage is taken off, in whichever
        /// direction the mistake goes — the errors are the same size, and an asymmetric chain
        /// cannot be justified by their sizes.
        /// </para>
        /// </remarks>
        public static float Level(bool worldPressurised, bool sealedByGame, float reportedLevel)
        {
            if (!worldPressurised) return 0f;
            if (!sealedByGame) return 0f;
            if (reportedLevel < 0f) return AssumedWhenUnanswered;
            if (reportedLevel <= 0f) return 0f;

            return reportedLevel > 1f ? 1f : reportedLevel;
        }

        /// <summary>
        /// What a sealed compartment on a pressurised world is assumed to hold when nothing
        /// answered for it.
        ///
        /// **Any positive value is the same decision.** `F21` measured the hottest block unmoved
        /// across every pressure from 0.2 to 1.0, because a room's links carry no pressure term —
        /// so what this constant chooses is whether the room mixes at all, not how hard. It is a
        /// named constant rather than a literal so that the choice is visible.
        /// </summary>
        public const float AssumedWhenUnanswered = 1f;

        /// <summary>
        /// Whether a room's air level is still undecided after the gas system has answered, and so
        /// needs the vents read as a fallback. Both vetoes are checked first, since neither answer can
        /// be changed by a vent and the vent read walks every vent on the grid.
        /// </summary>
        public static bool NeedsVentFallback(
            bool worldPressurised, bool sealedByGame, float reportedLevel)
        {
            if (!worldPressurised) return false;
            if (!sealedByGame) return false;

            return reportedLevel < 0f;
        }

        /// <summary>Sentinel <see cref="Level"/> accepts to mean that nothing reported a level.</summary>
        public const float NotReported = -1f;

        /// <summary>Oxygen at or below this reads as none.</summary>
        public const float OxygenPresent = 0.001f;

        /// <summary>
        /// Whether the game has air in a room that this model does not, which is the room-air failure
        /// worth reporting.
        /// </summary>
        /// <param name="hasAir">Whether this model is running air in the room.</param>
        /// <param name="vented">
        /// Whether the room stands open through a door, in which case being empty is correct.
        /// </param>
        /// <param name="gameOxygen">
        /// The game's oxygen level in the room, 0..1, or negative when it could not be asked.
        /// Unmeasured is never reported as a fault.
        /// </param>
        /// <remarks>
        /// Compares oxygen, not airtightness. Both <c>IsRoomAtPositionAirtight</c> and
        /// <c>IMyAirVent.IsPressurized</c> report whether a room is sealed, not whether it holds air.
        /// An enclosed space that was never filled, on a grid in vacuum, is sealed and empty in both
        /// models and is not a fault; testing sealing instead flags every such compartment and
        /// buries the genuine cases among them.
        /// </remarks>
        public static bool Disagrees(bool hasAir, bool vented, float gameOxygen)
        {
            if (hasAir) return false;
            if (vented) return false;

            return gameOxygen > OxygenPresent;
        }
    }
}
