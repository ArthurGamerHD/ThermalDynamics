using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// The heat-pump hardware a block type carries: which of its faces is the cold side, how much
    /// heat it can lift, and how much electricity it can draw doing it.
    ///
    /// Both faces are expressed along one axis of the block's own local space, so a rotated pump
    /// needs no special case, as with <see cref="CoolantPort"/>.
    /// </summary>
    public class HeatPumpShape
    {
        /// <summary>Cell of the block the cold face sits on, block-local.</summary>
        public Vector3I ColdCell;

        /// <summary>Direction the cold face looks, block-local. Heat is drawn from this side.</summary>
        public Vector3I ColdDirection = Vector3I.Forward;

        /// <summary>Cell of the block the hot face sits on, block-local.</summary>
        public Vector3I HotCell;

        /// <summary>Direction the hot face looks, block-local. Heat is rejected into this side.</summary>
        public Vector3I HotDirection = Vector3I.Backward;

        /// <summary>Most heat the pump can lift out of the cold side, W.</summary>
        public float RatedWatts = 50000f;

        /// <summary>Most electricity it can draw, W. The lift is limited by this and the rating.</summary>
        public float MaxPowerWatts = 20000f;

        /// <summary>A one-cell pump along an axis, with the cold and hot faces opposed.</summary>
        public static HeatPumpShape Along(Vector3I coldDirection, float ratedWatts, float maxPowerWatts)
        {
            return Centred(coldDirection, Vector3I.One, ratedWatts, maxPowerWatts);
        }

        /// <summary>
        /// A pump of any size along an axis, with both faces at the centre of the block's end caps.
        ///
        /// The centre matters once a pump is more than one cell across: taking a corner cell would
        /// bind the pump to whatever sits diagonally behind it rather than to what it faces.
        /// </summary>
        public static HeatPumpShape Centred(Vector3I coldDirection, Vector3I size, float ratedWatts, float maxPowerWatts)
        {
            HeatPumpShape shape = new HeatPumpShape();
            shape.ColdDirection = coldDirection;
            shape.HotDirection = -coldDirection;

            Vector3I extent = new Vector3I(
                Math.Max(1, size.X), Math.Max(1, size.Y), Math.Max(1, size.Z)) - Vector3I.One;

            // Centre of the face on the two axes the pump does not run along, and the far end of
            // the one it does.
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

    /// <summary>
    /// One placed heat pump, resolved against the grid: the node it draws from, the node it
    /// rejects into, and what it managed last step.
    ///
    /// A pump moves heat up a gradient, which nothing else in the model does, and pays for it with
    /// electricity at a price set by the Carnot relation: nearly free across a small difference and
    /// prohibitive across a large one. That relation bounds the block — each further kelvin costs
    /// more, so the pump's electrical limit binds long before absolute zero is reached.
    /// </summary>
    public class HeatPumpDevice
    {
        /// <summary>The pump block itself.</summary>
        public BlockInstance Block;

        /// <summary>Node heat is drawn from, or -1 when nothing is mounted on the cold face.</summary>
        public int ColdNodeIndex = -1;

        /// <summary>Node heat is rejected into, or -1 when nothing is mounted on the hot face.</summary>
        public int HotNodeIndex = -1;

        /// <summary>Most heat this pump can lift, W.</summary>
        public float RatedWatts;

        /// <summary>Most electricity this pump can draw, W.</summary>
        public float MaxPowerWatts;

        /// <summary>
        /// Whether the host is running the pump, from the terminal switch and any other host
        /// condition. A disabled pump costs nothing and moves nothing.
        /// </summary>
        public bool Enabled;

        /// <summary>
        /// Fraction of the requested electricity the grid supplied, 0..1. Set by the host from its
        /// own power model; an under-supplied pump lifts proportionally less rather than stopping.
        /// </summary>
        public float PowerAvailable = 1f;

        /// <summary>
        /// Share of its rating the player has asked this pump to draw, 0..1. The terminal's slider.
        ///
        /// Distinct from <see cref="PowerAvailable"/>, which is what the grid could supply: this is
        /// what the block was told to want. A pump throttled to a quarter draws a quarter and lifts
        /// what a quarter buys at the current gap, which is the lever a player has for spending power
        /// on cooling only when cooling is what they need.
        ///
        /// Defaults to full so a pump works the moment it is built and a harness needs no host.
        /// </summary>
        public float PowerSetting = 1f;

        /// <summary>
        /// Electricity this pump may draw at its current setting, W: its rating times the slider.
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

        /// <summary>Heat taken out of the cold side over the last step, W.</summary>
        public float LastLiftedWatts;

        /// <summary>Electricity consumed over the last step, W. The figure the host bills for.</summary>
        public float LastPowerWatts;

        /// <summary>
        /// Electricity the pump would have drawn last step had the grid supplied its full request, W.
        ///
        /// Hosts should request this from their power model rather than <see cref="LastPowerWatts"/>.
        /// Requesting what the pump achieved on an under-supplied grid lowers the request every
        /// step, and a pump that lowers its request because it was refused never recovers.
        /// </summary>
        public float LastDemandWatts;

        /// <summary>Heat delivered to the hot side over the last step, W: the lift plus the work.</summary>
        public float LastRejectedWatts;

        /// <summary>Coefficient of performance over the last step: heat lifted per watt drawn.</summary>
        public float LastCoefficient;

        /// <summary>True when the pump ran but could not reach its rated lift.</summary>
        public bool LastWasLimited;

        /// <summary>True when both faces resolve to a node. A pump missing a side does nothing.</summary>
        public bool IsConnected
        {
            get { return ColdNodeIndex >= 0 && HotNodeIndex >= 0; }
        }

        /// <summary>Energy accumulated across the substeps of one step, J.</summary>
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

        /// <summary>
        /// Converts the energy moved over a whole step into the rates the host and readouts report.
        /// Computed per step rather than per substep: a value written once per substep describes
        /// only that substep, understating the rate by roughly the substep count.
        /// </summary>
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

        /// <summary>
        /// Coefficient of performance for lifting heat from <paramref name="cold"/> into
        /// <paramref name="hot"/>: a fraction of the Carnot limit for a cooler, Tc/(Th-Tc).
        ///
        /// Pumping down a gradient is not special-cased; it saturates at the cap rather than
        /// branching.
        /// </summary>
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
