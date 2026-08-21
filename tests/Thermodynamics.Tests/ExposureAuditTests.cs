using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// Which block faces the model believes are open to the sky, and why.
    ///
    /// Exposure decides radiation, convection and solar gain, so a face wrongly called buried is a
    /// block that never sheds or takes heat — and from the outside every rejection looks the same.
    /// These pin all three rejection rules separately, on the cases that actually occur: a face
    /// partly covered by a smaller block, a face against a block that is not airtight, a face onto
    /// a room that is sealed and the same face once it is vented, and the interior faces of a
    /// multi-cell block.
    ///
    /// The audit is checked against <see cref="SurfaceMap.GetExposedFaces"/> everywhere, because
    /// its whole value is being trusted when the two could disagree.
    /// </summary>
    public class ExposureAuditTests
    {
        private static RoomMapper MapperFor(GridModel grid, out SurfaceMap surfaces)
        {
            surfaces = new SurfaceMap();
            surfaces.Rebuild(grid);

            RoomMapper mapper = new RoomMapper(surfaces);
            mapper.RequestRestart(grid);
            mapper.RunToCompletion();
            return mapper;
        }

        private static FaceExposure[] Explain(
            SurfaceMap surfaces, BlockInstance block, RoomMap rooms)
        {
            FaceExposure[] faces = new FaceExposure[Face.Count];
            SurfaceAudit.Explain(surfaces, block, rooms, faces);

            // The audit exists to explain the model, so it has to agree with it exactly. Any test
            // below that passes while these differ would be describing fiction.
            int[] counted = surfaces.GetExposedFaces(block, rooms);
            for (int face = 0; face < Face.Count; face++)
            {
                Assert.Equal(counted[face], faces[face].Exposed);
                Assert.Equal(
                    faces[face].Cells,
                    faces[face].Exposed + faces[face].Sealed + faces[face].Interior);

                // Mounted is a subset of Exposed rather than a rejection of its own.
                Assert.True(faces[face].Mounted <= faces[face].Exposed);
            }

            return faces;
        }

        // ---- the three rejection rules, separately -----------------------------------------

        [Fact]
        public void ALoneBlockHasEveryCellFaceExposedAndNothingRejected()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);

            SurfaceMap surfaces;
            RoomMapper mapper = MapperFor(builder.Grid, out surfaces);

            FaceExposure[] faces = Explain(surfaces, builder.Last, mapper.Map);

            for (int face = 0; face < Face.Count; face++)
            {
                Assert.Equal(1, faces[face].Cells);
                Assert.Equal(1, faces[face].Exposed);
                Assert.Equal(0, faces[face].Sealed);
                Assert.Equal(0, faces[face].Mounted);
                Assert.Equal(0, faces[face].Interior);
            }
        }

        [Fact]
        public void AFaceAgainstAnAirtightNeighbourIsRejectedAsSealed()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);
            BlockInstance subject = builder.Last;
            builder.Place(Catalog.LightArmor(), new Vector3I(1, 0, 0));

            SurfaceMap surfaces;
            RoomMapper mapper = MapperFor(builder.Grid, out surfaces);

            FaceExposure[] faces = Explain(surfaces, subject, mapper.Map);
            int right = Face.IndexOf(new Vector3I(1, 0, 0));

            Assert.Equal(0, faces[right].Exposed);
            Assert.Equal(1, faces[right].Sealed);
            Assert.Equal(0, faces[right].Interior);
        }

        [Fact]
        public void AFaceOntoASealedRoomIsRejectedAsInteriorNotAsSealed()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), new Vector3I(-1, -1, -1), new Vector3I(2, 2, 2));

            // The shell block directly above the cavity: its underside looks into the room.
            BlockInstance lid = builder.Grid.GetAtCell(new Vector3I(0, 1, 0));

            SurfaceMap surfaces;
            RoomMapper mapper = MapperFor(builder.Grid, out surfaces);
            Assert.Equal(1, mapper.Map.RoomCount);

            FaceExposure[] faces = Explain(surfaces, lid, mapper.Map);
            int down = Face.IndexOf(new Vector3I(0, -1, 0));
            int up = Face.IndexOf(new Vector3I(0, 1, 0));

            // Nothing is pressed against the underside — the room is empty space. It is rejected
            // for being indoors, which is the distinction the audit exists to make.
            Assert.Equal(0, faces[down].Exposed);
            Assert.Equal(1, faces[down].Interior);
            Assert.Equal(0, faces[down].Sealed);

            // The outside of the same block is still open to the sky.
            Assert.Equal(1, faces[up].Exposed);
        }

        [Fact]
        public void VentingARoomMakesTheFacesLookingIntoItExposedAgain()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), new Vector3I(-1, -1, -1), new Vector3I(2, 2, 2));
            BlockInstance lid = builder.Grid.GetAtCell(new Vector3I(0, 1, 0));

            SurfaceMap surfaces;
            RoomMapper mapper = MapperFor(builder.Grid, out surfaces);

            int down = Face.IndexOf(new Vector3I(0, -1, 0));
            Assert.Equal(1, Explain(surfaces, lid, mapper.Map)[down].Interior);

            // Punch the shell open. The room is gone, so the cavity is outdoors and the same face
            // radiates: the model must not keep treating it as an interior surface.
            builder.Grid.Remove(builder.Grid.GetAtCell(new Vector3I(1, 0, 0)));
            surfaces.Rebuild(builder.Grid);
            mapper.RequestRestart(builder.Grid);
            mapper.RunToCompletion();

            FaceExposure[] after = Explain(surfaces, lid, mapper.Map);

            Assert.Equal(1, after[down].Exposed);
            Assert.Equal(0, after[down].Interior);
        }

        // ---- partial coverage, which is the case worth being careful about ------------------

        [Fact]
        public void ASmallBlockCoversOnlyThePartOfALargeFaceItTouches()
        {
            GridBuilder builder = GridBuilder.Large();

            // A 3x1x1 block, and one 1x1x1 block against the middle of its long side.
            builder.Place(Catalog.LightArmorBar(3), Vector3I.Zero);
            BlockInstance bar = builder.Last;
            builder.Place(Catalog.LightArmor(), new Vector3I(1, 1, 0));

            SurfaceMap surfaces;
            RoomMapper mapper = MapperFor(builder.Grid, out surfaces);

            FaceExposure[] faces = Explain(surfaces, bar, mapper.Map);
            int up = Face.IndexOf(new Vector3I(0, 1, 0));

            // Three cell faces along the top, one of them covered. A face is not all-or-nothing.
            Assert.Equal(3, faces[up].Cells);
            Assert.Equal(2, faces[up].Exposed);
            Assert.Equal(1, faces[up].Sealed);
        }

        [Fact]
        public void PartialCoverageScalesExposedAreaRatherThanRemovingTheFace()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmorBar(3), Vector3I.Zero);
            BlockInstance bar = builder.Last;

            SurfaceMap surfaces;
            RoomMapper open = MapperFor(builder.Grid, out surfaces);
            int bare = Total(surfaces.GetExposedFaces(bar, open.Map));

            builder.Place(Catalog.LightArmor(), new Vector3I(1, 1, 0));
            surfaces.Rebuild(builder.Grid);
            open.RequestRestart(builder.Grid);
            open.RunToCompletion();

            int covered = Total(surfaces.GetExposedFaces(bar, open.Map));

            Assert.Equal(bare - 1, covered);
        }

        [Fact]
        public void InteriorCellFacesOfAMultiCellBlockAreNeverWalked()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmorCube(3), Vector3I.Zero);

            SurfaceMap surfaces;
            RoomMapper mapper = MapperFor(builder.Grid, out surfaces);

            FaceExposure[] faces = Explain(surfaces, builder.Last, mapper.Map);

            // 3x3 cell faces per side of a 3x3x3 block, and not one more: the cells inside the
            // block cannot radiate and are not visited at all.
            for (int face = 0; face < Face.Count; face++)
            {
                Assert.Equal(9, faces[face].Cells);
                Assert.Equal(9, faces[face].Exposed);
            }
        }

        [Fact]
        public void ABlockBuriedInAHullHasNoExposedFaceAndEveryRejectionIsAccountedFor()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), new Vector3I(-1, -1, -1), new Vector3I(2, 2, 2));
            builder.Place(Catalog.Reactor(), Vector3I.Zero);

            SurfaceMap surfaces;
            RoomMapper mapper = MapperFor(builder.Grid, out surfaces);

            FaceExposure[] faces = Explain(surfaces, builder.Last, mapper.Map);

            for (int face = 0; face < Face.Count; face++)
            {
                Assert.Equal(0, faces[face].Exposed);
                Assert.Equal(1, faces[face].Sealed + faces[face].Interior);
            }
        }

        // ---- the mount rule, which is the one that surprises people -------------------------

        [Fact]
        public void AFaceAgainstAnOpenLatticeRadiatesAndIsCountedAsBolted()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);
            BlockInstance armour = builder.Last;

            // A grating seals nothing — the room mapper will happily flood air straight through it.
            builder.Place(Catalog.Grating(), new Vector3I(1, 0, 0));

            SurfaceMap surfaces;
            RoomMapper mapper = MapperFor(builder.Grid, out surfaces);

            FaceExposure[] faces = Explain(surfaces, armour, mapper.Map);
            int right = Face.IndexOf(new Vector3I(1, 0, 0));

            // Nothing airtight is in the way and the cell beyond is outdoors, so the face sees the
            // sky. The two blocks are bolted together as well, which conducts, but a bolt through
            // an open lattice does not stop a hull panel radiating.
            Assert.Equal(1, faces[right].Exposed);
            Assert.Equal(0, faces[right].Sealed);
            Assert.Equal(0, faces[right].Interior);

            // The joint is still reported, as a subset of the exposure rather than instead of it.
            Assert.Equal(1, faces[right].Mounted);

            // The two halves of the model now agree: the room mapper calls the cell beyond the
            // face outdoors, and exposure counts the face.
            Assert.True(mapper.Map.IsExternal(new Vector3I(1, 0, 0)));
            Assert.False(mapper.Map.IsSolid(new Vector3I(1, 0, 0)));
        }

        [Fact]
        public void ASealingNeighbourStillBuriesTheFaceWhateverItsMounts()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);
            BlockInstance armour = builder.Last;

            // Armour mounts and seals. The sealing test rejects the face before mounting is even
            // considered, which is why deleting the mount rule cannot open up a solid hull.
            builder.Place(Catalog.LightArmor(), new Vector3I(1, 0, 0));

            SurfaceMap surfaces;
            RoomMapper mapper = MapperFor(builder.Grid, out surfaces);

            FaceExposure[] faces = Explain(surfaces, armour, mapper.Map);
            int right = Face.IndexOf(new Vector3I(1, 0, 0));

            Assert.Equal(0, faces[right].Exposed);
            Assert.Equal(1, faces[right].Sealed);
            Assert.Equal(0, faces[right].Mounted);
        }

        [Fact]
        public void TheBoltedCountNeedsBothSidesToMount()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);
            BlockInstance armour = builder.Last;

            SurfaceMap surfaces;
            RoomMapper mapper = MapperFor(builder.Grid, out surfaces);

            // With nothing on the other side there is no neighbour mount, so the face is exposed
            // and carries no joint: the count is about a joint, not about this block's own mount
            // points.
            FaceExposure[] faces = Explain(surfaces, armour, mapper.Map);
            int right = Face.IndexOf(new Vector3I(1, 0, 0));

            Assert.Equal(1, faces[right].Exposed);
            Assert.Equal(0, faces[right].Mounted);
        }

        // ---- the whole-grid walk the telemetry dump uses ------------------------------------

        [Fact]
        public void ExplainAllCoversEveryBlockAndMatchesThePerBlockWalk()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(3, 2, 2));

            SurfaceMap surfaces;
            RoomMapper mapper = MapperFor(builder.Grid, out surfaces);

            var all = SurfaceAudit.ExplainAll(surfaces, builder.Grid, mapper.Map);

            Assert.Equal(builder.Grid.BlockCount, all.Count);

            for (int i = 0; i < all.Count; i++)
            {
                Assert.Equal(
                    Total(surfaces.GetExposedFaces(all[i].Block, mapper.Map)),
                    all[i].TotalExposed);
            }
        }

        [Fact]
        public void ExplainAllStopsAtItsLimit()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(4, 4, 1));

            SurfaceMap surfaces;
            RoomMapper mapper = MapperFor(builder.Grid, out surfaces);

            Assert.Equal(5, SurfaceAudit.ExplainAll(surfaces, builder.Grid, mapper.Map, 5).Count);
        }

        [Fact]
        public void AuditingWithNoRoomMapTreatsEverythingOutsideTheBlockAsOutdoors()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), new Vector3I(-1, -1, -1), new Vector3I(2, 2, 2));
            BlockInstance lid = builder.Grid.GetAtCell(new Vector3I(0, 1, 0));

            SurfaceMap surfaces = new SurfaceMap();
            surfaces.Rebuild(builder.Grid);

            FaceExposure[] faces = Explain(surfaces, lid, null);
            int down = Face.IndexOf(new Vector3I(0, -1, 0));

            // No room map means no way to know a cavity is indoors, and the two walks have to be
            // wrong together rather than apart — which is what Explain() asserts.
            Assert.Equal(1, faces[down].Exposed);
        }

        private static int Total(int[] faces)
        {
            int total = 0;
            for (int i = 0; i < faces.Length; i++) total += faces[i];
            return total;
        }
    }
}
