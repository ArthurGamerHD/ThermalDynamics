using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// The scenario battery, built along four independent axes — environment, motion, load and
    /// configuration — with a scenario a point on all four at once. **A scenario earns its place by
    /// answering what nothing else answers** (`M8`), and directional cases are enumerated rather than
    /// sampled, because a ship is not symmetric.
    /// See balance-lab.md, Run the scenario battery.
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

            /// <summary>
            /// A second state to switch to partway through, or null for a scenario that holds one
            /// state throughout.
            ///
            /// **The shape a transient needs, and the reason it is here rather than named in the
            /// runner.** `recovery` has switched state mid-run since it was written, by a test on
            /// its own name — which works for one case and cannot be reused, so the next transient
            /// would be a second special case in the same method. This is that generalised.
            /// </summary>
            public ShipLoad.State Then;

            /// <summary>
            /// Simulated seconds the first state runs for before <see cref="Then"/> replaces it,
            /// as a function of the ship — because the length of a transient is a property of the
            /// hardware and not a number a scenario gets to choose.
            /// </summary>
            public Func<ShipAssembly, float> ThenAfterSeconds;
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
                Name = "jump-charge",
                Question = "G8: the ship charges its drives, finishes, and holds — the real event",
                Environment = t => Worlds.Shadow(),
                Load = ShipLoad.State.Full,
                Then = ShipLoad.State.Charged,
                ThenAfterSeconds = ShipLoad.ChargeSeconds,
            });

            scenarios.Add(new Scenario
            {
                Name = "full-electrical-charged",
                Question = "the same load with the jump drives charged rather than charging",
                Environment = t => Worlds.Shadow(),
                Load = ShipLoad.State.Charged,
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

            // **The ladder runs past the game's own limit on purpose.** Vanilla Space Engineers
            // caps a grid at 100 m/s, and the servers most of this mod's players are on raise that
            // — 300 m/s is the common setting. A balance that is only ever measured at the vanilla
            // cap says nothing about the world it will be played in, and friction goes as the cube
            // of airspeed, so the gap between 100 and 300 is a factor of twenty-seven in the one
            // term that is not bounded by ambient.
            float[] speeds = { 50f, 100f, 300f };
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
                Name = "storm-300",
                Question = "wind alone at a raised speed limit: a parked hull in a 300 m/s gale",
                Environment = t => Worlds.Storm(ThickAir, 300f),
                Load = ShipLoad.State.Idle,
                Seconds = 3600f,
            });

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
            return Run(ship, scenario, settings, -1f);
        }

        /// <summary>
        /// The same run stopped at a stated simulated clock rather than at equilibrium.
        ///
        /// <para>
        /// **For a paired experiment, where the stopping rule cannot be part of the difference.**
        /// <see cref="RunUntilSettled"/> stops when the hottest block moves less than
        /// <see cref="SettleWithin"/> in a chunk, so two configurations of the same ship stop at
        /// different instants — and a difference smaller than that tolerance, which is what a
        /// per-block substep cap produces, is then a reading of the stopping rule rather than of
        /// the cap (`M1`, `P6`). The control arm runs first and hands its own elapsed clock here.
        /// </para>
        ///
        /// <para>
        /// A negative or zero clock means *stop at equilibrium*, which is what
        /// <see cref="Run(Blueprints.Ship, Scenario, ThermalSettings)"/> asks for.
        /// </para>
        /// </summary>
        public static ScenarioOutcome RunForSeconds(Blueprints.Ship ship, Scenario scenario,
            float seconds, ThermalSettings settings = null)
        {
            return Run(ship, scenario, settings, seconds);
        }

        private static ScenarioOutcome Run(Blueprints.Ship ship, Scenario scenario,
            ThermalSettings settings, float fixedSeconds)
        {
            ShipAssembly assembly = ship.Build(settings ?? new ThermalSettings());
            assembly.CollectDiagnostics(true);

            ShipLoad.Apply(assembly, scenario.Load);

            AssemblyRunner runner = new AssemblyRunner(assembly);
            runner.Environment = scenario.Environment;
            runner.Integrity = GameBlocks.IntegrityOf;

            // The recovery case is the only one that changes state mid-run: it burns until the
            // ship is as hot as it is going to get, then everything is switched off and the
            // question becomes whether it comes back down.
            if (scenario.Name == "recovery")
            {
                // Half the clock each side of the throttle, so a fixed-clock pair splits where the
                // settle-stopped control did rather than at some other point in the event.
                float half = fixedSeconds > 0f ? fixedSeconds * 0.5f : scenario.Seconds;
                Advance(runner, half, fixedSeconds);
                ShipLoad.Apply(assembly, ShipLoad.State.Idle);
                Advance(runner, fixedSeconds > 0f ? fixedSeconds - half : scenario.Seconds,
                    fixedSeconds);
            }
            else if (scenario.Then != null)
            {
                // **Run to the clock, not to a settle.** The first half of a transient is an event
                // with a length the hardware sets, and stopping it early because the hull stopped
                // moving would measure a shorter event than the ship actually has — which is the
                // whole quantity the case exists to report.
                float first = scenario.ThenAfterSeconds == null
                    ? scenario.Seconds
                    : scenario.ThenAfterSeconds(assembly);

                float ceiling = fixedSeconds > 0f ? fixedSeconds : scenario.Seconds;
                if (first > ceiling) first = ceiling;
                if (first > 0f) runner.Run(first);

                ShipLoad.Apply(assembly, scenario.Then);
                Advance(runner, ceiling - first, fixedSeconds);
            }
            else
            {
                Advance(runner, fixedSeconds > 0f ? fixedSeconds : scenario.Seconds, fixedSeconds);
            }

            ScenarioOutcome outcome = ScenarioOutcome.Read(assembly, ship.Name, scenario.Name);
            outcome.WorkshopId = ship.WorkshopId;
            outcome.SecondsToCritical = runner.SecondsToCritical;
            outcome.SecondsToFirstLoss = runner.SecondsToFirstLoss;
            outcome.RunSeconds = runner.ElapsedSeconds;

            Settle(outcome, runner);
            return outcome;
        }

        /// <summary>Kelvin of movement across a chunk below which a run is called settled.</summary>
        public const float SettleWithin = 0.25f;

        /// <summary>Simulated seconds per chunk. The resolution the settle test can see.</summary>
        public const float Chunk = 60f;

        /// <summary>
        /// Steps a phase of a run: to equilibrium when the caller wants equilibrium, and to the
        /// clock when the caller has one to match.
        /// </summary>
        private static void Advance(AssemblyRunner runner, float seconds, float fixedSeconds)
        {
            if (seconds <= 0f) return;

            if (fixedSeconds > 0f)
            {
                // **In the same chunks the settle path uses**, because the sample history is what
                // the settling time and the peak rate are read from — one sample over the whole
                // span would leave the paired arm with two columns the control has and it does not.
                float done = 0f;
                while (done < seconds)
                {
                    float chunk = Math.Min(Chunk, seconds - done);
                    runner.Run(chunk);
                    done += chunk;
                }

                return;
            }

            RunUntilSettled(runner, seconds);
        }

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
            float lastStep = float.NaN;
            float elapsed = 0f;

            while (elapsed < ceiling)
            {
                float chunk = Math.Min(Chunk, ceiling - elapsed);
                runner.Run(chunk);
                elapsed += chunk;

                float hottest = runner.Hottest[runner.Hottest.Count - 1];
                if (!float.IsNaN(previous))
                {
                    float step = hottest - previous;
                    if (Math.Abs(step) <= SettleWithin) return;
                    if (Extrapolates && Converged(step, lastStep)) return;
                    lastStep = step;
                }

                previous = hottest;
            }
        }

        /// <summary>
        /// Whether what is left of a decaying approach is already inside <see cref="SettleWithin"/>.
        ///
        /// <para>
        /// **The plain test asks how far the hull moved last chunk; this asks how far it has left to
        /// go.** They are the same question only when the approach is fast. `vacuum-shadow` is the
        /// case where they are not: a hull radiating into the dark decays towards its floor, so its
        /// per-chunk movement shrinks geometrically and creeps under a fixed 0.25 K only after most
        /// of an hour. Measured on the 2026-08-28 air walk it is the **one scenario of four that
        /// never satisfies the plain test** — every ship runs its full 1,800 s while the other three
        /// stop at a median 120 — and it is **33.7 %** of the whole walk's cost.
        /// </para>
        ///
        /// <para>
        /// **So the rule is Richardson's, not a shorter clock.** If a chunk moved the hull `d` and
        /// the one before moved it `p` in the same direction, the ratio `r = d / p` is the decay per
        /// chunk and what remains is the sum of the rest of that series, `d * r / (1 - r)`. When
        /// that is inside the tolerance the run is *already* where it is going, and simulating the
        /// rest of the way changes the answer by less than the tolerance the plain test allows.
        /// This is a cheaper route to the same reading rather than a looser reading, which is why
        /// it is bounded by the same constant.
        /// </para>
        ///
        /// <para>
        /// **Every guard here is a refusal to extrapolate something that is not decaying.** Both
        /// steps must be in the same direction, the ratio must be under
        /// <see cref="ConvergedRatio"/> — a hull creeping at 0.999 per chunk is not converging on
        /// any timescale worth trusting an extrapolation over — and neither step may be zero. A run
        /// that oscillates, accelerates, or sits exactly still falls through to the plain test,
        /// which is the behaviour every dataset before this was collected under.
        /// </para>
        /// </summary>
        public static bool Converged(float step, float lastStep)
        {
            if (float.IsNaN(lastStep) || lastStep == 0f || step == 0f) return false;

            // Same direction, or this is not a decay.
            if ((step > 0f) != (lastStep > 0f)) return false;

            float ratio = Math.Abs(step) / Math.Abs(lastStep);
            if (ratio >= ConvergedRatio) return false;

            float remaining = Math.Abs(step) * ratio / (1f - ratio);
            return remaining <= SettleWithin;
        }

        /// <summary>
        /// The slowest per-chunk decay an extrapolation is trusted over.
        ///
        /// A geometric series only sums to something small when its ratio is comfortably under one,
        /// and the closer the ratio runs to one the more the estimate rests on two samples of a
        /// curve that is not exactly geometric — the hull sheds by radiation, which goes as the
        /// fourth power and is only locally exponential. At 0.9 the tail this skips is ten chunks
        /// of movement; above it the run is left to the plain test.
        /// </summary>
        public const float ConvergedRatio = 0.9f;

        /// <summary>
        /// Whether <see cref="Converged"/> is consulted, from <c>THERMAL_SETTLE_EXTRAPOLATE</c>.
        ///
        /// <para>
        /// **Off by default, because turning it on moves every reading this repository has taken.**
        /// The stopping point is where a run's peak, demand and step cost are read, so a walk with
        /// this on is not comparable with one taken without it — which is `M1` exactly. It is a
        /// switch so the two can be walked as arms of one experiment on one build, and the default
        /// moves when the paired measurement says what it costs, in a commit that cites it (`E11`).
        /// </para>
        ///
        /// <para>
        /// Read once. A run that consulted the environment per chunk would let a sweep change
        /// stopping rules half way through itself.
        /// </para>
        /// </summary>
        public static readonly bool Extrapolates =
            (System.Environment.GetEnvironmentVariable("THERMAL_SETTLE_EXTRAPOLATE") ?? "") == "1";

        /// <summary>
        /// The two figures that are about time rather than about the end state: how long the ship
        /// took to get within 5 K of where it finished, and the fastest it ever moved.
        ///
        /// Both are read from the sample history, so both are only as precise as the sampling. That
        /// is deliberate — resolving them finely would mean sampling every step, and they are used
        /// to sort ships rather than to decide anything on their own.
        /// </summary>

        /// <summary>Kelvin of the final value a sample must be inside to count as settled.</summary>
        public const float SettledWithinOfFinal = 5f;

        /// <summary>
        /// Seconds until the hottest-block trace first came within
        /// <see cref="SettledWithinOfFinal"/> of where it ended, or -1 if it never did.
        ///
        /// <para>
        /// **This is a settling time only when the block moved.** A hull that barely changed over
        /// the whole run is inside 5 K of its final value at the first sample and reports
        /// <see cref="Chunk"/>, so *has not begun* and *has finished* come out as the same number.
        /// It is not the limitation <see cref="ScenarioOutcome.BulkDriftKelvinPerSecond"/> guards —
        /// that one is one block settling ahead of the hull, and a hull that never started has a
        /// small drift too. Pinned by <c>SettleReadingTests</c>, and it is why `G8`'s settling half
        /// is scored on a scenario where the hull is driven somewhere rather than at idle.
        /// </para>
        /// </summary>
        public static float SettleSeconds(IList<float> samples, float final)
        {
            if (samples == null || samples.Count < 2) return -1f;

            for (int i = 1; i < samples.Count; i++)
            {
                if (Math.Abs(samples[i] - final) <= SettledWithinOfFinal) return (i + 1) * Chunk;
            }

            return -1f;
        }

        private static void Settle(ScenarioOutcome outcome, AssemblyRunner runner)
        {
            List<float> samples = runner.Hottest;
            if (samples.Count < 2) return;

            float fastest = 0f;

            for (int i = 1; i < samples.Count; i++)
            {
                float rate = Math.Abs(samples[i] - samples[i - 1]) / Chunk;
                if (rate > fastest) fastest = rate;
            }

            outcome.SecondsToSettle = SettleSeconds(samples, outcome.PeakKelvin);
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
