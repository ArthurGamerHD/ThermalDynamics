namespace Thermodynamics.Core
{
    public class CoolantPump
    {
        public BlockInstance Block;

        public int Direction = 1;

        public float Speed = 1f;

        public bool Enabled = true;

        public float PowerAvailable = 1f;

        public float MaxPowerWatts;

        public float LastPowerWatts;

        public float LastRefillWatts;

        public float Contribution
        {
            get
            {
                if (!Enabled) return 0f;
                return Direction * ThermalMath.Clamp01(Speed) * ThermalMath.Clamp01(PowerAvailable);
            }
        }

        public float DemandWatts
        {
            get
            {
                if (!Enabled) return 0f;
                return MaxPowerWatts * ThermalMath.Clamp01(Speed);
            }
        }
    }
}
