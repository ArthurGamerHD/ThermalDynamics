using System;
using System.Collections.Generic;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Thermodynamics.Core;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Game.Entity;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// Reads the world into an <see cref="EnvironmentSample"/>: ambient conditions, the sun, the wind,
    /// and whether anything is in the way of the light. The occlusion raycast is the most expensive
    /// thing a grid does, so it runs on an interval. See environment.md.
    /// </summary>
    public partial class ThermalGrid
    {
        private static Vector3 sunDirection = Vector3.Up;
        private static int sunDirectionFrame = -1;

        /// <summary>Pooled so the raycast path allocates nothing per call.</summary>
        private static readonly List<MyLineSegmentOverlapResult<MyEntity>> OverlapResults =
            new List<MyLineSegmentOverlapResult<MyEntity>>();

        /// <summary>Planet climate by planet entity, resolved once per planet per session.</summary>
        private static readonly Dictionary<long, PlanetThermalProperties> PlanetProperties =
            new Dictionary<long, PlanetThermalProperties>();

        private long currentPlanetId = -1;
        /// <summary>Last measured share of the grid the sun cannot reach, 0..1.</summary>
        private float solarOcclusion;

        /// <summary>True on steps that re-tested occlusion, so neighbours are gathered alongside.</summary>
        private bool occlusionTested;
        private int stepsSinceOcclusionTest = int.MaxValue;

        /// <summary>The most recent sample, kept for the HUD and the telemetry report.</summary>
        public EnvironmentSample LastSample;

        /// <summary>Per-grid buffer for registered heat sources, grown on demand and reused.</summary>
        private HeatSourceState[] heatSourceBuffer;

        /// <summary>The environment the solver used, derived from the sample.</summary>
        public EnvironmentState LastState
        {
            get { return Simulation.Solver.Environment; }
        }

        /// <summary>
        /// Drops the session-wide environment caches. Planet climate is keyed by entity id and the
        /// sun by frame number, neither of which carries over to another world.
        /// </summary>
        public static void ResetEnvironmentCaches()
        {
            PlanetProperties.Clear();
            OverlapResults.Clear();
            sunDirectionFrame = -1;
        }

        /// <summary>Reads the world for this grid, once per step.</summary>
        private EnvironmentSample Sample()
        {
            EnvironmentSample sample = new EnvironmentSample();

            Vector3D position = Grid.PositionComp.WorldAABB.Center;
            MatrixD worldToLocal = MatrixD.Transpose(Grid.WorldMatrix.GetOrientation());

            sample.SunDirection = SunDirection();
            sample.SunDirectionLocal = Vector3.Normalize(
                Vector3D.TransformNormal(sample.SunDirection, worldToLocal));

            sample.GridVelocity = Grid.Physics != null ? Grid.Physics.LinearVelocity : Vector3.Zero;

            PlanetManager.Planet planet = PlanetManager.GetClosestPlanet(position);
            SamplePlanet(ref sample, ref position, planet);
            SampleWind(ref sample, ref position, ref worldToLocal, planet);
            // A buried grid takes no sunlight, so the raycast is skipped. Either source of truth
            // suffices: this model's own depth figure or the game's underground flag.
            bool buried = sample.IsUnderground || sample.Depth > 0f;
            sample.SolarOcclusion = buried ? 1f : SolarOcclusion(ref position, ref sample);
            sample.IsSolarOccluded = sample.SolarOcclusion >= 1f;

            if (occlusionTested) RefreshShadowOccluders(ref position, ref sample);

            // Heat sources other than the sun. The buffer belongs to this grid and is reused, so a
            // session with no registered sources allocates nothing and costs one count test.
            sample.HeatSourceCount = ThermalHeatSources.Sample(position, ref worldToLocal, ref heatSourceBuffer);
            sample.HeatSources = heatSourceBuffer;

            LastSample = sample;
            profilePosition = position;
            profilePlanet = planet;

            if (Telemetry.Enabled && Stats != null) Stats.SampleEnvironment(this);

            return sample;
        }

        /// <summary>
        /// Sun direction for this frame. The sun is world-wide, so it is computed by the first grid
        /// of a frame and read by the rest.
        /// </summary>
        private static Vector3 SunDirection()
        {
            int frame = MyAPIGateway.Session != null ? MyAPIGateway.Session.GameplayFrameCounter : 0;
            if (frame == sunDirectionFrame) return sunDirection;

            sunDirection = MyVisualScriptLogicProvider.GetSunDirection();
            sunDirectionFrame = frame;
            return sunDirection;
        }

        private void SamplePlanet(ref EnvironmentSample sample, ref Vector3D position, PlanetManager.Planet planet)
        {
            if (planet == null || planet.Entity == null)
            {
                sample.HasPlanet = false;
                sample.UpDirection = Vector3.Up;
                sample.Weather = WeatherResponse.Calm;
                currentPlanetId = -1;
                hasAmbientHistory = false;
                return;
            }

            sample.HasPlanet = true;
            sample.AirDensity = planet.Entity.GetAirDensity(position);
            sample.IsUnderground = planet.Entity.IsUnderGround(position);

            Vector3D up = position - planet.Position;
            sample.UpDirection = up.LengthSquared() > 0 ? Vector3.Normalize(up) : Vector3.Up;

            // Position on the globe. The poles receive sunlight at a glancing angle at every hour,
            // so without this every latitude would share one climate.
            Vector3 axis = planet.Entity.PositionComp.WorldMatrixRef.Up;
            sample.LatitudeSine = Vector3.Dot(sample.UpDirection, Vector3.Normalize(axis));

            GroundTemperature.Ground ground = GroundUnder(planet.Entity, ref position);
            sample.GroundOffset = ground.Offset;
            sample.GroundSwing = ground.Swing;

            // Altitude and depth. Both derive from one radius and the surface height cached beside
            // the ground material, so neither costs its own lookup.
            float radius = (float)up.Length();
            sample.Radius = radius;
            sample.MeanRadius = planet.Entity.AverageRadius;
            sample.Altitude = radius - sample.MeanRadius;
            sample.Depth = groundSurfaceRadius > 0f ? groundSurfaceRadius - radius : 0f;

            sample.Weather = WeatherOver(ref position);
            sample.WeatherIntensity = MyVisualScriptLogicProvider.GetWeatherIntensity(position);

            long planetId = planet.Entity.EntityId;
            bool samePlanet = planetId == currentPlanetId;

            // The current air temperature, so ambient lags the sun rather than tracking it exactly.
            // Only supplied when a previous ambient exists for this planet: a grid that has just
            // arrived, or crossed between planets, still holds the previous world's figure or the
            // vacuum it was seeded with, and lagging up from 2.7 K would freeze every block on it
            // for minutes.
            sample.PreviousAmbient = LastState.AmbientTemperature;
            sample.HasPreviousAmbient = hasAmbientHistory && samePlanet;
            sample.SecondsSincePrevious = TickSeconds;
            hasAmbientHistory = true;

            if (!samePlanet)
            {
                currentPlanetId = planetId;
                Simulation.Planet = PropertiesOf(planet);

                if (Telemetry.Enabled && Stats != null) Stats.NotePlanet(planet.Entity.StorageName);
            }
        }

        /// <summary>
        /// True when the ambient in <see cref="LastState"/> is a climate this grid reached, rather
        /// than the vacuum every state starts at.
        /// </summary>
        private bool hasAmbientHistory;

        /// <summary>
        /// The weather over this point, resolved to its thermal effect.
        ///
        /// The game identifies weather by name — <c>RainHeavy</c>, <c>SandStormLight</c> — which is
        /// what the table is keyed on, so a lookup is a string compare against at most thirteen
        /// entries. Cached because the result changes only when a front moves over.
        /// </summary>
        private WeatherResponse.Weather WeatherOver(ref Vector3D position)
        {
            float influence = Settings.Instance.ClimateWeatherInfluence;
            if (influence <= 0f)
            {
                weatherName = null;
                return WeatherResponse.Calm;
            }

            string weather = MyVisualScriptLogicProvider.GetWeather(position);
            if (weather != weatherName || influence != weatherInfluence)
            {
                weatherName = weather;
                weatherInfluence = influence;
                weatherResponse = WeatherResponse.Soften(WeatherResponse.For(weather), influence);
            }

            return weatherResponse;
        }

        /// <summary>
        /// Half the grid's extent, metres: how far its centre can drop below the surface before all
        /// of it is buried. Bounded below so a one-block grid still fades over something.
        /// </summary>
        private float BurialDepth()
        {
            BoundingBoxD box = Grid.PositionComp.WorldAABB;
            double reach = box.HalfExtents.Length();

            return reach < 2.5 ? 2.5f : (float)reach;
        }

        /// <summary>The weather last looked up, and the table's response to it.</summary>
        private string weatherName;
        private float weatherInfluence = 1f;
        private WeatherResponse.Weather weatherResponse = WeatherResponse.Calm;

        /// <summary>The weather's name, for the readouts and the climate dump.</summary>
        public string WeatherName
        {
            get { return string.IsNullOrEmpty(weatherName) ? "" : weatherName; }
        }

        /// <summary>
        /// Where the last sample was taken, kept so the profile can be written after the step that
        /// turns it into a state. The sample and the resulting state belong in the same row, and
        /// reading the state at sampling time would give the previous step's, or none at all on
        /// the first step.
        /// </summary>
        private Vector3D profilePosition;
        private PlanetManager.Planet profilePlanet;

        /// <summary>
        /// Records what the world is doing at this grid, for balancing a planet's climate.
        ///
        /// Every figure here is free or already known except the voxel material under the grid and
        /// the game's comfort figure, which is why this runs on its own slow cadence rather than
        /// per step. Diagnostic only, and only with telemetry on.
        /// </summary>
        public void ProfileEnvironment()
        {
            if (!Telemetry.Enabled || Stats == null) return;

            Vector3D position = profilePosition;
            EnvironmentSample sample = LastSample;
            PlanetManager.Planet planet = profilePlanet;

            if (stepsSinceProfile < ProfileInterval)
            {
                stepsSinceProfile++;
                return;
            }

            stepsSinceProfile = 0;


            EnvironmentRow row = new EnvironmentRow();
            EnvironmentState state = LastState;

            row.AirDensity = sample.AirDensity;
            row.AtmosphereFactor = state.AtmosphereFactor;
            row.AmbientKelvin = state.AmbientTemperature;
            row.Underground = sample.IsUnderground;
            row.Depth = sample.Depth;
            row.WeatherAmbientOffset = state.WeatherTemperatureOffset;
            // The blended figure, which is what the surface exchanges at. The raw coefficient
            // reads as 50 W/(m2 K) in a vacuum, because the density term lives on the transfer.
            row.ConvectionCoefficient = state.EffectiveConvectionCoefficient;
            row.SolarEnergy = state.SolarEnergy;
            row.SolarOcclusion = state.SolarOcclusion;
            row.WindSpeed = state.WindSpeed;
            row.GridMeanKelvin = MeanTemperature();
            row.GridPeakKelvin = HottestNode != null ? HottestNode.Temperature : 0f;

            // Recorded as an elevation angle rather than a dot product, since a day-night curve is
            // read against the sun's height above the horizon.
            Vector3 up = sample.UpDirection;
            float sunDot = Vector3.Dot(Vector3.Normalize(up), Vector3.Normalize(sample.SunDirection));
            row.SunElevationDegrees = (float)(Math.Asin(MathHelper.Clamp(sunDot, -1f, 1f)) * 180d / Math.PI);

            if (planet != null && planet.Entity != null)
            {
                MyPlanet entity = planet.Entity;

                row.Planet = entity.StorageName;

                Vector3D centre = entity.PositionComp.WorldMatrixRef.Translation;
                double radius = (position - centre).Length();

                row.AltitudeSealevel = radius - entity.AverageRadius;

                Vector3D surface = entity.GetClosestSurfacePointGlobal(ref position);
                row.AltitudeSurface = radius - (surface - centre).Length();

                // Latitude against the planet's own axis, so a reading can be placed on the globe.
                Vector3D axis = entity.PositionComp.WorldMatrixRef.Up;
                double axisDot = MathHelper.Clamp(Vector3D.Dot(Vector3D.Normalize(position - centre), axis), -1d, 1d);
                row.LatitudeDegrees = (float)(Math.Asin(axisDot) * 180d / Math.PI);

                row.WeatherIntensity = sample.WeatherIntensity;
                row.Weather = WeatherName;
                row.WindCeiling = entity.GetWindSpeed(position);

                row.WindHeightAboveGround = sample.WindHeightAboveGround;
                row.WindBurial = sample.WindBurial;
                row.WindBandShare = sample.WindBandShare;
                row.WindProfileFactor = sample.WindProfileFactor;
                row.WindHeating = sample.WindHeating;
                row.WindSpeedUp = sample.WindSpeedUp;
                row.WindShelter = sample.WindShelter;
                row.WindChannelDegrees = sample.WindChannelDegrees;
                row.GridSpeed = sample.GridVelocity.Length();

                // Wind direction as a bearing: 0 is due north over the planet's own pole, 90 east.
                Vector3 east = Vector3.Cross(axis, up);
                if (east.LengthSquared() > 1e-6f)
                {
                    east = Vector3.Normalize(east);
                    Vector3 north = Vector3.Normalize(Vector3.Cross(up, east));
                    Vector3 wind = sample.WindDirection;

                    row.WindBearingDegrees = (float)(Math.Atan2(
                        Vector3.Dot(wind, east), Vector3.Dot(wind, north)) * 180d / Math.PI);
                }
                row.GameTemperature = MyVisualScriptLogicProvider.GetTemperatureInPoint(position);
                row.SurfaceMaterial = MaterialUnder(entity, ref surface);
            }

            Stats.NoteEnvironmentProfile(row);
        }

        /// <summary>
        /// The voxel material at the surface under the grid: snow, sand, grass, ice.
        ///
        /// Sampled slightly below the surface point, since the surface point lies on the boundary
        /// and a lookup there returns air as often as ground.
        /// </summary>
        private static string MaterialUnder(MyPlanet planet, ref Vector3D surface)
        {
            Vector3D centre = planet.PositionComp.WorldMatrixRef.Translation;
            Vector3D down = Vector3D.Normalize(centre - surface);
            Vector3D probe = surface + (down * MaterialProbeDepth);

            MyVoxelMaterialDefinition material = planet.GetMaterialAt(ref probe);
            return material == null ? "" : material.Id.SubtypeName;
        }

        /// <summary>Metres below the surface the material is read at.</summary>
        private const double MaterialProbeDepth = 1.5d;

        /// <summary>Steps between environment profile rows, roughly ten seconds of play.</summary>
        private const int ProfileInterval = 60;

        private int stepsSinceProfile = ProfileInterval;

        /// <summary>Mean block temperature, for comparing a grid against its own ambient.</summary>
        private float MeanTemperature()
        {
            IList<ThermalNode> nodes = Simulation.Solver.Nodes;
            if (nodes.Count == 0) return 0f;

            float total = 0f;
            for (int i = 0; i < nodes.Count; i++) total += nodes[i].Temperature;
            return total / nodes.Count;
        }

        /// <summary>
        /// Temperature offset contributed by the ground under this grid, K, scaled by
        /// <c>ClimateGroundInfluence</c>.
        ///
        /// The material lookup is a voxel read, so it is cached and refreshed only when the grid has
        /// moved far enough to be over different ground.
        /// </summary>
        private GroundTemperature.Ground GroundUnder(MyPlanet planet, ref Vector3D position)
        {
            RefreshSurface(planet, ref position);

            float influence = Settings.Instance.ClimateGroundInfluence;
            if (influence <= 0f) return GroundTemperature.Neutral;

            // Influence scales the whole contribution towards the planet's own figure, offset and
            // swing together.
            return new GroundTemperature.Ground(
                ground.Offset * influence,
                1f + ((ground.Swing - 1f) * influence));
        }

        /// <summary>
        /// Looks up where the ground is and what it is made of, in one voxel query, and caches both:
        /// depth is then a subtraction between refreshes. Refreshed regardless of
        /// <c>ClimateGroundInfluence</c>, which scales only the material's temperature offset.
        /// </summary>
        private void RefreshSurface(MyPlanet planet, ref Vector3D position)
        {
            if (Vector3D.DistanceSquared(position, groundSampledAt) <= GroundResampleDistance * GroundResampleDistance)
                return;

            groundSampledAt = position;

            Vector3D surface = planet.GetClosestSurfacePointGlobal(ref position);
            Vector3D centre = planet.PositionComp.WorldMatrixRef.Translation;

            groundSurfaceRadius = (float)(surface - centre).Length();
            ground = GroundTemperature.For(MaterialUnder(planet, ref surface));
        }

        /// <summary>Metres a grid may move before the ground under it is resampled.</summary>
        private const double GroundResampleDistance = 40d;

        private Vector3D groundSampledAt = Vector3D.PositiveInfinity;
        private GroundTemperature.Ground ground = GroundTemperature.Neutral;

        /// <summary>Distance from the planet's centre to the ground under the grid, m. 0 until sampled.</summary>
        private float groundSurfaceRadius;

        private static PlanetThermalProperties PropertiesOf(PlanetManager.Planet planet)
        {
            PlanetThermalProperties properties;
            if (PlanetProperties.TryGetValue(planet.Entity.EntityId, out properties)) return properties;

            PlanetDefinition definition = planet.Definition();

            properties = ThermalBlockCatalog.ToPlanetProperties(definition);

            // Only a definition that answered is cached. Until the lookup is up this is the mod's
            // own earthlike defaults, which is a climate rather than a vacuum, and it is replaced
            // by the planet's own the moment there is one to read.
            if (definition != null)
            {
                PlanetProperties[planet.Entity.EntityId] = properties;

                if (Telemetry.Enabled) Telemetry.NotePlanetProperties(
                    planet.Entity.StorageName, properties, definition.Supplied.ToString());
            }

            return properties;
        }

        private void SampleWind(
            ref EnvironmentSample sample, ref Vector3D position, ref MatrixD worldToLocal, PlanetManager.Planet planet)
        {
            if (!sample.HasPlanet || planet == null || sample.AirDensity <= 0f)
            {
                sample.RelativeWindSpeed = 0f;
                sample.RelativeWindDirectionLocal = Vector3.Zero;

                // Nothing is buried in space. Without this the diagnostic column reads 0 — the
                // wholly-buried figure — for every row a grid spends in vacuum.
                sample.WindBurial = 1f;
                return;
            }

            // The game's figure is a ceiling rather than a wind speed: the planet definition's
            // maximum scaled by air density, identical at pole and equator. Used directly it would
            // place every parked grid in a permanent 80 m/s gale, tripping friction heating and
            // doubling convection, so it sets the scale and the wind field supplies the rest.
            float ceiling = planet.Entity.GetWindSpeed(position);

            // Intensity places this point on the calm-to-storm scale; the effect's own wind
            // modifier distinguishes a gale from a still fog. Both were sampled for the climate, so
            // neither costs a second lookup.
            float weather = sample.WeatherIntensity;
            float weatherWind = WeatherResponse.Soften(sample.Weather, weather).WindMultiplier;

            Vector3 up = sample.UpDirection;
            Vector3 axis = planet.Entity.PositionComp.WorldMatrixRef.Up;

            // Height above the ground rather than above sea level. The surface radius under the grid
            // was already read for the ground material and the depth, so this costs nothing. Signed:
            // a grid digging itself in is below the surface, and the wind model reads that.
            float height = groundSurfaceRadius > 0f ? sample.Radius - groundSurfaceRadius : 0f;

            // The sun's height now, lagged into how warm the ground has become, which is what drives
            // the mixing that brings wind down to the surface — and takes it away again at night.
            float sunSine = Vector3.Dot(up, Vector3.Normalize(sample.SunDirection));
            windHeating = WindProfile.Heating(
                windHeating, sunSine, TickSeconds, Simulation.Planet.AmbientLagSeconds);

            Settings settings = Settings.Instance;

            WindSolver.Inputs inputs = new WindSolver.Inputs();
            inputs.Ceiling = ceiling;
            inputs.Up = up;
            inputs.Axis = axis;
            inputs.WeatherIntensity = weather;
            inputs.WeatherWind = weatherWind;
            inputs.Variation = WindField.Variation(position);
            inputs.HeightAboveGround = height;

            // The grid's own reach above its centre, which is how far it can descend before the
            // whole of it is under the surface. The game's underground flag answers a point rather
            // than a body and reads false for a ship whose deck is still open to the sky.
            inputs.BurialDepth = BurialDepth();
            inputs.Heating = windHeating;
            inputs.Roughness = settings.WindRoughnessLength;
            // Capped by the air there is. Several shipped worlds have less atmosphere over their
            // ground than the configured boundary layer is tall, and a profile evaluated in vacuum
            // is a profile of nothing. See WindProfile.GradientHeightIn and backlog B20.
            inputs.GradientHeight = WindProfile.GradientHeightIn(
                settings.WindGradientHeight, AirAboveGround(planet, groundSurfaceRadius));
            inputs.DiurnalAmplitude = settings.WindDiurnalAmplitude;
            inputs.DiurnalCrossover = settings.WindDiurnalCrossover;
            inputs.TerrainInfluence = settings.WindTerrainInfluence;
            inputs.TerrainRadius = settings.WindTerrainRadius;
            inputs.SlopeStrength = settings.WindSlopeStrength;

            // Read only when the ground gets a say at all, since it is sixteen surface lookups.
            inputs.Terrain =
                settings.WindTerrainInfluence > 0f && settings.WindTerrainRadius > 0f
                    && height < settings.WindGradientHeight * TerrainFadesBy
                    && ReadTerrain(planet, ref position, up, axis, settings.WindTerrainRadius)
                ? windTerrain : null;

            WindSolver.Result wind = WindSolver.Solve(ref inputs);

            Vector3 direction = wind.Direction;
            float speed = wind.Speed;

            sample.WindBurial = wind.Burial;
            sample.WindHeightAboveGround = height;
            sample.WindHeating = windHeating;
            sample.WindBandShare = wind.BandShare;
            sample.WindProfileFactor = wind.Profile;
            sample.WindSpeedUp = wind.SpeedUp;
            sample.WindShelter = wind.Shelter;
            sample.WindChannelDegrees = wind.ChannelDegrees;

            // Composed in Core, so the offline harness feels exactly the airflow a session does.
            sample.ComposeRelativeWind(
                direction.LengthSquared() > 0f ? direction : Vector3.Zero,
                direction.LengthSquared() > 0f ? speed : 0f,
                worldToLocal);

            Vector3 relative = (sample.WindDirection * sample.WindSpeed) - sample.GridVelocity;
            DrawWindVector(ref position, ref relative, sample.RelativeWindSpeed);
        }

        // ---- the local shaping of the wind ---------------------------------------------------

        /// <summary>Lagged share of the day's heating, 0..1. Negative until the first sample.</summary>
        private float windHeating = -1f;

        /// <summary>The terrain ring around this grid, reused so a sample allocates nothing.</summary>
        private readonly float[] windTerrain = new float[WindTerrain.SampleCount];

        /// <summary>Where <see cref="windTerrain"/> was taken. The ring is re-read when the grid leaves it.</summary>
        private Vector3D windTerrainAt;

        private bool hasWindTerrain;

        /// <summary>
        /// Height above ground at which terrain has stopped mattering, as a share of the gradient
        /// height. Speed-up, shelter and channelling are all surface-layer effects: a ship two
        /// kilometres up is in air that has forgotten what the ground under it looks like, and
        /// steering it along a valley it is nowhere near would be worse than not modelling terrain
        /// at all.
        /// </summary>
        private const float TerrainFadesBy = 1f;

        /// <summary>
        /// Reads the ring of ground heights around this grid, or keeps the one it has.
        ///
        /// Sixteen surface lookups, which is why they are not taken every step: the ring describes a
        /// few hundred metres of landscape and a grid that has not left it is still standing in the
        /// same landscape. Re-read once the grid has moved a quarter of the sampling radius.
        /// </summary>
        private bool ReadTerrain(
            PlanetManager.Planet planet, ref Vector3D position, Vector3 up, Vector3 axis, float radius)
        {
            double moved = radius * 0.25d;

            if (hasWindTerrain && Vector3D.DistanceSquared(position, windTerrainAt) < moved * moved)
            {
                return true;
            }

            Vector3 east = Vector3.Cross(axis, up);
            if (east.LengthSquared() < 1e-6f) return false;

            east = Vector3.Normalize(east);
            Vector3 north = Vector3.Normalize(Vector3.Cross(up, east));

            MyPlanet entity = planet.Entity;
            Vector3D centre = entity.PositionComp.GetPosition();

            // The site's own ground height, which every sample is measured against. Taken here rather
            // than reused from groundSurfaceRadius because that is the surface under the *grid*, and
            // a grid at altitude still wants the ring measured against the ground beneath it.
            Vector3D under = entity.GetClosestSurfacePointGlobal(ref position);
            double siteRadius = (under - centre).Length();

            for (int ring = 0; ring < WindTerrain.Radii; ring++)
            {
                double distance = ring == 0 ? radius * 0.5d : radius;

                for (int bearing = 0; bearing < WindTerrain.Bearings; bearing++)
                {
                    Vector3 offset = WindTerrain.BearingDirection(bearing, north, east);
                    Vector3D at = position + ((Vector3D)offset * distance);

                    // Back onto the sphere before asking, or the far samples of a large ring sit
                    // below the surface simply from the tangent plane falling away from it.
                    Vector3D radial = at - centre;
                    double length = radial.Length();
                    if (length <= 0d) continue;

                    at = centre + (radial / length * siteRadius);

                    Vector3D surface = entity.GetClosestSurfacePointGlobal(ref at);
                    windTerrain[WindTerrain.Index(ring, bearing)] =
                        (float)((surface - centre).Length() - siteRadius);
                }
            }

            windTerrainAt = position;
            hasWindTerrain = true;
            return true;
        }

        /// <summary>Metres of line drawn per metre per second of relative wind.</summary>
        private const double WindVectorScale = 0.6d;

        /// <summary>
        /// The *relative* wind this grid is flying through — the field minus the grid's own velocity,
        /// which is what heats the leading face and what makes this a different drawing from the wind
        /// map. Coloured on the friction threshold. See configuration.md, Presentation.
        /// </summary>
        private static void DrawWindVector(ref Vector3D position, ref Vector3 relative, float speed)
        {
            if (!Settings.Instance.DebugWindRaycast) return;
            if (MyAPIGateway.Utilities == null || MyAPIGateway.Utilities.IsDedicated) return;
            if (speed <= 0f) return;

            Vector4 colour = (speed > Settings.Instance.FrictionAtSpeedsAbove
                ? new Color(235, 70, 55)
                : new Color(90, 220, 120)).ToVector4();

            MySimpleObjectDraw.DrawLine(
                position,
                position + ((Vector3D)relative * WindVectorScale),
                MyStringId.GetOrCompute("Square"),
                ref colour,
                0.15f);
        }

        /// <summary>
        /// Share of the grid the sun cannot reach, 0..1. Re-tested every
        /// <see cref="Settings.SolarOcclusionInterval"/> steps and cached in between, since the
        /// geometry it walks changes over seconds.
        /// </summary>
        private float SolarOcclusion(ref Vector3D position, ref EnvironmentSample sample)
        {
            if (!Settings.Instance.EnableSolarHeat) return 1f;

            if (stepsSinceOcclusionTest < Settings.Instance.SolarOcclusionInterval)
            {
                stepsSinceOcclusionTest++;
                occlusionTested = false;
                return solarOcclusion;
            }

            stepsSinceOcclusionTest = 1;
            occlusionTested = true;

            if (Telemetry.Enabled && Stats != null) Stats.SolarTime.Begin();
            try
            {
                solarOcclusion = MeasureOcclusion(ref position, ref sample);
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalGrid.SolarOcclusion", e);
            }
            finally
            {
                if (Telemetry.Enabled && Stats != null) Stats.SolarTime.End();
            }

            return solarOcclusion;
        }

        /// <summary>
        /// Casts from several points spread through the grid and returns the share that cannot see the
        /// sun. Each sample is a full query — candidates along one ray are not candidates along
        /// another — so cost is linear in the sample count.
        /// </summary>
        private float MeasureOcclusion(ref Vector3D position, ref EnvironmentSample sample)
        {
            Settings settings = Settings.Instance;

            BoundingBoxD bounds = Grid.PositionComp.WorldAABB;
            SolarOcclusionSampler.Points(bounds, settings.SolarOcclusionSamples, SamplePoints);

            int occluded = 0;

            for (int i = 0; i < SamplePoints.Count; i++)
            {
                Vector3D origin = SamplePoints[i];
                if (RaycastSun(ref origin, ref sample)) occluded++;
            }

            return SamplePoints.Count == 0 ? 0f : occluded / (float)SamplePoints.Count;
        }

        /// <summary>
        /// Whether anything stands between one point and the sun.
        ///
        /// Each kind of occluder has its own switch because each costs differently: a planet is an
        /// angle against a radius, a voxel is a physics raycast, and another grid is a ray against
        /// its blocks — the most expensive of the three, and the one a fleet multiplies.
        /// </summary>
        private bool RaycastSun(ref Vector3D position, ref EnvironmentSample sample)
        {
            Settings settings = Settings.Instance;

            LineD line = new LineD(position, position + ((Vector3D)sample.SunDirection * 15000000d));

            OverlapResults.Clear();
            MyGamePruningStructure.GetTopmostEntitiesOverlappingRay(ref line, OverlapResults);

            bool occluded = false;

            for (int i = 0; i < OverlapResults.Count && !occluded; i++)
            {
                MyEntity entity = OverlapResults[i].Element;

                MyPlanet planet = entity as MyPlanet;
                if (planet != null)
                {
                    if (settings.SolarOcclusionPlanets)
                    {
                        occluded = OcclusionMath.IsOccludedBySphere(
                            position,
                            planet.PositionComp.WorldMatrixRef.Translation,
                            planet.AverageRadius,
                            sample.SunDirection);

                        if (occluded) continue;
                    }

                    // The planet sphere test says the sun is up; terrain may still block it.
                    occluded = TerrainOccluded(planet, ref position, ref sample);
                    continue;
                }

                MyVoxelBase voxel = entity as MyVoxelBase;
                if (voxel != null)
                {
                    if (!settings.SolarOcclusionVoxels) continue;
                    if (voxel.RootVoxel is MyPlanet) continue;

                    LineD segment;
                    voxel.PositionComp.WorldAABB.Intersect(ref line, out segment);

                    IHitInfo hit;
                    MyAPIGateway.Physics.CastRay(segment.From, segment.To, out hit, 28);
                    occluded = hit != null;
                    continue;
                }

                MyCubeGrid other = entity as MyCubeGrid;
                if (other != null && other.Physics != null && other.EntityId != Grid.EntityId)
                {
                    // Basic mode is this ray. Full mode resolves shadows per face, and counting it
                    // here as well would shade a whole grid for a shadow across one corner.
                    if (settings.SolarGridShadows != (int)GridShadowMode.Basic) continue;

                    LineD segment;
                    other.PositionComp.WorldAABB.Intersect(ref line, out segment);
                    occluded = other.RayCastBlocks(segment.From, segment.To).HasValue;
                }
            }

            if (settings.DebugSolarRaycast && !MyAPIGateway.Utilities.IsDedicated)
            {
                Vector4 color = (occluded ? Color.Red : Color.White).ToVector4();
                MySimpleObjectDraw.DrawLine(line.From, line.To, MyStringId.GetOrCompute("Square"), ref color, 0.1f);
            }

            return occluded;
        }

        /// <summary>Reused so a sampled occlusion test allocates nothing.</summary>
        private static readonly List<Vector3D> SamplePoints = new List<Vector3D>();

        /// <summary>
        /// Finds the grids near enough to shadow this one, folding each into one matrix from this
        /// grid's cells to that one's, so the shadow walk never handles two lattices. Runs on the
        /// occlusion interval and only when the relative pose moved; docked grids never do.
        /// </summary>
        private void RefreshShadowOccluders(ref Vector3D position, ref EnvironmentSample sample)
        {
            Settings settings = Settings.Instance;

            List<SunShadowMap.Occluder> occluders = Simulation.Solver.SunOccluders;

            bool wanted = settings.EnableSolarHeat
                && settings.SolarSelfShadowing
                && settings.SolarGridShadows == (int)GridShadowMode.Full;

            if (!wanted)
            {
                if (occluders.Count > 0)
                {
                    occluders.Clear();
                    Simulation.Solver.MarkSunOccludersChanged();
                }
                return;
            }

            OccluderScratch.Clear();

            LineD line = new LineD(position, position + ((Vector3D)sample.SunDirection * ShadowOccluderRange));

            OverlapResults.Clear();
            MyGamePruningStructure.GetTopmostEntitiesOverlappingRay(ref line, OverlapResults);

            double shadedSize = Grid.GridSize;
            MatrixD cellsToWorld = MatrixD.CreateScale(shadedSize) * Grid.WorldMatrix;

            for (int i = 0; i < OverlapResults.Count; i++)
            {
                MyCubeGrid other = OverlapResults[i].Element as MyCubeGrid;
                if (other == null || other.Closed || other.EntityId == Grid.EntityId) continue;
                if (other.GameLogic == null) continue;

                ThermalGrid thermals = other.GameLogic.GetAs<ThermalGrid>();
                if (thermals == null || thermals.Simulation == null) continue;

                MatrixD toOccluder = cellsToWorld
                    * other.PositionComp.WorldMatrixNormalizedInv
                    * MatrixD.CreateScale(1d / other.GridSize);

                OccluderScratch.Add(new SunShadowMap.Occluder
                {
                    Model = thermals.Simulation.Grid,
                    ToOccluder = toOccluder,
                    Id = other.EntityId,
                });
            }

            if (!OccludersMoved(occluders, OccluderScratch)) return;

            occluders.Clear();
            for (int i = 0; i < OccluderScratch.Count; i++) occluders.Add(OccluderScratch[i]);

            Simulation.Solver.MarkSunOccludersChanged();
        }

        /// <summary>
        /// Whether the occluder set has changed enough to justify a new pass: a grid gained or lost,
        /// or one that has moved by more than a cell or rotated appreciably. Below that the shadow
        /// moves by less than its own resolution.
        /// </summary>
        private static bool OccludersMoved(
            List<SunShadowMap.Occluder> current, List<SunShadowMap.Occluder> fresh)
        {
            if (current.Count != fresh.Count) return true;

            for (int i = 0; i < current.Count; i++)
            {
                if (current[i].Id != fresh[i].Id) return true;

                MatrixD was = current[i].ToOccluder;
                MatrixD now = fresh[i].ToOccluder;

                if (Vector3D.DistanceSquared(was.Translation, now.Translation) > 1d) return true;
                if (Vector3D.Dot(was.Forward, now.Forward) < 0.9994d) return true;   // ~2 degrees
                if (Vector3D.Dot(was.Up, now.Up) < 0.9994d) return true;
            }

            return false;
        }

        /// <summary>Metres out to which another grid is considered as a shadow caster.</summary>
        private const double ShadowOccluderRange = 5000d;

        private static readonly List<SunShadowMap.Occluder> OccluderScratch =
            new List<SunShadowMap.Occluder>();

        /// <summary>
        /// Whether planetary terrain stands between a point and the sun.
        ///
        /// Tested only for grids near a surface: for a grid in orbit the only obstruction is the
        /// planet's curvature, which the sphere test already covers.
        /// </summary>
        private bool TerrainOccluded(MyPlanet planet, ref Vector3D position, ref EnvironmentSample sample)
        {
            Settings settings = Settings.Instance;
            if (!settings.SolarOcclusionTerrain || settings.SolarTerrainRange <= 0f) return false;

            Vector3D centre = planet.PositionComp.WorldMatrixRef.Translation;
            double altitude = (position - centre).Length() - planet.AverageRadius;

            // Well above the tallest terrain, so no local ground can intersect the ray.
            if (altitude > MaxTerrainAltitude) return false;

            terrainPlanet = planet;

            return TerrainHorizon.Occluded(
                position,
                sample.SunDirection,
                centre,
                settings.SolarTerrainRange,
                TerrainSamples,
                surfaceRadius ?? (surfaceRadius = SurfaceRadiusAt));
        }

        /// <summary>
        /// Metres of atmosphere standing over the ground under this grid, or zero on an airless
        /// world.
        ///
        /// The engine measures its atmosphere from the *mean* radius while the ground is wherever
        /// it is, so a mountain has less air above it than a valley does and on a world whose peaks
        /// stand above their own air it has none at all.
        /// </summary>
        private static float AirAboveGround(PlanetManager.Planet planet, float groundSurfaceRadius)
        {
            if (planet == null || planet.Entity == null || !planet.Entity.HasAtmosphere) return 0f;
            if (groundSurfaceRadius <= 0f) return 0f;

            double top = planet.Entity.AverageRadius + planet.Entity.AtmosphereAltitude;
            double above = top - groundSurfaceRadius;
            return above <= 0d ? 0f : (float)above;
        }

        /// <summary>Metres above mean radius past which terrain cannot be in the way.</summary>
        private const double MaxTerrainAltitude = 15000d;

        /// <summary>Ground-height lookups per walk. Geometrically spaced, so few reach far.</summary>
        private const int TerrainSamples = 10;

        /// <summary>
        /// Distance from the planet's centre to the ground under a point.
        ///
        /// Held as a field alongside the planet so the walk receives a delegate allocated once for
        /// the life of the grid rather than one per test.
        /// </summary>
        private Func<Vector3D, double> surfaceRadius;
        private MyPlanet terrainPlanet;

        private double SurfaceRadiusAt(Vector3D point)
        {
            MyPlanet planet = terrainPlanet;
            if (planet == null) return 0d;

            Vector3D surface = planet.GetClosestSurfacePointGlobal(ref point);
            return (surface - planet.PositionComp.WorldMatrixRef.Translation).Length();
        }
    }
}
