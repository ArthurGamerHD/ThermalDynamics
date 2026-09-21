using System.Collections.Generic;
using Thermodynamics.Harness;

namespace Thermodynamics.Tests
{
    public class RemapLocalityLabTests
    {
        private static List<RemapLocalityLab.Row> rows;

/// <summary>Rows operation.</summary>
        private static List<RemapLocalityLab.Row> Rows()
        {
            if (rows == null) rows = RemapLocalityLab.Run(8000);
            return rows;
        }

        [Fact]
/// <summary>EveryMutationChangesSomethingAndVisitsTheBox operation.</summary>
        public void EveryMutationChangesSomethingAndVisitsTheBox()
        {
            foreach (RemapLocalityLab.Row row in Rows())
            {
                Assert.True(row.CellsChanged > 0, row.Mutation + " changed nothing; the lab's own guard should have thrown");
                Assert.True(row.CellsVisited > row.BoxCells / 2,
                    row.Mutation + " visited " + row.CellsVisited + " cells of a " + row.BoxCells
                    + "-cell box; a full reflood walks the box and then some, so the instrument is not measuring a full pass");
            }
        }

        [Fact]
/// <summary>ASkinChangeMovesAlmostNothingOfAFullPass operation.</summary>
        public void ASkinChangeMovesAlmostNothingOfAFullPass()
        {
/// <summary>Find operation.</summary>
            RemapLocalityLab.Row skin = Find("skin add");
            Assert.True(skin.CellsChanged * 100 < skin.CellsVisited,
                "adding one skin block changed " + skin.CellsChanged + " cells of " + skin.CellsVisited
                + " visited; if this ratio is real the change-local candidate answers itself");
        }

        [Fact]
/// <summary>RemovingARoomBoundaryMovesAtLeastTheRoom operation.</summary>
        public void RemovingARoomBoundaryMovesAtLeastTheRoom()
        {
/// <summary>Find operation.</summary>
            RemapLocalityLab.Row boundary = Find("room boundary remove");

            Assert.True(boundary.CellsChanged >= 2,
                "removing a room's boundary changed only " + boundary.CellsChanged + " cells");
            Assert.True(boundary.RoomsBefore > 0, "the fixture has no rooms, so the boundary mutation judged nothing");
        }

        [Fact]
/// <summary>RoomRenumberingAloneReadsAsNoChange operation.</summary>
        public void RoomRenumberingAloneReadsAsNoChange()
        {
/// <summary>Find operation.</summary>
            RemapLocalityLab.Row buried = Find("buried remove");
            Assert.True(buried.CellsChanged < 50,
                "removing one buried block read as " + buried.CellsChanged
                + " changed cells; the canonical room naming is letting renumbering read as change");
        }

/// <summary>Find operation.</summary>
        private static RemapLocalityLab.Row Find(string mutation)
        {
            foreach (RemapLocalityLab.Row row in Rows())
            {
                if (row.Mutation == mutation) return row;
            }
            throw new Xunit.Sdk.XunitException("no row for " + mutation);
        }
    }
}
