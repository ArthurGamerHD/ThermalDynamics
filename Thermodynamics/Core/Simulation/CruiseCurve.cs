using System;

namespace Thermodynamics.Core
{
    public static class CruiseCurve
    {

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

            float low = lightSpeed < heavySpeed ? lightSpeed : heavySpeed;
            float high = lightSpeed < heavySpeed ? heavySpeed : lightSpeed;

            if (interp > high) interp = high;
            if (interp < low) interp = low;

            return interp;
        }


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
