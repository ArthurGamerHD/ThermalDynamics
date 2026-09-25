using System;
using System.Collections.Generic;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Direction of temperature threshold crossing for notification.
    /// Used to specify whether to trigger on rising (above) or falling (below) thresholds.
    /// </summary>
    public enum ThresholdDirection
    {
        /// <summary>Trigger when temperature rises above the threshold.</summary>
        Rising = 0,

        /// <summary>Trigger when temperature falls below the threshold.</summary>
        Falling = 1,

        /// <summary>Trigger on both rising and falling crossings.</summary>
        Both = 2,
    }

    /// <summary>
    /// A temperature threshold definition for thermal monitoring.
    /// Stores the threshold temperature and direction for triggering notifications.
    /// </summary>
    public struct ThermalThreshold
    {
        /// <summary>
        /// Unique identifier for this threshold.
        /// Generated sequentially by ThermalThresholds.Add().
        /// </summary>
        public int Id;

        /// <summary>
        /// Temperature in Kelvin at which the threshold triggers.
        /// </summary>
        public float Temperature;

        /// <summary>
        /// Direction of temperature change that triggers this threshold.
        /// </summary>
        public ThresholdDirection Direction;


        /// <summary>
        /// Creates a new ThermalThreshold instance.
        /// </summary>
        /// <param name="id">Unique identifier for this threshold.</param>
        /// <param name="temperature">Temperature in Kelvin that triggers this threshold.</param>
        /// <param name="direction">Direction of temperature change that triggers.</param>
        public ThermalThreshold(int id, float temperature, ThresholdDirection direction)
        {
            Id = id;
            Temperature = temperature;
            Direction = direction;
        }
    }

    /// <summary>
    /// Records an event when a block's temperature crosses a threshold.
    /// Contains information about which threshold was crossed, the block involved,
    /// and the temperature values before and after the crossing.
    /// </summary>
    public struct ThresholdCrossing
    {
        /// <summary>
        /// ID of the threshold that was crossed.
        /// Matches the Id in the corresponding ThermalThreshold.
        /// </summary>
        public int ThresholdId;

        /// <summary>
        /// The block whose temperature crossed the threshold.
        /// Used to identify which block needs attention.
        /// </summary>
        public BlockInstance Block;

        /// <summary>
        /// The threshold temperature in Kelvin that was crossed.
        /// </summary>
        public float Threshold;

        /// <summary>
        /// The actual temperature in Kelvin when the crossing occurred.
        /// </summary>
        public float Temperature;

        /// <summary>
        /// True if the temperature crossed while rising (going up).
        /// False if the temperature crossed while falling (going down).
        /// </summary>
        public bool Rising;


        /// <summary>
        /// Creates a new ThresholdCrossing instance.
        /// </summary>
        /// <param name="id">Threshold ID that was crossed.</param>
        /// <param name="block">Block whose temperature crossed.</param>
        /// <param name="threshold">Temperature threshold value in Kelvin.</param>
        /// <param name="temperature">Actual temperature when crossing occurred.</param>
        /// <param name="rising">True if temperature was rising when it crossed.</param>
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
    /// Manages a collection of temperature thresholds for monitoring block temperatures.
    /// Allows registering thresholds with specific IDs and detecting when temperatures
    /// cross these thresholds during simulation steps.
    /// </summary>
    public class ThermalThresholds
    {
        /// <summary>
        /// Internal list of registered thresholds.
        /// </summary>
        private readonly List<ThermalThreshold> thresholds = new List<ThermalThreshold>();

        /// <summary>
        /// Next ID to assign to newly added thresholds.
        /// Sequential numbering starting from 1.
        /// </summary>
        private int nextId = 1;

        /// <summary>
        /// Gets a read-only view of all registered thresholds.
        /// </summary>
        public IList<ThermalThreshold> All
        {
            get { return thresholds; }
        }

        /// <summary>
        /// Gets the number of registered thresholds.
        /// </summary>
        public int Count
        {
            get { return thresholds.Count; }
        }


        /// <summary>
        /// Adds a new temperature threshold to monitor.
        /// Automatically assigns a unique ID and returns it.
        /// </summary>
        /// <param name="temperature">Temperature in Kelvin that triggers this threshold.</param>
        /// <param name="direction">Direction of temperature change that triggers.</param>
        /// <returns>The unique ID assigned to this threshold.</returns>
        public int Add(float temperature, ThresholdDirection direction)
        {
            int id = nextId++;
            thresholds.Add(new ThermalThreshold(id, temperature, direction));
            return id;
        }


        /// <summary>
        /// Adds or replaces a threshold with a specific ID.
        /// If a threshold with this ID already exists, it is removed first.
        /// Updates nextId if the provided ID is higher than current.
        /// </summary>
        /// <param name="threshold">The threshold to add (must have an ID).</param>
        public void Add(ThermalThreshold threshold)
        {
            Remove(threshold.Id);
            thresholds.Add(threshold);
            if (threshold.Id >= nextId) nextId = threshold.Id + 1;
        }


        /// <summary>
        /// Removes a threshold by its ID.
        /// </summary>
        /// <param name="id">The ID of the threshold to remove.</param>
        /// <returns>True if the threshold was found and removed, false otherwise.</returns>
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


        /// <summary>
        /// Clears all registered thresholds.
        /// </summary>
        public void Clear()
        {
            thresholds.Clear();
        }


        /// <summary>
        /// Checks if any thresholds were crossed during the last temperature change.
        /// Compares previous and current temperatures to determine if any registered
        /// thresholds were crossed, and in which direction.
        /// </summary>
        /// <param name="block">The block whose temperature changed.</param>
        /// <param name="previous">Temperature before the change in Kelvin.</param>
        /// <param name="current">Temperature after the change in Kelvin.</param>
        /// <param name="results">List to add crossing events to.</param>
        /// <remarks>
        /// A crossing is detected when:
        /// 1. The temperature range [min, max] spans the threshold temperature
        /// 2. The threshold direction matches the crossing direction
        ///    (Rising = crossing upward, Falling = crossing downward, Both = either)
        ///
        /// Example:
        ///   previous = 280K, current = 320K, threshold = 300K Rising
        ///   -> crossing detected (280 -> 320 spans 300, going up)
        ///
        ///   previous = 320K, current = 280K, threshold = 300K Rising
        ///   -> no crossing (going down, not up)
        /// </remarks>
        public void Collect(BlockInstance block, float previous, float current, List<ThresholdCrossing> results)
        {
            if (results == null || previous == current) return;

            // Determine crossing direction
            bool rising = current > previous;
            float low = rising ? previous : current;
            float high = rising ? current : previous;

            // Check each registered threshold
            for (int i = 0; i < thresholds.Count; i++)
            {
                ThermalThreshold threshold = thresholds[i];

                // Skip if threshold is outside the temperature range
                if (threshold.Temperature <= low || threshold.Temperature > high) continue;

                // Skip if direction doesn't match
                if (threshold.Direction == ThresholdDirection.Rising && !rising) continue;
                if (threshold.Direction == ThresholdDirection.Falling && rising) continue;

                // Record the crossing
                results.Add(new ThresholdCrossing(
                    threshold.Id, block, threshold.Temperature, current, rising));
            }
        }
    }
}
