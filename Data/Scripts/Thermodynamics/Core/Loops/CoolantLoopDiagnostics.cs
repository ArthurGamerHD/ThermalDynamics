using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>Why a run of coolant pipe did not become a loop.</summary>
    public enum CoolantFault
    {
        /// <summary>The block is part of a working loop.</summary>
        None = 0,

        /// <summary>A port faces a cell with nothing in it. The run has a free end.</summary>
        OpenEnd = 1,

        /// <summary>A port faces a block that carries no coolant plumbing at all.</summary>
        BlockedByNonCoolant = 2,

        /// <summary>
        /// The next block along has plumbing but no port facing back. Two pipes touching at the
        /// wrong rotation look connected and are not, which is the hardest fault to see by eye.
        /// </summary>
        PortsDoNotMeet = 3,

        /// <summary>The run closes on itself but contains no pump, so nothing circulates.</summary>
        NoPump = 4,

        /// <summary>
        /// The walk arrived at a block it had already left. A tee or a crossing, rather than a ring.
        /// </summary>
        BranchOrCrossing = 5,

        /// <summary>The run returned to its start through the port it left by, which is not a ring.</summary>
        DoubledBack = 6,
    }

    /// <summary>One example of a fault, for a readout to name a cell the player can walk to.</summary>
    public struct CoolantFaultExample
    {
        public Vector3I Cell;
        public string Subtype;
        public CoolantFault Fault;

        public CoolantFaultExample(Vector3I cell, string subtype, CoolantFault fault)
        {
            Cell = cell;
            Subtype = subtype;
            Fault = fault;
        }
    }

    /// <summary>
    /// Why the plumbing on a grid did or did not form loops.
    ///
    /// Built only when a caller asks for it. "I built a ring and nothing happened" is the question
    /// this answers, and it cannot be answered from the loop list, because the whole symptom is
    /// that the loop is not in it. A field dump showed six copies of one ship where four had a loop
    /// and two did not, with no way to tell what differed.
    /// </summary>
    public class CoolantLoopDiagnostics
    {
        /// <summary>Most examples kept per fault, so a grid of broken plumbing cannot flood a readout.</summary>
        public const int DefaultExampleLimit = 4;

        public readonly int[] Counts = new int[7];

        public readonly List<CoolantFaultExample> Examples = new List<CoolantFaultExample>();

        /// <summary>Coolant blocks claimed by a working loop.</summary>
        public int PipesInLoops;

        /// <summary>Coolant blocks that are part of no loop.</summary>
        public int PipesAdrift;

        /// <summary>Loops that formed.</summary>
        public int Loops;

        private readonly int exampleLimit;

        public CoolantLoopDiagnostics()
            : this(DefaultExampleLimit)
        {
        }

        public CoolantLoopDiagnostics(int exampleLimit)
        {
            this.exampleLimit = exampleLimit > 0 ? exampleLimit : DefaultExampleLimit;
        }

        public int CountOf(CoolantFault fault)
        {
            int index = (int)fault;
            return index >= 0 && index < Counts.Length ? Counts[index] : 0;
        }

        /// <summary>True when there is plumbing on the grid that is doing nothing.</summary>
        public bool HasFaults
        {
            get { return PipesAdrift > 0; }
        }

        internal void Record(BlockInstance block, CoolantFault fault)
        {
            int index = (int)fault;
            if (index > 0 && index < Counts.Length) Counts[index]++;

            PipesAdrift++;

            int kept = 0;
            for (int i = 0; i < Examples.Count; i++)
            {
                if (Examples[i].Fault == fault) kept++;
            }
            if (kept >= exampleLimit) return;

            Examples.Add(new CoolantFaultExample(
                block.Min, block.Model == null ? "?" : block.Model.Name, fault));
        }

        /// <summary>A short human-readable reason, for a terminal or a report line.</summary>
        public static string Describe(CoolantFault fault)
        {
            switch (fault)
            {
                case CoolantFault.OpenEnd: return "open end: a port faces empty space";
                case CoolantFault.BlockedByNonCoolant: return "a port faces a block with no plumbing";
                case CoolantFault.PortsDoNotMeet: return "pipes touch but their ports do not line up";
                case CoolantFault.NoPump: return "closed ring with no pump";
                case CoolantFault.BranchOrCrossing: return "a branch or crossing, not a ring";
                case CoolantFault.DoubledBack: return "the run doubles back on itself";
                default: return "none";
            }
        }
    }
}
