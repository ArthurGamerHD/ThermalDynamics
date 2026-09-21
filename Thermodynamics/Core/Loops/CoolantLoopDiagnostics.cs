using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    public enum CoolantFault
    {
        None = 0,

        OpenEnd = 1,

        BlockedByNonCoolant = 2,

        PortsDoNotMeet = 3,

        BranchOrCrossing = 4,

        DoubledBack = 5,

    }

    public struct CoolantFaultExample
    {
        public Vector3I Cell;
        public string Subtype;
        public CoolantFault Fault;

/// <summary>CoolantFaultExample operation.</summary>
        public CoolantFaultExample(Vector3I cell, string subtype, CoolantFault fault)
        {
            Cell = cell;
            Subtype = subtype;
            Fault = fault;
        }
    }

    public class CoolantLoopDiagnostics
    {
        public const int DefaultExampleLimit = 4;

        public readonly int[] Counts = new int[6];

/// <summary>List operation.</summary>
        public readonly List<CoolantFaultExample> Examples = new List<CoolantFaultExample>();

        public int PipesInLoops;

        public int PipesAdrift;

        public int Loops;

        private readonly int exampleLimit;

/// <summary>CoolantLoopDiagnostics operation.</summary>
        public CoolantLoopDiagnostics()
            : this(DefaultExampleLimit)
        {
        }

/// <summary>CoolantLoopDiagnostics operation.</summary>
        public CoolantLoopDiagnostics(int exampleLimit)
        {
            this.exampleLimit = exampleLimit > 0 ? exampleLimit : DefaultExampleLimit;
        }

/// <summary>CountOf operation.</summary>
        public int CountOf(CoolantFault fault)
        {
            int index = (int)fault;
            return index >= 0 && index < Counts.Length ? Counts[index] : 0;
        }

        public bool HasFaults
        {
            get { return PipesAdrift > 0; }
        }

/// <summary>Record operation.</summary>
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

/// <summary>Describe operation.</summary>
        public static string Describe(CoolantFault fault)
        {
            switch (fault)
            {
                case CoolantFault.OpenEnd: return "open end: a port faces empty space";
                case CoolantFault.BlockedByNonCoolant: return "a port faces a block with no plumbing";
                case CoolantFault.PortsDoNotMeet: return "pipes touch but their ports do not line up";
                case CoolantFault.BranchOrCrossing: return "a branch or crossing, not a ring";
                case CoolantFault.DoubledBack: return "the run doubles back on itself";
                default: return "none";
            }
        }
    }
}
