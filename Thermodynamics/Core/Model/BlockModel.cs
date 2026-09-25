using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    public class BlockModel
    {
        public string Name = "Unnamed";

        public Vector3I Size = Vector3I.One;

        public float Mass = 100f;

        public BlockThermalProperties Thermal = BlockThermalProperties.Default();

        public CoolantShape Coolant;

        public HeatPumpShape HeatPump;

        public int[] LocalSurfaces;

        public int[] LocalSurfacesWhenOpen;

        public bool HasOpenState
        {
            get { return LocalSurfacesWhenOpen != null; }
        }


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

        public int CellCount
        {
            get { return Math.Max(1, Size.X) * Math.Max(1, Size.Y) * Math.Max(1, Size.Z); }
        }

        public Vector3I Extents
        {

            get { return new Vector3I(Math.Max(1, Size.X), Math.Max(1, Size.Y), Math.Max(1, Size.Z)); }
        }


        private float[] localMountFraction;
        private float[] localSealFraction;


        public float LocalFaceMountFraction(int localFace)
        {
            EnsureFaceFractions();
            return (localFace >= 0 && localFace < Face.Count) ? localMountFraction[localFace] : 0f;
        }


        public float LocalFaceSealFraction(int localFace)
        {
            EnsureFaceFractions();
            return (localFace >= 0 && localFace < Face.Count) ? localSealFraction[localFace] : 0f;
        }


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
            localMountFraction = mount;
        }


        private void InvalidateFaceFractions()
        {
            localMountFraction = null;
            localSealFraction = null;
        }


        public BlockModel()
        {
        }


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


        public int LocalCellIndex(Vector3I localCell)
        {
            int sx = Math.Max(1, Size.X);
            int sy = Math.Max(1, Size.Y);
            return localCell.X + (sx * localCell.Y) + (sx * sy * localCell.Z);
        }

        public class FaceFractions
        {
            public readonly float[] Mount = new float[Face.Count];
            public readonly float[] SealClosed = new float[Face.Count];
            public readonly float[] SealOpen = new float[Face.Count];
        }

        private readonly FaceFractions[] fractionsByOrientation = new FaceFractions[36];

        private readonly int[][] oneCellSurfacesByOrientation = new int[72][];


        public int[] OneCellSurfaces(BlockOrientation orientation, bool structural)
        {

            int index = OneCellSlot(orientation, structural);
            return index < 0 ? null : oneCellSurfacesByOrientation[index];
        }


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


        private int OneCellSlot(BlockOrientation orientation, bool structural)
        {
            if (CellCount != 1) return -1;

            int index = (((int)orientation.Forward * 6) + (int)orientation.Up) * 2
                + (structural ? 0 : 1);
            return index >= 0 && index < oneCellSurfacesByOrientation.Length ? index : -1;
        }


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
