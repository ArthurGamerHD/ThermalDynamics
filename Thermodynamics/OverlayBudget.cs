using System;
using VRageMath;

namespace Thermodynamics
{
    public class OverlayBudget
    {
        public const double Unbounded = 1e9;

        public const double MinimumRadius = 6.0;

        public const double Deadband = 0.2;

        public int MaxBoxes = 12000;

        public double Radius = Unbounded;

        public int Considered;
        public int Drawn;
        public int OffScreen;

        public int WithinRadius;

        public int Beyond;

        public int Capped;

        public double Furthest;

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

        public bool IsLimiting
        {
            get { return Radius < Unbounded; }
        }

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

        public void Cull()
        {
            Considered++;
            OffScreen++;
        }

        public void EndFrame()
        {
            if (MaxBoxes <= 0) return;

            if (Beyond == 0 && Capped == 0)
            {
                if (IsLimiting && WithinRadius <= MaxBoxes * (1.0 - Deadband)) Radius = Unbounded;
                return;
            }

            if (Furthest <= 0) return;

            if (WithinRadius <= MaxBoxes && WithinRadius >= MaxBoxes * (1.0 - Deadband)) return;

            double target = MaxBoxes * (1.0 - (Deadband * 0.5));
            double ratio = WithinRadius <= 0 ? 2.0 : target / WithinRadius;

            Radius = Math.Max(MinimumRadius, Furthest * Math.Pow(ratio, 1.0 / 3.0));
        }

        public static bool InView(
            ref Vector3D delta, double boxRadius, ref Vector3D forward, double sinHalfAngle, double cosHalfAngle)
        {
            double along = Vector3D.Dot(delta, forward);

            if (along <= -boxRadius) return false;

            double lengthSquared = delta.LengthSquared();
            double acrossSquared = lengthSquared - (along * along);
            double across = acrossSquared <= 0 ? 0 : Math.Sqrt(acrossSquared);

            return (across * cosHalfAngle) - (along * sinHalfAngle) <= boxRadius;
        }

        public static double ConeHalfAngle(double verticalFovRadians, double aspect)
        {
            if (verticalFovRadians <= 0) return Math.PI * 0.5;
            if (aspect <= 0) aspect = 1.0;

            double tangent = Math.Tan(Math.Min(verticalFovRadians, Math.PI * 0.98) * 0.5);

            return Math.Atan(tangent * Math.Sqrt(1.0 + (aspect * aspect)));
        }
    }
}
