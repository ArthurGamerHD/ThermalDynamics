using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    public static class WindLab
    {
        public class Planet
        {
            public string Name = "EarthLike";

            public double AverageRadius = 60000d;

            public double HillMin = -0.01d;

            public double HillMax = 0.12d;

            public double LimitAltitude = 2.0d;

            public float Density = 1f;

            public float MaxWindSpeed = 80f;

            public bool HasAtmosphere = true;

/// <summary>Vector3 operation.</summary>
            public Vector3 Axis = new Vector3(0f, 1f, 0f);

            public double DayLength = 7200d;

            private Terrain ground;

            public Terrain Ground
            {
                get { return ground ?? (ground = new Terrain(this)); }
                set { ground = value; }
            }

            public double MaxHillHeight { get { return HillMax * AverageRadius; } }
            public double MinHillHeight { get { return HillMin * AverageRadius; } }
            public double OuterRadius { get { return AverageRadius + MaxHillHeight; } }
            public double InnerRadius { get { return AverageRadius + MinHillHeight; } }

            public double AtmosphereAltitude { get { return MaxHillHeight * LimitAltitude; } }

            public bool PeaksAboveAir { get { return HasAtmosphere && LimitAltitude < 1d; } }

/// <summary>AirDensity operation.</summary>
            public float AirDensity(double radius)
            {
                if (!HasAtmosphere || AtmosphereAltitude <= 0d) return 0f;

                double share = 1d - ((radius - AverageRadius) / AtmosphereAltitude);
                if (share < 0d) share = 0d;
                if (share > 1d) share = 1d;
                return (float)share * Density;
            }

/// <summary>WindCeiling operation.</summary>
            public float WindCeiling(double radius)
            {
                return MaxWindSpeed * AirDensity(radius);
            }

/// <summary>UpAt operation.</summary>
            public Vector3D UpAt(double latitude, double longitude)
            {
                double lat = latitude * Math.PI / 180d;
                double lon = longitude * Math.PI / 180d;

                return Vector3D.Normalize(new Vector3D(
                    Math.Cos(lat) * Math.Cos(lon),
                    Math.Sin(lat),
                    Math.Cos(lat) * Math.Sin(lon)));
            }

/// <summary>GroundRadius operation.</summary>
            public double GroundRadius(Vector3D up)
            {
                return AverageRadius + Ground.Height(up);
            }

/// <summary>HorizonFrom operation.</summary>
            public double HorizonFrom(double height)
            {
                if (height <= 0d) return 0d;
                return Math.Sqrt((2d * AverageRadius * height) + (height * height));
            }

            public double MetresPerDegree { get { return AverageRadius * Math.PI / 180d; } }

/// <summary>SunElevationSine operation.</summary>
            public double SunElevationSine(double latitude, double longitude, double dayFraction)
            {
                double lat = latitude * Math.PI / 180d;
                double hourAngle = ((dayFraction * 360d) + longitude) * Math.PI / 180d;

                return Math.Cos(lat) * Math.Cos(hourAngle);
            }


            public static readonly string[] VanillaNames =
            {
                "EarthLike", "Alien", "Mars", "Pertam", "Triton", "Europa", "Titan", "Moon",
            };

/// <summary>UsualDiameter operation.</summary>
            public static double UsualDiameter(string subtype)
            {
                switch (subtype)
                {
                    case "EarthLike": return 120000d;
                    case "Alien": return 120000d;
                    case "Mars": return 120000d;
                    case "Triton": return 80000d;
                    case "Pertam": return 60000d;
                    default: return 19000d;   // Moon, Europa, Titan
                }
            }

/// <summary>Vanilla operation.</summary>
            public static Planet Vanilla(string subtype, double diameterMetres)
            {
/// <summary>Planet operation.</summary>
                Planet planet = new Planet();
                planet.Name = subtype;
                planet.AverageRadius = diameterMetres * 0.5d;

                switch (subtype)
                {
                    case "EarthLike":
                        planet.HillMin = -0.01d; planet.HillMax = 0.12d;
                        planet.LimitAltitude = 2.0d; planet.Density = 1f; planet.MaxWindSpeed = 80f;
                        break;

                    case "Alien":
                        planet.HillMin = -0.01d; planet.HillMax = 0.12d;
                        planet.LimitAltitude = 2.0d; planet.Density = 1.2f; planet.MaxWindSpeed = 80f;
                        break;

                    case "Mars":
                        planet.HillMin = -0.01d; planet.HillMax = 0.12d;
                        planet.LimitAltitude = 2.0d; planet.Density = 1f; planet.MaxWindSpeed = 80f;
                        break;

                    case "Pertam":
                        planet.HillMin = -0.025d; planet.HillMax = 0.025d;
                        planet.LimitAltitude = 2.0d; planet.Density = 1f; planet.MaxWindSpeed = 80f;
                        break;

                    case "Triton":
                        planet.HillMin = -0.05d; planet.HillMax = 0.20d;
                        planet.LimitAltitude = 0.47d; planet.Density = 1f; planet.MaxWindSpeed = 80f;
                        break;

                    case "Europa":
                        planet.HillMin = -0.03d; planet.HillMax = 0.06d;
                        planet.LimitAltitude = 2.0d; planet.Density = 1f; planet.MaxWindSpeed = 30f;
                        break;

                    case "Titan":
                        planet.HillMin = -0.03d; planet.HillMax = 0.03d;
                        planet.LimitAltitude = 2.0d; planet.Density = 1f; planet.MaxWindSpeed = 80f;
                        break;

                    case "Moon":
                        planet.HillMin = -0.03d; planet.HillMax = 0.03d;
                        planet.HasAtmosphere = false;
                        planet.LimitAltitude = 1.0d; planet.Density = 0f; planet.MaxWindSpeed = 0f;
                        break;

                    default:
                        throw new ArgumentException("no such vanilla planet: " + subtype);
                }

                return planet;
            }

/// <summary>Vanilla operation.</summary>
            public static Planet Vanilla(string subtype)
            {
                return Vanilla(subtype, UsualDiameter(subtype));
            }
        }

        public class Terrain
        {
            public double[] Weights = { 0.62d, 0.24d, 0.11d, 0.03d };

            public double[] Frequencies = { 8d, 60d, 300d, 900d };

            public double Relief = 7800d;

            public double Offset = 3300d;

/// <summary>Terrain operation.</summary>
            public Terrain() { }

/// <summary>Terrain operation.</summary>
            public Terrain(Planet planet)
            {
                Relief = planet.MaxHillHeight - planet.MinHillHeight;

                Offset = (planet.MaxHillHeight + planet.MinHillHeight) * 0.5d;
            }

/// <summary>Height operation.</summary>
            public virtual double Height(Vector3D up)
            {
                double shape = 0d;

                for (int i = 0; i < Weights.Length && i < Frequencies.Length; i++)
                {
                    double k = Frequencies[i];

                    shape += Weights[i]
                        * Math.Sin((up.X * k) + (i * 1.7d))
                        * Math.Cos((up.Z * k) + (i * 0.9d))
                        * (0.75d + (0.25d * Math.Sin(up.Y * k * 0.5d)));
                }

                return Offset + (shape * Relief * 0.5d);
            }
        }

        public class FlatTerrain : Terrain
        {
/// <summary>FlatTerrain operation.</summary>
            public FlatTerrain() { }
            public FlatTerrain(Planet planet) : base(planet) { }
/// <summary>Height operation.</summary>
            public override double Height(Vector3D up) { return 0d; }
        }


        public class Options
        {
            public float Roughness = 0.03f;
            public float GradientHeight = 600f;
            public float DiurnalAmplitude = 0.35f;
            public float DiurnalCrossover = 80f;
            public float TerrainInfluence = 1f;
            public float TerrainRadius = 300f;

            public float SlopeStrength = 1f;

            public float WeatherIntensity = 0f;

            public float WeatherWind = 1f;

            public float AmbientLagSeconds = 45f;

            public float AmbientLagShareOfDay = 0.083f;

/// <summary>LagSecondsFor operation.</summary>
            public float LagSecondsFor(double dayLengthSeconds)
            {
                if (AmbientLagShareOfDay <= 0f || dayLengthSeconds <= 0d) return AmbientLagSeconds;
                return (float)(AmbientLagShareOfDay * dayLengthSeconds);
            }

            public double LatitudeLimit = 80d;
            public double LatitudeStep = 20d;
            public double LongitudeStep = 45d;

            public double[] Heights = { 2d, 10d, 100d, 400d, 1200d };

            public int StepsPerDay = 72;
        }

        public struct Row
        {
            public double Seconds;
            public double DayFraction;
            public double Latitude;
            public double Longitude;
            public double HeightAboveGround;
            public double GroundElevation;
            public double SunElevationDegrees;

            public float Ceiling;
            public float BandShare;
            public float Profile;
            public float Heating;
            public float SpeedUp;
            public float Shelter;
            public float ChannelDegrees;
            public float Speed;
            public float BearingDegrees;
        }

/// <summary>Run operation.</summary>
        public static List<Row> Run(Planet planet, Options options)
        {
            if (planet == null) planet = new Planet();
            if (options == null) options = new Options();

/// <summary>List operation.</summary>
            List<Row> rows = new List<Row>();

/// <summary>List operation.</summary>
            List<Site> sites = new List<Site>();

            for (double latitude = -options.LatitudeLimit;
                latitude <= options.LatitudeLimit + 1e-9d;
                latitude += options.LatitudeStep)
            {
                for (double longitude = 0d; longitude < 360d - 1e-9d; longitude += options.LongitudeStep)
                {
                    sites.Add(new Site(planet, options, latitude, longitude));
                }
            }

            double step = planet.DayLength / options.StepsPerDay;

            for (int i = 0; i <= options.StepsPerDay; i++)
            {
                double seconds = i * step;
                double dayFraction = seconds / planet.DayLength;

                for (int s = 0; s < sites.Count; s++)
                {
                    sites[s].Advance(planet, options, dayFraction, (float)step, seconds, rows);
                }
            }

            return rows;
        }

        private class Site
        {
            public readonly double Latitude;
            public readonly double Longitude;
            public readonly Vector3D Up;
            public readonly double GroundRadius;
            public readonly float[] Ring;

            private float heating = -1f;

/// <summary>Site operation.</summary>
            public Site(Planet planet, Options options, double latitude, double longitude)
            {
                Latitude = latitude;
                Longitude = longitude;
                Up = planet.UpAt(latitude, longitude);
                GroundRadius = planet.GroundRadius(Up);
/// <summary>ReadRing operation.</summary>
                Ring = ReadRing(planet, options, Up, GroundRadius);
            }

/// <summary>Advance operation.</summary>
            public void Advance(
                Planet planet, Options options, double dayFraction, float step, double seconds,
                List<Row> rows)
            {
                double sunSine = planet.SunElevationSine(Latitude, Longitude, dayFraction);

                heating = WindProfile.Heating(
                    heating, (float)sunSine, step,
                    options.LagSecondsFor(planet.DayLength));

                Vector3 up = (Vector3)Up;
                Vector3 east = Vector3.Cross(planet.Axis, up);
                bool hasEast = east.LengthSquared() > 1e-8f;
                if (hasEast) east = Vector3.Normalize(east);
                Vector3 north = hasEast
                    ? Vector3.Normalize(Vector3.Cross(up, east)) : Vector3.Zero;

                for (int h = 0; h < options.Heights.Length; h++)
                {
                    double height = options.Heights[h];
                    double radius = GroundRadius + height;
                    Vector3D position = Up * radius;

                    WindSolver.Inputs inputs = new WindSolver.Inputs();
                    inputs.Ceiling = planet.WindCeiling(radius);
                    inputs.Up = up;
                    inputs.Axis = planet.Axis;
                    inputs.WeatherIntensity = options.WeatherIntensity;
                    inputs.WeatherWind = options.WeatherWind;
                    inputs.Variation = WindField.Variation(position);
                    inputs.HeightAboveGround = (float)height;
                    inputs.Heating = heating;
                    inputs.Roughness = options.Roughness;
                    inputs.GradientHeight = WindProfile.GradientHeightIn(
                        options.GradientHeight,
                        planet.HasAtmosphere
                            ? (float)((planet.AverageRadius + planet.AtmosphereAltitude)
                                - GroundRadius)
                            : 0f);
                    inputs.DiurnalAmplitude = options.DiurnalAmplitude;
                    inputs.DiurnalCrossover = options.DiurnalCrossover;
                    inputs.TerrainInfluence = options.TerrainInfluence;
                    inputs.TerrainRadius = options.TerrainRadius;
                    inputs.SlopeStrength = options.SlopeStrength;
                    inputs.Terrain = Ring;

                    WindSolver.Result wind = WindSolver.Solve(ref inputs);

/// <summary>Row operation.</summary>
                    Row row = new Row();
                    row.Seconds = seconds;
                    row.DayFraction = dayFraction;
                    row.Latitude = Latitude;
                    row.Longitude = Longitude;
                    row.HeightAboveGround = height;
                    row.GroundElevation = GroundRadius - planet.AverageRadius;
                    row.SunElevationDegrees = Math.Asin(Clamp(sunSine, -1d, 1d)) * 180d / Math.PI;

                    row.Ceiling = inputs.Ceiling;
                    row.BandShare = wind.BandShare;
                    row.Profile = wind.Profile;
                    row.Heating = heating;
                    row.SpeedUp = wind.SpeedUp;
                    row.Shelter = wind.Shelter;
                    row.ChannelDegrees = wind.ChannelDegrees;
                    row.Speed = wind.Speed;

                    row.BearingDegrees = hasEast && wind.Direction.LengthSquared() > 1e-8f
                        ? (float)(Math.Atan2(
                            Vector3.Dot(wind.Direction, east),
                            Vector3.Dot(wind.Direction, north)) * 180d / Math.PI)
                        : 0f;

                    rows.Add(row);
                }
            }
        }

/// <summary>ReadRing operation.</summary>
        public static float[] ReadRing(Planet planet, Options options, Vector3D up, double groundRadius)
        {
            float[] ring = new float[WindTerrain.SampleCount];
            if (options.TerrainRadius <= 0f) return ring;

            Vector3 site = (Vector3)up;
            Vector3 east = Vector3.Cross(planet.Axis, site);
            if (east.LengthSquared() < 1e-8f) return ring;

            east = Vector3.Normalize(east);
            Vector3 north = Vector3.Normalize(Vector3.Cross(site, east));

            for (int radius = 0; radius < WindTerrain.Radii; radius++)
            {
                double distance = radius == 0
                    ? options.TerrainRadius * 0.5d : options.TerrainRadius;

                for (int bearing = 0; bearing < WindTerrain.Bearings; bearing++)
                {
                    Vector3 offset = WindTerrain.BearingDirection(bearing, north, east);
                    Vector3D at = Vector3D.Normalize((up * groundRadius) + ((Vector3D)offset * distance));

                    ring[WindTerrain.Index(radius, bearing)] =
                        (float)(planet.GroundRadius(at) - groundRadius);
                }
            }

            return ring;
        }

/// <summary>Clamp operation.</summary>
        private static double Clamp(double value, double low, double high)
        {
            if (value < low) return low;
            if (value > high) return high;
            return value;
        }


/// <summary>Csv operation.</summary>
        public static string Csv(List<Row> rows)
        {
/// <summary>StringBuilder operation.</summary>
            StringBuilder sb = new StringBuilder();
            sb.Append("time_s,day_fraction,latitude_deg,longitude_deg,ground_elev_m,")
              .Append("sun_elevation_deg,wind_agl_m,wind_ceiling,wind_band_share,wind_profile,")
              .Append("wind_heating,wind_speedup,wind_shelter,wind_channel_deg,wind_speed,")
              .Append("wind_bearing_deg\n");

            for (int i = 0; i < rows.Count; i++)
            {
                Row r = rows[i];
                sb.Append(N(r.Seconds)).Append(',').Append(N(r.DayFraction)).Append(',')
                  .Append(N(r.Latitude)).Append(',').Append(N(r.Longitude)).Append(',')
                  .Append(N(r.GroundElevation)).Append(',').Append(N(r.SunElevationDegrees)).Append(',')
                  .Append(N(r.HeightAboveGround)).Append(',').Append(N(r.Ceiling)).Append(',')
                  .Append(N(r.BandShare)).Append(',').Append(N(r.Profile)).Append(',')
                  .Append(N(r.Heating)).Append(',').Append(N(r.SpeedUp)).Append(',')
                  .Append(N(r.Shelter)).Append(',').Append(N(r.ChannelDegrees)).Append(',')
                  .Append(N(r.Speed)).Append(',').Append(N(r.BearingDegrees)).Append('\n');
            }

            return sb.ToString();
        }

/// <summary>N operation.</summary>
        private static string N(double value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }

/// <summary>Report operation.</summary>
        public static string Report(Planet planet, Options options)
        {
            if (planet == null) planet = new Planet();
            if (options == null) options = new Options();

/// <summary>Run operation.</summary>
            List<Row> rows = Run(planet, options);

/// <summary>StringBuilder operation.</summary>
            StringBuilder sb = new StringBuilder();
            sb.Append("Wind model, one simulated day on ").Append(planet.Name).Append('\n');
            sb.Append("  ").Append(rows.Count.ToString("n0")).Append(" samples: ")
              .Append(options.StepsPerDay + 1).Append(" times of day, ")
              .Append(options.Heights.Length).Append(" heights, latitudes ±")
              .Append(options.LatitudeLimit).Append("\n\n");

            sb.Append("By latitude, at 10 m, over the whole day\n");
            sb.Append("  lat     speed m/s          bearing      terrain\n");
            for (double lat = -options.LatitudeLimit; lat <= options.LatitudeLimit + 1e-9d;
                lat += options.LatitudeStep)
            {
                double target = lat;
/// <summary>Stat operation.</summary>
                Stat speed = new Stat(), bear = new Stat(), terr = new Stat();
                for (int i = 0; i < rows.Count; i++)
                {
                    Row r = rows[i];
                    if (Math.Abs(r.Latitude - target) > 1e-6d) continue;
                    if (Math.Abs(r.HeightAboveGround - 10d) > 1e-6d) continue;
                    speed.Add(r.Speed);
                    bear.Add(r.BearingDegrees);
                    terr.Add(r.SpeedUp * r.Shelter);
                }
                if (speed.Count == 0) continue;
                sb.Append(string.Format(CultureInfo.InvariantCulture,
                    "  {0,5:n0}   {1,5:n1} .. {2,5:n1} (mean {3,5:n1})   {4,6:n0}    x{5:n3}\n",
                    target, speed.Min, speed.Max, speed.Mean, bear.Mean, terr.Mean));
            }

            sb.Append("\nBy height, at the equatorward sites, day against night\n");
            sb.Append("  agl m    day m/s   night m/s   ratio\n");
            for (int h = 0; h < options.Heights.Length; h++)
            {
                double height = options.Heights[h];
/// <summary>Stat operation.</summary>
                Stat day = new Stat(), night = new Stat();
                for (int i = 0; i < rows.Count; i++)
                {
                    Row r = rows[i];
                    if (Math.Abs(r.HeightAboveGround - height) > 1e-6d) continue;
                    if (r.Heating > 0.6f) day.Add(r.Speed);
                    else if (r.Heating < 0.05f) night.Add(r.Speed);
                }
                if (day.Count == 0 || night.Count == 0) continue;
                sb.Append(string.Format(CultureInfo.InvariantCulture,
                    "  {0,6:n0}   {1,7:n2}   {2,9:n2}   {3,5:n2}\n",
                    height, day.Mean, night.Mean, night.Mean <= 0d ? 0d : day.Mean / night.Mean));
            }

            sb.Append("\nWhat the ground is doing\n");
/// <summary>Stat operation.</summary>
            Stat up = new Stat(), shelter = new Stat(), channel = new Stat(), elev = new Stat();
            for (int i = 0; i < rows.Count; i++)
            {
                Row r = rows[i];
                if (Math.Abs(r.HeightAboveGround - 10d) > 1e-6d) continue;
                up.Add(r.SpeedUp);
                shelter.Add(r.Shelter);
                channel.Add(r.ChannelDegrees);
                elev.Add(r.GroundElevation);
            }
            sb.Append(string.Format(CultureInfo.InvariantCulture,
                "  ground elevation  {0,7:n0} .. {1,6:n0} m\n", elev.Min, elev.Max));
            sb.Append(string.Format(CultureInfo.InvariantCulture,
                "  speed-up          {0,7:n3} .. {1,6:n3}  (mean {2:n3})\n", up.Min, up.Max, up.Mean));
            sb.Append(string.Format(CultureInfo.InvariantCulture,
                "  shelter           {0,7:n3} .. {1,6:n3}  (mean {2:n3})\n",
                shelter.Min, shelter.Max, shelter.Mean));
            sb.Append(string.Format(CultureInfo.InvariantCulture,
/// <summary>deg operation.</summary>
                "  channelling       {0,7:n1} .. {1,6:n1} deg (mean {2:n1})\n",
                channel.Min, channel.Max, channel.Mean));

            sb.Append("\nAgainst the engine's own figure\n");
/// <summary>Stat operation.</summary>
            Stat ceiling = new Stat(), speeds = new Stat();
            int over = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                ceiling.Add(rows[i].Ceiling);
                speeds.Add(rows[i].Speed);
                if (rows[i].Speed > rows[i].Ceiling) over++;
            }
            sb.Append(string.Format(CultureInfo.InvariantCulture,
                "  GetWindSpeed      {0,7:n1} .. {1,6:n1} m/s\n", ceiling.Min, ceiling.Max));
            sb.Append(string.Format(CultureInfo.InvariantCulture,
                "  modelled wind     {0,7:n1} .. {1,6:n1} m/s\n", speeds.Min, speeds.Max));
            sb.Append(string.Format(CultureInfo.InvariantCulture,
                "  samples over it   {0,7:n0}  ({1:n1}%)\n",
                over, rows.Count == 0 ? 0d : 100d * over / rows.Count));

            return sb.ToString();
        }

        private struct Stat
        {
            private double min, max, total;
            private int count;

/// <summary>Adds a .</summary>
            public void Add(double value)
            {
                if (count == 0 || value < min) min = value;
                if (count == 0 || value > max) max = value;
                total += value;
                count++;
            }

            public double Min { get { return count == 0 ? 0d : min; } }
            public double Max { get { return count == 0 ? 0d : max; } }
            public double Mean { get { return count == 0 ? 0d : total / count; } }
            public int Count { get { return count; } }
        }
    }
}
