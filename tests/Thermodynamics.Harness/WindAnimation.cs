using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    public static class WindAnimation
    {
        public const int Times = 24;
        public static readonly double[] Latitudes = { -80, -60, -40, -20, 0, 20, 40, 60, 80 };
        public static readonly double[] Heights = { 2d, 100d, 400d };

        public const int Longitudes = 12;

/// <summary>Json operation.</summary>
        public static string Json()
        {
/// <summary>StringBuilder operation.</summary>
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"times\":").Append(Times);
            sb.Append(",\"lats\":").Append(Numbers(Latitudes));
            sb.Append(",\"heights\":").Append(Numbers(Heights));
            sb.Append(",\"lons\":[");
            for (int i = 0; i < Longitudes; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append((i * 360 / Longitudes).ToString(CultureInfo.InvariantCulture));
            }
            sb.Append("],\"planets\":[");

            for (int p = 0; p < WindLab.Planet.VanillaNames.Length; p++)
            {
                if (p > 0) sb.Append(',');
                Planet(sb, WindLab.Planet.VanillaNames[p]);
            }

            sb.Append("]}");
            return sb.ToString();
        }

/// <summary>Planet operation.</summary>
        private static void Planet(StringBuilder sb, string name)
        {
            WindLab.Planet planet = WindLab.Planet.Vanilla(name);
            WindLab.Options options = new WindLab.Options();

            sb.Append("{\"name\":\"").Append(name).Append('"');
            sb.Append(",\"radiusKm\":").Append(N(planet.AverageRadius / 1000d));
            sb.Append(",\"atmosphereM\":").Append(N(planet.AtmosphereAltitude));
            sb.Append(",\"maxHillM\":").Append(N(planet.MaxHillHeight));
            sb.Append(",\"bandKm\":").Append(N(planet.MetresPerDegree * 30d / 1000d));
            sb.Append(",\"horizonM\":").Append(N(planet.HorizonFrom(2d)));
            sb.Append(",\"hasAir\":").Append(planet.HasAtmosphere ? "true" : "false");
            sb.Append(",\"maxWind\":").Append(N(planet.MaxWindSpeed));
            sb.Append(",\"peaksAboveAir\":").Append(planet.PeaksAboveAir ? "true" : "false");

            int L = Latitudes.Length, O = Longitudes, H = Heights.Length;

            float[] speed = new float[Times * H * L * O];
            float[] bearing = new float[Times * H * L * O];
            float[] heating = new float[Times * L * O];

            float[] carried = new float[L * O];
            for (int i = 0; i < carried.Length; i++) carried[i] = -1f;

            double step = planet.DayLength / Times;

            for (int t = 0; t < Times; t++)
            {
                double dayFraction = (double)t / Times;

                for (int a = 0; a < L; a++)
                {
                    for (int o = 0; o < O; o++)
                    {
                        double latitude = Latitudes[a];
                        double longitude = o * 360d / O;

                        Vector3D up = planet.UpAt(latitude, longitude);
                        double groundRadius = planet.GroundRadius(up);

                        double sunSine = planet.SunElevationSine(latitude, longitude, dayFraction);

                        int site = (a * O) + o;
                        carried[site] = WindProfile.Heating(
                            carried[site], (float)sunSine, (float)step, options.AmbientLagSeconds);

                        heating[(t * L * O) + site] = carried[site];

                        Vector3 upf = (Vector3)up;
                        Vector3 east = Vector3.Cross(planet.Axis, upf);
                        bool hasEast = east.LengthSquared() > 1e-8f;
                        if (hasEast) east = Vector3.Normalize(east);
                        Vector3 north = hasEast
                            ? Vector3.Normalize(Vector3.Cross(upf, east)) : Vector3.Zero;

                        float[] ring = WindLab.ReadRing(planet, options, up, groundRadius);

                        for (int h = 0; h < H; h++)
                        {
                            double radius = groundRadius + Heights[h];

                            WindSolver.Inputs inputs = new WindSolver.Inputs();
                            inputs.Ceiling = planet.WindCeiling(radius);
                            inputs.Up = upf;
                            inputs.Axis = planet.Axis;
                            inputs.WeatherIntensity = options.WeatherIntensity;
                            inputs.WeatherWind = options.WeatherWind;
                            inputs.Variation = WindField.Variation(up * radius);
                            inputs.HeightAboveGround = (float)Heights[h];
                            inputs.Heating = carried[site];
                            inputs.Roughness = options.Roughness;
                            inputs.GradientHeight = options.GradientHeight;
                            inputs.DiurnalAmplitude = options.DiurnalAmplitude;
                            inputs.DiurnalCrossover = options.DiurnalCrossover;
                            inputs.TerrainInfluence = options.TerrainInfluence;
                            inputs.TerrainRadius = options.TerrainRadius;
                            inputs.Terrain = ring;

                            WindSolver.Result wind = WindSolver.Solve(ref inputs);

                            int index = (((t * H) + h) * L + a) * O + o;
                            speed[index] = wind.Speed;

                            bearing[index] = hasEast && wind.Direction.LengthSquared() > 1e-8f
                                ? (float)(Math.Atan2(
                                    Vector3.Dot(wind.Direction, east),
                                    Vector3.Dot(wind.Direction, north)) * 180d / Math.PI)
                                : 0f;
                        }
                    }
                }
            }

            sb.Append(",\"speed\":").Append(Floats(speed, 2));
            sb.Append(",\"bearing\":").Append(Floats(bearing, 0));
            sb.Append(",\"heating\":").Append(Floats(heating, 3));
            sb.Append('}');
        }

/// <summary>Numbers operation.</summary>
        private static string Numbers(double[] values)
        {
/// <summary>StringBuilder operation.</summary>
            StringBuilder sb = new StringBuilder("[");
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(N(values[i]));
            }
            return sb.Append(']').ToString();
        }

/// <summary>Floats operation.</summary>
        private static string Floats(float[] values, int decimals)
        {
/// <summary>string operation.</summary>
            string format = decimals == 0 ? "0" : "0." + new string('#', decimals);
/// <summary>StringBuilder operation.</summary>
            StringBuilder sb = new StringBuilder("[");
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(values[i].ToString(format, CultureInfo.InvariantCulture));
            }
            return sb.Append(']').ToString();
        }

/// <summary>N operation.</summary>
        private static string N(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
