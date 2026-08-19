using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.ModAPI;
using VRageMath;

namespace Thermodynamics
{
    public class Tools
    {

        public const float MWtoWatt = 1000000;
        public const float WattToMW = 1f / MWtoWatt;
        public const float kWtoWatt = 1000;
        public const float KphToMps = 1000f / 60f / 60f;
        public const float BoltzmannConstant = 0.00000005670374419f;
        public const float VacuumTemperaturePower4 = 53.1441f; // vacuum temp is 2.7 kelven. 2.7^4 is 53.1441;

        /// <summary>Converts a single-axis unit vector into a direction index.</summary>
        public static int DirectionToIndex(Vector3I vector)
        {
            if (vector.X > 0) return 0;
            if (vector.X < 0) return 1;
            if (vector.Y > 0) return 2;
            if (vector.Y < 0) return 3;
            if (vector.Z > 0) return 4;
            return 5;
        }

        /// <summary>Converts a direction index into a single-axis unit vector.</summary>
        public static Vector3 IndexToDirection(int index) 
        {
            switch (index) 
            {
                case 0:
                    return new Vector3(1, 0, 0);
                case 1:
                    return new Vector3(-1, 0, 0);
                case 2:
                    return new Vector3(0, 1, 0);
                case 3:
                    return new Vector3(0,-1, 0);
                case 4:
                    return new Vector3(0, 0, 1);
                case 5:
                    return new Vector3(0, 0, -1);
                default:
                    return Vector3.Zero;
            }
        }

        public static Vector3I IndexToDirectionI(int index) 
        {
            switch (index) 
            {
                case 0:
                    return new Vector3I(1, 0, 0);
                case 1:
                    return new Vector3I(-1, 0, 0);
                case 2:
                    return new Vector3I(0, 1, 0);
                case 3:
                    return new Vector3I(0,-1, 0);
                case 4:
                    return new Vector3I(0, 0, 1);
                case 5:
                    return new Vector3I(0, 0, -1);
                default:
                    return Vector3I.Zero;
            }
        }

        public static string IndexToDirectionName(int index) 
        {
            switch (index) 
            {
                case 0:
                    return "Forward";
                case 1:
                    return "Left";
                case 2:
                    return "Up";
                case 3:
                    return "Down";
                case 4:
                    return "Right";
                case 5:
                    return "Backward";
                default:
                    return "NotADirection";
            }
        }

        /// <summary>Maps a temperature onto the mod's heat colour ramp.</summary>
        /// <param name="temp">Current temperature.</param>
        /// <param name="max">Upper bound of the ramp, rendered white.</param>
        /// <param name="low">Rendered blue; anything below it is black.</param>
        /// <param name="high">Rendered red.</param>
        /// <returns>An HSV colour.</returns>
        public static Vector3 GetTemperatureColor(float temp, float max = 1000, float low = 267f, float high = 500f)
        {
            // Clamp the temperature to the range 0..max.
            float t = Math.Max(0, Math.Min(max, temp));

            float h = 240f / 360f;
            float s = 1;
            float v = 0.5f;

            if (t < low)
            {
                v = (1.5f * (t / low)) - 1;
            }
            else if (t < high)
            {
                h = (240f - ((t - low) / (high - low) * 240f)) / 360f;
            }
            else
            {
                h = 0;
                s = 1 - (2 * ((t - high) / (max - high)));
            }

            return new Vector3(h, s, v);
        }

        /// <summary>
        /// Shared surface area of two boxes, in cell faces. Callers passing an
        /// <c>IMySlimBlock</c>'s bounds must add one to the maximum, which the game reports as
        /// inclusive.
        /// </summary>
        public static int FindTouchingSurfaceArea(Vector3I minA, Vector3I maxA, Vector3I minB, Vector3I maxB)
        {
            // Touching on the X face.
            if (minA.X == maxB.X || maxA.X == minB.X)
            {
                int overlapY = Math.Min(maxA.Y, maxB.Y) - Math.Max(minA.Y, minB.Y);
                int overlapZ = Math.Min(maxA.Z, maxB.Z) - Math.Max(minA.Z, minB.Z);
                if (overlapY > 0 && overlapZ > 0)
                {
                    return overlapY * overlapZ;
                }
            }

            // Touching on the Y face.
            if (minA.Y == maxB.Y || maxA.Y == minB.Y)
            {
                int overlapX = Math.Min(maxA.X, maxB.X) - Math.Max(minA.X, minB.X);
                int overlapZ = Math.Min(maxA.Z, maxB.Z) - Math.Max(minA.Z, minB.Z);
                if (overlapX > 0 && overlapZ > 0)
                {
                    return overlapX * overlapZ;
                }
            }

            // Touching on the Z face.
            if (minA.Z == maxB.Z || maxA.Z == minB.Z)
            {
                int overlapX = Math.Min(maxA.X, maxB.X) - Math.Max(minA.X, minB.X);
                int overlapY = Math.Min(maxA.Y, maxB.Y) - Math.Max(minA.Y, minB.Y);
                if (overlapX > 0 && overlapY > 0)
                {
                    return overlapX * overlapY;
                }
            }

            return 0;
        }

        public static bool IsSolarOccluded(Vector3D observer, Vector3 solarDirection, MyPlanet planet)
        {
            Vector3D local = observer - planet.PositionComp.WorldMatrixRef.Translation;
            double distance = local.Length();
            Vector3D localNorm = local / distance;

            double dot = Vector3.Dot(localNorm, solarDirection);
            return dot < GetLargestOcclusionDotProduct(GetVisualSize(distance, planet.AverageRadius));
        }

        /// <summary>Apparent angular size of a target, 0..1.</summary>
        /// <param name="distance">Distance between the observer and the target.</param>
        /// <param name="radius">Radius of the target.</param>
        public static double GetVisualSize(double distance, double radius)
        {
            return 2 * Math.Atan(radius / (2 * distance));
        }

        /// <summary>
        /// The dot product between the direction to a body and the direction to the sun below which
        /// the body occludes the sun. A fitted curve over the body's apparent size, returning a value
        /// between 0 and -1.
        /// </summary>
        public static double GetLargestOcclusionDotProduct(double visualSize)
        {
            return -1 + (0.85 * visualSize * visualSize * visualSize);
        }

        public static float KelvinToCelsius(float kelvin) 
        {
            return kelvin - 273.15f;
            
        }
        public static string KelvinToCelsiusString(float kelvin)
        {
            return $"{KelvinToCelsius(kelvin).ToString("n2")}°C";
        }

        public static float KelvinToFahrenheit(float kelvin)
        {
            return ((kelvin - 273.15f) * 9f / 5f) + 32f;
        }

        public static string KelvinToFahrenheitString(float kelvin)
        {
            return $"{KelvinToFahrenheit(kelvin).ToString("n2")}°F";
        }


    }
}
