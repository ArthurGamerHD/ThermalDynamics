using VRageMath;

namespace Thermodynamics.Core
{
    public static class LiftForce
    {
/// <summary>Vector operation.</summary>
        public static Vector3 Vector(Vector3 pressureWatts, Vector3 relativeWind,
            ThermalSettings settings)
        {
            if (settings == null || !settings.EnableLift) return Vector3.Zero;
            if (settings.LiftCoefficient <= 0f) return Vector3.Zero;

            float speed = relativeWind.Length();
            if (speed <= 0f) return Vector3.Zero;

            Vector3 flow = relativeWind / speed;

            Vector3 transverse = pressureWatts - (Vector3.Dot(pressureWatts, flow) * flow);

            float watts = transverse.Length();
            if (watts <= 0f) return Vector3.Zero;

            float newtons = DragForce.Newtons(watts, speed, settings) * settings.LiftCoefficient;
            if (newtons <= 0f) return Vector3.Zero;

            return transverse / watts * newtons;
        }
    }
}
