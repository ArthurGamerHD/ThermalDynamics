using System;

namespace Thermodynamics.Core
{
    /// <summary>
    /// How wind changes with height above the ground, and with the time of day.
    ///
    /// <see cref="WindField"/> answers what the wind does across a planet. This answers what it does
    /// in the first kilometre above one point of it, which is where every grid in the game actually
    /// sits and where the game's own figure — the planet's maximum scaled linearly by air density —
    /// says nothing at all.
    ///
    /// <para><b>The vertical profile.</b> In the atmospheric surface layer, wind follows a
    /// logarithmic profile: <c>u(z) = (u*/κ)·ln(z/z₀)</c>, where z₀ is the roughness length, roughly
    /// a tenth of the height of whatever covers the ground. Expressed as a multiplier against a
    /// reference height — 10 m, the meteorological standard — that is
    /// <c>ln((z+z₀)/z₀) / ln((z_ref+z₀)/z₀)</c>, which is what this computes. It is steep near the
    /// ground and flattens fast: over open country a block at 2 m sees about three quarters of the
    /// 10 m wind, and one at 100 m about a third more than it.</para>
    ///
    /// <para>The logarithm cannot keep climbing forever. It holds only through the surface layer;
    /// above the boundary layer the wind is set by the pressure field rather than by the ground, so
    /// the profile is flattened at a gradient height. **Wind therefore strengthens with altitude for
    /// a time and then stops** — and above that, the game's own air density is already taking the
    /// ceiling down to nothing, so the whole curve is a rise, a plateau, and a fall.</para>
    ///
    /// <para><b>The diurnal cycle,</b> which is the part that surprises people. Surface wind and
    /// wind aloft run in opposite directions over a day. By day the sun heats the ground, convection
    /// mixes the boundary layer, and momentum from aloft is dragged down to the surface: **surface
    /// wind peaks in the afternoon**. After sunset the ground cools, the mixing stops, and the air
    /// above decouples from the friction that was holding it back — it accelerates into a
    /// **nocturnal low-level jet** while the surface below goes calm. The two are anticorrelated,
    /// and they cross over at a height of a few tens of metres where the daily variation vanishes.
    /// A player who lands at dusk and walks out into still air, then flies to two hundred metres and
    /// meets a gale, is seeing the real thing.</para>
    ///
    /// <para>Everything here is a multiplier, pure, and free of the game.</para>
    /// </summary>
    public static class WindProfile
    {
        /// <summary>
        /// The height the profile is normalised against, m. Ten metres is the height the world's
        /// weather stations measure wind at, which makes every figure elsewhere in the model
        /// comparable with a real one.
        /// </summary>
        public const float ReferenceHeight = 10f;

        /// <summary>
        /// The lowest height the profile is evaluated at, m. The logarithmic profile goes to zero at
        /// the roughness length — correct, since air does not slip along the ground — but a grid
        /// resting *on* the ground would then convect into perfectly still air. A block has size;
        /// half a metre is the smallest height that means anything about one.
        /// </summary>
        public const float MinimumHeight = 0.5f;

        /// <summary>
        /// Wind at a height above the ground, as a multiple of the wind at
        /// <see cref="ReferenceHeight"/>.
        /// </summary>
        /// <param name="height">Metres above the ground, not above sea level.</param>
        /// <param name="roughness">
        /// Roughness length z₀, m. About a tenth of the height of what covers the ground: 0.0002 for
        /// open water, 0.03 for open grassland, 0.1 for scattered obstacles, 0.5 for forest or a
        /// built-up area.
        /// </param>
        /// <param name="gradientHeight">
        /// Height at which the profile stops climbing, m — the top of the boundary layer. Several
        /// hundred metres over open country, higher over rough ground.
        /// </param>
        public static float Multiplier(float height, float roughness, float gradientHeight)
        {
            if (roughness <= 0f) roughness = 0.0002f;
            if (gradientHeight < ReferenceHeight) gradientHeight = ReferenceHeight;

            if (height < MinimumHeight) height = MinimumHeight;
            if (height > gradientHeight) height = gradientHeight;

            double reference = Math.Log((ReferenceHeight + roughness) / roughness);
            if (reference <= 0d) return 1f;

            return (float)(Math.Log((height + roughness) / roughness) / reference);
        }

        /// <summary>
        /// How much of the day's heating has accumulated, 0..1, given the sun's height now and the
        /// value this returned last time.
        ///
        /// It is the sun's elevation put through the same first-order lag the ambient temperature
        /// uses, and for the same reason: the mixing that brings wind down to the surface is driven
        /// by the ground being warm, and the ground is warmest well after noon. Sharing the lag is
        /// what makes the windiest part of the afternoon line up with the warmest part, rather than
        /// with the sun's own high point.
        /// </summary>
        /// <param name="previous">What this returned last step, or a negative number on the first.</param>
        /// <param name="sunElevationSine">Sine of the sun's angle above the horizon.</param>
        /// <param name="seconds">Seconds since the previous value.</param>
        /// <param name="lagSeconds">The climate's own lag.</param>
        public static float Heating(
            float previous, float sunElevationSine, float seconds, float lagSeconds)
        {
            float target = sunElevationSine <= 0f ? 0f : sunElevationSine;
            if (target > 1f) target = 1f;

            // A lag with no history is not lagging, it is starting from a lie — the same fault that
            // once had every grid in a world chasing 2.7 K for the first three minutes of a session.
            if (previous < 0f) return target;

            return ClimateModel.Follow(previous, target, seconds, lagSeconds);
        }

        /// <summary>
        /// The time-of-day multiplier at a height, given how much the ground has been heated.
        ///
        /// One at the crossover height whatever the hour; above one in the afternoon and below one
        /// before dawn near the ground; and the other way round above the crossover, where the
        /// nocturnal jet lives.
        /// </summary>
        /// <param name="heating">0 at the coldest hour, 1 at peak afternoon. See <see cref="Heating"/>.</param>
        /// <param name="height">Metres above the ground.</param>
        /// <param name="amplitude">
        /// How far the multiplier swings either side of one, 0..1. Real surface wind commonly varies
        /// by a third or more between afternoon and pre-dawn.
        /// </param>
        /// <param name="crossover">
        /// Height at which the daily swing vanishes, m. Below it the surface cycle; above it, and
        /// fully reversed by twice it, the jet.
        /// </param>
        /// <param name="boundary">
        /// Top of the boundary layer, m — the same gradient height the profile flattens at. The
        /// daily cycle is a boundary-layer phenomenon: it is the ground heating and cooling that
        /// drives it, and air that has left the ground's influence does not have one. Fades to
        /// nothing between this height and twice it.
        ///
        /// This is not a detail. Without it the reversal saturates and never returns, so a ship at
        /// seven kilometres reads a nocturnal jet twelve times higher than any jet has ever been —
        /// which is exactly what a field dump found it doing.
        /// </param>
        public static float Diurnal(
            float heating, float height, float amplitude, float crossover, float boundary)
        {
            if (amplitude <= 0f) return 1f;
            if (amplitude > 1f) amplitude = 1f;
            if (crossover <= 0f) return 1f;

            if (heating < 0f) heating = 0f;
            if (heating > 1f) heating = 1f;
            if (height < 0f) height = 0f;

            // +1 on the ground, 0 at the crossover, −1 at twice it and above: the surface layer and
            // the layer above it, and the fact that between them is a height where nothing changes.
            float side = 1f - (height / crossover);
            if (side < -1f) side = -1f;

            // −1 at the coldest hour, +1 at the warmest.
            float swing = (2f * heating) - 1f;

            // Out of the boundary layer and the cycle is simply not there.
            float reach = 1f;
            if (boundary > 0f && height > boundary)
            {
                reach = 2f - (height / boundary);
                if (reach < 0f) reach = 0f;
            }

            float factor = 1f + (amplitude * side * swing * reach);
            return factor < 0f ? 0f : factor;
        }
    }
}
