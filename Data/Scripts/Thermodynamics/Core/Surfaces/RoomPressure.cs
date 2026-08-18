namespace Thermodynamics.Core
{
    /// <summary>
    /// How full of air a room is, decided from what the game says rather than from what this model
    /// would like.
    ///
    /// Pressurisation is not a thermal question and this mod has no business answering it. The game
    /// owns it: a world can have oxygen switched off entirely, a world with oxygen can have
    /// pressurisation switched off, and even in a world with both, whether a particular room holds
    /// air is the game's own sealing test, which knows about every block shape and door state there
    /// is. All this decides is what to do with those answers.
    ///
    /// The rule is that every one of them can veto air, and none of them can insist on it. Air is
    /// heat capacity: a room wrongly given air warms and cools like a room with a tonne of gas in
    /// it, and every surface bounding it exchanges with that. Wrongly denying air only costs the
    /// interior a little inertia, which is the mistake worth making.
    /// </summary>
    public static class RoomPressure
    {
        /// <summary>
        /// How full of air a room is, 0..1.
        /// </summary>
        /// <param name="worldPressurised">
        /// Whether the world has oxygen and pressurisation switched on. With either off, no room
        /// anywhere holds air, however sealed it is and whatever a vent claims.
        /// </param>
        /// <param name="sealedByGame">
        /// Whether the game itself calls this room airtight. Its answer, not this model's: the two
        /// agree on ordinary hulls and disagree about sloped blocks, half blocks and anything else
        /// whose real shape is finer than a cell.
        /// </param>
        /// <param name="reportedLevel">
        /// How full a vent in the room says it is, 0..1. Negative when no vent said anything, which
        /// is not the same as a vent saying "empty".
        /// </param>
        public static float Level(bool worldPressurised, bool sealedByGame, float reportedLevel)
        {
            if (!worldPressurised) return 0f;
            if (!sealedByGame) return 0f;
            if (reportedLevel <= 0f) return 0f;

            return reportedLevel > 1f ? 1f : reportedLevel;
        }

        /// <summary>The value <see cref="Level"/> takes to mean "nothing reported this".</summary>
        public const float NotReported = -1f;
    }
}
