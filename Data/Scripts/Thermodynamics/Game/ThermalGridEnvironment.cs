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
        private bool solarOccluded;
        private int stepsSinceOcclusionTest = int.MaxValue;

        /// <summary>The most recent sample, kept for the HUD and the telemetry report.</summary>
        public EnvironmentSample LastSample;

        /// <summary>The environment the solver actually used, derived from the sample.</summary>
        public EnvironmentState LastState
        {
            get { return Simulation.Solver.Environment; }
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
            sample.IsSolarOccluded = sample.IsUnderground || IsSolarOccluded(ref position, ref sample);

            LastSample = sample;
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

            long planetId = planet.Entity.EntityId;
            if (planetId != currentPlanetId)
            {
                currentPlanetId = planetId;
                Simulation.Planet = PropertiesOf(planet);

                if (Telemetry.Enabled && Stats != null) Stats.NotePlanet(planet.Entity.StorageName);
            }
        }

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

            float speed = MyVisualScriptLogicProvider.GetWeatherIntensity(position);
            if (speed == 0f) speed = planet.Entity.GetWindSpeed(position);

            Vector3 gravity = planet.GravityComponent.GetWorldGravityNormalized(position);
            Vector3 direction = Vector3.Cross(gravity, planet.Entity.WorldMatrix.Forward);
            if (direction.LengthSquared() > 0f) direction = Vector3.Normalize(direction);

            sample.WindSpeed = speed;
            sample.WindDirection = direction;

            Vector3 relative = (direction * speed) - sample.GridVelocity;
            sample.RelativeWindSpeed = relative.Length();
            sample.RelativeWindDirectionLocal = sample.RelativeWindSpeed > 0f
                ? Vector3.Normalize(Vector3D.TransformNormal(relative / sample.RelativeWindSpeed, worldToLocal))
                : Vector3.Zero;
        }

        /// <summary>
        /// Whether anything stands between the grid and the sun. Re-tested every
        /// <see cref="Settings.SolarOcclusionInterval"/> steps and cached in between: the
        /// geometry it walks changes over seconds, not over a sixtieth of one.
        /// </summary>
        private bool IsSolarOccluded(ref Vector3D position, ref EnvironmentSample sample)
        {
            if (!Settings.Instance.EnableSolarHeat) return true;

            if (stepsSinceOcclusionTest < Settings.Instance.SolarOcclusionInterval)
            {
                stepsSinceOcclusionTest++;
                return solarOccluded;
            }

            stepsSinceOcclusionTest = 1;

            if (Telemetry.Enabled && Stats != null) Stats.SolarTime.Begin();
            try
            {
                solarOccluded = RaycastSun(ref position, ref sample);
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalGrid.IsSolarOccluded", e);
            }
            finally
            {
                if (Telemetry.Enabled && Stats != null) Stats.SolarTime.End();
            }

            return solarOccluded;
        }

        private bool RaycastSun(ref Vector3D position, ref EnvironmentSample sample)
        {
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
                    Vector3D toGrid = position - planet.PositionComp.WorldMatrixRef.Translation;
                    double distance = toGrid.Length();
                    if (distance <= 0) continue;

                    double dot = Vector3D.Dot(toGrid / distance, sample.SunDirection);
                    double horizon = Tools.GetLargestOcclusionDotProduct(
                        Tools.GetVisualSize(distance, planet.AverageRadius));

                    occluded = dot < horizon;
                    continue;
                }

                MyVoxelBase voxel = entity as MyVoxelBase;
                if (voxel != null)
                {
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
                    LineD segment;
                    other.PositionComp.WorldAABB.Intersect(ref line, out segment);
                    occluded = other.RayCastBlocks(segment.From, segment.To).HasValue;
                }
            }

            if (Settings.Instance.DebugSolarRaycast && !MyAPIGateway.Utilities.IsDedicated)
            {
                Vector4 color = (occluded ? Color.Red : Color.White).ToVector4();
                MySimpleObjectDraw.DrawLine(line.From, line.To, MyStringId.GetOrCompute("Square"), ref color, 0.1f);
            }

            return occluded;
        }
    }
}
