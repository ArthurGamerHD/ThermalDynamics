using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    public struct MountRect
    {
        public Vector3I Normal;

        public Vector3 Start;

        public Vector3 End;

        public bool Enabled;


        public MountRect(Vector3I normal, Vector3 start, Vector3 end)
        {
            Normal = normal;
            Start = start;
            End = end;
            Enabled = true;
        }
    }


    public delegate bool SealTest(Vector3I localCell, int face);

    public static class BlockSurfaceBuilder
    {
        public static readonly BoundingBox[] FaceMountBounds = new BoundingBox[]
        {

            new BoundingBox(new Vector3(0.002f, 0.002f, -0.1f), new Vector3(0.998f, 0.998f, 0.1f)),

            new BoundingBox(new Vector3(-0.1f, 0.002f, 0.002f), new Vector3(0.1f, 0.998f, 0.998f)),

            new BoundingBox(new Vector3(0.002f, 0.900f, 0.002f), new Vector3(0.998f, 1.1f, 0.998f)),

            new BoundingBox(new Vector3(0.002f, -0.1f, 0.002f), new Vector3(0.998f, 0.1f, 0.998f)),

            new BoundingBox(new Vector3(0.900f, 0.002f, 0.002f), new Vector3(1.1f, 0.998f, 0.998f)),

            new BoundingBox(new Vector3(0.002f, 0.002f, 0.900f), new Vector3(0.998f, 0.998f, 1.1f)),
        };


        public static bool MountCovers(ref MountRect mount, Vector3I localCell, int face)
        {
            if (!mount.Enabled) return false;
            if (face < 0 || face >= Face.Count) return false;
            if (mount.Normal != Face.Offsets[face]) return false;


            Vector3 offset = new Vector3(localCell.X, localCell.Y, localCell.Z);

            BoundingBox rectangle = new BoundingBox(
                Vector3.Min(mount.Start, mount.End) - offset,
                Vector3.Max(mount.Start, mount.End) - offset);

            return FaceMountBounds[face].Intersects(rectangle);
        }


        public static int[] BuildSurfaces(Vector3I size, bool sealsEverywhere, SealTest seals, IList<MountRect> mounts)
        {
            int sx = Math.Max(1, size.X);
            int sy = Math.Max(1, size.Y);
            int sz = Math.Max(1, size.Z);

            int[] states = new int[sx * sy * sz];

            for (int z = 0; z < sz; z++)
            {
                for (int y = 0; y < sy; y++)
                {
                    for (int x = 0; x < sx; x++)
                    {

                        Vector3I cell = new Vector3I(x, y, z);
                        int state = 0;

                        for (int face = 0; face < Face.Count; face++)
                        {
                            if (sealsEverywhere || (seals != null && seals(cell, face)))
                            {
                                state = CellSurface.WithSelfAirtight(state, face, true);
                            }

                            if (CoveredByMount(mounts, cell, face))
                            {
                                state = CellSurface.WithSelfMount(state, face, true);
                            }
                        }

                        states[x + (sx * y) + (sx * sy * z)] = state;
                    }
                }
            }

            return states;
        }


        public static int[] BuildFallbackSurfaces(Vector3I size, bool sealsEverywhere)
        {
            int state = CellSurface.SelfMountMask | (sealsEverywhere ? CellSurface.SelfAirtightMask : 0);

            int count = Math.Max(1, size.X) * Math.Max(1, size.Y) * Math.Max(1, size.Z);
            int[] states = new int[count];
            for (int i = 0; i < count; i++)
            {
                states[i] = state;
            }
            return states;
        }


        private static bool CoveredByMount(IList<MountRect> mounts, Vector3I cell, int face)
        {
            if (mounts == null) return false;

            for (int i = 0; i < mounts.Count; i++)
            {
                MountRect mount = mounts[i];
                if (MountCovers(ref mount, cell, face)) return true;
            }
            return false;
        }
    }
}
