using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// A whole planet's wind, modelled at a desk — every latitude and a full day in a second, because
    /// the field is a pure function of position, weather and the hour. **It runs the shipped code, not
    /// a copy**: every sample goes through <see cref="WindSolver.Solve"/>, and only the planet and its
    /// ground are modelled here. The columns match the game's environment CSV one for one.
    /// See environment.md, Measuring it.
    /// </summary>
    public static class WindLab
    {
        /// <summary>
        /// A planet, built by the engine's own arithmetic read out of <c>Sandbox.Game.dll</c>:
        ///
        /// <code>
        /// maxHillHeight      = HillParams.Max * radius
        /// minHillHeight      = HillParams.Min * radius
        /// OuterRadius        = radius + maxHillHeight
        /// InnerRadius        = radius + minHillHeight
        /// AtmosphereAltitude = maxHillHeight * Atmosphere.LimitAltitude
        /// </code>
        ///
        /// <para>**Everything about a Space Engineers planet is a fraction of its radius**, and that
        /// radius is about a hundredth of a real one, which is why every borrowed constant lands
        /// oddly. See environment.md, World size.</para>
        /// </summary>
        public class Planet
        {
            public string Name = "EarthLike";

            /// <summary>Mean radius, m — what the engine measures altitude against.</summary>
            public double AverageRadius = 60000d;

            /// <summary>Deepest trench as a fraction of the radius. Negative.</summary>
            public double HillMin = -0.01d;

            /// <summary>Highest peak as a fraction of the radius.</summary>
            public double HillMax = 0.12d;

            /// <summary>Atmosphere height as a multiple of the highest peak.</summary>
            public double LimitAltitude = 2.0d;

            /// <summary>Sea-level air density, the definition's own figure.</summary>
            public float Density = 1f;

            /// <summary>The definition's maximum wind speed, m/s.</summary>
            public float MaxWindSpeed = 80f;

            public bool HasAtmosphere = true;

            /// <summary>The planet's north.</summary>
            public Vector3 Axis = new Vector3(0f, 1f, 0f);

            /// <summary>Seconds in a full sun rotation. The game's default is two hours.</summary>
            public double DayLength = 7200d;

            private Terrain ground;

            /// <summary>
            /// The ground. Defaults to terrain scaled to this planet's own hill range, so a moon
            /// gets a moon's relief without anyone setting a number.
            /// </summary>
            public Terrain Ground
            {
                get { return ground ?? (ground = new Terrain(this)); }
                set { ground = value; }
            }

            public double MaxHillHeight { get { return HillMax * AverageRadius; } }
            public double MinHillHeight { get { return HillMin * AverageRadius; } }
            public double OuterRadius { get { return AverageRadius + MaxHillHeight; } }
            public double InnerRadius { get { return AverageRadius + MinHillHeight; } }

            /// <summary>
            /// <c>AtmosphereAltitude = MaxHillHeight × LimitAltitude</c>. Note what that means: the
            /// atmosphere is measured from the *mean* radius and so are the mountains, so a
            /// <c>LimitAltitude</c> under one puts the peaks outside the air.
            /// </summary>
            public double AtmosphereAltitude { get { return MaxHillHeight * LimitAltitude; } }

            /// <summary>True when this world's highest ground stands above its own air. Triton does.</summary>
            public bool PeaksAboveAir { get { return HasAtmosphere && LimitAltitude < 1d; } }

            /// <summary>
            /// <c>MyPlanet.GetAirDensity</c>, reproduced from the decompiled engine:
            /// <c>clamp(1 - (r - AverageRadius)/AtmosphereAltitude, 0, 1) * Density</c>.
            ///
            /// Note what it does not do: it measures against the mean radius, not the ground, so a
            /// valley floor below the mean radius returns *more* air — and therefore more wind —
            /// than the ridge above it. That is the engine's behaviour, faithfully reproduced,
            /// because a model that quietly corrected it would stop predicting the game.
            /// </summary>
            public float AirDensity(double radius)
            {
                if (!HasAtmosphere || AtmosphereAltitude <= 0d) return 0f;

                double share = 1d - ((radius - AverageRadius) / AtmosphereAltitude);
                if (share < 0d) share = 0d;
                if (share > 1d) share = 1d;
                return (float)share * Density;
            }

            /// <summary><c>MyPlanet.GetWindSpeed</c>: the maximum, scaled by that density.</summary>
            public float WindCeiling(double radius)
            {
                return MaxWindSpeed * AirDensity(radius);
            }

            /// <summary>The unit vector at a latitude and longitude, in degrees.</summary>
            public Vector3D UpAt(double latitude, double longitude)
            {
                double lat = latitude * Math.PI / 180d;
                double lon = longitude * Math.PI / 180d;

                // The axis is +Y, so latitude runs into Y and longitude round the XZ plane.
                return Vector3D.Normalize(new Vector3D(
                    Math.Cos(lat) * Math.Cos(lon),
                    Math.Sin(lat),
                    Math.Cos(lat) * Math.Sin(lon)));
            }

            /// <summary>Distance from the centre to the ground at a point on the sphere.</summary>
            public double GroundRadius(Vector3D up)
            {
                return AverageRadius + Ground.Height(up);
            }

            /// <summary>
            /// How far someone standing this high can see before the world curves away, m.
            ///
            /// Worth having because the answer is startling: on a 60 km world the horizon from head
            /// height is about 490 m, against 5 km on Earth. Anything this model draws or reasons
            /// about at kilometre range is mostly over the edge of the world.
            /// </summary>
            public double HorizonFrom(double height)
            {
                if (height <= 0d) return 0d;
                return Math.Sqrt((2d * AverageRadius * height) + (height * height));
            }

            /// <summary>Metres of ground per degree of latitude. A circulation band is 30 of these.</summary>
            public double MetresPerDegree { get { return AverageRadius * Math.PI / 180d; } }

            /// <summary>
            /// Sine of the sun's elevation at a latitude and longitude, at an hour of the day.
            ///
            /// No axial tilt and no seasons — the game has neither. The sun goes round the equator,
            /// so the day is the same length everywhere and the poles are permanently at grazing
            /// incidence, which is exactly what the climate model already assumes.
            /// </summary>
            public double SunElevationSine(double latitude, double longitude, double dayFraction)
            {
                double lat = latitude * Math.PI / 180d;
                double hourAngle = ((dayFraction * 360d) + longitude) * Math.PI / 180d;

                return Math.Cos(lat) * Math.Cos(hourAngle);
            }

            // ---- the shipped worlds ----------------------------------------------------------

            /// <summary>Every planet the game ships, by subtype.</summary>
            public static readonly string[] VanillaNames =
            {
                "EarthLike", "Alien", "Mars", "Pertam", "Triton", "Europa", "Titan", "Moon",
            };

            /// <summary>
            /// The diameter each is usually generated at, m. A world-generation choice rather than a
            /// property of the definition — the star system scenario's figures.
            /// </summary>
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

            /// <summary>
            /// One of the game's own planets, at a diameter. Every figure is from
            /// <c>PlanetGeneratorDefinitions.sbc</c>; the diameter is supplied because it is a
            /// world-generation choice rather than part of the definition.
            /// </summary>
            public static Planet Vanilla(string subtype, double diameterMetres)
            {
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
                        // The odd one: 20% of the radius in mountain against an atmosphere only 9.4%
                        // of it deep, so its peaks stand in vacuum.
                        planet.HillMin = -0.05d; planet.HillMax = 0.20d;
                        planet.LimitAltitude = 0.47d; planet.Density = 1f; planet.MaxWindSpeed = 80f;
                        break;

                    case "Europa":
                        // Density and LimitAltitude are not authored on this one; the object
                        // builder's defaults stand in. An assumption, not a reading.
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

            /// <summary>The same planet at the diameter the game usually generates it at.</summary>
            public static Planet Vanilla(string subtype)
            {
                return Vanilla(subtype, UsualDiameter(subtype));
            }
        }

        /// <summary>
        /// A heightmap standing in for voxels.
        ///
        /// <para>Sinusoids at four scales, deterministic in position, with the relief scaled to the
        /// planet's own hill range — so a moon gets a moon's terrain and an earthlike world gets
        /// seven kilometres of mountain.</para>
        ///
        /// <para>The frequencies are in <b>unit-sphere</b> coordinates rather than metres, and that
        /// is deliberate: the game's heightmap is a fixed-resolution cube map per planet whatever its
        /// size, so a small moon's landforms are a small moon's size. Terrain in Space Engineers is
        /// self-similar across planet sizes and this reproduces that — which means a 300 m terrain
        /// ring spans a fraction of one landform on a 60 km world and several on a 9.5 km moon.
        /// Those are different regimes and both need testing.</para>
        /// </summary>
        public class Terrain
        {
            /// <summary>Relative weights per octave, largest feature first.</summary>
            public double[] Weights = { 0.62d, 0.24d, 0.11d, 0.03d };

            /// <summary>
            /// Spatial frequencies on the unit sphere: about 47 km, 6 km, 1.3 km and 400 m of arc on
            /// a 60 km world, and a sixth of each of those on a 9.5 km moon.
            /// </summary>
            public double[] Frequencies = { 8d, 60d, 300d, 900d };

            /// <summary>Metres from the deepest trench to the highest peak.</summary>
            public double Relief = 7800d;

            /// <summary>Metres the mean surface sits above the planet's average radius.</summary>
            public double Offset = 3300d;

            public Terrain() { }

            /// <summary>Terrain sized to a planet's own hill range, as the game generates it.</summary>
            public Terrain(Planet planet)
            {
                Relief = planet.MaxHillHeight - planet.MinHillHeight;

                // Centred in the hill range, so the ground swings between the planet's own floor and
                // its ceiling rather than pushing through either.
                Offset = (planet.MaxHillHeight + planet.MinHillHeight) * 0.5d;
            }

            /// <summary>Height above the mean radius, m.</summary>
            public virtual double Height(Vector3D up)
            {
                double shape = 0d;

                for (int i = 0; i < Weights.Length && i < Frequencies.Length; i++)
                {
                    double k = Frequencies[i];

                    // A product of sinusoids on two axes makes ridges and basins rather than
                    // corrugations, and the third term breaks the symmetry so the pattern does not
                    // repeat visibly on the diagonal.
                    shape += Weights[i]
                        * Math.Sin((up.X * k) + (i * 1.7d))
                        * Math.Cos((up.Z * k) + (i * 0.9d))
                        * (0.75d + (0.25d * Math.Sin(up.Y * k * 0.5d)));
                }

                return Offset + (shape * Relief * 0.5d);
            }
        }

        /// <summary>A perfectly flat world, for separating terrain effects from everything else.</summary>
        public class FlatTerrain : Terrain
        {
            public FlatTerrain() { }
            public FlatTerrain(Planet planet) : base(planet) { }
            public override double Height(Vector3D up) { return 0d; }
        }


        /// <summary>What the model is being run with. Mirrors the world settings by the same names.</summary>
        public class Options
        {
            public float Roughness = 0.03f;
            public float GradientHeight = 600f;
            public float DiurnalAmplitude = 0.35f;
            public float DiurnalCrossover = 80f;
            public float TerrainInfluence = 1f;
            public float TerrainRadius = 300f;

            /// <summary>Slope wind: air up a mountain by day, draining down it at night. 0..1.</summary>
            public float SlopeStrength = 1f;

            /// <summary>Weather intensity applied everywhere, 0..1. Zero is a clear day.</summary>
            public float WeatherIntensity = 0f;

            /// <summary>The weather's own wind modifier. One is weather that does nothing to wind.</summary>
            public float WeatherWind = 1f;

            /// <summary>The climate's lag, which the daily heating curve shares.</summary>
            public float AmbientLagSeconds = 45f;

            /// <summary>
            /// The same lag as a share of this world's day, which is what the model uses wherever
            /// the day's length is known. Zero leaves the absolute figure in charge.
            /// </summary>
            public float AmbientLagShareOfDay = 0.083f;

            /// <summary>The lag to use on a world with this day, in seconds.</summary>
            public float LagSecondsFor(double dayLengthSeconds)
            {
                if (AmbientLagShareOfDay <= 0f || dayLengthSeconds <= 0d) return AmbientLagSeconds;
                return (float)(AmbientLagShareOfDay * dayLengthSeconds);
            }

            /// <summary>Latitudes sampled, degrees, from −this to +this.</summary>
            public double LatitudeLimit = 80d;
            public double LatitudeStep = 20d;
            public double LongitudeStep = 45d;

            /// <summary>Heights above ground sampled, m.</summary>
            public double[] Heights = { 2d, 10d, 100d, 400d, 1200d };

            /// <summary>Steps the day is divided into.</summary>
            public int StepsPerDay = 72;
        }

        /// <summary>One modelled sample: the same fields the game's environment CSV carries.</summary>
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

        /// <summary>
        /// Runs a full day over the whole planet and returns every sample.
        ///
        /// The heating curve is carried forward per site rather than recomputed, because it is a lag
        /// and a lag has a history: the whole point of it is that the windiest part of the afternoon
        /// is not the sun's high point. Sites are stepped through the day together for that reason.
        /// </summary>
        public static List<Row> Run(Planet planet, Options options)
        {
            if (planet == null) planet = new Planet();
            if (options == null) options = new Options();

            List<Row> rows = new List<Row>();

            // Every site on the globe, with its terrain read once — the ground does not change over
            // the day, and reading it per step would be the only expensive thing here.
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

        /// <summary>One place on the planet, carrying its own heating history.</summary>
        private class Site
        {
            public readonly double Latitude;
            public readonly double Longitude;
            public readonly Vector3D Up;
            public readonly double GroundRadius;
            public readonly float[] Ring;

            private float heating = -1f;

            public Site(Planet planet, Options options, double latitude, double longitude)
            {
                Latitude = latitude;
                Longitude = longitude;
                Up = planet.UpAt(latitude, longitude);
                GroundRadius = planet.GroundRadius(Up);
                Ring = ReadRing(planet, options, Up, GroundRadius);
            }

            public void Advance(
                Planet planet, Options options, double dayFraction, float step, double seconds,
                List<Row> rows)
            {
                double sunSine = planet.SunElevationSine(Latitude, Longitude, dayFraction);

                // The lag is a share of the day, and this lab knows exactly how long its day is —
                // so it does not need the estimator the game side runs. See backlog C6.
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
                    // Capped by the air over this site's own ground, exactly as the game side does
                    // it — several shipped worlds have less atmosphere than the configured boundary
                    // layer is tall. See WindProfile.GradientHeightIn and backlog B20.
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

        /// <summary>
        /// The ring of ground heights around a site, in the layout <see cref="WindTerrain"/> expects.
        ///
        /// This is the offline twin of the game's sixteen surface lookups, and it does the same thing
        /// in the same order: step out along each bearing, put the point back on the site's own
        /// sphere so the tangent plane falling away does not read as a slope, and take the height
        /// there relative to the site's own ground.
        /// </summary>
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

        private static double Clamp(double value, double low, double high)
        {
            if (value < low) return low;
            if (value > high) return high;
            return value;
        }

        // ---- output -------------------------------------------------------------------------

        /// <summary>
        /// The columns the game's environment CSV uses for wind, so a modelled day and a measured
        /// session can be compared without translating either.
        /// </summary>
        public static string Csv(List<Row> rows)
        {
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

        private static string N(double value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// A readable summary of a modelled day: what the wind does with latitude, with height and
        /// with the hour, and how much of the answer the ground is responsible for.
        ///
        /// The three tables are the three questions a field dump has so far been unable to answer,
        /// because a session is short, a test world is at one latitude and the ground under it was
        /// nearly flat.
        /// </summary>
        public static string Report(Planet planet, Options options)
        {
            if (planet == null) planet = new Planet();
            if (options == null) options = new Options();

            List<Row> rows = Run(planet, options);

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
                "  channelling       {0,7:n1} .. {1,6:n1} deg (mean {2:n1})\n",
                channel.Min, channel.Max, channel.Mean));

            sb.Append("\nAgainst the engine's own figure\n");
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
