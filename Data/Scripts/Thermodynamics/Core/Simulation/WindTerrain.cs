using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// What the shape of the ground does to the wind blowing over it.
    ///
    /// Three effects, which between them are most of what a person standing outside actually
    /// notices, and none of which the game's own wind figure has any concept of:
    ///
    /// <list type="bullet">
    /// <item><b>Speed-up.</b> Air driven over a rise is squeezed between the hill and the flow above
    /// it and has to accelerate. Linearised flow theory — Jackson and Hunt, the basis of every wind
    /// atlas since — gives a fractional speed-up of roughly <c>2H/L</c> for a hill of height H and
    /// half-length L, which is why a summit commonly runs two to three times the wind of the valley
    /// under it. Wind loading codes cap the figure they will admit from this; so does
    /// <see cref="MaximumSpeedUp"/>.</item>
    ///
    /// <item><b>Sheltering.</b> Behind a ridge is a wind shadow, and the deeper the obstruction sits
    /// in the upwind sky the less wind arrives. This is measured here the way snow science measures
    /// it: the greatest upward angle from the site to the terrain upwind of it.</item>
    ///
    /// <item><b>Channelling.</b> A valley steers the wind along itself almost regardless of what the
    /// wind was doing before it arrived, and a narrowing valley accelerates the flow through it. The
    /// steering is the larger effect by far, and it is the one that makes terrain legible from the
    /// air: wind that follows the ground looks like weather, and wind that ignores it looks like a
    /// texture laid over a landscape.</item>
    /// </list>
    ///
    /// <para><b>The terrain is read as a ring of samples around the site.</b> Eight compass bearings
    /// at two radii, each giving the ground's height relative to the site's own — negative where the
    /// land falls away, positive where it rises. That is sixteen height lookups, which is what makes
    /// this affordable per grid, and it is enough to answer all three questions: the mean of the ring
    /// is exposure, the upwind arc is shelter, and the ring's second harmonic is the valley
    /// axis.</para>
    ///
    /// <para>It is not a flow solver, and nothing here conserves mass. It is the same bargain the
    /// rest of this model makes: the shapes a player can see, at a cost a game can pay.</para>
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
        /// How much this site stands above the land around it, as a slope: the mean fall from the
        /// site to its outer ring, divided by that ring's radius.
        ///
        /// Positive on a rise, negative in a hollow, and near zero on a plain or on an even slope —
        /// a hillside is not sheltered by being a hillside, and this correctly says so, because the
        /// ground rises as much on one side as it falls on the other.
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
        /// The speed multiplier left after whatever stands upwind, 0..1.
        ///
        /// The upwind direction is taken from where the wind is coming from, the samples straddling
        /// that bearing are interpolated, and the largest upward angle over both radii decides. A
        /// near obstruction shelters more than a far one of the same height, which falls out of
        /// using the angle rather than the height.
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
        /// The direction the wind actually takes here, after the ground has had its say.
        ///
        /// The ring's second harmonic is fitted — <c>h(θ) ≈ mean + A·cos(2(θ − φ))</c> — which is the
        /// shape a valley or a ridge makes when you walk a circle round a point in one: high on two
        /// opposite sides, low on the other two. φ is the bearing of the high sides, so the valley
        /// runs across it, and A over the radius is how steeply it is walled.
        ///
        /// A second harmonic rather than picking the lowest of four opposite pairs, because the pairs
        /// quantise the answer to 45° and a wind that snaps between eight directions as you walk
        /// looks like a bug. The fit is continuous in both the terrain and the bearing.
        ///
        /// The axis has no sense of its own — a valley runs both ways — so the end nearer the
        /// oncoming wind is the one taken. Wind blowing across a valley is turned to run along it,
        /// which way along being decided by which way it was already leaning.
        ///
        /// The rule is <b>turn toward the lowest ground</b>, and it is worth stating that way because
        /// a ridge is not simply a valley upside down. In a valley the lowest ground lies along the
        /// floor, so the wind runs along it. On a ridge crest the lowest ground is down either side,
        /// so the wind is left crossing the ridge — which is what air over a ridge does. One rule,
        /// two landforms, two different and correct answers.
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
        /// Which way the ground falls away from here, and how steeply.
        ///
        /// The ring's <b>first</b> harmonic — <c>h(θ) ≈ mean + A·cos(θ − φ)</c> — where φ is the
        /// bearing of the highest ground around the site, so downhill is the opposite of it. That is
        /// a different shape from the second harmonic <see cref="Channel"/> fits: a valley is high on
        /// two opposite sides and has no first harmonic at all, while a hillside is high on one side
        /// and has no second. Fitting both means a site on the wall of a valley gets an along-valley
        /// axis *and* a fall line, which is what it really has.
        ///
        /// It costs eight multiply-adds over heights that are already in memory. The expensive part
        /// of knowing about terrain — the sixteen surface lookups — is already paid by the time this
        /// is called, which is what makes slope winds essentially free.
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
