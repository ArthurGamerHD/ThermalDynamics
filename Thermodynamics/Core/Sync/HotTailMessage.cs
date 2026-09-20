using System.Collections.Generic;

namespace Thermodynamics.Core
{
    /// <summary>What one message on the temperature channel is.</summary>
    public enum HotTailKind
    {
        /// <summary>Not a message this build understands.</summary>
        Unknown = 0,

        /// <summary>A client asking the server to state a grid's whole hull once.</summary>
        SnapshotRequest = 1,

        /// <summary>The server stating the whole hull, or one slice of it, once.</summary>
        HullSnapshot = 2,

        /// <summary>
        /// The server stating the near-critical band.
        ///
        /// **The same records as a snapshot, distinguished because the client has to know.** A
        /// client waiting for its hull cannot tell one from the other by content — a band update on
        /// a hull whose band is the whole hull looks identical — and a client that took a band
        /// update for its snapshot would stop asking and keep the stale hull that the band alone is
        /// measured not to fix.
        /// </summary>
        Band = 3,
    }

    /// <summary>
    /// The envelope <see cref="HotTailCodec"/> travels in: which grid the temperatures are about,
    /// and which of the two things this message is.
    ///
    /// <para>
    /// **The grid is named on the wire because nothing else names it.** The packet is keyed by
    /// block position, which is only an identity inside a grid — the same position exists on every
    /// grid in the world — so a packet without a grid id is a packet that could be applied to the
    /// wrong hull.
    /// </para>
    ///
    /// <para>
    /// **Separate from the codec rather than folded into its header**, because the codec's format
    /// is what known-issues.md quotes bandwidth figures
    /// against. Ten bytes of envelope per packet is a constant that can be stated; a changed record
    /// format would make every measured figure in that table quietly wrong (`P2`).
    /// </para>
    ///
    /// <para>
    /// **A slice is a whole message.** A hull larger than one packet is sent as several messages,
    /// each complete and each applied on arrival, so there is no sequence to track, nothing to
    /// reassemble, and a lost message costs its own records rather than the hull.
    /// </para>
    /// </summary>
    public static class HotTailMessage
    {
        /// <summary>
        /// Version marker, distinct from the codec's own. A reader that does not recognise it
        /// decodes nothing: these temperatures are written onto a running simulation.
        /// </summary>
        public const byte Version1Marker = 0xFB;

        /// <summary>Marker, kind, and the grid's entity id.</summary>
        public const int HeaderSize = 1 + 1 + 8;

        /// <summary>
        /// Records one message may carry.
        ///
        /// **A packet-size bound, not a rate limit**: a hull with more blocks than this sends every
        /// one of them, in as many messages as it takes, in the same frame. What the bound is for
        /// is that a single message the transport refuses is a correction that never arrives, and
        /// the largest hulls this mod is built for have a million blocks. At this count a message
        /// is under 21 KB.
        /// </summary>
        public const int RecordsPerMessage = 2048;

        /// <summary>The bytes one message carrying this many records occupies.</summary>
        public static int SizeOf(int blocks)
        {
            return HeaderSize + HotTailCodec.SizeOf(blocks);
        }

        /// <summary>How many messages a selection of this size takes.</summary>
        public static int Messages(int records)
        {
            return HotTailCodec.Packets(records, RecordsPerMessage);
        }

        /// <summary>A client asking for one grid's whole hull. Header only; there is nothing to say.</summary>
        public static byte[] EncodeSnapshotRequest(long gridId)
        {
            byte[] bytes = new byte[HeaderSize];
            WriteHeader(bytes, HotTailKind.SnapshotRequest, gridId);
            return bytes;
        }

        /// <summary>
        /// One slice of one grid's temperatures, ready for the wire. The kind is
        /// <see cref="HotTailKind.HullSnapshot"/> or <see cref="HotTailKind.Band"/>; anything else
        /// is refused rather than sent as itself, because a reader would apply it.
        /// </summary>
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

        /// <summary>
        /// Reads a message. Returns <see cref="HotTailKind.Unknown"/> — and leaves
        /// <paramref name="results"/> empty — for anything this build does not recognise, anything
        /// short, and anything whose record count does not match the bytes that arrived.
        /// </summary>
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
                // Exactly the header: a request carrying a payload is a message some other build
                // wrote, and guessing at it is how a reader applies something it did not read.
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
