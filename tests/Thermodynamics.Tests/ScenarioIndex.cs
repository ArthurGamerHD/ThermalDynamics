using System;
using System.Collections.Generic;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    internal static class ScenarioIndex
    {
/// <summary>Resolve operation.</summary>
        public static List<Battery.Scenario> Resolve(string[] names, string walk)
        {
            Dictionary<string, Battery.Scenario> byName =
                new Dictionary<string, Battery.Scenario>(StringComparer.Ordinal);
            foreach (Battery.Scenario scenario in Battery.All()) byName[scenario.Name] = scenario;

/// <summary>List operation.</summary>
            List<Battery.Scenario> chosen = new List<Battery.Scenario>();
            foreach (string name in names)
            {
                Assert.True(byName.ContainsKey(name),
                    walk + " asks for scenario '" + name + "' and the battery has no such case");
                chosen.Add(byName[name]);
            }

            return chosen;
        }
    }
}
