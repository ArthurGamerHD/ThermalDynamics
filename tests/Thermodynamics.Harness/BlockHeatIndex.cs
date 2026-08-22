using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// One number per block: the heat it makes at full rating, over the most heat it could possibly
    /// shed while staying at its own critical temperature.
    ///
    /// <para>
    /// **Above 1, the block cooks itself and no build can prevent it.** The denominator is
    /// deliberately the best case that exists — every face exposed to deep space, and every face
    /// simultaneously bolted to armour held at ambient, so the block is getting both escape routes
    /// at once and at their most generous. A block that still cannot break even against that has no
    /// arrangement, no hull and no amount of surrounding cooling that saves it; the only fixes are
    /// to the definition. Below 1 the block is survivable in principle and the build decides.
    /// </para>
    ///
    /// <para>
    /// The index is a ratio of watts, so it reads directly as a prescription: 7.2 means the waste
    /// heat has to come down 7.2-fold, or the shedding has to go up by as much, before the block is
    /// even theoretically viable.
    /// </para>
    ///
    /// <para>
    /// **It is computed from the definition alone** — no simulation, no corpus, no scenario. That is
    /// what makes it a gate: a new block, or a changed waste fraction, can be checked the moment it
    /// is written, and the corpus is then only needed to confirm the consequence.
    /// </para>
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

            BlockThermalProperties thermal =
                BlockThermalDerivation.Derive(rating.Components, rating.TypeId);
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
                RadiatedWatts = radiated,
                ConductedWatts = conducted,
                Index = shed > 0f ? watts / shed : float.PositiveInfinity,
                SelfIndex = radiated > 0f ? watts / radiated : float.PositiveInfinity,
                HullAreaNeeded = exported > 0f && hullFlux > 0f ? exported / hullFlux : 0f,
                EquilibriumKelvin = area > 0f && thermal.Emissivity > 0f
                    ? (float)Math.Pow(watts / (area * thermal.Emissivity
                        * ThermalConstants.StefanBoltzmann), 0.25d)
                    : float.PositiveInfinity,
            };
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
