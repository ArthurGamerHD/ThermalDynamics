namespace Thermodynamics.Core
{
    /// <summary>
    /// What holds a ship under its cruise speed: the resistance force, and the speed ceiling handed
    /// to the engine with it. The other half of
    /// [RelativeTopSpeed](https://github.com/Gauge/RelativeTopSpeed)'s arithmetic, beside
    /// <see cref="CruiseCurve"/> (`K10`).
    ///
    /// <para>
    /// **Here rather than in `ThermalGridTopSpeed` because that file cannot be tested.** It needs a
    /// session, a physics component and a grid group, so everything it decided was decided where no
    /// check could look — and one of those decisions was dead. `EnableSpeedBoost` was read inside a
    /// condition the line above had already returned on, so the switch did nothing at all in any
    /// world, and the mod project compiled it without complaint. That is
    /// `wired-to-nothing` in its plainest form, and the fix is not just the condition: it is moving
    /// the choice somewhere a test can ask what the switch changes.
    /// </para>
    /// </summary>
    public static class TopSpeedForce
    {
        /// <summary>
        /// Newtons of resistance on a group travelling above its cruise speed, along its velocity.
        ///
        /// <para>
        /// `resistance × mass × (1 − cruise / speed)` — the original's, and the shape matters: it is
        /// zero at cruise and approaches `resistance × mass` as speed runs away, so a ship a little
        /// over is nudged and a ship far over is hauled. Nothing is applied at or below cruise,
        /// which is what makes the force a *ceiling* rather than a permanent tax on flying.
        /// </para>
        /// </summary>
        public static float Newtons(float resistance, float mass, float cruise, float speed)
        {
            // Written to reject a NaN speed rather than pass it into a force. A NaN here is a
            // physics component read mid-teleport, and it would reach AddForce.
            if (!(speed > cruise) || !(speed > 0f)) return 0f;
            if (!(mass > 0f) || !(resistance > 0f)) return 0f;

            float over = cruise > 0f ? 1f - (cruise / speed) : 1f;
            return resistance * mass * over;
        }

        /// <summary>
        /// The speed ceiling the engine is given with that force, m/s — <c>AddForce</c>'s
        /// <c>maxSpeed</c>, which clamps the body's velocity rather than capping the force.
        ///
        /// <para>
        /// **This is what `EnableSpeedBoost` decides, and it is the whole of what it decides.** Off,
        /// the ceiling is the cruise speed itself, so a ship cannot pass it at all — the middle rung
        /// of the ladder configuration.md describes. On, the ceiling is the world's boost speed, so
        /// thrust may carry a ship past cruise and this force drags it back, which is what makes a
        /// burst of thrust worth something.
        /// </para>
        ///
        /// <para>
        /// A boost ceiling below the cruise speed would be a ship held under its own cruise by the
        /// boost dial, which is not a rung anybody asked for, so the floor is cruise either way.
        /// `SpeedLimit` is above both and is the engine's own cap, so a boost ceiling set above it
        /// is inert rather than wrong.
        /// </para>
        /// </summary>
        public static float Ceiling(bool boostEnabled, float cruise, float boostCeiling)
        {
            if (!(cruise > 0f)) cruise = 0f;
            if (!boostEnabled) return cruise;

            return boostCeiling > cruise ? boostCeiling : cruise;
        }
    }
}
