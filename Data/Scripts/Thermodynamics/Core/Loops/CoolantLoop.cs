using System;
using System.Collections.Generic;

namespace Thermodynamics.Core
{
    /// <summary>A conduction path between a coolant loop and one block node.</summary>
    public struct LoopLink
    {
        public int NodeIndex;
        public float Conductance;

        public LoopLink(int nodeIndex, float conductance)
        {
            NodeIndex = nodeIndex;
            Conductance = conductance;
        }
    }

    /// <summary>
    /// A closed run of coolant pipes with at least one pump, treated as one well-mixed fluid
    /// mass. The fluid exchanges heat with the pipe blocks it runs through and with whatever is
    /// bolted to the sink faces.
    /// </summary>
    public class CoolantLoop
    {
        /// <summary>Pipe blocks forming the ring, in crawl order.</summary>
        public readonly List<BlockInstance> Pipes = new List<BlockInstance>();

        /// <summary>Conduction paths to block nodes.</summary>
        public readonly List<LoopLink> Links = new List<LoopLink>();

        public LoopThermalProperties Properties;

        /// <summary>Coolant temperature, K.</summary>
        public float Temperature;

        /// <summary>Heat capacity of the fluid, J/K.</summary>
        public float ThermalMass { get; private set; }

        /// <summary>True when at least one pipe in the ring is a pump.</summary>
        public bool HasPump;

        /// <summary>
        /// Stable identity across saves: an order-independent hash of every pipe position in the
        /// ring. A reload cannot swap two loops' temperatures the way an index-based key can,
        /// and unlike "smallest key in the ring" it cannot collide with the empty-loop value
        /// just because a ring happens to include the grid origin.
        /// </summary>
        public long Signature { get; private set; }

        public CoolantLoop(LoopThermalProperties properties, float initialTemperature)
        {
            Properties = (properties ?? LoopThermalProperties.Default()).Clone().Clamp();
            Temperature = initialTemperature;
            RefreshThermalMass();
        }

        public int PipeCount
        {
            get { return Pipes.Count; }
        }

        public void RefreshThermalMass()
        {
            ThermalMass = Math.Max(ThermalConstants.MinimumThermalMass, Properties.SpecificHeat * Properties.Mass);
        }

        /// <summary>Recomputes <see cref="Signature"/> from the current pipe set.</summary>
        public void RefreshSignature()
        {
            if (Pipes.Count == 0)
            {
                Signature = 0L;
                return;
            }

            unchecked
            {
                long combinedXor = 0L;
                long combinedSum = 0L;

                for (int i = 0; i < Pipes.Count; i++)
                {
                    long mixed = Mix(Pipes[i].Key);
                    combinedXor ^= mixed;
                    combinedSum += mixed;
                }

                long signature = (combinedXor * 31L) + combinedSum + Pipes.Count;
                Signature = signature == 0L ? 1L : signature;
            }
        }

        /// <summary>A 64-bit finaliser, so nearby positions do not produce nearby hashes.</summary>
        private static long Mix(long value)
        {
            unchecked
            {
                ulong x = (ulong)value + 0x9E3779B97F4A7C15UL;
                x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
                x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
                x = x ^ (x >> 31);
                return (long)x;
            }
        }

        public bool Contains(BlockInstance block)
        {
            for (int i = 0; i < Pipes.Count; i++)
            {
                if (Pipes[i] == block) return true;
            }
            return false;
        }

        public float Energy
        {
            get { return Temperature * ThermalMass; }
        }
    }
}
