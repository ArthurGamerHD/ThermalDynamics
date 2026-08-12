using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// One mount rectangle on a block type, in block-local space: the face it sits on and the
    /// area of that face it covers.
    ///
    /// This mirrors the game's <c>MyCubeBlockDefinition.MountPoint</c> without depending on it,
    /// so the surface maths can be built and tested outside the game.
    /// </summary>
    public struct MountRect
    {
        /// <summary>Outward normal, a single-axis unit vector in block-local space.</summary>
        public Vector3I Normal;

        /// <summary>One corner of the rectangle, in local cell units.</summary>
        public Vector3 Start;

        /// <summary>The opposite corner, in local cell units.</summary>
        public Vector3 End;

        /// <summary>Disabled mounts are declared but not buildable, and do not conduct.</summary>
        public bool Enabled;

        public MountRect(Vector3I normal, Vector3 start, Vector3 end)
        {
            Normal = normal;
            Start = start;
            End = end;
            Enabled = true;
        }
    }

    /// <summary>
    /// Whether one face of one block-local cell seals. The host answers from the block
    /// definition's pressurisation table.
    /// </summary>
    public delegate bool SealTest(Vector3I localCell, int face);

    /// <summary>
    /// Builds the per-cell surface bits of a block <em>type</em> from the two things every host
    /// can describe: which faces seal, and where the mount surfaces are.
    ///
    /// This runs once per block definition, not once per placed block, so its cost never enters
    /// the simulation. Everything it produces is block-local; <see cref="BlockInstance"/>
    /// rotates it into grid space.
    /// </summary>
    public static class BlockSurfaceBuilder
    {
        /// <summary>
        /// The slab of a unit cell each face occupies, in local cell units. A mount rectangle
        /// counts for a face when it intersects that face's slab.
        ///
        /// The 0.002 inset keeps a mount that merely abuts the edge of a neighbouring face from
        /// registering on both; the 0.1 depth accepts the small overshoot the game's own mount
        /// rectangles carry.
        /// </summary>
        public static readonly BoundingBox[] FaceMountBounds = new BoundingBox[]
        {
            new BoundingBox(new Vector3(0.002f, 0.002f, -0.1f), new Vector3(0.998f, 0.998f, 0.1f)),   // Forward  -Z
            new BoundingBox(new Vector3(-0.1f, 0.002f, 0.002f), new Vector3(0.1f, 0.998f, 0.998f)),   // Left     -X
            new BoundingBox(new Vector3(0.002f, 0.900f, 0.002f), new Vector3(0.998f, 1.1f, 0.998f)),  // Up       +Y
            new BoundingBox(new Vector3(0.002f, -0.1f, 0.002f), new Vector3(0.998f, 0.1f, 0.998f)),   // Down     -Y
            new BoundingBox(new Vector3(0.900f, 0.002f, 0.002f), new Vector3(1.1f, 0.998f, 0.998f)),  // Right    +X
            new BoundingBox(new Vector3(0.002f, 0.002f, 0.900f), new Vector3(0.998f, 0.998f, 1.1f)),  // Backward +Z
        };

        /// <summary>
        /// True when <paramref name="mount"/> lands on <paramref name="face"/> of
        /// <paramref name="localCell"/>.
        /// </summary>
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

        /// <summary>
        /// Builds one surface state per block-local cell, indexed the same way
        /// <see cref="BlockModel.LocalCellIndex"/> indexes them.
        /// </summary>
        /// <param name="size">Block size in cells.</param>
        /// <param name="sealsEverywhere">
        /// The block is airtight as a whole, so every face seals and
        /// <paramref name="seals"/> is not consulted.
        /// </param>
        /// <param name="seals">Per-face sealing test, or null when nothing seals.</param>
        /// <param name="mounts">Mount rectangles in block-local space, or null for none.</param>
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

        /// <summary>
        /// A block whose mount points the host cannot describe: every face of every cell mounts,
        /// which is what the simulation assumed before mount data existed.
        /// </summary>
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
