using System;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// How much of a grid the block overlay draws.
    ///
    /// Every box is six transparent quads and twelve lines, drawn every frame with no occlusion
    /// test, so a capital ship of forty thousand blocks asks the renderer for over half a million
    /// billboards a frame and the game stops being playable. Two mechanisms bound that.
    ///
    /// **Off-screen boxes are dropped.** The overlay scales each box about the eye onto a band in
    /// front of the near plane, which preserves where a block appears but not whether it appears at
    /// all: a block behind the player's shoulder still costs its billboards. A box outside the
    /// camera's cone contributes nothing to the picture, so it is not drawn. This changes no pixel.
    ///
    /// **What is left is bounded by a radius.** When a grid still exceeds the budget, the nearest
    /// part of it is drawn and the radius is fitted, frame by frame, to whatever keeps the count
    /// near the budget. It converges in a few frames after the camera moves, and lifts back to
    /// unbounded on a grid small enough not to need it.
    ///
    /// Free of any Space Engineers type beyond VRage.Math, so the fit and the cull are testable.
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
        /// Whether a box centred <paramref name="distance"/> metres from the eye is drawn, counting
        /// the decision. Call once per candidate, after the view cull.
        ///
        /// The radius decides; the box count is a hard stop behind it, holding the frame's cost
        /// while the radius is still being fitted. Counting what fell inside the radius either way
        /// is what the fit reads, so a frame the hard stop bound still measures the volume rather
        /// than the stop.
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
        /// Whether a box of the given radius, centred at <paramref name="delta"/> from the eye, is
        /// inside the camera's view cone.
        ///
        /// The cone is the one that contains the frustum, so this only rejects boxes that are
        /// certainly off screen — a box just outside a corner of the screen is kept. Exact
        /// sphere-against-cone: the box's bounding sphere is outside when its distance from the cone
        /// surface, measured in the plane through the axis and the box, exceeds its own radius.
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
