using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    public static class WorstCases
    {
        public class Built
        {
            public ThermalSimulation Simulation;

            public int Nodes;
            public int Links;
            public int Rooms;
            public int RoomsWithAir;
            public int CoolantLoops;
            public int HeatPumps;
            public int Producers;

/// <summary>ToString operation.</summary>
            public override string ToString()
            {
                return Nodes.ToString("n0") + " nodes, " + Links.ToString("n0") + " links, "
                    + RoomsWithAir + " of " + Rooms + " rooms with air, "
                    + CoolantLoops + " loops, " + HeatPumps + " pumps, "
                    + Producers.ToString("n0") + " producers";
            }
        }

/// <summary>Defaults operation.</summary>
        private static ThermalSettings Defaults(ThermalSettings settings)
        {
            if (settings != null) return settings;

/// <summary>ThermalSettings operation.</summary>
            ThermalSettings created = new ThermalSettings();
            created.MaxSubsteps = 4096;
            created.MaxElementVisitsPerStep = 0;
            return created.Derive();
        }


/// <summary>Hull operation.</summary>
        public static Built Hull(string shape, int size, ThermalSettings settings = null)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build(shape, size));

            return Finish(builder.BuildSimulation(Defaults(settings), 293.15f));
        }

/// <summary>Pressurised operation.</summary>
        public static Built Pressurised(string shape, int size, ThermalSettings settings = null)
        {
/// <summary>Hull operation.</summary>
            Built built = Hull(shape, size, settings);

            RoomMap rooms = built.Simulation.Rooms.Map;
            for (int r = 0; r < rooms.RoomCount; r++)
            {
                foreach (Vector3I cell in rooms.CellsOf(r))
                {
                    if (built.Simulation.SetRoomPressure(cell, 1f)) built.RoomsWithAir++;
                    break;
                }
            }

/// <summary>Measure operation.</summary>
            return Measure(built);
        }

/// <summary>Plumbed operation.</summary>
        public static Built Plumbed(string shape, int size, int rings = 8, ThermalSettings settings = null)
        {
            GridBuilder builder = GridBuilder.Large();
            HashSet<Vector3I> cells = LoadShapes.Build(shape, size);
            builder.PlaceCensus(cells);

            Vector3I max = Vector3I.Zero;
            foreach (Vector3I cell in cells)
            {
                if (cell.X > max.X) max.X = cell.X;
                if (cell.Y > max.Y) max.Y = cell.Y;
                if (cell.Z > max.Z) max.Z = cell.Z;
            }

            int placed = 0;
            for (int r = 0; r < rings; r++)
            {
/// <summary>Vector3I operation.</summary>
                Vector3I origin = new Vector3I(max.X + 2, max.Y - (r * 2), max.Z + 2);
                List<Vector3I> ring = PipeFitter.RectangleXZ(origin, 6, 5);

                try
                {
                    PipeFitter.BuildRing(builder, ring);
                    placed++;
                }
                catch (Exception)
                {
                }
            }

            BlockModel pump = Catalog.HeatPump();
            for (int p = 0; p < rings; p++)
            {
/// <summary>Vector3I operation.</summary>
                Vector3I at = new Vector3I(max.X + 2, max.Y - (p * 2), max.Z + 8);
                builder.Place(pump, at);
                builder.Place(Catalog.HeavyArmor(), at + Vector3I.Forward);
                builder.Place(Catalog.HeavyArmor(), at + Vector3I.Backward);
            }

            return Finish(builder.BuildSimulation(Defaults(settings), 293.15f));
        }

/// <summary>HeatedRings operation.</summary>
        public static Built HeatedRings(int rings = 1, float wasteWatts = 75000f,
            ThermalSettings settings = null, float parcelsPerSecond = 0f)
        {
            if (rings < 1) rings = 1;

            GridBuilder builder = GridBuilder.Large();

            for (int r = 0; r < rings; r++)
            {
                List<Vector3I> cells = PipeFitter.RectangleXZ(new Vector3I(0, r * 3, 0), 3, 3);

                Dictionary<int, Vector3I> sinks = new Dictionary<int, Vector3I>();
                sinks[2] = Vector3I.Down;

                PipeFitter.BuildRing(builder, cells, -1, sinks);
                builder.Place(Catalog.Reactor(), cells[2] + Vector3I.Down).Wasting(wasteWatts);
            }

/// <summary>Finish operation.</summary>
            Built built = Finish(builder.BuildSimulation(Defaults(settings), 300f));
            built.Producers = rings;

            if (parcelsPerSecond > 0f)
            {
                IList<CoolantLoop> loops = built.Simulation.Solver.Loops;
                for (int l = 0; l < loops.Count; l++)
                {
                    CoolantLoop loop = loops[l];
                    loop.Properties.LargeGridFlowRate = parcelsPerSecond * loop.ParcelLengthMetres;
                    loop.Properties.SmallGridFlowRate = loop.Properties.LargeGridFlowRate;
                    loop.RefreshFlow();
                }
            }

            return built;
        }

/// <summary>Burning operation.</summary>
        public static Built Burning(string shape, int size, ThermalSettings settings = null)
        {
/// <summary>Hull operation.</summary>
            Built built = Hull(shape, size, settings);
            built.Producers = Census.DriveCensus(built.Simulation, Census.ProducerWatts * 20f);
            return built;
        }

/// <summary>Scorched operation.</summary>
        public static Built Scorched(string shape, int size, ThermalSettings settings = null)
        {
/// <summary>Defaults operation.</summary>
            ThermalSettings held = settings ?? Defaults(null);
            held.EnableEnvironment = false;
            held.EnableSolarHeat = false;
            held.EnableFriction = false;
            held.EnableWasteHeat = false;
            held.Derive();

/// <summary>Hull operation.</summary>
            Built built = Hull(shape, size, held);

            built.Simulation.Solver.SetAllTemperatures(4000f);
            built.Producers = built.Nodes;
            return built;
        }


/// <summary>Fleet operation.</summary>
        public static List<Built> Fleet(string shape, int totalBlocks, int grids,
            ThermalSettings settings = null)
        {
/// <summary>List operation.</summary>
            List<Built> fleet = new List<Built>();
            if (grids < 1) grids = 1;

            int each = Math.Max(8, totalBlocks / grids);
            for (int i = 0; i < grids; i++) fleet.Add(Hull(shape, each, settings));

            return fleet;
        }

/// <summary>StepFleet operation.</summary>
        public static void StepFleet(IList<Built> fleet, EnvironmentSample sample, int steps)
        {
            for (int s = 0; s < steps; s++)
            {
                for (int i = 0; i < fleet.Count; i++)
                {
                    fleet[i].Simulation.StepExact(1, sample);
                }
            }
        }


/// <summary>Finish operation.</summary>
        private static Built Finish(ThermalSimulation simulation)
        {
            simulation.RebuildAll();
/// <summary>Measure operation.</summary>
            return Measure(new Built { Simulation = simulation });
        }

/// <summary>Measure operation.</summary>
        private static Built Measure(Built built)
        {
            ThermalSimulation simulation = built.Simulation;

            built.Nodes = simulation.Solver.Nodes.Count;
            built.Links = simulation.Solver.Links.Count;
            built.Rooms = simulation.Rooms.Map.RoomCount;
            built.CoolantLoops = simulation.Solver.Loops.Count;
            built.HeatPumps = simulation.Solver.HeatPumps.Count;

            int air = 0;
            IList<RoomAirNode> rooms = simulation.Solver.RoomAir;
            for (int i = 0; i < rooms.Count; i++)
            {
                if (rooms[i].HasAir) air++;
            }

            if (air > built.RoomsWithAir) built.RoomsWithAir = air;
            return built;
        }
    }
}
