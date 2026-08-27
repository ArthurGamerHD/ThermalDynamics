using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// A one-cell `BlockInstance` is built without walking its cells, and is held to the walk over
    /// every one of the twenty-four orientations, for a solid block, a partly mounted one, a door
    /// shut and a door open — the same cell, the same live bits, the same structural bits (`D8`).
    /// The uniform-bits short cut in the surface rotation is covered by the same comparison, since
    /// the solid block takes it and the partly mounted one cannot.
    /// </summary>
    public class BlockInstanceOneCellTests
    {
        private static IEnumerable<BlockModel> Models()
        {
            yield return Catalog.LightArmor();
            BlockModel partly = BlockModel.Solid("partly mounted", Vector3I.One, 100f, Catalog.LightArmor().Thermal);
            int state = CellSurface.SelfAirtightMask;
            state = CellSurface.WithSelfMount(state, 0, true);
            state = CellSurface.WithSelfMount(state, 2, true);
            partly.SetLocalSurface(Vector3I.Zero, state);
            yield return partly;
            yield return Catalog.SlideDoor();
        }

        [Fact]
        public void AOneCellBlockIsBuiltAsTheCellWalkWouldBuildIt()
        {
            int compared = 0;
            int differingBits = 0;
            foreach (BlockModel model in Models())
            {
                foreach (BlockOrientation orientation in PipeFitter.AllOrientations())
                {
                    foreach (bool shut in new[] { true, false })
                    {
                        Vector3I at = new Vector3I(-4, 7, 2);
                        BlockInstance fast = new BlockInstance(model, at, orientation);
                        fast.IsSealedByDoorState = shut;
                        fast.RefreshSurfaces();

                        BlockInstance walked = new BlockInstance(model, at, orientation);
                        walked.IsSealedByDoorState = shut;
                        walked.RefreshSurfaces();
                        walked.BuildGridSurfacesWalkingTheCells();

                        Assert.Equal(1, fast.CellCount);
                        Assert.Equal(walked.Cells[0], fast.Cells[0]);
                        Assert.Equal(at, fast.Cells[0]);
                        Assert.True(walked.SelfSurfaces[0] == fast.SelfSurfaces[0],
                            model.Name + " " + orientation + (shut ? " shut" : " open") + ": live bits "
                            + fast.SelfSurfaces[0] + " by the short path and " + walked.SelfSurfaces[0] + " by the walk");
                        Assert.Equal(walked.StructuralSurfaces[0], fast.StructuralSurfaces[0]);
                        if (fast.SelfSurfaces[0] != fast.StructuralSurfaces[0]) differingBits++;
                        compared++;
                    }
                }
            }

            Assert.Equal(3 * 24 * 2, compared);
            Assert.True(differingBits > 0, "no case had a door open, so the live half was never exercised");
        }
    }
}
