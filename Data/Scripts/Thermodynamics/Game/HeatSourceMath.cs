using System;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// How much of a point source reaches a place, and from which way: <c>P / 4πr²</c>, the near-field
    /// clamp and the range cutoff. Held apart from <see cref="ThermalHeatSources"/>, which cannot be
    /// compiled without the game assemblies, so the arithmetic the whole mechanism is judged on can be
    /// tested. See api.md, Heat sources.
    /// </summary>
    public static class HeatSourceMath
    {
        /// <summary>
        /// Below this many metres the inverse square law diverges, so a source at zero distance
        /// delivers its full output and no more rather than an infinity.
        /// </summary>
        public const double NearFieldMetres = 1.0;

        /// <summary>
        /// Watts per square metre arriving at <paramref name="at"/>, or zero when the source is
        /// out of range or making nothing.
        /// </summary>
        public static float Irradiance(Vector3D source, float watts, float range, Vector3D at)
        {
            if (watts <= 0f || range <= 0f) return 0f;

            double distanceSquared = (source - at).LengthSquared();
            if (distanceSquared > range * (double)range) return 0f;

            if (distanceSquared < NearFieldMetres) distanceSquared = NearFieldMetres;

            return (float)(watts / (4.0 * Math.PI * distanceSquared));
        }

        /// <summary>
        /// The direction a source lies in, in the grid's own frame, so the solver can ask which
        /// faces are looking at it. Zero when the two positions coincide.
        /// </summary>
        public static Vector3 Direction(Vector3D source, Vector3D at, ref MatrixD worldToLocal)
        {
            Vector3D delta = source - at;
            if (delta.LengthSquared() <= 0.0) return Vector3.Zero;

            return (Vector3)Vector3D.TransformNormal(Vector3D.Normalize(delta), worldToLocal);
        }
    }
}
