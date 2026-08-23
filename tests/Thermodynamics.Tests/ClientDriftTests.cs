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
    /// question was how far it may drift before it has to be corrected ([backlog](../../docs/backlog.md)
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
    }
}
