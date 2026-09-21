using System;
using System.Collections.Generic;

namespace Thermodynamics.Core
{
    public enum ThresholdDirection
    {
        Rising = 0,

        Falling = 1,

        Both = 2,
    }

    public struct ThermalThreshold
    {
        public int Id;

        public float Temperature;

        public ThresholdDirection Direction;

/// <summary>ThermalThreshold operation.</summary>
        public ThermalThreshold(int id, float temperature, ThresholdDirection direction)
        {
            Id = id;
            Temperature = temperature;
            Direction = direction;
        }
    }

    public struct ThresholdCrossing
    {
        public int ThresholdId;
        public BlockInstance Block;

        public float Threshold;

        public float Temperature;

        public bool Rising;

/// <summary>ThresholdCrossing operation.</summary>
        public ThresholdCrossing(int id, BlockInstance block, float threshold, float temperature, bool rising)
        {
            ThresholdId = id;
            Block = block;
            Threshold = threshold;
            Temperature = temperature;
            Rising = rising;
        }
    }

    public class ThermalThresholds
    {
/// <summary>List operation.</summary>
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

/// <summary>Adds a .</summary>
        public int Add(float temperature, ThresholdDirection direction)
        {
            int id = nextId++;
            thresholds.Add(new ThermalThreshold(id, temperature, direction));
            return id;
        }

/// <summary>Adds a .</summary>
        public void Add(ThermalThreshold threshold)
        {
            Remove(threshold.Id);
            thresholds.Add(threshold);
            if (threshold.Id >= nextId) nextId = threshold.Id + 1;
        }

/// <summary>Removes the .</summary>
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

/// <summary>Clear operation.</summary>
        public void Clear()
        {
            thresholds.Clear();
        }

/// <summary>Collect operation.</summary>
        public void Collect(BlockInstance block, float previous, float current, List<ThresholdCrossing> results)
        {
            if (results == null || previous == current) return;

            bool rising = current > previous;
            float low = rising ? previous : current;
            float high = rising ? current : previous;

            for (int i = 0; i < thresholds.Count; i++)
            {
                ThermalThreshold threshold = thresholds[i];

                if (threshold.Temperature <= low || threshold.Temperature > high) continue;

                if (threshold.Direction == ThresholdDirection.Rising && !rising) continue;
                if (threshold.Direction == ThresholdDirection.Falling && rising) continue;

                results.Add(new ThresholdCrossing(
                    threshold.Id, block, threshold.Temperature, current, rising));
            }
        }
    }
}
