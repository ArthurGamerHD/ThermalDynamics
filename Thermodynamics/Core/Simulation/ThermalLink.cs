using System;
using VRageMath;

namespace Thermodynamics.Core
{
    public struct ThermalLink
    {
        public int NodeA;
        public int NodeB;

        public float Conductance;

        public int ContactFaces;

/// <summary>ThermalLink operation.</summary>
        public ThermalLink(int nodeA, int nodeB, float conductance, int contactFaces)
        {
            NodeA = nodeA;
            NodeB = nodeB;
            Conductance = conductance;
            ContactFaces = contactFaces;
        }
    }

    public static class ConductionBuilder
    {
/// <summary>ContactFace operation.</summary>
        public static int ContactFace(BlockInstance a, BlockInstance b)
        {
            if (a == null || b == null || a == b) return -1;
            return BoxGeometry.TouchingFace(a.Min, a.MaxExclusive, b.Min, b.MaxExclusive);
        }

/// <summary>CountContactFaces operation.</summary>
        public static int CountContactFaces(BlockInstance a, BlockInstance b)
        {
            return CountContactFaces(a, b, ContactFace(a, b));
        }

/// <summary>CountContactFaces operation.</summary>
        public static int CountContactFaces(BlockInstance a, BlockInstance b, int face)
        {
            if (face < 0) return 0;

            int shared = BoxGeometry.ContactCells(a.Min, a.MaxExclusive, b.Min, b.MaxExclusive, Face.Axis(face));
            if (shared <= 0) return 0;

            float coverage = a.MountFraction(face) * b.MountFraction(Face.Opposite(face));
            if (coverage <= 0f) return 0;

            int bolted = (int)Math.Round(shared * coverage);
            if (bolted < 1 && coverage > 0f) bolted = 1;
            return bolted;
        }

/// <summary>Conductance operation.</summary>
        public static float Conductance(
            float latticeSize, BlockInstance a, BlockInstance b, int contactFaces, int axis)
        {
            if (contactFaces <= 0 || latticeSize <= 0f) return 0f;

/// <summary>Conductivity operation.</summary>
            float ka = Conductivity(a);
/// <summary>Conductivity operation.</summary>
            float kb = Conductivity(b);
            if (ka <= 0f || kb <= 0f) return 0f;

            float contactArea = contactFaces * latticeSize * latticeSize;

/// <summary>HalfDepth operation.</summary>
            float la = HalfDepth(a, axis, latticeSize);
/// <summary>HalfDepth operation.</summary>
            float lb = HalfDepth(b, axis, latticeSize);

            float resistance = (la / ka) + (lb / kb);
            if (resistance <= 0f) return 0f;

            return contactArea / resistance;
        }

/// <summary>Conductivity operation.</summary>
        private static float Conductivity(BlockInstance block)
        {
            return block.Thermal.Conductivity * ThermalConstants.ConductionScale;
        }

/// <summary>HalfDepth operation.</summary>
        private static float HalfDepth(BlockInstance block, int axis, float latticeSize)
        {
            return BoxGeometry.Depth(block.Extents, axis, latticeSize) * 0.5f;
        }
    }
}
