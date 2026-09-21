using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    public class ScenarioOutcome
    {
        public string Ship;
        public string Scenario;
        public long WorkshopId;
        public int Blocks;

        public int Grids;
        public int Joints;


        public float PeakKelvin;
        public float MeanKelvin;
        public float MedianKelvin;
        public float P95Kelvin;
        public float MinKelvin;

        public float GradientKelvin
        {
            get { return PeakKelvin - MinKelvin; }
        }

        public float HotSpotKelvin
        {
            get { return PeakKelvin - MeanKelvin; }
        }

        public string HottestBlock;
        public Vector3I HottestCell;

        public int HotSpotBlocks;


        public int BlocksOverCritical;

        public float OverCriticalShare
        {
            get { return Blocks == 0 ? 0f : BlocksOverCritical / (float)Blocks; }
        }

        public float MarginKelvin;

        public float SecondsToCritical = -1f;

        public float SecondsToFirstLoss = -1f;


        public float SecondsToSettle = -1f;

        public float PeakRateKelvinPerSecond;

        public float ThermalMass;

        public float BulkDriftKelvinPerSecond = float.NaN;

        public float BulkDriftWatts = float.NaN;

        public float MadeWatts;
        public float VentedWatts;

        public float RadiationWatts;
        public float ConvectionWatts;
        public float SolarWatts;
        public float FrictionWatts;
        public float GenerationWatts;


        public float SubstepsDemanded;
        public int SubstepsGranted;

        public int Links;

        public float RunSeconds;

        public int SubstepsPerBlockCap;

        public int FlooredNodes;

        public long SubstepCost;

/// <summary>ToString operation.</summary>
        public override string ToString()
        {
            return Ship + " / " + Scenario + ": " + PeakKelvin.ToString("n0") + " K peak";
        }

/// <summary>Read operation.</summary>
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
                Links = assembly.LinkCount,
                SubstepCost = assembly.WorstGridSubstepCost,
                FlooredNodes = assembly.FlooredNodes,
            };

            if (outcome.Blocks == 0) return outcome;

/// <summary>List operation.</summary>
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

                outcome.ThermalMass += node.ThermalMass;
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

            float near = outcome.PeakKelvin - 50f;
            for (int i = 0; i < temperatures.Count; i++)
            {
                if (temperatures[i] >= near) outcome.HotSpotBlocks++;
            }

            return outcome;
        }
    }
}
