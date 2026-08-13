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
            "radiator",
            "airlock",
            "coolant-failure",
            "welding",
            "stiff",
            "units",
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
                case "radiator": return Radiator();
                case "airlock": return Airlock();
                case "coolant-failure": return CoolantFailure();
                case "welding": return Welding();
                case "stiff": return Stiff();
                case "units": return Units();
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


        /// <summary>
        /// Do radiators earn their place? The same hull and the same waste heat, with and
        /// without panels standing clear of it.
        ///
        /// Clearance is the point: a panel bolted flat against the hull covers as much radiating
        /// area as it adds, which is a real build mistake and one this scenario can show.
        /// </summary>
        public static ScenarioResult Radiator()
        {
            float bare = ReactorHull(RadiatorPlacement.None);
            float flush = ReactorHull(RadiatorPlacement.Flush);
            float clear = ReactorHull(RadiatorPlacement.Clear);

            GridBuilder builder = HullBuilder(RadiatorPlacement.Clear);
            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);

            ScenarioRunner runner = new ScenarioRunner(simulation);
            runner.Environment = t => Worlds.Shadow();
            runner.Track("reactor", simulation.Grid.GetAtCell(new Vector3I(1, 1, 1)));
            runner.Run(3600f, 300f);

            return Result("radiator", runner,
                "2 MW into a 3x3x3 hull in shadow. Reactor settles at " + C(bare)
                + " bare, " + C(flush) + " with panels bolted flat against the hull, and "
                + C(clear) + " with panels standing clear on booms.");
        }

        private enum RadiatorPlacement
        {
            None,
            Flush,
            Clear
        }

        private static GridBuilder HullBuilder(RadiatorPlacement placement)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(3, 3, 3));

            BlockInstance centre = builder.Grid.GetAtCell(new Vector3I(1, 1, 1));
            builder.Grid.Remove(centre);
            builder.Placed.Remove(centre);
            builder.Place(Catalog.Reactor(), new Vector3I(1, 1, 1))
                   .Producing(2f * ThermalConstants.MegawattsToWatts);

            if (placement == RadiatorPlacement.Flush)
            {
                // Bolted straight onto the hull's faces.
                builder.Place(Catalog.Radiator(), new Vector3I(-1, 0, 0));
                builder.Place(Catalog.Radiator(), new Vector3I(3, 0, 0));
            }
            else if (placement == RadiatorPlacement.Clear)
            {
                // Held off the hull by a single armour block, so both sides of the panel see space.
                builder.Place(Catalog.LightArmor(), new Vector3I(-1, 1, 1));
                builder.Place(Catalog.Radiator(), new Vector3I(-2, 0, 0));
                builder.Place(Catalog.LightArmor(), new Vector3I(3, 1, 1));
                builder.Place(Catalog.Radiator(), new Vector3I(4, 0, 0));
            }

            return builder;
        }

        private static float ReactorHull(RadiatorPlacement placement)
        {
            GridBuilder builder = HullBuilder(placement);

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);
            ScenarioRunner runner = new ScenarioRunner(simulation);
            runner.Environment = t => Worlds.Shadow();
            runner.Run(7200f, 3600f);

            return simulation.Solver.GetNodeAt(new Vector3I(1, 1, 1)).Temperature;
        }

        /// <summary>
        /// A sealed room with a door in it. The door opens; the interior should stop being
        /// interior and start radiating.
        ///
        /// This is the path a busy airlock takes hundreds of times an hour, and the one that
        /// used to force a full topology rebuild per cycle.
        /// </summary>
        public static ScenarioResult Airlock()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(5, 5, 5));

            // Replace one shell block with a door that starts closed.
            BlockInstance wall = builder.Grid.GetAtCell(new Vector3I(2, 2, 0));
            builder.Grid.Remove(wall);
            builder.Placed.Remove(wall);

            BlockModel doorModel = BlockModel.Solid("Door", Vector3I.One, 400f, Catalog.DefaultThermal());
            builder.Place(doorModel, new Vector3I(2, 2, 0));
            BlockInstance door = builder.Last;

            // Standing on the floor of the room, not floating in the middle of it: a block with
            // neither neighbours nor exposure has no way at all to shed heat, and no such block
            // can exist on a real grid.
            builder.Place(Catalog.Reactor(), new Vector3I(2, 1, 2))
                   .Producing(1f * ThermalConstants.MegawattsToWatts);

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);
            ThermalNode interior = simulation.Solver.GetNodeAt(new Vector3I(2, 1, 2));

            ScenarioRunner runner = new ScenarioRunner(simulation);
            runner.Environment = t => Worlds.Shadow();
            runner.Track("interior", simulation.Grid.GetAtCell(new Vector3I(2, 1, 2)));
            runner.Run(900f, 300f);

            int sealedFaces = interior.TotalExposedFaces;
            float sealedTemperature = interior.Temperature;

            door.IsSealedByDoorState = false;
            simulation.RefreshBlockSealing(door);
            simulation.Rooms.RunToCompletion();
            simulation.Solver.RefreshExposure(simulation.Rooms.Map);

            runner.Run(900f, 300f);

            return Result("airlock", runner,
                "A 1 MW reactor inside a sealed 5x5x5 shell reaches " + C(sealedTemperature)
                + " with " + sealedFaces + " exposed faces. Opening the door leaves it with "
                + interior.TotalExposedFaces + " and it ends at " + C(interior.Temperature) + ".");
        }

        /// <summary>
        /// A cooled reactor, then the pump is destroyed. The ring stops being a loop and the
        /// reactor is on its own — the failure mode a coolant system exists to have.
        /// </summary>
        public static ScenarioResult CoolantFailure()
        {
            GridBuilder builder = GridBuilder.Large();

            Dictionary<int, Vector3I> sinks = new Dictionary<int, Vector3I>();
            sinks[1] = Vector3I.Down;

            List<BlockInstance> ring = PipeFitter.BuildRing(
                builder, PipeFitter.RectangleXZ(Vector3I.Zero, 4, 3), -1, sinks);

            builder.Place(Catalog.Reactor(), new Vector3I(1, -1, 0))
                   .Producing(5f * ThermalConstants.MegawattsToWatts);
            BlockInstance reactor = builder.Last;

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);

            ScenarioRunner runner = new ScenarioRunner(simulation);
            runner.Environment = t => Worlds.Shadow();
            runner.Track("reactor", reactor);
            runner.Run(1800f, 300f);

            float cooled = simulation.Solver.GetNode(reactor).Temperature;
            int loopsBefore = simulation.Solver.Loops.Count;

            // Take out the pump: whichever ring block declares itself one.
            BlockInstance pump = null;
            for (int i = 0; i < ring.Count; i++)
            {
                if (ring[i].Model.Coolant != null && ring[i].Model.Coolant.IsPump) pump = ring[i];
            }
            if (pump != null) simulation.RemoveBlock(pump);

            simulation.Update(1f / 6f, Worlds.Shadow());
            runner.Run(1800f, 300f);

            return Result("coolant-failure", runner,
                "5 MW reactor on a pumped ring holds at " + C(cooled) + " (" + loopsBefore
                + " loop). With the pump destroyed the ring stops circulating — "
                + simulation.Solver.Loops.Count + " loops — and the reactor ends at "
                + C(simulation.Solver.GetNode(reactor).Temperature) + ".");
        }

        /// <summary>
        /// A block welded up from a skeleton. Thermal mass tracks build progress, so the same
        /// heat into a tenth-welded block moves it ten times as far.
        ///
        /// Mass is the one input the game raises no event for, so the adapter sweeps it — this
        /// is what that sweep is protecting.
        /// </summary>
        public static ScenarioResult Welding()
        {
            // Short windows on purpose: at the shipped clock both masses reach the same
            // equilibrium within a minute, and it is the approach that differs, not the
            // destination.
            float skeleton = WeldedRise(0.1f, 5f);
            float finished = WeldedRise(1f, 5f);

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.HeavyArmor(), Vector3I.Zero);
            builder.Place(Catalog.Reactor(), new Vector3I(0, 0, 1))
                   .Producing(1f * ThermalConstants.MegawattsToWatts);

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);
            ThermalNode node = simulation.Solver.GetNode(builder.Placed[0]);
            node.Block.Mass *= 0.1f;
            node.RefreshThermalMass();

            ScenarioRunner runner = new ScenarioRunner(simulation);
            runner.Environment = t => Worlds.Shadow();
            runner.Track("skeleton", builder.Placed[0]);
            runner.Run(5f, 1f);

            // Finish welding it mid-run: the same block, ten times the thermal mass.
            node.Block.Mass *= 10f;
            node.RefreshThermalMass();
            runner.Run(55f, 5f);

            return Result("welding", runner,
                "Five seconds of a 1 MW reactor next to one heavy armour block. At a tenth of its "
                + "mass the block reaches " + C(skeleton) + "; fully welded it is still at "
                + C(finished) + ". The tracked run welds it up after those five seconds and "
                + "settles at " + C(runner.Final.Tracked["skeleton"]) + ".");
        }

        private static float WeldedRise(float massFraction, float seconds)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.HeavyArmor(), Vector3I.Zero);
            builder.Place(Catalog.Reactor(), new Vector3I(0, 0, 1))
                   .Producing(1f * ThermalConstants.MegawattsToWatts);

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);

            ThermalNode node = simulation.Solver.GetNode(builder.Placed[0]);
            node.Block.Mass *= massFraction;
            node.RefreshThermalMass();

            ScenarioRunner runner = new ScenarioRunner(simulation);
            runner.Environment = t => Worlds.Shadow();
            runner.Run(seconds, seconds);

            return node.Temperature;
        }

        /// <summary>
        /// The stiff case: a very light block bolted to a very heavy one. Coupling per unit
        /// capacity is what forces the solver to substep, and this is the shape that maximises
        /// it — the measurement behind whether an implicit solver is ever needed.
        /// </summary>
        public static ScenarioResult Stiff()
        {
            StringBuilder report = new StringBuilder();
            ScenarioRunner last = null;

            int[] frequencies = new int[] { 1, 4, 16 };
            for (int f = 0; f < frequencies.Length; f++)
            {
                ThermalSettings settings = new ThermalSettings();
                settings.Frequency = frequencies[f];
                settings.Derive();

                GridBuilder builder = GridBuilder.Large();
                builder.Fill(Catalog.HeavyArmor(), Vector3I.Zero, new Vector3I(3, 3, 3));

                BlockThermalProperties light = Catalog.DefaultThermal();
                BlockModel feather = BlockModel.Solid("Interior", Vector3I.One, 20f, light);
                builder.Place(feather, new Vector3I(3, 0, 0));

                ThermalSimulation simulation = builder.BuildSimulation(settings, 900f);

                ScenarioRunner runner = new ScenarioRunner(simulation);
                runner.Environment = t => Worlds.Shadow();
                runner.Track("feather", builder.Last);
                runner.Run(600f, 150f);

                int worst = 0;
                int clamped = 0;
                for (int i = 0; i < runner.Samples.Count; i++)
                {
                    if (runner.Samples[i].Substeps > worst) worst = runner.Samples[i].Substeps;
                }
                if (simulation.Solver.LastStepWasClamped) clamped++;

                report.Append("f=").Append(frequencies[f])
                      .Append(": ").Append(worst).Append(" substeps")
                      .Append(clamped > 0 ? " (clamped)" : "")
                      .Append(", feather ends ").Append(C(runner.Final.Tracked["feather"]))
                      .Append(f == frequencies.Length - 1 ? "" : "; ");

                last = runner;
            }

            return Result("stiff", last,
                "A 20 kg block bolted to a 3x3x3 heavy armour cube at 900 K. " + report);
        }

        /// <summary>
        /// Specific heat is stated in real J/(kg K) and the pace comes from HeatTimeScale. This
        /// shows what that trade actually buys: the same curve, sampled at different clocks.
        /// </summary>
        public static ScenarioResult Units()
        {
            StringBuilder report = new StringBuilder();
            ScenarioRunner last = null;

            float[] scales = new float[] { 1f, 25f, 225f };
            for (int i = 0; i < scales.Length; i++)
            {
                ThermalSettings settings = new ThermalSettings();
                settings.HeatTimeScale = scales[i];
                settings.Derive();

                GridBuilder builder = GridBuilder.Large();
                builder.Place(Catalog.HeavyArmor(), Vector3I.Zero);

                ThermalSimulation simulation = builder.BuildSimulation(settings, 800f);
                ScenarioRunner runner = new ScenarioRunner(simulation);
                runner.Environment = t => Worlds.Shadow();
                runner.Track("block", builder.Placed[0]);
                runner.Run(3600f, 600f);

                report.Append("x").Append(scales[i].ToString("n0")).Append(": ")
                      .Append(C(runner.Final.Tracked["block"]))
                      .Append(i == scales.Length - 1 ? "" : ", ");

                last = runner;
            }

            return Result("units", last,
                "One heavy armour block at 800 K in shadow, after one hour, at three thermal "
                + "clocks. Real steel is 450 J/(kg K); HeatTimeScale divides it. " + report);
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
