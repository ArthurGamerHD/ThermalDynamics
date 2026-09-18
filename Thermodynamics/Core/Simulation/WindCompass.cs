using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// The wind in the viewer's frame: a bearing relative to where they face, and the head and cross
    /// components it resolves into. Kept apart from the drawing so it can be tested without a session.
    /// Every angle is on the horizontal plane, since the wind is tangent to the surface.
    /// **A bearing points where the wind is going**, which is the opposite of meteorology's convention
    /// and the right one for a readout answering which way you are being pushed.
    /// </summary>
    public static class WindCompass
    {
        /// <summary>
        /// Below this a projected vector is treated as having no direction. Squared length, so it
        /// is a hundredth of a metre per second against a field that runs to tens.
        /// </summary>
        private const float MinimumLengthSquared = 1e-4f;

        /// <summary>
        /// Where the wind blows, in degrees clockwise from the way the viewer faces: 0 straight
        /// ahead, 90 to the right, 180 into the viewer's face, 270 to the left.
        ///
        /// Both vectors are flattened onto the plane the viewer stands on before the angle is
        /// taken, so looking at the sky does not swing the needle.
        /// </summary>
        /// <param name="wind">Where the wind blows, world space. Length is ignored.</param>
        /// <param name="forward">The way the viewer faces, world space.</param>
        /// <param name="up">Away from the planet's centre at the viewer, world space.</param>
        /// <param name="degrees">The bearing, 0..360. Zero when this returns false.</param>
        /// <returns>
        /// False when there is no bearing to give: no wind, or a viewer looking straight up or
        /// straight down, where a facing has no horizontal part to measure against.
        /// </returns>
        public static bool Bearing(Vector3 wind, Vector3 forward, Vector3 up, out float degrees)
        {
            degrees = 0f;

            if (up.LengthSquared() < MinimumLengthSquared) return false;
            up = Vector3.Normalize(up);

            Vector3 windFlat = Flatten(wind, up);
            Vector3 faceFlat = Flatten(forward, up);

            if (windFlat.LengthSquared() < MinimumLengthSquared) return false;
            if (faceFlat.LengthSquared() < MinimumLengthSquared) return false;

            windFlat = Vector3.Normalize(windFlat);
            faceFlat = Vector3.Normalize(faceFlat);

            // Right of the facing, taken on the same plane. Clockwise as seen from above is the
            // direction a compass rose turns, which is what makes a positive angle read as "to the
            // right" rather than needing a sign convention explained on screen.
            Vector3 right = Vector3.Cross(faceFlat, up);
            if (right.LengthSquared() < MinimumLengthSquared) return false;
            right = Vector3.Normalize(right);

            double angle = Math.Atan2(Vector3.Dot(windFlat, right), Vector3.Dot(windFlat, faceFlat));
            degrees = Normalise((float)(angle * 180d / Math.PI));
            return true;
        }

        /// <summary>
        /// The part of the wind blowing the way the viewer faces, m/s. Negative is a headwind, which
        /// is the sign that matters: a pilot reads the minus rather than the word.
        /// </summary>
        public static float Along(Vector3 wind, Vector3 forward, Vector3 up)
        {
            if (up.LengthSquared() < MinimumLengthSquared) return 0f;
            up = Vector3.Normalize(up);

            Vector3 faceFlat = Flatten(forward, up);
            if (faceFlat.LengthSquared() < MinimumLengthSquared) return 0f;

            return Vector3.Dot(Flatten(wind, up), Vector3.Normalize(faceFlat));
        }

        /// <summary>
        /// The part blowing across the facing, m/s. Positive is to the right, matching the bearing.
        /// </summary>
        public static float Across(Vector3 wind, Vector3 forward, Vector3 up)
        {
            if (up.LengthSquared() < MinimumLengthSquared) return 0f;
            up = Vector3.Normalize(up);

            Vector3 faceFlat = Flatten(forward, up);
            if (faceFlat.LengthSquared() < MinimumLengthSquared) return 0f;

            Vector3 right = Vector3.Cross(Vector3.Normalize(faceFlat), up);
            if (right.LengthSquared() < MinimumLengthSquared) return 0f;

            return Vector3.Dot(Flatten(wind, up), Vector3.Normalize(right));
        }

        /// <summary>
        /// The bearing as one of eight sectors, 0 dead ahead and counting clockwise. For a readout
        /// that names a direction rather than drawing one: an arrow can carry a degree, a line of
        /// text cannot.
        /// </summary>
        public static int Sector(float degrees)
        {
            degrees = Normalise(degrees);

            // Half a sector of offset, so the sector is centred on its own bearing rather than
            // starting at it — otherwise a wind two degrees left of dead ahead reads as ahead-left.
            return ((int)((degrees + 22.5f) / 45f)) & 7;
        }

        /// <summary>The eight sectors named, in the order <see cref="Sector"/> returns them.</summary>
        public static string SectorName(int sector)
        {
            switch (sector & 7)
            {
                case 0: return "ahead";
                case 1: return "ahead right";
                case 2: return "right";
                case 3: return "astern right";
                case 4: return "astern";
                case 5: return "astern left";
                case 6: return "left";
                default: return "ahead left";
            }
        }

        /// <summary>The component of a vector lying in the plane whose normal is <paramref name="up"/>.</summary>
        private static Vector3 Flatten(Vector3 vector, Vector3 up)
        {
            return vector - (up * Vector3.Dot(vector, up));
        }

        /// <summary>An angle in degrees folded into 0..360.</summary>
        public static float Normalise(float degrees)
        {
            if (float.IsNaN(degrees) || float.IsInfinity(degrees)) return 0f;

            degrees = degrees % 360f;
            return degrees < 0f ? degrees + 360f : degrees;
        }
    }
}
