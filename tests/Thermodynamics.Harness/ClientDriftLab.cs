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
        /// <summary>
        /// A correction protocol under test: how often the server states the near-critical band,
        /// and how much of it it is allowed to send.
        ///
        /// <para>
        /// **What is modelled is the reconciliation, not the transport.** The server selects
        /// through <see cref="HotTailCodec"/> — the same call the mod makes — the bytes are packed
        /// and unpacked through the same codec so a packing error shows up here rather than in a
        /// session, and the client applies them. What is not modelled is latency, loss and the
        /// game's own send queue, none of which this lab can see; those are why the mechanism has
        /// a switch and a counter rather than only a measurement.
        /// </para>
        /// </summary>
        public class Correction
        {
            /// <summary>Simulated seconds between updates. Zero or less means no correction.</summary>
            public float IntervalSeconds;

            /// <summary>
            /// How far below its own critical temperature a block is still worth sending, K.
            /// Defaults to the band the mod already glows over, which is the set a player is being
            /// warned about.
            /// </summary>
            public float BandKelvin = Incandescence.GlowBandKelvin;

            /// <summary>Most blocks one update may carry. Zero or less means no budget.</summary>
            public int MaxBlocks;

            /// <summary>
            /// Whether the server states the **whole** hull once, when the client joins, before it
            /// starts tracking the band.
            ///
            /// <para>
            /// **Measured, and it is what the band alone cannot do.** Correcting the near-critical
            /// band on a client whose whole hull is stale leaves a residual that never clears: the
            /// corrected blocks conduct to neighbours that are still wrong, and a block crossing
            /// into the band arrives with its client-side twin far behind. Replicating every block
            /// on an interval removes the residual and costs fifteen times the bandwidth; doing it
            /// **once** costs one packet and removes the same residual, because after it the client
            /// is not stale any more and the band is tracking rather than repairing.
            /// </para>
            /// </summary>
            public bool WholeHullOnJoin;

            public static Correction None
            {
                get { return new Correction { IntervalSeconds = 0f }; }
            }
        }

        /// <summary>
        /// The client's machine, to the one extent this lab can model it.
        ///
        /// <para>
        /// **Frame rate is not a divergence source and a hitch is.** The scheduler carries
        /// fractional step credit between frames, so a client at ten frames a second still owes and
        /// runs the same number of solver steps as one at sixty — it just runs them in bigger
        /// groups. What loses simulated time is <see cref="SimulationScheduler.StepsDue"/> hitting
        /// its per-frame cap, where it **drops the backlog rather than catching up** so a stall
        /// cannot become a stutter. At the shipped eight steps a second and a cap of four, that
        /// needs a frame over half a second: a world load, a large paste, an autosave, a hitch.
        /// </para>
        ///
        /// <para>
        /// **A single hitch is the perturbation this lab already runs.** Dropping the backlog once
        /// leaves the client holding the server's state from a moment ago and running forward on
        /// the same inputs, which is exactly what restoring a stale save does — so the join figures
        /// cover it. What they do not cover is a machine that does it *repeatedly*, which never
        /// gets the quiet run the convergence needs, and that is what this models.
        /// </para>
        /// </summary>
        public class Machine
        {
            /// <summary>Simulated seconds between hitches. Zero or less means none.</summary>
            public float HitchEverySeconds;

            /// <summary>How much simulated time each hitch costs the client.</summary>
            public float HitchLosesSeconds = 1f;

            public static Machine KeepsUp
            {
                get { return new Machine { HitchEverySeconds = 0f }; }
            }
        }

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
            /// Of those, the ones where the **server** has the block past critical and the client
            /// does not — the client showing safe while the block is being destroyed.
            ///
            /// **The two directions are different defects and a correction reaches them
            /// differently.** A server states the blocks *it* has in the band, so this direction is
            /// covered by construction; the other one is a block the client thinks is failing and
            /// the server does not, which no packet built from the server's own hot set contains.
            /// Counted separately so that a correction which fixes one and leaves the other cannot
            /// report as fixing both.
            /// </summary>
            public int ClientShowsSafe;

            /// <summary>The other direction: the client cries wolf where the server is calm.</summary>
            public int ClientCriesWolf;

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

            /// <summary>The same, counting only the client showing safe where the server is not.</summary>
            public float SecondsShowingSafe;

            /// <summary>
            /// The same for the other direction, which a packet built from the server's own hot set
            /// cannot contain.
            ///
            /// **Reported whether or not it is zero.** A correction that only ever states what the
            /// server has hot is structurally blind to a client that thinks a cool block is
            /// failing, and the only evidence that this does not happen is a column that would show
            /// it if it did (`P2`).
            /// </summary>
            public float SecondsCryingWolf;

            /// <summary>The correction this run was made under. Never null.</summary>
            public Correction Protocol = Correction.None;

            /// <summary>The client machine this run was made on. Never null.</summary>
            public Machine Host = Machine.KeepsUp;

            /// <summary>How many hitches the client took, and what they cost it in total.</summary>
            public int Hitches;

            /// <summary>Simulated seconds the client never ran, summed over every hitch.</summary>
            public float SecondsLostToHitches;

            /// <summary>How many updates the server sent over the run.</summary>
            public int Updates;

            /// <summary>Total bytes those updates carried, as the codec packs them.</summary>
            public long Bytes;

            /// <summary>The most blocks any one update carried, after the budget.</summary>
            public int PeakBlocksSent;

            /// <summary>
            /// How many blocks the budget cut, summed over every update.
            ///
            /// **A truncated packet is a client left wrong about the blocks that were cut**, so
            /// this is the correction's own blind spot and is reported whether or not it is zero
            /// (`P2`).
            /// </summary>
            public long BlocksDropped;

            /// <summary>Bytes a second of simulated time, which is what a server pays.</summary>
            public float BytesPerSecond;

            /// <summary>
            /// The most blocks any one sample had on the wrong side of critical.
            ///
            /// **"At least one block" is a harsh binary on a hull of nine thousand**, and a
            /// protocol that leaves one block wrong reads the same as one that leaves three
            /// thousand wrong. This is the column that separates them.
            /// </summary>
            public int PeakDisagreeing;

            /// <summary>The mean of the same over every sample, which is what a player mostly sees.</summary>
            public float MeanDisagreeing;
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
            return Measure(scenario, staleSeconds, watchSeconds, blocks, settings, Correction.None);
        }

        /// <summary>
        /// The same comparison, with the server correcting the client on an interval.
        ///
        /// The correction runs at the sample boundary, and the sample is taken *after* it, so what
        /// every column reports is the readout a player would be looking at — not the state
        /// between an update and its arrival.
        /// </summary>
        public static Run Measure(string scenario, float staleSeconds, float watchSeconds,
            int blocks, ThermalSettings settings, Correction protocol)
        {
            return Measure(scenario, staleSeconds, watchSeconds, blocks, settings, protocol,
                Machine.KeepsUp);
        }

        /// <summary>
        /// The same comparison again, on a client that loses simulated time as it runs.
        /// </summary>
        public static Run Measure(string scenario, float staleSeconds, float watchSeconds,
            int blocks, ThermalSettings settings, Correction protocol, Machine host)
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
                Protocol = protocol ?? Correction.None,
                Host = host ?? Machine.KeepsUp,
            };

            List<StoredTemperature> selection = new List<StoredTemperature>();
            List<StoredTemperature> received = new List<StoredTemperature>();

            // **The run advances by the finer of the two cadences, not by the sample.** Stepping a
            // whole sample at a time would make every interval under SampleSeconds land on the
            // same boundary and report identically, which is the instrument's resolution being
            // read as the protocol's (`P2`).
            float tick = run.Protocol.IntervalSeconds > 0f
                ? Math.Min(SampleSeconds, run.Protocol.IntervalSeconds)
                : SampleSeconds;

            // Never finer than a solver step: Step rounds seconds to whole steps, so a smaller tick
            // would advance the clock while advancing neither simulation, and the run would report
            // a protocol that corrected a hull nothing had moved.
            if (tick < world.StepSeconds) tick = world.StepSeconds;

            // The join packet, before the run starts: this is what a client fetching on load gets,
            // and it is one send rather than a cadence.
            if (run.Protocol.WholeHullOnJoin && run.Protocol.IntervalSeconds > 0f)
            {
                Correct(server, client, run, selection, received, float.MaxValue);
            }

            float elapsed = 0f;
            float sinceUpdate = float.MaxValue;
            float sinceSample = 0f;
            float sinceHitch = 0f;
            float owed = 0f;

            while (elapsed < watchSeconds)
            {
                Step(server, environment, tick);

                // A hitch is the client not running, not the client running wrong: the scheduler
                // drops the backlog, so those steps are never taken and the simulated time is gone.
                if (owed > 0f)
                {
                    owed -= tick;
                    run.SecondsLostToHitches += tick;
                }
                else
                {
                    Step(client, environment, tick);
                }

                elapsed += tick;
                sinceUpdate += tick;
                sinceSample += tick;
                sinceHitch += tick;

                if (run.Host.HitchEverySeconds > 0f && sinceHitch >= run.Host.HitchEverySeconds)
                {
                    sinceHitch = 0f;
                    owed += run.Host.HitchLosesSeconds;
                    run.Hitches++;
                }

                // **Sampled before the correction, never after.** Taking the reading immediately
                // after an update measures the client at the one moment it is right, and an
                // interval equal to the sample cadence then reports a perfect protocol whatever it
                // does in between. What a player sees is the state at the end of an interval, so
                // that is what is read (`M7`: measure a pass against its own start).
                if (sinceSample >= SampleSeconds)
                {
                    Sample sample = Compare(server, client);
                    sample.Seconds = elapsed;
                    run.Samples.Add(sample);

                    if (sample.DisagreeOnCritical > 0) run.SecondsMisreadingCritical += sinceSample;
                    if (sample.ClientShowsSafe > 0) run.SecondsShowingSafe += sinceSample;
                    if (sample.ClientCriesWolf > 0) run.SecondsCryingWolf += sinceSample;
                    if (run.SecondsToTenKelvin < 0f && sample.MaxKelvin < 10f) run.SecondsToTenKelvin = elapsed;
                    if (run.SecondsToAgree < 0f && sample.MaxKelvin < 1f) run.SecondsToAgree = elapsed;

                    sinceSample = 0f;
                }

                if (run.Protocol.IntervalSeconds > 0f && sinceUpdate >= run.Protocol.IntervalSeconds)
                {
                    sinceUpdate = 0f;
                    Correct(server, client, run, selection, received, run.Protocol.BandKelvin);
                }
            }

            run.BytesPerSecond = elapsed > 0f ? (float)(run.Bytes / (double)elapsed) : 0f;

            double disagreeing = 0d;
            for (int i = 0; i < run.Samples.Count; i++)
            {
                int count = run.Samples[i].DisagreeOnCritical;
                disagreeing += count;
                if (count > run.PeakDisagreeing) run.PeakDisagreeing = count;
            }

            run.MeanDisagreeing = run.Samples.Count > 0
                ? (float)(disagreeing / run.Samples.Count) : 0f;

            return run;
        }

        /// <summary>
        /// One update: the server selects its band, the bytes are packed and unpacked, and the
        /// client applies them.
        ///
        /// **The round trip through the codec is deliberate.** Selecting on one side and assigning
        /// on the other would measure a protocol nobody can send — the quantisation, the position
        /// key and the record layout are part of what is being tested, and a lab that skips them
        /// reports an accuracy the wire cannot deliver (`E7`).
        /// </summary>
        private static void Correct(ThermalSimulation server, ThermalSimulation client, Run run,
            List<StoredTemperature> selection, List<StoredTemperature> received, float band)
        {
            // The join packet is not budgeted: a truncated join leaves exactly the residual the
            // join exists to remove, and it happens once.
            int budget = band >= float.MaxValue ? 0 : run.Protocol.MaxBlocks;
            int inBand = server.ExportHotTail(band, budget, selection);

            byte[] packet = HotTailCodec.Encode(selection);
            if (!HotTailCodec.TryDecode(packet, received)) return;

            client.ImportHotTail(received);

            run.Updates++;
            run.Bytes += packet.Length;
            run.BlocksDropped += inBand - selection.Count;
            if (selection.Count > run.PeakBlocksSent) run.PeakBlocksSent = selection.Count;
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
                if (serverOver == clientOver) continue;

                sample.DisagreeOnCritical++;
                if (serverOver) sample.ClientShowsSafe++;
                else sample.ClientCriesWolf++;
            }

            sample.MeanKelvin = (float)(total / count);
            return sample;
        }

        /// <summary>
        /// The correction sweep as a table: what each protocol leaves the readout wrong for, and
        /// what it costs to send.
        ///
        /// **The uncorrected run is the first row and is not optional**, because every figure here
        /// is a difference against it and a sweep printed without its control is a set of numbers
        /// with nothing to be better than.
        /// </summary>
        public static string CorrectionReport(IList<Run> runs)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("What the server stating its near-critical band buys, and what it costs");
            text.AppendLine();
            text.AppendLine("interval    band   budget   misreading   wrong blocks       peak"
                + "   sent   dropped     B/s");

            for (int i = 0; i < runs.Count; i++)
            {
                Run run = runs[i];
                bool off = run.Protocol == null || run.Protocol.IntervalSeconds <= 0f;

                text.AppendLine(string.Format(
                    "{0,-12}{1,4}{2,9}{3,13}{4,15}{5,10}{6,7}{7,10}{8,8}",
                    off ? "none" : run.Protocol.IntervalSeconds.ToString("n0") + " s",
                    off ? "-" : (run.Protocol.BandKelvin >= 10000f
                        ? "all" : run.Protocol.BandKelvin.ToString("n0") + "K")
                        + (run.Protocol.WholeHullOnJoin ? "+j" : ""),
                    off ? "-" : (run.Protocol.MaxBlocks > 0
                        ? run.Protocol.MaxBlocks.ToString("n0") : "none"),
                    run.SecondsMisreadingCritical.ToString("n0") + " s",
                    run.MeanDisagreeing.ToString("n1") + " / " + run.PeakDisagreeing.ToString("n0"),
                    run.PeakBlocksSent.ToString("n0"),
                    run.Updates.ToString("n0"),
                    run.BlocksDropped.ToString("n0"),
                    run.BytesPerSecond.ToString("n0")));
            }

            text.AppendLine();
            text.AppendLine("misreading is seconds with at least one block on the wrong side of critical, which");
            text.AppendLine("is a harsh binary on a large hull — wrong blocks is the mean and the worst count");
            text.AppendLine("behind it. peak is the largest update sent, dropped is what the budget cut from");
            text.AppendLine("one, and B/s is over simulated time for one grid. The band row 'all' is the");
            text.AppendLine("diagnostic rather than a proposal: it replicates every block that can fail, which");
            text.AppendLine("is what says whether a residual is the interval or the hull left behind it.");
            text.AppendLine("Every sample is taken at the end of an interval, so each protocol is charged for");
            text.AppendLine("the drift it allows. Crying wolf — the client warning where the server is calm —");
            text.AppendLine("was zero on every run in this table and is in the csv.");

            return text.ToString();
        }

        /// <summary>
        /// What a client that keeps losing simulated time looks like, with the correction off and
        /// on.
        ///
        /// **The staleness is zero in every row here**, so nothing in this table is the join: the
        /// client starts in exactly the server's state and every kelvin of disagreement was
        /// produced by the hitches.
        /// </summary>
        public static string HitchReport(IList<Run> runs)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("A client that keeps losing simulated time, started in step with the server");
            text.AppendLine();
            text.AppendLine("hitch        cost   correction   lost      max K   misreading   showing safe");

            for (int i = 0; i < runs.Count; i++)
            {
                Run run = runs[i];
                bool never = run.Host == null || run.Host.HitchEverySeconds <= 0f;

                float worst = 0f;
                for (int s = 0; s < run.Samples.Count; s++)
                {
                    if (run.Samples[s].MaxKelvin > worst) worst = run.Samples[s].MaxKelvin;
                }

                text.AppendLine(string.Format(
                    "{0,-13}{1,5}{2,13}{3,7}{4,11}{5,13}{6,15}",
                    never ? "never" : "every " + run.Host.HitchEverySeconds.ToString("n0") + " s",
                    never ? "-" : run.Host.HitchLosesSeconds.ToString("n0") + " s",
                    run.Protocol == null || run.Protocol.IntervalSeconds <= 0f
                        ? "off" : "every " + run.Protocol.IntervalSeconds.ToString("n0") + " s",
                    run.SecondsLostToHitches.ToString("n0") + " s",
                    worst.ToString("n1"),
                    run.SecondsMisreadingCritical.ToString("n0") + " s",
                    run.SecondsShowingSafe.ToString("n0") + " s"));
            }

            text.AppendLine();
            text.AppendLine("lost is simulated seconds the client never ran. max K is the worst disagreement");
            text.AppendLine("reached at any sample, which is the column that says whether the error is bounded:");
            text.AppendLine("a single perturbation decays, and a repeated one need not.");

            return text.ToString();
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
