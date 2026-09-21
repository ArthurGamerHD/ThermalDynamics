using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class SunlightPanelWalk
    {
        private const int Panel = 200;

        private const float ColdStart = 2.7f;

        private const float Clock = 1800f;

        private static readonly Vector3[] Directions =
        {
            Vector3.Forward, Vector3.Backward,
            Vector3.Left, Vector3.Right,
            Vector3.Up, Vector3.Down,
        };

        [Fact]
/// <summary>SunlightNeverCoolsFromAnyDirection operation.</summary>
        public void SunlightNeverCoolsFromAnyDirection()
        {
            List<Blueprints.Ship> ships = CorpusFixture.Spread(Panel);
            if (ships.Count == 0) return;

            List<List<string>> reports = LabRun.Map(ships, Compare, LabMode.Parallel);

/// <summary>List operation.</summary>
            List<string> violations = new List<string>();
            foreach (List<string> report in reports) violations.AddRange(report);

            Assert.True(ships.Count > 0);
            Assert.Empty(violations);
        }

/// <summary>Compare operation.</summary>
        private static List<string> Compare(Blueprints.Ship ship)
        {
/// <summary>List operation.</summary>
            List<string> violations = new List<string>();

/// <summary>Run operation.</summary>
            ScenarioOutcome shadow = Run(ship, null);
            List<ScenarioOutcome> record = new List<ScenarioOutcome> { shadow };

            foreach (Vector3 direction in Directions)
            {
/// <summary>Run operation.</summary>
                ScenarioOutcome sunlit = Run(ship, direction);
                record.Add(sunlit);

                if (sunlit.MeanKelvin < shadow.MeanKelvin - 0.5f)
                {
                    violations.Add("sunlight-panel: " + ship.Name + " is "
                        + (shadow.MeanKelvin - sunlit.MeanKelvin).ToString("n1")
/// <summary>Name operation.</summary>
                        + " K cooler lit from " + Name(direction) + " than in shadow, on equal "
                        + "clocks from a cold start — the solar path cooled a hull");
                }
            }

            CorpusRecord.Outcomes("sunlight-panel", record);
            return violations;
        }

/// <summary>Run operation.</summary>
        private static ScenarioOutcome Run(Blueprints.Ship ship, Vector3? sunDirection)
        {
            ShipAssembly assembly = ship.Build(null, ColdStart);
/// <summary>AssemblyRunner operation.</summary>
            AssemblyRunner runner = new AssemblyRunner(assembly);

            runner.Environment = sunDirection == null
                ? (System.Func<float, EnvironmentSample>)(t => Worlds.Shadow())
                : t => Worlds.Space(sunDirection.Value);

            runner.Run(Clock);

            ScenarioOutcome outcome = ScenarioOutcome.Read(assembly, ship.Name,
/// <summary>Name operation.</summary>
                sunDirection == null ? "cold-shadow" : "cold-sunlit-" + Name(sunDirection.Value));
            outcome.WorkshopId = ship.WorkshopId;
            return outcome;
        }

/// <summary>Name operation.</summary>
        private static string Name(Vector3 direction)
        {
            if (direction == Vector3.Forward) return "forward";
            if (direction == Vector3.Backward) return "backward";
            if (direction == Vector3.Left) return "left";
            if (direction == Vector3.Right) return "right";
            if (direction == Vector3.Up) return "up";
            return "down";
        }
    }
}
