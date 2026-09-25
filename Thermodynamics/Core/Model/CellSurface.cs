using System;
using System.Text;

namespace Thermodynamics.Core
{
    public static class CellSurface
    {
        public const int SelfAirtightShift = 0;
        public const int NeighbourAirtightShift = 6;
        public const int SelfMountShift = 12;
        public const int NeighbourMountShift = 18;

        public const int SelfAirtightMask = 0x3F;
        public const int SelfMountMask = 0x3F << SelfMountShift;


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


        public static int SelfOnly(int state)
        {
            return state & (SelfAirtightMask | SelfMountMask);
        }


        public static bool IsFullySealed(int state)
        {
            return (state & SelfAirtightMask) == SelfAirtightMask;
        }


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
