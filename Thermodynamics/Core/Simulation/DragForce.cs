using System;
using VRageMath;

namespace Thermodynamics.Core
{
    public static class DragForce
    {
/// <summary>Newtons operation.</summary>
        public static float Newtons(float frictionWatts, float speed, ThermalSettings settings)
        {
            if (settings == null || frictionWatts <= 0f || speed <= 0f) return 0f;
            if (settings.FrictionScale <= 0f || settings.DragCoefficient <= 0f) return 0f;

            return frictionWatts * settings.DragCoefficient
                / (2f * settings.FrictionScale * speed);
        }

/// <summary>Vector operation.</summary>
        public static Vector3 Vector(float frictionWatts, Vector3 relativeWind, ThermalSettings settings)
        {
            float speed = relativeWind.Length();
/// <summary>Newtons operation.</summary>
            float newtons = Newtons(frictionWatts, speed, settings);
            if (newtons <= 0f) return Vector3.Zero;

            return relativeWind / speed * newtons;
        }
    }
}
