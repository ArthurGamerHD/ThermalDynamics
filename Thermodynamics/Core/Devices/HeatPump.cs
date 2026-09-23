using System;
using VRageMath;

namespace Thermodynamics.Core
{
    public class HeatPumpShape
    {
        public Vector3I ColdCell;

        public Vector3I ColdDirection = Vector3I.Forward;

        public Vector3I HotCell;

        public Vector3I HotDirection = Vector3I.Backward;

        public float RatedWatts = 50000f;

        public float MaxPowerWatts = 20000f;


        public static HeatPumpShape Along(Vector3I coldDirection, float ratedWatts, float maxPowerWatts)
        {

            return Centred(coldDirection, Vector3I.One, ratedWatts, maxPowerWatts);
        }


        public static HeatPumpShape Centred(Vector3I coldDirection, Vector3I size, float ratedWatts, float maxPowerWatts)
        {

            HeatPumpShape shape = new HeatPumpShape();
            shape.ColdDirection = coldDirection;
            shape.HotDirection = -coldDirection;


            Vector3I extent = new Vector3I(
                Math.Max(1, size.X), Math.Max(1, size.Y), Math.Max(1, size.Z)) - Vector3I.One;

            Vector3I middle = extent / 2;
            Vector3I axis = Vector3I.Abs(coldDirection);
            Vector3I along = axis * extent;


            Vector3I coldEnd = IsNegative(coldDirection) ? Vector3I.Zero : along;

            Vector3I hotEnd = IsNegative(coldDirection) ? along : Vector3I.Zero;
            Vector3I across = (Vector3I.One - axis) * middle;

            shape.ColdCell = across + coldEnd;
            shape.HotCell = across + hotEnd;

            shape.RatedWatts = ratedWatts > 0f ? ratedWatts : 0f;
            shape.MaxPowerWatts = maxPowerWatts > 0f ? maxPowerWatts : 0f;
            return shape;
        }


        private static bool IsNegative(Vector3I direction)
        {
            return direction.X + direction.Y + direction.Z < 0;
        }
    }

    public class HeatPumpDevice
    {
        public BlockInstance Block;

        public int ColdNodeIndex = -1;

        public int HotNodeIndex = -1;

        public float RatedWatts;

        public float MaxPowerWatts;

        public bool Enabled;

        public float PowerAvailable = 1f;

        public float PowerSetting = 1f;

        public float SettablePowerWatts
        {
            get
            {
                float setting = PowerSetting;
                if (setting < 0f) setting = 0f;
                if (setting > 1f) setting = 1f;
                return MaxPowerWatts * setting;
            }
        }

        public float LastLiftedWatts;

        public float LastPowerWatts;

        public float LastDemandWatts;

        public float LastRejectedWatts;

        public float LastCoefficient;

        public bool LastWasLimited;

        public float LastOptimalMarginKelvin;

        public bool IsConnected
        {
            get { return ColdNodeIndex >= 0 && HotNodeIndex >= 0; }
        }

        internal float LiftedEnergy;
        internal float PowerEnergy;
        internal float RejectedEnergy;
        internal float DemandEnergy;


        internal void BeginStep()
        {
            LiftedEnergy = 0f;
            PowerEnergy = 0f;
            RejectedEnergy = 0f;
            DemandEnergy = 0f;
        }


        internal void EndStep(float deltaSeconds)
        {
            if (deltaSeconds <= 0f) return;

            LastLiftedWatts = LiftedEnergy / deltaSeconds;
            LastPowerWatts = PowerEnergy / deltaSeconds;
            LastRejectedWatts = RejectedEnergy / deltaSeconds;
            LastDemandWatts = DemandEnergy / deltaSeconds;
            LastCoefficient = LastPowerWatts > 0f ? LastLiftedWatts / LastPowerWatts : 0f;
            LastWasLimited = Enabled && IsConnected && LastLiftedWatts < RatedWatts - 1f;
        }


        public static float Coefficient(float cold, float hot, float carnotFraction, float maxCoefficient)
        {
            if (carnotFraction <= 0f || maxCoefficient <= 0f) return 0f;
            if (cold <= 0f) return 0f;

            float difference = hot - cold;
            if (difference <= 0f) return maxCoefficient;

            float coefficient = carnotFraction * (cold / difference);
            if (coefficient > maxCoefficient) return maxCoefficient;
            return coefficient > 0f ? coefficient : 0f;
        }


        public override string ToString()
        {
            return "heat pump " + (Block == null ? "?" : Block.Min.ToString())
                + " lift " + LastLiftedWatts.ToString("n0") + "W"
                + " draw " + LastPowerWatts.ToString("n0") + "W"
                + " cop " + LastCoefficient.ToString("n2");
        }
    }
}
