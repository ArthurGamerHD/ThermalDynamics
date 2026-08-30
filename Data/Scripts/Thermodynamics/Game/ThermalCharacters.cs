using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using Thermodynamics.Core;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.Utils;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// Heat as something that happens to a player, not only to a block.
    ///
    /// <para>
    /// The physics is <see cref="SuitThermal"/> and has no game types in it; this is the half that
    /// finds the room a player is standing in, carries their suit temperature between passes, and
    /// hands the damage to the game. See configuration.md, The suit.
    /// </para>
    ///
    /// <para>
    /// **Server only**, like every other damage the mod applies (`C10`), and **off costs nothing**
    /// (`C7`): with the switch down the pass returns before it looks up a single player.
    /// </para>
    /// </summary>
    public static class ThermalCharacters
    {

        /// <summary>
        /// Suit interior temperature per player, K. Keyed by identity rather than by entity so a
        /// respawn does not inherit the last body's temperature — a dead player's entity is gone,
        /// and <see cref="Forget"/> is what clears the row.
        /// </summary>
        private static readonly Dictionary<long, float> Interiors = new Dictionary<long, float>();

        private static readonly List<IMyPlayer> Players = new List<IMyPlayer>();

        /// <summary>Players whose suit was overwhelmed at the end of the last pass.</summary>
        public static int Overwhelmed { get; private set; }

        /// <summary>The last pass's hottest suit interior, K, or zero when nobody was simulated.</summary>
        public static float HottestInterior { get; private set; }

        /// <summary>Forgets a player's suit, so their next pass starts at comfort.</summary>
        public static void Forget(long identityId)
        {
            Interiors.Remove(identityId);
        }

        public static void Reset()
        {
            Interiors.Clear();
            Overwhelmed = 0;
            HottestInterior = 0f;
        }

        /// <summary>
        /// One pass over the players in the world. <paramref name="seconds"/> is the simulated time
        /// since the last one, which is what the suit integrates over.
        /// </summary>
        public static void Step(float seconds)
        {
            Overwhelmed = 0;
            HottestInterior = 0f;

            Settings settings = Settings.Instance;
            if (settings == null || !settings.EnableSuitDamage) return;
            if (MyAPIGateway.Session == null || !MyAPIGateway.Session.IsServer) return;
            if (MyAPIGateway.Players == null || seconds <= 0f) return;

            Players.Clear();
            MyAPIGateway.Players.GetPlayers(Players);

            for (int i = 0; i < Players.Count; i++)
            {
                Step(settings.ToCore(), Players[i], seconds);
            }

            Players.Clear();
        }

        private static void Step(ThermalSettings core, IMyPlayer player, float seconds)
        {
            if (player == null || core == null) return;

            IMyCharacter character = player.Character;
            if (character == null || character.IsDead)
            {
                if (player.IdentityId != 0L) Forget(player.IdentityId);
                return;
            }

            float roomKelvin;
            if (!RoomTemperature(character, out roomKelvin))
            {
                // Outside anything this mod has mapped. Not "safe" — unmeasured, which is why the
                // suit is put back to comfort rather than left holding a temperature it reached
                // somewhere else. See backlog C17.
                Forget(player.IdentityId);
                return;
            }

            float interior;
            if (!Interiors.TryGetValue(player.IdentityId, out interior))
            {
                interior = SuitThermal.ComfortKelvin;
            }

            IMyControllableEntity controllable = character as IMyControllableEntity;
            bool helmetOpen = controllable != null && !controllable.EnabledHelmet;

            SuitStepResult result = SuitThermal.Step(core, interior, roomKelvin, helmetOpen,
                character.SuitEnergyLevel > 0f, seconds);

            Interiors[player.IdentityId] = result.InteriorKelvin;

            if (result.Overwhelmed) Overwhelmed++;
            if (result.InteriorKelvin > HottestInterior) HottestInterior = result.InteriorKelvin;

            if (result.Damage <= 0f) return;

            IMyDestroyableObject destroyable = character as IMyDestroyableObject;
            if (destroyable != null) destroyable.DoDamage(result.Damage, ThermalGrid.ThermalDamage, true);
        }

        /// <summary>
        /// The air temperature where a character is standing, or false when they are not inside a
        /// room this mod holds air for.
        ///
        /// <para>
        /// A bounding-box test per live grid, which is why this runs on a rota rather than every
        /// frame. A character standing in a room is inside that grid's box, so the box rejects
        /// every grid they are nowhere near before anything is converted.
        /// </para>
        /// </summary>
        private static bool RoomTemperature(IMyCharacter character, out float kelvin)
        {
            kelvin = 0f;
            if (character == null) return false;

            Vector3D position = character.GetPosition();
            IList<ThermalGrid> grids = ThermalGrid.LiveGrids;

            for (int i = 0; i < grids.Count; i++)
            {
                ThermalGrid grid = grids[i];
                if (grid == null || grid.Grid == null || grid.Simulation == null) continue;
                if (grid.Grid.MarkedForClose) continue;
                if (grid.Grid.PositionComp.WorldAABB.Contains(position) == ContainmentType.Disjoint) continue;

                RoomAirNode air = grid.Simulation.GetRoomAir(
                    grid.Grid.WorldToGridInteger(position));
                if (air == null || air.Pressure <= 0f) continue;

                kelvin = air.Temperature;
                return true;
            }

            return false;
        }
    }
}
