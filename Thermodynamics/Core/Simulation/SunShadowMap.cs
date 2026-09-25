using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    public class SunShadowMap
    {

        private CellBitset shadowed = new CellBitset();


        private CellBitset building = new CellBitset();


        private readonly List<Vector3I> pending = new List<Vector3I>();


        private readonly CellBitset queued = new CellBitset();

        public struct Occluder
        {
            public GridModel Model;

            public MatrixD ToOccluder;

            public long Id;
        }


        private readonly List<Occluder> occluders = new List<Occluder>();


        private readonly List<Vector3D> occluderSun = new List<Vector3D>();

        private GridModel grid;

        private GridModel resultGrid;

        private Vector3 sun;
        private Vector3 passSun;
        private int cursor;

        public bool IsBuilt { get; private set; }

        public bool IsRunning
        {
            get { return cursor < pending.Count; }
        }

        public Vector3 SunDirection { get { return sun; } }

        public int ShadowedCount { get { return shadowed.Count; } }

        public int PendingCells { get { return Math.Max(0, pending.Count - cursor); } }


        public bool NeedsRestart(ref Vector3 sunLocal, float cosineTolerance)
        {
            if (!IsBuilt && !IsRunning) return true;

            Vector3 reference = IsRunning ? passSun : sun;
            return Vector3.Dot(reference, sunLocal) < cosineTolerance;
        }


        public void Clear()
        {
            shadowed.Clear();
            building.Clear();
            pending.Clear();
            queued.Clear();
            cursor = 0;
            grid = null;
            resultGrid = null;
            IsBuilt = false;
        }


        public void Restart(GridModel model, Vector3 sunLocal)
        {
            Restart(model, sunLocal, null);
        }


        public void Restart(GridModel model, Vector3 sunLocal, IList<Occluder> others)
        {
            pending.Clear();
            occluders.Clear();
            occluderSun.Clear();
            cursor = 0;

            grid = model;
            if (grid == null || sunLocal.LengthSquared() < 1e-6f) return;

            Vector3I setMin = grid.Min - Vector3I.One;

            Vector3I setMaxExclusive = grid.Max + new Vector3I(2, 2, 2);
            building.Reset(setMin, setMaxExclusive);
            queued.Reset(setMin, setMaxExclusive);

            passSun = Vector3.Normalize(sunLocal);

            if (others != null)
            {
                for (int i = 0; i < others.Count; i++)
                {
                    Occluder occluder = others[i];
                    if (occluder.Model == null) continue;

                    Vector3D direction = Vector3D.TransformNormal(passSun, occluder.ToOccluder);
                    if (direction.LengthSquared() < 1e-12) continue;

                    occluders.Add(occluder);
                    occluderSun.Add(Vector3D.Normalize(direction));
                }
            }

            IList<BlockInstance> blocks = grid.Blocks;
            for (int i = 0; i < blocks.Count; i++)
            {
                Vector3I[] cells = blocks[i].Cells;
                for (int c = 0; c < cells.Length; c++)
                {
                    for (int face = 0; face < Face.Count; face++)
                    {
                        Vector3I outside = cells[c] + Face.Offsets[face];

                        if (grid.IsOccupied(outside)) continue;
                        if (!queued.Add(outside)) continue;

                        pending.Add(outside);
                    }
                }
            }
        }


        public bool Step(int budget)
        {
            if (!IsRunning) return false;

            int slice = Math.Max(1, budget);
            int end = slice >= pending.Count - cursor ? pending.Count : cursor + slice;

            for (; cursor < end; cursor++)
            {
                if (Blocked(pending[cursor])) building.Add(pending[cursor]);
            }

            if (IsRunning) return false;

            CellBitset finished = building;
            building = shadowed;
            shadowed = finished;

            pending.Clear();
            cursor = 0;

            sun = passSun;
            resultGrid = grid;
            IsBuilt = true;
            return true;
        }


        public void RunToCompletion()
        {
            while (IsRunning) Step(int.MaxValue);
        }


        public bool IsLit(Vector3I cell)
        {
            return !IsBuilt || !shadowed.Contains(cell);
        }


        public bool IsFaceLit(Vector3I cell, int face)
        {
            Vector3I outside = cell + Face.Offsets[face];

            if (resultGrid != null && resultGrid.IsOccupied(outside)) return false;

            return IsLit(outside);
        }


        public float FaceLitFraction(BlockInstance block, int face)
        {
            if (!IsBuilt || block == null) return 1f;

            BoxGeometry.FaceSpan span = BoxGeometry.Span(block.Min, block.MaxExclusive, face);

            int cells = 0;
            int lit = 0;

            for (int a = span.MinU; a < span.MaxExclusiveU; a++)
            {
                for (int b = span.MinV; b < span.MaxExclusiveV; b++)
                {
                    Vector3I cell = BoxGeometry.WithComponent(Vector3I.Zero, span.Axis, span.Slab);
                    cell = BoxGeometry.WithComponent(cell, span.U, a);
                    cell = BoxGeometry.WithComponent(cell, span.V, b);

                    cells++;
                    if (IsFaceLit(cell, face)) lit++;
                }
            }

            return cells == 0 ? 1f : lit / (float)cells;
        }

        public int OccluderCount { get { return occluders.Count; } }


        private bool Blocked(Vector3I start)
        {
            if (BlockedBySelf(start)) return true;


            Vector3D origin = new Vector3D(start.X, start.Y, start.Z);

            for (int i = 0; i < occluders.Count; i++)
            {
                Occluder occluder = occluders[i];

                Vector3D from = Vector3D.Transform(origin, occluder.ToOccluder);
                if (VoxelWalk.Blocked(occluder.Model, from, occluderSun[i])) return true;
            }

            return false;
        }


        private bool BlockedBySelf(Vector3I start)
        {
            Vector3I min = grid.Min;
            Vector3I max = grid.Max;


            float exit = BoxExit(start, min, max);
            if (exit <= 0f) return false;

            int x = start.X, y = start.Y, z = start.Z;


            int stepX = Sign(passSun.X), stepY = Sign(passSun.Y), stepZ = Sign(passSun.Z);


            float tMaxX = Boundary(passSun.X);

            float tMaxY = Boundary(passSun.Y);

            float tMaxZ = Boundary(passSun.Z);


            float tDeltaX = Delta(passSun.X);

            float tDeltaY = Delta(passSun.Y);

            float tDeltaZ = Delta(passSun.Z);

            int limit = (2 * ((max.X - min.X) + (max.Y - min.Y) + (max.Z - min.Z))) + 8;

            for (int i = 0; i < limit; i++)
            {
                float t;

                if (tMaxX <= tMaxY && tMaxX <= tMaxZ)
                {
                    t = tMaxX;
                    x += stepX;
                    tMaxX += tDeltaX;
                }

                else if (tMaxY <= tMaxZ)
                {
                    t = tMaxY;
                    y += stepY;
                    tMaxY += tDeltaY;
                }
                else
                {
                    t = tMaxZ;
                    z += stepZ;
                    tMaxZ += tDeltaZ;
                }

                if (t > exit) return false;

                if (grid.IsOccupied(new Vector3I(x, y, z))) return true;
            }

            return false;
        }


        private float BoxExit(Vector3I start, Vector3I min, Vector3I max)
        {
            float enter = 0f;
            float exit = float.MaxValue;

            for (int axis = 0; axis < 3; axis++)
            {
                float origin = BoxGeometry.Component(start, axis);
                float direction = axis == 0 ? passSun.X : axis == 1 ? passSun.Y : passSun.Z;

                float low = BoxGeometry.Component(min, axis) - 0.5f;
                float high = BoxGeometry.Component(max, axis) + 0.5f;

                if (Math.Abs(direction) < 1e-6f)
                {
                    if (origin < low || origin > high) return 0f;
                    continue;
                }

                float t1 = (low - origin) / direction;
                float t2 = (high - origin) / direction;

                if (t1 > t2)
                {
                    float swap = t1;
                    t1 = t2;
                    t2 = swap;
                }

                if (t1 > enter) enter = t1;
                if (t2 < exit) exit = t2;
            }

            return exit < enter ? 0f : exit;
        }


        private static int Sign(float value)
        {
            if (value > 0f) return 1;
            if (value < 0f) return -1;
            return 0;
        }


        private static float Boundary(float component)
        {
            float magnitude = Math.Abs(component);
            return magnitude < 1e-6f ? float.MaxValue : 0.5f / magnitude;
        }


        private static float Delta(float component)
        {
            float magnitude = Math.Abs(component);
            return magnitude < 1e-6f ? float.MaxValue : 1f / magnitude;
        }
    }
}
