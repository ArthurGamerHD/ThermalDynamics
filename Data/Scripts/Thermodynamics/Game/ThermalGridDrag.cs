using System.Collections.Generic;
using Sandbox.ModAPI;
using Thermodynamics.Core;
using VRage.Game.ModAPI;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// Takes the momentum the air has been buying with heat, which is where this mod stops being
    /// read-only about motion.
    ///
    /// <para>
    /// The solver has always computed drag power — `FrictionScale × ρ × v_rel³ × A_windward` is
    /// `½ C_d ρ A v³` under another name — turned it into heat in the hull, and taken nothing from
    /// the ship. Over the 8,144 published blueprints at `reentry` the median hull is given 5.05 MW
    /// that way (thermal-model.md's change log).
    /// </para>
    ///
    /// <para>
    /// **The force is applied to the physical constraint group, not to the grid, and that is a
    /// requirement rather than a refinement.** This model's drag is the *windward projection*, and
    /// the depth along the wind never enters it — so a hull cut across the wind has each half
    /// taking the drag of the whole: measured at 172,800 W, 172,800 W and 172,800 W on a 4×4×4
    /// (`DragGroupingTests`). Summed per grid, a ship gets **exactly N times** draggier for being
    /// N grids, so it would get slower for growing a turret. `GetGridGroup(Physical)` is the unit
    /// the engine already keeps, and it is broader than this mod's `ThermalBridges`, which pair
    /// grids across rotors and pistons only — a connector links two grids physically and conducts
    /// no heat (thermal-model.md's change log).
    /// </para>
    ///
    /// <para>
    /// **Server only** (`C10`), and **off by default** (`C7`, `B38`): two mods that both slow a
    /// ship down is a collision this one answers with a switch rather than a detection.
    /// </para>
    /// </summary>
    public static class ThermalGridDrag
    {
        /// <summary>Scratch for a group's grids, so a tick allocates nothing.</summary>
        private static readonly List<IMyCubeGrid> GroupGrids = new List<IMyCubeGrid>();

        /// <summary>Groups already handled this tick, keyed as GridGroups.TryClaim keys them.</summary>
        private static readonly HashSet<long> Handled = new HashSet<long>();

        /// <summary>
        /// Applies one tick of drag to every live grid group.
        ///
        /// <para>
        /// Walked from the mod's own live list rather than from the engine's entities, because the
        /// figure being applied is one this mod computed and only a simulated grid has one. A group
        /// is handled once however many of its grids are live.
        /// </para>
        /// </summary>
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

                // **A grid making no drag never resolves its group, which is what makes a parked
                // fleet free** (the drag milestone). Resolving the group is the expensive half of this — a call
                // into the engine and a list of every grid joined to it — and a ship sitting in a
                // hangar, in vacuum, or below `FrictionAtSpeedsAbove` has nothing for it to sum.
                // A group with *any* moving grid in it is still reached, by that grid; this skips
                // groups where every member is still, not members of a moving group.
                if (thermals.Simulation.FrictionWatts <= 0f) continue;

                // A base standing in wind has friction watts now that the floor is zero, and no
                // use for a force; skipping it here is what keeps its group unresolved. A dynamic
                // grid docked to it walks its own entry and stops on the static member inside.
                if (thermals.Grid.IsStatic) continue;

                ApplyToGroupOf(thermals);
            }
        }

        /// <summary>
        /// Sums the group's drag and applies it once, at the group's own centre of mass.
        ///
        /// <para>
        /// **One force at one point, rather than one per grid at each grid's centre.** A force at
        /// each subgrid's own centre of mass produces a net torque on the assembly that no real air
        /// produces, and loads the joints with it — rotors detach (the drag milestone).
        /// </para>
        /// </summary>
        private static void ApplyToGroupOf(ThermalGrid leader)
        {
            if (!GridGroups.TryClaim(leader.Grid, GroupGrids, Handled)) return;

            // **A group with a static member is anchored and takes no force at all.** With the
            // friction floor at zero the term is live in any breeze, so every base standing in
            // wind reaches here every tick — and the test touches no thermal adapter, so bailing
            // here is what keeps those bases free.
            if (AeroGroupForces.Anchored(GroupGrids)) return;

            Vector3D centre;
            float mass;
            Vector3 drag, lift;
            AeroGroupForces.Sum(GroupGrids, out centre, out mass, out drag, out lift);

            // **Lift joins the same resultant and is applied at the same point.**
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
