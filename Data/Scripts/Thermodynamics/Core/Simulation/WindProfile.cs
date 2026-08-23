using System;

namespace Thermodynamics.Core
{
    /// <summary>
    /// How wind changes with height above the ground and with the time of day, in the first kilometre
    /// where every grid actually sits. A logarithmic surface-layer profile flattened at a gradient
    /// height, and a daily cycle that runs one way at the surface and the other above the crossover.
    /// Everything here is a pure multiplier. See environment.md, The vertical profile and The daily
    /// cycle.
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
        /// <summary>
        /// The boundary layer's top, where the air runs out first.
        ///
        /// <para>
        /// **A boundary layer cannot be taller than the atmosphere it is in.** The shipped
        /// `WindGradientHeight` is 600 m and several of the game's own worlds have less air than
        /// that above their ground — Titan's is 285 m — so the profile was being asked about
        /// heights in vacuum, and the only thing keeping the answer sane was the engine's own wind
        /// ceiling reaching zero first. See backlog `B20`.
        /// </para>
        ///
        /// <para>
        /// <paramref name="airAboveGround"/> is the atmosphere's top measured from the ground under
        /// the grid, not from the mean radius: a ship on a mountain has less air over it than one
        /// in a valley, and on a world whose peaks stand above their own air it has none.
        /// Non-positive leaves the configured height alone, which is the airless case — there is no
        /// wind there for a profile to shape.
        /// </para>
        /// </summary>
        public static float GradientHeightIn(float configured, float airAboveGround)
        {
            if (airAboveGround <= 0f) return configured;
            if (airAboveGround >= configured) return configured;

            return airAboveGround < ReferenceHeight ? ReferenceHeight : airAboveGround;
        }

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
        /// How much of the day's heating has accumulated, 0..1: the sun's elevation through the same
        /// first-order lag the ambient temperature uses, which is what lines the windiest part of the
        /// afternoon up with the warmest.
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
        /// Top of the boundary layer, m. The daily cycle is driven by the ground, so it fades to
        /// nothing between this height and twice it — without which the reversal saturates and a ship
        /// at seven kilometres reads a jet twelve times higher than any jet has ever been.
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
