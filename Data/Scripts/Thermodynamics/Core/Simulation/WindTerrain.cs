using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// What the shape of the ground does to the wind blowing over it: speed-up, sheltering and
    /// channelling. All three are read off one ring of sixteen height samples around the site — its
    /// mean is exposure, its upwind arc is shelter, its second harmonic is the valley axis. Not a
    /// flow solver, and nothing here conserves mass. See environment.md, Terrain.
    /// </summary>
    public static class WindTerrain
    {
        /// <summary>Bearings sampled around a site. Eight is a compass rose.</summary>
        public const int Bearings = 8;

        /// <summary>Radii sampled along each bearing: one close in, one further out.</summary>
        public const int Radii = 2;

        /// <summary>Heights a full sample holds.</summary>
        public const int SampleCount = Bearings * Radii;

        /// <summary>
        /// The most a rise may add, as a fraction. Wind loading codes cap what they will credit to
        /// topography for the same reason this does: the linear theory the figure comes from stops
        /// being true on exactly the steep ground that produces the largest numbers.
        /// </summary>
        public const float MaximumSpeedUp = 0.6f;

        /// <summary>The most a hollow may take away, as a fraction.</summary>
        public const float MaximumSlowDown = 0.45f;

        /// <summary>Speed-up per unit of slope, from the <c>2H/L</c> of linearised flow theory.</summary>
        public const float SpeedUpPerSlope = 2f;

        /// <summary>The most sheltering may take away, as a fraction, in a complete wind shadow.</summary>
        public const float MaximumShelter = 0.75f;

        /// <summary>
        /// Upwind horizon angle at which sheltering is complete, in degrees. A wall at 40° above the
        /// horizon upwind is, for these purposes, all of the sky the wind was coming from.
        /// </summary>
        public const float FullShelterDegrees = 40f;

        /// <summary>
        /// Cross-valley slope at which channelling is total — the wind ends up along the valley
        /// whatever it was doing. A one-in-three side is a valley in anyone's terms.
        /// </summary>
        public const float FullChannelSlope = 0.33f;

        /// <summary>
        /// The bearing of a ring sample, as a unit vector in the local tangent plane.
        ///
        /// Index 0 is north and they run clockwise — east at 2, south at 4 — so a sample's index is
        /// its compass point. Both vectors must be tangent to the surface and perpendicular.
        /// </summary>
        public static Vector3 BearingDirection(int bearing, Vector3 north, Vector3 east)
        {
            double angle = (bearing % Bearings) * (2d * Math.PI / Bearings);
            return (north * (float)Math.Cos(angle)) + (east * (float)Math.Sin(angle));
        }

        /// <summary>Index into a sample array. Rings are stored one radius at a time.</summary>
        public static int Index(int radius, int bearing)
        {
            return (radius * Bearings) + (bearing % Bearings);
        }

        /// <summary>
        /// How much this site stands above the land around it, as a slope. Positive on a rise,
        /// negative in a hollow, near zero on a plain — and on an even hillside, which is not
        /// sheltered by being one.
        /// </summary>
        /// <param name="heights">
        /// Ground height at each sample relative to the site's own ground, m.
        /// <see cref="SampleCount"/> of them.
        /// </param>
        /// <param name="radius">Radius of the outer ring, m.</param>
        public static float Relief(float[] heights, float radius)
        {
            if (heights == null || heights.Length < SampleCount || radius <= 0f) return 0f;

            float total = 0f;
            for (int bearing = 0; bearing < Bearings; bearing++)
            {
                total += heights[Index(Radii - 1, bearing)];
            }

            // Negated: the ring lying *below* the site is the site standing above the ring.
            return -(total / Bearings) / radius;
        }

        /// <summary>
        /// The speed multiplier a site's exposure earns it. One on flat ground, more on a rise, less
        /// in a hollow.
        /// </summary>
        public static float SpeedUp(float relief)
        {
            float change = SpeedUpPerSlope * relief;

            if (change > MaximumSpeedUp) change = MaximumSpeedUp;
            if (change < -MaximumSlowDown) change = -MaximumSlowDown;

            return 1f + change;
        }

        /// <summary>
        /// The speed multiplier left after whatever stands upwind, 0..1: the largest upward angle over
        /// both radii, which is why a near obstruction shelters more than a far one of the same height.
        /// </summary>
        /// <param name="heights">Relative heights, as for <see cref="Relief"/>.</param>
        /// <param name="innerRadius">Radius of the inner ring, m.</param>
        /// <param name="outerRadius">Radius of the outer ring, m.</param>
        /// <param name="wind">Where the wind blows, tangent to the surface. Length ignored.</param>
        /// <param name="north">Local north, unit, tangent.</param>
        /// <param name="east">Local east, unit, tangent.</param>
        public static float Shelter(
            float[] heights, float innerRadius, float outerRadius,
            Vector3 wind, Vector3 north, Vector3 east)
        {
            if (heights == null || heights.Length < SampleCount) return 1f;
            if (innerRadius <= 0f || outerRadius <= 0f) return 1f;
            if (wind.LengthSquared() < 1e-8f) return 1f;

            // Upwind: the direction the wind is arriving from.
            Vector3 upwind = -Vector3.Normalize(wind);

            double bearing = Bearing(upwind, north, east);
            float steepest = 0f;

            for (int radius = 0; radius < Radii; radius++)
            {
                float height = Interpolate(heights, radius, bearing);
                if (height <= 0f) continue;

                float distance = radius == 0 ? innerRadius : outerRadius;
                float angle = (float)(Math.Atan2(height, distance) * 180d / Math.PI);

                if (angle > steepest) steepest = angle;
            }

            if (steepest <= 0f) return 1f;

            float share = steepest / FullShelterDegrees;
            if (share > 1f) share = 1f;

            return 1f - (MaximumShelter * share);
        }

        /// <summary>
        /// The direction the wind actually takes here, from the ring's second harmonic
        /// <c>h(θ) ≈ mean + A·cos(2(θ − φ))</c> — the shape a valley or a ridge makes. A harmonic
        /// rather than the lowest of four opposite pairs, which would quantise the answer to 45°.
        /// The rule is <b>turn toward the lowest ground</b>, which gives a valley an along-floor wind
        /// and a ridge crest a crossing one. See environment.md, Terrain.
        /// </summary>
        /// <param name="strength">
        /// How much of the effect to apply, 0..1, for a caller that wants to turn it down.
        /// </param>
        /// <returns>
        /// A unit vector, or the input direction where there is nothing to channel it. Zero if the
        /// input was zero.
        /// </returns>
        public static Vector3 Channel(
            float[] heights, float radius, Vector3 wind, Vector3 north, Vector3 east, float strength)
        {
            if (wind.LengthSquared() < 1e-8f) return Vector3.Zero;

            Vector3 direction = Vector3.Normalize(wind);
            if (heights == null || heights.Length < SampleCount) return direction;
            if (radius <= 0f || strength <= 0f) return direction;

            // Fit the second harmonic over the outer ring.
            double cosine = 0d;
            double sine = 0d;

            for (int bearing = 0; bearing < Bearings; bearing++)
            {
                double angle = bearing * (2d * Math.PI / Bearings);
                float height = heights[Index(Radii - 1, bearing)];

                cosine += height * Math.Cos(2d * angle);
                sine += height * Math.Sin(2d * angle);
            }

            cosine *= 2d / Bearings;
            sine *= 2d / Bearings;

            double amplitude = Math.Sqrt((cosine * cosine) + (sine * sine));
            if (amplitude < 1e-4d) return direction;

            float confinement = (float)(amplitude / radius) / FullChannelSlope;
            if (confinement > 1f) confinement = 1f;
            confinement *= strength > 1f ? 1f : strength;

            if (confinement <= 0f) return direction;

            // φ is where the ring stands highest; the valley runs across it.
            double high = 0.5d * Math.Atan2(sine, cosine);
            double along = high + (Math.PI * 0.5d);

            Vector3 axis = (north * (float)Math.Cos(along)) + (east * (float)Math.Sin(along));
            if (axis.LengthSquared() < 1e-8f) return direction;

            axis = Vector3.Normalize(axis);
            if (Vector3.Dot(axis, direction) < 0f) axis = -axis;

            Vector3 turned = (direction * (1f - confinement)) + (axis * confinement);
            return turned.LengthSquared() < 1e-8f ? direction : Vector3.Normalize(turned);
        }

        /// <summary>
        /// Which way the ground falls away from here, and how steeply: the ring's <b>first</b>
        /// harmonic, which a valley has none of where <see cref="Channel"/>'s second has none on a
        /// hillside. Costs eight multiply-adds over heights already in memory.
        /// See environment.md, Slope winds.
        /// </summary>
        /// <param name="slope">
        /// How steeply it falls, as a gradient: the harmonic's amplitude over the ring radius. Zero
        /// on ground with no consistent fall line.
        /// </param>
        /// <returns>A unit vector pointing downhill, or zero where the ground has no fall line.</returns>
        public static Vector3 Downhill(
            float[] heights, float radius, Vector3 north, Vector3 east, out float slope)
        {
            slope = 0f;

            if (heights == null || heights.Length < SampleCount) return Vector3.Zero;
            if (radius <= 0f) return Vector3.Zero;

            double cosine = 0d;
            double sine = 0d;

            for (int bearing = 0; bearing < Bearings; bearing++)
            {
                double angle = bearing * (2d * Math.PI / Bearings);
                float height = heights[Index(Radii - 1, bearing)];

                cosine += height * Math.Cos(angle);
                sine += height * Math.Sin(angle);
            }

            cosine *= 2d / Bearings;
            sine *= 2d / Bearings;

            double amplitude = Math.Sqrt((cosine * cosine) + (sine * sine));
            if (amplitude < 1e-4d) return Vector3.Zero;

            slope = (float)(amplitude / radius);

            // φ points at the high side; the ground falls the other way.
            double high = Math.Atan2(sine, cosine);
            double down = high + Math.PI;

            Vector3 direction = (north * (float)Math.Cos(down)) + (east * (float)Math.Sin(down));
            return direction.LengthSquared() < 1e-8f ? Vector3.Zero : Vector3.Normalize(direction);
        }

        /// <summary>A direction's bearing in radians clockwise from north, 0..2π.</summary>
        private static double Bearing(Vector3 direction, Vector3 north, Vector3 east)
        {
            double angle = Math.Atan2(Vector3.Dot(direction, east), Vector3.Dot(direction, north));
            if (angle < 0d) angle += 2d * Math.PI;
            return angle;
        }

        /// <summary>The ring's height at an arbitrary bearing, between its two nearest samples.</summary>
        private static float Interpolate(float[] heights, int radius, double bearing)
        {
            double step = 2d * Math.PI / Bearings;
            double position = bearing / step;

            int low = (int)Math.Floor(position);
            float fraction = (float)(position - low);

            int a = ((low % Bearings) + Bearings) % Bearings;
            int b = (a + 1) % Bearings;

            return (heights[Index(radius, a)] * (1f - fraction))
                + (heights[Index(radius, b)] * fraction);
        }
    }
}
