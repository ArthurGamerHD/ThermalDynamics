using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Sandbox.ModAPI;
using VRageMath;

namespace Thermodynamics
{
    public static class ThermalHeatSourceDebug
    {
        private const float PlaceDistance = 10f;

        private class Placed
        {
            public int Id;
            public double ExpiresAt;     // seconds since session start; 0 means never
        }

/// <summary>List operation.</summary>
        private static readonly List<Placed> Ours = new List<Placed>();

        private static double elapsed;

/// <summary>Update operation.</summary>
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

/// <summary>Run operation.</summary>
        public static string Run(string argument)
        {
            HeatSourceCommand.Parsed command = HeatSourceCommand.Parse(argument);

            switch (command.Verb)
            {
                case HeatSourceCommand.Verb.List:
/// <summary>List operation.</summary>
                    return List();

                case HeatSourceCommand.Verb.Clear:
/// <summary>Clear operation.</summary>
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
/// <summary>Place operation.</summary>
                    return Place(command.Watts, command.Range, 0f);

                case HeatSourceCommand.Verb.Pulse:
/// <summary>Place operation.</summary>
                    return Place(command.Watts, command.Range, command.Seconds);
            }

            return command.Error ?? HeatSourceCommand.Help();
        }

/// <summary>Place operation.</summary>
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

/// <summary>List operation.</summary>
        private static string List()
        {
            if (ThermalHeatSources.Count == 0) return "no heat sources registered";

/// <summary>StringBuilder operation.</summary>
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

/// <summary>Clear operation.</summary>
        private static string Clear()
        {
            int count = ThermalHeatSources.Count;
            ThermalHeatSources.Clear();
            Ours.Clear();
            return "removed " + count + " heat sources";
        }

/// <summary>Reset operation.</summary>
        public static void Reset()
        {
            Ours.Clear();
            elapsed = 0.0;
        }
    }
}
