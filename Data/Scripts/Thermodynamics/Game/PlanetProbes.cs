using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// Regular wind readings from fixed points all over a planet, taken whether or not anything is
    /// standing there.
    ///
    /// <para><b>Why this can exist at all:</b> the wind field is a pure function of position, the
    /// weather and the hour. Nothing about it depends on a grid, so there is no reason the only
    /// places it gets measured should be the handful of spots somebody happened to park in. The
    /// first field run made the case: three minutes of samples between 5° and 30° of latitude, on
    /// ground gentle enough that the terrain model never did anything, from which almost nothing
    /// about the model could be concluded.</para>
    ///
    /// <para>A lattice of probes answers all of that in one pass. Every latitude including the poles
    /// and the equator, several heights each so the diurnal crossover and the nocturnal jet are
    /// visible, sampled right through the day — and the ground under each probe is real terrain
    /// rather than whatever was under the landing pad.</para>
    ///
    /// <para>Each probe runs the same <see cref="WindSolver.Solve"/> the grids do, so this measures
    /// the model rather than a description of it, and writes the same columns
    /// <c>Thermodynamics.Harness.WindLab</c> writes offline — so a probe sweep, a grid's own
    /// telemetry and a modelled day are all directly comparable.</para>
    ///
    /// <para>Off unless telemetry is on and <see cref="Settings.TelemetryPlanetProbes"/> is set.
    /// Server side: it reads the world rather than anyone's screen.</para>
    /// </summary>
    public static class PlanetProbes
    {
        /// <summary>Heights above ground each probe reads, m.</summary>
        private static readonly float[] Heights = { 2f, 10f, 100f, 400f, 1200f };

        /// <summary>
        /// Longitudes per latitude ring. Eight is enough to see the field vary along a band without
        /// making the sweep expensive; the interesting variation is with latitude and height.
        /// </summary>
        private const int Longitudes = 8;

        /// <summary>
        /// Latitude rings, from pole to pole. Nine gives ±80° in twenty degree steps plus the
        /// equator — which the circulation bands sit between, and where the field's known fault is.
        /// </summary>
        private const int Latitudes = 9;

        public struct Row
        {
            public float Seconds;
            public string Planet;
            public float Latitude;
            public float Longitude;
            public float GroundElevation;
            public float HeightAboveGround;
            public float SunElevationDegrees;

            public float Ceiling;
            public float BandShare;
            public float Profile;
            public float Heating;
            public float SpeedUp;
            public float Shelter;
            public float ChannelDegrees;
            public float Speed;
            public float BearingDegrees;

            // The climate at the same point, from the same functions the grids use. A probe sweep is
            // the only way to see a planet's thermals across every latitude and a whole day without
            // parking a fleet, and the wind and the climate share every input, so they share a row.
            public float AmbientKelvin;
            public float AmbientTargetKelvin;
            public float AirDensity;
        }

        /// <summary>Everything collected this session. Read by the report writer.</summary>
        public static readonly List<Row> Rows = new List<Row>();

        /// <summary>One probe's lasting state: where it is, what the ground round it looks like.</summary>
        private class Probe
        {
            public float Latitude;
            public float Longitude;
            public Vector3D Up;
            public double GroundRadius;
            public float[] Terrain;

            /// <summary>Its own lagged heating, since the hour differs with longitude.</summary>
            public float Heating = -1f;

            /// <summary>Lagged ambient per height, K. Zero until the first sample.</summary>
            public float[] Ambient = new float[Heights.Length];
        }

        private static readonly List<Probe> Sites = new List<Probe>();

        private static long planetId = -1;
        private static int stepsSince;

        public static void Reset()
        {
            Rows.Clear();
            Sites.Clear();
            planetId = -1;
            stepsSince = 0;
        }

        /// <summary>
        /// Advances the sweep. Called once per solver step from the session, not per grid.
        ///
        /// The lattice is built once per planet — sixteen surface lookups per probe, and then never
        /// again, because a probe does not move and the ground under it does not change. What
        /// repeats is the arithmetic, which is free.
        /// </summary>
        public static void Step(float seconds)
        {
            int interval = Settings.Instance.TelemetryPlanetProbes;
            if (interval <= 0 || !Telemetry.Enabled)
            {
                if (Sites.Count > 0) Sites.Clear();
                return;
            }

            stepsSince++;
            if (stepsSince < interval) return;

            float elapsed = stepsSince * seconds;
            stepsSince = 0;

            PlanetManager.Planet planet = Anchor();
            if (planet == null || planet.Entity == null)
            {
                Sites.Clear();
                planetId = -1;
                return;
            }

            if (planet.Entity.EntityId != planetId || Sites.Count == 0)
            {
                Build(planet);
                planetId = planet.Entity.EntityId;
            }

            Sample(planet, elapsed);
        }

        /// <summary>
        /// The planet to probe: the one the first simulated grid is nearest. A world with no grids
        /// on a planet has nothing to compare probes against, so there is nothing to sweep.
        /// </summary>
        private static PlanetManager.Planet Anchor()
        {
            IList<ThermalGrid> grids = ThermalGrid.LiveGrids;
            if (grids == null) return null;

            for (int i = 0; i < grids.Count; i++)
            {
                ThermalGrid thermals = grids[i];
                if (thermals == null || thermals.Grid == null || thermals.Simulation == null) continue;

                PlanetManager.Planet planet =
                    PlanetManager.GetClosestPlanet(thermals.Grid.PositionComp.GetPosition());

                if (planet != null && planet.Entity != null && planet.Entity.HasAtmosphere) return planet;
            }

            return null;
        }

        private static void Build(PlanetManager.Planet planet)
        {
            Sites.Clear();

            MyPlanet entity = planet.Entity;
            Vector3D centre = entity.PositionComp.GetPosition();
            Vector3D axis = Vector3D.Normalize(entity.PositionComp.WorldMatrixRef.Up);
            Vector3D prime = Vector3D.Normalize(entity.PositionComp.WorldMatrixRef.Forward);
            Vector3D side = Vector3D.Normalize(Vector3D.Cross(axis, prime));

            float radius = Settings.Instance.WindTerrainRadius;

            for (int i = 0; i < Latitudes; i++)
            {
                // −80 to +80 inclusive, with the equator in the middle. The poles themselves are
                // skipped: the circulation has no direction there and the field says so.
                float latitude = -80f + (i * (160f / (Latitudes - 1)));
                double lat = latitude * Math.PI / 180d;

                for (int j = 0; j < Longitudes; j++)
                {
                    float longitude = j * (360f / Longitudes);
                    double lon = longitude * Math.PI / 180d;

                    Vector3D up = Vector3D.Normalize(
                        (axis * Math.Sin(lat))
                        + (prime * (Math.Cos(lat) * Math.Cos(lon)))
                        + (side * (Math.Cos(lat) * Math.Sin(lon))));

                    Vector3D at = centre + (up * entity.MaximumRadius);
                    Vector3D surface = entity.GetClosestSurfacePointGlobal(ref at);

                    Probe probe = new Probe();
                    probe.Latitude = latitude;
                    probe.Longitude = longitude;
                    probe.Up = up;
                    probe.GroundRadius = (surface - centre).Length();
                    probe.Terrain = ReadTerrain(entity, ref centre, ref axis, probe, radius);

                    Sites.Add(probe);
                }
            }
        }

        /// <summary>The ring of ground heights around a probe, as <see cref="WindTerrain"/> wants it.</summary>
        private static float[] ReadTerrain(
            MyPlanet entity, ref Vector3D centre, ref Vector3D axis, Probe probe, float radius)
        {
            float[] ring = new float[WindTerrain.SampleCount];
            if (radius <= 0f) return ring;

            Vector3 up = (Vector3)probe.Up;
            Vector3 east = Vector3.Cross((Vector3)axis, up);
            if (east.LengthSquared() < 1e-8f) return ring;

            east = Vector3.Normalize(east);
            Vector3 north = Vector3.Normalize(Vector3.Cross(up, east));

            Vector3D origin = centre + (probe.Up * probe.GroundRadius);

            for (int ringIndex = 0; ringIndex < WindTerrain.Radii; ringIndex++)
            {
                double distance = ringIndex == 0 ? radius * 0.5d : radius;

                for (int bearing = 0; bearing < WindTerrain.Bearings; bearing++)
                {
                    Vector3 offset = WindTerrain.BearingDirection(bearing, north, east);
                    Vector3D at = origin + ((Vector3D)offset * distance);

                    Vector3D radial = at - centre;
                    double length = radial.Length();
                    if (length <= 0d) continue;

                    at = centre + (radial / length * probe.GroundRadius);

                    Vector3D surface = entity.GetClosestSurfacePointGlobal(ref at);
                    ring[WindTerrain.Index(ringIndex, bearing)] =
                        (float)((surface - centre).Length() - probe.GroundRadius);
                }
            }

            return ring;
        }

        private static void Sample(PlanetManager.Planet planet, float elapsed)
        {
            MyPlanet entity = planet.Entity;
            Vector3D centre = entity.PositionComp.GetPosition();
            Vector3 axis = entity.PositionComp.WorldMatrixRef.Up;

            Vector3 sun = MyVisualScriptLogicProvider.GetSunDirection();
            if (sun.LengthSquared() > 1e-8f) sun = Vector3.Normalize(sun);

            Settings settings = Settings.Instance;
            float seconds = (float)Math.Round(Telemetry.SessionSeconds);

            // The planet's own thermal properties, which Data/Planets.xml now supplies per world
            // rather than one earthlike entry standing in for all eight.
            PlanetThermalProperties properties =
                ThermalBlockCatalog.ToPlanetProperties(planet.Definition());

            for (int s = 0; s < Sites.Count; s++)
            {
                Probe probe = Sites[s];

                Vector3 up = (Vector3)probe.Up;
                float sunSine = Vector3.Dot(up, sun);

                probe.Heating = WindProfile.Heating(
                    probe.Heating, sunSine, elapsed, Simulation(planet));

                Vector3 east = Vector3.Cross(axis, up);
                bool hasEast = east.LengthSquared() > 1e-8f;
                if (hasEast) east = Vector3.Normalize(east);
                Vector3 north = hasEast ? Vector3.Normalize(Vector3.Cross(up, east)) : Vector3.Zero;

                // The weather over the probe, which is a lookup per probe rather than per height.
                Vector3D at = centre + (probe.Up * probe.GroundRadius);
                float intensity = MyVisualScriptLogicProvider.GetWeatherIntensity(at);
                float weatherWind = 1f;

                if (intensity > 0f && settings.ClimateWeatherInfluence > 0f)
                {
                    string name = MyVisualScriptLogicProvider.GetWeather(at);
                    if (!string.IsNullOrEmpty(name))
                    {
                        weatherWind = WeatherResponse.Soften(
                            WeatherResponse.Soften(
                                WeatherResponse.For(name), settings.ClimateWeatherInfluence),
                            intensity).WindMultiplier;
                    }
                }

                for (int h = 0; h < Heights.Length; h++)
                {
                    float height = Heights[h];
                    double radius = probe.GroundRadius + height;
                    Vector3D position = centre + (probe.Up * radius);

                    WindSolver.Inputs inputs = new WindSolver.Inputs();
                    inputs.Ceiling = entity.GetWindSpeed(position);
                    inputs.Up = up;
                    inputs.Axis = axis;
                    inputs.WeatherIntensity = intensity;
                    inputs.WeatherWind = weatherWind;
                    inputs.Variation = WindField.Variation(position);
                    inputs.HeightAboveGround = height;
                    inputs.Heating = probe.Heating;
                    inputs.Roughness = settings.WindRoughnessLength;
                    inputs.GradientHeight = settings.WindGradientHeight;
                    inputs.DiurnalAmplitude = settings.WindDiurnalAmplitude;
                    inputs.DiurnalCrossover = settings.WindDiurnalCrossover;
                    inputs.TerrainInfluence = settings.WindTerrainInfluence;
                    inputs.TerrainRadius = settings.WindTerrainRadius;
                    inputs.SlopeStrength = settings.WindSlopeStrength;
                    inputs.Terrain = probe.Terrain;

                    WindSolver.Result wind = WindSolver.Solve(ref inputs);

                    Row row = new Row();
                    row.Seconds = seconds;
                    row.Planet = entity.StorageName;
                    row.Latitude = probe.Latitude;
                    row.Longitude = probe.Longitude;
                    row.GroundElevation = (float)(probe.GroundRadius - entity.AverageRadius);
                    row.HeightAboveGround = height;
                    row.SunElevationDegrees =
                        (float)(Math.Asin(MathHelper.Clamp(sunSine, -1f, 1f)) * 180d / Math.PI);

                    row.Ceiling = inputs.Ceiling;
                    row.BandShare = wind.BandShare;
                    row.Profile = wind.Profile;
                    row.Heating = probe.Heating;
                    row.SpeedUp = wind.SpeedUp;
                    row.Shelter = wind.Shelter;
                    row.ChannelDegrees = wind.ChannelDegrees;
                    row.Speed = wind.Speed;

                    row.BearingDegrees = hasEast && wind.Direction.LengthSquared() > 1e-8f
                        ? (float)(Math.Atan2(
                            Vector3.Dot(wind.Direction, east),
                            Vector3.Dot(wind.Direction, north)) * 180d / Math.PI)
                        : 0f;

                    // The climate here, through the same chain a grid's ambient goes through: the
                    // day-night target at this latitude, cooled for altitude, thinned by the air,
                    // then lagged. The lag is kept per probe and per height, since the two heights
                    // of one probe are different air.
                    float latitudeSine = Vector3.Dot(up, Vector3.Normalize(axis));
                    float target = ClimateModel.Target(properties, latitudeSine, sunSine, 0f);

                    target = ClimateModel.Lapse(
                        target, (float)(radius - entity.AverageRadius), properties.AmbientLapseRate);

                    float density = entity.GetAirDensity(position);
                    target = ClimateModel.Thin(target, density, settings.VacuumTemperature);

                    row.AmbientTargetKelvin = target;
                    row.AirDensity = density;

                    float previous = probe.Ambient[h];
                    row.AmbientKelvin = previous <= 0f
                        ? target
                        : ClimateModel.Follow(previous, target, elapsed, properties.AmbientLagSeconds);
                    probe.Ambient[h] = row.AmbientKelvin;

                    Rows.Add(row);
                }
            }
        }

        /// <summary>The climate lag the heating curve shares, from whichever grid has the planet.</summary>
        private static float Simulation(PlanetManager.Planet planet)
        {
            return Settings.Instance.PlanetAmbientLagSeconds > 0f
                ? Settings.Instance.PlanetAmbientLagSeconds : 45f;
        }

        /// <summary>The sweep as CSV, in the same column layout the offline model writes.</summary>
        public static string Csv()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("time_s,planet,latitude_deg,longitude_deg,ground_elev_m,sun_elevation_deg,")
              .Append("wind_agl_m,wind_ceiling,wind_band_share,wind_profile,wind_heating,")
              .Append("wind_speedup,wind_shelter,wind_channel_deg,wind_speed,wind_bearing_deg,")
              .Append("air_density,ambient_target_k,ambient_k,ambient_c\n");

            for (int i = 0; i < Rows.Count; i++)
            {
                Row r = Rows[i];
                sb.Append(N(r.Seconds)).Append(',').Append(r.Planet ?? "").Append(',')
                  .Append(N(r.Latitude)).Append(',').Append(N(r.Longitude)).Append(',')
                  .Append(N(r.GroundElevation)).Append(',').Append(N(r.SunElevationDegrees)).Append(',')
                  .Append(N(r.HeightAboveGround)).Append(',').Append(N(r.Ceiling)).Append(',')
                  .Append(N(r.BandShare)).Append(',').Append(N(r.Profile)).Append(',')
                  .Append(N(r.Heating)).Append(',').Append(N(r.SpeedUp)).Append(',')
                  .Append(N(r.Shelter)).Append(',').Append(N(r.ChannelDegrees)).Append(',')
                  .Append(N(r.Speed)).Append(',').Append(N(r.BearingDegrees)).Append(',')
                  .Append(N(r.AirDensity)).Append(',').Append(N(r.AmbientTargetKelvin)).Append(',')
                  .Append(N(r.AmbientKelvin)).Append(',')
                  .Append(N(r.AmbientKelvin - 273.15f)).Append('\n');
            }

            return sb.ToString();
        }

        private static string N(float value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }
    }
}
