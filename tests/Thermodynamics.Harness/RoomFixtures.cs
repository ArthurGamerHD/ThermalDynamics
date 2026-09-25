using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    public static class RoomFixtures
    {

        public static readonly Vector3I DoorCell = new Vector3I(2, 1, -1);


        public static GridBuilder DooredShell()
        {
            GridBuilder builder = GridBuilder.Large();
            AddDooredShell(builder);
            return builder;
        }


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
