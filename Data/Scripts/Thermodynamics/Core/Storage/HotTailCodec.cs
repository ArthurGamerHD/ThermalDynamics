using System;
using System.Collections.Generic;

namespace Thermodynamics.Core
{
    /// <summary>
    /// **The near-critical tail**: which blocks a server has to tell a client about, packed small
    /// enough to send often.
    ///
    /// <para>
    /// A client re-simulates from the same inputs and converges on the server without being told
    /// anything, because the model is dissipative — but it is on the wrong side of a block's
    /// critical temperature for 150 to 305 s while it does, against a damage event that runs a
    /// median 37 s. The readout is wrong about the one thing it is for, for longer than the thing
    /// lasts. See known-issues.md and [backlog.md](../../../../docs/backlog.md) `B4`.
    /// </para>
    ///
    /// <para>
    /// **A per-grid scalar cannot fix it**: at the join the worst block is 105 K out against a mean
    /// of 20 K, so one correction per grid fixes the armour and leaves the blocks that matter
    /// wrong. What has to travel is the tail itself.
    /// </para>
    ///
    /// <para>
    /// **The band is the one the mod already draws.** A block glows over the last
    /// <see cref="Incandescence.GlowBandKelvin"/> kelvin before its own critical temperature, and
    /// that is exactly the set a player is being warned about — so replicating that band makes
    /// every signal the mod raises the server's answer, and defining it twice would let the two
    /// drift apart silently (`P5`).
    /// </para>
    ///
    /// <para>
    /// **Ten bytes a block.** Eight for the position key, which is what the save format already
    /// uses and the only identity a client and a server agree on — node indices come from block
    /// insertion order and two machines do not build a grid in the same order — and two for the
    /// temperature, fixed point at a tenth of a kelvin. A tenth is far finer than any readout, and
    /// the range reaches 6,553.5 K, well past the 1,500 K where a run is called away.
    /// </para>
    ///
    /// <para>
    /// Free of any game type, so the selection rule is measured against the solver in
    /// `ClientDriftLab` rather than against its own arithmetic (`C5`, `E7`).
    /// </para>
    /// </summary>
    public static class HotTailCodec
    {
        /// <summary>
        /// Version marker. A reader that does not recognise it decodes nothing rather than
        /// guessing, because a misread packet writes wrong temperatures onto a live simulation.
        /// </summary>
        public const byte Version1Marker = 0xFC;

        /// <summary>Bytes per block: an eight byte position key and a two byte temperature.</summary>
        public const int RecordSize = 10;

        /// <summary>Marker plus the record count.</summary>
        public const int HeaderSize = 1 + 4;

        /// <summary>Kelvin per quantum of the packed temperature.</summary>
        public const float TemperatureStep = 0.1f;

        /// <summary>The hottest temperature the packing can carry, K.</summary>
        public const float MaxTemperature = 65535f * TemperatureStep;

        /// <summary>
        /// Fills <paramref name="results"/> with the blocks inside the warning band, hottest
        /// relative to their own critical temperature first, and returns how many were inside the
        /// band before the budget was applied.
        ///
        /// <para>
        /// **The count returned is the whole tail, not what fits.** A caller that reads
        /// <c>results.Count</c> as the tail length cannot tell a grid whose tail fits from one that
        /// was truncated, and a truncated packet is a client left wrong about the blocks that were
        /// cut (`P2`). Pass a budget of zero or less for no budget.
        /// </para>
        ///
        /// <para>
        /// **Ordered by margin against their own critical temperature rather than by kelvin**,
        /// because a 400 K block that fails at 420 K is in more trouble than a 900 K one that fails
        /// at 1,400 K, and the budget is spent on the blocks nearest failing.
        /// </para>
        /// </summary>
        public static int Select(IList<ThermalNode> nodes, float bandKelvin, int budget,
            List<StoredTemperature> results)
        {
            if (results == null) return 0;
            results.Clear();
            if (nodes == null) return 0;

            int inBand = 0;

            for (int i = 0; i < nodes.Count; i++)
            {
                ThermalNode node = nodes[i];
                float critical = node.Thermal.CriticalTemperature;
                if (critical <= 0f) continue;

                float floor = critical > bandKelvin ? critical - bandKelvin : 0f;
                if (node.Temperature < floor) continue;

                inBand++;
                results.Add(new StoredTemperature(node.Block.Position, node.Temperature));
            }

            if (budget > 0 && results.Count > budget)
            {
                // Sorted only when the budget bites, because sorting a tail that fits is work for
                // an order nothing reads.
                results.Sort(HottestFirst(nodes));
                results.RemoveRange(budget, results.Count - budget);
            }

            return inBand;
        }

        /// <summary>
        /// Orders two selected blocks by how far past their own critical temperature they are.
        ///
        /// The comparison needs each block's critical temperature and the selection carries only a
        /// position and a temperature, so it is resolved through the node list it came from. Built
        /// per call, and only on the path where the budget bit.
        /// </summary>
        private static Comparison<StoredTemperature> HottestFirst(IList<ThermalNode> nodes)
        {
            Dictionary<long, float> criticals = new Dictionary<long, float>(nodes.Count);
            for (int i = 0; i < nodes.Count; i++)
            {
                ThermalNode node = nodes[i];
                criticals[GridMath.Key(node.Block.Position)] = node.Thermal.CriticalTemperature;
            }

            return delegate (StoredTemperature a, StoredTemperature b)
            {
                return Margin(criticals, b).CompareTo(Margin(criticals, a));
            };
        }

        private static float Margin(Dictionary<long, float> criticals, StoredTemperature entry)
        {
            float critical;
            if (!criticals.TryGetValue(GridMath.Key(entry.Position), out critical) || critical <= 0f)
            {
                return float.NegativeInfinity;
            }

            return entry.Temperature - critical;
        }

        /// <summary>The bytes one selection occupies on the wire.</summary>
        public static int SizeOf(int blocks)
        {
            return HeaderSize + blocks * RecordSize;
        }

        /// <summary>Packs a selection. Never null; an empty tail is a header and no records.</summary>
        public static byte[] Encode(IList<StoredTemperature> tail)
        {
            int count = tail == null ? 0 : tail.Count;
            byte[] bytes = new byte[SizeOf(count)];

            int at = 0;
            bytes[at++] = Version1Marker;
            WriteInt32(bytes, ref at, count);

            for (int i = 0; i < count; i++)
            {
                WriteInt64(bytes, ref at, GridMath.Key(tail[i].Position));
                WriteUInt16(bytes, ref at, Quantise(tail[i].Temperature));
            }

            return bytes;
        }

        /// <summary>
        /// Unpacks a selection, or returns false and leaves <paramref name="results"/> empty.
        ///
        /// **A short or unrecognised packet decodes nothing rather than what it can.** These
        /// temperatures are written onto a running simulation, so half a packet applied is a hull
        /// with a few blocks moved to values that were never sent.
        /// </summary>
        public static bool TryDecode(byte[] data, List<StoredTemperature> results)
        {
            if (results == null) return false;
            results.Clear();

            if (data == null || data.Length < HeaderSize) return false;
            if (data[0] != Version1Marker) return false;

            int at = 1;
            int count = ReadInt32(data, ref at);
            if (count < 0 || data.Length < SizeOf(count)) return false;

            for (int i = 0; i < count; i++)
            {
                long key = ReadInt64(data, ref at);
                ushort packed = ReadUInt16(data, ref at);
                results.Add(new StoredTemperature(GridMath.FromKey(key), packed * TemperatureStep));
            }

            return true;
        }

        /// <summary>
        /// A temperature as a tenth-kelvin quantum, clamped to what two bytes hold.
        ///
        /// The clamp is at both ends: below zero cannot happen in kelvin and is checked anyway,
        /// and above <see cref="MaxTemperature"/> the block is thousands of kelvin past any
        /// critical temperature in the game, where the exact value stops carrying information.
        /// </summary>
        public static ushort Quantise(float kelvin)
        {
            if (kelvin <= 0f) return 0;
            if (kelvin >= MaxTemperature) return ushort.MaxValue;
            return (ushort)(kelvin / TemperatureStep + 0.5f);
        }

        private static void WriteInt32(byte[] bytes, ref int at, int value)
        {
            bytes[at++] = (byte)value;
            bytes[at++] = (byte)(value >> 8);
            bytes[at++] = (byte)(value >> 16);
            bytes[at++] = (byte)(value >> 24);
        }

        private static int ReadInt32(byte[] bytes, ref int at)
        {
            int value = bytes[at] | (bytes[at + 1] << 8) | (bytes[at + 2] << 16) | (bytes[at + 3] << 24);
            at += 4;
            return value;
        }

        private static void WriteInt64(byte[] bytes, ref int at, long value)
        {
            for (int i = 0; i < 8; i++) bytes[at++] = (byte)(value >> (i * 8));
        }

        private static long ReadInt64(byte[] bytes, ref int at)
        {
            long value = 0;
            for (int i = 0; i < 8; i++) value |= (long)bytes[at + i] << (i * 8);
            at += 8;
            return value;
        }

        private static void WriteUInt16(byte[] bytes, ref int at, ushort value)
        {
            bytes[at++] = (byte)value;
            bytes[at++] = (byte)(value >> 8);
        }

        private static ushort ReadUInt16(byte[] bytes, ref int at)
        {
            ushort value = (ushort)(bytes[at] | (bytes[at + 1] << 8));
            at += 2;
            return value;
        }
    }
}
