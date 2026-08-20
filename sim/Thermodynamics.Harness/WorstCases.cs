using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// Hulls and worlds built to be expensive, and each one able to say whether it succeeded.
    ///
    /// <para>
    /// The benchmarks measured a ship in vacuum for most of this project's life, and a ship in
    /// vacuum is the cheapest case on nearly every axis: no convection, no friction, no air in the
    /// compartments, no plumbing, one grid. The report recorded convection as costing nothing
    /// because <c>AtmosphereFactor</c> was zero and the branch never ran, which is a worse failure
    /// than a wrong number — it is a confident zero.
    /// </para>
    ///
    /// <para>
    /// So every scenario here returns a <see cref="Census"/> alongside its simulation, saying what
    /// it actually built: how many rooms hold air, how many coolant loops closed, how many pumps
    /// bound, how many blocks are past their rating. <c>WorstCaseTests</c> asserts those are
    /// non-zero. A scenario that quietly builds nothing is the failure mode this file exists to
    /// prevent, and it is not hypothetical.
    /// </para>
    /// </summary>
    public static class WorstCases
    {
        /// <summary>What a scenario actually managed to build, so a benchmark can refuse to lie.</summary>
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

            public override string ToString()
            {
                return Nodes.ToString("n0") + " nodes, " + Links.ToString("n0") + " links, "
                    + RoomsWithAir + " of " + Rooms + " rooms with air, "
                    + CoolantLoops + " loops, " + HeatPumps + " pumps, "
                    + Producers.ToString("n0") + " producers";
            }
        }

        private static ThermalSettings Defaults(ThermalSettings settings)
        {
            if (settings != null) return settings;

            ThermalSettings created = new ThermalSettings();
            created.MaxSubsteps = 4096;
            created.MaxElementVisitsPerStep = 0;
            return created.Derive();
        }

        // ----------------------------------------------------------------------------------
        // Hulls
        // ----------------------------------------------------------------------------------

        /// <summary>The plain census hull, for comparison against the expensive ones.</summary>
        public static Built Hull(string shape, int size, ThermalSettings settings = null)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build(shape, size));

            return Finish(builder.BuildSimulation(Defaults(settings), 293.15f));
        }

        /// <summary>
        /// A hull with every compartment the room mapper found filled with air.
        ///
        /// Room air is the lightest thing on a ship and touches the most surface, so a small
        /// pressurised compartment is routinely the stiffest element on a grid — the solver's own
        /// substep estimate says so in a comment. No benchmark had ever put air in one.
        /// </summary>
        public static Built Pressurised(string shape, int size, ThermalSettings settings = null)
        {
            Built built = Hull(shape, size, settings);

            IList<HashSet<Vector3I>> rooms = built.Simulation.Rooms.Map.Rooms;
            for (int r = 0; r < rooms.Count; r++)
            {
                foreach (Vector3I cell in rooms[r])
                {
                    if (built.Simulation.SetRoomPressure(cell, 1f)) built.RoomsWithAir++;
                    break;
                }
            }

            return Measure(built);
        }

        /// <summary>
        /// A hull plumbed with coolant rings and heat pumps.
        ///
        /// Both read "within the noise" in every report so far, which was true and uninformative:
        /// the hulls had none. A loop is a per-loop pass over its links every substep and a pump is
        /// a device stepped every substep, so their cost is real and simply was never present.
        /// </summary>
        public static Built Plumbed(string shape, int size, int rings = 8, ThermalSettings settings = null)
        {
            GridBuilder builder = GridBuilder.Large();
            HashSet<Vector3I> cells = LoadShapes.Build(shape, size);
            builder.PlaceCensus(cells);

            // Rings are laid in clear space beside the hull rather than inside it, because a ring
            // has to be a closed loop of adjacent cells and a hull's interior is not reliably
            // shaped for one. The cost being measured is the loop's own passes, which do not
            // depend on where it sits.
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
                Vector3I origin = new Vector3I(max.X + 2, max.Y - (r * 2), max.Z + 2);
                List<Vector3I> ring = PipeFitter.RectangleXZ(origin, 6, 5);

                try
                {
                    PipeFitter.BuildRing(builder, ring);
                    placed++;
                }
                catch (Exception)
                {
                    // A ring that cannot be oriented is a harness problem, not a measurement, and
                    // the count below is what says whether enough of them landed.
                }
            }

            // Heat pumps bridge two blocks, so they go where there is structure either side.
            BlockModel pump = Catalog.HeatPump();
            for (int p = 0; p < rings; p++)
            {
                Vector3I at = new Vector3I(max.X + 2, max.Y - (p * 2), max.Z + 8);
                builder.Place(pump, at);
                builder.Place(Catalog.HeavyArmor(), at + Vector3I.Forward);
                builder.Place(Catalog.HeavyArmor(), at + Vector3I.Backward);
            }

            return Finish(builder.BuildSimulation(Defaults(settings), 293.15f));
        }

        /// <summary>
        /// A hull driven hard enough that blocks cross their rating and damage events are raised
        /// every step.
        ///
        /// Every field session so far reported zero damage events, so the path that collects
        /// overheats, allocates the event and hands it to the host has never been in a
        /// measurement. It is on the apply pass, which is the second hottest loop in the solver.
        /// </summary>
        public static Built Burning(string shape, int size, ThermalSettings settings = null)
        {
            Built built = Hull(shape, size, settings);
            built.Producers = Census.DriveCensus(built.Simulation, Census.ProducerWatts * 20f);
            return built;
        }

        // ----------------------------------------------------------------------------------
        // Fleets
        // ----------------------------------------------------------------------------------

        /// <summary>
        /// Many grids instead of one, which is what a world is and what no benchmark has ever
        /// been.
        ///
        /// The largest finding of this project — every grid in a world stepping on the same frame
        /// — was invisible to every benchmark here and came from a telemetry dump, because the
        /// harness runs a single grid. A fleet also pays each grid's fixed per-step cost
        /// separately: state sync, the stability estimate and the mass floor are each a pass over
        /// that grid's nodes, and two hundred small grids pay two hundred of them.
        /// </summary>
        public static List<Built> Fleet(string shape, int totalBlocks, int grids,
            ThermalSettings settings = null)
        {
            List<Built> fleet = new List<Built>();
            if (grids < 1) grids = 1;

            int each = Math.Max(8, totalBlocks / grids);
            for (int i = 0; i < grids; i++) fleet.Add(Hull(shape, each, settings));

            return fleet;
        }

        /// <summary>Steps a whole fleet once, as the scheduler does.</summary>
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

        // ----------------------------------------------------------------------------------

        private static Built Finish(ThermalSimulation simulation)
        {
            simulation.RebuildAll();
            return Measure(new Built { Simulation = simulation });
        }

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
