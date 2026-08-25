namespace Thermodynamics.Core
{
    /// <summary>What a server owes one client about one grid at this moment.</summary>
    public enum HotTailSend
    {
        /// <summary>Nothing is due.</summary>
        Nothing = 0,

        /// <summary>The whole hull, once, because this client has never been told it.</summary>
        Snapshot = 1,

        /// <summary>The near-critical band, because the interval has elapsed.</summary>
        Band = 2,
    }

    /// <summary>
    /// When a server states a grid to one client: the whole hull once, then the band on an
    /// interval.
    ///
    /// <para>
    /// **Both halves are measured, and neither works alone.** Correcting the band on a client whose
    /// hull is stale leaves 145 s of 560 s misread on the census hull however often it is sent,
    /// because a corrected block conducts to stale neighbours; stating the hull once and then
    /// tracking the band takes it to nothing. The evidence is in
    /// known-issues.md, measured by `ClientDriftLab`.
    /// </para>
    ///
    /// <para>
    /// **Why the snapshot goes first and resets the interval.** A snapshot states every block the
    /// band would have, so a band update sent behind it in the same breath is bytes for values the
    /// client already has.
    /// </para>
    ///
    /// <para>
    /// **Why a request can be refused.** The snapshot is the one large thing this protocol sends —
    /// 94 KB on a 9,430-node hull — and it is sent because a client asked. A client that asks in a
    /// loop would otherwise be a client that makes the server transmit a hull per frame, so a
    /// repeat inside the cooldown is dropped. Honest retries are slower than the cooldown, so they
    /// are answered; see <see cref="RetrySeconds"/>.
    /// </para>
    ///
    /// <para>
    /// Free of any game type, so the policy is testable without a session — which is the only way
    /// it can be tested at all (`C5`).
    /// </para>
    /// </summary>
    public class HotTailSchedule
    {
        /// <summary>
        /// Simulated seconds between band updates.
        ///
        /// **Five, and the reason is the bias rather than the drift.** Against a stale join —
        /// a perturbation, which decays — a sixty-second interval already leaves the readout right
        /// for a whole run once the hull has been stated. Against a degraded *input*, which does
        /// not decay, the client re-diverges between updates and the interval is the only lever:
        /// the measured combined case halves at five seconds and is explicitly not closed by it.
        /// Five is also well inside the 37 s median damage event this exists to be right about.
        /// </summary>
        public const float DefaultIntervalSeconds = 5f;

        /// <summary>
        /// Seconds a client must wait before a second snapshot of the same grid is honoured.
        ///
        /// Shorter than <see cref="RetrySeconds"/>, so a client whose request or answer was lost is
        /// answered on its next attempt rather than punished for the loss.
        /// </summary>
        public const float DefaultSnapshotCooldownSeconds = 5f;

        /// <summary>
        /// Seconds a client waits for a snapshot before asking again.
        ///
        /// The request and the answer both travel over a lossy link this code cannot see, so an
        /// unanswered request has to be re-asked or the client keeps a stale hull for the session.
        /// </summary>
        public const float RetrySeconds = 8f;

        /// <summary>Seconds between band updates. Zero or less sends the band only after a snapshot.</summary>
        public float IntervalSeconds = DefaultIntervalSeconds;

        /// <summary>Seconds between honoured snapshots of one grid for one client.</summary>
        public float SnapshotCooldownSeconds = DefaultSnapshotCooldownSeconds;

        private float sinceBand;
        private float sinceSnapshot = float.MaxValue;
        private bool snapshotWanted;

        /// <summary>Whether a snapshot has been asked for and not yet handed over.</summary>
        public bool SnapshotWanted
        {
            get { return snapshotWanted; }
        }

        /// <summary>
        /// Records that a client has asked for the whole hull.
        /// </summary>
        /// <returns>
        /// False when the request is dropped: one is already outstanding, or the last snapshot was
        /// inside the cooldown.
        /// </returns>
        public bool RequestSnapshot()
        {
            if (snapshotWanted) return false;
            if (sinceSnapshot < SnapshotCooldownSeconds) return false;

            snapshotWanted = true;
            return true;
        }

        /// <summary>Advances both clocks by a step of simulated time.</summary>
        public void Advance(float seconds)
        {
            if (seconds <= 0f) return;

            sinceBand += seconds;

            // Saturates rather than growing without bound: a client that joined an hour ago and a
            // client that joined a week ago are the same client as far as the cooldown is
            // concerned, and a float that has grown large stops resolving small additions.
            if (sinceSnapshot < SnapshotCooldownSeconds) sinceSnapshot += seconds;
        }

        /// <summary>
        /// What to send now, consuming it: the caller is expected to send whatever this returns.
        ///
        /// A snapshot outranks the band and restarts the interval, because it has just stated every
        /// block the band would have carried.
        /// </summary>
        public HotTailSend Next()
        {
            if (snapshotWanted)
            {
                snapshotWanted = false;
                sinceSnapshot = 0f;
                sinceBand = 0f;
                return HotTailSend.Snapshot;
            }

            if (IntervalSeconds > 0f && sinceBand >= IntervalSeconds)
            {
                sinceBand = 0f;
                return HotTailSend.Band;
            }

            return HotTailSend.Nothing;
        }
    }
    /// <summary>
    /// The other half of the handshake: when a client asks for a grid's whole hull, and when it
    /// asks again.
    ///
    /// <para>
    /// **It has to be the client that asks.** The server cannot tell when a grid has finished
    /// streaming in on some other machine, and a snapshot sent before the client has built the grid
    /// is a snapshot applied to blocks that do not exist yet — every record of it dropped, silently,
    /// by the one guard that makes an unknown position safe.
    /// </para>
    ///
    /// <para>
    /// **It backs off rather than giving up.** A request and its answer both cross a link this code
    /// cannot see, and a client that stopped asking would hold a stale hull for the rest of the
    /// session — the exact defect this protocol exists to remove. Backing off bounds the chatter
    /// instead: a grid nobody will ever answer for costs ten bytes a minute.
    /// </para>
    /// </summary>
    public class HotTailRequest
    {
        /// <summary>Seconds before the first repeat.</summary>
        public float RetrySeconds = HotTailSchedule.RetrySeconds;

        /// <summary>The longest the wait between repeats grows to.</summary>
        public float MaxRetrySeconds = 60f;

        private float waited;
        private float wait;
        private int asked;
        private bool answered;

        /// <summary>Whether the server has stated this grid at least once.</summary>
        public bool Answered
        {
            get { return answered; }
        }

        /// <summary>How many times this client has asked.</summary>
        public int Attempts
        {
            get { return asked; }
        }

        public void Advance(float seconds)
        {
            if (seconds > 0f && !answered) waited += seconds;
        }

        /// <summary>
        /// Whether to send a request now, consuming it. True immediately the first time, then once
        /// per wait, the wait doubling to <see cref="MaxRetrySeconds"/>.
        /// </summary>
        public bool ShouldAsk()
        {
            if (answered) return false;

            if (asked == 0)
            {
                asked = 1;
                waited = 0f;
                wait = RetrySeconds;
                return true;
            }

            if (waited < wait) return false;

            asked++;
            waited = 0f;
            wait = wait * 2f > MaxRetrySeconds ? MaxRetrySeconds : wait * 2f;
            return true;
        }

        /// <summary>
        /// Records that the server has stated this grid's whole hull, which is what stops the
        /// asking.
        ///
        /// **A band update does not count**, which is why the two are different messages. The band
        /// on a stale hull is measured to leave 145 s of 560 s misread however often it is sent, so
        /// a client that took one for its snapshot would stop asking for the thing that fixes it.
        /// </summary>
        public void Answer()
        {
            answered = true;
        }
    }
}
