using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Shape definition for a heat pump block in the thermal simulation.
    /// Specifies the cold and hot cell positions and directions relative to the block's local coordinate system.
    /// Used during grid construction to determine heat pump geometry.
    /// </summary>
    public class HeatPumpShape
    {
        /// <summary>
        /// Local cell position of the cold side inlet/outlet.
        /// This is where heat is absorbed from the coolant loop.
        /// </summary>
        public Vector3I ColdCell;

        /// <summary>
        /// Direction vector (normalized) for the cold side flow.
        /// Points away from the heat pump at the cold cell.
        /// </summary>
        public Vector3I ColdDirection = Vector3I.Forward;

        /// <summary>
        /// Local cell position of the hot side inlet/outlet.
        /// This is where heat is rejected to the coolant loop.
        /// </summary>
        public Vector3I HotCell;

        /// <summary>
        /// Direction vector (normalized) for the hot side flow.
        /// Points away from the heat pump at the hot cell.
        /// </summary>
        public Vector3I HotDirection = Vector3I.Backward;

        /// <summary>
        /// Rated heat transfer capacity in watts (target operating point).
        /// The heat pump is designed to lift this much heat when operating normally.
        /// </summary>
        public float RatedWatts = 50000f;

        /// <summary>
        /// Maximum power consumption in watts.
        /// The electrical power required to operate the heat pump at full capacity.
        /// </summary>
        public float MaxPowerWatts = 20000f;


        /// <summary>
        /// Creates a linear heat pump shape aligned with the given direction.
        /// Cold and hot cells are placed at opposite ends of a 1x1x1 block.
        /// </summary>
        /// <param name="coldDirection">Direction from cold to hot side.</param>
        /// <param name="ratedWatts">Rated heat transfer capacity.</param>
        /// <param name="maxPowerWatts">Maximum electrical power consumption.</param>
        /// <returns>A new HeatPumpShape configured for linear alignment.</returns>
        public static HeatPumpShape Along(Vector3I coldDirection, float ratedWatts, float maxPowerWatts)
        {
            return Centred(coldDirection, Vector3I.One, ratedWatts, maxPowerWatts);
        }


        /// <summary>
        /// Creates a centered heat pump shape for the given block size and direction.
        /// Positions cold and hot cells at opposite ends of the block's longest axis,
        /// centered in the perpendicular dimensions.
        /// </summary>
        /// <param name="coldDirection">Direction from cold to hot side (must be aligned with an axis).</param>
        /// <param name="size">Block dimensions in cells.</param>
        /// <param name="ratedWatts">Rated heat transfer capacity.</param>
        /// <param name="maxPowerWatts">Maximum electrical power consumption.</param>
        /// <returns>A new HeatPumpShape with cold and hot cells positioned at opposite ends.</returns>
        /// <remarks>
        /// The algorithm:
        /// 1. Determines the block's extent (size minus 1 for cell-based positioning)
        /// 2. Finds the middle point in all dimensions
        /// 3. Identifies which axis aligns with coldDirection (using absolute values)
        /// 4. Places coldEnd at one end of that axis, hotEnd at the opposite end
        /// 5. Centers both cells in the perpendicular dimensions
        /// 6. Adjusts for negative directions by swapping ends
        /// </remarks>
        public static HeatPumpShape Centred(Vector3I coldDirection, Vector3I size, float ratedWatts, float maxPowerWatts)
        {
            HeatPumpShape shape = new HeatPumpShape();
            shape.ColdDirection = coldDirection;
            shape.HotDirection = -coldDirection;

            // Convert size to extent (number of cell gaps between first and last cell)
            Vector3I extent = new Vector3I(
                Math.Max(1, size.X), Math.Max(1, size.Y), Math.Max(1, size.Z)) - Vector3I.One;

            // Middle point in each dimension (integer division)
            Vector3I middle = extent / 2;

            // Determine which axis the heat pump is aligned with
            Vector3I axis = Vector3I.Abs(coldDirection);
            Vector3I along = axis * extent;

            // Calculate endpoints along the alignment axis
            // If direction is negative, cold is at the far end; otherwise at zero
            Vector3I coldEnd = IsNegative(coldDirection) ? Vector3I.Zero : along;
            Vector3I hotEnd = IsNegative(coldDirection) ? along : Vector3I.Zero;

            // Center cells in perpendicular dimensions
            Vector3I across = (Vector3I.One - axis) * middle;

            shape.ColdCell = across + coldEnd;
            shape.HotCell = across + hotEnd;

            shape.RatedWatts = ratedWatts > 0f ? ratedWatts : 0f;
            shape.MaxPowerWatts = maxPowerWatts > 0f ? maxPowerWatts : 0f;
            return shape;
        }


        /// <summary>
        /// Determines if a direction vector points in a negative direction.
        /// Uses the sum of all components - if negative, the direction is primarily negative.
        /// </summary>
        /// <param name="direction">The direction vector to check.</param>
        /// <returns>True if direction.X + direction.Y + direction.Z less than 0</returns>
        private static bool IsNegative(Vector3I direction)
        {
            return direction.X + direction.Y + direction.Z < 0;
        }
    }

    /// <summary>
    /// Runtime state for an installed heat pump block in the thermal simulation.
    /// Tracks actual temperatures, power consumption, and heat transfer performance.
    /// </summary>
    public class HeatPumpDevice
    {
        /// <summary>
        /// Reference to the block instance containing this heat pump.
        /// Used for position tracking and block-specific properties.
        /// </summary>
        public BlockInstance Block;

        /// <summary>
        /// Index of the thermal node connected to the cold side of the heat pump.
        /// Heat is absorbed from this node (temperature decreases).
        /// -1 indicates not connected.
        /// </summary>
        public int ColdNodeIndex = -1;

        /// <summary>
        /// Index of the thermal node connected to the hot side of the heat pump.
        /// Heat is rejected to this node (temperature increases).
        /// -1 indicates not connected.
        /// </summary>
        public int HotNodeIndex = -1;

        /// <summary>
        /// Rated heat transfer capacity in watts (target operating point).
        /// Set from the HeatPumpShape during construction.
        /// </summary>
        public float RatedWatts;

        /// <summary>
        /// Maximum electrical power consumption in watts.
        /// Set from the HeatPumpShape during construction.
        /// </summary>
        public float MaxPowerWatts;

        /// <summary>
        /// True if the heat pump is enabled (turned on).
        /// Disabled pumps do not transfer heat.
        /// </summary>
        public bool Enabled;

        /// <summary>
        /// Available power ratio (0-1) from the ship's power grid.
        /// 1.0 = full power available, 0.0 = no power.
        /// </summary>
        public float PowerAvailable = 1f;

        /// <summary>
        /// User-defined power setting (0-1).
        /// Controls the target heat transfer rate relative to RatedWatts.
        /// </summary>
        public float PowerSetting = 1f;

        /// <summary>
        /// Calculates the target heat transfer power in watts based on the power setting.
        /// Scales MaxPowerWatts by the power setting (clamped to 0-1).
        /// </summary>
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

        /// <summary>
        /// Heat lifted in the last step in watts.
        /// Amount of heat transferred from cold side to hot side.
        /// </summary>
        public float LastLiftedWatts;

        /// <summary>
        /// Electrical power drawn in the last step in watts.
        /// Energy consumed from the power grid to operate the pump.
        /// </summary>
        public float LastPowerWatts;

        /// <summary>
        /// Heat demand in the last step in watts.
        /// Theoretical heat that needed to be lifted based on temperature difference.
        /// </summary>
        public float LastDemandWatts;

        /// <summary>
        /// Heat rejected to the hot side in the last step in watts.
        /// Sum of lifted heat plus input power (conservation of energy).
        /// </summary>
        public float LastRejectedWatts;

        /// <summary>
        /// Coefficient of Performance (COP) in the last step.
        /// Ratio of heat lifted to electrical power consumed.
        /// COP = LiftedWatts / PowerWatts (higher is more efficient).
        /// </summary>
        public float LastCoefficient;

        /// <summary>
        /// True if the heat pump was limited in the last step.
        /// Indicates the pump couldn't lift its rated amount due to insufficient power or temperature constraints.
        /// </summary>
        public bool LastWasLimited;

        /// <summary>
        /// Temperature margin to optimal operating point in Kelvin.
        /// Difference between actual cold-side temperature and ideal operating temperature.
        /// </summary>
        public float LastOptimalMarginKelvin;

        /// <summary>
        /// True if both cold and hot nodes are connected.
        /// A heat pump must have both sides connected to transfer heat.
        /// </summary>
        public bool IsConnected
        {
            get { return ColdNodeIndex >= 0 && HotNodeIndex >= 0; }
        }

        /// <summary>
        /// Accumulator for heat lifted energy in joules (step积分).
        /// </summary>
        internal float LiftedEnergy;

        /// <summary>
        /// Accumulator for electrical power energy in joules (step积分).
        /// </summary>
        internal float PowerEnergy;

        /// <summary>
        /// Accumulator for heat rejected energy in joules (step积分).
        /// </summary>
        internal float RejectedEnergy;

        /// <summary>
        /// Accumulator for heat demand energy in joules (step积分).
        /// </summary>
        internal float DemandEnergy;


        /// <summary>
        /// Initializes energy accumulators at the start of a simulation step.
        /// Resets all energy counters to zero for the new step.
        /// </summary>
        internal void BeginStep()
        {
            LiftedEnergy = 0f;
            PowerEnergy = 0f;
            RejectedEnergy = 0f;
            DemandEnergy = 0f;
        }


        /// <summary>
        /// Finalizes energy calculations at the end of a simulation step.
        /// Converts accumulated energy (joules) to power (watts) by dividing by step duration.
        /// Also calculates COP and determines if the pump was limited.
        /// </summary>
        /// <param name="deltaSeconds">Duration of the step in seconds.</param>
        internal void EndStep(float deltaSeconds)
        {
            if (deltaSeconds <= 0f) return;

            // Convert energy accumulators to power rates
            LastLiftedWatts = LiftedEnergy / deltaSeconds;
            LastPowerWatts = PowerEnergy / deltaSeconds;
            LastRejectedWatts = RejectedEnergy / deltaSeconds;
            LastDemandWatts = DemandEnergy / deltaSeconds;

            // Calculate Coefficient of Performance (COP)
            // COP = Heat Lifted / Electrical Power Input
            LastCoefficient = LastPowerWatts > 0f ? LastLiftedWatts / LastPowerWatts : 0f;

            // Mark if pump was limited (couldn't achieve rated performance)
            LastWasLimited = Enabled && IsConnected && LastLiftedWatts < RatedWatts - 1f;
        }


        /// <summary>
        /// Calculates the theoretical maximum COP (Coefficient of Performance) for a heat pump.
        /// Based on the Carnot cycle efficiency, modified by a fraction factor.
        /// COP = (CarnotFraction * TCold) / (THot - TCold)
        /// When temperatures are close, COP approaches the maximum coefficient.
        /// </summary>
        /// <param name="cold">Cold side temperature in Kelvin.</param>
        /// <param name="hot">Hot side temperature in Kelvin.</param>
        /// <param name="carnotFraction">Fraction of Carnot efficiency the pump achieves (0-1).</param>
        /// <param name="maxCoefficient">Maximum achievable COP (design limit).</param>
        /// <returns>The actual COP, or maxCoefficient if temperatures are equal.</returns>
        public static float Coefficient(float cold, float hot, float carnotFraction, float maxCoefficient)
        {
            if (carnotFraction <= 0f || maxCoefficient <= 0f) return 0f;
            if (cold <= 0f) return 0f;

            float difference = hot - cold;
            if (difference <= 0f) return maxCoefficient;

            // Carnot-based COP calculation
            float coefficient = carnotFraction * (cold / difference);
            if (coefficient > maxCoefficient) return maxCoefficient;
            return coefficient > 0f ? coefficient : 0f;
        }


        /// <summary>
        /// Returns a human-readable string representation of the heat pump's current state.
        /// Shows lift, power draw, and COP for monitoring purposes.
        /// </summary>
        /// <returns>Formatted string like "heat pump (100,0,0) lift 5000W draw 1200W cop 4.17".</returns>
        public override string ToString()
        {
            return "heat pump " + (Block == null ? "?" : Block.Min.ToString())
                + " lift " + LastLiftedWatts.ToString("n0") + "W"
                + " draw " + LastPowerWatts.ToString("n0") + "W"
                + " cop " + LastCoefficient.ToString("n2");
        }
    }
}
