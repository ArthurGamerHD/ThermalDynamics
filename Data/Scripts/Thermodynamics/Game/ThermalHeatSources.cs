using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRage.ModAPI;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// Point heat sources registered by other mods: a burning wreck, a plasma bolt, a second sun.
    ///
    /// A source is a position, a power and a reach. The registry turns that into the irradiance
    /// reaching one grid — watts spread over the sphere at that distance — and the simulation
    /// treats the result exactly as it treats sunlight, because it is the same physics.
    ///
    /// Sources bound to an entity follow it and disappear with it, so a mod that registers one on
    /// a projectile does not have to remember to clean it up.
    /// </summary>
    public static class ThermalHeatSources
    {
        public class HeatSource
        {
            /// <summary>Registry identity, returned to whoever registered it.</summary>
            public int Id;

            /// <summary>The entity this source follows. Null for a fixed world position.</summary>
            public IMyEntity Entity;

            /// <summary>World position, used when <see cref="Entity"/> is null.</summary>
            public Vector3D Position;

            /// <summary>Radiated power, W. Spread over the sphere at the sampling distance.</summary>
            public float Watts;

            /// <summary>Beyond this many metres the source is not sampled at all.</summary>
            public float Range;

            /// <summary>False once the source has been removed or its entity has gone.</summary>
            public bool Alive = true;

            public Vector3D WorldPosition
            {
                get { return Entity == null ? Position : Entity.WorldMatrix.Translation; }
            }
        }

        private static readonly List<HeatSource> Sources = new List<HeatSource>();
        private static int nextId = 1;

        /// <summary>Registered sources, live ones only.</summary>
        public static int Count
        {
            get { return Sources.Count; }
        }

        /// <summary>
        /// Registers a source that follows an entity. Returns its id, or 0 when the entity is
        /// already gone.
        /// </summary>
        public static int Add(IMyEntity entity, float watts, float range)
        {
            if (entity == null || entity.MarkedForClose) return 0;
            return Register(entity, Vector3D.Zero, watts, range);
        }

        /// <summary>Registers a source fixed at a world position.</summary>
        public static int Add(Vector3D position, float watts, float range)
        {
            return Register(null, position, watts, range);
        }

        private static int Register(IMyEntity entity, Vector3D position, float watts, float range)
        {
            if (watts <= 0f || range <= 0f) return 0;

            HeatSource source = new HeatSource();
            source.Id = nextId++;
            source.Entity = entity;
            source.Position = position;
            source.Watts = watts;
            source.Range = range;

            Sources.Add(source);
            return source.Id;
        }

        /// <summary>Changes a source's output. Returns false when the id is not registered.</summary>
        public static bool Update(int id, float watts)
        {
            for (int i = 0; i < Sources.Count; i++)
            {
                if (Sources[i].Id != id) continue;
                Sources[i].Watts = Math.Max(0f, watts);
                return true;
            }
            return false;
        }

        public static bool Remove(int id)
        {
            for (int i = 0; i < Sources.Count; i++)
            {
                if (Sources[i].Id != id) continue;
                Sources[i].Alive = false;
                Sources.RemoveAt(i);
                return true;
            }
            return false;
        }

        public static void Clear()
        {
            Sources.Clear();
        }

        /// <summary>
        /// Fills a host buffer with the sources reaching one grid.
        ///
        /// Called once per grid per sampled step, so it is written to allocate nothing: the
        /// caller owns the buffer and this grows it only when a session genuinely has more
        /// sources than it did before.
        /// </summary>
        /// <param name="gridCentre">World position to sample at.</param>
        /// <param name="worldToLocal">Grid orientation, transposed — world direction to local.</param>
        /// <param name="buffer">Host buffer, resized when too small.</param>
        /// <returns>Number of entries written.</returns>
        public static int Sample(Vector3D gridCentre, ref MatrixD worldToLocal, ref HeatSourceState[] buffer)
        {
            if (Sources.Count == 0) return 0;

            int written = 0;

            for (int i = Sources.Count - 1; i >= 0; i--)
            {
                HeatSource source = Sources[i];

                // An entity that has been removed takes its source with it.
                if (source.Entity != null && (source.Entity.MarkedForClose || source.Entity.Closed))
                {
                    source.Alive = false;
                    Sources.RemoveAt(i);
                    continue;
                }

                if (source.Watts <= 0f) continue;

                Vector3D delta = source.WorldPosition - gridCentre;
                double distanceSquared = delta.LengthSquared();
                if (distanceSquared > source.Range * (double)source.Range) continue;

                // Inside a metre the inverse square law runs away; a source you are standing in
                // delivers its full output and no more.
                if (distanceSquared < 1.0) distanceSquared = 1.0;

                float irradiance = (float)(source.Watts / (4.0 * Math.PI * distanceSquared));
                if (irradiance <= 0f) continue;

                Vector3D direction = Vector3D.Normalize(delta);
                Vector3 local = (Vector3)Vector3D.TransformNormal(direction, worldToLocal);

                if (buffer == null || written >= buffer.Length)
                {
                    HeatSourceState[] grown = new HeatSourceState[Math.Max(4, written * 2)];
                    if (buffer != null) Array.Copy(buffer, grown, written);
                    buffer = grown;
                }

                buffer[written] = new HeatSourceState(local, irradiance);
                written++;
            }

            return written;
        }
    }
}
