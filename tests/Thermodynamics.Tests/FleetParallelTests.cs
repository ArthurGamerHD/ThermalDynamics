using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    public class FleetParallelTests
    {
        private const int Grids = 6;

        private const int NodesEach = 400;

        private const int Steps = 8;

/// <summary>Sets the tings.</summary>
        private static ThermalSettings Settings()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.MaxElementVisitsPerStep = 0;
            return settings.Derive();
        }

/// <summary>Run operation.</summary>
        private static float[][] Run(bool parallel)
        {
/// <summary>Sets the tings.</summary>
            ThermalSettings settings = Settings();
            List<ThermalSimulation> fleet = FleetParallelLab.Fleet(Grids, NodesEach, settings);

            EnvironmentState state =
                EnvironmentSolver.Solve(settings, fleet[0].Planet, Worlds.Shadow());

            if (parallel)
            {
                FleetParallelLab.StepParallel(fleet, settings, state, Steps, Grids);
            }
            else
            {
                FleetParallelLab.StepSequential(fleet, settings, state, Steps);
            }

            float[][] result = new float[fleet.Count][];
            for (int i = 0; i < fleet.Count; i++) result[i] = SolverAb.Temperatures(fleet[i]);
            return result;
        }

        [Fact]
/// <summary>SteppingAFleetInParallelIsIdenticalToSteppingItInOrder operation.</summary>
        public void SteppingAFleetInParallelIsIdenticalToSteppingItInOrder()
        {
/// <summary>Run operation.</summary>
            float[][] sequential = Run(false);
/// <summary>Run operation.</summary>
            float[][] parallel = Run(true);

            Assert.Equal(sequential.Length, parallel.Length);

            int judged = 0;
            for (int g = 0; g < sequential.Length; g++)
            {
                Assert.Equal(sequential[g].Length, parallel[g].Length);
                Assert.True(sequential[g].Length > NodesEach / 2,
                    "grid " + g + " built " + sequential[g].Length + " nodes");

                for (int i = 0; i < sequential[g].Length; i++)
                {
                    judged++;
                    if (sequential[g][i] == parallel[g][i]) continue;

                    Assert.Fail( "grid " + g + " node " + i + " is " + parallel[g][i]
                        + " in parallel against " + sequential[g][i] + " in order");
                }
            }

            Assert.True(judged > Grids * NodesEach / 2,
/// <summary>nothing operation.</summary>
                "only " + judged + " nodes were compared, so this agreed about almost nothing (`E8`)");
        }

        [Fact]
/// <summary>TheFleetTheComparisonRunsOnActuallyMoves operation.</summary>
        public void TheFleetTheComparisonRunsOnActuallyMoves()
        {
/// <summary>Sets the tings.</summary>
            ThermalSettings settings = Settings();
            List<ThermalSimulation> fleet = FleetParallelLab.Fleet(2, NodesEach, settings);

            EnvironmentState state =
                EnvironmentSolver.Solve(settings, fleet[0].Planet, Worlds.Shadow());

            float[] before = SolverAb.Temperatures(fleet[0]);
            FleetParallelLab.StepSequential(fleet, settings, state, Steps);
            float[] after = SolverAb.Temperatures(fleet[0]);

            int moved = 0;
            float low = float.MaxValue;
            float high = float.MinValue;
            for (int i = 0; i < after.Length; i++)
            {
                if (after[i] != before[i]) moved++;
                if (after[i] < low) low = after[i];
                if (after[i] > high) high = after[i];
            }

            Assert.True(moved > after.Length / 10,
                "only " + moved + " of " + after.Length + " nodes moved in " + Steps + " steps");
            Assert.True(high - low > 100f,
                "the fleet spans " + (high - low).ToString("n1") + " K, which is not a gradient");
        }

        [Fact]
/// <summary>EveryPieceOfStaticStateInTheCoreSaysWhyTwoGridsMayShareIt operation.</summary>
        public void EveryPieceOfStaticStateInTheCoreSaysWhyTwoGridsMayShareIt()
        {
/// <summary>typeof operation.</summary>
            Assembly core = typeof(ThermalSolver).Assembly;

            Dictionary<string, string> reasons = new Dictionary<string, string>
            {
                { "Thermodynamics.Core.BlockMaterials.Table", "read-only table" },
                { "Thermodynamics.Core.ReferenceMaterials.Table", "read-only table" },
                { "Thermodynamics.Core.GroundTemperature.Grounds", "read-only table" },
                { "Thermodynamics.Core.WeatherResponse.Weathers", "read-only table" },
                { "Thermodynamics.Core.PlanetThermalDerivation.LevelTemperatures", "read-only table" },
                { "Thermodynamics.Core.Incandescence.Locus", "read-only table" },
                { "Thermodynamics.Core.FidelityEnds.All", "read-only table" },
                { "Thermodynamics.Core.ThermalSolver+SubstepProfile.DemandEdges", "read-only table" },
                { "Thermodynamics.Core.ThermalSolver+SubstepProfile.ProjectedCaps", "read-only table" },

                { "Thermodynamics.Core.ThermalSolver+StepPhaseProfile.Names", "read-only table" },

                { "Thermodynamics.Core.CellBitset.EmptyPrefix", "empty array" },
                { "Thermodynamics.Core.RoomMap.EmptyCells", "empty array" },
                { "Thermodynamics.Core.RoomMap.EmptyRanges", "empty array" },

                { "Thermodynamics.Core.RoomMapper.MinusX", "geometry constant" },
                { "Thermodynamics.Core.RoomMapper.PlusX", "geometry constant" },
                { "Thermodynamics.Core.RoomMapper.LateralFaces", "geometry constant" },

                { "Thermodynamics.Core.Face.Offsets", "geometry constant" },
                { "Thermodynamics.Core.Face.Normals", "geometry constant" },
                { "Thermodynamics.Core.Face.NamesByIndex", "geometry constant" },
                { "Thermodynamics.Core.BoxGeometry.PositiveFace", "geometry constant" },
                { "Thermodynamics.Core.BoxGeometry.NegativeFace", "geometry constant" },
                { "Thermodynamics.Core.BlockSurfaceBuilder.FaceMountBounds", "geometry constant" },
                { "Thermodynamics.Core.SolarOcclusionSampler.Corners", "geometry constant" },

                { "Thermodynamics.Core.BlockMaterials.Steel", "value constant" },
                { "Thermodynamics.Core.GroundTemperature.Neutral", "value constant" },
                { "Thermodynamics.Core.ReferenceMaterials.MildSteel", "value constant" },
                { "Thermodynamics.Core.ReferenceMaterials.Aluminium", "value constant" },
                { "Thermodynamics.Core.ReferenceMaterials.Copper", "value constant" },
                { "Thermodynamics.Core.ReferenceMaterials.SodaLimeGlass", "value constant" },
                { "Thermodynamics.Core.WeatherResponse.Calm", "value constant" },

                { "Thermodynamics.Core.BlockOrientation.rotatedAxes", "table built once, never written after" },
                { "Thermodynamics.Core.BlockOrientation.rotatedFaces", "table built once, never written after" },
                { "Thermodynamics.Core.BlockOrientation.legal", "table built once, never written after" },

                { "Thermodynamics.Core.GridMath.KeyByFace", "table built once, never written after" },

                { "Thermodynamics.Core.ShapeNormal.OneCellOffsets", "table built once, never written after" },
                { "Thermodynamics.Core.ShapeNormal.OneCellUnits", "table built once, never written after" },

                { "Thermodynamics.Core.ThermalValidation.Lock", "the lock itself" },

                { "Thermodynamics.Core.RoomMap.AllExternal", "shared empty default, never written" },

                { "Thermodynamics.Core.ThermalValidation.Said", "mutable, off the stepping path" },
                { "Thermodynamics.Core.ThermalValidation.Found", "mutable, off the stepping path" },
                { "Thermodynamics.Core.ThermalValidation.Writer", "mutable, off the stepping path" },
            };

/// <summary>List operation.</summary>
            List<string> unexplained = new List<string>();
            int judged = 0;
            int explained = 0;

            foreach (Type type in core.GetTypes())
            {
                if (type.FullName == null) continue;
                if (!type.FullName.StartsWith("Thermodynamics.", StringComparison.Ordinal)) continue;

                FieldInfo[] fields = type.GetFields(
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];

                    if (field.IsLiteral) continue;

                    if (field.IsInitOnly && (field.FieldType.IsPrimitive || field.FieldType.IsEnum))
                    {
                        continue;
                    }

                    judged++;

                    string name = type.FullName + "." + field.Name;
                    if (reasons.ContainsKey(name))
                    {
                        explained++;
                        continue;
                    }

                    unexplained.Add(name + " : " + field.FieldType.Name);
                }
            }

            Assert.True(judged > 15,
/// <summary>nothing operation.</summary>
                "only " + judged + " static fields were looked at, so this judged nothing (`E8`)");
            Assert.True(explained > 10,
                "only " + explained + " of the listed fields were found, so the list has gone stale"
                + " and this check is guarding names that no longer exist");
            Assert.True(unexplained.Count == 0,
                "static state in the core with no reason written down. Two grids on two threads"
                + " would share it, so each one is a decision:\n  "
                + string.Join("\n  ", unexplained));
        }
    }
}
