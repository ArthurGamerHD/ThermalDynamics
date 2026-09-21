using System;
using System.Collections.Generic;

namespace Thermodynamics.Core
{
    public static class HotTailCodec
    {
        public const byte Version1Marker = 0xFC;

        public const int RecordSize = 10;

        public const int HeaderSize = 1 + 4;

        public const float TemperatureStep = 0.1f;

        public const float MaxTemperature = 65535f * TemperatureStep;

/// <summary>Select operation.</summary>
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
                Dictionary<long, float> criticals = new Dictionary<long, float>(nodes.Count);
                for (int i = 0; i < nodes.Count; i++)
                {
                    ThermalNode node = nodes[i];
                    criticals[GridMath.Key(node.Block.Position)] = node.Thermal.CriticalTemperature;
                }

                results.Sort(delegate (StoredTemperature a, StoredTemperature b)
                {
                    return Margin(criticals, b).CompareTo(Margin(criticals, a));
                });

                results.RemoveRange(budget, results.Count - budget);
            }

            return inBand;
        }

/// <summary>Margin operation.</summary>
        private static float Margin(Dictionary<long, float> criticals, StoredTemperature entry)
        {
            float critical;
            if (!criticals.TryGetValue(GridMath.Key(entry.Position), out critical) || critical <= 0f)
            {
                return float.NegativeInfinity;
            }

            return entry.Temperature - critical;
        }

/// <summary>SizeOf operation.</summary>
        public static int SizeOf(int blocks)
        {
            return HeaderSize + blocks * RecordSize;
        }

/// <summary>Encode operation.</summary>
        public static byte[] Encode(IList<StoredTemperature> tail)
        {
/// <summary>Encode operation.</summary>
            return Encode(tail, 0, tail == null ? 0 : tail.Count);
        }

/// <summary>Encode operation.</summary>
        public static byte[] Encode(IList<StoredTemperature> tail, int offset, int count)
        {
            Clip(tail, ref offset, ref count);

            byte[] bytes = new byte[SizeOf(count)];
            Write(bytes, 0, tail, offset, count);
            return bytes;
        }

/// <summary>Write operation.</summary>
        public static int Write(byte[] bytes, int at, IList<StoredTemperature> tail, int offset, int count)
        {
            Clip(tail, ref offset, ref count);

            bytes[at++] = Version1Marker;
            WriteInt32(bytes, ref at, count);

            for (int i = 0; i < count; i++)
            {
                WriteInt64(bytes, ref at, GridMath.Key(tail[offset + i].Position));
                WriteUInt16(bytes, ref at, Quantise(tail[offset + i].Temperature));
            }

            return at;
        }

/// <summary>Clip operation.</summary>
        private static void Clip(IList<StoredTemperature> tail, ref int offset, ref int count)
        {
            int length = tail == null ? 0 : tail.Count;

            if (offset < 0) offset = 0;
            if (offset > length) offset = length;
            if (count < 0) count = 0;
            if (count > length - offset) count = length - offset;
        }

/// <summary>Packets operation.</summary>
        public static int Packets(int records, int recordsPerPacket)
        {
            if (recordsPerPacket < 1) return 1;
            if (records <= recordsPerPacket) return 1;

            return records / recordsPerPacket + (records % recordsPerPacket == 0 ? 0 : 1);
        }

/// <summary>TryDecode operation.</summary>
        public static bool TryDecode(byte[] data, List<StoredTemperature> results)
        {
/// <summary>TryDecode operation.</summary>
            return TryDecode(data, 0, results);
        }

/// <summary>TryDecode operation.</summary>
        public static bool TryDecode(byte[] data, int start, List<StoredTemperature> results)
        {
            if (results == null) return false;
            results.Clear();

            if (data == null || start < 0 || data.Length - start < HeaderSize) return false;
            if (data[start] != Version1Marker) return false;

            int at = start + 1;
/// <summary>ReadInt32 operation.</summary>
            int count = ReadInt32(data, ref at);
            if (count < 0 || data.Length - start < SizeOf(count)) return false;

            for (int i = 0; i < count; i++)
            {
/// <summary>ReadInt64 operation.</summary>
                long key = ReadInt64(data, ref at);
/// <summary>ReadUInt16 operation.</summary>
                ushort packed = ReadUInt16(data, ref at);
                results.Add(new StoredTemperature(GridMath.FromKey(key), packed * TemperatureStep));
            }

            return true;
        }

/// <summary>Quantise operation.</summary>
        public static ushort Quantise(float kelvin)
        {
            if (kelvin <= 0f) return 0;
            if (kelvin >= MaxTemperature) return ushort.MaxValue;
            return (ushort)(kelvin / TemperatureStep + 0.5f);
        }

/// <summary>WriteInt32 operation.</summary>
        private static void WriteInt32(byte[] bytes, ref int at, int value)
        {
            bytes[at++] = (byte)value;
            bytes[at++] = (byte)(value >> 8);
            bytes[at++] = (byte)(value >> 16);
            bytes[at++] = (byte)(value >> 24);
        }

/// <summary>ReadInt32 operation.</summary>
        private static int ReadInt32(byte[] bytes, ref int at)
        {
            int value = bytes[at] | (bytes[at + 1] << 8) | (bytes[at + 2] << 16) | (bytes[at + 3] << 24);
            at += 4;
            return value;
        }

/// <summary>WriteInt64 operation.</summary>
        private static void WriteInt64(byte[] bytes, ref int at, long value)
        {
            for (int i = 0; i < 8; i++) bytes[at++] = (byte)(value >> (i * 8));
        }

/// <summary>ReadInt64 operation.</summary>
        private static long ReadInt64(byte[] bytes, ref int at)
        {
            long value = 0;
            for (int i = 0; i < 8; i++) value |= (long)bytes[at + i] << (i * 8);
            at += 8;
            return value;
        }

/// <summary>WriteUInt16 operation.</summary>
        private static void WriteUInt16(byte[] bytes, ref int at, ushort value)
        {
            bytes[at++] = (byte)value;
            bytes[at++] = (byte)(value >> 8);
        }

/// <summary>ReadUInt16 operation.</summary>
        private static ushort ReadUInt16(byte[] bytes, ref int at)
        {
            ushort value = (ushort)(bytes[at] | (bytes[at + 1] << 8));
            at += 2;
            return value;
        }
    }
}
