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

        public static int ContactFace(BlockInstance a, BlockInstance b)
        {
            if (a == null || b == null || a == b) return -1;
            return BoxGeometry.TouchingFace(a.Min, a.MaxExclusive, b.Min, b.MaxExclusive);
        }


        public static int CountContactFaces(BlockInstance a, BlockInstance b)
        {
            return CountContactFaces(a, b, ContactFace(a, b));
        }


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


        public static float Conductance(
            float latticeSize, BlockInstance a, BlockInstance b, int contactFaces, int axis)
        {
            if (contactFaces <= 0 || latticeSize <= 0f) return 0f;


            float ka = Conductivity(a);

            float kb = Conductivity(b);
            if (ka <= 0f || kb <= 0f) return 0f;

            float contactArea = contactFaces * latticeSize * latticeSize;


            float la = HalfDepth(a, axis, latticeSize);

            float lb = HalfDepth(b, axis, latticeSize);

            float resistance = (la / ka) + (lb / kb);
            if (resistance <= 0f) return 0f;

            return contactArea / resistance;
        }


        private static float Conductivity(BlockInstance block)
        {
            return block.Thermal.Conductivity * ThermalConstants.ConductionScale;
        }


        private static float HalfDepth(BlockInstance block, int axis, float latticeSize)
        {
            return BoxGeometry.Depth(block.Extents, axis, latticeSize) * 0.5f;
        }
    }
}
