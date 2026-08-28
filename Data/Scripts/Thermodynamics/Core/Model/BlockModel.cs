using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Everything the simulation needs to know about a block <em>type</em>. One instance is
    /// shared by every placed block of that type, so it must stay immutable after construction.
    /// </summary>
    public class BlockModel
    {
        /// <summary>Subtype name, for diagnostics and for matching save data.</summary>
        public string Name = "Unnamed";

        /// <summary>Size in cells, in the block's own local space.</summary>
        public Vector3I Size = Vector3I.One;

        /// <summary>Mass in kg of a complete block.</summary>
        public float Mass = 100f;

        /// <summary>Thermal description. Never null.</summary>
        public BlockThermalProperties Thermal = BlockThermalProperties.Default();

        /// <summary>Coolant plumbing, or null when the block is not part of the coolant system.</summary>
        public CoolantShape Coolant;

        /// <summary>Heat-pump hardware, or null when the block does not pump heat.</summary>
        public HeatPumpShape HeatPump;

        /// <summary>
        /// Per-cell surface bits in block-local space, indexed by
        /// <see cref="LocalCellIndex"/>. Only the "self" half is meaningful here.
        /// </summary>
        public int[] LocalSurfaces;

        /// <summary>
        /// The same bits for a block whose state has stopped it sealing, such as an open door. Null
        /// for nearly every block. A second bit set rather than a flag, because a door's sealing is not
        /// uniform: its walk-through face seals by door rule and its sides from the definition.
        /// </summary>
        public int[] LocalSurfacesWhenOpen;

        /// <summary>
        /// True when this block has an open state, meaning it is a door — the only way to be one.
        /// A block that has none is unaffected by <see cref="BlockInstance.IsSealedByDoorState"/>
        /// whatever that flag says, so its live surfaces and its structure cannot disagree.
        /// </summary>
        public bool HasOpenState
        {
            get { return LocalSurfacesWhenOpen != null; }
        }

        /// <summary>
        /// Surface bits for one local cell in the given sealing state. A block with no open state
        /// answers the same either way.
        /// </summary>
        public int LocalSurfaceState(Vector3I localCell, bool sealedByState)
        {
            int index = LocalCellIndex(localCell);

            if (!sealedByState && LocalSurfacesWhenOpen != null)
            {
                return LocalSurfacesWhenOpen[index];
            }

            return LocalSurfaces == null
                ? (CellSurface.SelfAirtightMask | CellSurface.SelfMountMask)
                : LocalSurfaces[index];
        }

        /// <summary>Total cells occupied.</summary>
        public int CellCount
        {
            get { return Math.Max(1, Size.X) * Math.Max(1, Size.Y) * Math.Max(1, Size.Z); }
        }

        /// <summary>Extents in cells, with every axis at least one.</summary>
        public Vector3I Extents
        {
            get { return new Vector3I(Math.Max(1, Size.X), Math.Max(1, Size.Y), Math.Max(1, Size.Z)); }
        }

        // ---- per-face summaries ----------------------------------------------------------

        private float[] localMountFraction;
        private float[] localSealFraction;

        /// <summary>
        /// Fraction of one local face's cells that carry a mount surface, 0..1.
        ///
        /// Computed once per block type and cached, so a placed block never walks its own cells.
        /// This is the summary the conduction and exposure arithmetic works from, and it is
        /// size-independent where a per-cell list is not.
        /// </summary>
        public float LocalFaceMountFraction(int localFace)
        {
            EnsureFaceFractions();
            return (localFace >= 0 && localFace < Face.Count) ? localMountFraction[localFace] : 0f;
        }

        /// <summary>Fraction of one local face's cells that seal while the block is sealing, 0..1.</summary>
        public float LocalFaceSealFraction(int localFace)
        {
            EnsureFaceFractions();
            return (localFace >= 0 && localFace < Face.Count) ? localSealFraction[localFace] : 0f;
        }

        /// <summary>The same for the open state, which for a door is its side faces alone.</summary>
        public float LocalFaceSealFractionWhenOpen(int localFace)
        {
            EnsureFaceFractions();
            return (localFace >= 0 && localFace < Face.Count) ? localOpenSealFraction[localFace] : 0f;
        }

        private float[] localOpenSealFraction;

        private void EnsureFaceFractions()
        {
            if (localMountFraction != null) return;

            float[] mount = new float[Face.Count];
            float[] seal = new float[Face.Count];
            float[] openSeal = new float[Face.Count];
            Vector3I extents = Extents;

            for (int face = 0; face < Face.Count; face++)
            {
                int total = 0;
                int mounted = 0;
                int sealed_ = 0;
                int openSealed = 0;
                int currentFace = face;

                BoxGeometry.ForEachFaceCell(Vector3I.Zero, extents, face, cell =>
                {
                    int state = LocalSurfaceState(cell, true);
                    int openState = LocalSurfaceState(cell, false);

                    total++;
                    if (CellSurface.SelfMount(state, currentFace)) mounted++;
                    if (CellSurface.SelfAirtight(state, currentFace)) sealed_++;
                    if (CellSurface.SelfAirtight(openState, currentFace)) openSealed++;
                });

                mount[face] = total == 0 ? 0f : mounted / (float)total;
                seal[face] = total == 0 ? 0f : sealed_ / (float)total;
                openSeal[face] = total == 0 ? 0f : openSealed / (float)total;
            }

            localSealFraction = seal;
            localOpenSealFraction = openSeal;
            localMountFraction = mount;   // assigned last: it is the "is cached" flag
        }

        /// <summary>Drops the cached per-face summaries after the surface bits change.</summary>
        private void InvalidateFaceFractions()
        {
            localMountFraction = null;
            localSealFraction = null;
        }

        public BlockModel()
        {
        }

        /// <summary>
        /// A solid block: every face of every cell is both airtight and a mount surface. The
        /// common case for armour and most functional blocks.
        /// </summary>
        public static BlockModel Solid(string name, Vector3I size, float mass, BlockThermalProperties thermal)
        {
            BlockModel m = new BlockModel();
            m.Name = name;
            m.Size = size;
            m.Mass = mass;
            m.Thermal = thermal ?? BlockThermalProperties.Default();
            m.LocalSurfaces = BuildUniformSurfaces(m.CellCount, CellSurface.SelfAirtightMask | CellSurface.SelfMountMask);
            return m;
        }

        /// <summary>
        /// A block that mounts on every face but seals none: a lattice, an open frame, or any block
        /// the game marks as not airtight.
        /// </summary>
        public static BlockModel Open(string name, Vector3I size, float mass, BlockThermalProperties thermal)
        {
            BlockModel m = new BlockModel();
            m.Name = name;
            m.Size = size;
            m.Mass = mass;
            m.Thermal = thermal ?? BlockThermalProperties.Default();
            m.LocalSurfaces = BuildUniformSurfaces(m.CellCount, CellSurface.SelfMountMask);
            return m;
        }

        private static int[] BuildUniformSurfaces(int cellCount, int state)
        {
            int[] surfaces = new int[cellCount];
            for (int i = 0; i < cellCount; i++)
            {
                surfaces[i] = state;
            }
            return surfaces;
        }

        /// <summary>Index into <see cref="LocalSurfaces"/> for a block-local cell.</summary>
        public int LocalCellIndex(Vector3I localCell)
        {
            int sx = Math.Max(1, Size.X);
            int sy = Math.Max(1, Size.Y);
            return localCell.X + (sx * localCell.Y) + (sx * sy * localCell.Z);
        }

        /// <summary>
        /// A block's six mount and seal fractions in grid space, for one orientation. Shared per model
        /// rather than held per instance: at most twenty-four copies per block type, against four
        /// <c>float[6]</c> arrays per placed block. See memory.md, 1c.
        /// </summary>
        public class FaceFractions
        {
            public readonly float[] Mount = new float[Face.Count];
            public readonly float[] SealClosed = new float[Face.Count];
            public readonly float[] SealOpen = new float[Face.Count];
        }

        /// <summary>
        /// Face fractions by orientation, built on demand. Six forward directions by six up
        /// directions; only twenty-four of the thirty-six are legal and the rest stay null.
        /// </summary>
        private readonly FaceFractions[] fractionsByOrientation = new FaceFractions[36];

        /// <summary>
        /// One-cell blocks' surface arrays, one per orientation and layer, shared by every instance
        /// of this model in that orientation.
        ///
        /// <para>
        /// **A one-cell block's surface array does not depend on where it is.** Its only cell is
        /// the origin, so the bits are a function of the model and the orientation alone — and a
        /// census hull is mostly one-cell blocks, so `place` was allocating an `int[1]` per block
        /// for an answer that repeats a few dozen times over the whole grid. The cells array is
        /// **not** interned and cannot be: it holds the block's own `Min`.
        /// </para>
        ///
        /// <para>
        /// Same shape as <see cref="fractionsByOrientation"/> above, and safe for the same reason:
        /// two threads that race to fill a slot compute the same value and write a reference
        /// atomically, so the loser's array is garbage rather than a wrong answer. What it *does*
        /// require is that nothing writes through the array afterwards, which
        /// `OneCellSurfacesAreNeverWrittenThrough` asserts over the whole tree — an aliased array
        /// written by one block would change the others silently.
        /// </para>
        /// </summary>
        private readonly int[][] oneCellSurfacesByOrientation = new int[72][];

        /// <summary>
        /// The shared `int[1]` a one-cell block of this model, orientation and layer uses, or null
        /// if this slot has not been filled yet.
        ///
        /// <para>
        /// **Asked and stored in two calls rather than one call taking a rotate delegate.** The
        /// delegate version was written first and measured: a method group converted at a call site
        /// on the placement path allocates a delegate object *per block*, which is larger than the
        /// `int[1]` it was there to save — the `place` stage's allocation went from 36,044 KB to
        /// 40,005 KB. The stage lab's allocation column is what said so.
        /// </para>
        /// </summary>
        public int[] OneCellSurfaces(BlockOrientation orientation, bool structural)
        {
            int index = OneCellSlot(orientation, structural);
            return index < 0 ? null : oneCellSurfacesByOrientation[index];
        }

        /// <summary>
        /// Fills a slot with the caller's rotated bits and hands back the array every later block
        /// of this model, orientation and layer will share.
        ///
        /// <para>
        /// A racing caller may have filled it first. Its array holds the same value — the bits are
        /// a function of the model, the orientation and the layer, and of nothing else — so the
        /// winner's is kept and the loser's is garbage rather than a wrong answer.
        /// </para>
        /// </summary>
        public int[] StoreOneCellSurfaces(BlockOrientation orientation, bool structural, int rotated)
        {
            int index = OneCellSlot(orientation, structural);
            if (index < 0) return new[] { rotated };

            int[] known = oneCellSurfacesByOrientation[index];
            if (known != null) return known;

            int[] built = { rotated };
            oneCellSurfacesByOrientation[index] = built;
            return built;
        }

        /// <summary>Which slot a one-cell block of this orientation and layer shares, or -1 if it shares none.</summary>
        private int OneCellSlot(BlockOrientation orientation, bool structural)
        {
            if (CellCount != 1) return -1;

            int index = (((int)orientation.Forward * 6) + (int)orientation.Up) * 2
                + (structural ? 0 : 1);
            return index >= 0 && index < oneCellSurfacesByOrientation.Length ? index : -1;
        }

        /// <summary>
        /// The face fractions for one orientation of this model, built on first use. Two threads may
        /// arrive together and both are allowed to build: the values are identical, and the reference
        /// is published by one aligned write. Cheaper than a lock on the placement path.
        /// </summary>
        public FaceFractions FractionsFor(BlockOrientation orientation)
        {
            int index = ((int)orientation.Forward * 6) + (int)orientation.Up;
            if (index < 0 || index >= fractionsByOrientation.Length) index = 0;

            FaceFractions known = fractionsByOrientation[index];
            if (known != null) return known;

            FaceFractions built = new FaceFractions();
            for (int localFace = 0; localFace < Face.Count; localFace++)
            {
                int gridFace = orientation.RotateFace(localFace);
                if (gridFace < 0) continue;

                built.Mount[gridFace] = LocalFaceMountFraction(localFace);
                built.SealClosed[gridFace] = LocalFaceSealFraction(localFace);
                built.SealOpen[gridFace] = LocalFaceSealFractionWhenOpen(localFace);
            }

            fractionsByOrientation[index] = built;
            return built;
        }

        public IEnumerable<Vector3I> LocalCells()
        {
            for (int z = 0; z < Math.Max(1, Size.Z); z++)
            {
                for (int y = 0; y < Math.Max(1, Size.Y); y++)
                {
                    for (int x = 0; x < Math.Max(1, Size.X); x++)
                    {
                        yield return new Vector3I(x, y, z);
                    }
                }
            }
        }

        /// <summary>
        /// Overrides one local cell's self surface bits. Describes blocks that are only partly
        /// sealed, such as slopes and doors.
        /// </summary>
        public BlockModel SetLocalSurface(Vector3I localCell, int selfState)
        {
            if (LocalSurfaces == null)
            {
                LocalSurfaces = new int[CellCount];
            }
            LocalSurfaces[LocalCellIndex(localCell)] = CellSurface.SelfOnly(selfState);
            InvalidateFaceFractions();
            return this;
        }

        /// <summary>Attaches coolant plumbing, returning this for chaining.</summary>
        public BlockModel WithCoolant(CoolantShape shape)
        {
            Coolant = shape;
            return this;
        }

        public BlockModel WithHeatPump(HeatPumpShape shape)
        {
            HeatPump = shape;
            return this;
        }
    }
}
