using System;
using System.Collections.Generic;

namespace Thermodynamics.Core
{
    /// <summary>How far along a block is toward the thing a player is being warned about.</summary>
    public enum HeatCueStage
    {
        /// <summary>Nothing is coming.</summary>
        None = 0,

        /// <summary>It crosses its critical temperature within the lead.</summary>
        Approaching = 1,

        /// <summary>It is above its critical temperature now.</summary>
        Critical = 2,
    }

    /// <summary>What one block looks and sounds like this instant.</summary>
    public struct HeatCue
    {
        /// <summary>The block, so a host can find its entity.</summary>
        public BlockInstance Block;

        /// <summary>Its temperature, K.</summary>
        public float Kelvin;

        /// <summary>Its critical temperature, K.</summary>
        public float Critical;

        /// <summary>How brightly it glows, 0..1. See <see cref="Incandescence"/>.</summary>
        public float Glow;

        public HeatCueStage Stage;

        /// <summary>
        /// True on the one scan the stage advanced on. **This is what a sound plays on** — the
        /// glow is a state and the audio is an event, and the difference between them is this flag.
        /// A stage falling back does not set it, because a block cooling out of trouble is not
        /// news.
        /// </summary>
        public bool Announce;

        /// <summary>
        /// Seconds until it crosses, or <see cref="float.PositiveInfinity"/> where the forecast
        /// says it never does.
        /// </summary>
        public float SecondsToCritical;
    }

    /// <summary>
    /// The state one grid's cue scan carries between scans: what each warm block was doing last
    /// time, so a rate and a stage change can be read off it.
    ///
    /// <para>
    /// **Held for warm blocks only, and that is the whole design.** Every other way of getting a
    /// rate wants a float on every node, and the per-node arrays are the thing the memory work is
    /// trying to shrink. A hull with nothing hot on it carries an empty dictionary; the corpus says
    /// the number of blocks above critical on a ship in trouble has a median of two.
    /// </para>
    /// </summary>
    public class HeatCueState
    {
        /// <summary>
        /// Fraction of its own critical temperature at which a block starts being watched.
        ///
        /// <para>
        /// It has to be a fraction rather than a temperature because the shipped definitions run
        /// from 500 K to 1,522 K of critical, so no single number is near all of them. 0.75 is one
        /// interval of watching before the cue can be due at any plausible rate, and it is
        /// deliberately generous: being watched costs a dictionary entry, and being wrong about who
        /// to watch costs a cue.
        /// </para>
        /// </summary>
        public const float WatchFraction = 0.75f;

        /// <summary>What one watched block was doing at the previous scan.</summary>
        public struct Previous
        {
            public float Kelvin;
            public float Rate;
            public HeatCueStage Stage;
            public bool HasRate;
        }

        private readonly Dictionary<BlockInstance, Previous> watched =
            new Dictionary<BlockInstance, Previous>();

        private readonly List<BlockInstance> cold = new List<BlockInstance>();

        /// <summary>Blocks currently being watched.</summary>
        public int Count
        {
            get { return watched.Count; }
        }

        public bool TryGet(BlockInstance block, out Previous previous)
        {
            return watched.TryGetValue(block, out previous);
        }

        public void Set(BlockInstance block, Previous previous)
        {
            watched[block] = previous;
        }

        /// <summary>Forgets every block the last scan did not touch.</summary>
        public void Forget(HashSet<BlockInstance> seen)
        {
            if (watched.Count == 0) return;

            cold.Clear();
            foreach (KeyValuePair<BlockInstance, Previous> entry in watched)
            {
                if (seen == null || !seen.Contains(entry.Key)) cold.Add(entry.Key);
            }

            for (int i = 0; i < cold.Count; i++) watched.Remove(cold[i]);
            cold.Clear();
        }

        public void Clear()
        {
            watched.Clear();
            cold.Clear();
        }
    }

    public partial class ThermalSolver
    {
        /// <summary>
        /// The lowest critical temperature on the grid, K, or
        /// <see cref="float.PositiveInfinity"/> for a grid whose blocks have none.
        ///
        /// <para>
        /// **A lower bound rather than the exact minimum**, and deliberately: the state refresh is
        /// incremental, so a block leaving the grid cannot raise it. A bound that is too low makes
        /// a scan happen that would have found nothing, which is the direction that is safe to be
        /// wrong in — the opposite would silence a cue.
        /// </para>
        /// </summary>
        public float LowestCriticalTemperature
        {
            get { return lowestCritical; }
        }

        private float lowestCritical = float.PositiveInfinity;

        private readonly HashSet<BlockInstance> cueSeen = new HashSet<BlockInstance>();

        /// <summary>
        /// The temperature below which no block on this grid is interesting to a cue: the lower of
        /// where the coolest block starts glowing and where it starts being watched for a warning.
        /// Both are read off that block's own rating, so the floor follows what the ship is made
        /// of.
        ///
        /// **The whole cost of the feature on a hull where nothing is hot is comparing the hottest
        /// block against this**, which is what the intent asks for — see document-of-intent.md,
        /// Natural feedback.
        /// </summary>
        public float CueFloorTemperature()
        {
            if (float.IsInfinity(lowestCritical)) return float.PositiveInfinity;

            float watch = lowestCritical * HeatCueState.WatchFraction;
            float glow = Incandescence.GlowStartKelvin(lowestCritical);
            return watch < glow ? watch : glow;
        }

        /// <summary>
        /// Appends a cue for every block that is glowing or near its rating.
        ///
        /// <para>
        /// <paramref name="interval"/> is seconds of play since the previous scan and sets the rate
        /// the forecast reads, so a caller that skips scans must pass the real gap rather than its
        /// nominal one. The scan itself is two flat-array reads and a comparison per node until a
        /// block passes the floor.
        /// </para>
        /// </summary>
        public void CollectHeatCues(HeatCueState state, float interval, List<HeatCue> results)
        {
            if (state == null || results == null) return;

            int count = nodes.Count;
            if (count == 0)
            {
                state.Clear();
                return;
            }

            cueSeen.Clear();

            for (int i = 0; i < count; i++)
            {
                float kelvin = nodeTemperatures[i];
                float critical = nodeCritical[i];

                // The one test a cold hull pays for. Both halves come off the block's own rating:
                // a share of it starts the watch, and the last hundred kelvin starts the glow.
                if (critical <= 0f || (kelvin < critical * HeatCueState.WatchFraction
                    && kelvin < Incandescence.GlowStartKelvin(critical)))
                {
                    continue;
                }

                ThermalNode node = nodes[i];
                BlockInstance block = node.Block;
                if (block == null) continue;

                cueSeen.Add(block);

                HeatCueState.Previous previous;
                bool had = state.TryGet(block, out previous);

                float rate = 0f;
                bool hasRate = false;
                if (had && interval > 0f)
                {
                    rate = (kelvin - previous.Kelvin) / interval;
                    hasRate = true;
                }

                HeatForecast forecast = HeatWarning.Forecast(kelvin, rate,
                    had && previous.HasRate ? previous.Rate : 0f, interval, critical);

                HeatCueStage stage = HeatCueStage.None;
                if (critical > 0f && kelvin >= critical)
                {
                    stage = HeatCueStage.Critical;
                }
                else if (forecast.WillCross && forecast.Seconds <= HeatWarning.LeadSeconds)
                {
                    stage = HeatCueStage.Approaching;
                }

                HeatCue cue = new HeatCue();
                cue.Block = block;
                cue.Kelvin = kelvin;
                cue.Critical = critical;
                cue.Glow = Incandescence.Glow(kelvin, critical);
                cue.Stage = stage;
                cue.Announce = stage > (had ? previous.Stage : HeatCueStage.None);
                cue.SecondsToCritical = forecast.WillCross
                    ? forecast.Seconds
                    : float.PositiveInfinity;

                results.Add(cue);

                HeatCueState.Previous now = new HeatCueState.Previous();
                now.Kelvin = kelvin;
                now.Rate = rate;
                now.HasRate = hasRate;
                now.Stage = stage;
                state.Set(block, now);
            }

            state.Forget(cueSeen);
            cueSeen.Clear();
        }
    }
}
