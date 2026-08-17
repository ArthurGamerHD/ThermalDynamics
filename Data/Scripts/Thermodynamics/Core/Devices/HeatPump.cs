using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// The heat-pump hardware a block type carries: which of its faces is the cold side, how much
    /// heat it can lift, and how much electricity it can draw doing it.
    ///
    /// Both faces are one axis of the block in its own local space, so a rotated pump needs no
    /// special case — the same reason <see cref="CoolantPort"/> is local.
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

        /// <summary>Most electricity it can draw, W. What the pump lifts is limited by both.</summary>
        public float MaxPowerWatts = 20000f;

        /// <summary>
        /// A one-cell pump along an axis: cold face one way, hot face the opposite way.
        /// </summary>
        public static HeatPumpShape Along(Vector3I coldDirection, float ratedWatts, float maxPowerWatts)
        {
            return Centred(coldDirection, Vector3I.One, ratedWatts, maxPowerWatts);
        }

        /// <summary>
        /// A pump of any size along an axis, with both faces on the middle of the block's end
        /// caps rather than on a corner of them.
        ///
        /// The middle matters as soon as a pump is more than one cell across its own axis: the
        /// small-grid block is three by three, and reading its corner cell would bind the pump to
        /// whatever happened to be diagonally behind it instead of to what it is facing.
        /// </summary>
        public static HeatPumpShape Centred(Vector3I coldDirection, Vector3I size, float ratedWatts, float maxPowerWatts)
        {
            HeatPumpShape shape = new HeatPumpShape();
            shape.ColdDirection = coldDirection;
            shape.HotDirection = -coldDirection;

            Vector3I extent = new Vector3I(
                Math.Max(1, size.X), Math.Max(1, size.Y), Math.Max(1, size.Z)) - Vector3I.One;

            // Middle of the face on the two axes the pump does not run along, and the far end of
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
    /// A pump moves heat the wrong way up a gradient, which nothing else in the model does. It
    /// pays for that with electricity, and the price is set by Carnot: lifting heat across a small
    /// difference is nearly free, and across a large one is ruinous. That single relation is what
    /// stops the block being a cheat — you cannot drive a reactor to absolute zero, because the
    /// closer you get the more power each further kelvin costs, and the pump's electrical limit
    /// arrives long before the temperature does.
    /// </summary>
    public class HeatPumpDevice
    {
        /// <summary>The pump block itself.</summary>
        public BlockInstance Block;

        /// <summary>Node heat is drawn out of, or -1 when nothing is bolted to the cold face.</summary>
        public int ColdNodeIndex = -1;

        /// <summary>Node heat is rejected into, or -1 when nothing is bolted to the hot face.</summary>
        public int HotNodeIndex = -1;

        /// <summary>Most heat this pump can lift, W.</summary>
        public float RatedWatts;

        /// <summary>Most electricity this pump can draw, W.</summary>
        public float MaxPowerWatts;

        /// <summary>
        /// Whether the host is running it — the terminal switch, and whatever else the host
        /// decides. A pump the host has not enabled costs nothing and moves nothing.
        /// </summary>
        public bool Enabled;

        /// <summary>
        /// Fraction of the electricity it asked for that the grid actually supplied, 0..1. The
        /// host sets this from its own power model; a browned-out pump lifts proportionally less
        /// rather than stopping dead.
        /// </summary>
        public float PowerAvailable = 1f;

        /// <summary>Heat taken out of the cold side over the last step, W.</summary>
        public float LastLiftedWatts;

        /// <summary>Electricity consumed over the last step, W. What the host bills for.</summary>
        public float LastPowerWatts;

        /// <summary>
        /// Electricity the pump would have drawn last step had the grid supplied everything it
        /// asked for, W.
        ///
        /// This is what a host should request from its power model, not
        /// <see cref="LastPowerWatts"/>. Asking for what it managed on a browned-out grid asks for
        /// less every step than the step before, and a pump that keeps lowering its request
        /// because its request was not met never recovers when the power comes back.
        /// </summary>
        public float LastDemandWatts;

        /// <summary>Heat put into the hot side over the last step, W — the lift plus the work.</summary>
        public float LastRejectedWatts;

        /// <summary>Coefficient of performance over the last step: heat lifted per watt drawn.</summary>
        public float LastCoefficient;

        /// <summary>True when the pump ran but could not lift its rating, W being the limit.</summary>
        public bool LastWasLimited;

        /// <summary>True when it is wired up at both ends. A pump missing a side does nothing.</summary>
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
        /// Turns the energy moved over a whole step into the rates the host and the readouts
        /// quote. Reported per step and not per substep deliberately: a value written once per
        /// substep describes the last substep only, which is how the old model came to
        /// under-report every rate of change by roughly the substep count.
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
        /// Coefficient of performance for lifting heat out of <paramref name="cold"/> and into
        /// <paramref name="hot"/>: a fraction of the Carnot limit for a cooler, Tc/(Th-Tc).
        ///
        /// Pumping downhill is not a special case worth its own code path — it is just a very
        /// efficient pump — so it saturates at the cap rather than branching.
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
