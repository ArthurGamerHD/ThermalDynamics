using System;
using VRageMath;

namespace Thermodynamics.Core
{
    public class DayLength
    {
        public const float MinimumSweptRadians = 0.628f;

        public const float MaximumStepSeconds = 1f;

        private Vector3 previous;
        private bool has;

        private double sweptRadians;
        private double sweptSeconds;

        public float Seconds
        {
            get
            {
                if (sweptRadians < MinimumSweptRadians || sweptSeconds <= 0d) return -1f;
                return (float)(2d * Math.PI * sweptSeconds / sweptRadians);
            }
        }

        public bool Known
        {
            get { return Seconds > 0f; }
        }


        public void Reset()
        {
            has = false;
            sweptRadians = 0d;
            sweptSeconds = 0d;
        }


        public void Observe(Vector3 sunDirection, float seconds)
        {
            if (sunDirection.LengthSquared() < 1e-8f) return;

            Vector3 now = Vector3.Normalize(sunDirection);

            if (!has)
            {
                previous = now;
                has = true;
                return;
            }

            if (seconds <= 0f || seconds > MaximumStepSeconds)
            {
                previous = now;
                return;
            }

            double swept = Math.Atan2(
                Vector3.Cross(previous, now).Length(), Vector3.Dot(previous, now));

            previous = now;

            if (swept <= 0d) return;

            sweptRadians += swept;
            sweptSeconds += seconds;
        }
    }
}
