using System;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// How much of a grid the block overlay draws, bounded two ways: boxes outside the camera's cone
    /// are dropped, which changes no pixel, and what is left is held to <c>DebugOverlayMaxBoxes</c> by
    /// a radius fitted each frame. Free of any Space Engineers type beyond VRage.Math, so both are
    /// testable. See configuration.md, The block overlay.
    /// </summary>
    public class OverlayBudget
    {
        /// <summary>Radius standing for no limit. Larger than any grid the game can hold.</summary>
        public const double Unbounded = 1e9;

        /// <summary>Metres below which the radius is not allowed to fall, so something is drawn.</summary>
        public const double MinimumRadius = 6.0;

        /// <summary>
        /// How far from the budget the count may sit before the radius is refitted. Without a
        /// deadband the radius chases its own overshoot and the drawn volume visibly pulses.
        /// </summary>
        public const double Deadband = 0.2;

        public int MaxBoxes = 12000;

        /// <summary>Metres from the eye beyond which a box is not drawn.</summary>
        public double Radius = Unbounded;

        // ---- this frame ---------------------------------------------------------------------
        public int Considered;
        public int Drawn;
        public int OffScreen;

        /// <summary>Candidates inside the radius, drawn or not. What the radius is fitted against.</summary>
        public int WithinRadius;

        /// <summary>Dropped for being beyond the radius.</summary>
        public int Beyond;

        /// <summary>Dropped by the hard cap, inside the radius. Only while the fit is still settling.</summary>
        public int Capped;

        /// <summary>Distance of the furthest candidate inside the radius, metres.</summary>
        public double Furthest;

        /// <summary>Everything the frame wanted to draw and did not.</summary>
        public int OverBudget
        {
            get { return Beyond + Capped; }
        }

        public void BeginFrame()
        {
            Considered = 0;
            Drawn = 0;
            OffScreen = 0;
            WithinRadius = 0;
            Beyond = 0;
            Capped = 0;
            Furthest = 0;
        }

        /// <summary>True while the radius is holding part of the grid back.</summary>
        public bool IsLimiting
        {
            get { return Radius < Unbounded; }
        }

        /// <summary>
        /// Whether a box centred <paramref name="distance"/> metres from the eye is drawn, counting the
        /// decision. The radius decides and the box count is a hard stop behind it; what fell inside
        /// the radius is counted either way, so the fit measures the volume rather than the stop.
        /// </summary>
        public bool Accept(double distance)
        {
            Considered++;

            if (distance > Radius)
            {
                Beyond++;
                return false;
            }

            WithinRadius++;
            if (distance > Furthest) Furthest = distance;

            if (Drawn >= MaxBoxes)
            {
                Capped++;
                return false;
            }

            Drawn++;
            return true;
        }

        /// <summary>Counts a box the camera cannot see. Not a candidate, so not against the budget.</summary>
        public void Cull()
        {
            Considered++;
            OffScreen++;
        }

        /// <summary>
        /// Fits the radius to the budget for the next frame.
        ///
        /// The count inside a radius rises with its cube, since blocks fill a volume, so the
        /// correction is the cube root of how far off the count is. That reaches the budget in a
        /// few frames instead of drifting towards it over a second of play.
        /// </summary>
        public void EndFrame()
        {
            if (MaxBoxes <= 0) return;

            // Nothing was beyond the radius, and with room to spare: the grid fits, so the radius
            // stops limiting. The spare is what keeps this from oscillating against the frame that
            // refits it — a view drawing its whole grid at the budget keeps the radius it has.
            if (Beyond == 0 && Capped == 0)
            {
                if (IsLimiting && WithinRadius <= MaxBoxes * (1.0 - Deadband)) Radius = Unbounded;
                return;
            }

            // Fitted against the furthest candidate inside the radius rather than the radius itself:
            // on the first limited frame the radius is unbounded, and scaling it would do nothing.
            if (Furthest <= 0) return;

            // Aimed under the budget rather than at it, so the steady state has no boxes left to
            // the hard stop — which drops whichever blocks the walk reaches last, and so would show
            // an arbitrary part of the shell missing.
            if (WithinRadius <= MaxBoxes && WithinRadius >= MaxBoxes * (1.0 - Deadband)) return;

            double target = MaxBoxes * (1.0 - (Deadband * 0.5));
            double ratio = WithinRadius <= 0 ? 2.0 : target / WithinRadius;

            Radius = Math.Max(MinimumRadius, Furthest * Math.Pow(ratio, 1.0 / 3.0));
        }

        /// <summary>
        /// Whether a box of the given radius at <paramref name="delta"/> from the eye is inside the
        /// camera's view cone — the cone containing the frustum, so this rejects only what is
        /// certainly off screen. Exact sphere-against-cone.
        /// </summary>
        public static bool InView(
            ref Vector3D delta, double boxRadius, ref Vector3D forward, double sinHalfAngle, double cosHalfAngle)
        {
            double along = Vector3D.Dot(delta, forward);

            // Behind the eye by more than its own size. Kept as a separate test because the cone
            // one alone accepts everything in the mirrored cone behind the camera.
            if (along <= -boxRadius) return false;

            double lengthSquared = delta.LengthSquared();
            double acrossSquared = lengthSquared - (along * along);
            double across = acrossSquared <= 0 ? 0 : Math.Sqrt(acrossSquared);

            return (across * cosHalfAngle) - (along * sinHalfAngle) <= boxRadius;
        }

        /// <summary>
        /// Half-angle of the cone containing a frustum of this vertical field of view and aspect
        /// ratio, radians. The diagonal is the widest part of the frustum, so it sets the cone.
        /// </summary>
        public static double ConeHalfAngle(double verticalFovRadians, double aspect)
        {
            if (verticalFovRadians <= 0) return Math.PI * 0.5;
            if (aspect <= 0) aspect = 1.0;

            double tangent = Math.Tan(Math.Min(verticalFovRadians, Math.PI * 0.98) * 0.5);

            return Math.Atan(tangent * Math.Sqrt(1.0 + (aspect * aspect)));
        }
    }
}
