using System.Collections.Generic;
using Sandbox.ModAPI;
using Thermodynamics.Core;
using VRage.Game.ModAPI;
using VRageMath;

namespace Thermodynamics
{
    public static class ThermalGridDrag
    {
/// <summary>List operation.</summary>
        private static readonly List<IMyCubeGrid> GroupGrids = new List<IMyCubeGrid>();

/// <summary>HashSet operation.</summary>
        private static readonly HashSet<long> Handled = new HashSet<long>();

/// <summary>Tick operation.</summary>
        public static void Tick()
        {
            if (!Settings.Instance.EnableDrag) return;
            if (MyAPIGateway.Session == null || !MyAPIGateway.Session.IsServer) return;

            IList<ThermalGrid> grids = ThermalGrid.LiveGrids;
            if (grids.Count == 0) return;

            Handled.Clear();

            for (int i = 0; i < grids.Count; i++)
            {
                ThermalGrid thermals = grids[i];
                if (thermals == null || thermals.Grid == null || thermals.Simulation == null) continue;

                if (thermals.Simulation.FrictionWatts <= 0f) continue;

                if (thermals.Grid.IsStatic) continue;

                ApplyToGroupOf(thermals);
            }
        }

/// <summary>Applies the togroupof.</summary>
        private static void ApplyToGroupOf(ThermalGrid leader)
        {
            if (!GridGroups.TryClaim(leader.Grid, GroupGrids, Handled)) return;

            if (AeroGroupForces.Anchored(GroupGrids)) return;

            Vector3D centre;
            float mass;
            Vector3 drag, lift;
            AeroGroupForces.Sum(GroupGrids, out centre, out mass, out drag, out lift);

            Vector3 newtons = drag + lift;
            if (mass <= 0f || newtons.LengthSquared() <= 0f) return;

            IMyCubeGrid applyTo = null;
            for (int i = 0; i < GroupGrids.Count; i++)
            {
                if (GroupGrids[i] != null && GroupGrids[i].Physics != null)
                {
                    applyTo = GroupGrids[i];
                    break;
                }
            }

            if (applyTo == null) return;

            applyTo.Physics.AddForce(
                VRage.Game.Components.MyPhysicsForceType.APPLY_WORLD_FORCE,
                newtons, centre / mass, null);
        }
    }
}
