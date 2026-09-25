namespace Thermodynamics.Core
{
    public enum HotTailSend
    {
        Nothing = 0,

        Snapshot = 1,

        Band = 2,
    }

    public class HotTailSchedule
    {
        public const float DefaultIntervalSeconds = 5f;

        public const float DefaultSnapshotCooldownSeconds = 5f;

        public const float RetrySeconds = 8f;

        public float IntervalSeconds = DefaultIntervalSeconds;

        public float SnapshotCooldownSeconds = DefaultSnapshotCooldownSeconds;

        private float sinceBand;
        private float sinceSnapshot = float.MaxValue;
        private bool snapshotWanted;

        public bool SnapshotWanted
        {
            get { return snapshotWanted; }
        }


        public bool RequestSnapshot()
        {
            if (snapshotWanted) return false;
            if (sinceSnapshot < SnapshotCooldownSeconds) return false;

            snapshotWanted = true;
            return true;
        }


        public void Advance(float seconds)
        {
            if (seconds <= 0f) return;

            sinceBand += seconds;

            if (sinceSnapshot < SnapshotCooldownSeconds) sinceSnapshot += seconds;
        }


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
    public class HotTailRequest
    {
        public float RetrySeconds = HotTailSchedule.RetrySeconds;

        public float MaxRetrySeconds = 60f;

        private float waited;
        private float wait;
        private int asked;
        private bool answered;

        public bool Answered
        {
            get { return answered; }
        }

        public int Attempts
        {
            get { return asked; }
        }


        public void Advance(float seconds)
        {
            if (seconds > 0f && !answered) waited += seconds;
        }


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


        public void Answer()
        {
            answered = true;
        }
    }
}
