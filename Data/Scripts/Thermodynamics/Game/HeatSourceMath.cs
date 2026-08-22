using System;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// How much of a point source reaches a place, and from which way.
    ///
    /// <para>
    /// This is the arithmetic of the whole mechanism — a source radiates <c>P / 4πr²</c> and the
    /// solver weights that by which faces look at it — and until now it lived inside
    /// <see cref="ThermalHeatSources.Sample"/>, in a file that cannot be compiled without the game
    /// assemblies and therefore has never been tested. The registry around it is covered; the
    /// inverse square, the near-field clamp and the range cutoff were not.
    /// </para>
    ///
    /// <para>
    /// That matters most for the thing the mechanism is for. A modder's thermal missile, a bonfire,
    /// an explosion — all of them are judged by whether the heat falls off correctly with distance,
    /// and a source that delivered its full output at any range, or nothing past one metre, would
    /// look plausible in every existing test.
    /// </para>
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
