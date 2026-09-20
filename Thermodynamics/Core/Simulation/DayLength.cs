using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// How long this world's day is, measured from the sun rather than asked for.
    ///
    /// <para>
    /// **The climate's lag is a share of the day, and nothing knew how long a day was.**
    /// `AmbientLagSeconds` was 45 absolute seconds against a rotation a server sets to anything: it
    /// attenuates a four-minute day to less than half and does nothing at all to a two-hour one, so
    /// the same authored figure means two different climates. What it wants to be is a fraction —
    /// Earth's air peaks about two hours after noon out of twenty-four, which is a twelfth.
    /// </para>
    ///
    /// <para>
    /// **Measured rather than read.** `MySectorWeatherComponent.RotationInterval` exists and would
    /// answer directly, but whether the script whitelist admits it cannot be established outside a
    /// session, and a type the in-game compiler rejects takes the mod down at world load. The sun's
    /// own direction is already sampled every frame for the solar term, and the angle it sweeps
    /// between two samples is the same fact — so this needs no new type, survives a server changing
    /// the interval mid-session, and works on a world whose rotation nothing publishes.
    /// See environment.md, and backlog `C6`.
    /// </para>
    /// </summary>
    public class DayLength
    {
        /// <summary>
        /// Radians the sun must have swept before an estimate is offered.
        ///
        /// A tenth of a turn. Less is a rate taken over an angle small enough that the arc-cosine's
        /// own error is a large share of it; more is a session that runs a long time with no answer.
        /// </summary>
        public const float MinimumSweptRadians = 0.628f;

        /// <summary>
        /// Seconds a single sample may cover before it is discarded rather than believed.
        ///
        /// A frame that took a second of real time — a world load, a paste, a stall — moves the sun
        /// as far as many ordinary frames and would read as a much faster rotation. Bounding the
        /// step is cheaper than reasoning about which frames were honest.
        /// </summary>
        public const float MaximumStepSeconds = 1f;

        private Vector3 previous;
        private bool has;

        private double sweptRadians;
        private double sweptSeconds;

        /// <summary>Seconds in a full rotation, or -1 until enough of one has been seen.</summary>
        public float Seconds
        {
            get
            {
                if (sweptRadians < MinimumSweptRadians || sweptSeconds <= 0d) return -1f;
                return (float)(2d * Math.PI * sweptSeconds / sweptRadians);
            }
        }

        /// <summary>Whether an estimate is available.</summary>
        public bool Known
        {
            get { return Seconds > 0f; }
        }

        /// <summary>Forgets everything, so a new world is measured rather than inherited.</summary>
        public void Reset()
        {
            has = false;
            sweptRadians = 0d;
            sweptSeconds = 0d;
        }

        /// <summary>
        /// One sample of where the sun is. <paramref name="seconds"/> is the time since the last.
        /// </summary>
        public void Observe(Vector3 sunDirection, float seconds)
        {
            if (sunDirection.LengthSquared() < 1e-8f) return;

            Vector3 now = Vector3.Normalize(sunDirection);

            if (!has)
            {
                previous = now;
                has = true;
                return;
            }

            if (seconds <= 0f || seconds > MaximumStepSeconds)
            {
                // Still worth keeping as the new reference: the sun is where it is, and the next
                // step measures from here.
                previous = now;
                return;
            }

            // **`atan2(|a x b|, a.b)` rather than `acos(a.b)`.** A frame moves the sun a thousandth
            // of a radian, and the cosine of an angle that small is one to within a few float
            // epsilons — so an arc-cosine of it has about two significant digits left and reads a
            // four-minute day as a four-and-a-bit-minute one. The cross product's magnitude *is*
            // the small quantity, so it keeps every digit it started with.
            double swept = Math.Atan2(
                Vector3.Cross(previous, now).Length(), Vector3.Dot(previous, now));

            previous = now;

            // A sun that has not moved is a paused world or a static world, and averaging its
            // stillness in would report a day of no length at all.
            if (swept <= 0d) return;

            sweptRadians += swept;
            sweptSeconds += seconds;
        }
    }
}
