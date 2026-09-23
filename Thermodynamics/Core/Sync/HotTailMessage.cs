using System.Collections.Generic;

namespace Thermodynamics.Core
{
    public enum HotTailKind
    {
        Unknown = 0,

        SnapshotRequest = 1,

        HullSnapshot = 2,

        Band = 3,
    }

    public static class HotTailMessage
    {
        public const byte Version1Marker = 0xFB;

        public const int HeaderSize = 1 + 1 + 8;

        public const int RecordsPerMessage = 2048;


        public static int SizeOf(int blocks)
        {
            return HeaderSize + HotTailCodec.SizeOf(blocks);
        }


        public static int Messages(int records)
        {
            return HotTailCodec.Packets(records, RecordsPerMessage);
        }


        public static byte[] EncodeSnapshotRequest(long gridId)
        {
            byte[] bytes = new byte[HeaderSize];
            WriteHeader(bytes, HotTailKind.SnapshotRequest, gridId);
            return bytes;
        }


        public static byte[] EncodeTemperatures(HotTailKind kind, long gridId,
            IList<StoredTemperature> tail, int offset, int count)
        {
            if (kind != HotTailKind.HullSnapshot && kind != HotTailKind.Band) return null;

            int length = tail == null ? 0 : tail.Count;
            if (offset < 0) offset = 0;
            if (offset > length) offset = length;
            if (count < 0) count = 0;
            if (count > length - offset) count = length - offset;

            byte[] bytes = new byte[SizeOf(count)];
            WriteHeader(bytes, kind, gridId);
            HotTailCodec.Write(bytes, HeaderSize, tail, offset, count);
            return bytes;
        }


        public static HotTailKind TryDecode(byte[] data, out long gridId,
            List<StoredTemperature> results)
        {
            gridId = 0;
            if (results != null) results.Clear();

            if (data == null || data.Length < HeaderSize) return HotTailKind.Unknown;
            if (data[0] != Version1Marker) return HotTailKind.Unknown;

            byte kind = data[1];

            gridId = ReadInt64(data, 2);

            if (kind == (byte)HotTailKind.SnapshotRequest)
            {
                return data.Length == HeaderSize ? HotTailKind.SnapshotRequest : HotTailKind.Unknown;
            }

            if (kind != (byte)HotTailKind.HullSnapshot && kind != (byte)HotTailKind.Band)
            {
                return HotTailKind.Unknown;
            }

            if (results == null) return HotTailKind.Unknown;

            return HotTailCodec.TryDecode(data, HeaderSize, results)
                ? (HotTailKind)kind
                : HotTailKind.Unknown;
        }


        private static void WriteHeader(byte[] bytes, HotTailKind kind, long gridId)
        {
            bytes[0] = Version1Marker;
            bytes[1] = (byte)kind;
            for (int i = 0; i < 8; i++) bytes[2 + i] = (byte)(gridId >> (i * 8));
        }


        private static long ReadInt64(byte[] bytes, int at)
        {
            long value = 0;
            for (int i = 0; i < 8; i++) value |= (long)bytes[at + i] << (i * 8);
            return value;
        }
    }
}
