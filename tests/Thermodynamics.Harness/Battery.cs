using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// The scenario battery: every state a ship can be in that tells you something different about
    /// its thermal behaviour.
    ///
    /// <para>
    /// The battery is built along four independent axes, and a scenario is a point on all four at
    /// once. Nothing is included because it seems interesting; a scenario earns its place by
    /// answering a question no other scenario answers, and where two would answer the same one, the
    /// cheaper is kept.
    /// </para>
    ///
    /// <list type="bullet">
    /// <item><b>Environment</b> — what the outside is doing to the ship: vacuum, sun, air, weather,
    /// ground. This is where external heating and external cooling live, and they are the same axis
    /// because a planet's surface does both depending on the hour.</item>
    /// <item><b>Motion</b> — friction heats the leading face and airflow cools every face, and both
    /// depend on speed and on *which way the ship is pointing*. A ship travelling nose-first heats
    /// a different set of blocks from the same ship travelling belly-first.</item>
    /// <item><b>Load</b> — what is switched on inside: idle, full electrical, thrust in one named
    /// direction, weapons, everything at once.</item>
    /// <item><b>Configuration</b> — the ship as built, against the same ship with cooling fitted.
    /// This is the only axis the player controls directly, and the one criterion G3 is about.</item>
    /// </list>
    ///
    /// <para>
    /// Directional cases are enumerated rather than sampled. Friction and thrust both land their
    /// heat somewhere specific, and a ship is not symmetric: running only the forward case would
    /// miss that most designs have their thrusters and their thin armour on different faces.
    /// </para>
    /// </summary>
    public static class Battery
    {
        /// <summary>One state to put a ship in.</summary>
        public class Scenario
        {
            public string Name;

            /// <summary>What the outside is doing, as a function of elapsed simulated seconds.</summary>
            public Func<float, EnvironmentSample> Environment;

            /// <summary>What is switched on inside.</summary>
            public ShipLoad.State Load;

            /// <summary>Simulated seconds to run. Long enough to settle, not longer.</summary>
            public float Seconds = 1800f;

            /// <summary>What this scenario is here to find out. Printed with the results.</summary>
            public string Question;
        }

        /// <summary>Air density of an earthlike surface, and of a thin one.</summary>
        private const float ThickAir = 1f;
        private const float ThinAir = 0.4f;

        /// <summary>
        /// The whole battery. Directional cases expand to six, so the count is larger than the
        /// list below reads.
        /// </summary>
        public static List<Scenario> All()
        {
            List<Scenario> scenarios = new List<Scenario>();

            // ---- environment: the outside alone, with the ship idle ----------------------------
            // Every one of these is the ship doing nothing, so any temperature it reaches came from
            // outside it. That is what makes them the controls for everything below.

            scenarios.Add(new Scenario
            {
                Name = "vacuum-shadow",
                Question = "the cold reference: nothing in, nothing out but radiation",
                Environment = t => Worlds.Shadow(),
                Load = ShipLoad.State.Idle,
            });

            scenarios.Add(new Scenario
            {
                Name = "vacuum-sunlit",
                Question = "external heating at its worst: one face held to the sun",
                Environment = t => Worlds.Space(Vector3.Forward),
                Load = ShipLoad.State.Idle,
            });

            scenarios.Add(new Scenario
            {
                Name = "orbit-cycling",
                Question = "thermal inertia: does a ship average the day or chase it",
                Environment = t => ((int)(t / 900f) % 2) == 0 ? Worlds.Space(Vector3.Forward) : Worlds.Shadow(),
                Load = ShipLoad.State.Idle,
            });

            scenarios.Add(new Scenario
            {
                Name = "surface-hot-noon",
                Question = "external heating in air: hot ground, high sun, still",
                Environment = t => Worlds.PlanetSurface(ThickAir, 0.5f),
                Load = ShipLoad.State.Idle,
            });

            scenarios.Add(new Scenario
            {
                Name = "surface-cold-night",
                Question = "external cooling in air: does anything freeze below a usable floor",
                Environment = t => Worlds.PlanetSurface(ThinAir, 0f),
                Load = ShipLoad.State.Idle,
            });

            scenarios.Add(new Scenario
            {
                Name = "surface-windy",
                Question = "forced convection at rest: how much wind alone is worth",
                Environment = t => Worlds.PlanetSurface(ThickAir, 0.5f, 60f),
                Load = ShipLoad.State.Idle,
            });

            scenarios.Add(new Scenario
            {
                Name = "underground",
                Question = "buried: no sun, no wind, rock ambient",
                Environment = t => Worlds.Underground(ThickAir),
                Load = ShipLoad.State.Idle,
            });

            // ---- load: the inside alone, in the coldest environment there is -------------------
            // Run in shadow on purpose. Anything these reach is the ship heating itself, with no
            // sun or air to argue about, which is the only way to attribute it.

            scenarios.Add(new Scenario
            {
                Name = "idle",
                Question = "G1: does a parked ship survive doing nothing",
                Environment = t => Worlds.Shadow(),
                Load = ShipLoad.State.Idle,
            });

            scenarios.Add(new Scenario
            {
                Name = "full-electrical",
                Question = "G2: every consumer at rating, reactors supplying what they ask",
                Environment = t => Worlds.Shadow(),
                Load = ShipLoad.State.Full,
            });

            scenarios.Add(new Scenario
            {
                Name = "all-peak",
                Question = "where heat concentrates when everything runs at once",
                Environment = t => Worlds.Shadow(),
                Load = ShipLoad.State.Everything,
            });

            // Thrust, one direction at a time. The hot-spot case: a ship burning forward heats the
            // thrusters at its stern and nothing at its bow, and which blocks those are is a
            // property of the design rather than of the load.
            for (int face = 0; face < Face.Count; face++)
            {
                int direction = face;
                scenarios.Add(new Scenario
                {
                    Name = "burn-" + ShipLoad.DirectionName(direction),
                    Question = "hot spots with thrusters firing " + ShipLoad.DirectionName(direction),
                    Environment = t => Worlds.Shadow(),
                    Load = ShipLoad.State.Burn(direction),
                });
            }

            // ---- motion: friction and airflow, per direction of travel -------------------------
            // Friction heats the leading face with the cube of airspeed, and airflow cools every
            // face. Which one wins is a question of speed, and *where* it lands is a question of
            // heading — so both are enumerated.

            float[] speeds = { 50f, 100f };
            foreach (float speed in speeds)
            {
                float knots = speed;
                scenarios.Add(new Scenario
                {
                    Name = "flight-" + (int)speed,
                    Question = "friction against airflow at " + (int)speed + " m/s: which wins",
                    Environment = t => Worlds.Flight(ThickAir, knots),
                    Load = ShipLoad.State.Burn(Face.Forward),
                    Seconds = 3600f,
                });
            }

            // ---- wind against velocity, isolated and composed ----------------------------------
            // The hull feels one scalar: the relative wind. A session with both a storm and a
            // moving ship cannot attribute a heat to either, so the battery holds each still while
            // the other moves, then runs the two compositions where the sum does something neither
            // part does. flight-100 above is the velocity-only case.

            scenarios.Add(new Scenario
            {
                Name = "storm-parked",
                Question = "wind alone: a parked hull in a 100 m/s gale heats exactly as flight-100 does",
                Environment = t => Worlds.Storm(ThickAir, 100f),
                Load = ShipLoad.State.Idle,
                Seconds = 3600f,
            });

            scenarios.Add(new Scenario
            {
                Name = "flight-headwind",
                Question = "composition: 40 of wind against 60 of speed trips a threshold neither reaches alone",
                Environment = t => Worlds.WindAndMotion(ThickAir, 40f, Vector3.Forward, Vector3.Backward * 60f),
                Load = ShipLoad.State.Burn(Face.Forward),
                Seconds = 3600f,
            });

            scenarios.Add(new Scenario
            {
                Name = "flight-downwind",
                Question = "composition: 80 of speed in a 60 m/s tailwind is 20 of airflow — no friction at full throttle",
                Environment = t => Worlds.WindAndMotion(ThickAir, 60f, Vector3.Forward, Vector3.Forward * 80f),
                Load = ShipLoad.State.Burn(Face.Forward),
                Seconds = 3600f,
            });

            scenarios.Add(new Scenario
            {
                Name = "reentry",
                Question = "the leading face at terminal speed in thick air",
                Environment = t => Worlds.Flight(ThickAir, 200f),
                Load = ShipLoad.State.Idle,
                Seconds = 1200f,
            });

            // ---- transients --------------------------------------------------------------------

            scenarios.Add(new Scenario
            {
                Name = "recovery",
                Question = "G5: from a full burn, throttled to idle, does it come back",
                Environment = t => Worlds.Shadow(),
                Load = ShipLoad.State.Everything,
                Seconds = 1200f,
            });

            return scenarios;
        }

        /// <summary>
        /// Runs one ship through one scenario and reads the result.
        ///
        /// Diagnostics are on, because the per-mechanism watt shares are most of what makes an
        /// outcome interpretable — a hot ship is a different problem depending on whether the heat
        /// came from its reactors, the sun or the air it is pushing through.
        /// </summary>
        public static ScenarioOutcome Run(Blueprints.Ship ship, Scenario scenario,
            ThermalSettings settings = null)
        {
            ShipAssembly assembly = ship.Build(settings ?? new ThermalSettings());
            assembly.CollectDiagnostics(true);

            ShipLoad.Apply(assembly, scenario.Load);

            AssemblyRunner runner = new AssemblyRunner(assembly);
            runner.Environment = scenario.Environment;

            // The recovery case is the only one that changes state mid-run: it burns until the
            // ship is as hot as it is going to get, then everything is switched off and the
            // question becomes whether it comes back down.
            if (scenario.Name == "recovery")
            {
                RunUntilSettled(runner, scenario.Seconds);
                ShipLoad.Apply(assembly, ShipLoad.State.Idle);
                RunUntilSettled(runner, scenario.Seconds);
            }
            else
            {
                RunUntilSettled(runner, scenario.Seconds);
            }

            ScenarioOutcome outcome = ScenarioOutcome.Read(assembly, ship.Name, scenario.Name);
            outcome.WorkshopId = ship.WorkshopId;
            outcome.SecondsToCritical = runner.SecondsToCritical;

            Settle(outcome, runner);
            return outcome;
        }

        /// <summary>Kelvin of movement across a chunk below which a run is called settled.</summary>
        public const float SettleWithin = 0.25f;

        /// <summary>Simulated seconds per chunk. The resolution the settle test can see.</summary>
        public const float Chunk = 60f;

        /// <summary>
        /// Steps until the hottest block stops moving, or until the ceiling is reached.
        ///
        /// Almost every run in the battery is flat long before its clock runs out — a small hull in
        /// shadow settles in minutes and then sits there. Running to the ceiling regardless is most
        /// of the cost of the lab and buys nothing. Stopping on equilibrium also makes the run
        /// length itself a measurement: how long a ship takes to settle is its thermal inertia, and
        /// one that never settles is saying something too.
        /// </summary>
        private static void RunUntilSettled(AssemblyRunner runner, float ceiling)
        {
            float previous = float.NaN;
            float elapsed = 0f;

            while (elapsed < ceiling)
            {
                float chunk = Math.Min(Chunk, ceiling - elapsed);
                runner.Run(chunk);
                elapsed += chunk;

                float hottest = runner.Hottest[runner.Hottest.Count - 1];
                if (!float.IsNaN(previous) && Math.Abs(hottest - previous) <= SettleWithin) return;

                previous = hottest;
            }
        }

        /// <summary>
        /// The two figures that are about time rather than about the end state: how long the ship
        /// took to get within 5 K of where it finished, and the fastest it ever moved.
        ///
        /// Both are read from the sample history, so both are only as precise as the sampling. That
        /// is deliberate — resolving them finely would mean sampling every step, and they are used
        /// to sort ships rather than to decide anything on their own.
        /// </summary>
        private static void Settle(ScenarioOutcome outcome, AssemblyRunner runner)
        {
            List<float> samples = runner.Hottest;
            if (samples.Count < 2) return;

            float final = outcome.PeakKelvin;
            float fastest = 0f;

            for (int i = 1; i < samples.Count; i++)
            {
                float rate = Math.Abs(samples[i] - samples[i - 1]) / Chunk;
                if (rate > fastest) fastest = rate;

                if (outcome.SecondsToSettle < 0f && Math.Abs(samples[i] - final) <= 5f)
                {
                    outcome.SecondsToSettle = (i + 1) * Chunk;
                }
            }

            outcome.PeakRateKelvinPerSecond = fastest;

            BulkDrift(outcome, runner);
        }

        /// <summary>Samples at the end of a run across which the bulk is asked to hold still.</summary>
        public const int BulkWindow = 3;

        /// <summary>
        /// How fast the ship as a whole was still moving when the run ended.
        ///
        /// The run stops when the hottest block stops, which on a large hull happens long before
        /// the armour has finished shedding what it started with. Reading the bulk temperature
        /// across the last few samples is what tells the difference between a ship that has settled
        /// and one whose worst block merely has.
        ///
        /// The window is a net rate rather than a worst step because the quantity wanted is the
        /// heat actually going into store or coming out of it, and a single noisy step between two
        /// substep counts is not that.
        /// </summary>
        private static void BulkDrift(ScenarioOutcome outcome, AssemblyRunner runner)
        {
            List<float> samples = runner.Bulk;
            if (samples.Count < BulkWindow + 1) return;

            float first = samples[samples.Count - 1 - BulkWindow];
            float last = samples[samples.Count - 1];

            outcome.BulkDriftKelvinPerSecond = (last - first) / (BulkWindow * Chunk);
            outcome.BulkDriftWatts = outcome.ThermalMass * outcome.BulkDriftKelvinPerSecond;
        }
    }
}
