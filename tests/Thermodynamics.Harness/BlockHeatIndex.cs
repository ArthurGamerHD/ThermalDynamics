using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    public static class BlockHeatIndex
    {
        public const float AmbientKelvin = 293.15f;

        public const float PaceHeatTimeScale = 90f;

        private static readonly float NeighbourConductivity =
            BlockMaterials.Steel.Conductivity * ThermalConstants.ConductionScale;

        public class Reading
        {
            public string Subtype;
            public string TypeId;
            public bool Large;

            public float Watts;

            public string Source;

            public float AreaSquareMetres;

            public float CriticalKelvin;
            public float Emissivity;

            public float RadiativeCoefficient;

            public float RadiatedWatts;

            public float ConductedWatts;

            public float Index;

            public float SelfIndex;

            public float EquilibriumKelvin;

            public float HullAreaNeeded;

            public float HeatCapacity;

            public float SecondsToCritical;

            public float Integrity;

            public float DamagePerKelvin;

            public float SecondsCriticalToLoss;

            public float SecondsToLoss
            {
                get { return SecondsToCritical + SecondsCriticalToLoss; }
            }

            public bool Impossible
            {
                get { return Index > 1f; }
            }
        }

/// <summary>All operation.</summary>
        public static List<Reading> All()
        {
/// <summary>List operation.</summary>
            List<Reading> readings = new List<Reading>();

            foreach (KeyValuePair<string, GameBlocks.Definition> entry in GameBlocks.BySubtype())
            {
/// <summary>Measure operation.</summary>
                Reading reading = Measure(entry.Value);
                if (reading != null) readings.Add(reading);
            }

            readings.Sort(delegate (Reading a, Reading b) { return b.Index.CompareTo(a.Index); });
            return readings;
        }

/// <summary>Measure operation.</summary>
        public static Reading Measure(GameBlocks.Definition rating)
        {
            if (rating == null || rating.Components.Count == 0) return null;

            BlockThermalProperties thermal = ShippedBlocks.DeriveWithFunction(
                rating.Components, rating.TypeId, rating.PowerEfficiency);
            if (thermal == null) return null;

            float watts;
            string source;

            if (rating.PowerOutputWatts > 0f)
            {
                watts = rating.PowerOutputWatts * thermal.ProducerWasteEnergy;
                source = "output";
            }
/// <summary>if operation.</summary>
            else if (rating.ThrustNewtons > 0f)
            {
                watts = rating.ThrustNewtons * thermal.ConsumerWasteEnergy;
                source = "thrust";
            }
            else
            {
                watts = rating.PowerDrawWatts * thermal.ConsumerWasteEnergy;
                source = "draw";
            }

            if (watts <= 1f) return null;

            float cell = rating.Large ? Catalog.LargeGridSize : Catalog.SmallGridSize;
            Vector3I size = rating.Size;

            float area = 2f * (size.X * size.Y + size.Y * size.Z + size.X * size.Z) * cell * cell;
            area *= thermal.ExposedSurfaceMultiplier;

            float critical = thermal.CriticalTemperature;
            float radiated = thermal.Emissivity * ThermalConstants.StefanBoltzmann * area
                * (Pow4(critical) - Pow4(AmbientKelvin));

            float conductivity = thermal.Conductivity * ThermalConstants.ConductionScale;
            float conductance = 0f;

            if (conductivity > 0f)
            {
/// <summary>FaceConductance operation.</summary>
                conductance += FaceConductance(size.Y * size.Z, size.X, cell, conductivity);
/// <summary>FaceConductance operation.</summary>
                conductance += FaceConductance(size.X * size.Z, size.Y, cell, conductivity);
/// <summary>FaceConductance operation.</summary>
                conductance += FaceConductance(size.X * size.Y, size.Z, cell, conductivity);
                conductance *= 2f;
            }

            float conducted = conductance * (critical - AmbientKelvin);

            float shed = radiated + conducted;

            float exported = watts - radiated;
            float hullFlux = BlockMaterials.Steel.Emissivity * ThermalConstants.StefanBoltzmann
                * (Pow4(400f) - Pow4(ThermalConstants.MinimumTemperature));

            float capacity = rating.Mass * thermal.SpecificHeat / PaceHeatTimeScale;

            return new Reading
            {
                Subtype = rating.SubtypeId,
                TypeId = rating.TypeId,
                Large = rating.Large,
                Watts = watts,
                Source = source,
                AreaSquareMetres = area,
                CriticalKelvin = critical,
                Emissivity = thermal.Emissivity,
                RadiativeCoefficient = thermal.Emissivity * ThermalConstants.StefanBoltzmann * area,
                RadiatedWatts = radiated,
                ConductedWatts = conducted,
                Index = shed > 0f ? watts / shed : float.PositiveInfinity,
                SelfIndex = radiated > 0f ? watts / radiated : float.PositiveInfinity,
                HullAreaNeeded = exported > 0f && hullFlux > 0f ? exported / hullFlux : 0f,
                EquilibriumKelvin = area > 0f && thermal.Emissivity > 0f
                    ? (float)Math.Pow(watts / (area * thermal.Emissivity
                        * ThermalConstants.StefanBoltzmann), 0.25d)
                    : float.PositiveInfinity,
                HeatCapacity = capacity,
/// <summary>SecondsToReach operation.</summary>
                SecondsToCritical = SecondsToReach(capacity, watts,
                    thermal.Emissivity * ThermalConstants.StefanBoltzmann * area, critical),
                Integrity = rating.Integrity,
                DamagePerKelvin = thermal.OverheatDamagePerKelvin,
/// <summary>SecondsFromCriticalToLoss operation.</summary>
                SecondsCriticalToLoss = SecondsFromCriticalToLoss(capacity, watts,
                    thermal.Emissivity * ThermalConstants.StefanBoltzmann * area, critical,
                    thermal.OverheatDamagePerKelvin, rating.Integrity),
            };
        }

/// <summary>SecondsToReach operation.</summary>
        public static float SecondsToReach(float capacity, float watts, float radiativeCoefficient,
            float target)
        {
            if (capacity <= 0f || watts <= 0f) return float.PositiveInfinity;
            if (target <= AmbientKelvin) return 0f;

            const int Intervals = 2048;                     // even, as Simpson requires
/// <summary>Pow4 operation.</summary>
            double ambient4 = Pow4(AmbientKelvin);
            double width = (target - AmbientKelvin) / (double)Intervals;
            double total = 0d;

            for (int i = 0; i <= Intervals; i++)
            {
                double temperature = AmbientKelvin + (i * width);
                double net = watts - (radiativeCoefficient * (Pow4d(temperature) - ambient4));

                if (net <= 0d) return float.PositiveInfinity;

                double weight = (i == 0 || i == Intervals) ? 1d : ((i % 2) == 1 ? 4d : 2d);
                total += weight * (capacity / net);
            }

            return (float)(total * width / 3d);
        }

        public const float LossHorizonSeconds = 3600f;

/// <summary>SecondsFromCriticalToLoss operation.</summary>
        public static float SecondsFromCriticalToLoss(float capacity, float watts,
            float radiativeCoefficient, float critical, float damagePerKelvin, float integrity)
        {
            if (capacity <= 0f || watts <= 0f || damagePerKelvin <= 0f) return float.PositiveInfinity;
            if (integrity <= 0f) return 0f;
            if (critical <= AmbientKelvin) return float.PositiveInfinity;

            if (radiativeCoefficient <= 0d)
            {
                double linear = Math.Sqrt(2d * capacity * integrity / (damagePerKelvin * (double)watts));
                return linear > LossHorizonSeconds ? float.PositiveInfinity : (float)linear;
            }

/// <summary>Pow4d operation.</summary>
            double ambient4 = Pow4d(AmbientKelvin);
            double equilibrium = Math.Pow((watts / radiativeCoefficient) + ambient4, 0.25d);

            if (equilibrium <= critical) return float.PositiveInfinity;

            const int Intervals = 2048;
            const double Closest = 0.001d;          // how near the equilibrium the grid reaches

            double span = equilibrium - critical;
            double decay = Math.Pow(Closest, 1d / Intervals);

            double seconds = 0d;
            double damage = 0d;
            double previousTemperature = critical;
            double criticalNet = watts - (radiativeCoefficient * (Pow4d(critical) - ambient4));
            double previousTime = capacity / criticalNet;           // dt/dT where the damage starts
            double previousDamage = 0d;                             // (T - critical) is zero there
            double gap = 1d;

            for (int i = 1; i <= Intervals; i++)
            {
                gap *= decay;
                double temperature = equilibrium - (span * gap);
                double net = watts - (radiativeCoefficient * (Pow4d(temperature) - ambient4));
                if (net <= 0d) break;

                double time = capacity / net;
                double rate = damagePerKelvin * (temperature - critical) * time;
                double width = temperature - previousTemperature;

                double stepSeconds = 0.5d * (previousTime + time) * width;
                double stepDamage = 0.5d * (previousDamage + rate) * width;

                if (damage + stepDamage >= integrity)
                {
                    double share = stepDamage > 0d ? (integrity - damage) / stepDamage : 0d;
                    double landed = seconds + (stepSeconds * share);
                    return landed > LossHorizonSeconds ? float.PositiveInfinity : (float)landed;
                }

                seconds += stepSeconds;
                damage += stepDamage;
                if (seconds > LossHorizonSeconds) return float.PositiveInfinity;

                previousTemperature = temperature;
                previousTime = time;
                previousDamage = rate;
            }

            double tail = damagePerKelvin * (previousTemperature - critical);
            if (tail <= 0d) return float.PositiveInfinity;

            double total = seconds + ((integrity - damage) / tail);
            return total > LossHorizonSeconds ? float.PositiveInfinity : (float)total;
        }

/// <summary>Pow4d operation.</summary>
        private static double Pow4d(double value)
        {
            double square = value * value;
            return square * square;
        }

/// <summary>FaceConductance operation.</summary>
        private static float FaceConductance(int contactCells, int depthCells, float cell,
            float conductivity)
        {
            if (contactCells <= 0 || depthCells <= 0) return 0f;

            float contactArea = contactCells * cell * cell;
            float half = depthCells * cell * 0.5f;
            float neighbourHalf = cell * 0.5f;

            float resistance = (half / conductivity) + (neighbourHalf / NeighbourConductivity);
            return resistance > 0f ? contactArea / resistance : 0f;
        }

/// <summary>Pow4 operation.</summary>
        private static float Pow4(float value)
        {
            float square = value * value;
            return square * square;
        }
    }
}
