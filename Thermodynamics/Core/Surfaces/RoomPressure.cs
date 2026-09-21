namespace Thermodynamics.Core
{
    public static class RoomPressure
    {
/// <summary>Level operation.</summary>
        public static float Level(bool worldPressurised, bool sealedByGame, float reportedLevel)
        {
            if (!worldPressurised) return 0f;
            if (!sealedByGame) return 0f;
            if (reportedLevel < 0f) return AssumedWhenUnanswered;
            if (reportedLevel <= 0f) return 0f;

            return reportedLevel > 1f ? 1f : reportedLevel;
        }

        public const float AssumedWhenUnanswered = 1f;

/// <summary>NeedsVentFallback operation.</summary>
        public static bool NeedsVentFallback(
            bool worldPressurised, bool sealedByGame, float reportedLevel)
        {
            if (!worldPressurised) return false;
            if (!sealedByGame) return false;

            return reportedLevel < 0f;
        }

        public const float NotReported = -1f;

        public const float OxygenPresent = 0.001f;

/// <summary>Disagrees operation.</summary>
        public static bool Disagrees(bool hasAir, bool vented, float gameOxygen)
        {
            if (hasAir) return false;
            if (vented) return false;

            return gameOxygen > OxygenPresent;
        }
    }
}
