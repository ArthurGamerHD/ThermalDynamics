using System;

namespace Thermodynamics.Core
{
    /// <summary>
    /// The speed a grid of a given mass cruises at, as
    /// [RelativeTopSpeed](https://github.com/Gauge/RelativeTopSpeed) computes it: a cubic Hermite
    /// spline through three authored mass points, so a light ship holds its speed and a heavy one is
    /// held down to a slower one.
    ///
    /// <para>
    /// **Ported behaviour-first rather than rewritten** (`K10`). The arithmetic below is the
    /// original's, quirks included, because a world moving from that mod to this one should fly the
    /// same: the same three masses and three speeds give the same curve to the float. What this
    /// model adds is not a different curve — it is the air, which the original has none of.
    /// </para>
    ///
    /// <para>
    /// **The quirk worth naming**, because reading the original without it produces a curve that is
    /// wrong everywhere: it passes its three cruise speeds in the order min, mid, max into
    /// parameters named max, mid, min, and then clamps against those names. The two reversals
    /// cancel, and the result is a curve bounded by the two authored ends. Here the parameters are
    /// named for what they hold and the clamp is written by value, which is the same behaviour said
    /// once instead of twice — `CruiseCurveTests` pins it at the ends and the middle.
    /// </para>
    /// </summary>
    public static class CruiseCurve
    {
        /// <summary>
        /// The cruise speed for a mass, m/s. <paramref name="lightSpeed"/> is the speed at
        /// <paramref name="minMass"/> and below, <paramref name="heavySpeed"/> the speed at
        /// <paramref name="maxMass"/> and above, and the curve runs between them through
        /// <paramref name="midSpeed"/> at <paramref name="midMass"/>.
        /// </summary>
        public static float Speed(float mass, float minMass, float midMass, float maxMass,
            float lightSpeed, float midSpeed, float heavySpeed)
        {
            if (mass > maxMass) return heavySpeed;
            if (mass < minMass) return lightSpeed;

            bool lessThanMid = mass < midMass;

            double speed0;
            double speed1;
            double deltaX;
            double deltaY;
            double x;
            double slopeRatio = 1;

            if (lessThanMid)
            {
                speed0 = lightSpeed;
                speed1 = midSpeed;
                deltaX = midMass - minMass;
                deltaY = midSpeed - lightSpeed;
                x = (mass - minMass) / deltaX;
            }
            else
            {
                speed0 = midSpeed;
                speed1 = heavySpeed;
                deltaX = maxMass - midMass;
                deltaY = heavySpeed - midSpeed;
                x = (mass - midMass) / deltaX;
                slopeRatio = deltaX / (midMass - minMass);
            }

            double slope0 = deltaY * slopeRatio;
            double slope1 = slope0;
            double specialSlope = (heavySpeed - lightSpeed) * slopeRatio * 0.2f;

            if (lessThanMid) slope1 = specialSlope;
            else slope0 = specialSlope;

            float interp = (float)Interpolate(x, speed0, slope0, speed1, slope1);

            // **The clamp, written by value rather than by the original's parameter names.** A
            // Hermite spline through three points overshoots between them, and this keeps a cruise
            // speed inside the range its world authored. The original reads `if (interp > minCruise)
            // interp = minCruise` under a comment saying "dont flip the signs THEY ARE CORRECT" —
            // and they are, because its call site hands `MinCruise` to a parameter named `maxCruise`
            // and `MaxCruise` to one named `minCruise`. Two reversals cancelling is a thing to
            // reproduce in behaviour and not in spelling, so this bounds the reading between the
            // two ends whichever way round they are.
            float low = lightSpeed < heavySpeed ? lightSpeed : heavySpeed;
            float high = lightSpeed < heavySpeed ? heavySpeed : lightSpeed;

            if (interp > high) interp = high;
            if (interp < low) interp = low;

            return interp;
        }

        /// <summary>
        /// A cubic Hermite spline between two points and the first derivatives at them, with
        /// <paramref name="x"/> in [0,1].
        /// </summary>
        public static double Interpolate(double x, double y0, double m0, double y1, double m1)
        {
            double x2 = x * x;
            double x3 = x2 * x;

            return (2 * x3 - 3 * x2 + 1) * y0
                + (x3 - 2 * x2 + x) * m0
                + (-2 * x3 + 3 * x2) * y1
                + (x3 - x2) * m1;
        }
    }
}
