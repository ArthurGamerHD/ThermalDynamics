using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// A conduction path between two nodes, with a single symmetric conductance in W/K — one figure
    /// shared by both ends, which is what makes the exchange energy-conserving.
    /// See thermal-model.md, The three invariants.
    /// </summary>
    public struct ThermalLink
    {
        public int NodeA;
        public int NodeB;

        /// <summary>Conductance, W/K. Always positive.</summary>
        public float Conductance;

        /// <summary>Mounted contact area in lattice cell faces. Diagnostic only.</summary>
        public int ContactFaces;

        public ThermalLink(int nodeA, int nodeB, float conductance, int contactFaces)
        {
            NodeA = nodeA;
            NodeB = nodeB;
            Conductance = conductance;
            ContactFaces = contactFaces;
        }
    }

    /// <summary>
    /// Builds conduction links between blocks. Every query is answered from the two blocks' bounds and
    /// their per-face surface summaries in constant time; nothing walks a block's cells, which on an
    /// SE2 lattice would be eight thousand of them per five-metre block.
    /// See scale-design.md, Cell-centric to boundary-centric.
    /// </summary>
    public static class ConductionBuilder
    {
        /// <summary>
        /// The face of <paramref name="a"/> that touches <paramref name="b"/>, or -1 when the
        /// two blocks do not share any area.
        /// </summary>
        public static int ContactFace(BlockInstance a, BlockInstance b)
        {
            if (a == null || b == null || a == b) return -1;
            return BoxGeometry.TouchingFace(a.Min, a.MaxExclusive, b.Min, b.MaxExclusive);
        }

        /// <summary>
        /// Shared area where both blocks carry a mount surface across the joint. This is the area
        /// heat conducts through.
        /// </summary>
        /// <remarks>
        /// Exact where a face is uniformly mounted or uniformly bare. Where both sides are partly
        /// mounted the fractions combine as independent, since the model records coverage per face
        /// rather than per cell. See known-issues.md, Deliberate limits.
        /// </remarks>
        public static int CountContactFaces(BlockInstance a, BlockInstance b)
        {
            int face = ContactFace(a, b);
            if (face < 0) return 0;

            int shared = BoxGeometry.ContactCells(a.Min, a.MaxExclusive, b.Min, b.MaxExclusive, Face.Axis(face));
            if (shared <= 0) return 0;

            float coverage = a.MountFraction(face) * b.MountFraction(Face.Opposite(face));
            if (coverage <= 0f) return 0;

            int bolted = (int)Math.Round(shared * coverage);
            if (bolted < 1 && coverage > 0f) bolted = 1;
            return bolted;
        }

        /// <summary>
        /// Conductance in W/K for a contact between two blocks.
        ///
        /// Two conductors in series: heat travels from the centre of block A to the interface,
        /// then from the interface to the centre of block B.
        /// <code>
        ///   G = A_contact / (L_a / k_a + L_b / k_b)
        /// </code>
        /// with L the half-depth of each block along the contact axis and k its conductivity in
        /// W/(m K). Result is symmetric by construction.
        ///
        /// <para>
        /// Both terms scale with the blocks' real dimensions, which is what allows one grid to mix
        /// block sizes: the contact area is the overlap of the two faces, and the larger block's
        /// greater depth makes it the slower conductor.
        /// </para>
        /// </summary>
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
