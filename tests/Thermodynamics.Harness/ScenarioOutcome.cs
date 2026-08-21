using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// Everything one ship in one state is worth recording.
    ///
    /// <para>
    /// A single final temperature is not enough to judge balance by, and the reason is spatial. Two
    /// ships settling at the same peak are different ships if one is uniformly warm and the other
    /// is cold everywhere except a 900 K knot around its thrusters — the first has a cooling
    /// problem and the second has a *layout* problem, and only one of them is fixed by adding
    /// radiators. So this records the distribution and where its top end is, not just its top end.
    /// </para>
    ///
    /// <para>
    /// It also records what got the heat there. The per-mechanism shares say whether a hot ship is
    /// hot from its own reactors, from the sun, or from flying too fast through air, and those
    /// three have nothing to do with one another.
    /// </para>
    /// </summary>
    public class ScenarioOutcome
    {
        public string Ship;
        public string Scenario;
        public long WorkshopId;
        public int Blocks;

        /// <summary>Grids in the blueprint, and the mechanical joints linking them.</summary>
        public int Grids;
        public int Joints;

        // ---- where it ended up -----------------------------------------------------------------

        /// <summary>Kelvin, across every block, at the end of the run.</summary>
        public float PeakKelvin;
        public float MeanKelvin;
        public float MedianKelvin;
        public float P95Kelvin;
        public float MinKelvin;

        /// <summary>Peak minus min: how unevenly the heat is spread.</summary>
        public float GradientKelvin
        {
            get { return PeakKelvin - MinKelvin; }
        }

        /// <summary>
        /// Peak minus mean. The hot-spot measure: a ship that is uniformly warm has a small one
        /// however hot it is, and a ship with a knot around its thrusters has a large one however
        /// cool its average.
        /// </summary>
        public float HotSpotKelvin
        {
            get { return PeakKelvin - MeanKelvin; }
        }

        /// <summary>Which block reached <see cref="PeakKelvin"/>, and where it sits.</summary>
        public string HottestBlock;
        public Vector3I HottestCell;

        /// <summary>Blocks within 50 K of the peak: whether the hot spot is one block or a region.</summary>
        public int HotSpotBlocks;

        // ---- whether it survived ---------------------------------------------------------------

        public int BlocksOverCritical;

        public float OverCriticalShare
        {
            get { return Blocks == 0 ? 0f : BlocksOverCritical / (float)Blocks; }
        }

        /// <summary>Kelvin the peak block sits below its own critical temperature. Negative is damage.</summary>
        public float MarginKelvin;

        /// <summary>Simulated seconds before the first block went critical, or -1.</summary>
        public float SecondsToCritical = -1f;

        // ---- how it got there ------------------------------------------------------------------

        /// <summary>Simulated seconds to come within 5 K of the final peak. Thermal inertia.</summary>
        public float SecondsToSettle = -1f;

        /// <summary>Fastest rate of change seen at the peak block, K/s.</summary>
        public float PeakRateKelvinPerSecond;

        /// <summary>Watts the grid was making and shedding when the run ended.</summary>
        public float MadeWatts;
        public float VentedWatts;

        /// <summary>
        /// Watts by mechanism at the end of the run, summed over the grid. Positive is heat in.
        ///
        /// These are what distinguish the *kind* of trouble a ship is in. A hull hot from solar
        /// gain wants shading or a lower absorptivity; one hot from friction wants to slow down;
        /// one hot from its own reactors wants radiators. The temperature alone says none of that.
        /// </summary>
        public float RadiationWatts;
        public float ConvectionWatts;
        public float SolarWatts;
        public float FrictionWatts;
        public float GenerationWatts;

        // ---- what it cost ----------------------------------------------------------------------

        public float SubstepsDemanded;
        public int SubstepsGranted;

        /// <summary>Share of the grid's energy the run gained or lost that it should not have.</summary>
        public float EnergyDrift;

        public override string ToString()
        {
            return Ship + " / " + Scenario + ": " + PeakKelvin.ToString("n0") + " K peak";
        }

        /// <summary>
        /// Reads a finished run into an outcome.
        ///
        /// Every figure comes from the solver's own state rather than from the sample history, so
        /// nothing here depends on how often the run was sampled — except the two that are about
        /// time, which are handed in.
        /// </summary>
        public static ScenarioOutcome Read(ShipAssembly assembly, string ship, string scenario)
        {
            ScenarioOutcome outcome = new ScenarioOutcome
            {
                Ship = ship,
                Scenario = scenario,
                Blocks = assembly.NodeCount,
                Grids = assembly.Simulations.Count,
                Joints = assembly.Bridges.Count,
                MinKelvin = float.MaxValue,
                MadeWatts = assembly.HeatGainWatts,
                VentedWatts = assembly.VentedWatts,
                SubstepsDemanded = assembly.RequiredSubsteps,
                SubstepsGranted = assembly.GrantedSubsteps,
            };

            if (outcome.Blocks == 0) return outcome;

            List<float> temperatures = new List<float>(outcome.Blocks);
            float total = 0f;
            float margin = float.MaxValue;

            foreach (ThermalNode node in assembly.Nodes)
            {
                float kelvin = node.Temperature;

                temperatures.Add(kelvin);
                total += kelvin;

                if (kelvin > outcome.PeakKelvin)
                {
                    outcome.PeakKelvin = kelvin;
                    outcome.HottestBlock = node.Block.Name;
                    outcome.HottestCell = node.Block.Position;
                }
                if (kelvin < outcome.MinKelvin) outcome.MinKelvin = kelvin;

                float critical = node.Block.Thermal.CriticalTemperature;
                if (kelvin > critical) outcome.BlocksOverCritical++;
                if (critical - kelvin < margin) margin = critical - kelvin;

                outcome.RadiationWatts += node.LastRadiationWatts;
                outcome.ConvectionWatts += node.LastConvectionWatts;
                outcome.SolarWatts += node.LastSolarWatts;
                outcome.FrictionWatts += node.LastFrictionWatts;
                outcome.GenerationWatts += node.HeatGenerationWatts;
            }

            outcome.MeanKelvin = total / outcome.Blocks;
            outcome.MarginKelvin = margin;

            temperatures.Sort();
            outcome.MedianKelvin = temperatures[temperatures.Count / 2];
            outcome.P95Kelvin = temperatures[(int)(0.95f * (temperatures.Count - 1))];

            // A hot spot that is one block is a definition problem; one that is fifty blocks is a
            // layout problem. The distinction is only visible if the region is counted.
            float near = outcome.PeakKelvin - 50f;
            for (int i = 0; i < temperatures.Count; i++)
            {
                if (temperatures[i] >= near) outcome.HotSpotBlocks++;
            }

            return outcome;
        }
    }
}
