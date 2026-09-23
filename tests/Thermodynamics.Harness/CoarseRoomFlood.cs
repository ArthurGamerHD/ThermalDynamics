using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    public class CoarseRoomFlood
    {
        public readonly int Edge;

        private Vector3I boxMin;
        private int sizeX, sizeY, sizeZ;
        private int superX, superY, superZ;

        private const byte Open = 0;
        private const byte Mixed = 1;

        private byte[] superState;

        private byte[][] tiles;

        private ulong[] visitedSuper;
        private ulong[][] visitedFine;

        private int[] regionSuper;

        private int[][] regionFine;


        private readonly HashSet<long> doorCells = new HashSet<long>();

        private struct Entry
        {
            public int Super;

            public int Fine;
        }

        private Entry[] frontier = new Entry[1024];
        private int frontierHead;
        private int frontierCount;


        public int ExternalCells;


        public readonly List<int> RegionCells = new List<int>();

        public int RoomCount
        {
            get { return RegionCells.Count - 1; }
        }


        public long SupercellsTaken;

        public long FineCellsVisited;

        public long BoundaryProbes;

        public long SealingBytes;


        public CoarseRoomFlood(int edge)
        {
            if (edge < 2)
            {
                throw new ArgumentException("a supercell of edge " + edge
                    + " is the fine walk wearing a costume; use 2 or more");
            }
            Edge = edge;
        }


        public void Run(GridModel grid, SurfaceMap surfaces)
        {
            if (grid == null || grid.BlockCount == 0)
            {
                throw new ArgumentException("an empty grid has no rooms to find");
            }

            boxMin = grid.Min - Vector3I.One;

            Vector3I boxMaxEx = grid.Max + new Vector3I(2, 2, 2);
            sizeX = boxMaxEx.X - boxMin.X;
            sizeY = boxMaxEx.Y - boxMin.Y;
            sizeZ = boxMaxEx.Z - boxMin.Z;

            superX = (sizeX + Edge - 1) / Edge;
            superY = (sizeY + Edge - 1) / Edge;
            superZ = (sizeZ + Edge - 1) / Edge;
            int superCount = superX * superY * superZ;

            superState = new byte[superCount];
            tiles = new byte[superCount][];
            visitedFine = new ulong[superCount][];
            regionFine = new int[superCount][];
            visitedSuper = new ulong[(superCount + 63) >> 6];
            regionSuper = new int[superCount];
            for (int i = 0; i < superCount; i++) regionSuper[i] = -1;

            ExternalCells = 0;
            RegionCells.Clear();
            SupercellsTaken = 0;
            FineCellsVisited = 0;
            BoundaryProbes = 0;
            SealingBytes = 0;
            frontierHead = 0;
            frontierCount = 0;

            ClassifyByBlockBounds(grid);
            FillSealingTiles(surfaces);
            CollectDoorCells(grid);

            RegionCells.Add(0);
            SeedCell(boxMin, 0);
            Flood();
            ExternalCells = RegionCells[0];

            InteriorScan();
        }


        public int RegionOf(Vector3I cell)
        {
            int x = cell.X - boxMin.X;
            int y = cell.Y - boxMin.Y;
            int z = cell.Z - boxMin.Z;
            if (x < 0 || x >= sizeX || y < 0 || y >= sizeY || z < 0 || z >= sizeZ) return -1;

            int sx = x / Edge, sy = y / Edge, sz = z / Edge;

            int super = SuperIndex(sx, sy, sz);
            if (superState[super] == Open) return regionSuper[super];

            int[] regions = regionFine[super];
            if (regions == null) return -1;

            int dimX, dimY, dimZ;
            TileDims(sx, sy, sz, out dimX, out dimY, out dimZ);
            int off = (x - sx * Edge) + dimX * ((y - sy * Edge) + dimY * (z - sz * Edge));
            return regions[off];
        }



        private void ClassifyByBlockBounds(GridModel grid)
        {
            IList<BlockInstance> blocks = grid.Blocks;
            for (int i = 0; i < blocks.Count; i++)
            {
                Vector3I min = blocks[i].Min - boxMin;
                Vector3I maxEx = blocks[i].MaxExclusive - boxMin;

                int sx0 = min.X / Edge, sx1 = (maxEx.X - 1) / Edge;
                int sy0 = min.Y / Edge, sy1 = (maxEx.Y - 1) / Edge;
                int sz0 = min.Z / Edge, sz1 = (maxEx.Z - 1) / Edge;

                for (int sz = sz0; sz <= sz1; sz++)
                {
                    for (int sy = sy0; sy <= sy1; sy++)
                    {
                        for (int sx = sx0; sx <= sx1; sx++)
                        {
                            superState[SuperIndex(sx, sy, sz)] = Mixed;
                        }
                    }
                }
            }
        }


        private void FillSealingTiles(SurfaceMap surfaces)
        {
            foreach (Vector3I cell in surfaces.Cells)
            {
                int x = cell.X - boxMin.X;
                int y = cell.Y - boxMin.Y;
                int z = cell.Z - boxMin.Z;
                if (x < 0 || x >= sizeX || y < 0 || y >= sizeY || z < 0 || z >= sizeZ) continue;

                int sx = x / Edge, sy = y / Edge, sz = z / Edge;

                int super = SuperIndex(sx, sy, sz);

                superState[super] = Mixed;

                byte[] tile = tiles[super];
                if (tile == null)
                {
                    int dimX, dimY, dimZ;
                    TileDims(sx, sy, sz, out dimX, out dimY, out dimZ);
                    tile = new byte[dimX * dimY * dimZ];
                    tiles[super] = tile;
                    SealingBytes += tile.Length;
                }

                int tx, ty, tz;
                TileDims(sx, sy, sz, out tx, out ty, out tz);
                int off = (x - sx * Edge) + tx * ((y - sy * Edge) + ty * (z - sz * Edge));
                tile[off] = (byte)(surfaces.GetStructuralState(cell) & CellSurface.SelfAirtightMask);
            }
        }


        private void CollectDoorCells(GridModel grid)
        {
            doorCells.Clear();
            IList<BlockInstance> doors = grid.StateDependentBlocks;
            for (int d = 0; d < doors.Count; d++)
            {
                Vector3I[] cells = doors[d].Cells;
                for (int c = 0; c < cells.Length; c++)
                {
                    doorCells.Add(GridMath.Key(cells[c]));
                }
            }
        }



        private void SeedCell(Vector3I cell, int region)
        {
            int x = cell.X - boxMin.X;
            int y = cell.Y - boxMin.Y;
            int z = cell.Z - boxMin.Z;
            int sx = x / Edge, sy = y / Edge, sz = z / Edge;

            int super = SuperIndex(sx, sy, sz);

            if (superState[super] == Open)
            {
                TakeSuper(super, region);
                return;
            }

            int dimX, dimY, dimZ;
            TileDims(sx, sy, sz, out dimX, out dimY, out dimZ);
            int off = (x - sx * Edge) + dimX * ((y - sy * Edge) + dimY * (z - sz * Edge));
            TakeFine(super, off, region);
        }


        private void Flood()
        {
            while (frontierCount > 0)
            {

                Entry entry = Dequeue();
                if (entry.Fine < 0) StepSuper(entry.Super);

                else StepFine(entry.Super, entry.Fine);
            }
        }


        private void StepSuper(int super)
        {
            SupercellsTaken++;
            int region = regionSuper[super];

            int sx, sy, sz;
            SuperCoords(super, out sx, out sy, out sz);
            int dimX, dimY, dimZ;
            TileDims(sx, sy, sz, out dimX, out dimY, out dimZ);
            RegionCells[region] += dimX * dimY * dimZ;

            for (int face = 0; face < Face.Count; face++)
            {
                Vector3I offset = Face.Offsets[face];
                int nx = sx + offset.X, ny = sy + offset.Y, nz = sz + offset.Z;
                if (nx < 0 || nx >= superX || ny < 0 || ny >= superY || nz < 0 || nz >= superZ) continue;


                int neighbour = SuperIndex(nx, ny, nz);
                if (superState[neighbour] == Open)
                {
                    if (!SuperVisited(neighbour)) TakeSuper(neighbour, region);
                    continue;
                }

                EnterMixedFace(neighbour, nx, ny, nz, face, region);
            }
        }


        private void EnterMixedFace(int neighbour, int nx, int ny, int nz, int face, int region)
        {
            int dimX, dimY, dimZ;
            TileDims(nx, ny, nz, out dimX, out dimY, out dimZ);
            byte[] tile = tiles[neighbour];

            Vector3I offset = Face.Offsets[face];
            int planeX0 = 0, planeX1 = dimX - 1;
            int planeY0 = 0, planeY1 = dimY - 1;
            int planeZ0 = 0, planeZ1 = dimZ - 1;

            if (offset.X > 0) planeX1 = 0;
            else if (offset.X < 0) planeX0 = dimX - 1;
            else if (offset.Y > 0) planeY1 = 0;
            else if (offset.Y < 0) planeY0 = dimY - 1;
            else if (offset.Z > 0) planeZ1 = 0;
            else planeZ0 = dimZ - 1;

            int blockedBit = 1 << Face.Opposite(face);

            for (int z = planeZ0; z <= planeZ1; z++)
            {
                for (int y = planeY0; y <= planeY1; y++)
                {
                    for (int x = planeX0; x <= planeX1; x++)
                    {
                        BoundaryProbes++;
                        int off = x + dimX * (y + dimY * z);
                        if (tile != null && (tile[off] & blockedBit) != 0) continue;
                        if (FineVisited(neighbour, off, dimX * dimY * dimZ)) continue;

                        TakeFine(neighbour, off, region);
                    }
                }
            }
        }


        private void StepFine(int super, int off)
        {
            FineCellsVisited++;
            int region = regionFine[super][off];
            RegionCells[region]++;

            int sx, sy, sz;
            SuperCoords(super, out sx, out sy, out sz);
            int dimX, dimY, dimZ;
            TileDims(sx, sy, sz, out dimX, out dimY, out dimZ);

            byte[] tile = tiles[super];
            int here = tile == null ? 0 : tile[off];

            int lx = off % dimX;
            int ly = (off / dimX) % dimY;
            int lz = off / (dimX * dimY);
            int x = sx * Edge + lx;
            int y = sy * Edge + ly;
            int z = sz * Edge + lz;

            for (int face = 0; face < Face.Count; face++)
            {
                if ((here & (1 << face)) != 0) continue;

                Vector3I offset = Face.Offsets[face];
                int gx = x + offset.X, gy = y + offset.Y, gz = z + offset.Z;
                if (gx < 0 || gx >= sizeX || gy < 0 || gy >= sizeY || gz < 0 || gz >= sizeZ) continue;

                int nsx = gx / Edge, nsy = gy / Edge, nsz = gz / Edge;

                int neighbourSuper = SuperIndex(nsx, nsy, nsz);

                if (superState[neighbourSuper] == Open)
                {
                    if (!SuperVisited(neighbourSuper)) TakeSuper(neighbourSuper, region);
                    continue;
                }

                int ndimX, ndimY, ndimZ;
                TileDims(nsx, nsy, nsz, out ndimX, out ndimY, out ndimZ);
                int noff = (gx - nsx * Edge) + ndimX * ((gy - nsy * Edge) + ndimY * (gz - nsz * Edge));

                byte[] neighbourTile = tiles[neighbourSuper];
                if (neighbourTile != null
                    && (neighbourTile[noff] & (1 << Face.Opposite(face))) != 0) continue;
                if (FineVisited(neighbourSuper, noff, ndimX * ndimY * ndimZ)) continue;

                TakeFine(neighbourSuper, noff, region);
            }
        }


        private void InteriorScan()
        {
            int superCount = superX * superY * superZ;
            for (int super = 0; super < superCount; super++)
            {
                if (superState[super] == Open)
                {
                    if (SuperVisited(super)) continue;

                    RegionCells.Add(0);
                    TakeSuper(super, RegionCells.Count - 1);
                    Flood();
                    continue;
                }

                int sx, sy, sz;
                SuperCoords(super, out sx, out sy, out sz);
                int dimX, dimY, dimZ;
                TileDims(sx, sy, sz, out dimX, out dimY, out dimZ);
                int volume = dimX * dimY * dimZ;
                byte[] tile = tiles[super];

                for (int off = 0; off < volume; off++)
                {
                    if (FineVisited(super, off, volume)) continue;

                    int state = tile == null ? 0 : tile[off];
                    if ((state & CellSurface.SelfAirtightMask) == CellSurface.SelfAirtightMask
                        && !IsDoorCell(super, off, sx, sy, sz, dimX, dimY))
                    {
                        MarkFine(super, off, volume);
                        continue;
                    }

                    RegionCells.Add(0);
                    TakeFine(super, off, RegionCells.Count - 1);
                    Flood();
                }
            }
        }


        private bool IsDoorCell(int super, int off, int sx, int sy, int sz, int dimX, int dimY)
        {
            if (doorCells.Count == 0) return false;

            int lx = off % dimX;
            int ly = (off / dimX) % dimY;
            int lz = off / (dimX * dimY);

            Vector3I cell = new Vector3I(
                boxMin.X + sx * Edge + lx,
                boxMin.Y + sy * Edge + ly,
                boxMin.Z + sz * Edge + lz);
            return doorCells.Contains(GridMath.Key(cell));
        }



        private void TakeSuper(int super, int region)
        {
            visitedSuper[super >> 6] |= 1ul << (super & 63);
            regionSuper[super] = region;
            Enqueue(new Entry { Super = super, Fine = -1 });
        }


        private void TakeFine(int super, int off, int region)
        {
            int sx, sy, sz;
            SuperCoords(super, out sx, out sy, out sz);
            int dimX, dimY, dimZ;
            TileDims(sx, sy, sz, out dimX, out dimY, out dimZ);
            int volume = dimX * dimY * dimZ;

            MarkFine(super, off, volume);
            int[] regions = regionFine[super];
            regions[off] = region;
            Enqueue(new Entry { Super = super, Fine = off });
        }


        private void MarkFine(int super, int off, int volume)
        {
            ulong[] bits = visitedFine[super];
            if (bits == null)
            {
                bits = new ulong[(volume + 63) >> 6];
                visitedFine[super] = bits;

                int[] regions = new int[volume];
                for (int i = 0; i < volume; i++) regions[i] = -1;
                regionFine[super] = regions;
            }
            bits[off >> 6] |= 1ul << (off & 63);
        }


        private bool SuperVisited(int super)
        {
            return (visitedSuper[super >> 6] & (1ul << (super & 63))) != 0;
        }


        private bool FineVisited(int super, int off, int volume)
        {
            ulong[] bits = visitedFine[super];
            if (bits == null)
            {
                MarkFine(super, off, volume);
                bits = visitedFine[super];
                bits[off >> 6] &= ~(1ul << (off & 63));
                return false;
            }
            return (bits[off >> 6] & (1ul << (off & 63))) != 0;
        }



        private int SuperIndex(int sx, int sy, int sz)
        {
            return (sz * superY + sy) * superX + sx;
        }


        private void SuperCoords(int super, out int sx, out int sy, out int sz)
        {
            sx = super % superX;
            sy = (super / superX) % superY;
            sz = super / (superX * superY);
        }


        private void TileDims(int sx, int sy, int sz, out int dimX, out int dimY, out int dimZ)
        {
            dimX = Math.Min(Edge, sizeX - sx * Edge);
            dimY = Math.Min(Edge, sizeY - sy * Edge);
            dimZ = Math.Min(Edge, sizeZ - sz * Edge);
        }



        private void Enqueue(Entry entry)
        {
            if (frontierCount == frontier.Length)
            {
                Entry[] grown = new Entry[frontier.Length * 2];
                for (int i = 0; i < frontierCount; i++)
                {
                    grown[i] = frontier[(frontierHead + i) % frontier.Length];
                }
                frontier = grown;
                frontierHead = 0;
            }

            frontier[(frontierHead + frontierCount) % frontier.Length] = entry;
            frontierCount++;
        }


        private Entry Dequeue()
        {
            Entry entry = frontier[frontierHead];
            frontierHead = (frontierHead + 1) % frontier.Length;
            frontierCount--;
            return entry;
        }
    }
}
