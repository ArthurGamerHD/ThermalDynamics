namespace Thermodynamics.Core
{
    public static class TopSpeedForce
    {

        public static float Newtons(float resistance, float mass, float cruise, float speed)
        {
            if (!(speed > cruise) || !(speed > 0f)) return 0f;
            if (!(mass > 0f) || !(resistance > 0f)) return 0f;

            float over = cruise > 0f ? 1f - (cruise / speed) : 1f;
            return resistance * mass * over;
        }


        public static float Ceiling(bool boostEnabled, float cruise, float boostCeiling)
        {
            if (!(cruise > 0f)) cruise = 0f;
            if (!boostEnabled) return cruise;

            return boostCeiling > cruise ? boostCeiling : cruise;
        }
    }
}
