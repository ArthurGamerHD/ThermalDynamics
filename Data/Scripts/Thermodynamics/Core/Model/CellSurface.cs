using System;
using System.Text;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Bit layout for one grid cell's surface state. 24 bits, four groups of six, each group
    /// indexed by <see cref="Face"/>.
    ///
    /// <code>
    /// bits  0-5   self airtight       this cell's face seals
    /// bits  6-11  neighbour airtight  the adjacent cell's facing side seals
    /// bits 12-17  self mount          this cell's face carries a mount surface
    /// bits 18-23  neighbour mount     the adjacent cell's facing side carries one
    /// </code>
    ///
    /// The "self" half is supplied by the block. The "neighbour" half is derived by
    /// <see cref="SurfaceMap"/> and is always the mirror of a neighbouring cell's self bits.
    /// </summary>
    public static class CellSurface
    {
        public const int SelfAirtightShift = 0;
        public const int NeighbourAirtightShift = 6;
        public const int SelfMountShift = 12;
        public const int NeighbourMountShift = 18;

        public const int SelfAirtightMask = 0x3F;
        public const int NeighbourAirtightMask = 0x3F << NeighbourAirtightShift;
        public const int SelfMountMask = 0x3F << SelfMountShift;
        public const int NeighbourMountMask = 0x3F << NeighbourMountShift;

        /// <summary>All six self faces sealed: a fully airtight cell.</summary>
        public const int FullySealed = SelfAirtightMask;

        public static bool SelfAirtight(int state, int face)
        {
            return (state & (1 << (SelfAirtightShift + face))) != 0;
        }

        public static bool NeighbourAirtight(int state, int face)
        {
            return (state & (1 << (NeighbourAirtightShift + face))) != 0;
        }

        public static bool SelfMount(int state, int face)
        {
            return (state & (1 << (SelfMountShift + face))) != 0;
        }

        public static bool NeighbourMount(int state, int face)
        {
            return (state & (1 << (NeighbourMountShift + face))) != 0;
        }

        public static int WithSelfAirtight(int state, int face, bool value)
        {
            return Set(state, SelfAirtightShift + face, value);
        }

        public static int WithSelfMount(int state, int face, bool value)
        {
            return Set(state, SelfMountShift + face, value);
        }

        public static int WithNeighbourAirtight(int state, int face, bool value)
        {
            return Set(state, NeighbourAirtightShift + face, value);
        }

        public static int WithNeighbourMount(int state, int face, bool value)
        {
            return Set(state, NeighbourMountShift + face, value);
        }

        /// <summary>Strips the derived neighbour half, keeping only what the block itself declares.</summary>
        public static int SelfOnly(int state)
        {
            return state & (SelfAirtightMask | SelfMountMask);
        }

        /// <summary>True when every self face is sealed.</summary>
        public static bool IsFullySealed(int state)
        {
            return (state & SelfAirtightMask) == SelfAirtightMask;
        }

        /// <summary>
        /// Given a neighbour's state, the neighbour bits it contributes to this cell across
        /// <paramref name="face"/>. The neighbour's own face is the opposite one.
        /// </summary>
        public static int NeighbourContribution(int neighbourState, int face)
        {
            int opposite = Face.Opposite(face);
            int contribution = 0;
            if (SelfAirtight(neighbourState, opposite))
            {
                contribution |= 1 << (NeighbourAirtightShift + face);
            }
            if (SelfMount(neighbourState, opposite))
            {
                contribution |= 1 << (NeighbourMountShift + face);
            }
            return contribution;
        }

        private static int Set(int state, int bit, bool value)
        {
            if (value) return state | (1 << bit);
            return state & ~(1 << bit);
        }

        public static string Describe(int state)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("selfAir[");
            AppendGroup(sb, state, SelfAirtightShift);
            sb.Append("] nbrAir[");
            AppendGroup(sb, state, NeighbourAirtightShift);
            sb.Append("] selfMount[");
            AppendGroup(sb, state, SelfMountShift);
            sb.Append("] nbrMount[");
            AppendGroup(sb, state, NeighbourMountShift);
            sb.Append("]");
            return sb.ToString();
        }

        private static void AppendGroup(StringBuilder sb, int state, int shift)
        {
            for (int i = 0; i < Face.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(((state & (1 << (shift + i))) != 0) ? Face.Name(i).Substring(0, 1) : "-");
            }
        }
    }
}
