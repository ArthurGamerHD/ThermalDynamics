using System;
using System.Collections.Generic;

namespace Thermodynamics.Core
{
    /// <summary>Which way a block has to cross a threshold for it to report.</summary>
    public enum ThresholdDirection
    {
        /// <summary>Report only when the temperature rises through the threshold.</summary>
        Rising = 0,

        /// <summary>Report only when it falls through.</summary>
        Falling = 1,

        /// <summary>Report both.</summary>
        Both = 2,
    }

    /// <summary>
    /// A temperature another mod is interested in. Registered once, checked every step, and
    /// reported as a <see cref="ThresholdCrossing"/> whenever a block moves through it.
    /// </summary>
    public struct ThermalThreshold
    {
        /// <summary>Caller-assigned identity, echoed back on every crossing.</summary>
        public int Id;

        /// <summary>The temperature to watch, K.</summary>
        public float Temperature;

        public ThresholdDirection Direction;

        public ThermalThreshold(int id, float temperature, ThresholdDirection direction)
        {
            Id = id;
            Temperature = temperature;
            Direction = direction;
        }
    }

    /// <summary>One block moving through one threshold during one step.</summary>
    public struct ThresholdCrossing
    {
        public int ThresholdId;
        public BlockInstance Block;

        /// <summary>The threshold that was crossed, K.</summary>
        public float Threshold;

        /// <summary>Temperature at the end of the step, K.</summary>
        public float Temperature;

        /// <summary>True when the block was heating through the threshold.</summary>
        public bool Rising;

        public ThresholdCrossing(int id, BlockInstance block, float threshold, float temperature, bool rising)
        {
            ThresholdId = id;
            Block = block;
            Threshold = threshold;
            Temperature = temperature;
            Rising = rising;
        }
    }

    /// <summary>
    /// The set of temperatures being watched on one grid.
    ///
    /// Kept apart from the solver so the cost is visible: with nothing registered, a step tests
    /// one integer. With thresholds registered, the check is one comparison per node per
    /// threshold against the temperatures the step already has in hand — no extra pass over the
    /// grid, and no per-block subscription list to maintain.
    ///
    /// Crossings are detected against the temperature at the top of the step, not the top of a
    /// substep, so a block that crosses and recrosses within one step reports once, in the
    /// direction it actually ended up going.
    /// </summary>
    public class ThermalThresholds
    {
        private readonly List<ThermalThreshold> thresholds = new List<ThermalThreshold>();
        private int nextId = 1;

        public IList<ThermalThreshold> All
        {
            get { return thresholds; }
        }

        public int Count
        {
            get { return thresholds.Count; }
        }

        /// <summary>Registers a threshold and returns its id.</summary>
        public int Add(float temperature, ThresholdDirection direction)
        {
            int id = nextId++;
            thresholds.Add(new ThermalThreshold(id, temperature, direction));
            return id;
        }

        /// <summary>Registers a threshold with an id the caller chooses.</summary>
        public void Add(ThermalThreshold threshold)
        {
            Remove(threshold.Id);
            thresholds.Add(threshold);
            if (threshold.Id >= nextId) nextId = threshold.Id + 1;
        }

        public bool Remove(int id)
        {
            for (int i = 0; i < thresholds.Count; i++)
            {
                if (thresholds[i].Id != id) continue;
                thresholds.RemoveAt(i);
                return true;
            }
            return false;
        }

        public void Clear()
        {
            thresholds.Clear();
        }

        /// <summary>
        /// Appends every threshold that <paramref name="previous"/> to <paramref name="current"/>
        /// crosses for one block.
        /// </summary>
        public void Collect(BlockInstance block, float previous, float current, List<ThresholdCrossing> results)
        {
            if (results == null || previous == current) return;

            bool rising = current > previous;
            float low = rising ? previous : current;
            float high = rising ? current : previous;

            for (int i = 0; i < thresholds.Count; i++)
            {
                ThermalThreshold threshold = thresholds[i];

                // Half-open so a block sitting exactly on a threshold cannot report twice.
                if (threshold.Temperature <= low || threshold.Temperature > high) continue;

                if (threshold.Direction == ThresholdDirection.Rising && !rising) continue;
                if (threshold.Direction == ThresholdDirection.Falling && rising) continue;

                results.Add(new ThresholdCrossing(
                    threshold.Id, block, threshold.Temperature, current, rising));
            }
        }
    }
}
