namespace Thermodynamics.Core
{
    /// <summary>
    /// A door in a wall: one face of one block that seals when the block is shut and does not
    /// when it is open, together with the two regions it joins.
    ///
    /// A portal is a property of how the ship is built, so it is found once, when the room map is
    /// built. Whether it is currently open is a property of the door, read live. That split is
    /// the whole point: a door cycling costs a walk over the portals, not a new flood fill of the
    /// grid's bounding box.
    /// </summary>
    public struct RoomPortal
    {
        /// <summary>The door.</summary>
        public BlockInstance Block;

        /// <summary>The grid-space face of <see cref="Block"/> that opens.</summary>
        public int Face;

        /// <summary>
        /// Region on the door's own side: a room index, or <see cref="RoomMap.ExternalRegion"/>.
        /// </summary>
        public int RegionA;

        /// <summary>Region on the far side of <see cref="Face"/>.</summary>
        public int RegionB;

        public RoomPortal(BlockInstance block, int face, int regionA, int regionB)
        {
            Block = block;
            Face = face;
            RegionA = regionA;
            RegionB = regionB;
        }

        /// <summary>True while the door is not sealing, so the two regions are one volume.</summary>
        public bool IsOpen
        {
            get { return Block != null && !Block.IsSealedByDoorState; }
        }

        /// <summary>A portal between two different regions is the only kind that can vent one.</summary>
        public bool JoinsDistinctRegions
        {
            get { return RegionA != RegionB; }
        }
    }
}
