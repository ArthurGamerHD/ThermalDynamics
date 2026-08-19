using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class CoolantLoopTests
    {
        private static ThermalSettings Isolated()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.EnableFriction = false;
            settings.EnableDamage = false;
            return settings.Derive();
        }

        [Fact]
        public void AClosedPumpedRingIsDetected()
        {
            GridBuilder builder = GridBuilder.Large();
            PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, 3, 3));

            ThermalSimulation simulation = builder.BuildSimulation(Isolated());

            Assert.Single(simulation.Solver.Loops);
            Assert.Equal(8, simulation.Solver.Loops[0].PipeCount);
            Assert.True(simulation.Solver.Loops[0].HasPump);
        }

        [Fact]
        public void ARingWithoutAPumpIsIgnored()
        {
            GridBuilder builder = GridBuilder.Large();
            List<Vector3I> cells = PipeFitter.RectangleXZ(Vector3I.Zero, 3, 3);

            // build the ring by hand with no pump anywhere
            for (int i = 0; i < cells.Count; i++)
            {
                Vector3I cell = cells[i];
                Vector3I toPrevious = cells[(i - 1 + cells.Count) % cells.Count] - cell;
                Vector3I toNext = cells[(i + 1) % cells.Count] - cell;

                BlockModel model = toPrevious == -toNext
                    ? Catalog.CoolantPipeStraight()
                    : Catalog.CoolantPipeCorner();

                builder.Place(model, cell, PipeFitter.Orient(model, toPrevious, toNext));
            }

            ThermalSimulation simulation = builder.BuildSimulation(Isolated());
            Assert.Empty(simulation.Solver.Loops);
        }

        [Fact]
        public void AnOpenRunIsNotALoop()
        {
            GridBuilder builder = GridBuilder.Large();
            for (int x = 0; x < 4; x++)
            {
                BlockModel model = x == 0 ? Catalog.CoolantPump() : Catalog.CoolantPipeStraight();
                builder.Place(model, new Vector3I(x, 0, 0),
                    PipeFitter.Orient(model, Vector3I.Left, Vector3I.Right));
            }

            ThermalSimulation simulation = builder.BuildSimulation(Isolated());
            Assert.Empty(simulation.Solver.Loops);
        }

        [Fact]
        public void BreakingTheRingDestroysTheLoop()
        {
            GridBuilder builder = GridBuilder.Large();
            List<BlockInstance> ring = PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, 3, 3));

            ThermalSimulation simulation = builder.BuildSimulation(Isolated());
            Assert.Single(simulation.Solver.Loops);

            simulation.RemoveBlock(ring[3]);
            simulation.RebuildAll();

            Assert.Empty(simulation.Solver.Loops);
        }

        [Fact]
        public void ClosingTheRingCreatesTheLoop()
        {
            GridBuilder builder = GridBuilder.Large();
            List<Vector3I> cells = PipeFitter.RectangleXZ(Vector3I.Zero, 3, 3);
            List<BlockInstance> ring = PipeFitter.BuildRing(builder, cells);

            BlockInstance last = ring[ring.Count - 1];
            builder.Grid.Remove(last);
            builder.Placed.Remove(last);

            ThermalSimulation simulation = builder.BuildSimulation(Isolated());
            Assert.Empty(simulation.Solver.Loops);

            simulation.AddBlock(new BlockInstance(last.Model, last.Min, last.Orientation));
            simulation.RebuildAll();

            Assert.Single(simulation.Solver.Loops);
        }

        [Fact]
        public void ARingIsFoundOnlyOnceNoMatterWhereTheSearchStarts()
        {
            GridBuilder builder = GridBuilder.Large();
            PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, 4, 3));

            ThermalSimulation simulation = builder.BuildSimulation(Isolated());

            Assert.Single(simulation.Solver.Loops);
            Assert.Equal(10, simulation.Solver.Loops[0].PipeCount);
        }

        [Fact]
        public void TwoSeparateRingsAreBothFound()
        {
            GridBuilder builder = GridBuilder.Large();
            PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, 3, 3));
            PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(new Vector3I(0, 5, 0), 3, 3));

            ThermalSimulation simulation = builder.BuildSimulation(Isolated());
            Assert.Equal(2, simulation.Solver.Loops.Count);
        }

        [Fact]
        public void AMultiCellPumpIsWalkedEndToEnd()
        {
            // A 1x1x3 pump stands in for three consecutive cells of the ring. The original
            // crawler handled this by multiplying the step by three whenever the subtype name
            // was the small grid pump; here the block's ports say where they are.
            GridBuilder builder = GridBuilder.Large();

            // 2 x 5 ring in the XZ plane
            List<Vector3I> path = new List<Vector3I>();
            for (int z = 0; z < 5; z++) path.Add(new Vector3I(0, 0, z));
            for (int z = 4; z >= 0; z--) path.Add(new Vector3I(1, 0, z));

            // the pump occupies path cells 1, 2 and 3
            BlockModel pump = Catalog.CoolantPumpLong();
            builder.Place(pump, new Vector3I(0, 0, 1), BlockOrientation.Identity);

            for (int i = 0; i < path.Count; i++)
            {
                if (i >= 1 && i <= 3) continue;

                Vector3I cell = path[i];
                Vector3I toPrevious = path[(i - 1 + path.Count) % path.Count] - cell;
                Vector3I toNext = path[(i + 1) % path.Count] - cell;

                BlockModel model = toPrevious == -toNext
                    ? Catalog.CoolantPipeStraight()
                    : Catalog.CoolantPipeCorner();

                builder.Place(model, cell, PipeFitter.Orient(model, toPrevious, toNext));
            }

            ThermalSimulation simulation = builder.BuildSimulation(Isolated());

            Assert.Single(simulation.Solver.Loops);
            Assert.True(simulation.Solver.Loops[0].HasPump);

            // seven single cells plus the pump
            Assert.Equal(8, simulation.Solver.Loops[0].PipeCount);
        }

        [Fact]
        public void LoopSignatureIsStableAcrossRebuilds()
        {
            GridBuilder builder = GridBuilder.Large();
            PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, 3, 3));

            ThermalSimulation simulation = builder.BuildSimulation(Isolated());
            long first = simulation.Solver.Loops[0].Signature;

            simulation.RebuildAll();
            long second = simulation.Solver.Loops[0].Signature;

            Assert.Equal(first, second);
            Assert.NotEqual(0L, first);
        }

        [Fact]
        public void ARebuildKeepsTheCoolantTemperature()
        {
            GridBuilder builder = GridBuilder.Large();
            PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, 3, 3));

            ThermalSimulation simulation = builder.BuildSimulation(Isolated());
            simulation.Solver.Loops[0].Temperature = 777f;

            simulation.RebuildAll();

            Assert.Equal(777f, simulation.Solver.Loops[0].Temperature, 3);
        }

        [Fact]
        public void CoolantMovesHeatFromASinkFaceIntoTheFluid()
        {
            GridBuilder builder = GridBuilder.Large();

            Dictionary<int, Vector3I> sinks = new Dictionary<int, Vector3I>();
            sinks[1] = Vector3I.Down;

            PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, 3, 3), -1, sinks);
            builder.Place(Catalog.HeavyArmor(), new Vector3I(1, -1, 0));
            BlockInstance hot = builder.Last;

            ThermalSimulation simulation = builder.BuildSimulation(Isolated(), 300f);
            Assert.Single(simulation.Solver.Loops);

            CoolantLoop loop = simulation.Solver.Loops[0];
            ThermalNode hotNode = simulation.Solver.GetNode(hot);
            hotNode.Temperature = 900f;

            float before = loop.Temperature;
            simulation.StepExact(20, Worlds.Shadow());

            Assert.True(loop.Temperature > before, "coolant should have absorbed heat");
            Assert.True(hotNode.Temperature < 900f, "the hot block should have given some up");
        }

        [Fact]
        public void CoolantTransportConservesEnergy()
        {
            GridBuilder builder = GridBuilder.Large();

            Dictionary<int, Vector3I> sinks = new Dictionary<int, Vector3I>();
            sinks[1] = Vector3I.Down;
            sinks[5] = Vector3I.Up;

            PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, 3, 3), -1, sinks);
            builder.Place(Catalog.HeavyArmor(), new Vector3I(1, -1, 0));
            builder.Place(Catalog.HeavyArmor(), new Vector3I(1, 1, 2));

            ThermalSimulation simulation = builder.BuildSimulation(Isolated(), 300f);
            simulation.Solver.GetNodeAt(new Vector3I(1, -1, 0)).Temperature = 900f;

            float before = simulation.Solver.TotalEnergy;
            simulation.StepExact(200, Worlds.Shadow());
            float after = simulation.Solver.TotalEnergy;

            Assert.Equal(1f, after / before, 3);
        }

        /// <summary>
        /// A loop reports what it drew and what it shed as two figures, because a loop doing its job
        /// has a net of about zero. This is the case the two-figure form exists for: a reactor at one
        /// sink and a radiator at another, where the fluid is a conduit rather than a store.
        /// </summary>
        [Fact]
        public void AWorkingLoopReportsGrossFlowNotItsNearZeroNet()
        {
            Dictionary<int, Vector3I> sinks = new Dictionary<int, Vector3I>();
            sinks[1] = Vector3I.Down;   // reactor
            sinks[5] = Vector3I.Up;     // radiator

            GridBuilder builder = GridBuilder.Large();
            List<Vector3I> cells = PipeFitter.RectangleXZ(Vector3I.Zero, 5, 5);
            PipeFitter.BuildRing(builder, cells, -1, sinks);

            builder.Place(Catalog.Reactor(), cells[1] + Vector3I.Down).Producing(200000f);
            builder.Place(Catalog.Radiator(), cells[5] + Vector3I.Up);

            ThermalSettings settings = new ThermalSettings();
            settings.EnableSolarHeat = false;
            settings.EnableFriction = false;
            settings.EnableDamage = false;

            ThermalSimulation simulation = builder.BuildSimulation(settings.Derive(), 293.15f);
            CoolantLoop loop = simulation.Solver.Loops[0];

            // Long enough for the fluid to stop warming and start simply carrying.
            simulation.StepExact(30000, Worlds.Shadow());

            Assert.True(loop.LastWattsAbsorbed > 1000f,
                "the loop reports drawing only " + loop.LastWattsAbsorbed + " W off a 200 kW reactor");
            Assert.True(loop.LastWattsRejected > 1000f,
                "the loop reports shedding only " + loop.LastWattsRejected + " W into a radiator");

            // The point of the pair: the net is small against either gross figure, so a single
            // net figure would describe this loop as idle.
            Assert.True(Math.Abs(loop.LastNetWatts) < loop.LastWattsAbsorbed * 0.1f,
                "net " + loop.LastNetWatts + " W against " + loop.LastWattsAbsorbed
                + " W absorbed: the loop has not reached balance, so this is not testing the case");
        }

        /// <summary>
        /// The two figures are the same energy the integrator applied to the fluid, so their
        /// difference has to be the change in the fluid's own heat content and nothing else.
        /// </summary>
        [Fact]
        public void TheReportedFlowAccountsForTheFluidsChangeInHeat()
        {
            Dictionary<int, Vector3I> sinks = new Dictionary<int, Vector3I>();
            sinks[1] = Vector3I.Down;

            GridBuilder builder = GridBuilder.Large();
            List<Vector3I> cells = PipeFitter.RectangleXZ(Vector3I.Zero, 3, 3);
            PipeFitter.BuildRing(builder, cells, -1, sinks);
            builder.Place(Catalog.HeavyArmor(), cells[1] + Vector3I.Down);
            BlockInstance hot = builder.Last;

            ThermalSimulation simulation = builder.BuildSimulation(Isolated(), 300f);
            CoolantLoop loop = simulation.Solver.Loops[0];
            simulation.Solver.GetNode(hot).Temperature = 900f;

            float before = loop.Temperature;
            simulation.StepExact(1, Worlds.Shadow());

            float reported = loop.LastNetWatts * simulation.Settings.StepSeconds;
            float actual = (loop.Temperature - before) * loop.ThermalMass;

            Assert.Equal(1f, reported / actual, 2);
        }

        /// <summary>An idle loop reports zero rather than whatever it last carried.</summary>
        [Fact]
        public void ALoopInBalanceWithItsSurroundingsReportsNothing()
        {
            GridBuilder builder = GridBuilder.Large();
            PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, 3, 3));

            ThermalSimulation simulation = builder.BuildSimulation(Isolated(), 300f);
            CoolantLoop loop = simulation.Solver.Loops[0];

            simulation.StepExact(10, Worlds.Shadow());

            Assert.Equal(0f, loop.LastWattsAbsorbed, 3);
            Assert.Equal(0f, loop.LastWattsRejected, 3);
        }

        [Fact]
        public void DisablingLoopsRemovesThemFromTheSolver()
        {
            ThermalSettings settings = Isolated();
            settings.EnableCoolantLoops = false;

            GridBuilder builder = GridBuilder.Large();
            PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, 3, 3));

            ThermalSimulation simulation = builder.BuildSimulation(settings);
            Assert.Empty(simulation.Solver.Loops);
        }

        [Fact]
        public void PipeAndPlateConductanceRespectTheirScalers()
        {
            GridModel grid = new GridModel(2.5f);
            BlockInstance pipe = grid.Add(Catalog.CoolantPipeStraight(), Vector3I.Zero);

            LoopThermalProperties properties = LoopThermalProperties.Default();
            float baseline = CoolantLoopBuilder.PipeConductance(grid, pipe, properties);

            properties.PipeSurfaceAreaScaler = 2f;
            float doubled = CoolantLoopBuilder.PipeConductance(grid, pipe, properties);

            Assert.Equal(baseline * 2f, doubled, 2);

            LoopThermalProperties plate = LoopThermalProperties.Default();
            Assert.True(CoolantLoopBuilder.PlateConductance(grid, plate) > 0f);
        }
    }
}
