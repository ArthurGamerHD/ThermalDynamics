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
    /// Reads the world into an <see cref="EnvironmentSample"/>: ambient conditions, the sun, the
    /// wind, and whether anything is in the way of the light.
    ///
    /// The sun direction is one value for the whole session, and occlusion changes slowly, so
    /// both are cached rather than recomputed per grid per step. The raycast behind occlusion is
    /// the single most expensive thing a grid does.
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

        /// <summary>True on the steps that actually re-tested, so neighbours are gathered with it.</summary>
        private bool occlusionTested;
        private int stepsSinceOcclusionTest = int.MaxValue;

        /// <summary>The most recent sample, kept for the HUD and the telemetry report.</summary>
        public EnvironmentSample LastSample;

        /// <summary>Per-grid buffer for registered heat sources, grown on demand and reused.</summary>
        private HeatSourceState[] heatSourceBuffer;

        /// <summary>The environment the solver actually used, derived from the sample.</summary>
        public EnvironmentState LastState
        {
            get { return Simulation.Solver.Environment; }
        }

        /// <summary>
        /// Drops the session-wide environment caches. Planet climate is keyed by entity id and
        /// the sun by frame number, and neither means anything in the next world.
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
            sample.SolarOcclusion = sample.IsUnderground ? 1f : SolarOcclusion(ref position, ref sample);
            sample.IsSolarOccluded = sample.SolarOcclusion >= 1f;

            if (occlusionTested) RefreshShadowOccluders(ref position, ref sample);

            // Heat sources other than the sun. The buffer belongs to this grid and is reused, so
            // a session with no registered sources allocates nothing and costs one count test.
            sample.HeatSourceCount = ThermalHeatSources.Sample(position, ref worldToLocal, ref heatSourceBuffer);
            sample.HeatSources = heatSourceBuffer;

            LastSample = sample;
            profilePosition = position;
            profilePlanet = planet;

            if (Telemetry.Enabled && Stats != null) Stats.SampleEnvironment(this);

            return sample;
        }

        /// <summary>
        /// The sun moves for the whole world at once, so every grid in a frame gets the same
        /// answer and only the first one pays for it.
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
                currentPlanetId = -1;
                return;
            }

            sample.HasPlanet = true;
            sample.AirDensity = planet.Entity.GetAirDensity(position);
            sample.IsUnderground = planet.Entity.IsUnderGround(position);

            Vector3D up = position - planet.Position;
            sample.UpDirection = up.LengthSquared() > 0 ? Vector3.Normalize(up) : Vector3.Up;

            // Where on the globe: the poles get their sunlight at a glancing angle whatever the
            // hour, and without this every latitude was the same climate.
            Vector3 axis = planet.Entity.PositionComp.WorldMatrixRef.Up;
            sample.LatitudeSine = Vector3.Dot(sample.UpDirection, Vector3.Normalize(axis));

            GroundTemperature.Ground ground = GroundUnder(planet.Entity, ref position);
            sample.GroundOffset = ground.Offset;
            sample.GroundSwing = ground.Swing;

            // Where the air is now, so it can chase the sun rather than track it.
            sample.PreviousAmbient = LastState.AmbientTemperature;
            sample.SecondsSincePrevious = TickSeconds;

            long planetId = planet.Entity.EntityId;
            if (planetId != currentPlanetId)
            {
                currentPlanetId = planetId;
                Simulation.Planet = PropertiesOf(planet);

                if (Telemetry.Enabled && Stats != null) Stats.NotePlanet(planet.Entity.StorageName);
            }
        }

        /// <summary>
        /// Records what the world is doing at this grid, for balancing a planet's climate against.
        ///
        /// Everything here is either free or already known, except the two lookups at the bottom —
        /// the voxel material under the grid and the game's own comfort figure — which is why this
        /// runs on its own slow cadence rather than per step. It is a diagnostic, and it only runs
        /// with telemetry on.
        /// </summary>
        /// <summary>
        /// Where the last sample was taken, kept so the profile can be written after the step that
        /// turns it into a state — the sample and the state it produced belong in the same row, and
        /// reading the state at sampling time gives the previous step's answer, or on the first
        /// step, no answer at all.
        /// </summary>
        private Vector3D profilePosition;
        private PlanetManager.Planet profilePlanet;

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
            row.SolarEnergy = state.SolarEnergy;
            row.SolarOcclusion = state.SolarOcclusion;
            row.WindSpeed = state.WindSpeed;
            row.GridMeanKelvin = MeanTemperature();
            row.GridPeakKelvin = HottestNode != null ? HottestNode.Temperature : 0f;

            // The sun's height above the horizon says more than a dot product does: the whole
            // question of a day-night curve is what the temperature is at ten degrees up.
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

                // Latitude against the planet's own axis, so a reading can be placed on the globe:
                // a pole and an equator are different climates and this is what tells them apart.
                Vector3D axis = entity.PositionComp.WorldMatrixRef.Up;
                double axisDot = MathHelper.Clamp(Vector3D.Dot(Vector3D.Normalize(position - centre), axis), -1d, 1d);
                row.LatitudeDegrees = (float)(Math.Asin(axisDot) * 180d / Math.PI);

                row.WeatherIntensity = MyVisualScriptLogicProvider.GetWeatherIntensity(position);
                row.WindCeiling = entity.GetWindSpeed(position);

                // Where the wind is going, as a bearing, so the map can be read off the dump:
                // 0 is due north over the planet's own pole, 90 due east.
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
        /// The voxel material at the surface under the grid — snow, sand, grass, ice.
        ///
        /// Sampled a little below the surface point, because the surface point itself sits on the
        /// boundary and a lookup there answers about the air as often as about the ground.
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

        /// <summary>Steps between environment profile rows. About ten seconds of play.</summary>
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
        /// What the ground under this grid is worth, K, scaled by how much the world lets it count.
        ///
        /// The material lookup is a voxel read, so it is cached and only refreshed when the grid
        /// has moved far enough to be standing on something else. A parked base pays for it once.
        /// </summary>
        private GroundTemperature.Ground GroundUnder(MyPlanet planet, ref Vector3D position)
        {
            float influence = Settings.Instance.ClimateGroundInfluence;
            if (influence <= 0f) return GroundTemperature.Neutral;

            if (Vector3D.DistanceSquared(position, groundSampledAt) > GroundResampleDistance * GroundResampleDistance)
            {
                groundSampledAt = position;

                Vector3D surface = planet.GetClosestSurfacePointGlobal(ref position);
                ground = GroundTemperature.For(MaterialUnder(planet, ref surface));
            }

            // Influence dials the whole opinion down toward the planet's own, offset and swing
            // together: half influence is half the shift and half the extra swing.
            return new GroundTemperature.Ground(
                ground.Offset * influence,
                1f + ((ground.Swing - 1f) * influence));
        }

        /// <summary>Metres a grid may move before the ground under it is looked at again.</summary>
        private const double GroundResampleDistance = 40d;

        private Vector3D groundSampledAt = Vector3D.PositiveInfinity;
        private GroundTemperature.Ground ground = GroundTemperature.Neutral;

        private static PlanetThermalProperties PropertiesOf(PlanetManager.Planet planet)
        {
            PlanetThermalProperties properties;
            if (PlanetProperties.TryGetValue(planet.Entity.EntityId, out properties)) return properties;

            properties = ThermalBlockCatalog.ToPlanetProperties(planet.Definition());
            PlanetProperties[planet.Entity.EntityId] = properties;
            return properties;
        }

        private void SampleWind(
            ref EnvironmentSample sample, ref Vector3D position, ref MatrixD worldToLocal, PlanetManager.Planet planet)
        {
            if (!sample.HasPlanet || planet == null || sample.AirDensity <= 0f)
            {
                sample.RelativeWindSpeed = 0f;
                sample.RelativeWindDirectionLocal = Vector3.Zero;
                return;
            }

            // The game's figure is a ceiling, not a wind: the planet definition's maximum scaled
            // by air density, identical at the pole and the equator. Taken literally it puts a
            // parked ship in a permanent 80 m/s gale — tripping friction heating and doubling
            // convection — so it sets the scale and the field decides the rest.
            float ceiling = planet.Entity.GetWindSpeed(position);
            float weather = MyVisualScriptLogicProvider.GetWeatherIntensity(position);

            Vector3 up = sample.UpDirection;
            Vector3 axis = planet.Entity.PositionComp.WorldMatrixRef.Up;

            Vector3 direction = WindField.Direction(up, axis);
            float speed = WindField.Speed(ceiling, weather, WindField.Variation(position));

            sample.WindSpeed = direction.LengthSquared() > 0f ? speed : 0f;
            sample.WindDirection = direction;

            Vector3 relative = (direction * speed) - sample.GridVelocity;
            sample.RelativeWindSpeed = relative.Length();
            sample.RelativeWindDirectionLocal = sample.RelativeWindSpeed > 0f
                ? Vector3.Normalize(Vector3D.TransformNormal(relative / sample.RelativeWindSpeed, worldToLocal))
                : Vector3.Zero;
        }

        /// <summary>
        /// How much of the grid the sun cannot reach, 0..1. Re-tested every
        /// <see cref="Settings.SolarOcclusionInterval"/> steps and cached in between: the geometry
        /// it walks changes over seconds, not over a sixtieth of one.
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
        /// Casts from a few points spread through the grid and reports the share of them that
        /// cannot see the sun.
        ///
        /// Each sample is a full query: the candidates along one ray are not the candidates along
        /// another, and reusing one list would miss the asteroid that covers the bow and not the
        /// stern — which is the whole reason for sampling more than once. So the cost is linear in
        /// the sample count, which is why it is a setting and why it defaults to one.
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
        /// Each kind of occluder is its own switch, because each costs a different amount. A planet
        /// is an angle against a radius and costs nothing worth measuring. A voxel is a physics
        /// raycast. Another grid is a ray against its blocks, which is the dearest of the three and
        /// the one a fleet multiplies.
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
                        Vector3D toGrid = position - planet.PositionComp.WorldMatrixRef.Translation;
                        double distance = toGrid.Length();
                        if (distance <= 0) continue;

                        double dot = Vector3D.Dot(toGrid / distance, sample.SunDirection);
                        double horizon = Tools.GetLargestOcclusionDotProduct(
                            Tools.GetVisualSize(distance, planet.AverageRadius));

                        occluded = dot < horizon;
                        if (occluded) continue;
                    }

                    // The ball says the sun is up. The ground may still disagree.
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
                    // Basic is this ray. Full answers per face instead, and counting it here as
                    // well would shade an entire ship for a shadow across one corner of it.
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
        /// Finds the grids near enough to cast a shadow on this one and hands them to the solver in
        /// its own cell space.
        ///
        /// Everything about the geometry is folded into one matrix per occluder: the shaded grid's
        /// cells to metres, its metres to the world, the world to the occluder's metres, and its
        /// metres to its cells. After that the shadow walk never has to know that two lattices are
        /// involved at all.
        ///
        /// Run on the occlusion interval, not per step, and only when the pose has actually
        /// changed — two ships docked together never move relative to each other, and rebuilding a
        /// pass for them every interval would be the whole cost of the feature for nothing.
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
        /// Whether the occluder set is different enough to be worth a new pass: a grid gained or
        /// lost, or one that has shifted by more than a cell or turned appreciably. Under that, the
        /// shadow it casts moves by less than the resolution the shadow has.
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
        /// Whether the planet's own terrain stands between a point and the sun.
        ///
        /// Only asked of grids near a surface. A ship in orbit has nothing but curvature between it
        /// and the horizon, which the ball test already answered, and walking the ground for it
        /// would spend lookups to be told what is already known.
        /// </summary>
        private bool TerrainOccluded(MyPlanet planet, ref Vector3D position, ref EnvironmentSample sample)
        {
            Settings settings = Settings.Instance;
            if (!settings.SolarOcclusionTerrain || settings.SolarTerrainRange <= 0f) return false;

            Vector3D centre = planet.PositionComp.WorldMatrixRef.Translation;
            double altitude = (position - centre).Length() - planet.AverageRadius;

            // Above the tallest mountain by a good margin, nothing local can reach the ray.
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

        /// <summary>Metres above mean radius past which terrain cannot be in the way.</summary>
        private const double MaxTerrainAltitude = 15000d;

        /// <summary>Ground-height lookups per walk. Geometrically spaced, so this reaches far.</summary>
        private const int TerrainSamples = 10;

        /// <summary>
        /// Distance from the planet's centre to the ground under a point.
        ///
        /// Held as a field, with the planet beside it, so the walk gets a delegate that is
        /// allocated once for the life of the grid rather than one per test.
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
