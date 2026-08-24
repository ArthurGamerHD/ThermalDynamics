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
            "first-room",
            "stiff",
            "units",
            "perf",
            "capital",
            "fleet",
            "interior",
            "solver",
            "self-shadow",
            "shadow-cost",
            "weather",
            "underground",
            "cooling-plant",
            "loop-faults",
            "loop-dry",
            "heatpump-backwards",
            "heatpump-limits",
            "cooling-runaway",
            "loop-stiffness",
            "loop-layout",
            "air-conditioning",
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
                case "first-room": return FirstRoom();
                case "stiff": return Stiff();
                case "units": return Units();
                case "perf": return Performance();
                case "capital": return Capital();
                case "fleet": return Fleet();
                case "interior": return Interior();
                case "solver": return Solver();
                case "self-shadow": return SelfShadow();
                case "shadow-cost": return ShadowCost();
                case "weather": return Weather();
                case "underground": return Underground();
                case "cooling-plant": return CoolingPlant();
                case "loop-faults": return LoopFaults();
                case "loop-dry": return LoopDry();
                case "heatpump-backwards": return HeatPumpBackwards();
                case "heatpump-limits": return HeatPumpLimits();
                case "cooling-runaway": return CoolingRunaway();
                case "loop-stiffness": return LoopStiffness();
                case "loop-layout": return LoopLayout();
                case "air-conditioning": return AirConditioning();
                default:
                    throw new ArgumentException("Unknown scenario: " + name);
            }
        }

        /// <summary>
        /// A slab in full sunlight, of the shape that showed the model up: four cells thick, seven
        /// tall, four deep, with a recess cut into one face.
        ///
        /// It answers the question the pictures kept raising — which faces of a solid hull are lit,
        /// and by how much — three ways at once. The sunward face should be lit whole. The two
        /// flanks should be lit whole and dimmer, because they are square-on to nothing but still
        /// out in the open, and a model that treats shadow as a property of a block instead of a
        /// face lights only the outermost row of them. The recess should be dark, because the wall
        /// beside it is in the way.
        ///
        /// It also states the cost of the two models against each other, since that is what the
        /// setting is for.
        /// </summary>
        public static ScenarioResult SelfShadow()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.SolarSelfShadowing = true;

            GridBuilder builder = Slab();
            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.Solver.CollectDiagnostics = true;

            // Sun over the +X flank and a little above, the angle the test world was standing in.
            EnvironmentSample sun = Worlds.Space(new Vector3(0.9004f, 0.1619f, -0.4038f));

            ScenarioRunner runner = new ScenarioRunner(simulation);
            runner.Environment = t => sun;
            runner.Track("sunward-face", simulation.Solver.GetNodeAt(new Vector3I(3, 3, 1)).Block);
            runner.Track("recess-floor", simulation.Solver.GetNodeAt(new Vector3I(2, 3, 1)).Block);
            runner.Track("shaded-flank", simulation.Solver.GetNodeAt(new Vector3I(0, 0, 1)).Block);
            runner.Run(1800f, 300f);

            SunShadowMap shadow = simulation.Solver.SunShadow;

            float sunward = LitShare(simulation, shadow, Face.Right);
            float top = LitShare(simulation, shadow, Face.Up);
            float flank = LitShare(simulation, shadow, Face.Forward);

            // The same grid with self-shadowing off, for the comparison the setting exists to let
            // people make.
            ThermalSettings cheap = new ThermalSettings();
            cheap.SolarSelfShadowing = false;

            ThermalSimulation plain = Slab().BuildSimulation(cheap, 293.15f);
            plain.Solver.CollectDiagnostics = true;

            ScenarioRunner cheapRunner = new ScenarioRunner(plain);
            cheapRunner.Environment = t => sun;
            cheapRunner.Run(1800f, 300f);

            return Result("self-shadow", runner,
                "Solid slab in sunlight, sun over the +X flank. Exposed faces lit: sunward "
                + Pct(sunward) + ", top " + Pct(top) + ", flank " + Pct(flank)
                + ", recess floor " + Pct(RecessShare(simulation, shadow))
                + ". Shadowed air cells: " + shadow.ShadowedCount
                + ". Grid solar with self-shadowing " + W(TotalSolar(simulation))
                + " against " + W(TotalSolar(plain)) + " without. Hottest "
                + C(runner.Final.HottestTemperature) + " against "
                + C(cheapRunner.Final.HottestTemperature) + ".");
        }

        /// <summary>
        /// What self-shadowing costs against the model it replaces.
        ///
        /// The two are not the same kind of work, so both halves have to be reported. The cheap
        /// model costs a dot product per face on every step, forever. This one costs that plus a
        /// walk over the hull's air cells — but only when the sun has moved, which on a planet is
        /// seconds of play apart, and the walk is spread over ticks besides. A per-step average
        /// alone would flatter it; a pass cost alone would damn it.
        /// </summary>
        public static ScenarioResult ShadowCost()
        {
            const int side = 20;

            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(side, side, side));

            Vector3 sun = Vector3.Normalize(new Vector3(0.9004f, 0.1619f, -0.4038f));
            EnvironmentSample sample = Worlds.Space(sun);

            double cheap = StepCost(builder, false, sample);
            double shadowed = StepCost(builder, true, sample);

            ThermalSimulation solid = builder.BuildSimulation(Shadowing(true), 293.15f);
            solid.Update(1f / 60f, sample);
            PassCost solidPass = MeasurePass(solid, sun);

            // A hull with rooms in it is the harder case, and the realistic one: every interior
            // cell borders a block, so it is walked too, and its walk ends against the hull rather
            // than in open space.
            GridBuilder hull = GridBuilder.Large();
            hull.Shell(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(30, 20, 20));
            for (int y = 4; y < 20; y += 6)
            {
                hull.Fill(Catalog.LightArmor(), new Vector3I(1, y, 1), new Vector3I(29, y + 1, 19));
            }

            ThermalSimulation ship = hull.BuildSimulation(Shadowing(true), 293.15f);
            ship.Update(1f / 60f, sample);
            PassCost shipPass = MeasurePass(ship, sun);

            int budget = solid.Solver.SunShadowBudget;

            return Result("shadow-cost", new ScenarioRunner(solid),
                side + "^3 solid grid, " + solid.Solver.Nodes.Count + " blocks: step with "
                + "self-shadowing off " + Ms(cheap) + ", on " + Ms(shadowed)
                + " (" + Overhead(cheap, shadowed) + "). One full pass " + solidPass.Cells
                + " air cells in " + Ms(solidPass.Milliseconds) + ", "
                + Slices(solidPass, budget) + ". A 30x20x20 hull with decks, "
                + ship.Solver.Nodes.Count + " blocks: pass " + shipPass.Cells + " air cells in "
                + Ms(shipPass.Milliseconds) + ", " + Slices(shipPass, budget)
                + ". A pass runs when the sun moves 2 degrees, and never between.");
        }

        private struct PassCost
        {
            public int Cells;
            public double Milliseconds;
        }

        /// <summary>Best of three whole passes, so a stray scheduling hiccup is not the headline.</summary>
        private static PassCost MeasurePass(ThermalSimulation simulation, Vector3 sun)
        {
            SunShadowMap map = simulation.Solver.SunShadow;

            PassCost best = new PassCost();
            best.Milliseconds = double.MaxValue;

            for (int i = 0; i < 3; i++)
            {
                Stopwatch pass = Stopwatch.StartNew();
                map.Restart(simulation.Grid, sun);
                int cells = map.PendingCells;
                map.RunToCompletion();
                pass.Stop();

                if (pass.Elapsed.TotalMilliseconds < best.Milliseconds)
                {
                    best.Milliseconds = pass.Elapsed.TotalMilliseconds;
                    best.Cells = cells;
                }
            }

            return best;
        }

        private static string Slices(PassCost pass, int budget)
        {
            int slices = Math.Max(1, (pass.Cells + budget - 1) / budget);
            return slices + " slices of " + budget + " at " + Ms(pass.Milliseconds / slices) + " each";
        }

        private static ThermalSettings Shadowing(bool on)
        {
            ThermalSettings settings = new ThermalSettings();
            settings.SolarSelfShadowing = on;
            return settings;
        }

        /// <summary>
        /// Milliseconds per tick in the steady state — the sun standing still, so no pass is
        /// running. Best of three runs, because this is a small difference between large numbers
        /// and a single sample of it is mostly scheduler noise.
        /// </summary>
        private static double StepCost(GridBuilder builder, bool shadowing, EnvironmentSample sample)
        {
            double best = double.MaxValue;

            for (int attempt = 0; attempt < 3; attempt++)
            {
                ThermalSimulation simulation = builder.BuildSimulation(Shadowing(shadowing), 293.15f);

                // Let any pass finish first: this is the steady state, not the first tick.
                for (int i = 0; i < 60; i++) simulation.Update(1f / 60f, sample);

                const int measured = 400;
                Stopwatch run = Stopwatch.StartNew();
                for (int i = 0; i < measured; i++) simulation.Update(1f / 60f, sample);
                run.Stop();

                double perStep = run.Elapsed.TotalMilliseconds / measured;
                if (perStep < best) best = perStep;
            }

            return best;
        }

        private static string Ms(double milliseconds)
        {
            return milliseconds.ToString("n3") + " ms";
        }

        private static string Overhead(double baseline, double measured)
        {
            if (baseline <= 0) return "n/a";

            double percent = ((measured / baseline) - 1d) * 100d;
            return (percent >= 0 ? "+" : "") + percent.ToString("n1") + "%";
        }

        /// <summary>The slab: a four-thick wall with a two-cell recess cut into its shaded side.</summary>
        private static GridBuilder Slab()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(4, 7, 4));

            // A doorway-sized bite out of the -X face, so some faces are open to the sky and
            // cannot see the sun.
            for (int y = 2; y < 5; y++)
            {
                for (int z = 1; z < 3; z++)
                {
                    builder.Remove(new Vector3I(0, y, z));
                    builder.Remove(new Vector3I(1, y, z));
                }
            }

            return builder;
        }

        /// <summary>Share of one direction's exposed cell faces that the sun reaches, 0..1.</summary>
        private static float LitShare(ThermalSimulation simulation, SunShadowMap shadow, int face)
        {
            int exposed = 0;
            float lit = 0f;

            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                int cells = nodes[i].GetExposedFaces(face);
                if (cells == 0) continue;

                exposed += cells;
                lit += cells * shadow.FaceLitFraction(nodes[i].Block, face);
            }

            return exposed == 0 ? 0f : lit / exposed;
        }

        /// <summary>Share of the recess's inward-looking faces the sun reaches.</summary>
        private static float RecessShare(ThermalSimulation simulation, SunShadowMap shadow)
        {
            int cells = 0;
            float lit = 0f;

            for (int y = 2; y < 5; y++)
            {
                for (int z = 1; z < 3; z++)
                {
                    ThermalNode node = simulation.Solver.GetNodeAt(new Vector3I(2, y, z));
                    if (node == null) continue;

                    cells++;
                    lit += shadow.FaceLitFraction(node.Block, Face.Left);
                }
            }

            return cells == 0 ? 0f : lit / cells;
        }

        private static float TotalSolar(ThermalSimulation simulation)
        {
            float total = 0f;
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++) total += nodes[i].LastSolarWatts;
            return total;
        }

        private static string Pct(float fraction)
        {
            return (fraction * 100f).ToString("n0") + "%";
        }

        private static string W(float watts)
        {
            return watts >= 1000f
                ? (watts / 1000f).ToString("n1") + " kW"
                : watts.ToString("n0") + " W";
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
                   .Wasting(1250000f);
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
                "Pumped coolant ring between a 1.25 MW source and a plain armour block: " + found
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
            builder.Place(Catalog.Reactor(), Vector3I.Zero).Wasting(500000f);
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
                "A 500 kW source sealed inside a 3x3x3 hull. Its exposed faces: "
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
            while (coalescedSim.HasPendingWork) coalescedSim.Update(1f / 60f, Worlds.Shadow());
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
                "500 kW into a 3x3x3 hull in shadow. The source settles at " + C(bare)
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
                   .Wasting(500000f);

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

            builder.Place(Catalog.AirtightDoor(), new Vector3I(2, 2, 0));
            BlockInstance door = builder.Last;

            // Standing on the floor of the room, not floating in the middle of it: a block with
            // neither neighbours nor exposure has no way at all to shed heat, and no such block
            // can exist on a real grid.
            builder.Place(Catalog.Reactor(), new Vector3I(2, 1, 2))
                   .Wasting(250000f);

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
                "A 250 kW source inside a sealed 5x5x5 shell reaches " + C(sealedTemperature)
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
                   .Wasting(1250000f);
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
                "A 1.25 MW source on a pumped ring holds at " + C(cooled) + " (" + loopsBefore
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
            builder.Place(Catalog.Reactor(), new Vector3I(0, 0, 1)).Wasting(250000f);

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
                "Five seconds of a 250 kW source next to one heavy armour block. At a tenth of its "
                + "mass the block reaches " + C(skeleton) + "; fully welded it is still at "
                + C(finished) + ". The tracked run welds it up after those five seconds and "
                + "settles at " + C(runner.Final.Tracked["skeleton"]) + ".");
        }

        /// <summary>
        /// The shape a player actually builds a first room in: a 3x3x3 armour shell with a door
        /// in one wall and a reactor bolted to the outside, welded one block at a time rather
        /// than loaded whole.
        ///
        /// It exists because a field report had exactly this grid mapping as zero sealed rooms.
        /// It runs the audit at the end, so the answer is the classification of every cell rather
        /// than a room count with nothing behind it.
        /// </summary>
        public static ScenarioResult FirstRoom()
        {
            GridModel grid = new GridModel(Catalog.LargeGridSize);
            ThermalSimulation simulation = new ThermalSimulation(new ThermalSettings(), grid);

            BlockModel armour = Catalog.LightArmor();
            BlockModel doorModel = Catalog.SlideDoor();
            BlockInstance door = null;

            for (int z = -1; z <= 1; z++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int x = -1; x <= 1; x++)
                    {
                        if (x == 0 && y == 0 && z == 0) continue;

                        Vector3I cell = new Vector3I(x, y, z);
                        bool isDoor = cell == new Vector3I(0, 0, -1);

                        BlockInstance block = new BlockInstance(
                            isDoor ? doorModel : armour, cell, BlockOrientation.Identity);
                        if (isDoor) door = block;

                        simulation.AddBlock(block);

                        // one welded block per ten-frame tick, as the adapter polls
                        simulation.Update(10f / 60f, Worlds.Shadow());
                    }
                }
            }

            BlockInstance reactor = new BlockInstance(Catalog.Reactor(), new Vector3I(0, 0, 2), BlockOrientation.Identity);
            simulation.AddBlock(reactor);
            reactor.PowerProducedWatts = 0.3f * ThermalConstants.MegawattsToWatts;

            while (simulation.HasPendingWork) simulation.Update(10f / 60f, Worlds.Shadow());

            RoomAudit closed = simulation.AuditRooms();

            door.IsSealedByDoorState = false;
            simulation.RefreshBlockSealing(door);
            simulation.Rooms.RunToCompletion();
            RoomAudit opened = simulation.AuditRooms();

            door.IsSealedByDoorState = true;
            simulation.RefreshBlockSealing(door);
            simulation.Rooms.RunToCompletion();
            simulation.Solver.RefreshExposure(simulation.Rooms.Map);

            ScenarioRunner runner = new ScenarioRunner(simulation);
            runner.Environment = t => Worlds.Shadow();
            runner.Track("reactor", reactor);
            runner.Run(300f, 60f);

            return Result("first-room", runner,
                "A 3x3x3 shell with a door, welded a block at a time, maps as "
                + closed.RoomCount + " sealed room over " + closed.SearchVolume + " search cells ("
                + closed.ExternalCells + " external, " + closed.SolidCells + " structure, "
                + closed.RoomCells + " room, " + closed.OpenBlockCells + " block cells outdoors). "
                + "Opening the door leaves " + opened.RoomCount + " rooms and puts "
                + opened.OpenBlockCells + " block cells outdoors. The reactor bolted to the outside ends at "
                + C(reactor.PowerProducedWatts > 0 ? runner.Final.Tracked["reactor"] : 0f) + ".");
        }

        private static float WeldedRise(float massFraction, float seconds)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.HeavyArmor(), Vector3I.Zero);
            builder.Place(Catalog.Reactor(), new Vector3I(0, 0, 1)).Wasting(250000f);

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

        /// <summary>
        /// A capital ship at the scale the block-count stress test actually ran: tens of
        /// thousands of cells, hollow, bulkheaded into sealed compartments.
        ///
        /// The <see cref="Performance"/> scenario measures a solid cube, which is the cheapest
        /// possible shape for everything except the solver — no interior surface, one room, no
        /// exterior to flood fill. The field reports say the expensive stages on a real ship are
        /// the one-shot ones: a 44,632 cell grid spent 152 ms in a single room-mapping call and
        /// 151 ms in a single topology rebuild, against 23 ms for a solver step. This is the
        /// shape that shows that, and the number to watch is the worst single call, not the mean.
        /// </summary>
        public static ScenarioResult Capital()
        {
            Stopwatch build = Stopwatch.StartNew();

            GridBuilder builder = GridBuilder.Large();
            builder.PlaceAll(Catalog.HeavyArmor(),
                GridShapes.Ship(fuselageLength: 180, fuselageWidth: 21, bulkheadSpacing: 6));

            StageTimings timings = new StageTimings();
            ThermalSimulation simulation = new ThermalSimulation(new ThermalSettings(), builder.Grid);
            simulation.Profiler = timings;

            for (int i = 0; i < builder.Placed.Count; i++)
            {
                simulation.Solver.AddBlock(builder.Placed[i], 293.15f);
            }

            simulation.RebuildAll();
            while (simulation.HasPendingWork) simulation.Update(1f / 60f, Worlds.Shadow());
            build.Stop();

            int cells = simulation.Solver.Nodes.Count;
            int links = simulation.Solver.Links.Count;
            int rooms = simulation.Rooms.Map.RoomCount;

            double rebuildWorst =
                timings.WorstMs(SimulationPhase.Topology)
                + timings.WorstMs(SimulationPhase.RoomMapping)
                + timings.WorstMs(SimulationPhase.Exposure);

            // StepExact deliberately skips the profiler, so the solver is timed by hand — the
            // same way the perf scenario does it.
            EnvironmentState state = EnvironmentSolver.Solve(
                simulation.Settings, simulation.Planet, Worlds.Space(new Vector3(0f, 1f, 0f)));

            const int measured = 40;
            for (int i = 0; i < 5; i++) simulation.Solver.Step(simulation.Settings.StepSeconds, state);

            Stopwatch solve = Stopwatch.StartNew();
            for (int i = 0; i < measured; i++) simulation.Solver.Step(simulation.Settings.StepSeconds, state);
            solve.Stop();

            double solverPerStep = solve.Elapsed.TotalMilliseconds / measured;

            ScenarioRunner runner = new ScenarioRunner(simulation);
            runner.Environment = t => Worlds.Space(new Vector3(0f, 1f, 0f));
            runner.Track("skin", builder.Placed[0]);
            runner.Run(60f, 15f);

            return Result("capital", runner,
                cells.ToString("n0") + " cells, " + links.ToString("n0") + " links, "
                + rooms + " sealed rooms. Build and first map " + build.ElapsedMilliseconds
                + " ms. Worst single rebuild " + rebuildWorst.ToString("n1")
                + " ms against " + solverPerStep.ToString("n2") + " ms per solver step: "
                + timings.Describe(SimulationPhase.Topology) + ", "
                + timings.Describe(SimulationPhase.RoomMapping) + ", "
                + timings.Describe(SimulationPhase.Exposure) + ".");
        }

        /// <summary>
        /// Twenty ships in one world, stepped together, which is what the stress test did.
        ///
        /// Each grid carries its own solver, its own room map and its own environment sample,
        /// and nothing shares a frame budget between them. This measures the thing that costs a
        /// server: the whole fleet's cost in one simulated frame, against one ship's.
        /// </summary>
        public static ScenarioResult Fleet()
        {
            const int fleetSize = 20;

            ThermalSettings settings = new ThermalSettings();
            settings.Derive();

            List<ThermalSimulation> fleet = new List<ThermalSimulation>();
            ScenarioRunner first = null;
            int cellsEach = 0;

            for (int i = 0; i < fleetSize; i++)
            {
                GridBuilder builder = GridBuilder.Large();
                builder.PlaceAll(Catalog.HeavyArmor(),
                    GridShapes.Ship(fuselageLength: 40, fuselageWidth: 9, bulkheadSpacing: 6));

                // One hot appliance per ship, so no grid is a trivially uniform solve.
                Vector3I engineRoom = new Vector3I(4, 4, 6);
                BlockInstance occupant = builder.Grid.GetAtCell(engineRoom);
                if (occupant != null)
                {
                    builder.Grid.Remove(occupant);
                    builder.Placed.Remove(occupant);
                }
                builder.Place(Catalog.Reactor(), engineRoom)
                       .Wasting(500000f);

                ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
                while (simulation.HasPendingWork) simulation.Update(1f / 60f, Worlds.Shadow());

                cellsEach = simulation.Solver.Nodes.Count;
                fleet.Add(simulation);

                if (i == 0)
                {
                    first = new ScenarioRunner(simulation);
                    first.Environment = t => Worlds.Shadow();
                    first.Track("reactor", builder.Last);
                }
            }

            EnvironmentState state = EnvironmentSolver.Solve(settings, fleet[0].Planet, Worlds.Shadow());

            // Warm up, then run the same number of solver steps twice: all of them on one ship,
            // and then spread across the fleet. Equal work either way, so what the comparison
            // isolates is per-grid overhead rather than per-cell cost.
            const int frames = 100;
            int steps = frames * fleetSize;
            for (int i = 0; i < fleet.Count; i++) fleet[i].Solver.Step(settings.StepSeconds, state);

            Stopwatch one = Stopwatch.StartNew();
            for (int s = 0; s < steps; s++) fleet[0].Solver.Step(settings.StepSeconds, state);
            one.Stop();

            Stopwatch all = Stopwatch.StartNew();
            for (int s = 0; s < frames; s++)
            {
                for (int i = 0; i < fleet.Count; i++) fleet[i].Solver.Step(settings.StepSeconds, state);
            }
            all.Stop();

            double onePerStep = one.Elapsed.TotalMilliseconds / steps;
            double fleetPerStep = all.Elapsed.TotalMilliseconds / frames;

            first.Run(60f, 20f);

            return Result("fleet", first,
                fleetSize + " ships of " + cellsEach.ToString("n0") + " cells. A solver step costs "
                + onePerStep.ToString("n3") + " ms on one ship and "
                + (onePerStep <= 0d ? 0d : (fleetPerStep / fleetSize) / onePerStep).ToString("n2")
                + "x that when the same steps are spread across " + fleetSize
                + " grids, so the cost is per cell and not per grid. One frame of the whole fleet "
                + "is " + fleetPerStep.ToString("n3") + " ms, and at "
                + settings.StepsPerSecond.ToString("n0") + " steps a second that is "
                + (fleetPerStep * settings.StepsPerSecond).ToString("n1")
                + " ms of every simulated second.");
        }

        /// <summary>
        /// A hot appliance with no exposed face at all, against the same appliance on the skin.
        ///
        /// The stress test found whole block types — 5,399 hydrogen thrusters, 1,600 lights —
        /// reporting a mean exposed area of exactly zero across every instance, which means they
        /// radiate nothing and see no sun. That is legitimate for a buried block, and this is
        /// what legitimate looks like: the heat has to leave sideways through conduction, and the
        /// block has to settle rather than run away.
        /// </summary>
        public static ScenarioResult Interior()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.HeavyArmor(), new Vector3I(-3, -3, -3), new Vector3I(4, 4, 4));

            // Swap the centre for the appliance: a 200 kW consumer, which is what a large radio
            // antenna at full range draws, and the block the field report named as the hottest.
            BlockInstance centre = builder.Grid.GetAtCell(Vector3I.Zero);
            builder.Grid.Remove(centre);
            builder.Placed.Remove(centre);
            builder.Place(Catalog.Battery(), Vector3I.Zero).Consuming(200000f);
            BlockInstance buried = builder.Last;

            // The same appliance bolted onto the skin, where it can radiate.
            builder.Place(Catalog.Battery(), new Vector3I(0, 4, 0)).Consuming(200000f);
            BlockInstance exposed = builder.Last;

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);
            while (simulation.HasPendingWork) simulation.Update(1f / 60f, Worlds.Shadow());

            ScenarioRunner runner = new ScenarioRunner(simulation);
            runner.Environment = t => Worlds.Shadow();
            runner.Track("buried", buried);
            runner.Track("skin", exposed);
            runner.Track("hull", builder.Grid.GetAtCell(new Vector3I(0, 3, 0)));
            runner.Run(3600f, 600f);

            int buriedFaces = simulation.Solver.GetNode(buried).TotalExposedFaces;
            int exposedFaces = simulation.Solver.GetNode(exposed).TotalExposedFaces;

            return Result("interior", runner,
                "A 200 kW consumer at 5% waste heat, buried in heavy armour ("
                + buriedFaces + " exposed faces) and bolted to the skin (" + exposedFaces
                + "). Buried ends at " + C(runner.Final.Tracked["buried"]) + ", on the skin "
                + C(runner.Final.Tracked["skin"]) + ", the hull above it "
                + C(runner.Final.Tracked["hull"]) + ".");
        }

        /// <summary>
        /// What one solver step actually costs, on a grid shaped and loaded like the one the
        /// stress test ran.
        ///
        /// <see cref="Performance"/> does not measure this, and it is worth being explicit about
        /// why: it steps a solid cube of one block type that has been left to settle, so nearly
        /// every link joins two cells at the same temperature. The conduction loop's first act is
        /// to skip a link whose ends agree, which means the headline "ms/step" is largely the
        /// cost of *not* conducting. The field run's ships were 44,632 cells with reactors and
        /// thrusters in them and needed six substeps; almost every link there carries a gradient.
        ///
        /// So this seeds a real spread of temperatures and reports the number that can be
        /// compared across grid sizes and substep counts: nanoseconds per link visit.
        /// </summary>
        public static ScenarioResult Solver()
        {
            GridBuilder builder = GridBuilder.Large();

            // Not one block type. Substeps are set by the stiffest node — the largest ratio of
            // conductance to thermal mass on the grid — so a ship built entirely of one heavy
            // block solves in a single substep and never exercises the loop that dominates the
            // field run. A real ship is armour with light fittings bolted through it, and the
            // light fitting is the stiff node: grating is a sixteenth of heavy armour's mass at
            // the same conductivity.
            BlockModel armour = Catalog.HeavyArmor();
            BlockModel fitting = Catalog.Grating();

            int index = 0;
            foreach (Vector3I cell in GridShapes.Ship(
                fuselageLength: 180, fuselageWidth: 21, bulkheadSpacing: 6))
            {
                builder.Place((index++ % 8) == 0 ? fitting : armour, cell);
            }

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);
            while (simulation.HasPendingWork) simulation.Update(1f / 60f, Worlds.Shadow());

            int cells = simulation.Solver.Nodes.Count;
            int links = simulation.Solver.Links.Count;

            // How much of the grid the environment pass has anything to do for. A hollow ship is
            // mostly skin; a solid station is mostly interior, and the two cost very differently
            // per cell for the same cell count.
            int exposed = 0;
            for (int i = 0; i < cells; i++)
            {
                if (simulation.Solver.Nodes[i].TotalExposedFaces > 0) exposed++;
            }

            float step = simulation.Settings.StepSeconds;

            // Measured twice: at the configured step length, which this grid solves in one
            // substep, and at a step six times longer, which it does not. Substepping is where
            // the cost lives — the field run's ships spent every step at six — and the two
            // figures separate the per-step overhead from the per-substep work.
            SolverCost single = MeasureSolver(simulation, step, links);
            SolverCost stiff = MeasureSolver(simulation, step * 6f, links);

            ScenarioRunner runner = new ScenarioRunner(simulation);
            runner.Environment = t => Worlds.Space(new Vector3(0f, 1f, 0f));
            runner.Track("skin", builder.Placed[0]);
            runner.Run(30f, 10f);

            return Result("solver", runner,
                cells.ToString("n0") + " cells (" + exposed.ToString("n0")
                + " exposed), " + links.ToString("n0")
                + " links, seeded across a 250-750 K spread so no link is skipped. "
                + single.Describe(simulation.Settings.StepsPerSecond) + " A step six times longer: "
                + stiff.Describe(simulation.Settings.StepsPerSecond / 6f));
        }

        /// <summary>
        /// Spreads the grid's temperatures across 250-750 K, deterministically.
        ///
        /// Both the spread and its repeatability matter: a link whose ends agree is skipped by
        /// the conduction loop, so a settled grid measures the wrong thing, and a benchmark that
        /// starts from a different state each run cannot be compared against its own past. The
        /// generator is written out rather than taken from <c>Random</c> so the sequence is the
        /// same on any runtime.
        /// </summary>
        private static void SeedSpread(ThermalSimulation simulation)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;

            uint state = 2463534242u;
            for (int i = 0; i < nodes.Count; i++)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                nodes[i].Temperature = 250f + ((state % 100000u) * 0.005f);
            }
        }

        /// <summary>One solver benchmark window.</summary>
        private struct SolverCost
        {
            public double PerStepMs;
            public double PerVisitNs;
            public double MeanSubsteps;

            public string Describe(float stepsPerSecond)
            {
                return PerStepMs.ToString("n4") + " ms per step at " + MeanSubsteps.ToString("n2")
                    + " substeps, which is " + PerVisitNs.ToString("n2") + " ns per link visit and "
                    + (PerStepMs * stepsPerSecond).ToString("n2") + " ms per simulated second.";
            }
        }

        private static SolverCost MeasureSolver(ThermalSimulation simulation, float step, int links)
        {
            EnvironmentState environment = EnvironmentSolver.Solve(
                simulation.Settings, simulation.Planet, Worlds.Space(new Vector3(0f, 1f, 0f)));

            SeedSpread(simulation);
            for (int i = 0; i < 5; i++) simulation.Solver.Step(step, environment);

            // Substeps fall as the grid equalises, so the count is read from the measured window
            // rather than assumed, and the per-visit figure is derived from it.
            long visits = 0;
            const int measured = 50;

            Stopwatch run = Stopwatch.StartNew();
            for (int i = 0; i < measured; i++)
            {
                simulation.Solver.Step(step, environment);
                visits += (long)simulation.Solver.LastSubsteps * links;
            }
            run.Stop();

            SolverCost cost = new SolverCost();
            cost.PerStepMs = run.Elapsed.TotalMilliseconds / measured;
            cost.PerVisitNs = visits <= 0 ? 0d : (run.Elapsed.TotalMilliseconds * 1e6) / visits;
            cost.MeanSubsteps = (double)visits / measured / (links <= 0 ? 1 : links);
            return cost;
        }


        /// <summary>
        /// A storm rolling over a parked plate, and off again.
        ///
        /// The question it answers is whether the weather reaches the temperature model at all.
        /// Before this round it did not: the game's weather was read, used to pick a point on a
        /// calm-to-storm scale for the wind, and thrown away, so a blizzard and a clear noon were
        /// the same climate. Three of the four things a storm does are visible here — the air
        /// drops, the sun goes out, and the coefficient that strips heat off the hull more than
        /// doubles — and the fourth, the wind, is the one that already worked.
        /// </summary>
        public static ScenarioResult Weather()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(3, 1, 3));

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);

            // Noon, sea level, clear air. The storm arrives at 300 s, peaks, and has gone by 900.
            WeatherResponse.Weather storm = WeatherResponse.For("SnowHeavy");

            ScenarioRunner runner = new ScenarioRunner(simulation);
            runner.Environment = t =>
            {
                EnvironmentSample sample = Worlds.PlanetSurface(1f, 0.5f);
                sample.Weather = storm;
                sample.WeatherIntensity = Intensity(t, 300f, 600f, 900f);
                return sample;
            };
            runner.Track("plate", builder.Placed[0]);
            runner.Run(1200f, 60f);

            float clear = runner.Samples[5].AmbientTemperature;      // 300 s, storm just arriving
            float worst = float.MaxValue;
            float coldest = float.MaxValue;

            // From 1: the first sample is recorded before any step has run, so its ambient is the
            // vacuum the solver was seeded with rather than a climate.
            for (int i = 1; i < runner.Samples.Count; i++)
            {
                float ambient = runner.Samples[i].AmbientTemperature;
                if (ambient < worst) worst = ambient;

                float plate = runner.Samples[i].Tracked["plate"];
                if (plate < coldest) coldest = plate;
            }

            EnvironmentState peak = EnvironmentSolver.Solve(
                simulation.Settings, simulation.Planet, StormAt(storm, 1f));
            EnvironmentState calm = EnvironmentSolver.Solve(
                simulation.Settings, simulation.Planet, StormAt(storm, 0f));

            return Result("weather", runner,
                "3x1x3 plate at noon, heavy snowstorm arriving at 300 s and gone by 900 s. Ambient "
                + C(clear) + " falling to " + C(worst) + ", plate down to " + C(coldest)
                + ". At the peak the sun delivers " + peak.SolarEnergy.ToString("n0")
                + " W/m2 against " + calm.SolarEnergy.ToString("n0")
                + " in clear air, and convection runs at " + peak.ConvectionCoefficient.ToString("n1")
                + " against " + calm.ConvectionCoefficient.ToString("n1") + " W/(m2 K).");
        }

        /// <summary>The same place under a storm of a given strength, for the two-figure comparison.</summary>
        private static EnvironmentSample StormAt(WeatherResponse.Weather storm, float intensity)
        {
            EnvironmentSample sample = Worlds.PlanetSurface(1f, 0.5f);
            sample.Weather = storm;
            sample.WeatherIntensity = intensity;
            return sample;
        }

        /// <summary>A weather that fades in, peaks and fades out again over a window.</summary>
        private static float Intensity(float t, float start, float peak, float end)
        {
            if (t <= start || t >= end) return 0f;
            if (t < peak) return (t - start) / (peak - start);
            return 1f - ((t - peak) / (end - peak));
        }

        /// <summary>
        /// The same plate at five depths, over a full day.
        ///
        /// Underground used to be one number at any depth on any planet. What should happen is two
        /// things at very different scales: the day damps out over tens of metres of rock, and then
        /// the rock itself warms toward the core below the sea-level deadzone. Both are visible in
        /// one table, and so is the reason the deadzone is measured from sea level — the mountain
        /// row is a kilometre inside the rock and still cold, because it is four kilometres above
        /// the level where the heat starts.
        /// </summary>
        public static ScenarioResult Underground()
        {
            StringBuilder summary = new StringBuilder();
            summary.Append("3x1x3 plate over one day at five depths. ");

            ScenarioRunner last = null;

            float[] depths = { 0f, 10f, 100f, 5000f, 20000f };
            string[] labels = { "surface", "10 m", "100 m", "5 km", "20 km" };

            for (int i = 0; i < depths.Length; i++)
            {
                float depth = depths[i];

                GridBuilder builder = GridBuilder.Large();
                builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(3, 1, 3));

                ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);

                ScenarioRunner runner = new ScenarioRunner(simulation);
                float dayLength = 7200f;
                runner.Environment = t =>
                {
                    EnvironmentSample sample = Worlds.PlanetSurface(1f, (t / dayLength) % 1f);
                    if (depth <= 0f) return sample;

                    sample.IsUnderground = true;
                    sample.Depth = depth;
                    sample.Radius = Worlds.EarthlikeRadius - depth;
                    sample.Altitude = -depth;
                    return sample;
                };
                runner.Track("plate", builder.Placed[0]);
                runner.Run(dayLength, dayLength / 24f);

                float min = float.MaxValue;
                float max = float.MinValue;
                for (int j = 1; j < runner.Samples.Count; j++)
                {
                    float ambient = runner.Samples[j].AmbientTemperature;
                    if (ambient < min) min = ambient;
                    if (ambient > max) max = ambient;
                }

                summary.Append(labels[i]).Append(' ').Append(C(min)).Append(" to ").Append(C(max))
                       .Append(" (swing ").Append((max - min).ToString("n1")).Append(" K)");
                summary.Append(i == depths.Length - 1 ? ". " : ", ");

                last = runner;
            }

            // A kilometre into a peak standing 5 km above sea level: deep rock, still above the heat.
            GridBuilder peak = GridBuilder.Large();
            peak.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(3, 1, 3));
            ThermalSimulation mountain = peak.BuildSimulation(new ThermalSettings(), 293.15f);

            EnvironmentSample inside = Worlds.PlanetSurface(1f, 0.5f);
            inside.IsUnderground = true;
            inside.Depth = 1000f;
            inside.Radius = Worlds.EarthlikeRadius + 4000f;
            inside.Altitude = 4000f;

            EnvironmentState tunnel = EnvironmentSolver.Solve(mountain.Settings, mountain.Planet, inside);

            summary.Append("A kilometre into a peak 5 km above sea level reads ")
                   .Append(C(tunnel.AmbientTemperature))
                   .Append(", because the deadzone is measured from sea level and not from the ground.");

            return Result("underground", last, summary.ToString());
        }

        // ---- the cooling plant, whole and broken ---------------------------------------------
        //
        // Everything below was written against a field telemetry dump: a 1,355 cell ship carrying
        // 288 coolant pipe blocks, 4 pumps, 8 radiators and 4 heat pumps, peaking at 886 K on a
        // hydrogen thruster, granted 3 substeps against the 21 its lightest block demanded. Nothing
        // in the suite exercised those four systems together, and the dump could not say whether
        // any of them worked.

        /// <summary>
        /// The general case: a whole cooling plant, on the shape the field ship is.
        ///
        /// Generation on the inside — a reactor and a bank of hydrogen thrusters, which is what the
        /// dump found running hottest — a pumped ring with sink faces against both, heat pumps
        /// lifting out of the ring, and radiators on the skin taking their reject heat. That is the
        /// full chain the mod exists to make possible, and it is the one arrangement no scenario
        /// tested end to end.
        ///
        /// Reported against the same ship with the plumbing left out, because the number that means
        /// anything is the difference the plant makes rather than the temperature it settles at.
        /// </summary>
        public static ScenarioResult CoolingPlant()
        {
            float withPlant, withoutPlant, loopDrawn, loopShed, pumpLift;
            float ignoredDrawn, ignoredShed, ignoredLift;

            ScenarioRunner plant = BuildCoolingPlant(true, out withPlant,
                out loopDrawn, out loopShed, out pumpLift);
            BuildCoolingPlant(false, out withoutPlant,
                out ignoredDrawn, out ignoredShed, out ignoredLift);

            return Result("cooling-plant", plant,
                "A reactor and eight hard-drawing batteries behind a pumped ring, heat pumps and "
                + "eight radiators. Hottest block " + C(withPlant) + " with the plant, "
                + C(withoutPlant) + " without it. The loop draws "
                + loopDrawn.ToString("n0") + " W and sheds " + loopShed.ToString("n0")
                + " W; the pumps lift " + pumpLift.ToString("n0") + " W.");
        }

        private static ScenarioRunner BuildCoolingPlant(bool plumbing, out float hottest,
            out float loopDrawn, out float loopShed, out float pumpLift)
        {
            GridBuilder builder = GridBuilder.Large();

            // A deck, with the machinery standing on it and the plumbing running over the machinery.
            builder.Fill(Catalog.LightArmor(), new Vector3I(0, 0, 0), new Vector3I(13, 2, 6));

            List<Vector3I> machinery = new List<Vector3I>();

            Vector3I reactorCell = new Vector3I(1, 2, 1);
            builder.Place(Catalog.Reactor(), reactorCell)
                   .Wasting(750000f);
            machinery.Add(reactorCell);

            // Eight consumers drawing hard. Batteries rather than the thrusters the dump found
            // running hottest, because a large thruster is 3x3x4 cells: a row of them would need
            // four-cell spacing and the ring above could only reach one face of each. One cell per
            // heat source is what makes this a test of the plumbing rather than of the geometry.
            for (int i = 0; i < 8; i++)
            {
                Vector3I cell = new Vector3I(3 + i, 2, 1);
                builder.Place(Catalog.Battery(), cell)
                       .Consuming(1f * ThermalConstants.MegawattsToWatts);
                machinery.Add(cell);
            }

            if (plumbing)
            {
                List<Vector3I> cells = PipeFitter.RectangleXZ(new Vector3I(1, 3, 1), 11, 4);

                // Sinks are derived from where the machinery actually is rather than from guessed
                // ring indices, so the plant cannot quietly end up plumbed past its own heat.
                Dictionary<int, Vector3I> sinks = new Dictionary<int, Vector3I>();
                for (int i = 0; i < cells.Count; i++)
                {
                    if (machinery.Contains(cells[i] + Vector3I.Down)) sinks[i] = Vector3I.Down;
                }

                PipeFitter.BuildRing(builder, cells, -1, sinks);

                // Pumps lift out of the ring's far side into radiators standing clear of the hull.
                for (int i = 0; i < cells.Count; i++)
                {
                    if (sinks.ContainsKey(i)) continue;
                    if (cells[i].Z != 4) continue;          // the run away from the machinery
                    if ((cells[i].X % 3) != 1) continue;    // every third cell along it

                    Vector3I pump = cells[i] + Vector3I.Up;
                    builder.Place(Catalog.HeatPump(), pump,
                        new BlockOrientation(Base6Directions.Direction.Down, Base6Directions.Direction.Forward));
                    builder.Place(Catalog.Radiator(), pump + Vector3I.Up);
                }
            }

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);

            // The host owns a pump's switch and its power; nothing in the harness plays that part.
            IList<HeatPumpDevice> pumps = simulation.Solver.HeatPumps;
            for (int i = 0; i < pumps.Count; i++)
            {
                pumps[i].Enabled = true;
                pumps[i].PowerAvailable = 1f;
            }

            ScenarioRunner runner = new ScenarioRunner(simulation);
            runner.Environment = t => Worlds.Shadow();
            runner.Track("reactor", simulation.Grid.GetAtCell(reactorCell));
            if (simulation.Solver.Loops.Count > 0) runner.TrackLoop("coolant", simulation.Solver.Loops[0]);
            runner.Run(7200f, 600f);

            hottest = runner.Final.HottestTemperature;

            loopDrawn = 0f;
            loopShed = 0f;
            for (int i = 0; i < simulation.Solver.Loops.Count; i++)
            {
                loopDrawn += simulation.Solver.Loops[i].LastWattsAbsorbed;
                loopShed += simulation.Solver.Loops[i].LastWattsRejected;
            }

            pumpLift = 0f;
            for (int i = 0; i < pumps.Count; i++) pumpLift += pumps[i].LastLiftedWatts;

            return runner;
        }

        /// <summary>
        /// Every way a ring fails to become a loop, on one grid, each reported with its reason.
        ///
        /// The player-facing failure this covers is "I built a ring and nothing happened", which is
        /// invisible in the loop list by construction: the symptom is that the loop is absent. The
        /// field dump had six copies of one ship, four with a loop and two without, and no way to
        /// tell what differed.
        /// </summary>
        public static ScenarioResult LoopFaults()
        {
            GridBuilder builder = GridBuilder.Large();

            // A ring that works.
            PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, 3, 3));

            // A closed ring with no pump in it.
            List<Vector3I> pumpless = PipeFitter.RectangleXZ(new Vector3I(0, 10, 0), 3, 3);
            for (int i = 0; i < pumpless.Count; i++)
            {
                Vector3I cell = pumpless[i];
                Vector3I toPrevious = pumpless[(i - 1 + pumpless.Count) % pumpless.Count] - cell;
                Vector3I toNext = pumpless[(i + 1) % pumpless.Count] - cell;

                BlockModel model = toPrevious == -toNext
                    ? Catalog.CoolantPipeStraight()
                    : Catalog.CoolantPipeCorner();
                builder.Place(model, cell, PipeFitter.Orient(model, toPrevious, toNext));
            }

            // A pump with nothing on either end.
            builder.Place(Catalog.CoolantPump(), new Vector3I(0, 20, 0),
                new BlockOrientation(Base6Directions.Direction.Forward, Base6Directions.Direction.Up));

            // A run walking into ordinary armour.
            builder.Place(Catalog.CoolantPump(), new Vector3I(0, 25, 0),
                new BlockOrientation(Base6Directions.Direction.Forward, Base6Directions.Direction.Up));
            builder.Place(Catalog.HeavyArmor(), new Vector3I(0, 25, 1));
            builder.Place(Catalog.HeavyArmor(), new Vector3I(0, 25, -1));

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);
            CoolantLoopDiagnostics diagnosis = simulation.DiagnoseLoops();

            ScenarioRunner runner = new ScenarioRunner(simulation);
            runner.Environment = t => Worlds.Shadow();
            runner.Run(60f, 30f);

            StringBuilder faults = new StringBuilder();
            for (int i = 1; i < diagnosis.Counts.Length; i++)
            {
                if (diagnosis.Counts[i] == 0) continue;
                if (faults.Length > 0) faults.Append("; ");
                faults.Append(diagnosis.Counts[i]).Append(" x ")
                      .Append(CoolantLoopDiagnostics.Describe((CoolantFault)i));
            }

            return Result("loop-faults", runner,
                diagnosis.Loops + " loop from " + diagnosis.PipesInLoops + " pipes, "
                + diagnosis.PipesAdrift + " pipes adrift: " + faults + ".");
        }

        /// <summary>
        /// A loop that formed correctly and cools nothing, because no sink face touches anything.
        ///
        /// The worst kind of failure to diagnose from a readout: the ring is closed, the pump is
        /// there, the loop count is 1, and the coolant sits at hull temperature forever. Every
        /// figure looks healthy. Only the watts moved give it away, which is why they are collected.
        /// </summary>
        public static ScenarioResult LoopDry()
        {
            float plumbedOnly = RingAgainstReactor(false);
            float withSink = RingAgainstReactor(true);

            GridBuilder builder = GridBuilder.Large();
            List<Vector3I> cells = PipeFitter.RectangleXZ(Vector3I.Zero, 4, 3);
            PipeFitter.BuildRing(builder, cells);
            builder.Place(Catalog.Reactor(), new Vector3I(1, -1, 0))
                   .Wasting(500000f);

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);
            ScenarioRunner runner = new ScenarioRunner(simulation);
            runner.Environment = t => Worlds.Shadow();
            runner.Track("reactor", simulation.Grid.GetAtCell(new Vector3I(1, -1, 0)));
            runner.TrackLoop("coolant", simulation.Solver.Loops[0]);
            runner.Run(3600f, 600f);

            CoolantLoop loop = simulation.Solver.Loops[0];

            return Result("loop-dry", runner,
                "A closed pumped ring with no sink face against the reactor: 1 loop, coolant at "
                + C(loop.Temperature) + ", drawing " + loop.LastWattsAbsorbed.ToString("n0")
                + " W. Reactor " + C(plumbedOnly) + " with plumbing only against " + C(withSink)
                + " with one sink face turned to meet it.");
        }

        private static float RingAgainstReactor(bool sink)
        {
            GridBuilder builder = GridBuilder.Large();

            Dictionary<int, Vector3I> sinks = new Dictionary<int, Vector3I>();
            if (sink) sinks[1] = Vector3I.Down;

            PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, 4, 3), -1, sinks);
            builder.Place(Catalog.Reactor(), new Vector3I(1, -1, 0))
                   .Wasting(500000f);
            BlockInstance reactor = builder.Last;

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);
            ScenarioRunner runner = new ScenarioRunner(simulation);
            runner.Environment = t => Worlds.Shadow();
            runner.Run(3600f, 1800f);

            return simulation.Solver.GetNode(reactor).Temperature;
        }

        /// <summary>
        /// A heat pump installed the wrong way round: cold face on the radiator, hot face on the
        /// reactor.
        ///
        /// The worst realistic build error, because it is a rotation rather than a mistake anyone
        /// would notice, and because it does not merely fail — it pumps the radiator's cold into the
        /// reactor and charges electricity for making the ship hotter. The model must punish it, and
        /// the terminal must be able to say so.
        /// </summary>
        public static ScenarioResult HeatPumpBackwards()
        {
            float correct = PumpBetweenReactorAndRadiator(true);
            float backwards = PumpBetweenReactorAndRadiator(false);

            GridBuilder builder = GridBuilder.Large();
            ScenarioRunner runner = PumpRunner(false, builder);

            return Result("heatpump-backwards", runner,
                "A pump between a 125 kW source and a radiator. The source " + C(correct)
                + " with the cold face against it, " + C(backwards)
                + " with the pump turned around — the wrong way costs "
                + (backwards - correct).ToString("n1") + " K and the same electricity.");
        }

        private static float PumpBetweenReactorAndRadiator(bool correctWayRound)
        {
            GridBuilder builder = GridBuilder.Large();
            ScenarioRunner runner = PumpRunner(correctWayRound, builder);
            return runner.Final.Tracked["reactor"];
        }

        private static ScenarioRunner PumpRunner(bool correctWayRound, GridBuilder builder)
        {
            builder.Place(Catalog.Reactor(), Vector3I.Zero).Wasting(125000f);
            BlockInstance reactor = builder.Last;

            // The cold face is the block's local Forward, and orientation Forward is the world
            // direction that points. The reactor is at -Z of the pump, so cooling it means looking
            // Forward; turning the pump round points the cold face at the radiator instead.
            Base6Directions.Direction forward = correctWayRound
                ? Base6Directions.Direction.Forward
                : Base6Directions.Direction.Backward;

            builder.Place(Catalog.HeatPump(), new Vector3I(0, 0, 1),
                new BlockOrientation(forward, Base6Directions.Direction.Up));
            builder.Place(Catalog.Radiator(), new Vector3I(0, 0, 2));

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);

            IList<HeatPumpDevice> pumps = simulation.Solver.HeatPumps;
            for (int i = 0; i < pumps.Count; i++)
            {
                pumps[i].Enabled = true;
                pumps[i].PowerAvailable = 1f;
            }

            ScenarioRunner runner = new ScenarioRunner(simulation);
            runner.Environment = t => Worlds.Shadow();
            runner.Track("reactor", reactor);
            runner.Run(3600f, 600f);
            return runner;
        }

        /// <summary>
        /// One pump, swept across the gap it has to lift over, reporting which of its three limits
        /// binds at each width and what the coefficient costs.
        ///
        /// The docs call which limit binds the block's whole character: cheap and rating-bound over a
        /// small gap, ruinous and Carnot-bound over a large one. That is a claim about numbers and it
        /// had none behind it. The last row also settles the documented promise that nothing clamps
        /// the cold side — the electrical rating stops it long before the temperature does.
        /// </summary>
        public static ScenarioResult HeatPumpLimits()
        {
            StringBuilder report = new StringBuilder();
            ScenarioRunner last = null;
            float[] hotSides = new float[] { 300f, 350f, 500f, 1200f };

            for (int i = 0; i < hotSides.Length; i++)
            {
                GridBuilder builder = GridBuilder.Large();
                builder.Place(Catalog.HeavyArmor(), Vector3I.Zero);
                BlockInstance cold = builder.Last;
                // A block's orientation Forward is the world direction its local Forward points, and
                // the cold face is local Forward. The cold block is at -Z of the pump, so the cold
                // face looks Forward. Getting this backwards is silent: the pump runs, and every
                // figure it reports is for the other pair of faces.
                builder.Place(Catalog.HeatPump(), new Vector3I(0, 0, 1),
                    new BlockOrientation(Base6Directions.Direction.Forward, Base6Directions.Direction.Up));
                builder.Place(Catalog.HeavyArmor(), new Vector3I(0, 0, 2));
                BlockInstance hot = builder.Last;

                ThermalSettings settings = new ThermalSettings();
                settings.EnableEnvironment = false;
                settings.EnableDamage = false;

                ThermalSimulation simulation = builder.BuildSimulation(settings.Derive(), 290f);
                HeatPumpDevice pump = simulation.Solver.HeatPumps[0];
                pump.Enabled = true;
                pump.PowerAvailable = 1f;

                // Checked rather than assumed: an inverted pump reports a full coefficient at every
                // gap, because a hot side below the cold side saturates the cap.
                if (pump.ColdNodeIndex != simulation.Solver.GetNode(cold).Index)
                {
                    throw new InvalidOperationException("the pump's cold face is not on the cold block");
                }

                // Both sides are pinned by an external hand after every step, so the sweep measures
                // the pump at a fixed gap rather than watching the gap close. Without the cold side
                // pinned it drifts upward: with no environment, the work the pump spends has nowhere
                // to go but back through the block it was lifting from.
                const float coldSide = 290f;
                float hotSide = hotSides[i];

                ScenarioRunner runner = new ScenarioRunner(simulation);
                runner.Environment = t => Worlds.Shadow();
                runner.AfterStep = sim =>
                {
                    sim.Solver.GetNode(hot).Temperature = hotSide;
                    sim.Solver.GetNode(cold).Temperature = coldSide;
                };
                runner.Track("cold", cold);
                runner.Run(60f, 30f);

                bool ratingBound = pump.LastLiftedWatts >= pump.RatedWatts - 1f;
                report.Append("gap ").Append((hotSide - coldSide).ToString("n0"))
                      .Append(" K: lift ").Append(pump.LastLiftedWatts.ToString("n0"))
                      .Append(" W, cop ").Append(pump.LastCoefficient.ToString("n2"))
                      .Append(ratingBound ? " (rating)" : " (Carnot)")
                      .Append(i == hotSides.Length - 1 ? "" : "; ");

                last = runner;
            }

            return Result("heatpump-limits", last,
                "One 60 kW pump against four gap widths. " + report + ".");
        }

        /// <summary>
        /// More heat than the radiators can shed, which is where a real ship ends up.
        ///
        /// A cooling plant does not fail gradually: while it has headroom it holds temperature almost
        /// flat, and past that everything it touches rises together, because the loop ties them into
        /// one mass. This measures where that knee is and confirms the model reaches damage rather
        /// than running away to a number.
        /// </summary>
        public static ScenarioResult CoolingRunaway()
        {
            StringBuilder report = new StringBuilder();
            ScenarioRunner last = null;
            float[] megawatts = new float[] { 0.5f, 2f, 8f };

            for (int i = 0; i < megawatts.Length; i++)
            {
                GridBuilder builder = GridBuilder.Large();

                Dictionary<int, Vector3I> sinks = new Dictionary<int, Vector3I>();
                sinks[1] = Vector3I.Down;
                sinks[5] = Vector3I.Up;

                List<Vector3I> cells = PipeFitter.RectangleXZ(Vector3I.Zero, 5, 5);
                PipeFitter.BuildRing(builder, cells, -1, sinks);

                builder.Place(Catalog.Reactor(), cells[1] + Vector3I.Down)
                       .Wasting(megawatts[i] * ThermalConstants.MegawattsToWatts * 0.25f);
                BlockInstance reactor = builder.Last;
                builder.Place(Catalog.Radiator(), cells[5] + Vector3I.Up);

                ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);
                ScenarioRunner runner = new ScenarioRunner(simulation);
                runner.Environment = t => Worlds.Shadow();
                runner.Track("reactor", reactor);
                runner.TrackLoop("coolant", simulation.Solver.Loops[0]);
                runner.Run(7200f, 900f);

                report.Append(megawatts[i].ToString("n1")).Append(" MW: reactor ")
                      .Append(C(runner.Final.Tracked["reactor"])).Append(", coolant ")
                      .Append(C(runner.Final.Tracked["coolant"]))
                      .Append(", ").Append(runner.Final.OverheatingBlocks).Append(" over critical")
                      .Append(i == megawatts.Length - 1 ? "" : "; ");

                last = runner;
            }

            return Result("cooling-runaway", last,
                "One radiator against three heat loads. " + report + ".");
        }

        /// <summary>
        /// A very long ring, which is the stiffest thing a player can build cheaply.
        ///
        /// Each pipe couples to the fluid at full strength and the fluid mass is a flat figure per
        /// loop, so coupling grows with length while capacity does not: a 76 pipe ring reaches a
        /// time constant shorter than the step that integrates it. The substep estimate has to see
        /// that — a stiff element the estimator cannot see is how an integrator goes unstable — so
        /// this reports the demand, what was granted, and whether energy survived.
        /// </summary>
        public static ScenarioResult LoopStiffness()
        {
            StringBuilder report = new StringBuilder();
            ScenarioRunner last = null;
            int[] sides = new int[] { 3, 9, 20 };

            for (int i = 0; i < sides.Length; i++)
            {
                GridBuilder builder = GridBuilder.Large();

                Dictionary<int, Vector3I> sinks = new Dictionary<int, Vector3I>();
                sinks[1] = Vector3I.Down;

                List<Vector3I> cells = PipeFitter.RectangleXZ(Vector3I.Zero, sides[i], sides[i]);
                PipeFitter.BuildRing(builder, cells, -1, sinks);
                builder.Place(Catalog.HeavyArmor(), cells[1] + Vector3I.Down);
                BlockInstance hot = builder.Last;

                ThermalSettings settings = new ThermalSettings();
                settings.EnableEnvironment = false;
                settings.EnableDamage = false;

                ThermalSimulation simulation = builder.BuildSimulation(settings.Derive(), 300f);
                CoolantLoop loop = simulation.Solver.Loops[0];
                simulation.Solver.GetNode(hot).Temperature = 1200f;

                float conductance = 0f;
                for (int l = 0; l < loop.Links.Count; l++) conductance += loop.Links[l].Conductance;

                float before = simulation.Solver.TotalEnergy;

                ScenarioRunner runner = new ScenarioRunner(simulation);
                runner.Environment = t => Worlds.Shadow();
                runner.Track("sink", hot);
                runner.TrackLoop("coolant", loop);
                runner.Run(300f, 150f);

                float after = simulation.Solver.TotalEnergy;

                report.Append(loop.PipeCount).Append(" pipes: ")
                      .Append(conductance.ToString("n0")).Append(" W/K on ")
                      .Append(loop.ThermalMass.ToString("n0")).Append(" J/K, tau ")
                      .Append((loop.ThermalMass / conductance).ToString("n3")).Append(" s, ")
                      .Append(simulation.Solver.LastSubsteps).Append(" substeps for ")
                      .Append(simulation.Solver.LastRequiredSubsteps.ToString("n1"))
                      .Append(" demanded, energy x").Append((after / before).ToString("n4"))
                      .Append(i == sides.Length - 1 ? "" : "; ");

                last = runner;
            }

            return Result("loop-stiffness", last,
                "A ring at three lengths, step " + (1f / new ThermalSettings().Derive().StepsPerSecond).ToString("n4")
                + " s. " + report + ".");
        }

        /// <summary>
        /// How to lay a loop out: one ring or several, and where to put the sinks.
        ///
        /// The intuition this was written to test was that several small rings should beat one large
        /// one, because flow rises only with the square root of combined pumping while the distance
        /// heat must travel rises linearly with ring size — so splitting a ring in four ought to make
        /// transport twice as fast for the same pumps.
        ///
        /// It is wrong, and the first measurement looked like it was right: four small rings came out
        /// 40 K ahead of one big one. All of that was where the sources sat. Spread the same four
        /// reactors evenly around the big ring instead of bunching them at one end and it matches the
        /// four small rings to within 2 K. What saturates a loop is several sources dumping into one
        /// short stretch of pipe, not the length of the ring they sit on.
        /// </summary>
        public static ScenarioResult LoopLayout()
        {
            float bunched = LoopLayoutPlant(1, 4, false);
            float spread = LoopLayoutPlant(1, 4, true);
            ScenarioRunner runner;
            float small = LoopLayoutPlant(4, 1, false, out runner);

            return Result("loop-layout", runner,
                "Four 62.5 kW sources and four radiators, 32 pipes and 4 pumps, arranged three ways. "
                + "One ring with the sources bunched: " + C(bunched) + ". One ring with them spread "
                + "evenly: " + C(spread) + ". Four separate rings: " + C(small)
                + ". Splitting the ring buys nothing; spreading the sources buys "
                + (bunched - spread).ToString("n0") + " K.");
        }

        private static float LoopLayoutPlant(int rings, int pumpsPerRing, bool spreadSources)
        {
            ScenarioRunner ignored;
            return LoopLayoutPlant(rings, pumpsPerRing, spreadSources, out ignored);
        }

        private static float LoopLayoutPlant(int rings, int pumpsPerRing, bool spreadSources,
            out ScenarioRunner runner)
        {
            GridBuilder builder = GridBuilder.Large();
            List<BlockInstance> reactors = new List<BlockInstance>();

            int reactorsPerRing = rings == 1 ? 4 : 1;
            int side = rings == 1 ? 9 : 3;

            for (int r = 0; r < rings; r++)
            {
                List<Vector3I> cells = PipeFitter.RectangleXZ(new Vector3I(0, r * 14, 0), side, side);
                Dictionary<int, Vector3I> sinks = new Dictionary<int, Vector3I>();

                for (int i = 0; i < reactorsPerRing; i++)
                {
                    int step = cells.Count / Math.Max(1, reactorsPerRing);
                    int sourceAt = spreadSources ? (i * step) + 1 : 1 + i;
                    int radiatorAt = spreadSources
                        ? (i * step) + 1 + (step / 2)
                        : (cells.Count / 2) + i;

                    sinks[sourceAt] = Vector3I.Down;
                    sinks[radiatorAt] = Vector3I.Up;
                }

                PipeFitter.BuildRing(builder, cells, -1, sinks);

                for (int i = 0; i < reactorsPerRing; i++)
                {
                    int step = cells.Count / Math.Max(1, reactorsPerRing);
                    int sourceAt = spreadSources ? (i * step) + 1 : 1 + i;
                    int radiatorAt = spreadSources
                        ? (i * step) + 1 + (step / 2)
                        : (cells.Count / 2) + i;

                    builder.Place(Catalog.Reactor(), cells[sourceAt] + Vector3I.Down)
                           .Wasting(62500f);
                    reactors.Add(builder.Last);

                    builder.Place(Catalog.Radiator(), cells[radiatorAt] + Vector3I.Up);
                }
            }

            ThermalSettings settings = new ThermalSettings();
            settings.EnableSolarHeat = false;
            settings.EnableFriction = false;
            settings.EnableDamage = false;

            ThermalSimulation simulation = builder.BuildSimulation(settings.Derive(), 293.15f);

            IList<CoolantLoop> loops = simulation.Solver.Loops;
            for (int l = 0; l < loops.Count; l++)
            {
                while (loops[l].Pumps.Count < pumpsPerRing)
                {
                    CoolantPump extra = new CoolantPump();
                    extra.MaxPowerWatts = 20000f;
                    loops[l].Pumps.Add(extra);
                }
                loops[l].RefreshFlow();
            }

            runner = new ScenarioRunner(simulation);
            runner.Environment = t => Worlds.Shadow();
            runner.Track("reactor", reactors[0]);
            runner.Run(10000f, 2000f);

            float hottest = 0f;
            for (int i = 0; i < reactors.Count; i++)
            {
                float t = simulation.Solver.GetNode(reactors[i]).Temperature;
                if (t > hottest) hottest = t;
            }
            return hottest;
        }

        /// <summary>
        /// Can a heat pump air-condition a room?
        ///
        /// Not directly: a heat pump binds to two *blocks*, and a room's air is not a block. It has to
        /// work through a wall — put the cold face on a block that bounds the compartment and the wall
        /// goes cold, the air in contact with it gives up its heat, and the room follows. The hot face
        /// goes outside, into a radiator.
        ///
        /// Which means it only works on a **pressurised** room. With no air there is nothing coupling
        /// the compartment to its walls, and the pump is just chilling a piece of hull.
        /// </summary>
        public static ScenarioResult AirConditioning()
        {
            ScenarioRunner runner;
            float without = ConditionedCabin(false, out runner);
            float with = ConditionedCabin(true, out runner);

            return Result("air-conditioning", runner,
                "A sealed cabin with a 15 kW source in it, and a heat pump on one wall rejecting into "
                + "a radiator outside. Room air settles at " + C(without) + " with the pump off and "
                + C(with) + " with it on, a difference of " + (without - with).ToString("n0") + " K.");
        }

        private static float ConditionedCabin(bool pumpRunning, out ScenarioRunner runner)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), new Vector3I(-1, -1, -1), new Vector3I(4, 4, 4));

            builder.Place(Catalog.Reactor(), new Vector3I(1, 1, 1)).Wasting(15000f);

            // Cold face on the cabin wall, hot face away from it, radiator beyond that.
            builder.Place(Catalog.HeatPump(), new Vector3I(1, 1, -2),
                new BlockOrientation(Base6Directions.Direction.Backward, Base6Directions.Direction.Up));
            builder.Place(Catalog.Radiator(), new Vector3I(1, 1, -4));

            ThermalSettings settings = new ThermalSettings();
            settings.EnableSolarHeat = false;
            settings.EnableFriction = false;
            settings.EnableDamage = false;

            ThermalSimulation simulation = builder.BuildSimulation(settings.Derive(), 320f);
            simulation.RebuildAll();

            Vector3I inside = new Vector3I(2, 1, 1);
            simulation.SetRoomPressure(inside, 1f);
            RoomAirNode air = simulation.Solver.GetRoomAir(simulation.Rooms.Map, inside);

            IList<HeatPumpDevice> pumps = simulation.Solver.HeatPumps;
            for (int i = 0; i < pumps.Count; i++)
            {
                pumps[i].Enabled = pumpRunning;
                pumps[i].PowerAvailable = 1f;
            }

            runner = new ScenarioRunner(simulation);
            runner.Environment = t => Worlds.Shadow();
            runner.Run(5000f, 1000f);

            return air == null ? 0f : air.Temperature;
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
