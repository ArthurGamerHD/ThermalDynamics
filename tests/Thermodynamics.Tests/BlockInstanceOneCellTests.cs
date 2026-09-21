using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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

        /// <summary>
        /// A block holds its two surface layers in one array whenever they cannot differ — every
        /// block that is not a door, and a door while it is shut — and in two once a door stands
        /// open, because opening it must leave the structural layer where it was.
        ///
        /// <para>
        /// **A refresh no longer hands back a fresh array for a one-cell block, and that is the
        /// interning of Pass 9, Iteration 9.** Those arrays are shared per model, orientation and
        /// layer, so a refresh that changes nothing hands back the same object. What
        /// `ThermalSimulation.RefreshBlock` actually needs is unaffected — `SameSurfaces` compares
        /// **by value**, and an array compared against itself reports *unchanged*, which is the
        /// right answer when nothing changed. A door cycling still swaps *which* interned array the
        /// live layer points at, so a real change is still a different object with different
        /// values. Both halves are asserted below rather than left to that argument.
        /// </para>
        /// </summary>
        [Fact]
        public void TheTwoSurfaceLayersShareOneArrayExactlyWhenTheyCannotDiffer()
        {
            BlockInstance armour = new BlockInstance(Catalog.LightArmor(), Vector3I.Zero, BlockOrientation.Identity);
            Assert.Same(armour.StructuralSurfaces, armour.SelfSurfaces);

            BlockInstance door = new BlockInstance(Catalog.SlideDoor(), Vector3I.Zero, BlockOrientation.Identity);
            Assert.True(door.HasStateDependentSealing);
            Assert.True(door.IsSealedByDoorState);
            Assert.Same(door.StructuralSurfaces, door.SelfSurfaces);

            int[] structuralBefore = door.StructuralSurfaces;
            int[] liveBefore = door.SelfSurfaces;
            door.IsSealedByDoorState = false;
            door.RefreshSurfaces();

            // Opening it leaves the structural layer exactly where it was — the same interned
            // array, since a door is one cell — and moves the live layer to the *other* interned
            // array, which is a different object holding different bits. That is what
            // `RefreshBlock` sees, and it is what it needs to see.
            Assert.Same(structuralBefore, door.StructuralSurfaces);
            Assert.NotSame(liveBefore, door.SelfSurfaces);
            Assert.NotSame(door.StructuralSurfaces, door.SelfSurfaces);
            Assert.Equal(structuralBefore, door.StructuralSurfaces);
            Assert.NotEqual(liveBefore, door.SelfSurfaces);

            // And shutting it again puts the live layer back on the structural array, which is the
            // round trip a door actually makes.
            door.IsSealedByDoorState = true;
            door.RefreshSurfaces();
            Assert.Same(door.StructuralSurfaces, door.SelfSurfaces);
            Assert.Equal(liveBefore, door.SelfSurfaces);

            // A one-cell block's refresh hands back the interned array, and the values are what a
            // caller compares. A multi-cell block still gets a fresh one, because only the one-cell
            // path is interned.
            int[] armourBefore = armour.SelfSurfaces;
            armour.RefreshSurfaces();
            Assert.Equal(armourBefore, armour.SelfSurfaces);
            Assert.Same(armour.StructuralSurfaces, armour.SelfSurfaces);

            BlockInstance multi = new BlockInstance(Catalog.LightArmorCube(2), Vector3I.Zero, BlockOrientation.Identity);
            Assert.True(multi.Model.CellCount > 1,
                "the multi-cell case needs a block with more than one cell to say anything");
            int[] multiBefore = multi.StructuralSurfaces;
            multi.RefreshSurfaces();
            Assert.NotSame(multiBefore, multi.StructuralSurfaces);
            Assert.Equal(multiBefore, multi.StructuralSurfaces);
        }

        /// <summary>
        /// **Nothing writes through a block's surface arrays, anywhere in the tree.**
        ///
        /// <para>
        /// One-cell blocks share their surface arrays per model and orientation, so a single
        /// `block.SelfSurfaces[i] = …` would change every other block of that model and orientation
        /// on every grid in the session. Nothing does that today; the reason to assert it is that
        /// the code that would is ordinary-looking and its failure is invisible — a neighbouring
        /// block's walls quietly change, and every downstream answer stays self-consistent.
        /// </para>
        ///
        /// <para>
        /// The arrays are read through public properties, so the check is over the source rather
        /// than over an instance: an element assignment whose target is one of the three
        /// properties, in any file under `Data/` or `tests/`. The builder inside `BlockInstance`
        /// writes the private fields, which is where writing belongs and is not what this reads.
        /// </para>
        /// </summary>
        [Fact]
        public void NothingWritesThroughABlocksSurfaceArrays()
        {
            string root = ShippedBlocks.RepoRoot();
            string[] properties = { "SelfSurfaces", "StructuralSurfaces", "Cells" };
            List<string> writes = new List<string>();

            foreach (string folder in new[] { "Thermodynamics/Content/Data/", "tests" })
            {
                foreach (string path in Directory.GetFiles(Path.Combine(root, folder), "*.cs",
                    SearchOption.AllDirectories))
                {
                    string relative = path.Substring(root.Length).TrimStart('/', '\\').Replace('\\', '/');
                    if (relative.Contains("/obj/") || relative.Contains("/bin/")) continue;

                    SyntaxNode tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();

                    foreach (AssignmentExpressionSyntax assignment in tree.DescendantNodes()
                        .OfType<AssignmentExpressionSyntax>())
                    {
                        ElementAccessExpressionSyntax element =
                            assignment.Left as ElementAccessExpressionSyntax;
                        if (element == null) continue;

                        MemberAccessExpressionSyntax member =
                            element.Expression as MemberAccessExpressionSyntax;
                        if (member == null) continue;
                        if (Array.IndexOf(properties, member.Name.Identifier.Text) < 0) continue;

                        writes.Add(relative + ":"
                            + (assignment.GetLocation().GetLineSpan().StartLinePosition.Line + 1));
                    }
                }
            }

            Assert.True(writes.Count == 0,
                "these write through a block's surface or cell array, which one-cell blocks share"
                + " per model and orientation — the write would land on every other block of that"
                + " model and orientation in the session: "
                + string.Join(", ", writes.ToArray()));
        }

        /// <summary>
        /// **Two one-cell blocks of one model and orientation share one surface array, and two of
        /// different orientations do not.**
        ///
        /// <para>
        /// The sharing is the saving — a census hull is mostly one-cell blocks, each of which was
        /// allocating an `int[1]` for an answer that depends only on the model and the orientation.
        /// It is asserted in both directions because a cache keyed too coarsely gives every
        /// orientation the same bits, which is a wrong grid rather than a slow one, and one keyed
        /// too finely quietly saves nothing (`E8`).
        /// </para>
        /// </summary>
        [Fact]
        public void OneCellBlocksOfAModelAndOrientationShareOneSurfaceArray()
        {
            BlockModel model = Catalog.LightArmor();

            BlockInstance here = new BlockInstance(model, Vector3I.Zero, BlockOrientation.Identity);
            BlockInstance there = new BlockInstance(model, new Vector3I(9, 4, 7), BlockOrientation.Identity);

            Assert.Same(here.StructuralSurfaces, there.StructuralSurfaces);
            Assert.Equal(Vector3I.Zero, here.Cells[0]);
            Assert.Equal(new Vector3I(9, 4, 7), there.Cells[0]);
            Assert.NotSame(here.Cells, there.Cells);

            // Every orientation gets its own array, and no two orientations that rotate the bits
            // differently are handed each other's.
            List<int[]> seen = new List<int[]>();
            int distinct = 0;
            foreach (BlockOrientation orientation in PipeFitter.AllOrientations())
            {
                BlockInstance block = new BlockInstance(model, Vector3I.Zero, orientation);
                BlockInstance twin = new BlockInstance(model, new Vector3I(3, 3, 3), orientation);

                Assert.Same(block.StructuralSurfaces, twin.StructuralSurfaces);

                bool known = false;
                for (int i = 0; i < seen.Count; i++)
                {
                    if (ReferenceEquals(seen[i], block.StructuralSurfaces)) known = true;
                }
                if (!known)
                {
                    seen.Add(block.StructuralSurfaces);
                    distinct++;
                }
            }

            Assert.True(distinct > 1,
                "every orientation was handed the same array, so the cache is keyed on nothing");
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

                        // **The oracle must not have written through the cache the fast path
                        // reads.** A one-cell block's surface arrays are interned per model and
                        // orientation, so without the detach in
                        // `BuildGridSurfacesWalkingTheCells` the walk would write its answer into
                        // the array `fast` is holding — and the comparison below would be of that
                        // array against itself, which passes for any two values whatsoever. This is
                        // the assertion that the two sides are two sides.
                        Assert.NotSame(fast.StructuralSurfaces, walked.StructuralSurfaces);
                        Assert.NotSame(fast.SelfSurfaces, walked.SelfSurfaces);

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
