using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// The room suite's standard sealed pocket: a 6×6×6 light-armour shell around a 4×4×4 pocket
    /// of air, with one slide door in a wall at (2, 1, −1).
    ///
    /// Four test classes built this by hand, cell loop and door coordinate alike — the rig class
    /// the cleanup effort's iteration 6 is about: a shell with a misplaced door still builds a
    /// room, so a sealing test against the wrong fixture fails for the wrong reason and passes.
    /// The door cell is a named constant so a test that asserts *about the door* asserts about
    /// the cell the fixture actually placed it in.
    /// </summary>
    public static class RoomFixtures
    {
        /// <summary>Where the shell's one door sits, in grid cells.</summary>
        public static readonly Vector3I DoorCell = new Vector3I(2, 1, -1);

        /// <summary>A large-grid builder holding the shell; callers add what their test needs.</summary>
        public static GridBuilder DooredShell()
        {
            GridBuilder builder = GridBuilder.Large();
            AddDooredShell(builder);
            return builder;
        }

        /// <summary>The same shell, added to a builder a test has already started.</summary>
        public static void AddDooredShell(GridBuilder builder)
        {
            BlockModel armour = Catalog.LightArmor();
            BlockModel door = Catalog.SlideDoor();

            for (int x = -1; x <= 4; x++)
            for (int y = -1; y <= 4; y++)
            for (int z = -1; z <= 4; z++)
            {
                bool wall = x == -1 || x == 4 || y == -1 || y == 4 || z == -1 || z == 4;
                if (!wall) continue;
                Vector3I cell = new Vector3I(x, y, z);
                builder.Place(cell == DoorCell ? door : armour, cell, BlockOrientation.Identity);
            }
        }
    }
}
