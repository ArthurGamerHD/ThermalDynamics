using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// One number per block: the heat it makes at full rating over the most it could possibly shed at
    /// its own critical temperature, where the denominator is deliberately the best case that exists.
    /// **Above 1 the block cooks itself and only the definition can be changed.** Computed from the
    /// definition alone, which is what makes it a gate.
    /// See balance.md, One number says whether a block can survive itself.
    /// </summary>
    public static class BlockHeatIndex
    {
        /// <summary>
        /// What the block is assumed to be surrounded by: ordinary steel armour at the temperature
        /// a grid starts at. Both halves of that are generous — armour is the best-conducting thing
        /// a hull is commonly made of, and holding it at ambient means the neighbours are treated
        /// as an infinite heat sink that never warms up.
        /// </summary>
        public const float AmbientKelvin = 293.15f;

        /// <summary>
        /// The clock the pace figures are quoted against: the shipped <c>HeatTimeScale</c>.
        ///
        /// Every seconds-to-critical here is proportional to it, so a world running another value
        /// scales them by the ratio. It is named rather than read from a settings instance because
        /// this class is a property of the definitions and must not depend on a session.
        /// </summary>
        public const float PaceHeatTimeScale = 225f;

        private static readonly float NeighbourConductivity =
            BlockMaterials.Steel.Conductivity * ThermalConstants.ConductionScale;

        public class Reading
        {
            public string Subtype;
            public string TypeId;
            public bool Large;

            /// <summary>Waste watts with the block at its full rating.</summary>
            public float Watts;

            /// <summary>Where those watts come from: its output, its draw, or its thrust.</summary>
            public string Source;

            /// <summary>Bare exterior area, every face exposed, m2.</summary>
            public float AreaSquareMetres;

            public float CriticalKelvin;
            public float Emissivity;

            /// <summary>Emissivity x sigma x area, W/K^4: the block's whole radiating skin.</summary>
            public float RadiativeCoefficient;

            /// <summary>Watts it can radiate at its critical temperature.</summary>
            public float RadiatedWatts;

            /// <summary>Watts it can conduct into ambient armour on every face at that temperature.</summary>
            public float ConductedWatts;

            /// <summary>
            /// Waste over the sum of the two. **Above 1 is unsurvivable anywhere**, because the
            /// denominator already assumes every face radiating to deep space and every face bolted
            /// to armour that never warms up.
            /// </summary>
            public float Index;

            /// <summary>
            /// Waste over what the block can radiate through its own skin alone, with no help from
            /// its neighbours.
            ///
            /// <para>
            /// This is the number that says whether a block is *self-sufficient*, and it is usually
            /// the more useful of the two. A block above 1 here is not doomed — it can survive by
            /// exporting into the hull — but it is now the hull's problem, and the value is how many
            /// times its own skin's worth of heat it has to push somewhere else. A jump drive sits
            /// at 7.4: it is survivable only where something else sheds seven-eighths of its output,
            /// which is why every corpus ship carrying one still loses a block.
            /// </para>
            /// </summary>
            public float SelfIndex;

            /// <summary>Where a fully exposed block would settle on its own skin, ignoring conduction.</summary>
            public float EquilibriumKelvin;

            /// <summary>
            /// Hull area, in square metres of ordinary armour at 400 K, needed to shed what this
            /// block exports. Zero for a block that is self-sufficient.
            /// </summary>
            public float HullAreaNeeded;

            /// <summary>
            /// Heat capacity at the shipped <c>HeatTimeScale</c>, J/K. This is the simulated
            /// figure rather than the physical one: the solver divides every capacity by that
            /// dial, so a block's real thermal mass is this times the dial.
            /// </summary>
            public float HeatCapacity;

            /// <summary>
            /// Seconds from ambient to its own critical temperature, alone in the dark. **The index
            /// says where a block ends up and this says how long it takes**, which
            /// <see cref="Index"/> cannot distinguish. Infinite where the equilibrium is at or below
            /// critical; conduction is excluded, since this is the block on its own.
            /// See balance.md, No block lands in the 2–5 minute window.
            /// </summary>
            public float SecondsToCritical;

            /// <summary>
            /// The block's full hit points, from <see cref="GameBlocks.Definition.Integrity"/>.
            /// Zero where the install is absent, which is the only case this figure is unavailable.
            /// </summary>
            public float Integrity;

            /// <summary>
            /// Hit points the block loses per kelvin over its rating per simulated second, from its
            /// own definition. Carried so the span above can be recomputed at another value — which
            /// is the only way to ask what the dial is worth. See balance.md, How long a block has
            /// after it crosses.
            /// </summary>
            public float DamagePerKelvin;

            /// <summary>
            /// Simulated seconds from the crossing to the block being destroyed, alone in the dark
            /// and on the same terms as <see cref="SecondsToCritical"/>.
            ///
            /// <para>
            /// **The crossing is not the loss, and this is the difference.** The solver damages an
            /// overheating block by <c>(T - critical) x OverheatDamagePerKelvin</c> per simulated
            /// second, so the moment it crosses it is losing hit points at a rate of zero. How long
            /// it survives is that rate integrated against the block's own integrity, and it is the
            /// span a warning has to be useful in. See balance.md, How long a block has after it
            /// crosses.
            /// </para>
            ///
            /// <para>
            /// Infinite where the block never crosses at all, and infinite where it settles so near
            /// its own limit that an hour of play does not finish it — past that the figure has
            /// stopped being a statement about gameplay.
            /// </para>
            /// </summary>
            public float SecondsCriticalToLoss;

            /// <summary>
            /// <see cref="SecondsToCritical"/> plus <see cref="SecondsCriticalToLoss"/>: seconds
            /// from a cold start at full rating to the block being gone.
            /// </summary>
            public float SecondsToLoss
            {
                get { return SecondsToCritical + SecondsCriticalToLoss; }
            }

            public bool Impossible
            {
                get { return Index > 1f; }
            }
        }

        /// <summary>Every vanilla block that makes heat at all, worst first.</summary>
        public static List<Reading> All()
        {
            List<Reading> readings = new List<Reading>();

            foreach (KeyValuePair<string, GameBlocks.Definition> entry in GameBlocks.BySubtype())
            {
                Reading reading = Measure(entry.Value);
                if (reading != null) readings.Add(reading);
            }

            readings.Sort(delegate (Reading a, Reading b) { return b.Index.CompareTo(a.Index); });
            return readings;
        }

        /// <summary>
        /// The index for one block, or null where it makes no heat to speak of.
        ///
        /// Properties are derived from the block's component list exactly as
        /// <see cref="Blueprints.Model"/> derives them, so the number describes the block the corpus
        /// actually simulated rather than a second opinion about it. <c>ShippedBlocks</c> is the
        /// wrong source here: it holds the eighteen blocks this mod ships, not the game's.
        /// </summary>
        public static Reading Measure(GameBlocks.Definition rating)
        {
            if (rating == null || rating.Components.Count == 0) return null;

            BlockThermalProperties thermal = ShippedBlocks.DeriveWithFunction(
                rating.Components, rating.TypeId, rating.PowerEfficiency);
            if (thermal == null) return null;

            // ---- what it makes at full rating --------------------------------------------------
            // The same three cases ShipLoad applies, and in the same order: a producer's heat comes
            // off its output, a thruster's off its thrust as a watt-equivalent, and everything else
            // off its draw.
            float watts;
            string source;

            if (rating.PowerOutputWatts > 0f)
            {
                watts = rating.PowerOutputWatts * thermal.ProducerWasteEnergy;
                source = "output";
            }
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

            // ---- radiation, at its own limit -----------------------------------------------------
            float area = 2f * (size.X * size.Y + size.Y * size.Z + size.X * size.Z) * cell * cell;
            area *= thermal.ExposedSurfaceMultiplier;

            float critical = thermal.CriticalTemperature;
            float radiated = thermal.Emissivity * ThermalConstants.StefanBoltzmann * area
                * (Pow4(critical) - Pow4(AmbientKelvin));

            // ---- conduction, into armour on every face -------------------------------------------
            // The solver's own series formula: contact area over the sum of each side's half-depth
            // divided by its conductivity. Summed over all six faces, with the neighbour the best
            // ordinary conductor a hull is built from.
            float conductivity = thermal.Conductivity * ThermalConstants.ConductionScale;
            float conductance = 0f;

            if (conductivity > 0f)
            {
                conductance += FaceConductance(size.Y * size.Z, size.X, cell, conductivity);
                conductance += FaceConductance(size.X * size.Z, size.Y, cell, conductivity);
                conductance += FaceConductance(size.X * size.Y, size.Z, cell, conductivity);
                conductance *= 2f;
            }

            float conducted = conductance * (critical - AmbientKelvin);

            float shed = radiated + conducted;

            // What the hull has to take off it, and what that costs in armour. Ordinary steel
            // radiates at 0.15, and 400 K is about as hot as a hull can run without its own
            // problems, so this is a fair price rather than a best case.
            float exported = watts - radiated;
            float hullFlux = BlockMaterials.Steel.Emissivity * ThermalConstants.StefanBoltzmann
                * (Pow4(400f) - Pow4(ThermalConstants.MinimumTemperature));

            // ---- pace: how long it takes to get there ------------------------------------------
            // The destination and the pace are different questions and the index only answers the
            // first. Capacity is the solver's own, so the seconds are the seconds a player waits.
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
                SecondsToCritical = SecondsToReach(capacity, watts,
                    thermal.Emissivity * ThermalConstants.StefanBoltzmann * area, critical),
                Integrity = rating.Integrity,
                DamagePerKelvin = thermal.OverheatDamagePerKelvin,
                SecondsCriticalToLoss = SecondsFromCriticalToLoss(capacity, watts,
                    thermal.Emissivity * ThermalConstants.StefanBoltzmann * area, critical,
                    thermal.OverheatDamagePerKelvin, rating.Integrity),
            };
        }

        /// <summary>
        /// Seconds for a lump of heat capacity <paramref name="capacity"/> to climb from ambient to
        /// <paramref name="target"/> while making <paramref name="watts"/> and radiating through a
        /// skin of <paramref name="radiativeCoefficient"/> = emissivity x sigma x area.
        ///
        /// <para>
        /// Integrated in temperature rather than in time — <c>t = integral of C/net(T) dT</c> —
        /// because that has no timestep to argue about and lands on the exact answer for a smooth
        /// integrand. Simpson's rule over a fixed number of intervals, so the figure is
        /// reproducible to the bit.
        /// </para>
        ///
        /// <para>
        /// Returns infinity when the net rate reaches zero before the target does, which is exactly
        /// the case of a block whose equilibrium is below its own limit. Returns zero for a block
        /// that is already at or above the target.
        /// </para>
        /// </summary>
        public static float SecondsToReach(float capacity, float watts, float radiativeCoefficient,
            float target)
        {
            if (capacity <= 0f || watts <= 0f) return float.PositiveInfinity;
            if (target <= AmbientKelvin) return 0f;

            const int Intervals = 2048;                     // even, as Simpson requires
            double ambient4 = Pow4(AmbientKelvin);
            double width = (target - AmbientKelvin) / (double)Intervals;
            double total = 0d;

            for (int i = 0; i <= Intervals; i++)
            {
                double temperature = AmbientKelvin + (i * width);
                double net = watts - (radiativeCoefficient * (Pow4d(temperature) - ambient4));

                // The net rate falls monotonically with temperature, so the first non-positive
                // sample is the equilibrium and nothing past it is reachable.
                if (net <= 0d) return float.PositiveInfinity;

                double weight = (i == 0 || i == Intervals) ? 1d : ((i % 2) == 1 ? 4d : 2d);
                total += weight * (capacity / net);
            }

            return (float)(total * width / 3d);
        }

        /// <summary>
        /// The horizon this figure is quoted over. A block that has not been destroyed after an
        /// hour of play is reported as never destroyed, because past that the number is no longer
        /// answering a question about a session.
        /// </summary>
        public const float LossHorizonSeconds = 3600f;

        /// <summary>
        /// Simulated seconds from the crossing to destruction, for a block making
        /// <paramref name="watts"/>, alone in the dark behind a skin of
        /// <paramref name="radiativeCoefficient"/> = emissivity x sigma x area.
        ///
        /// <para>
        /// Two coupled quantities: the temperature, which climbs from <paramref name="critical"/>
        /// towards its own equilibrium, and the damage, which is the solver's own
        /// <c>(T - critical) x <paramref name="damagePerKelvin"/></c> per simulated second
        /// integrated from the crossing. The answer is the time at which the second reaches
        /// <paramref name="integrity"/>.
        /// </para>
        ///
        /// <para>
        /// Integrated in temperature rather than in time, as <see cref="SecondsToReach"/> is, so
        /// there is no timestep to argue about — but on a grid graded geometrically towards the
        /// equilibrium rather than a uniform one, because the integrand goes as
        /// <c>1/(equilibrium - T)</c> and a uniform grid spends all its samples where nothing is
        /// happening. The last thousandth of the approach is closed rather than integrated: a block
        /// that far in is at a constant damage rate, so what is left is a division.
        /// </para>
        ///
        /// <para>
        /// Returns infinity where the block never reaches <paramref name="critical"/> at all, and
        /// where destruction is past <see cref="LossHorizonSeconds"/>. Returns zero for a block with
        /// no integrity to lose, which is a block the install could not price.
        /// </para>
        /// </summary>
        public static float SecondsFromCriticalToLoss(float capacity, float watts,
            float radiativeCoefficient, float critical, float damagePerKelvin, float integrity)
        {
            if (capacity <= 0f || watts <= 0f || damagePerKelvin <= 0f) return float.PositiveInfinity;
            if (integrity <= 0f) return 0f;
            if (critical <= AmbientKelvin) return float.PositiveInfinity;

            // A skin that radiates nothing has no equilibrium: the climb is linear, the damage rate
            // rises linearly with it, and the time follows in closed form.
            if (radiativeCoefficient <= 0d)
            {
                double linear = Math.Sqrt(2d * capacity * integrity / (damagePerKelvin * (double)watts));
                return linear > LossHorizonSeconds ? float.PositiveInfinity : (float)linear;
            }

            double ambient4 = Pow4d(AmbientKelvin);
            double equilibrium = Math.Pow((watts / radiativeCoefficient) + ambient4, 0.25d);

            // Its own skin holds it under the limit, so it never crosses and never takes damage.
            // The same case SecondsToReach reports as infinite, and reported the same way here.
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
                    // Linear in the interval, which is all the trapezoid claimed anyway.
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

            // Settled within a thousandth of its equilibrium with integrity to spare: from here the
            // damage rate is constant, so the rest is arithmetic rather than integration.
            double tail = damagePerKelvin * (previousTemperature - critical);
            if (tail <= 0d) return float.PositiveInfinity;

            double total = seconds + ((integrity - damage) / tail);
            return total > LossHorizonSeconds ? float.PositiveInfinity : (float)total;
        }

        private static double Pow4d(double value)
        {
            double square = value * value;
            return square * square;
        }

        /// <summary>
        /// Conductance of one face into an armour cube, W/K.
        ///
        /// <paramref name="contactCells"/> is the face's area in lattice cells and
        /// <paramref name="depthCells"/> the block's extent along that axis, whose half is the
        /// distance from its centre to the interface.
        /// </summary>
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

        private static float Pow4(float value)
        {
            float square = value * value;
            return square * square;
        }
    }
}
