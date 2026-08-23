using System;
using System.Collections.Generic;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// How long a client that started from the wrong temperatures stays wrong.
    ///
    /// <para>
    /// Block temperatures are not replicated. A client re-simulates from the same inputs and a
    /// client joining mid-session starts from whatever the world was last *saved* at, so its
    /// readouts disagree with the server's by however much the ship moved since. The open question
    /// was how far a client may drift before it has to be corrected — and the answer this lab
    /// exists to test is that it may not need correcting at all, because **the model is
    /// dissipative**: two runs of the same grid with the same inputs and different initial
    /// temperatures converge, and what decides whether a protocol is needed is how fast.
    /// </para>
    ///
    /// <para>
    /// **The perturbation is the real one.** The client is not started from noise or from ambient:
    /// it is started from the state the server was actually in a stated number of simulated seconds
    /// earlier, which is exactly what restoring a save gives it. Both then run forward on identical
    /// inputs, because everything else a client and a server disagree about — settings, the work
    /// budget, room pressure — is replicated or server-authoritative already.
    /// </para>
    ///
    /// <para>
    /// See known-issues.md, and [backlog.md](../../docs/backlog.md) `B4`.
    /// </para>
    /// </summary>
    public static class ClientDriftLab
    {
        /// <summary>One moment in the comparison.</summary>
        public class Sample
        {
            /// <summary>Simulated seconds since the client joined.</summary>
            public float Seconds;

            /// <summary>The largest disagreement between the two runs, K.</summary>
            public float MaxKelvin;

            /// <summary>The mean disagreement over every block, K.</summary>
            public float MeanKelvin;

            /// <summary>
            /// Blocks the two runs put on opposite sides of their own critical temperature.
            ///
            /// The disagreement that is not cosmetic: a client showing safe where the server is
            /// damaging is the readout a player trusts being wrong about the thing it is for.
            /// </summary>
            public int DisagreeOnCritical;

            /// <summary>
            /// Blocks the server has within a tenth of their own critical temperature or past it.
            ///
            /// **The size of the set a correction would have to carry.** The disagreement is not a
            /// uniform offset — the worst block is several times the mean — so a per-grid scalar
            /// would leave exactly the blocks that matter wrong. What a correction has to replicate
            /// is the hot tail, and this is how long that tail is. A tenth, because that is the
            /// band a warning is about: below it, being wrong by a few kelvin changes nothing a
            /// player would do.
            /// </summary>
            public int HotBlocks;
        }

        public class Run
        {
            /// <summary>How stale the client's starting state was, in simulated seconds.</summary>
            public float StaleSeconds;

            public string Scenario;
            public int Blocks;

            /// <summary>The disagreement at the moment the client joined.</summary>
            public float JoinKelvin;

            public readonly List<Sample> Samples = new List<Sample>();

            /// <summary>
            /// Simulated seconds until the largest disagreement falls under a kelvin, or -1 if it
            /// never does within the clock.
            /// </summary>
            public float SecondsToAgree = -1f;

            /// <summary>The same, to under ten kelvin.</summary>
            public float SecondsToTenKelvin = -1f;

            /// <summary>
            /// Simulated seconds for which at least one block was on the wrong side of its own
            /// critical temperature.
            /// </summary>
            public float SecondsMisreadingCritical;
        }

        /// <summary>Seconds between samples. Fine enough to see a fast convergence.</summary>
        public const float SampleSeconds = 5f;

        /// <summary>
        /// Runs one comparison: warm a hull, take the state <paramref name="staleSeconds"/> before
        /// the join, and run the two forward together for <paramref name="watchSeconds"/>.
        /// </summary>
        public static Run Measure(string scenario, float staleSeconds, float watchSeconds,
            int blocks = 2000, ThermalSettings settings = null)
        {
            ThermalSettings world = settings ?? new ThermalSettings().Derive();
            Func<float, EnvironmentSample> environment = Environment(scenario);

            ThermalSimulation server = Hulls.Driven(world, blocks);

            // Long enough that the hull is somewhere interesting rather than at its start, and
            // that the stale state is genuinely behind.
            float warm = 120f;
            Step(server, environment, warm);

            float[] stale = Temperatures(server);
            Step(server, environment, staleSeconds);

            // The client is the same hull, built the same way, put into the state the server was in
            // staleSeconds ago. Built rather than copied so nothing is shared between them.
            ThermalSimulation client = Hulls.Driven(world, blocks);
            Step(client, environment, warm);
            Restore(client, stale);

            Run run = new Run
            {
                StaleSeconds = staleSeconds,
                Scenario = scenario,
                Blocks = server.Solver.Nodes.Count,
                JoinKelvin = MaxDifference(server, client),
            };

            float elapsed = 0f;
            while (elapsed < watchSeconds)
            {
                Step(server, environment, SampleSeconds);
                Step(client, environment, SampleSeconds);
                elapsed += SampleSeconds;

                Sample sample = Compare(server, client);
                sample.Seconds = elapsed;
                run.Samples.Add(sample);

                if (sample.DisagreeOnCritical > 0) run.SecondsMisreadingCritical += SampleSeconds;
                if (run.SecondsToTenKelvin < 0f && sample.MaxKelvin < 10f) run.SecondsToTenKelvin = elapsed;
                if (run.SecondsToAgree < 0f && sample.MaxKelvin < 1f) run.SecondsToAgree = elapsed;
            }

            return run;
        }

        /// <summary>The environments a client and a server would both be computing.</summary>
        private static Func<float, EnvironmentSample> Environment(string scenario)
        {
            if (scenario == "sunlit") return t => Worlds.Space(new Vector3(0.3f, 0.9f, 0.2f));
            if (scenario == "planet") return t => Worlds.PlanetSurface(1f, 0.5f);
            return t => Worlds.Shadow();
        }

        private static void Step(ThermalSimulation simulation,
            Func<float, EnvironmentSample> environment, float seconds)
        {
            int steps = (int)Math.Round(seconds / simulation.Settings.StepSeconds);
            for (int i = 0; i < steps; i++) simulation.StepExact(1, environment(0f));
        }

        private static float[] Temperatures(ThermalSimulation simulation)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            float[] values = new float[nodes.Count];
            for (int i = 0; i < nodes.Count; i++) values[i] = nodes[i].Temperature;
            return values;
        }

        /// <summary>
        /// Puts a run into a saved state, exactly as loading a world does: the temperature is the
        /// one value a host may write from outside a step, and the solver re-reads it.
        /// </summary>
        private static void Restore(ThermalSimulation simulation, float[] temperatures)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            int count = Math.Min(nodes.Count, temperatures.Length);
            for (int i = 0; i < count; i++) nodes[i].Temperature = temperatures[i];
        }

        private static float MaxDifference(ThermalSimulation a, ThermalSimulation b)
        {
            return Compare(a, b).MaxKelvin;
        }

        private static Sample Compare(ThermalSimulation server, ThermalSimulation client)
        {
            IList<ThermalNode> mine = server.Solver.Nodes;
            IList<ThermalNode> theirs = client.Solver.Nodes;

            Sample sample = new Sample();
            int count = Math.Min(mine.Count, theirs.Count);
            if (count == 0) return sample;

            double total = 0d;
            for (int i = 0; i < count; i++)
            {
                float difference = Math.Abs(mine[i].Temperature - theirs[i].Temperature);
                total += difference;
                if (difference > sample.MaxKelvin) sample.MaxKelvin = difference;

                float critical = mine[i].Thermal.CriticalTemperature;
                if (critical <= 0f) continue;

                if (mine[i].Temperature > critical * 0.9f) sample.HotBlocks++;

                bool serverOver = mine[i].Temperature > critical;
                bool clientOver = theirs[i].Temperature > critical;
                if (serverOver != clientOver) sample.DisagreeOnCritical++;
            }

            sample.MeanKelvin = (float)(total / count);
            return sample;
        }

        /// <summary>The whole comparison as a table, for the command and the report.</summary>
        public static string Report(IList<Run> runs)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("scenario   stale s   join K   to 10 K    to 1 K   misreading critical");

            for (int i = 0; i < runs.Count; i++)
            {
                Run run = runs[i];
                text.AppendLine(string.Format(
                    "{0,-10}{1,8:n0}{2,9:n1}{3,10}{4,10}{5,22}",
                    run.Scenario, run.StaleSeconds, run.JoinKelvin,
                    run.SecondsToTenKelvin < 0f ? "never" : run.SecondsToTenKelvin.ToString("n0") + " s",
                    run.SecondsToAgree < 0f ? "never" : run.SecondsToAgree.ToString("n0") + " s",
                    run.SecondsMisreadingCritical.ToString("n0") + " s"));
            }

            return text.ToString();
        }
    }
}
