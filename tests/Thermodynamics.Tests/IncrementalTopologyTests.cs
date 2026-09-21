using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    public class IncrementalTopologyTests
    {
/// <summary>LinkSignature operation.</summary>
        private static List<string> LinkSignature(ThermalSolver solver)
        {
/// <summary>List operation.</summary>
            List<string> rows = new List<string>();
            IList<ThermalLink> links = solver.Links;

            for (int i = 0; i < links.Count; i++)
            {
                ThermalLink link = links[i];

                Vector3I a = solver.Nodes[link.NodeA].Block.Position;
                Vector3I b = solver.Nodes[link.NodeB].Block.Position;

/// <summary>Key operation.</summary>
                string low = Key(a);
/// <summary>Key operation.</summary>
                string high = Key(b);
                if (string.CompareOrdinal(low, high) > 0)
                {
                    string swap = low;
                    low = high;
                    high = swap;
                }

                rows.Add(low + "|" + high + "|" + link.Conductance.ToString("f6")
                    + "|" + link.ContactFaces);
            }

            rows.Sort(StringComparer.Ordinal);
            return rows;
        }

/// <summary>Key operation.</summary>
        private static string Key(Vector3I cell)
        {
            return cell.X + "," + cell.Y + "," + cell.Z;
        }

/// <summary>Shape operation.</summary>
        private static List<Vector3I> Shape()
        {
/// <summary>List operation.</summary>
            List<Vector3I> cells = new List<Vector3I>();

            for (int z = 0; z < 4; z++)
                for (int y = 0; y < 3; y++)
                    for (int x = 0; x < 3; x++)
                        cells.Add(new Vector3I(x, y, z));

            cells.Add(new Vector3I(3, 1, 1));
            cells.Add(new Vector3I(4, 1, 1));
            cells.Add(new Vector3I(5, 2, 2));
            cells.Add(new Vector3I(-3, 0, 0));

            return cells;
        }

/// <summary>Pace operation.</summary>
        private static ThermalSettings Pace()
        {
            ThermalSettings settings = new ThermalSettings { Frequency = 4 };
            return settings.Derive();
        }

/// <summary>BuiltAllAtOnce operation.</summary>
        private static ThermalSimulation BuiltAllAtOnce(List<Vector3I> cells)
        {
            GridBuilder builder = GridBuilder.Large();
            for (int i = 0; i < cells.Count; i++)
            {
                builder.Place(Model(i), cells[i]);
            }

/// <summary>ThermalSimulation operation.</summary>
            ThermalSimulation simulation = new ThermalSimulation(Pace(), builder.Grid);
            for (int i = 0; i < builder.Placed.Count; i++)
            {
                simulation.Solver.AddBlock(builder.Placed[i], 293.15f);
            }

            simulation.RebuildAll();
            return simulation;
        }

/// <summary>BuiltOneAtATime operation.</summary>
        private static ThermalSimulation BuiltOneAtATime(List<Vector3I> cells)
        {
/// <summary>GridModel operation.</summary>
            GridModel grid = new GridModel(Catalog.LargeGridSize);
/// <summary>ThermalSimulation operation.</summary>
            ThermalSimulation simulation = new ThermalSimulation(Pace(), grid);

            for (int i = 0; i < cells.Count; i++)
            {
                simulation.AddBlock(
/// <summary>BlockInstance operation.</summary>
                    new BlockInstance(Model(i), cells[i], BlockOrientation.Identity), 293.15f);

                simulation.Update(LoadBenchmarks.TickSeconds, Worlds.Shadow());
            }

            while (simulation.HasPendingWork)
            {
                simulation.Update(LoadBenchmarks.TickSeconds, Worlds.Shadow());
            }

            return simulation;
        }

/// <summary>Model operation.</summary>
        private static BlockModel Model(int index)
        {
            return (index % 3) == 0 ? Catalog.Grating() : Catalog.HeavyArmor();
        }

        [Fact]
/// <summary>PlacingOneBlockAtATimeBuildsTheSameGraphAsBuildingItAllAtOnce operation.</summary>
        public void PlacingOneBlockAtATimeBuildsTheSameGraphAsBuildingItAllAtOnce()
        {
/// <summary>Shape operation.</summary>
            List<Vector3I> cells = Shape();

/// <summary>LinkSignature operation.</summary>
            List<string> incremental = LinkSignature(BuiltOneAtATime(cells).Solver);
/// <summary>LinkSignature operation.</summary>
            List<string> global = LinkSignature(BuiltAllAtOnce(cells).Solver);

            Assert.Equal(global.Count, incremental.Count);
            Assert.Equal(global, incremental);
        }

        [Fact]
/// <summary>TwoBlocksPlacedTogetherAgainstEachOtherGetOneLinkNotTwo operation.</summary>
        public void TwoBlocksPlacedTogetherAgainstEachOtherGetOneLinkNotTwo()
        {
/// <summary>GridModel operation.</summary>
            GridModel grid = new GridModel(Catalog.LargeGridSize);
/// <summary>ThermalSimulation operation.</summary>
            ThermalSimulation simulation = new ThermalSimulation(Pace(), grid);

            simulation.AddBlock(new BlockInstance(Catalog.HeavyArmor(), Vector3I.Zero,
                BlockOrientation.Identity), 293.15f);
            simulation.AddBlock(new BlockInstance(Catalog.HeavyArmor(), new Vector3I(1, 0, 0),
                BlockOrientation.Identity), 293.15f);

            simulation.Update(LoadBenchmarks.TickSeconds, Worlds.Shadow());

            Assert.Single(simulation.Solver.Links);
            Assert.Equal(1, simulation.Solver.Nodes[0].LinkCount);
            Assert.Equal(1, simulation.Solver.Nodes[1].LinkCount);
        }

        [Fact]
/// <summary>ConductanceTotalsMatchTheGlobalBuild operation.</summary>
        public void ConductanceTotalsMatchTheGlobalBuild()
        {
/// <summary>Shape operation.</summary>
            List<Vector3I> cells = Shape();

/// <summary>BuiltOneAtATime operation.</summary>
            ThermalSimulation incremental = BuiltOneAtATime(cells);
/// <summary>BuiltAllAtOnce operation.</summary>
            ThermalSimulation global = BuiltAllAtOnce(cells);

            incremental.Solver.SetAllTemperatures(293.15f);
            global.Solver.SetAllTemperatures(293.15f);

            float step = incremental.Settings.StepSeconds;
            float a = incremental.Solver.RequiredSubsteps(step);
            float b = global.Solver.RequiredSubsteps(step);

            float difference = Math.Abs(a - b) / Math.Max(1e-6f, Math.Abs(b));
            Assert.True(difference < 1e-4f,
                "substep estimate differed by " + difference.ToString("e2")
                + " relative: incremental " + a + ", global " + b);
        }

        [Fact]
/// <summary>BothRoutesSimulateToTheSameTemperatures operation.</summary>
        public void BothRoutesSimulateToTheSameTemperatures()
        {
/// <summary>Shape operation.</summary>
            List<Vector3I> cells = Shape();

/// <summary>BuiltOneAtATime operation.</summary>
            ThermalSimulation incremental = BuiltOneAtATime(cells);
/// <summary>BuiltAllAtOnce operation.</summary>
            ThermalSimulation global = BuiltAllAtOnce(cells);

            SeedByPosition(incremental);
            SeedByPosition(global);

            EnvironmentSample sample = Worlds.Space(new Vector3(0f, 1f, 0f));
            incremental.StepExact(40, sample);
            global.StepExact(40, sample);

            for (int i = 0; i < cells.Count; i++)
            {
                ThermalNode fromIncremental = incremental.Solver.GetNodeAt(cells[i]);
                ThermalNode fromGlobal = global.Solver.GetNodeAt(cells[i]);

                Assert.NotNull(fromIncremental);
                Assert.NotNull(fromGlobal);
                Assert.Equal(fromGlobal.Temperature, fromIncremental.Temperature, 3);
            }
        }


        [Fact]
/// <summary>GrindingBlocksOffLeavesTheSameGraphAsNeverBuildingThem operation.</summary>
        public void GrindingBlocksOffLeavesTheSameGraphAsNeverBuildingThem()
        {
/// <summary>Shape operation.</summary>
            List<Vector3I> cells = Shape();

            Vector3I[] removed =
            {
/// <summary>Vector3I operation.</summary>
                new Vector3I(1, 1, 1),
/// <summary>Vector3I operation.</summary>
                new Vector3I(4, 1, 1),
/// <summary>Vector3I operation.</summary>
                new Vector3I(-3, 0, 0),
/// <summary>Vector3I operation.</summary>
                new Vector3I(0, 0, 0),
/// <summary>Vector3I operation.</summary>
                new Vector3I(2, 2, 3),
            };

/// <summary>BuiltAllAtOnce operation.</summary>
            ThermalSimulation ground = BuiltAllAtOnce(cells);
            for (int i = 0; i < removed.Length; i++)
            {
                BlockInstance block = ground.Grid.GetAtCell(removed[i]);
                Assert.NotNull(block);
                ground.RemoveBlock(block);
                ground.Update(LoadBenchmarks.TickSeconds, Worlds.Shadow());
            }
            while (ground.HasPendingWork) ground.Update(LoadBenchmarks.TickSeconds, Worlds.Shadow());

/// <summary>List operation.</summary>
            List<Vector3I> remaining = new List<Vector3I>();
            for (int i = 0; i < cells.Count; i++)
            {
                bool dropped = false;
                for (int r = 0; r < removed.Length; r++)
                {
                    if (cells[i] == removed[r]) dropped = true;
                }
                if (!dropped) remaining.Add(cells[i]);
            }

/// <summary>BuiltAllAtOnce operation.</summary>
            ThermalSimulation fresh = BuiltAllAtOnce(cells);
            for (int i = 0; i < removed.Length; i++)
            {
                BlockInstance block = fresh.Grid.GetAtCell(removed[i]);
                fresh.Grid.Remove(block);
                fresh.Solver.RemoveBlock(block);
                fresh.Surfaces.RemoveBlock(block);
            }
            fresh.Solver.RebuildLinks();

            Assert.Equal(remaining.Count, ground.Solver.Nodes.Count);
            Assert.Equal(LinkSignature(fresh.Solver), LinkSignature(ground.Solver));
        }

        [Fact]
/// <summary>ArbitraryBuildingAndGrindingAlwaysMatchesARebuild operation.</summary>
        public void ArbitraryBuildingAndGrindingAlwaysMatchesARebuild()
        {
/// <summary>GridModel operation.</summary>
            GridModel grid = new GridModel(Catalog.LargeGridSize);
/// <summary>ThermalSimulation operation.</summary>
            ThermalSimulation simulation = new ThermalSimulation(Pace(), grid);

/// <summary>List operation.</summary>
            List<Vector3I> plot = new List<Vector3I>();
            for (int z = 0; z < 5; z++)
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                        plot.Add(new Vector3I(x, y, z));

            uint state = 0x12345678u;
            int steps = 400;

            for (int step = 0; step < steps; step++)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;

                Vector3I cell = plot[(int)(state % (uint)plot.Count)];
                BlockInstance occupant = grid.GetAtCell(cell);

                if (occupant != null)
                {
                    simulation.RemoveBlock(occupant);
                }
                else
                {
                    simulation.AddBlock(
/// <summary>BlockInstance operation.</summary>
                        new BlockInstance(Model(step), cell, BlockOrientation.Identity), 293.15f);
                }

                simulation.Update(LoadBenchmarks.TickSeconds, Worlds.Shadow());

/// <summary>LinkSignature operation.</summary>
                List<string> incremental = LinkSignature(simulation.Solver);
                simulation.Solver.RebuildLinks();
/// <summary>LinkSignature operation.</summary>
                List<string> rebuilt = LinkSignature(simulation.Solver);

                Assert.True(rebuilt.Count == incremental.Count && Same(rebuilt, incremental),
                    "graph diverged at step " + step + " on cell " + cell
                    + ": incremental had " + incremental.Count + " links, a rebuild "
                    + rebuilt.Count);
            }
        }

/// <summary>Same operation.</summary>
        private static bool Same(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
            }
            return true;
        }

        [Fact]
/// <summary>LinkCountsPerNodeSurviveChurn operation.</summary>
        public void LinkCountsPerNodeSurviveChurn()
        {
/// <summary>Shape operation.</summary>
            List<Vector3I> cells = Shape();
/// <summary>BuiltAllAtOnce operation.</summary>
            ThermalSimulation simulation = BuiltAllAtOnce(cells);

            simulation.RemoveBlock(simulation.Grid.GetAtCell(new Vector3I(1, 1, 1)));
            simulation.RemoveBlock(simulation.Grid.GetAtCell(new Vector3I(4, 1, 1)));
            simulation.AddBlock(new BlockInstance(Catalog.HeavyArmor(), new Vector3I(1, 1, 1),
                BlockOrientation.Identity), 293.15f);
            simulation.Update(LoadBenchmarks.TickSeconds, Worlds.Shadow());

            int[] counted = new int[simulation.Solver.Nodes.Count];
            IList<ThermalLink> links = simulation.Solver.Links;
            for (int i = 0; i < links.Count; i++)
            {
                counted[links[i].NodeA]++;
                counted[links[i].NodeB]++;
            }

            for (int i = 0; i < counted.Length; i++)
            {
                Assert.Equal(counted[i], simulation.Solver.Nodes[i].LinkCount);
            }
        }

        [Fact]
/// <summary>GrindingDoesNotDisturbWhatIsLeftStanding operation.</summary>
        public void GrindingDoesNotDisturbWhatIsLeftStanding()
        {
/// <summary>Shape operation.</summary>
            List<Vector3I> cells = Shape();
/// <summary>Vector3I operation.</summary>
            Vector3I doomed = new Vector3I(4, 1, 1);

/// <summary>BuiltAllAtOnce operation.</summary>
            ThermalSimulation ground = BuiltAllAtOnce(cells);
            SeedByPosition(ground);
            ground.RemoveBlock(ground.Grid.GetAtCell(doomed));
            ground.Update(LoadBenchmarks.TickSeconds, Worlds.Shadow());

/// <summary>BuiltAllAtOnce operation.</summary>
            ThermalSimulation reference = BuiltAllAtOnce(cells);
            SeedByPosition(reference);
            BlockInstance block = reference.Grid.GetAtCell(doomed);
            reference.Grid.Remove(block);
            reference.Solver.RemoveBlock(block);
            reference.Surfaces.RemoveBlock(block);
            reference.Solver.RebuildLinks();

            EnvironmentSample sample = Worlds.Space(new Vector3(0f, 1f, 0f));
            ground.StepExact(40, sample);
            reference.StepExact(40, sample);

            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i] == doomed) continue;

                ThermalNode a = ground.Solver.GetNodeAt(cells[i]);
                ThermalNode b = reference.Solver.GetNodeAt(cells[i]);
                Assert.NotNull(a);
                Assert.NotNull(b);
                Assert.Equal(b.Temperature, a.Temperature, 3);
            }
        }

        [Fact]
/// <summary>PlacingAndRemovingManyBlocksWithoutSteppingStaysInBounds operation.</summary>
        public void PlacingAndRemovingManyBlocksWithoutSteppingStaysInBounds()
        {
/// <summary>GridModel operation.</summary>
            GridModel grid = new GridModel(Catalog.LargeGridSize);
/// <summary>ThermalSimulation operation.</summary>
            ThermalSimulation simulation = new ThermalSimulation(Pace(), grid);

            simulation.AddBlock(new BlockInstance(Catalog.HeavyArmor(), Vector3I.Zero,
                BlockOrientation.Identity), 293.15f);
            simulation.Update(LoadBenchmarks.TickSeconds, Worlds.Shadow());

/// <summary>List operation.</summary>
            List<BlockInstance> placed = new List<BlockInstance>();
            for (int i = 1; i < 600; i++)
            {
/// <summary>BlockInstance operation.</summary>
                BlockInstance block = new BlockInstance(Catalog.HeavyArmor(),
/// <summary>Vector3I operation.</summary>
                    new Vector3I(0, 0, i), BlockOrientation.Identity);
                simulation.AddBlock(block, 293.15f);
                placed.Add(block);
            }

            for (int i = 0; i < placed.Count; i++)
            {
                simulation.RemoveBlock(placed[i]);
            }

            simulation.Update(LoadBenchmarks.TickSeconds, Worlds.Shadow());

            Assert.Single(simulation.Solver.Nodes);
            Assert.Empty(simulation.Solver.Links);
        }

        [Fact]
/// <summary>ConductanceTotalsSurviveTheBuffersGrowing operation.</summary>
        public void ConductanceTotalsSurviveTheBuffersGrowing()
        {
/// <summary>GridModel operation.</summary>
            GridModel grid = new GridModel(Catalog.LargeGridSize);
/// <summary>ThermalSolver operation.</summary>
            ThermalSolver solver = new ThermalSolver(new ThermalSettings(), grid, new SurfaceMap());

            for (int i = 0; i < 40; i++)
            {
/// <summary>BlockInstance operation.</summary>
                BlockInstance block = new BlockInstance(Model(i), new Vector3I(0, 0, i),
                    BlockOrientation.Identity);
                grid.Add(block);
                solver.AddBlock(block, 293.15f);
            }
            solver.RebuildLinks();

            for (int i = 40; i < 700; i++)
            {
/// <summary>BlockInstance operation.</summary>
                BlockInstance block = new BlockInstance(Model(i), new Vector3I(0, 0, i),
                    BlockOrientation.Identity);
                grid.Add(block);
                solver.AddBlock(block, 293.15f);
            }

            solver.BuildLinksIfNeeded();

            float step = solver.Settings.StepSeconds;
            float incremental = solver.RequiredSubsteps(step);

            solver.RebuildLinks();
            float rebuilt = solver.RequiredSubsteps(step);

            float difference = Math.Abs(incremental - rebuilt) / Math.Max(1e-6f, Math.Abs(rebuilt));
            Assert.True(difference < 1e-4f,
                "substep estimate after the buffers grew was " + incremental
                + ", a rebuild says " + rebuilt);
        }

        [Fact]
/// <summary>GrindingDownALongRunThatWasFullyLinkedLeavesNothingBehind operation.</summary>
        public void GrindingDownALongRunThatWasFullyLinkedLeavesNothingBehind()
        {
/// <summary>GridModel operation.</summary>
            GridModel grid = new GridModel(Catalog.LargeGridSize);
/// <summary>ThermalSimulation operation.</summary>
            ThermalSimulation simulation = new ThermalSimulation(Pace(), grid);

/// <summary>List operation.</summary>
            List<BlockInstance> placed = new List<BlockInstance>();
            for (int i = 0; i < 600; i++)
            {
/// <summary>BlockInstance operation.</summary>
                BlockInstance block = new BlockInstance(Catalog.HeavyArmor(),
/// <summary>Vector3I operation.</summary>
                    new Vector3I(0, 0, i), BlockOrientation.Identity);
                simulation.AddBlock(block, 293.15f);
                placed.Add(block);
            }

            simulation.Update(LoadBenchmarks.TickSeconds, Worlds.Shadow());
            simulation.Update(LoadBenchmarks.TickSeconds, Worlds.Shadow());
            Assert.Equal(599, simulation.Solver.Links.Count);

            for (int i = 0; i < placed.Count; i++)
            {
                simulation.RemoveBlock(placed[i]);
                simulation.Update(LoadBenchmarks.TickSeconds, Worlds.Shadow());
            }

            Assert.Empty(simulation.Solver.Nodes);
            Assert.Empty(simulation.Solver.Links);
        }

/// <summary>SeedByPosition operation.</summary>
        private static void SeedByPosition(ThermalSimulation simulation)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                Vector3I at = nodes[i].Block.Position;
                nodes[i].Temperature = 300f + 40f * ((at.X * 7 + at.Y * 13 + at.Z * 23) % 11);
            }
        }

        [Fact]
/// <summary>APlacementUndoneBeforeTheNextTickLeavesNoLink operation.</summary>
        public void APlacementUndoneBeforeTheNextTickLeavesNoLink()
        {
/// <summary>GridModel operation.</summary>
            GridModel grid = new GridModel(Catalog.LargeGridSize);
/// <summary>ThermalSimulation operation.</summary>
            ThermalSimulation simulation = new ThermalSimulation(Pace(), grid);

            simulation.AddBlock(new BlockInstance(Catalog.HeavyArmor(), Vector3I.Zero,
                BlockOrientation.Identity), 293.15f);
            simulation.Update(LoadBenchmarks.TickSeconds, Worlds.Shadow());

/// <summary>BlockInstance operation.</summary>
            BlockInstance transient = new BlockInstance(Catalog.HeavyArmor(), new Vector3I(1, 0, 0),
                BlockOrientation.Identity);
            simulation.AddBlock(transient, 293.15f);
            simulation.RemoveBlock(transient);

            simulation.Update(LoadBenchmarks.TickSeconds, Worlds.Shadow());

            Assert.Single(simulation.Solver.Nodes);
            Assert.Empty(simulation.Solver.Links);
        }
    }
}
