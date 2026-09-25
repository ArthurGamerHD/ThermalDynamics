namespace Thermodynamics.Core
{
    public struct RoomPortal
    {
        public BlockInstance Block;

        public int Face;

        public int RegionA;

        public int RegionB;


        public RoomPortal(BlockInstance block, int face, int regionA, int regionB)
        {
            Block = block;
            Face = face;
            RegionA = regionA;
            RegionB = regionB;
        }

        public bool IsOpen
        {
            get { return Block != null && !Block.IsSealedByDoorState; }
        }

        public bool JoinsDistinctRegions
        {
            get { return RegionA != RegionB; }
        }
    }
}
