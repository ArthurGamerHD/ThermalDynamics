using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Sandbox.ModAPI;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// Placing and dialling point heat sources from chat, so the mechanism can be seen without writing
    /// a second mod against it. Drives the same <see cref="ThermalHeatSources"/> entry points the API
    /// does, which is what makes it a debug tool rather than a second implementation; the command
    /// parsing is in <see cref="HeatSourceCommand"/>, which has no game reference.
    /// See api.md, Heat sources.
    /// </summary>
    public static class ThermalHeatSourceDebug
    {
        /// <summary>Metres in front of the camera a placed source lands.</summary>
        private const float PlaceDistance = 10f;

        /// <summary>
        /// Sources placed from chat, and when a transient one should be removed. Kept apart from
        /// the registry's own list so a pulse cannot expire a source another mod registered.
        /// </summary>
        private class Placed
        {
            public int Id;
            public double ExpiresAt;     // seconds since session start; 0 means never
        }

        private static readonly List<Placed> Ours = new List<Placed>();

        private static double elapsed;

        /// <summary>
        /// Expires any pulse whose time is up. Called once per session tick; costs a walk of the
        /// chat-placed list, which is empty in every world where nobody typed the command.
        /// </summary>
        public static void Update(float seconds)
        {
            elapsed += seconds;
            if (Ours.Count == 0) return;

            for (int i = Ours.Count - 1; i >= 0; i--)
            {
                if (Ours[i].ExpiresAt <= 0.0 || elapsed < Ours[i].ExpiresAt) continue;

                ThermalHeatSources.Remove(Ours[i].Id);
                Ours.RemoveAt(i);
            }
        }

        /// <summary>Runs a <c>heat</c> subcommand and returns what to tell the player.</summary>
        public static string Run(string argument)
        {
            HeatSourceCommand.Parsed command = HeatSourceCommand.Parse(argument);

            switch (command.Verb)
            {
                case HeatSourceCommand.Verb.List:
                    return List();

                case HeatSourceCommand.Verb.Clear:
                    return Clear();

                case HeatSourceCommand.Verb.Remove:
                    return ThermalHeatSources.Remove(command.Id)
                        ? "removed heat source " + command.Id
                        : "no heat source with id " + command.Id;

                case HeatSourceCommand.Verb.Set:
                    return ThermalHeatSources.Update(command.Id, command.Watts)
                        ? "heat source " + command.Id + " now "
                          + HeatSourceCommand.Describe(command.Watts)
                        : "no heat source with id " + command.Id;

                case HeatSourceCommand.Verb.Place:
                    return Place(command.Watts, command.Range, 0f);

                case HeatSourceCommand.Verb.Pulse:
                    return Place(command.Watts, command.Range, command.Seconds);
            }

            return command.Error ?? HeatSourceCommand.Help();
        }

        /// <summary>
        /// Puts a source just in front of the player's camera, which is where someone typing this
        /// is looking. Fixed in the world rather than attached to the player, so it can be flown
        /// away from and observed from a ship rather than following the observer around.
        /// </summary>
        private static string Place(float watts, float range, float lifetime)
        {
            MatrixD camera = MyAPIGateway.Session != null && MyAPIGateway.Session.Camera != null
                ? MyAPIGateway.Session.Camera.WorldMatrix
                : MatrixD.Identity;

            Vector3D position = camera.Translation + (camera.Forward * PlaceDistance);

            int id = ThermalHeatSources.Add(position, watts, range);
            if (id == 0) return "could not place a heat source";

            Ours.Add(new Placed
            {
                Id = id,
                ExpiresAt = lifetime > 0f ? elapsed + lifetime : 0.0,
            });

            string note = lifetime > 0f
                ? " for " + lifetime.ToString("n0", CultureInfo.InvariantCulture) + " s"
                : "";

            return "heat source " + id + ": " + HeatSourceCommand.Describe(watts) + " out to "
                + range.ToString("n0", CultureInfo.InvariantCulture) + " m" + note;
        }

        private static string List()
        {
            if (ThermalHeatSources.Count == 0) return "no heat sources registered";

            StringBuilder sb = new StringBuilder();
            sb.Append(ThermalHeatSources.Count).AppendLine(" heat sources:");

            IList<ThermalHeatSources.HeatSource> all = ThermalHeatSources.All;
            for (int i = 0; i < all.Count; i++)
            {
                ThermalHeatSources.HeatSource source = all[i];
                sb.Append("  ").Append(source.Id).Append(": ")
                    .Append(HeatSourceCommand.Describe(source.Watts))
                    .Append(", range ").Append(source.Range.ToString("n0", CultureInfo.InvariantCulture))
                    .Append(" m").Append(source.Entity != null ? ", following an entity" : "")
                    .AppendLine();
            }

            return sb.ToString().TrimEnd();
        }

        private static string Clear()
        {
            int count = ThermalHeatSources.Count;
            ThermalHeatSources.Clear();
            Ours.Clear();
            return "removed " + count + " heat sources";
        }

        /// <summary>Forgets chat-placed bookkeeping. For a session teardown, and for tests.</summary>
        public static void Reset()
        {
            Ours.Clear();
            elapsed = 0.0;
        }
    }
}
