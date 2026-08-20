using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Slope winds: air running up a mountain by day and draining back down it at night.
    ///
    /// <para>This is a different mechanism from everything else in
    /// <see cref="WindTerrain"/>. Speed-up, sheltering and channelling are <b>mechanical</b> — they
    /// are what the ground does to wind that was already blowing. A slope wind is <b>thermal</b>:
    /// the ground makes it, out of nothing, and it blows even when the air is otherwise still.</para>
    ///
    /// <list type="bullet">
    /// <item><b>Anabatic, by day.</b> Sunlight heats a slope, the air against it warms, becomes
    /// buoyant, and rises <i>along the ground</i> rather than straight up — so the flow is toward the
    /// summit. Typically 3–5 m/s, hundreds of metres deep, starting after sunrise and strongest in
    /// the afternoon.</item>
    /// <item><b>Katabatic, by night.</b> The slope radiates its heat to the sky, the air against it
    /// cools and grows dense, and gravity drains it downhill to pool in the valley. Typically
    /// 3–8 m/s — much more where it runs over ice — and <b>much shallower</b>, 10 to 100 m, roughly
    /// a twentieth of the drop it has fallen.</item>
    /// </list>
    ///
    /// <para><b>The condition that matters most for a game:</b> both are weak-wind phenomena. They
    /// form under calm, clear, high-pressure conditions, and a real synoptic wind simply overruns
    /// them. So this fades out as the ambient wind rises — which is also what keeps it from
    /// compounding into the storm case that already runs too strong.</para>
    ///
    /// <para>Pure, and cheap by construction: the fall line comes from
    /// <see cref="WindTerrain.Downhill"/>, which is eight multiply-adds over heights already in
    /// memory, and the time of day comes from the heating figure the diurnal profile already
    /// computes. No new terrain lookups, no new state.</para>
    /// </summary>
    public static class WindSlope
    {
        /// <summary>Upslope speed at a full slope in full afternoon heating, m/s.</summary>
        public const float AnabaticSpeed = 4f;

        /// <summary>
        /// Downslope speed at a full slope on a cold clear night, m/s. Stronger than the upslope
        /// flow: buoyancy driving cold air downhill beats buoyancy dragging warm air up it.
        /// </summary>
        public const float KatabaticSpeed = 6f;

        /// <summary>
        /// The gradient at which a slope wind reaches full strength. One in four is a mountainside;
        /// gentler ground gets a proportionally weaker flow, flat ground gets none.
        /// </summary>
        public const float FullSlope = 0.25f;

        /// <summary>Height above ground at which the daytime upslope flow has died out, m.</summary>
        public const float AnabaticDepth = 300f;

        /// <summary>
        /// Height at which the night-time drainage flow has died out, m. Far shallower than the
        /// daytime flow, which is the most distinctive thing about it: stand on a slope at night and
        /// the cold air is running past your knees while the air overhead is doing something else.
        /// </summary>
        public const float KatabaticDepth = 80f;

        /// <summary>
        /// Ambient wind at which a slope flow is half overrun, m/s. Slope winds belong to calm
        /// nights and still afternoons; a real wind erases them.
        /// </summary>
        public const float CalmSpeed = 5f;

        /// <summary>
        /// The slope wind at a point, as a velocity to add to the ambient wind.
        ///
        /// Zero where there is no slope, no strength, or too much wind already.
        /// </summary>
        /// <param name="downhill">Unit vector pointing downhill. From <see cref="WindTerrain.Downhill"/>.</param>
        /// <param name="slope">The gradient of that fall, as a fraction.</param>
        /// <param name="heating">
        /// 0 at the coldest hour, 1 at peak afternoon — the same lagged figure the diurnal profile
        /// runs on, so the upslope flow starts when the ground actually warms rather than at dawn.
        /// </param>
        /// <param name="height">Metres above the ground.</param>
        /// <param name="ambientSpeed">The wind that is blowing here anyway, m/s.</param>
        /// <param name="strength">A caller's master scale, 0..1.</param>
        public static Vector3 Velocity(
            Vector3 downhill, float slope, float heating, float height,
            float ambientSpeed, float strength)
        {
            if (strength <= 0f) return Vector3.Zero;
            if (slope <= 0f) return Vector3.Zero;
            if (downhill.LengthSquared() < 1e-8f) return Vector3.Zero;

            if (heating < 0f) heating = 0f;
            if (heating > 1f) heating = 1f;
            if (height < 0f) height = 0f;

            // −1 at the coldest hour, +1 at the warmest. Positive is the daytime upslope flow.
            float swing = (2f * heating) - 1f;
            if (swing > -1e-4f && swing < 1e-4f) return Vector3.Zero;

            bool upslope = swing > 0f;

            float depth = upslope ? AnabaticDepth : KatabaticDepth;
            if (height >= depth) return Vector3.Zero;

            float share = slope / FullSlope;
            if (share > 1f) share = 1f;

            float speed = (upslope ? AnabaticSpeed : KatabaticSpeed)
                * Math.Abs(swing)
                * share
                * (1f - (height / depth))
                * Suppression(ambientSpeed)
                * (strength > 1f ? 1f : strength);

            if (speed <= 0f) return Vector3.Zero;

            // Uphill is the way the ground does not fall.
            Vector3 direction = upslope ? -downhill : downhill;
            return Vector3.Normalize(direction) * speed;
        }

        /// <summary>
        /// How much of a slope wind survives an ambient wind of this speed, 0..1.
        ///
        /// A gentle hyperbola rather than a cutoff, so a ship flying out of a calm valley into a
        /// breeze does not step off the end of the effect.
        /// </summary>
        public static float Suppression(float ambientSpeed)
        {
            if (ambientSpeed <= 0f) return 1f;
            return CalmSpeed / (CalmSpeed + ambientSpeed);
        }

        /// <summary>
        /// Which way a slope wind is blowing here, for a readout: +1 upslope, −1 downslope, 0 none.
        /// </summary>
        public static int Sense(float heating, float height)
        {
            float swing = (2f * heating) - 1f;
            if (swing > 0f) return height < AnabaticDepth ? 1 : 0;
            if (swing < 0f) return height < KatabaticDepth ? -1 : 0;
            return 0;
        }
    }
}
