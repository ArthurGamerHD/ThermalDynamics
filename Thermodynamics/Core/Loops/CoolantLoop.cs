using System;
using System.Collections.Generic;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Link between a coolant loop segment and a thermal node for heat exchange.
    /// Represents a single connection point where heat is transferred between
    /// the coolant and a block's thermal node.
    /// </summary>
    public struct LoopLink
    {
        /// <summary>
        /// Index of the thermal node connected to this loop segment.
        /// Points to a node in the ThermalSolver's node array.
        /// </summary>
        public int NodeIndex;

        /// <summary>
        /// Thermal conductance in W/K (Watts per Kelvin temperature difference).
        /// Determines how much heat flows for a given temperature difference.
        /// Higher conductance = more efficient heat transfer.
        /// </summary>
        public float Conductance;

        /// <summary>
        /// Index of the coolant loop segment (pipe) in the Loop.Pipes list.
        /// Identifies which pipe in the loop this link connects to.
        /// </summary>
        public int SegmentIndex;


        /// <summary>
        /// Creates a new LoopLink instance.
        /// </summary>
        /// <param name="nodeIndex">Index of the thermal node.</param>
        /// <param name="conductance">Thermal conductance in W/K.</param>
        /// <param name="segmentIndex">Index of the coolant loop segment.</param>
        public LoopLink(int nodeIndex, float conductance, int segmentIndex)
        {
            NodeIndex = nodeIndex;
            Conductance = conductance;
            SegmentIndex = segmentIndex;
        }
    }

    /// <summary>
    /// Represents a closed-loop coolant system in the thermal simulation.
    /// A coolant loop consists of multiple connected pipes that circulate
    /// a fluid (coolant) to transfer heat between blocks.
    /// </summary>
    /// <remarks>
    /// The loop uses a segmented model where each pipe in the loop is
    /// represented as a discrete segment with its own temperature.
    /// Flow is simulated by shifting segments ( parcels ) around the loop.
    /// 
    /// Key concepts:
    /// - Parcel: A discrete temperature segment representing a portion of coolant
    /// - Shift: Offset into the segments array (circular buffer)
    /// - Travel: Accumulated fractional movement (for sub-stepping)
    /// - Advection: Movement of segments around the loop to simulate flow
    /// - Mixing: Mean-reverting process to stabilize temperature gradients
    /// </remarks>
    public class CoolantLoop
    {
        /// <summary>
        /// List of block instances that form the pipes in this loop.
        /// Each block must have CoolantShape configured with link ports.
        /// </summary>
        public readonly List<BlockInstance> Pipes = new List<BlockInstance>();

        /// <summary>
        /// List of thermal links connecting this loop to the grid's thermal nodes.
        /// Each link represents heat exchange between a loop segment and a block node.
        /// </summary>
        public readonly List<LoopLink> Links = new List<LoopLink>();

        /// <summary>
        /// Thermal properties of the coolant fluid and pipe connections.
        /// Defines heat transfer coefficients, mass per pipe, and flow rates.
        /// </summary>
        public LoopThermalProperties Properties;

        /// <summary>
        /// Average temperature of all loop segments in Kelvin.
        /// When setting, all segments are assigned this temperature.
        /// When getting, returns the arithmetic mean of all segment temperatures.
        /// </summary>
        public float Temperature
        {
            get
            {
                if (segments == null || segmentCount == 0) return seeded;

                float total = 0f;
                for (int i = 0; i < segmentCount; i++) total += segments[i];
                return total / segmentCount;
            }
            set
            {
                seeded = value;
                for (int i = 0; i < segmentCount; i++) segments[i] = value;
            }
        }

        /// <summary>
        /// Gets the temperature of a specific pipe in the loop.
        /// Uses circular buffer indexing based on parcel position.
        /// </summary>
        /// <param name="index">Index of the pipe (0 to Pipes.Count-1).</param>
        /// <returns>Temperature of the specified pipe in Kelvin.</returns>
        public float SegmentTemperature(int index)
        {
            if (segments == null || segmentCount == 0) return seeded;
            if (index < 0 || index >= Pipes.Count) return seeded;
            return segments[ParcelOf(index)];
        }

        /// <summary>
        /// Converts a pipe index to a segment (parcel) index.
        /// Implements circular buffer wrapping based on current shift position.
        /// </summary>
        /// <param name="pipeIndex">Index of the pipe in the Pipes list.</param>
        /// <returns>Index into the segments array (circular buffer position).</returns>
        /// <remarks>
        /// The parcel index accounts for the current flow position:
        ///   parcelIndex = (pipeIndex - shift) mod segmentCount
        /// This allows segments to "move" around the loop as coolant flows.
        /// </remarks>
        public int ParcelOf(int pipeIndex)
        {
            if (segmentCount <= 0) return 0;

            int slot = (pipeIndex - shift) % segmentCount;
            return slot < 0 ? slot + segmentCount : slot;
        }

        /// <summary>
        /// Number of temperature segments (parcels) in the loop.
        /// Equals the number of pipes in this loop.
        /// </summary>
        public int ParcelCount
        {
            get { return segmentCount; }
        }

        /// <summary>
        /// Current offset into the circular buffer of segments.
        /// Represents how far the parcels have moved due to flow.
        /// Negative values are handled by wrapping to the end of the array.
        /// </summary>
        private int shift;

        /// <summary>
        /// Accumulated fractional parcel movement.
        /// Used for precise flow simulation with sub-stepping.
        /// </summary>
        private float travelled;


        /// <summary>
        /// Sets the temperature of a specific pipe in the loop.
        /// Ensures the temperature stays above the minimum allowed value.
        /// </summary>
        /// <param name="pipeIndex">Index of the pipe in the Pipes list.</param>
        /// <param name="temperature">Target temperature in Kelvin.</param>
        public void SetSegmentTemperature(int pipeIndex, float temperature)
        {
            if (segments == null || segmentCount == 0) return;
            if (pipeIndex < 0 || pipeIndex >= Pipes.Count) return;

            segments[ParcelOf(pipeIndex)] = temperature < ThermalConstants.MinimumTemperature
                ? ThermalConstants.MinimumTemperature
                : temperature;
        }

        /// <summary>
        /// Gets the highest temperature among all loop segments in Kelvin.
        /// Useful for detecting hot spots in the coolant loop.
        /// </summary>
        public float HottestSegment
        {
            get
            {
                if (segments == null || segmentCount == 0) return seeded;

                float worst = segments[0];
                for (int i = 1; i < segmentCount; i++)
                {
                    if (segments[i] > worst) worst = segments[i];
                }
                return worst;
            }
        }

        /// <summary>
        /// Gets the lowest temperature among all loop segments in Kelvin.
        /// Useful for detecting cold spots in the coolant loop.
        /// </summary>
        public float ColdestSegment
        {
            get
            {
                if (segments == null || segmentCount == 0) return seeded;

                float best = segments[0];
                for (int i = 1; i < segmentCount; i++)
                {
                    if (segments[i] < best) best = segments[i];
                }
                return best;
            }
        }

        /// <summary>
        /// Array of temperature segments (parcels) in the loop.
        /// Uses circular buffer indexing with the shift field.
        /// </summary>
        private float[] segments = new float[0];

        /// <summary>
        /// Number of valid segments in the segments array.
        /// Typically equals Pipes.Count when loop is fully initialized.
        /// </summary>
        private int segmentCount;

        /// <summary>
        /// Default temperature used when segments array is empty or uninitialized.
        /// </summary>
        private float seeded;

        /// <summary>
        /// Total thermal mass of the entire loop in J/K.
        /// Sum of all segment thermal masses.
        /// </summary>
        public float ThermalMass { get; private set; }

        /// <summary>
        /// Thermal mass of each individual segment in J/K.
        /// Assumes uniform mass across all parcels.
        /// </summary>
        public float SegmentThermalMass { get; private set; }


        /// <summary>
        /// Initializes or reinitializes the loop with the given properties and temperature.
        /// Creates the segments array with proper capacity and sets initial temperatures.
        /// </summary>
        /// <param name="properties">Thermal properties of the coolant system.</param>
        /// <param name="initialTemperature">Starting temperature in Kelvin.</param>
        public CoolantLoop(LoopThermalProperties properties, float initialTemperature)
        {
            Properties = properties;
            seeded = initialTemperature;
            RefreshSignature();
            RefreshThermalMass();
        }


        /// <summary>
        /// Recalculates the loop signature (flow-based identification).
        /// Used to identify the same loop after rebuilds.
        /// </summary>
        internal void RefreshSignature()
        {
            long hash = 0;
            for (int i = 0; i < Pipes.Count; i++)
            {
                hash = (hash * 31) + Pipes[i].Key;
            }
            Signature = hash;
        }


        /// <summary>
        /// Gets the loop's unique signature hash.
        /// Used for matching loops across simulation steps.
        /// </summary>
        internal long Signature { get; private set; }


        /// <summary>
        /// Recalculates thermal mass properties based on coolant and pipe characteristics.
        /// Uses LoopThermalProperties to determine mass per pipe and specific heat.
        /// </summary>
        internal void RefreshThermalMass()
        {
            if (segmentCount <= 0)
            {
                ThermalMass = 0f;
                SegmentThermalMass = 0f;
                return;
            }

            float mass = Properties.MassPerPipe(ParcelLengthMetres);
            SegmentThermalMass = (mass * Properties.SpecificHeat) / HeatTimeScale;
            if (SegmentThermalMass < ThermalConstants.MinimumThermalMass)
            {
                SegmentThermalMass = ThermalConstants.MinimumThermalMass;
            }

            ThermalMass = SegmentThermalMass * segmentCount;
        }


        /// <summary>
        /// Recalculates flow properties based on pipe characteristics and grid size.
        /// Updates flow rate and thermal mass properties.
        /// </summary>
        internal void RefreshFlow()
        {
            if (segmentCount <= 0) return;

            // Calculate flow rate based on pipe diameter and grid cell size
            float diameter = 0.25f * ParcelLengthMetres;
            float area = diameter * diameter * 0.7853981634f;
            float volume = area * ParcelLengthMetres;
            float mass = volume * Properties.CoolantKilogramsPerCubicMetre;

            if (mass <= 0f) return;

            SegmentThermalMass = (mass * Properties.SpecificHeat) / HeatTimeScale;
            if (SegmentThermalMass < ThermalConstants.MinimumThermalMass)
            {
                SegmentThermalMass = ThermalConstants.MinimumThermalMass;
            }

            ThermalMass = SegmentThermalMass * segmentCount;
            FlowMass = mass;

            float flow = Properties.FlowRateFor(ParcelLengthMetres);
            FlowSegmentsPerSecond = flow / ParcelLengthMetres;
        }


        /// <summary>
        /// Current loop flow rate in liters per second.
        /// </summary>
        internal float FlowRate { get; private set; }


        /// <summary>
        /// Length of each pipe section in meters.
        /// Derived from the grid cell size during construction.
        /// </summary>
        public float ParcelLengthMetres { get; set; }


        /// <summary>
        /// Heat time scale factor in seconds.
        /// Used to scale thermal mass calculations for simulation stability.
        /// </summary>
        internal float HeatTimeScale
        {
            get { return Properties.SpecificHeat <= 0f ? 1f : Properties.SpecificHeat; }
        }


        /// <summary>
        /// Mass of coolant in each pipe section in kilograms.
        /// </summary>
        internal float FlowMass { get; private set; }


        /// <summary>
        /// Number of parcels that pass a point per second.
        /// Calculated from flow rate and parcel length.
        /// </summary>
        internal float FlowSegmentsPerSecond { get; private set; }


        /// <summary>
        /// True if the loop contains at least one pump.
        /// Pumps actively drive coolant flow rather than relying on natural convection.
        /// </summary>
        public bool HasPump { get; set; }


        /// <summary>
        /// Array of thermal power values for each segment in watts.
        /// Positive = heating, Negative = cooling.
        /// </summary>
        private float[] segmentWatts = new float[0];


        /// <summary>
        /// Clears all segment power values to zero.
        /// Called at the start of each simulation step.
        /// </summary>
        internal void ClearSegmentWatts()
        {
            if (segmentWatts.Length != segmentCount) segmentWatts = new float[segmentCount];
            else Array.Clear(segmentWatts, 0, segmentWatts.Length);
        }


        /// <summary>
        /// Applies a heat transfer to a specific segment.
        /// Updates the segment temperature based on energy input.
        /// </summary>
        /// <param name="parcel">Index of the segment to update.</param>
        /// <param name="watts">Heat power in watts (positive = heating).</param>
        /// <param name="h">Time step fraction.</param>
        /// <param name="effectiveMass">Thermal mass of the segment in J/K.</param>
        internal void ApplyParcelWatts(int parcel, float watts, float h, float effectiveMass)
        {
            if (segments == null || parcel < 0 || parcel >= segmentCount) return;

            // Temperature change = energy / mass
            // Energy = watts * h (power * time)
            float updated = segments[parcel] + (watts * h / effectiveMass);
            segments[parcel] = updated < ThermalConstants.MinimumTemperature
                ? ThermalConstants.MinimumTemperature
                : updated;
        }


        /// <summary>
        /// True if the loop uses well-mixed model (single temperature for all segments).
        /// When true, all segments have the same temperature and flow doesn't shift parcels.
        /// </summary>
        public bool WellMixed
        {
            get { return wellMixed; }
            set
            {
                if (wellMixed == value) return;

                wellMixed = value;
                RefreshThermalMass();
            }
        }

        private bool wellMixed;


        /// <summary>
        /// Advances the loop by simulating coolant flow over a time step.
        /// Moves parcels around the circular buffer to simulate flow direction
        /// and applies mixing to stabilize temperature gradients.
        /// </summary>
        /// <param name="h">Time step fraction (deltaSeconds / HeatTimeScale).</param>
        /// <remarks>
        /// The advection algorithm:
        /// 1. Calculate parcels moved = flowRate * h
        /// 2. For whole parcels: shift circular buffer position
        /// 3. For fractional movement: apply mean-reverting mixing
        /// 
        /// Example: If 2.5 parcels move, shift by 2 and mix with 0.5 fraction.
        /// </remarks>
        public void Advect(float h)
        {
            if (segmentCount < 2 || FlowSegmentsPerSecond == 0f) return;

            // Calculate how many parcels (and fractions) should move
            float parcels = FlowSegmentsPerSecond * h;
            if (parcels == 0f) return;

            // Accumulate fractional movement
            travelled += parcels;

            // Move whole parcels and update circular buffer shift
            int whole = (int)travelled;
            if (whole != 0)
            {
                travelled -= whole;
                shift = (shift + whole) % segmentCount;
                if (shift < 0) shift += segmentCount;
            }

            // Handle fractional advection with mean-reverting mixing
            float magnitude = parcels < 0f ? -parcels : parcels;
            if (magnitude > 1f) MixToward(1f - (1f / magnitude));
        }


        /// <summary>
        /// Applies mean-reverting mixing to temperature segments.
        /// Reduces temperature gradients by pulling each segment toward the mean.
        /// Used to stabilize the simulation when fractional parcels move.
        /// </summary>
        /// <param name="fraction">Mixing strength (0-1).
        /// 0 = no mixing, 1 = full mixing to mean temperature.</param>
        /// <remarks>
        /// The mixing operation:
        ///   segment[i] = segment[i] + fraction * (mean - segment[i])
        /// 
        /// This is a form of exponential smoothing that gradually reduces
        /// temperature differences while preserving total energy.
        /// </remarks>
        private void MixToward(float fraction)
        {
            if (segmentCount < 2) return;
            if (fraction <= 0f) return;
            if (fraction > 1f) fraction = 1f;

            // Calculate current mean temperature
            float total = 0f;
            for (int i = 0; i < segmentCount; i++) total += segments[i];

            float mean = total / segmentCount;
            for (int i = 0; i < segmentCount; i++)
            {
                // Move segment toward mean by fraction amount
                segments[i] += fraction * (mean - segments[i]);
            }
        }


        /// <summary>
        /// Calculates the mixing fraction needed for a given time step.
        /// Used to determine how much mean-reverting mixing to apply during advection.
        /// </summary>
        /// <param name="substepSeconds">Duration of the substep in seconds.</param>
        /// <returns>Mixing fraction (0-1). 0 if no mixing needed, increases with flow rate.</returns>
        /// <remarks>
        /// When parcels move more than one full segment per step (parcels > 1),
        /// some mixing is needed to maintain stability. The formula:
        ///   mixing = 1 - (1 / parcels)
        /// 
        /// Examples:
        ///   parcels = 1.0 -> mixing = 0.0 (no mixing needed)
        ///   parcels = 2.0 -> mixing = 0.5 (moderate mixing)
        ///   parcels = 10.0 -> mixing = 0.9 (strong mixing)
        /// </remarks>
        public float MixingFraction(float substepSeconds)
        {
            float parcels = FlowSegmentsPerSecond * substepSeconds;
            if (parcels < 0f) parcels = -parcels;
            if (parcels <= 1f) return 0f;
            return 1f - (1f / parcels);
        }


        /// <summary>
        /// Total heat absorbed in the last step in joules.
        /// Energy taken from cold sources in the loop.
        /// </summary>
        public float LastWattsAbsorbed;

        /// <summary>
        /// Total heat rejected in the last step in joules.
        /// Energy dumped to hot sinks in the loop.
        /// </summary>
        public float LastWattsRejected;

        /// <summary>
        /// Net heat transfer in the last step in watts.
        /// Absorbed - Rejected (positive = loop gaining heat).
        /// </summary>
        public float LastNetWatts
        {
            get { return LastWattsAbsorbed - LastWattsRejected; }
        }

        /// <summary>
        /// Accumulator for absorbed energy in joules (step积分).
        /// </summary>
        internal float AbsorbedEnergy;

        /// <summary>
        /// Accumulator for rejected energy in joules (step积分).
        /// </summary>
        internal float RejectedEnergy;


        /// <summary>
        /// Initializes energy accumulators at the start of a simulation step.
        /// </summary>
        internal void BeginStep()
        {
            AbsorbedEnergy = 0f;
            RejectedEnergy = 0f;
        }


        /// <summary>
        /// Finalizes energy calculations at the end of a simulation step.
        /// Converts accumulated energy to power rates by dividing by step duration.
        /// </summary>
        /// <param name="deltaSeconds">Duration of the step in seconds.</param>
        internal void EndStep(float deltaSeconds)
        {
            if (deltaSeconds <= 0f) return;

            LastWattsAbsorbed = AbsorbedEnergy / deltaSeconds;
            LastWattsRejected = RejectedEnergy / deltaSeconds;
        }
    }
}
