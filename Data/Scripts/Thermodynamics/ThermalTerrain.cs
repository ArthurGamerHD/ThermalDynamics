using System;
using System.Collections.Generic;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// A coarse thermal picture of the ground under the player.
    ///
    /// Thermal vision blanks the visible-light frame completely, which takes the terrain with it —
    /// a mod cannot shade voxels. So the ground is redrawn from samples: a ring pattern of points
    /// around the camera, each one dropped onto the planet's surface, each drawn as a patch at the
    /// temperature that piece of ground would be.
    ///
    /// Rings rather than a square grid because ground detail matters in inverse proportion to
    /// distance: spacing grows geometrically outward, so a few hundred samples cover half a
    /// kilometre with the density concentrated where the player is standing.
    ///
    /// Sampling is spread over frames and only restarted when the camera has actually moved, so
    /// the per-frame cost is a fixed slice of surface queries and a draw call per patch.
    /// </summary>
    public static class ThermalTerrain
    {
        public struct Patch
        {
            public Vector3D Position;
            public Vector3 Normal;
            public float Radius;
            public float Temperature;
        }

        /// <summary>Rings of samples outward from the camera.</summary>
        private const int Rings = 22;

        /// <summary>Samples around each ring.</summary>
        private const int Spokes = 24;

        /// <summary>Radius of the innermost ring, m.</summary>
        private const double FirstRing = 6.0;

        /// <summary>Each ring is this much further out than the one inside it.</summary>
        private const double RingGrowth = 1.28;

        /// <summary>Surface queries per frame. The whole pattern refreshes over several frames.</summary>
        private const int QueriesPerFrame = 96;

        /// <summary>Camera movement that invalidates the pattern, m.</summary>
        private const double RebuildDistance = 12.0;

        private static readonly List<Patch> Patches = new List<Patch>();

        private static Vector3D origin;
        private static long planetId = -1;
        private static int cursor;
        private static bool complete;

        /// <summary>The patches to draw. Empty when the camera is not near a planet surface.</summary>
        public static IList<Patch> Current
        {
            get { return Patches; }
        }

        public static void Clear()
        {
            Patches.Clear();
            planetId = -1;
            cursor = 0;
            complete = false;
        }

        /// <summary>
        /// Advances the sample pattern. Call once a frame with the camera position; cheap when
        /// nothing has changed and bounded when everything has.
        /// </summary>
        public static void Update(Vector3D eye, float dayTemperature, float nightTemperature)
        {
            PlanetManager.Planet planet = PlanetManager.GetClosestPlanet(eye);
            MyPlanet entity = planet == null ? null : planet.Entity;

            if (entity == null)
            {
                if (Patches.Count > 0) Clear();
                return;
            }

            // Only from the ground, or close enough to it that the ground is what fills the frame.
            double altitude = Vector3D.Distance(eye, planet.Position) - entity.AverageRadius;
            if (altitude > 2000.0 || altitude < -500.0)
            {
                if (Patches.Count > 0) Clear();
                return;
            }

            if (entity.EntityId != planetId || Vector3D.DistanceSquared(eye, origin) > RebuildDistance * RebuildDistance)
            {
                planetId = entity.EntityId;
                origin = eye;
                cursor = 0;
                complete = false;
                Patches.Clear();
            }

            if (complete) return;

            Vector3D up = Vector3D.Normalize(origin - planet.Position);
            Vector3D east = Vector3D.Normalize(Vector3D.Cross(up, Vector3D.Forward.Equals(up) ? Vector3D.Right : Vector3D.Forward));
            Vector3D north = Vector3D.Cross(east, up);

            Vector3D sun = MyVisualScriptLogicProvider.GetSunDirection();

            int total = Rings * Spokes;
            int budget = Math.Min(QueriesPerFrame, total - cursor);

            for (int i = 0; i < budget; i++)
            {
                int index = cursor + i;
                int ring = index / Spokes;
                int spoke = index % Spokes;

                double radius = FirstRing * Math.Pow(RingGrowth, ring);
                double angle = (spoke / (double)Spokes) * Math.PI * 2.0;

                // Alternate rings are offset half a step so the pattern does not leave spokes of
                // bare ground running away from the camera.
                if ((ring & 1) == 1) angle += Math.PI / Spokes;

                Vector3D offset = (east * Math.Cos(angle) * radius) + (north * Math.Sin(angle) * radius);
                Vector3D probe = origin + offset;

                Vector3D surface = entity.GetClosestSurfacePointGlobal(probe);
                Vector3D normal = Vector3D.Normalize(surface - planet.Position);

                // Ground facing the sun has been absorbing all day; ground in shadow has not. This
                // is what puts relief into an otherwise flat field of ambient.
                float incidence = (float)Vector3D.Dot(normal, sun);
                if (incidence < 0f) incidence = 0f;

                Patch patch;
                patch.Position = surface;
                patch.Normal = (Vector3)normal;
                patch.Radius = (float)(radius * (RingGrowth - 1.0) * 1.4) + 3f;
                patch.Temperature = nightTemperature + ((dayTemperature - nightTemperature) * incidence);

                Patches.Add(patch);
            }

            cursor += budget;
            if (cursor >= total) complete = true;
        }
    }
}
