namespace Thermodynamics.Core
{
    /// <summary>
    /// How full of air a room is, decided from the game's answers rather than this model's.
    ///
    /// Pressurisation belongs to the game: a world can have oxygen disabled, a world with oxygen can
    /// have pressurisation disabled, and with both enabled, whether a room holds air is the game's
    /// own sealing test, which accounts for every block shape and door state. This type only
    /// combines those answers.
    ///
    /// Each answer can veto air and none can require it. Air is heat capacity, so a room wrongly
    /// given air warms and cools as if it held a mass of gas that every bounding surface exchanges
    /// with, while wrongly denying air only costs the interior some thermal inertia.
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
        /// How full a vent in the room reports it to be, 0..1. Negative when no vent reported, which
        /// is distinct from a vent reporting empty.
        /// </param>
        public static float Level(bool worldPressurised, bool sealedByGame, float reportedLevel)
        {
            if (!worldPressurised) return 0f;
            if (!sealedByGame) return 0f;
            if (reportedLevel <= 0f) return 0f;

            return reportedLevel > 1f ? 1f : reportedLevel;
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
        /// An enclosed space nobody piped air into, on a grid in vacuum, is sealed and empty in both
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
