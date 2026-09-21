using System;
using System.Collections.Generic;

namespace Thermodynamics.Core
{
    public enum HeatCueStage
    {
        None = 0,

        Approaching = 1,

        Critical = 2,
    }

    public struct HeatCue
    {
        public BlockInstance Block;

        public float Kelvin;

        public float Critical;

        public float Glow;

        public HeatCueStage Stage;

        public bool Announce;

        public float SecondsToCritical;
    }

    public class HeatCueState
    {
        public const float WatchFraction = 0.75f;

        public struct Previous
        {
            public float Kelvin;
            public float Rate;
            public HeatCueStage Stage;
            public bool HasRate;
        }

        private readonly Dictionary<BlockInstance, Previous> watched =
            new Dictionary<BlockInstance, Previous>();

/// <summary>List operation.</summary>
        private readonly List<BlockInstance> cold = new List<BlockInstance>();

        public int Count
        {
            get { return watched.Count; }
        }

/// <summary>TryGet operation.</summary>
        public bool TryGet(BlockInstance block, out Previous previous)
        {
            return watched.TryGetValue(block, out previous);
        }

/// <summary>Sets the .</summary>
        public void Set(BlockInstance block, Previous previous)
        {
            watched[block] = previous;
        }

/// <summary>Forget operation.</summary>
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

/// <summary>Clear operation.</summary>
        public void Clear()
        {
            watched.Clear();
            cold.Clear();
        }
    }

    public partial class ThermalSolver
    {
        public float LowestCriticalTemperature
        {
            get { return lowestCritical; }
        }

        private float lowestCritical = float.PositiveInfinity;

/// <summary>HashSet operation.</summary>
        private readonly HashSet<BlockInstance> cueSeen = new HashSet<BlockInstance>();

/// <summary>CueFloorTemperature operation.</summary>
        public float CueFloorTemperature()
        {
            if (float.IsInfinity(lowestCritical)) return float.PositiveInfinity;

            float watch = lowestCritical * HeatCueState.WatchFraction;
            float glow = Incandescence.GlowStartKelvin(lowestCritical);
            return watch < glow ? watch : glow;
        }

/// <summary>CollectHeatCues operation.</summary>
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
/// <summary>if operation.</summary>
                else if (forecast.WillCross && forecast.Seconds <= HeatWarning.LeadSeconds)
                {
                    stage = HeatCueStage.Approaching;
                }

/// <summary>HeatCue operation.</summary>
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
