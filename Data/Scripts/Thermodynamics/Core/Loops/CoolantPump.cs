namespace Thermodynamics.Core
{
    /// <summary>
    /// One coolant pump in a ring: the reason a ring carries heat to its far side rather than
    /// saturating where it is made. The host owns all three inputs — the terminal's speed, whether the
    /// block is on and functional, and how much power the grid supplied.
    /// </summary>
    public class CoolantPump
    {
        /// <summary>The pump block.</summary>
        public BlockInstance Block;

        /// <summary>
        /// Which way round the ring this pump pushes: +1 with the ring's own order, -1 against it, set
        /// from the block's orientation when the loop is traced. A backwards pump drives a backwards
        /// ring, which works as well; two facing each other cancel. See blocks.md, Coolant loop rules.
        /// </summary>
        public int Direction = 1;

        /// <summary>
        /// Speed the terminal is set to, 0..1.
        ///
        /// Defaults to full so a ring built in a test or a harness circulates without a host to drive
        /// it; the game overwrites this every step from the block's own control.
        /// </summary>
        public float Speed = 1f;

        /// <summary>Whether the host is running this pump at all: switch, damage, construction state.</summary>
        public bool Enabled = true;

        /// <summary>Fraction of its requested power the grid supplied, 0..1.</summary>
        public float PowerAvailable = 1f;

        /// <summary>Most electricity this pump draws, W, at full speed.</summary>
        public float MaxPowerWatts;

        /// <summary>Electricity drawn over the last step, W.</summary>
        public float LastPowerWatts;

        /// <summary>
        /// What this pump was charged for refilling on the last step, W.
        ///
        /// Kept so the charge can be taken off before the next one is added: a refill runs for
        /// many steps, and adding to the block's drawn power without removing the previous
        /// addition would bill it once per step for as long as it ran.
        /// </summary>
        public float LastRefillWatts;

        /// <summary>
        /// This pump's share of the ring's flow, signed by <see cref="Direction"/>: speed times what
        /// the grid supplied. A demand rather than a flow — <see cref="CoolantLoop.RefreshFlow"/> sums
        /// these and takes the square root — so opposed pumps subtract.
        /// </summary>
        public float Contribution
        {
            get
            {
                if (!Enabled) return 0f;
                return Direction * Clamp01(Speed) * Clamp01(PowerAvailable);
            }
        }

        /// <summary>
        /// Electricity this pump wants at its current setting, W. **Linear in speed, deliberately**:
        /// a real centrifugal pump's cubed affinity law, against flow going as a square root, makes
        /// ten idling pumps a hundredth the price of one working, which is an exploit rather than a
        /// trade. See thermal-model.md, Coolant loops.
        /// </summary>
        public float DemandWatts
        {
            get
            {
                if (!Enabled) return 0f;
                return MaxPowerWatts * Clamp01(Speed);
            }
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
