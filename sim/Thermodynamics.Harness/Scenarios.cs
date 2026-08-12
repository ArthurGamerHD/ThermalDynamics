using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    public class ScenarioResult
    {
        public string Name;
        public string Summary;
        public ScenarioRunner Runner;
        public string Csv;
    }

    /// <summary>
    /// The scenario library. Each one is a self-contained experiment that answers a question
    /// about the model, and each is cheap enough to run from a test.
    /// </summary>
    public static class Scenarios
    {
        public static readonly string[] Names = new string[]
        {
            "vacuum-soak",
            "reactor",
            "atmosphere",
            "daynight",
            "reentry",
            "coolant",
            "sealed-room",
            "meltdown",
            "perf",
        };

        public static ScenarioResult Run(string name)
        {
            switch (name)
            {
                case "vacuum-soak": return VacuumSoak();
                case "reactor": return Reactor();
                case "atmosphere": return Atmosphere();
                case "daynight": return DayNight();
                case "reentry": return Reentry();
                case "coolant": return Coolant();
                case "sealed-room": return SealedRoom();
                case "meltdown": return Meltdown();
                case "perf": return Performance();
                default:
                    throw new ArgumentException("Unknown scenario: " + name);
            }
        }

        /// <summary>A single hot block radiating into empty space.</summary>
        public static ScenarioResult VacuumSoak()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.HeavyArmor(), Vector3I.Zero);

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 800f);

            ScenarioRunner runner = new ScenarioRunner(simulation);
            runner.Environment = t => Worlds.Shadow();
            runner.Track("block", builder.Placed[0]);
            runner.Run(3600f, 300f);

            return Result("vacuum-soak", runner,
                "One heavy armour block at 800 K radiating into shadow. Ends at "
                + C(runner.Final.HottestTemperature) + " after one hour.");
        }

        /// <summary>A reactor buried in armour, with and without a path to the surface.</summary>
        public static ScenarioResult Reactor()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), new Vector3I(-2, -2, -2), new Vector3I(3, 3, 3));

            // swap the centre block for a reactor
            BlockInstance centre = builder.Grid.GetAtCell(Vector3I.Zero);
            builder.Grid.Remove(centre);
            builder.Placed.Remove(centre);
            builder.Place(Catalog.Reactor(), Vector3I.Zero).Producing(15f * ThermalConstants.MegawattsToWatts);

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);

            ScenarioRunner runner = new ScenarioRunner(simulation);
            runner.Environment = t => Worlds.Shadow();
            runner.Track("reactor", builder.Last);
            runner.Track("hull", builder.Grid.GetAtCell(new Vector3I(2, 0, 0)));
            runner.Run(7200f, 600f);

            return Result("reactor", runner,
                "15 MW reactor at the centre of a 5x5x5 light armour cube in shadow. Reactor reaches "
                + C(runner.Final.Tracked["reactor"]) + ", hull "
                + C(runner.Final.Tracked["hull"]) + ".");
        }

        /// <summary>The same reactor cube, on a planet surface, where convection dominates.</summary>
        public static ScenarioResult Atmosphere()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), new Vector3I(-2, -2, -2), new Vector3I(3, 3, 3));

            BlockInstance centre = builder.Grid.GetAtCell(Vector3I.Zero);
            builder.Grid.Remove(centre);
            builder.Placed.Remove(centre);
            builder.Place(Catalog.Reactor(), Vector3I.Zero).Producing(15f * ThermalConstants.MegawattsToWatts);

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);

            ScenarioRunner runner = new ScenarioRunner(simulation);
            runner.Environment = t => Worlds.PlanetSurface(1f, 0.25f);
            runner.Track("reactor", builder.Last);
            runner.Track("hull", builder.Grid.GetAtCell(new Vector3I(2, 0, 0)));
            runner.Run(7200f, 600f);

            return Result("atmosphere", runner,
                "The same reactor cube at sea level. Reactor settles at "
                + C(runner.Final.Tracked["reactor"]) + " against "
                + C(runner.Final.AmbientTemperature) + " ambient.");
        }

        /// <summary>Bare hull through a full day, tracking the solar swing.</summary>
        public static ScenarioResult DayNight()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(3, 1, 3));

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 250f);

            ScenarioRunner runner = new ScenarioRunner(simulation);
            float dayLength = 7200f;
            runner.Environment = t => Worlds.PlanetSurface(0f, (t / dayLength) % 1f);
            runner.Track("plate", builder.Placed[0]);
            runner.Run(dayLength * 2f, dayLength / 24f);

            float min = float.MaxValue;
            float max = float.MinValue;
            for (int i = 0; i < runner.Samples.Count; i++)
            {
                float value = runner.Samples[i].Tracked["plate"];
                if (value < min) min = value;
                if (value > max) max = value;
            }

            return Result("daynight", runner,
                "Airless 3x1x3 plate over two rotations. Swings between "
                + C(min) + " and " + C(max) + ".");
        }

        /// <summary>Atmospheric entry: fast, dense air, leading face into the flow.</summary>
        public static ScenarioResult Reentry()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.HeavyArmor(), Vector3I.Zero, new Vector3I(3, 3, 1));

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);

            ScenarioRunner runner = new ScenarioRunner(simulation);
            runner.Environment = t => Worlds.Flight(0.8f, 300f);
            runner.Track("nose", builder.Placed[0]);
            runner.Run(600f, 60f);

            return Result("reentry",
                runner,
                "3x3 heavy armour face into 300 m/s of 0.8 density air. Nose reaches "
                + C(runner.Final.Tracked["nose"]) + ".");
        }

        /// <summary>
        /// A reactor bolted to a coolant ring that also runs past a radiator: the loop should
        /// move heat out of the reactor and into a block that can shed it.
        /// </summary>
        public static ScenarioResult Coolant()
        {
            GridBuilder builder = GridBuilder.Large();

            // A 4x2 ring in the XZ plane, pumped, with sinks on the two long straights.
            List<Vector3I> ring = PipeFitter.RectangleXZ(Vector3I.Zero, 4, 2);
            Dictionary<int, Vector3I> sinks = new Dictionary<int, Vector3I>();
            sinks[1] = Vector3I.Down;   // toward the reactor
            sinks[2] = Vector3I.Up;     // toward the radiator

            List<BlockInstance> pipes = PipeFitter.BuildRing(builder, ring, 5, sinks);

            builder.Place(Catalog.Reactor(), new Vector3I(1, -1, 0))
                   .Producing(5f * ThermalConstants.MegawattsToWatts);
            BlockInstance reactor = builder.Last;

            builder.Place(Catalog.LightArmor(), new Vector3I(2, 1, 0));
            BlockInstance sink = builder.Last;

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);

            ScenarioRunner runner = new ScenarioRunner(simulation);
            runner.Environment = t => Worlds.Shadow();
            runner.Track("reactor", reactor);
            runner.Track("sink-block", sink);
            if (simulation.Solver.Loops.Count > 0)
            {
                runner.TrackLoop("coolant", simulation.Solver.Loops[0]);
            }
            runner.Run(3600f, 300f);

            string found = simulation.Solver.Loops.Count > 0
                ? simulation.Solver.Loops.Count + " loop of " + simulation.Solver.Loops[0].PipeCount + " pipes, "
                  + simulation.Solver.Loops[0].Links.Count + " links"
                : "NO CLOSED LOOP (" + pipes.Count + " pipes placed)";

            return Result("coolant", runner,
                "Pumped coolant ring between a 5 MW reactor and a plain armour block: " + found
                + ". Reactor " + C(runner.Final.Tracked["reactor"])
                + ", coolant " + (runner.Final.Tracked.ContainsKey("coolant") ? C(runner.Final.Tracked["coolant"]) : "n/a")
                + ", sink block " + C(runner.Final.Tracked["sink-block"]) + ".");
        }

        /// <summary>
        /// A reactor sealed inside a shell it is bolted to. The reactor must have no exposed
        /// faces, so its only way out is conduction through the hull.
        /// </summary>
        public static ScenarioResult SealedRoom()
        {
            GridBuilder builder = GridBuilder.Large();

            // 3x3x3 shell with a single hollow cell at the centre.
            builder.Shell(Catalog.LightArmor(), new Vector3I(-1, -1, -1), new Vector3I(2, 2, 2));
            builder.Place(Catalog.Reactor(), Vector3I.Zero).Producing(2f * ThermalConstants.MegawattsToWatts);
            BlockInstance reactor = builder.Last;

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);

            ThermalNode interior = simulation.Solver.GetNode(reactor);
            ThermalNode shell = simulation.Solver.GetNodeAt(new Vector3I(1, 0, 0));

            ScenarioRunner runner = new ScenarioRunner(simulation);
            runner.Environment = t => Worlds.Shadow();
            runner.Track("interior-reactor", reactor);
            runner.Track("shell", shell.Block);
            runner.Run(3600f, 600f);

            return Result("sealed-room", runner,
                "2 MW reactor sealed inside a 3x3x3 hull. Reactor exposed faces: "
                + interior.TotalExposedFaces + " (expected 0), conduction links: "
                + interior.LinkCount + ", shell exposed faces: "
                + shell.TotalExposedFaces + ". Reactor "
                + C(runner.Final.Tracked["interior-reactor"]) + ", shell "
                + C(runner.Final.Tracked["shell"]) + ".");
        }

        /// <summary>An overpowered reactor with nowhere to dump heat, to exercise damage.</summary>
        public static ScenarioResult Meltdown()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.Reactor(), Vector3I.Zero).Producing(300f * ThermalConstants.MegawattsToWatts);

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);

            float totalDamage = 0f;
            float timeToCritical = -1f;

            ScenarioRunner runner = new ScenarioRunner(simulation);
            runner.Environment = t => Worlds.Shadow();
            runner.Track("reactor", builder.Placed[0]);
            runner.AfterStep = s =>
            {
                for (int d = 0; d < s.Overheats.Count; d++)
                {
                    totalDamage += s.Overheats[d].Damage;
                }
                if (timeToCritical < 0f && s.Overheats.Count > 0)
                {
                    timeToCritical = runner.ElapsedSeconds;
                }
            };

            runner.Run(600f, 60f);

            return Result("meltdown", runner,
                "300 MW into a single unshielded reactor for 10 minutes: "
                + C(simulation.Solver.HottestNode().Temperature) + ", critical after "
                + (timeToCritical < 0f ? "never" : timeToCritical.ToString("n1") + " s")
                + ", cumulative damage " + totalDamage.ToString("n0") + ".");
        }

        /// <summary>Throughput on a grid large enough to matter.</summary>
        public static ScenarioResult Performance()
        {
            const int side = 20;

            Stopwatch build = Stopwatch.StartNew();
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(side, side, side));
            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);
            build.Stop();

            int blocks = simulation.Solver.Nodes.Count;
            int links = simulation.Solver.Links.Count;

            EnvironmentSample environment = Worlds.Shadow();
            EnvironmentState state = EnvironmentSolver.Solve(simulation.Settings, simulation.Planet, environment);

            // warm up, then measure
            for (int i = 0; i < 10; i++) simulation.Solver.Step(simulation.Settings.StepSeconds, state);

            Stopwatch run = Stopwatch.StartNew();
            const int measured = 200;
            for (int i = 0; i < measured; i++)
            {
                simulation.Solver.Step(simulation.Settings.StepSeconds, state);
            }
            run.Stop();

            double perStepMs = run.Elapsed.TotalMilliseconds / measured;

            // How much the coalesced room mapping saves during construction. The original
            // restarted the whole flood fill on every single block add or remove.
            const int weldCount = 400;

            Stopwatch coalesced = Stopwatch.StartNew();
            GridModel coalescedGrid = new GridModel(Catalog.LargeGridSize);
            ThermalSimulation coalescedSim = new ThermalSimulation(new ThermalSettings(), coalescedGrid);
            for (int i = 0; i < weldCount; i++)
            {
                coalescedSim.AddBlock(new BlockInstance(Catalog.LightArmor(), CellFor(i), BlockOrientation.Identity));
            }
            coalescedSim.Update(1f / 60f, Worlds.Shadow());
            while (coalescedSim.Rooms.HasWorkPending) coalescedSim.Update(1f / 60f, Worlds.Shadow());
            coalesced.Stop();

            Stopwatch perBlock = Stopwatch.StartNew();
            GridModel eagerGrid = new GridModel(Catalog.LargeGridSize);
            ThermalSimulation eagerSim = new ThermalSimulation(new ThermalSettings(), eagerGrid);
            for (int i = 0; i < weldCount; i++)
            {
                eagerSim.AddBlock(new BlockInstance(Catalog.LightArmor(), CellFor(i), BlockOrientation.Identity));
                eagerSim.Rooms.RequestRestart(eagerGrid);
                eagerSim.Rooms.RunToCompletion();
            }
            perBlock.Stop();

            StringBuilder sb = new StringBuilder();
            sb.Append(blocks.ToString("n0")).Append(" blocks, ")
              .Append(links.ToString("n0")).Append(" conduction links. Build+map ")
              .Append(build.ElapsedMilliseconds).Append(" ms. Solver ")
              .Append(perStepMs.ToString("n4")).Append(" ms/step (")
              .Append((perStepMs * simulation.Settings.StepsPerSecond).ToString("n3"))
              .Append(" ms per simulated second). Welding ").Append(weldCount)
              .Append(" blocks: ").Append(coalesced.ElapsedMilliseconds)
              .Append(" ms coalesced vs ").Append(perBlock.ElapsedMilliseconds)
              .Append(" ms remapping per block.");

            ScenarioResult result = new ScenarioResult();
            result.Name = "perf";
            result.Summary = sb.ToString();
            result.Csv = string.Empty;
            return result;
        }

        /// <summary>Packs a sequential index into a compact 10x10 footprint.</summary>
        private static Vector3I CellFor(int index)
        {
            return new Vector3I(index % 10, (index / 10) % 10, index / 100);
        }

        private static ScenarioResult Result(string name, ScenarioRunner runner, string summary)
        {
            ScenarioResult result = new ScenarioResult();
            result.Name = name;
            result.Runner = runner;
            result.Summary = summary;
            result.Csv = runner.ToCsv();
            return result;
        }

        private static string C(float kelvin)
        {
            return ThermalConstants.KelvinToCelsius(kelvin).ToString("n1") + " C";
        }
    }
}
