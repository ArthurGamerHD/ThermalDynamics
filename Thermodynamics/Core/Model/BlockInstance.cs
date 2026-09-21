using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    public class BlockInstance
    {
        public BlockModel Model;

        public Vector3I Position;

        public Vector3I Min;

        public Vector3I MaxExclusive;

        public BlockOrientation Orientation;

        public float Mass;

        public float PowerProducedWatts;

        public float PowerConsumedWatts;

        public float ThrustWatts;

        public bool IsSealedByDoorState = true;

        public long Key;

        public int NodeIndex = -1;

        public int GridSlot = -1;

        private int[] gridSurfaces;
        private int[] gridStructuralSurfaces;
        private Vector3I[] gridCells;

        private BlockModel.FaceFractions fractions;

/// <summary>BlockInstance operation.</summary>
        public BlockInstance(BlockModel model, Vector3I min, BlockOrientation orientation)
        {
            if (model == null) throw new ArgumentNullException("model");

            Model = model;
            Orientation = orientation;
            Min = min;
            Mass = model.Mass;

            Vector3I rotatedSize = Vector3I.Abs(orientation.Rotate(model.Size));
            MaxExclusive = min + rotatedSize;
            Position = min;
            Key = GridMath.Key(Position);

            BuildGridSurfaces();
        }

        public Vector3I[] Cells
        {
            get { return gridCells; }
        }

        public int[] SelfSurfaces
        {
            get { return gridSurfaces; }
        }

        public int[] StructuralSurfaces
        {
            get { return gridStructuralSurfaces; }
        }

        public bool HasStateDependentSealing
        {
            get { return Model.HasOpenState; }
        }

/// <summary>IsPortalFace operation.</summary>
        public bool IsPortalFace(int gridFace)
        {
            if (!HasStateDependentSealing) return false;
            if (gridFace < 0 || gridFace >= Face.Count) return false;

            return fractions.SealClosed[gridFace] > 0f && fractions.SealOpen[gridFace] <= 0f;
        }

        public Vector3I Extents
        {
            get { return MaxExclusive - Min; }
        }

/// <summary>MountFraction operation.</summary>
        public float MountFraction(int gridFace)
        {
            return (gridFace >= 0 && gridFace < Face.Count) ? fractions.Mount[gridFace] : 0f;
        }

/// <summary>SealFraction operation.</summary>
        public float SealFraction(int gridFace)
        {
            if (gridFace < 0 || gridFace >= Face.Count) return 0f;
            return IsSealedByDoorState ? fractions.SealClosed[gridFace] : fractions.SealOpen[gridFace];
        }

/// <summary>FaceAreaCells operation.</summary>
        public int FaceAreaCells(int gridFace)
        {
            return BoxGeometry.FaceAreaCells(Extents, gridFace);
        }

        public int CellCount
        {
            get { return gridCells.Length; }
        }

        public BlockThermalProperties Thermal
        {
            get { return Model.Thermal; }
        }

        public string Name
        {
            get { return Model.Name; }
        }

/// <summary>LocalToGrid operation.</summary>
        public Vector3I LocalToGrid(Vector3I localCell)
        {
            Vector3I rotated = Orientation.Rotate(localCell);
            return Min + (rotated - RotatedLocalMin());
        }

/// <summary>LocalDirectionToGrid operation.</summary>
        public Vector3I LocalDirectionToGrid(Vector3I localDirection)
        {
            return Orientation.Rotate(localDirection);
        }

/// <summary>RotatedLocalMin operation.</summary>
        private Vector3I RotatedLocalMin()
        {
            Vector3I a = Orientation.Rotate(Vector3I.Zero);
            Vector3I b = Orientation.Rotate(Model.Size - Vector3I.One);
            return Vector3I.Min(a, b);
        }

/// <summary>Builds the API method table.</summary>
        private void BuildFaceFractions()
        {
            fractions = Model.FractionsFor(Orientation);
        }

/// <summary>Builds the API method table.</summary>
        private void BuildGridSurfaces()
        {
            BuildFaceFractions();

            int count = Model.CellCount;
            gridCells = new Vector3I[count];

            bool shared = IsSealedByDoorState || !Model.HasOpenState;

            if (count == 1)
            {
                gridCells[0] = Min;

/// <summary>OneCellSurfaces operation.</summary>
                gridStructuralSurfaces = OneCellSurfaces(true);
/// <summary>OneCellSurfaces operation.</summary>
                gridSurfaces = shared ? gridStructuralSurfaces : OneCellSurfaces(false);
                return;
            }

            gridStructuralSurfaces = new int[count];
            gridSurfaces = shared ? gridStructuralSurfaces : new int[count];

            BuildGridSurfacesWalkingTheCells();
        }

/// <summary>Builds the API method table.</summary>
        public void BuildGridSurfacesWalkingTheCells()
        {
            DetachInternedSurfaces();

/// <summary>ReferenceEquals operation.</summary>
            bool shared = ReferenceEquals(gridSurfaces, gridStructuralSurfaces);
            int i = 0;
            foreach (Vector3I local in Model.LocalCells())
            {
/// <summary>LocalToGrid operation.</summary>
                gridCells[i] = LocalToGrid(local);
/// <summary>RotateSurface operation.</summary>
                gridStructuralSurfaces[i] = RotateSurface(Model.LocalSurfaceState(local, true));
                if (!shared)
                {
                    gridSurfaces[i] = IsSealedByDoorState
                        ? gridStructuralSurfaces[i]
/// <summary>RotateSurface operation.</summary>
                        : RotateSurface(Model.LocalSurfaceState(local, false));
                }
                i++;
            }
        }

/// <summary>DetachInternedSurfaces operation.</summary>
        private void DetachInternedSurfaces()
        {
            if (Model.CellCount != 1) return;

/// <summary>ReferenceEquals operation.</summary>
            bool shared = ReferenceEquals(gridSurfaces, gridStructuralSurfaces);
            int[] structural = { gridStructuralSurfaces[0] };
            int[] live = shared ? structural : new[] { gridSurfaces[0] };

            gridStructuralSurfaces = structural;
            gridSurfaces = live;
        }

/// <summary>OneCellSurfaces operation.</summary>
        private int[] OneCellSurfaces(bool structural)
        {
            int[] known = Model.OneCellSurfaces(Orientation, structural);
            if (known != null) return known;

            return Model.StoreOneCellSurfaces(Orientation, structural,
                RotateSurface(Model.LocalSurfaceState(Vector3I.Zero, structural)));
        }

/// <summary>RotateSurface operation.</summary>
        private int RotateSurface(int localState)
        {
            if (localState == (CellSurface.SelfAirtightMask | CellSurface.SelfMountMask)
                || localState == 0)
            {
                return localState;
            }

            int rotated = 0;
            for (int face = 0; face < Face.Count; face++)
            {
                int gridFace = Orientation.RotateFace(face);
                if (gridFace < 0) continue;

                if (CellSurface.SelfAirtight(localState, face))
                {
                    rotated = CellSurface.WithSelfAirtight(rotated, gridFace, true);
                }
                if (CellSurface.SelfMount(localState, face))
                {
                    rotated = CellSurface.WithSelfMount(rotated, gridFace, true);
                }
            }
            return rotated;
        }

/// <summary>RefreshSurfaces operation.</summary>
        public void RefreshSurfaces()
        {
            BuildGridSurfaces();
        }

/// <summary>CoolantLinkPorts operation.</summary>
        public List<GridPort> CoolantLinkPorts()
        {
/// <summary>ToGridPorts operation.</summary>
            return ToGridPorts(Model.Coolant == null ? null : Model.Coolant.LinkPorts);
        }

/// <summary>CoolantSinkPorts operation.</summary>
        public List<GridPort> CoolantSinkPorts()
        {
/// <summary>ToGridPorts operation.</summary>
            return ToGridPorts(Model.Coolant == null ? null : Model.Coolant.SinkPorts);
        }

/// <summary>TryHeatPumpCells operation.</summary>
        public bool TryHeatPumpCells(out Vector3I coldCell, out Vector3I hotCell)
        {
            HeatPumpShape shape = Model.HeatPump;
            if (shape == null)
            {
                coldCell = Vector3I.Zero;
                hotCell = Vector3I.Zero;
                return false;
            }

/// <summary>LocalToGrid operation.</summary>
            coldCell = LocalToGrid(shape.ColdCell) + LocalDirectionToGrid(shape.ColdDirection);
/// <summary>LocalToGrid operation.</summary>
            hotCell = LocalToGrid(shape.HotCell) + LocalDirectionToGrid(shape.HotDirection);
            return true;
        }

/// <summary>ToGridPorts operation.</summary>
        private List<GridPort> ToGridPorts(CoolantPort[] ports)
        {
/// <summary>List operation.</summary>
            List<GridPort> result = new List<GridPort>();
            if (ports == null) return result;

            for (int i = 0; i < ports.Length; i++)
            {
                result.Add(new GridPort(
                    LocalToGrid(ports[i].LocalCell),
                    LocalDirectionToGrid(ports[i].LocalDirection)));
            }
            return result;
        }

/// <summary>ToString operation.</summary>
        public override string ToString()
        {
            return Model.Name + "@" + Min;
        }
    }

    public struct GridPort
    {
        public Vector3I Cell;
        public Vector3I Direction;

/// <summary>GridPort operation.</summary>
        public GridPort(Vector3I cell, Vector3I direction)
        {
            Cell = cell;
            Direction = direction;
        }

        public Vector3I Target
        {
            get { return Cell + Direction; }
        }
    }
}
