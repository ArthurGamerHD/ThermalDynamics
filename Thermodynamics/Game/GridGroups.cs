using System.Collections.Generic;
using VRage.Game.ModAPI;

namespace Thermodynamics
{
    /// <summary>
    /// Claiming a physical grid group once per tick, for the passes that apply one force per
    /// group rather than one per grid.
    ///
    /// A group is identified by the smallest entity id in it, so whichever of its grids a walk
    /// reaches first names the same group and the rest are skipped. The drag pass and the
    /// top-speed pass each carried their own copy of this walk; the id convention is one
    /// statement now, so a pass added later cannot pick a different one and handle every group
    /// once per member.
    /// </summary>
    public static class GridGroups
    {
        /// <summary>
        /// Resolves <paramref name="leader"/>'s physical group into <paramref name="grids"/> and
        /// claims it in <paramref name="handled"/>. False when there is no group, the group is
        /// empty, or another of its grids already claimed it this tick — the caller's cue to
        /// skip, since the group has been or will be handled whole by whoever claimed it.
        /// </summary>
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
