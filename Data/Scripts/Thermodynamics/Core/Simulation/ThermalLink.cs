using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// A conduction path between two nodes, with a single symmetric conductance in W/K.
    ///
    /// One conductance shared by both ends is what makes the exchange energy-conserving: the watts
    /// leaving one node are exactly the watts entering the other. Deriving a coefficient per node
    /// from that node's own geometry would let the two halves disagree and create or destroy heat
    /// at every asymmetric joint.
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
    /// Builds conduction links between blocks.
    ///
    /// Every query here is answered from the two blocks' bounds and their per-face surface
    /// summaries, in constant time. Nothing walks a block's cells.
    ///
    /// <para>
    /// Counting mounted cell faces directly would iterate every cell of block A and, for each of its
    /// six faces, search every cell of block B for a matching mount: O(cellsA * 6 * cellsB). That is
    /// tolerable for one- and two-cell blocks, but on a lattice fine enough for Space Engineers 2's
    /// smallest blocks a five-metre block occupies around eight thousand cells, and one such pair
    /// would cost hundreds of millions of operations per topology rebuild.
    /// </para>
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
        /// Total shared area between two blocks, in lattice cell faces, whether bolted or not.
        /// </summary>
        public static int SharedContactCells(BlockInstance a, BlockInstance b)
        {
            int face = ContactFace(a, b);
            if (face < 0) return 0;
            return BoxGeometry.ContactCells(a.Min, a.MaxExclusive, b.Min, b.MaxExclusive, Face.Axis(face));
        }

        /// <summary>
        /// Shared area where both blocks carry a mount surface across the joint. This is the area
        /// heat conducts through.
        /// </summary>
        /// <remarks>
        /// Mount coverage is tracked per face as a fraction, so where a face is uniformly
        /// mounted or uniformly bare the answer is exact. Where both sides are only partly
        /// mounted the fractions are combined as independent, which is the neutral assumption
        /// when the model no longer records which individual cells carry the mount.
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
        /// The axis two blocks abut on, for picking the conduction depth. Defaults to Z when
        /// they do not touch.
        /// </summary>
        public static int ContactAxis(BlockInstance a, BlockInstance b)
        {
            int face = ContactFace(a, b);
            return face < 0 ? 2 : Face.Axis(face);
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
        /// Both terms scale with the blocks' real dimensions, so a joint between a small block and a
        /// large one is described correctly: the contact area is the overlap of the two faces, and
        /// the larger block's greater depth makes it the slower conductor. This is what allows one
        /// grid to mix block sizes.
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
