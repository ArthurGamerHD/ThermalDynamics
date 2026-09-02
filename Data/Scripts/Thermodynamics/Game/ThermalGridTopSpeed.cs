using Sandbox.Definitions;
using Sandbox.ModAPI;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// A ship's top speed as a function of its mass, absorbed from
    /// [RelativeTopSpeed](https://github.com/Gauge/RelativeTopSpeed) (`K10`).
    ///
    /// <para>
    /// **The engine's cap is one number for every ship in the world, and that is the thing this
    /// replaces.** The cap is raised to <see cref="Settings.SpeedLimit"/> so the game allows more
    /// than 100 m/s, and each grid is then held under a cruise speed of its own — high for a fighter,
    /// low for a freighter — by a force rather than by a limit, so a ship pushed past it is dragged
    /// back rather than stopped dead.
    /// </para>
    ///
    /// <para>
    /// **Ported rather than reinvented.** The force is the original's,
    /// `resistance × mass × (1 − cruise / speed)` along the velocity, capped by the boost ceiling,
    /// and the curve is <see cref="Core.CruiseCurve"/>. What this mod adds is not in here: the air.
    /// The drag the thermal model computes is a separate force behind `EnableDrag`, and a world may
    /// run either, both or neither.
    /// </para>
    ///
    /// <para>
    /// **Server-side, and off by default.** Writing to `Physics` is authority over the game's own
    /// movement, which this mod holds only where a player asks for it — the same argument, and the
    /// same default, as the drag switch beside it.
    /// </para>
    /// </summary>
    public static class ThermalGridTopSpeed
    {
        /// <summary>Whether the engine's cap currently carries this mod's figure.</summary>
        private static bool capApplied;

        /// <summary>What the world's definition held before this mod raised it, m/s.</summary>
        private static float shippedLargeCap;
        private static float shippedSmallCap;

        /// <summary>The cap this mod last wrote, so a changed setting is noticed.</summary>
        private static float appliedCap;

        /// <summary>Scratch for a group's grids, so a tick allocates nothing.</summary>
        private static readonly List<IMyCubeGrid> GroupGrids = new List<IMyCubeGrid>();

        /// <summary>Groups already handled this tick, keyed by the smallest entity id in them.</summary>
        private static readonly HashSet<long> Handled = new HashSet<long>();

        /// <summary>
        /// One tick: the world's cap in step with the setting, and one resistance force per physical
        /// group over its cruise speed.
        /// </summary>
        public static void Tick()
        {
            if (MyAPIGateway.Session == null || !MyAPIGateway.Session.IsServer) return;

            Settings settings = Settings.Instance;
            if (settings == null) return;

            ApplyCap(settings);

            if (!settings.EnableTopSpeed) return;

            IList<ThermalGrid> grids = ThermalGrid.LiveGrids;
            if (grids.Count == 0) return;

            Handled.Clear();

            for (int i = 0; i < grids.Count; i++)
            {
                ThermalGrid thermals = grids[i];
                if (thermals == null || thermals.Grid == null) continue;

                // A grid that is not moving has no cruise speed to be over, and resolving its group
                // is the expensive half of this.
                IMyCubeGrid grid = thermals.Grid;
                if (grid.Physics == null || grid.Physics.Speed <= 0f) continue;

                ApplyToGroupOf(grid, settings);
            }
        }

        /// <summary>
        /// The cruise speed this world gives a group of this mass and size, m/s. Read here rather
        /// than on the settings object so that the twelve dials the curve is drawn from have a
        /// reader outside the file that declares them.
        /// </summary>
        private static float CruiseSpeed(Settings settings, float mass, bool largeGrid)
        {
            return largeGrid
                ? Core.CruiseCurve.Speed(mass,
                    settings.LargeGridMinMass, settings.LargeGridMidMass, settings.LargeGridMaxMass,
                    settings.LargeGridMinCruise, settings.LargeGridMidCruise, settings.LargeGridMaxCruise)
                : Core.CruiseCurve.Speed(mass,
                    settings.SmallGridMinMass, settings.SmallGridMidMass, settings.SmallGridMaxMass,
                    settings.SmallGridMinCruise, settings.SmallGridMidCruise, settings.SmallGridMaxCruise);
        }

        /// <summary>
        /// Raises the world's speed cap while this is on and puts back what the world shipped when it
        /// is turned off, so a world that stops using this stops flying differently.
        /// </summary>
        private static void ApplyCap(Settings settings)
        {
            MyDefinitionManager definitions = MyDefinitionManager.Static;
            if (definitions == null || definitions.EnvironmentDefinition == null) return;

            if (!settings.EnableTopSpeed)
            {
                if (!capApplied) return;

                definitions.EnvironmentDefinition.LargeShipMaxSpeed = shippedLargeCap;
                definitions.EnvironmentDefinition.SmallShipMaxSpeed = shippedSmallCap;
                capApplied = false;
                return;
            }

            if (!capApplied)
            {
                shippedLargeCap = definitions.EnvironmentDefinition.LargeShipMaxSpeed;
                shippedSmallCap = definitions.EnvironmentDefinition.SmallShipMaxSpeed;
                capApplied = true;
                appliedCap = float.NaN;
            }

            if (appliedCap == settings.SpeedLimit) return;

            appliedCap = settings.SpeedLimit;
            definitions.EnvironmentDefinition.LargeShipMaxSpeed = appliedCap;
            definitions.EnvironmentDefinition.SmallShipMaxSpeed = appliedCap;
        }

        /// <summary>
        /// Holds one physical group under the cruise speed its combined mass earns.
        ///
        /// <para>
        /// **One force at the group's centre of mass**, for the reason the drag pass gives: a force
        /// at each subgrid's own centre makes a torque no real resistance makes, and pulls rotors
        /// apart. The mass the curve is read with is the group's, so a tug and its load are one ship.
        /// </para>
        /// </summary>
        private static void ApplyToGroupOf(IMyCubeGrid leader, Settings settings)
        {
            IMyGridGroupData group = leader.GetGridGroup(GridLinkTypeEnum.Physical);
            if (group == null) return;

            GroupGrids.Clear();
            group.GetGrids(GroupGrids);
            if (GroupGrids.Count == 0) return;

            long identity = long.MaxValue;
            for (int i = 0; i < GroupGrids.Count; i++)
            {
                if (GroupGrids[i] != null && GroupGrids[i].EntityId < identity)
                {
                    identity = GroupGrids[i].EntityId;
                }
            }

            if (!Handled.Add(identity)) return;

            IMyCubeGrid physical = null;
            float mass = 0f;
            bool largeGrid = leader.GridSizeEnum == MyCubeSize.Large;

            for (int i = 0; i < GroupGrids.Count; i++)
            {
                IMyCubeGrid grid = GroupGrids[i];
                if (grid == null || grid.Physics == null) continue;

                // The group moves as one body, so one of its grids carries the physics that answers
                // for all of them; whichever the walk reaches first is that one.
                if (physical == null)
                {
                    physical = grid;
                    largeGrid = grid.GridSizeEnum == MyCubeSize.Large;
                }

                mass += grid.Physics.Mass;
            }

            if (physical == null || mass <= 0f) return;

            float speed = physical.Physics.Speed;
            float cruise = CruiseSpeed(settings, mass, largeGrid);
            if (speed <= cruise) return;

            // Boosting off means a ship may not exceed its cruise speed at all; on, it may be pushed
            // past and is dragged back, which is what makes a burst of thrust worth something.
            if (!settings.EnableSpeedBoost && speed <= cruise) return;

            float resistance = largeGrid ? settings.LargeGridResistance : settings.SmallGridResistance;
            float ceiling = largeGrid ? settings.LargeGridMaxBoostSpeed : settings.SmallGridMaxBoostSpeed;

            float newtons = resistance * mass * (1f - (cruise / speed));
            Vector3 force = physical.Physics.LinearVelocity * -newtons;

            physical.Physics.AddForce(VRage.Game.Components.MyPhysicsForceType.APPLY_WORLD_FORCE, force,
                physical.Physics.CenterOfMassWorld, null, ceiling);
        }
    }
}
