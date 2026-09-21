using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    public class MultiCellAndDamageTests
    {
/// <summary>Fragile operation.</summary>
        private static BlockThermalProperties Fragile()
        {
            BlockThermalProperties t = Catalog.DefaultThermal();
            t.CriticalTemperature = 400f;
            t.OverheatDamagePerKelvin = 1f;
            return t;
        }

        [Fact]
/// <summary>ABlockIsIdentifiedByItsMinimumCell operation.</summary>
        public void ABlockIsIdentifiedByItsMinimumCell()
        {
/// <summary>GridModel operation.</summary>
            GridModel grid = new GridModel(2.5f);
            BlockModel model = BlockModel.Solid("BigBlock", new Vector3I(3, 3, 3), 5000f, Catalog.DefaultThermal());

/// <summary>Vector3I operation.</summary>
            Vector3I min = new Vector3I(4, 0, 0);
            BlockInstance block = grid.Add(model, min);

            Assert.Equal(min, block.Position);
            Assert.Equal(GridMath.Key(min), block.Key);

            Assert.Same(block, grid.GetAtCell(min));
            Assert.Same(block, grid.GetAtCell(min + new Vector3I(1, 1, 1)));
            Assert.Same(block, grid.GetAtCell(min + new Vector3I(2, 2, 2)));
        }

        [Fact]
/// <summary>AnOverheatEventNamesTheBlockByItsMinimumCell operation.</summary>
        public void AnOverheatEventNamesTheBlockByItsMinimumCell()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            BlockModel big = BlockModel.Solid("BigBlock", new Vector3I(3, 3, 3), 5000f, Fragile());
            builder.Place(big, new Vector3I(2, 0, 0));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 900f);
            simulation.RebuildAll();
            simulation.StepExact(1, Worlds.Shadow());

            Assert.Single(simulation.Overheats);
            Assert.Equal(new Vector3I(2, 0, 0), simulation.Overheats[0].Block.Position);
        }

        [Fact]
/// <summary>OverheatsFromEveryStepOfAnUpdateReachTheHost operation.</summary>
        public void OverheatsFromEveryStepOfAnUpdateReachTheHost()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(BlockModel.Solid("Fragile", Vector3I.One, 500f, Fragile()), Vector3I.Zero);

            ThermalSimulation simulation = builder.BuildSimulation(settings, 900f);
            simulation.RebuildAll();

            simulation.StepExact(5, Worlds.Shadow());

            Assert.Equal(5, simulation.Overheats.Count);
        }

        [Fact]
/// <summary>EachUpdateReportsOnlyItsOwnOverheats operation.</summary>
        public void EachUpdateReportsOnlyItsOwnOverheats()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(BlockModel.Solid("Fragile", Vector3I.One, 500f, Fragile()), Vector3I.Zero);

            ThermalSimulation simulation = builder.BuildSimulation(settings, 900f);
            simulation.RebuildAll();

            simulation.StepExact(3, Worlds.Shadow());
            Assert.Equal(3, simulation.Overheats.Count);

            simulation.StepExact(2, Worlds.Shadow());
            Assert.Equal(2, simulation.Overheats.Count);
        }

        [Fact]
/// <summary>ACoolBlockProducesNoOverheats operation.</summary>
        public void ACoolBlockProducesNoOverheats()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);
            simulation.RebuildAll();
            simulation.StepExact(10, Worlds.Shadow());

            Assert.Empty(simulation.Overheats);
        }

        [Fact]
/// <summary>RefreshingSealingLeavesTheConductionGraphAlone operation.</summary>
        public void RefreshingSealingLeavesTheConductionGraphAlone()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(3, 3, 3));

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);
            simulation.RebuildAll();

            int links = simulation.Solver.Links.Count;
            float conductance = simulation.Solver.Links[0].Conductance;

            BlockInstance door = builder.Placed[0];
            door.IsSealedByDoorState = false;
            simulation.RefreshBlockSealing(door);

            simulation.Update(1f / 6f, Worlds.Shadow());

            Assert.Equal(links, simulation.Solver.Links.Count);
            Assert.Equal(conductance, simulation.Solver.Links[0].Conductance, 4);
        }

        [Fact]
/// <summary>RefreshingSealingStillRemapsTheRooms operation.</summary>
        public void RefreshingSealingStillRemapsTheRooms()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(3, 3, 3));

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);
            simulation.RebuildAll();

            Assert.False(simulation.Rooms.HasWorkPending);

            simulation.RefreshBlockSealing(builder.Placed[0]);

            Assert.True(simulation.Rooms.HasWorkPending);
        }
    }
}
