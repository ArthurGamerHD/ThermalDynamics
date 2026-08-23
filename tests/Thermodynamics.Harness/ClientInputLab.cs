using System;
using System.Collections.Generic;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// **Every input a client drives its own simulation from, degraded one at a time and then all
    /// at once**, with the readout scored against the server's.
    ///
    /// <para>
    /// [ClientDriftLab](ClientDriftLab.cs) answers a narrower question: a client that started from
    /// the wrong temperatures and is otherwise perfect. That is one cause of disagreement and it is
    /// the benign one, because it is a **perturbation** — the model is dissipative, so a single
    /// wrong state decays on its own. A client's inputs are not perturbations. A sun direction that
    /// is permanently a degree off, a reactor whose power arrives a second late, a hull the client
    /// believes is in thinner air: each of those is a **standing bias**, and a bias does not decay,
    /// it settles. Nothing measured before this told the two apart.
    /// </para>
    ///
    /// <para>
    /// **The column that tells them apart is the standing error.** Every run reports the worst
    /// disagreement it reached and the mean over its final third; a perturbation ends near zero
    /// whatever it peaked at, and a bias ends where it settled. A degradation whose standing error
    /// is not zero is one the correction is not merely accelerating but holding together.
    /// </para>
    ///
    /// <para>
    /// **What this cannot see, stated rather than assumed.** It runs two instances of the same
    /// solver in one process, so it models neither the transport — latency, loss, the game's send
    /// queue — nor the engine behaviour behind each degraded input. Every magnitude below is a
    /// knob this lab turns, not a figure measured from a session, and the ones that are guesses say
    /// so where they are declared. What the lab does answer is the shape: which inputs bias and
    /// which perturb, and whether the correction reaches each.
    /// </para>
    ///
    /// <para>
    /// See known-issues.md and [backlog.md](../../docs/backlog.md) `B4`.
    /// </para>
    /// </summary>
    public static class ClientInputLab
    {
        /// <summary>
        /// One way a client's view of the world is worse than the server's.
        ///
        /// <para>
        /// Every field defaults to *not degraded*, so a case names only what it breaks and the
        /// combined case is the union of the others rather than a separate opinion about what a bad
        /// client looks like.
        /// </para>
        /// </summary>
        public class Degradation
        {
            /// <summary>What this case is called in the table.</summary>
            public string Name = "none";

            /// <summary>One line on what is being degraded and why that is what the engine does.</summary>
            public string Because = "";

            /// <summary>
            /// The client joined holding the server's state from this many simulated seconds ago.
            /// **A perturbation**: it is what restoring a save gives, and it happens once.
            /// </summary>
            public float StaleSeconds;

            /// <summary>
            /// Simulated seconds between the client dropping its solver backlog, which is what
            /// <see cref="SimulationScheduler.StepsDue"/> does on a frame long enough to exceed its
            /// per-frame cap. **A repeated perturbation.**
            /// </summary>
            public float HitchEverySeconds;

            /// <summary>How much simulated time each hitch costs.</summary>
            public float HitchLosesSeconds = 5f;

            /// <summary>
            /// The client samples the environment the server had this many seconds ago.
            /// **A bias**: grid position and orientation replicate on their own schedule, so a
            /// moving ship's client-side sun, altitude and air are always of a moment ago.
            /// </summary>
            public float EnvironmentLagSeconds;

            /// <summary>
            /// The client's sun direction is off by this many degrees, permanently.
            /// **A bias**, and the one with no measured magnitude behind it — how far a client's
            /// replicated orientation trails the server's is engine behaviour this lab cannot see.
            /// </summary>
            public float SunAngleDegrees;

            /// <summary>
            /// The client's block power is the server's from this many seconds ago. **A bias while
            /// the load is moving**: a reactor's output reaches a client through the game's own
            /// block replication, which is neither instant nor continuous.
            /// </summary>
            public float PowerLagSeconds;

            /// <summary>
            /// The client's block power is wrong by this share, permanently. **A bias**, and it
            /// stands in for the quantisation and rounding in whatever the game replicates rather
            /// than for a measured error.
            /// </summary>
            public float PowerErrorShare;

            /// <summary>
            /// The client's air density is wrong by this much, 0..1. **A bias**: the game's own gas
            /// system decides what is pressurised, and a client's answer can differ — which matters
            /// because convection is the strongest path a hull has.
            /// </summary>
            public float AirDensityError;

            /// <summary>
            /// The client is running the shipped defaults while the server is not. **A bias, and
            /// the worst case of one that should not happen**: settings replicate, and a client
            /// fetches them on load, so this is what a fetch that never landed would look like.
            /// </summary>
            public bool OnDefaultSettings;
        }

        /// <summary>What one degraded client did, with the correction off or on.</summary>
        public class Result
        {
            public string Name;
            public string Because;

            /// <summary>The correction under which this was run. Never null.</summary>
            public ClientDriftLab.Correction Protocol = ClientDriftLab.Correction.None;

            public int Blocks;
            public string Scenario;

            /// <summary>The worst disagreement at any sample, K.</summary>
            public float PeakKelvin;

            /// <summary>
            /// The mean disagreement over the final third of the run, K.
            ///
            /// **This is the column that separates a perturbation from a bias.** A one-off wrong
            /// state decays to nothing whatever it peaked at; a wrong input settles somewhere and
            /// stays there. A run whose standing error is not near zero has not recovered and will
            /// not.
            /// </summary>
            public float StandingKelvin;

            /// <summary>Simulated seconds with at least one block on the wrong side of critical.</summary>
            public float SecondsMisreading;

            /// <summary>The worst count behind that, since one block wrong reads like three thousand.</summary>
            public int PeakDisagreeing;

            /// <summary>Bytes a second of simulated time the correction cost.</summary>
            public float BytesPerSecond;

            /// <summary>The largest single update, in blocks.</summary>
            public int PeakBlocksSent;

            /// <summary>
            /// The most blocks the **server** had past critical at any sample.
            ///
            /// **Without this the readout columns cannot be believed.** A run on a hull that never
            /// overheats reports nothing wrong with the client's readout, for the same reason a
            /// blank page has no spelling mistakes (`E8`). A zero here voids every readout column
            /// in the row, and the report says so rather than printing them.
            /// </summary>
            public int PeakServerCritical;
        }

        /// <summary>Seconds between readings.</summary>
        public const float SampleSeconds = 5f;

        /// <summary>
        /// How long the scripted load runs before it changes, s.
        ///
        /// **The load has to move or half these knobs are invisible.** A hull driven at a constant
        /// wattage has a client whose lagged power is the same number as the server's, so a power
        /// lag would measure as no error at all — which is a property of the rig, not of the engine.
        /// </summary>
        public const float LoadPeriodSeconds = 120f;

        /// <summary>
        /// The two states the scripted load alternates between, W a producer.
        ///
        /// <para>
        /// **Above the census figure, deliberately, and this is a choice with a cost.** The census
        /// wattage settles a hull *below* its rating, which is what the field measured — and a hull
        /// that never crosses a critical temperature makes every readout column in this table zero,
        /// so the sweep would report a correction that fixed everything by having nothing to fix
        /// (`E8`). The multiplier below is what puts the hull on the other side of critical without
        /// changing anything else about it, which is the same lever the benchmarks use. What it
        /// costs is that the kelvin figures here are of a hull under more load than a real one, so
        /// they are read as *which inputs bias* rather than as *how far a real client drifts*.
        /// </para>
        /// </summary>
        public const float LoadMultiplier = 3f;

        /// <summary>Full load, W a producer.</summary>
        public const float LoadedWatts = Census.ProducerWatts * LoadMultiplier;

        /// <summary>Idle is not zero: a ship with everything off still runs its own systems.</summary>
        public const float IdleWatts = Census.ProducerWatts * LoadMultiplier * 0.1f;

        /// <summary>
        /// Runs one degraded client against a server, and scores the readout.
        /// </summary>
        public static Result Measure(Degradation degradation, ClientDriftLab.Correction protocol,
            string scenario = "planet", float seconds = 600f, int blocks = 2000)
        {
            Degradation how = degradation ?? new Degradation();
            ClientDriftLab.Correction fix = protocol ?? ClientDriftLab.Correction.None;

            ThermalSettings world = new ThermalSettings().Derive();

            // The server's world differs from the shipped defaults, so that a client which never
            // received the settings is running different physics rather than the same physics.
            ThermalSettings served = new ThermalSettings();
            served.HeatTimeScale = world.HeatTimeScale * 0.5f;
            served = served.Derive();

            ThermalSettings serverWorld = how.OnDefaultSettings ? served : world;
            ThermalSettings clientWorld = how.OnDefaultSettings ? world : serverWorld;

            ThermalSimulation server = Hulls.Driven(serverWorld, blocks);
            ThermalSimulation client = Hulls.Driven(clientWorld, blocks);

            Result result = new Result
            {
                Name = how.Name,
                Because = how.Because,
                Protocol = fix,
                Blocks = server.Solver.Nodes.Count,
                Scenario = scenario,
            };

            // Warmed together so the run starts from a hull that is somewhere, and both sides see
            // the same warm-up: what is being measured is the degradation, not the start.
            float warm = 120f;
            Drive(server, LoadedWatts);
            Drive(client, LoadedWatts);
            Advance(server, Sample(scenario, 0f, how, false), warm);
            Advance(client, Sample(scenario, 0f, how, true), warm);

            if (how.StaleSeconds > 0f)
            {
                float[] stale = Temperatures(client);
                Advance(server, Sample(scenario, 0f, how, false), how.StaleSeconds);
                Restore(client, stale);
            }

            List<StoredTemperature> selection = new List<StoredTemperature>();
            List<StoredTemperature> received = new List<StoredTemperature>();
            List<float> disagreements = new List<float>();

            float tick = fix.IntervalSeconds > 0f
                ? Math.Min(SampleSeconds, fix.IntervalSeconds) : SampleSeconds;
            if (tick < serverWorld.StepSeconds) tick = serverWorld.StepSeconds;

            float elapsed = 0f;
            float sinceUpdate = float.MaxValue;
            float sinceSample = 0f;
            float sinceHitch = 0f;
            float owed = 0f;

            float servedWatts = float.NaN;
            float clientWatts = float.NaN;

            while (elapsed < seconds)
            {
                float now = elapsed;

                servedWatts = Retune(server, servedWatts, Watts(now));
                clientWatts = Retune(client, clientWatts,
                    Watts(now - how.PowerLagSeconds) * (1f + how.PowerErrorShare));

                Advance(server, Sample(scenario, now, how, false), tick);

                if (owed > 0f)
                {
                    owed -= tick;
                }
                else
                {
                    Advance(client, Sample(scenario, now - how.EnvironmentLagSeconds, how, true), tick);
                }

                elapsed += tick;
                sinceUpdate += tick;
                sinceSample += tick;
                sinceHitch += tick;

                if (how.HitchEverySeconds > 0f && sinceHitch >= how.HitchEverySeconds)
                {
                    sinceHitch = 0f;
                    owed += how.HitchLosesSeconds;
                }

                if (sinceSample >= SampleSeconds)
                {
                    float worst;
                    int over;
                    int disagreeing = Compare(server, client, out worst, out over);

                    if (over > result.PeakServerCritical) result.PeakServerCritical = over;
                    disagreements.Add(worst);
                    if (worst > result.PeakKelvin) result.PeakKelvin = worst;
                    if (disagreeing > result.PeakDisagreeing) result.PeakDisagreeing = disagreeing;
                    if (disagreeing > 0) result.SecondsMisreading += sinceSample;

                    sinceSample = 0f;
                }

                if (fix.IntervalSeconds > 0f && sinceUpdate >= fix.IntervalSeconds)
                {
                    sinceUpdate = 0f;

                    int inBand = server.ExportHotTail(fix.BandKelvin, fix.MaxBlocks, selection);
                    byte[] packet = HotTailCodec.Encode(selection);

                    if (HotTailCodec.TryDecode(packet, received))
                    {
                        client.ImportHotTail(received);
                        result.BytesPerSecond += packet.Length;
                        if (selection.Count > result.PeakBlocksSent) result.PeakBlocksSent = selection.Count;
                    }

                    if (inBand < 0) return result;   // unreachable; keeps inBand read where it is set
                }
            }

            result.BytesPerSecond = elapsed > 0f ? result.BytesPerSecond / elapsed : 0f;
            result.StandingKelvin = FinalThird(disagreements);
            return result;
        }

        /// <summary>
        /// The mean of the final third of a series.
        ///
        /// A third rather than the last sample, because one reading is noise and the question is
        /// where the run *settled*.
        /// </summary>
        private static float FinalThird(IList<float> series)
        {
            if (series == null || series.Count == 0) return 0f;

            int from = series.Count - Math.Max(1, series.Count / 3);
            double total = 0d;
            for (int i = from; i < series.Count; i++) total += series[i];

            return (float)(total / (series.Count - from));
        }

        /// <summary>The scripted load at a moment: a square wave, so a lag has something to lag.</summary>
        public static float Watts(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int half = (int)(seconds / LoadPeriodSeconds);
            return half % 2 == 0 ? LoadedWatts : IdleWatts;
        }

        /// <summary>Re-drives a hull only when its wattage actually changed.</summary>
        private static float Retune(ThermalSimulation simulation, float current, float wanted)
        {
            if (current == wanted) return current;
            Census.DriveCensus(simulation, wanted);
            return wanted;
        }

        /// <summary>
        /// The environment one side sees at a moment. The client's is built from the same function
        /// with its own lag already applied by the caller, plus whatever bias it carries.
        /// </summary>
        private static EnvironmentSample Sample(string scenario, float seconds, Degradation how,
            bool onClient)
        {
            if (seconds < 0f) seconds = 0f;

            if (scenario == "shadow") return Worlds.Shadow();

            if (scenario == "sunlit")
            {
                // A quarter turn every five minutes, so the sun moves across the hull and a lag or
                // an angle error is a different direction rather than the same one.
                float angle = seconds * (float)(Math.PI / 600d);
                if (onClient) angle += Degrees(how.SunAngleDegrees);

                return Worlds.Space(new Vector3(
                    (float)Math.Cos(angle), (float)Math.Sin(angle), 0.2f));
            }

            // A planet day, which drives ambient and sun together out of one number.
            float dayLength = 1200f;
            float timeOfDay = 0.25f + (seconds / dayLength);
            if (onClient) timeOfDay += Degrees(how.SunAngleDegrees) / (float)(2d * Math.PI);

            float air = 1f;
            if (onClient) air = Math.Max(0f, Math.Min(1f, air - how.AirDensityError));

            return Worlds.PlanetSurface(air, timeOfDay);
        }

        private static float Degrees(float degrees)
        {
            return (float)(degrees * Math.PI / 180d);
        }

        private static void Drive(ThermalSimulation simulation, float watts)
        {
            Census.DriveCensus(simulation, watts);
        }

        private static void Advance(ThermalSimulation simulation, EnvironmentSample environment,
            float seconds)
        {
            int steps = (int)Math.Round(seconds / simulation.Settings.StepSeconds);
            for (int i = 0; i < steps; i++) simulation.StepExact(1, environment);
        }

        private static float[] Temperatures(ThermalSimulation simulation)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            float[] values = new float[nodes.Count];
            for (int i = 0; i < nodes.Count; i++) values[i] = nodes[i].Temperature;
            return values;
        }

        private static void Restore(ThermalSimulation simulation, float[] temperatures)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            int count = Math.Min(nodes.Count, temperatures.Length);
            for (int i = 0; i < count; i++) nodes[i].Temperature = temperatures[i];
        }

        /// <summary>Blocks on opposite sides of critical, and the worst disagreement in kelvin.</summary>
        private static int Compare(ThermalSimulation server, ThermalSimulation client,
            out float worstKelvin, out int serverOverCritical)
        {
            IList<ThermalNode> mine = server.Solver.Nodes;
            IList<ThermalNode> theirs = client.Solver.Nodes;

            worstKelvin = 0f;
            serverOverCritical = 0;
            int disagreeing = 0;
            int count = Math.Min(mine.Count, theirs.Count);

            for (int i = 0; i < count; i++)
            {
                float difference = Math.Abs(mine[i].Temperature - theirs[i].Temperature);
                if (difference > worstKelvin) worstKelvin = difference;

                float critical = mine[i].Thermal.CriticalTemperature;
                if (critical <= 0f) continue;

                bool over = mine[i].Temperature > critical;
                if (over) serverOverCritical++;
                if (over != theirs[i].Temperature > critical) disagreeing++;
            }

            return disagreeing;
        }

        /// <summary>
        /// The standing set of degradations: each input alone, then all of them together.
        ///
        /// **The combined case is the union of the others and not a separate opinion.** Building it
        /// by hand would let it quietly become the case that makes the correction look best.
        /// </summary>
        public static List<Degradation> All()
        {
            List<Degradation> cases = new List<Degradation>
            {
                new Degradation
                {
                    Name = "none",
                    Because = "the control: two identical clients, which must agree exactly",
                },
                new Degradation
                {
                    Name = "stale join",
                    Because = "joined holding the server's state from 60 s ago, as a save does",
                    StaleSeconds = 60f,
                },
                new Degradation
                {
                    Name = "hitching",
                    Because = "drops its solver backlog every 30 s, as StepsDue does on a long frame",
                    HitchEverySeconds = 30f,
                    HitchLosesSeconds = 5f,
                },
                new Degradation
                {
                    Name = "environment lag",
                    Because = "samples the environment the server had 2 s ago",
                    EnvironmentLagSeconds = 2f,
                },
                new Degradation
                {
                    Name = "sun angle",
                    Because = "its replicated orientation puts the sun 5 degrees off",
                    SunAngleDegrees = 5f,
                },
                new Degradation
                {
                    Name = "power lag",
                    Because = "block power reaches it 2 s late, so a throttle change arrives late",
                    PowerLagSeconds = 2f,
                },
                new Degradation
                {
                    Name = "power error",
                    Because = "block power arrives 5 % out, standing in for what replication rounds",
                    PowerErrorShare = 0.05f,
                },
                new Degradation
                {
                    Name = "thinner air",
                    Because = "its gas system says the hull is in 20 % less air than the server's",
                    AirDensityError = 0.2f,
                },
                new Degradation
                {
                    Name = "wrong settings",
                    Because = "never received the world's settings, so it is running other physics",
                    OnDefaultSettings = true,
                },
            };

            Degradation everything = new Degradation
            {
                Name = "all at once",
                Because = "the union of every case above, which is what a bad client is",
            };

            for (int i = 0; i < cases.Count; i++)
            {
                Degradation one = cases[i];
                if (one.StaleSeconds > everything.StaleSeconds) everything.StaleSeconds = one.StaleSeconds;
                if (one.HitchEverySeconds > 0f) everything.HitchEverySeconds = one.HitchEverySeconds;
                if (one.HitchLosesSeconds > everything.HitchLosesSeconds) everything.HitchLosesSeconds = one.HitchLosesSeconds;
                if (one.EnvironmentLagSeconds > everything.EnvironmentLagSeconds) everything.EnvironmentLagSeconds = one.EnvironmentLagSeconds;
                if (one.SunAngleDegrees > everything.SunAngleDegrees) everything.SunAngleDegrees = one.SunAngleDegrees;
                if (one.PowerLagSeconds > everything.PowerLagSeconds) everything.PowerLagSeconds = one.PowerLagSeconds;
                if (one.PowerErrorShare > everything.PowerErrorShare) everything.PowerErrorShare = one.PowerErrorShare;
                if (one.AirDensityError > everything.AirDensityError) everything.AirDensityError = one.AirDensityError;
                if (one.OnDefaultSettings) everything.OnDefaultSettings = true;
            }

            cases.Add(everything);
            return cases;
        }

        /// <summary>The sweep as a table.</summary>
        public static string Report(IList<Result> results)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("Each input a client drives its own simulation from, degraded, with the");
            text.AppendLine("correction off and on.");
            text.AppendLine();
            text.AppendLine("degradation         fix      peak K   standing K   misreading    worst   B/s");

            int judged = 0;

            for (int i = 0; i < results.Count; i++)
            {
                Result result = results[i];
                bool off = result.Protocol == null || result.Protocol.IntervalSeconds <= 0f;
                bool anythingFailed = result.PeakServerCritical > 0;
                if (anythingFailed) judged++;

                text.AppendLine(string.Format(
                    "{0,-20}{1,-9}{2,8}{3,13}{4,13}{5,9}{6,6}",
                    result.Name,
                    off ? "off" : result.Protocol.IntervalSeconds.ToString("n0") + " s"
                        + (result.Protocol.WholeHullOnJoin ? "+j" : ""),
                    result.PeakKelvin.ToString("n1"),
                    result.StandingKelvin.ToString("n2"),
                    anythingFailed ? result.SecondsMisreading.ToString("n0") + " s" : "nothing hot",
                    anythingFailed ? result.PeakDisagreeing.ToString("n0") : "-",
                    result.BytesPerSecond.ToString("n0")));
            }

            if (judged < results.Count)
            {
                text.AppendLine();
                text.AppendLine("**" + (results.Count - judged) + " of " + results.Count
                    + " rows had no block past critical on the server**, so their readout columns");
                text.AppendLine("judged nothing and are printed as 'nothing hot' rather than as zero (E8). The");
                text.AppendLine("kelvin columns are still real; the readout columns are not.");
            }

            text.AppendLine();
            text.AppendLine("standing K is the mean disagreement over the final third of the run, and it is the");
            text.AppendLine("column that separates a perturbation from a bias: a one-off wrong state decays to");
            text.AppendLine("nothing whatever it peaked at, and a wrong input settles somewhere and stays. worst");
            text.AppendLine("is the largest number of blocks the two put on opposite sides of critical at once.");
            text.AppendLine();
            text.AppendLine("Every magnitude here is a knob this lab turns rather than a figure measured from a");
            text.AppendLine("session; what the table answers is which inputs bias, which perturb, and whether");
            text.AppendLine("the correction reaches each.");

            return text.ToString();
        }
    }
}
