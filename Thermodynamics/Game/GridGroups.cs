using System.Collections.Generic;
using VRage.Game.ModAPI;

namespace Thermodynamics
{
    public static class GridGroups
    {
        public static bool TryClaim(IMyCubeGrid leader, List<IMyCubeGrid> grids, HashSet<long> handled)
        {
            IMyGridGroupData group = leader.GetGridGroup(GridLinkTypeEnum.Physical);
            if (group == null) return false;

            grids.Clear();
            group.GetGrids(grids);
            if (grids.Count == 0) return false;

            long identity = long.MaxValue;
            for (int i = 0; i < grids.Count; i++)
            {
                if (grids[i] != null && grids[i].EntityId < identity)
                {
                    identity = grids[i].EntityId;
                }
            }

            return handled.Add(identity);
        }
    }
}
