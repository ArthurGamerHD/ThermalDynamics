using System;
using System.Collections.Generic;

namespace Thermodynamics.Core
{
    public static class ThermalValidation
    {
/// <summary>object operation.</summary>
        private static readonly object Lock = new object();
/// <summary>HashSet operation.</summary>
        private static readonly HashSet<string> Said = new HashSet<string>();
/// <summary>List operation.</summary>
        private static readonly List<string> Found = new List<string>();

        public static Action<string> Writer;

        public static List<string> Problems
        {
/// <summary>lock operation.</summary>
            get { lock (Lock) { return new List<string>(Found); } }
        }

        public static int Count
        {
/// <summary>lock operation.</summary>
            get { lock (Lock) { return Found.Count; } }
        }

/// <summary>Reset operation.</summary>
        public static void Reset()
        {
            lock (Lock)
            {
                Said.Clear();
                Found.Clear();
            }
        }

/// <summary>Check operation.</summary>
        public static void Check(string subject, BlockThermalProperties declared)
        {
            if (declared != null) Report(subject, declared.Validate());
        }

/// <summary>Check operation.</summary>
        public static void Check(ThermalSettings settings)
        {
            if (settings != null) Report("settings", settings.Validate());
        }

/// <summary>Report operation.</summary>
        private static void Report(string subject, List<string> problems)
        {
            if (problems == null || problems.Count == 0) return;

            for (int i = 0; i < problems.Count; i++)
            {
                string line = subject + ": " + problems[i];

                lock (Lock)
                {
                    if (!Said.Add(line)) continue;
                    Found.Add(line);
                }

                Action<string> writer = Writer;
                if (writer == null) continue;

                try
                {
                    writer(line);
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
