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
        /// Which way round the ring this pump pushes: +1 with the ring's own order, -1 against it.
        ///
        /// Set from the block's orientation when the loop is traced — a pump drives fluid out of its
        /// outlet port, and whether that port faces the next pipe in the ring or the previous one is
        /// the whole of it. A pump fitted the other way round is not broken, it drives the loop
        /// backwards, and a loop driven backwards works exactly as well.
        ///
        /// Two pumps facing each other therefore cancel, which is worth knowing before building it:
        /// the ring holds coolant, the pumps draw their power, and nothing circulates.
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
        /// This pump's share of the ring's flow, in units of one pump at full speed, signed by
        /// <see cref="Direction"/>.
        ///
        /// Speed times what the grid supplied: a pump set to half speed on a browned-out grid moves
        /// half of half. <see cref="CoolantLoop.RefreshFlow"/> sums these and takes the square root of
        /// the magnitude, so this is a demand rather than a flow, and opposed pumps subtract.
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
        /// Electricity this pump wants at its current setting, W. Linear in speed.
        ///
        /// A real centrifugal pump follows the affinity law — shaft power with the cube of speed — and
        /// this was written that way first. Combined with flow going as the square root of combined
        /// pumping it produced an exploit rather than a trade: getting a given flow from N pumps costs
        /// <c>maxPower x K^3 / N^2</c>, so ten pumps idling at a tenth each cost a hundredth of one
        /// pump working, and the optimal build was always "more pumps, all barely on".
        ///
        /// Linear closes it exactly. Flow F needs <c>sum of speeds = (F/base)^2</c>, so the bill is
        /// <c>maxPower x (F/base)^2</c> — a function of the flow alone, with the pump count cancelled
        /// out. Doubling flow costs four times the power however it is arranged, and a second pump
        /// buys redundancy and headroom rather than a discount.
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
