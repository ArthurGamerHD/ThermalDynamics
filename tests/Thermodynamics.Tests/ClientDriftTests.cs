using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// A client that joined with the wrong temperatures converges on the server's, and this is what
    /// keeps the figures that decision rests on from moving quietly.
    ///
    /// <para>
    /// Block temperatures are not replicated: a client re-simulates from the same inputs, and a
    /// client joining mid-session starts from whatever the world was last saved at. The open
    /// question was how far it may drift before it has to be corrected (backlog.md
    /// `B4`), and the answer is that the model is dissipative — the disagreement decays on its own,
    /// so what matters is how fast, and what the readout says while it lasts.
    /// </para>
    ///
    /// <para>
    /// The runs here are small and short because the *claims* are about shape rather than about a
    /// particular hull: convergence happens, air is faster than vacuum, and a run against itself
    /// agrees exactly. The figures a decision is quoted from come from `-- drift`, at sizes a test
    /// suite has no business running.
    /// </para>
    /// </summary>
    [Trait("speed", "slow")]
    public class ClientDriftTests
    {
        /// <summary>
        /// The control, and the reason to believe anything else here. Two runs started from the
        /// same state on the same inputs must agree **exactly** — if they did not, every figure
        /// this lab produces would be measuring the harness rather than the staleness (`E8`).
        /// </summary>
        [Fact]
        public void ARunAgainstItselfDisagreesAboutNothing()
        {
            ClientDriftLab.Run run = ClientDriftLab.Measure("shadow", 0f, 30f, 400);

            Assert.NotEmpty(run.Samples);
            Assert.Equal(0f, run.JoinKelvin, 5);

            foreach (ClientDriftLab.Sample sample in run.Samples)
            {
                Assert.Equal(0f, sample.MaxKelvin, 5);
                Assert.Equal(0, sample.DisagreeOnCritical);
            }

            Assert.Equal(0f, run.SecondsMisreadingCritical, 5);
        }

        /// <summary>
        /// A stale client starts wrong. Without this the convergence claim below would be satisfied
        /// by a lab that never perturbed anything.
        /// </summary>
        [Fact]
        public void AStaleClientStartsWrong()
        {
            ClientDriftLab.Run run = ClientDriftLab.Measure("shadow", 60f, 30f, 400);

            Assert.True(run.JoinKelvin > 5f,
                "a client sixty seconds behind should disagree by more than five kelvin, got "
                + run.JoinKelvin);
        }

        /// <summary>
        /// **The finding B4 turns on**: the disagreement decays without anybody correcting it. A
        /// model where it did not would need a replication protocol; one where it does needs a
        /// decision about the seconds in between.
        /// </summary>
        [Fact]
        public void TheDisagreementDecaysOnItsOwn()
        {
            ClientDriftLab.Run run = ClientDriftLab.Measure("shadow", 60f, 300f, 400);

            Assert.NotEmpty(run.Samples);

            float last = run.Samples[run.Samples.Count - 1].MaxKelvin;
            Assert.True(last < run.JoinKelvin * 0.5f,
                "five minutes in, the disagreement should be well under half what it started at: "
                + run.JoinKelvin + " K to " + last + " K");

            // And monotonically, near enough: a dissipative system has no reason to diverge again,
            // so a run that got worse across a whole sample would be a finding rather than noise.
            for (int i = 1; i < run.Samples.Count; i++)
            {
                Assert.True(run.Samples[i].MaxKelvin <= run.Samples[i - 1].MaxKelvin + 0.001f,
                    "the disagreement grew between " + run.Samples[i - 1].Seconds + " s and "
                    + run.Samples[i].Seconds + " s");
            }
        }

        /// <summary>
        /// Air converges faster than vacuum, because convection is a far stronger path to a shared
        /// ambient than radiation is. It is a mechanism rather than a coincidence, and it is why
        /// the figures are quoted per environment rather than as one number.
        /// </summary>
        [Fact]
        public void AirConvergesFasterThanVacuum()
        {
            ClientDriftLab.Run vacuum = ClientDriftLab.Measure("shadow", 60f, 120f, 400);
            ClientDriftLab.Run air = ClientDriftLab.Measure("planet", 60f, 120f, 400);

            float vacuumEnd = vacuum.Samples[vacuum.Samples.Count - 1].MaxKelvin;
            float airEnd = air.Samples[air.Samples.Count - 1].MaxKelvin;

            Assert.True(airEnd < vacuumEnd,
                "air should have converged further than vacuum in the same time: "
                + airEnd + " K against " + vacuumEnd + " K");
        }

        // ---- a client that keeps losing time ---------------------------------------------------

        /// <summary>
        /// **A client that keeps hitching does not converge, and that is a second defect rather than
        /// a worse version of the first.** The convergence above is what happens after *one*
        /// perturbation; a machine that drops its solver backlog every few seconds is perturbed
        /// again before it has finished recovering, and holds a standing error indefinitely.
        ///
        /// <para>
        /// Started perfectly in step with the server, so every kelvin here was made by the hitches
        /// and none of it is the join.
        /// </para>
        /// </summary>
        [Fact]
        public void AClientThatKeepsLosingTimeHoldsAStandingError()
        {
            ClientDriftLab.Run steady = ClientDriftLab.Measure("shadow", 0f, 240f, 400, null,
                ClientDriftLab.Correction.None, ClientDriftLab.Machine.KeepsUp);

            ClientDriftLab.Run hitching = ClientDriftLab.Measure("shadow", 0f, 240f, 400, null,
                ClientDriftLab.Correction.None,
                new ClientDriftLab.Machine { HitchEverySeconds = 10f, HitchLosesSeconds = 5f });

            // The control: in step and left alone, the two runs are the same arithmetic and must
            // agree exactly, or the figure below is measuring the harness (`E8`).
            Assert.Equal(0f, steady.Samples[steady.Samples.Count - 1].MaxKelvin, 4);

            Assert.True(hitching.Hitches > 0, "the rig took no hitches");
            Assert.True(hitching.SecondsLostToHitches > 0f, "the client lost no time");

            float last = hitching.Samples[hitching.Samples.Count - 1].MaxKelvin;
            Assert.True(last > 0.5f,
                "a client still losing time should still be wrong at the end of the run, got "
                + last + " K");
        }

        // ---- what the correction buys ----------------------------------------------------------

        /// <summary>
        /// The server stating its near-critical band leaves the readout wrong for less time than
        /// leaving the client alone does. The claim is the direction, not a figure — the figures
        /// come from `-- drift --correct` at sizes a suite has no business running.
        /// </summary>
        [Fact]
        public void CorrectingTheBandLeavesTheReadoutWrongForLessTime()
        {
            ClientDriftLab.Run alone = ClientDriftLab.Measure("shadow", 60f, 300f, 2000, null,
                ClientDriftLab.Correction.None);

            ClientDriftLab.Run corrected = ClientDriftLab.Measure("shadow", 60f, 300f, 2000, null,
                new ClientDriftLab.Correction { IntervalSeconds = 5f });

            Assert.True(alone.SecondsShowingSafe > 0f,
                "the rig needs an uncorrected client that misreads critical; it did not");

            Assert.True(corrected.SecondsShowingSafe < alone.SecondsShowingSafe,
                "the correction should shorten the time the client shows safe: "
                + corrected.SecondsShowingSafe + " s against " + alone.SecondsShowingSafe + " s");
        }

        /// <summary>
        /// **The correction is charged for the drift it allows.** A sample taken immediately after
        /// an update reads the client at the one moment it is right, so a longer interval must
        /// leave the readout wrong for longer — and if it did not, the lab would be measuring its
        /// own sampling rather than the protocol (`M7`).
        /// </summary>
        [Fact]
        public void ALongerIntervalLeavesTheReadoutWrongForLonger()
        {
            ClientDriftLab.Run tight = ClientDriftLab.Measure("shadow", 60f, 300f, 2000, null,
                new ClientDriftLab.Correction { IntervalSeconds = 5f });

            ClientDriftLab.Run loose = ClientDriftLab.Measure("shadow", 60f, 300f, 2000, null,
                new ClientDriftLab.Correction { IntervalSeconds = 60f });

            Assert.True(loose.SecondsShowingSafe > tight.SecondsShowingSafe,
                "a minute between updates should be worse than five seconds: "
                + loose.SecondsShowingSafe + " s against " + tight.SecondsShowingSafe + " s");

            Assert.True(loose.Updates < tight.Updates);
            Assert.True(loose.Bytes < tight.Bytes);
        }

        /// <summary>
        /// The bytes the lab charges are the bytes the codec makes, so the bandwidth column is a
        /// measurement of the wire format rather than an estimate beside it (`P5`).
        /// </summary>
        [Fact]
        public void TheBytesChargedAreTheBytesTheCodecPacks()
        {
            ClientDriftLab.Run run = ClientDriftLab.Measure("shadow", 60f, 60f, 2000, null,
                new ClientDriftLab.Correction { IntervalSeconds = 5f });

            Assert.True(run.Updates > 0);
            Assert.True(run.Bytes >= run.Updates * (long)HotTailCodec.HeaderSize);
            Assert.True(run.Bytes
                <= run.Updates * (long)HotTailCodec.SizeOf(run.PeakBlocksSent));
        }

        /// <summary>
        /// A correction with no interval sends nothing at all — the off switch is off, and costs
        /// what off costs (`C7`, `P8`).
        /// </summary>
        [Fact]
        public void TheCorrectionSwitchedOffSendsNothing()
        {
            ClientDriftLab.Run run = ClientDriftLab.Measure("shadow", 60f, 60f, 400, null,
                ClientDriftLab.Correction.None);

            Assert.Equal(0, run.Updates);
            Assert.Equal(0L, run.Bytes);
            Assert.Equal(0f, run.BytesPerSecond, 5);
        }
    }
}
