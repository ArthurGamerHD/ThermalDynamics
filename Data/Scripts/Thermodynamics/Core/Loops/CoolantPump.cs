namespace Thermodynamics.Core
{
    /// <summary>
    /// One coolant pump in a ring, resolved against the grid.
    ///
    /// The pump is the reason a ring cools anything. A ring with no pump running still holds coolant
    /// and still exchanges with the blocks it touches, but nothing carries that heat to the far side —
    /// so the coolant beside a reactor saturates while the coolant at the radiator stays cold. See
    /// <see cref="CoolantLoop"/>.
    ///
    /// The host owns all three inputs, as it does for <see cref="HeatPumpDevice"/>: the terminal's
    /// speed setting, whether the block is switched on and functional, and how much of the power it
    /// asked for the grid actually supplied.
    /// </summary>
    public class CoolantPump
    {
        /// <summary>The pump block.</summary>
        public BlockInstance Block;

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
        /// This pump's share of the ring's flow, in units of one pump at full speed.
        ///
        /// Speed times what the grid supplied: a pump set to half speed on a browned-out grid moves
        /// half of half. <see cref="CoolantLoop.RefreshFlow"/> takes the square root of the sum, so
        /// this is a demand rather than a flow.
        /// </summary>
        public float Contribution
        {
            get
            {
                if (!Enabled) return 0f;
                return Clamp01(Speed) * Clamp01(PowerAvailable);
            }
        }

        /// <summary>
        /// Electricity this pump wants at its current setting, W.
        ///
        /// The affinity law: a centrifugal pump's shaft power goes with the cube of its speed, so half
        /// speed costs an eighth. That is real pump behaviour and it is the most interesting number on
        /// the block — because flow only goes as the square root of combined pumping, two pumps at
        /// half speed move about 1.4 times one pump's flow for a quarter of its power. A player who
        /// over-builds their plumbing and throttles it back is rewarded for it, and a player who
        /// runs one pump flat out to cool a big ship pays the worst rate available.
        /// </summary>
        public float DemandWatts
        {
            get
            {
                if (!Enabled) return 0f;

                float speed = Clamp01(Speed);
                return MaxPowerWatts * speed * speed * speed;
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
