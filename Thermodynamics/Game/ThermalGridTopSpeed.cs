using Sandbox.Definitions;
using Sandbox.ModAPI;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;

namespace Thermodynamics
{
    public static class ThermalGridTopSpeed
    {
        private static bool capApplied;

        private static float shippedLargeCap;
        private static float shippedSmallCap;

        private static float appliedCap;

        private static readonly List<IMyCubeGrid> GroupGrids = new List<IMyCubeGrid>();

        private static readonly HashSet<long> Handled = new HashSet<long>();

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

                IMyCubeGrid grid = thermals.Grid;
                if (grid.Physics == null || grid.Physics.Speed <= 0f) continue;

                ApplyToGroupOf(grid, settings);
            }
        }

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

        private static void ApplyToGroupOf(IMyCubeGrid leader, Settings settings)
        {
            if (!GridGroups.TryClaim(leader, GroupGrids, Handled)) return;

            IMyCubeGrid physical = null;
            float mass = 0f;
            bool largeGrid = leader.GridSizeEnum == MyCubeSize.Large;

            for (int i = 0; i < GroupGrids.Count; i++)
            {
                IMyCubeGrid grid = GroupGrids[i];
                if (grid == null || grid.Physics == null) continue;

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

            float resistance = largeGrid ? settings.LargeGridResistance : settings.SmallGridResistance;
            float newtons = Core.TopSpeedForce.Newtons(resistance, mass, cruise, speed);
            if (newtons <= 0f) return;

            float boost = largeGrid ? settings.LargeGridMaxBoostSpeed : settings.SmallGridMaxBoostSpeed;
            float ceiling = Core.TopSpeedForce.Ceiling(settings.EnableSpeedBoost, cruise, boost);

            Vector3 force = physical.Physics.LinearVelocity * -newtons;

            physical.Physics.AddForce(VRage.Game.Components.MyPhysicsForceType.APPLY_WORLD_FORCE, force,
                physical.Physics.CenterOfMassWorld, null, maxSpeed: ceiling);
        }
    }
}
