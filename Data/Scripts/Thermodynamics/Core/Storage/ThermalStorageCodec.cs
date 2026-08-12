using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>One saved block temperature.</summary>
    public struct StoredTemperature
    {
        public Vector3I Position;
        public float Temperature;

        public StoredTemperature(Vector3I position, float temperature)
        {
            Position = position;
            Temperature = temperature;
        }
    }

    /// <summary>One saved loop temperature, keyed by the loop's stable signature.</summary>
    public struct StoredLoop
    {
        public long Signature;
        public float Temperature;

        public StoredLoop(long signature, float temperature)
        {
            Signature = signature;
            Temperature = temperature;
        }
    }

    /// <summary>
    /// Serialises grid temperatures to and from a base64 blob.
    ///
    /// Two formats are supported. Version 1 is the original layout, kept so existing saves load;
    /// version 2 fixes its three defects: positions are 64-bit so distant blocks cannot alias,
    /// temperatures keep their fractional part, and loops are keyed by a stable signature rather
    /// than by their index in a list that is rebuilt on load.
    /// </summary>
    public static class ThermalStorageCodec
    {
        private const byte Version2Marker = 0xFD;
        private const byte SectionBlocks = 1;
        private const byte SectionLoops = 2;

        private const int LegacyRecordSize = 6;
        private const int LegacyLoopRecordSize = 3;

        // ---- version 2 ---------------------------------------------------------------------

        /// <summary>Encodes block and loop temperatures in the current format.</summary>
        public static string Encode(IList<StoredTemperature> blocks, IList<StoredLoop> loops)
        {
            int blockCount = blocks == null ? 0 : blocks.Count;
            int loopCount = loops == null ? 0 : loops.Count;

            byte[] bytes = new byte[2 + 4 + (blockCount * 12) + 1 + 4 + (loopCount * 12)];
            int at = 0;

            bytes[at++] = Version2Marker;
            bytes[at++] = SectionBlocks;
            WriteInt32(bytes, ref at, blockCount);

            for (int i = 0; i < blockCount; i++)
            {
                StoredTemperature entry = blocks[i];
                WriteInt64(bytes, ref at, GridMath.Key(entry.Position));
                WriteSingle(bytes, ref at, entry.Temperature);
            }

            bytes[at++] = SectionLoops;
            WriteInt32(bytes, ref at, loopCount);

            for (int i = 0; i < loopCount; i++)
            {
                StoredLoop entry = loops[i];
                WriteInt64(bytes, ref at, entry.Signature);
                WriteSingle(bytes, ref at, entry.Temperature);
            }

            return Convert.ToBase64String(bytes);
        }

        /// <summary>
        /// Decodes either format. Returns false when the payload is unreadable, leaving the
        /// output lists empty rather than throwing into the host's load path.
        /// </summary>
        public static bool TryDecode(string data, List<StoredTemperature> blocks, List<StoredLoop> loops)
        {
            if (blocks != null) blocks.Clear();
            if (loops != null) loops.Clear();

            if (string.IsNullOrEmpty(data)) return false;

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(data);
            }
            catch (FormatException)
            {
                return false;
            }

            if (bytes.Length == 0) return false;

            if (bytes[0] == Version2Marker)
            {
                return TryDecodeVersion2(bytes, blocks, loops);
            }

            return TryDecodeLegacyBlocks(bytes, blocks);
        }

        private static bool TryDecodeVersion2(byte[] bytes, List<StoredTemperature> blocks, List<StoredLoop> loops)
        {
            try
            {
                int at = 1;
                while (at < bytes.Length)
                {
                    byte section = bytes[at++];
                    int count = ReadInt32(bytes, ref at);
                    if (count < 0) return false;

                    for (int i = 0; i < count; i++)
                    {
                        long key = ReadInt64(bytes, ref at);
                        float temperature = ReadSingle(bytes, ref at);

                        if (section == SectionBlocks)
                        {
                            if (blocks != null) blocks.Add(new StoredTemperature(GridMath.FromKey(key), temperature));
                        }
                        else if (section == SectionLoops)
                        {
                            if (loops != null) loops.Add(new StoredLoop(key, temperature));
                        }
                    }
                }
                return true;
            }
            catch (IndexOutOfRangeException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        // ---- version 1 (read only) ----------------------------------------------------------

        /// <summary>
        /// Encodes in the original format. Kept so the change can be rolled back, and so tests
        /// can prove the reader handles real historical data.
        /// </summary>
        public static string EncodeLegacyBlocks(IList<StoredTemperature> blocks)
        {
            int count = blocks == null ? 0 : blocks.Count;
            byte[] bytes = new byte[count * LegacyRecordSize];

            int at = 0;
            for (int i = 0; i < count; i++)
            {
                int id = GridMath.LegacyFlatten(blocks[i].Position);
                bytes[at] = (byte)id;
                bytes[at + 1] = (byte)(id >> 8);
                bytes[at + 2] = (byte)(id >> 16);
                bytes[at + 3] = (byte)(id >> 24);

                short temperature = (short)blocks[i].Temperature;
                bytes[at + 4] = (byte)temperature;
                bytes[at + 5] = (byte)(temperature >> 8);

                at += LegacyRecordSize;
            }

            return Convert.ToBase64String(bytes);
        }

        public static string EncodeLegacyLoops(IList<float> loopTemperatures)
        {
            int count = loopTemperatures == null ? 0 : loopTemperatures.Count;
            byte[] bytes = new byte[count * LegacyLoopRecordSize];

            int at = 0;
            for (int i = 0; i < count; i++)
            {
                bytes[at] = (byte)i;
                short temperature = (short)loopTemperatures[i];
                bytes[at + 1] = (byte)temperature;
                bytes[at + 2] = (byte)(temperature >> 8);
                at += LegacyLoopRecordSize;
            }

            return Convert.ToBase64String(bytes);
        }

        private static bool TryDecodeLegacyBlocks(byte[] bytes, List<StoredTemperature> blocks)
        {
            if (bytes.Length % LegacyRecordSize != 0) return false;
            if (blocks == null) return true;

            for (int i = 0; i + LegacyRecordSize <= bytes.Length; i += LegacyRecordSize)
            {
                int id = bytes[i];
                id |= bytes[i + 1] << 8;
                id |= bytes[i + 2] << 16;
                id |= bytes[i + 3] << 24;

                short temperature = (short)(bytes[i + 4] | (bytes[i + 5] << 8));

                blocks.Add(new StoredTemperature(GridMath.LegacyUnflatten(id), temperature));
            }
            return true;
        }

        /// <summary>Decodes the original loop blob, which is keyed by list index.</summary>
        public static bool TryDecodeLegacyLoops(string data, List<float> temperaturesByIndex)
        {
            if (temperaturesByIndex != null) temperaturesByIndex.Clear();
            if (string.IsNullOrEmpty(data)) return false;

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(data);
            }
            catch (FormatException)
            {
                return false;
            }

            if (bytes.Length % LegacyLoopRecordSize != 0) return false;
            if (temperaturesByIndex == null) return true;

            for (int i = 0; i + LegacyLoopRecordSize <= bytes.Length; i += LegacyLoopRecordSize)
            {
                int index = bytes[i];
                short temperature = (short)(bytes[i + 1] | (bytes[i + 2] << 8));

                while (temperaturesByIndex.Count <= index)
                {
                    temperaturesByIndex.Add(0f);
                }
                temperaturesByIndex[index] = temperature;
            }
            return true;
        }

        // ---- primitives ---------------------------------------------------------------------

        private static void WriteInt32(byte[] bytes, ref int at, int value)
        {
            bytes[at++] = (byte)value;
            bytes[at++] = (byte)(value >> 8);
            bytes[at++] = (byte)(value >> 16);
            bytes[at++] = (byte)(value >> 24);
        }

        private static int ReadInt32(byte[] bytes, ref int at)
        {
            int value = bytes[at]
                | (bytes[at + 1] << 8)
                | (bytes[at + 2] << 16)
                | (bytes[at + 3] << 24);
            at += 4;
            return value;
        }

        private static void WriteInt64(byte[] bytes, ref int at, long value)
        {
            for (int i = 0; i < 8; i++)
            {
                bytes[at++] = (byte)(value >> (i * 8));
            }
        }

        private static long ReadInt64(byte[] bytes, ref int at)
        {
            long value = 0;
            for (int i = 0; i < 8; i++)
            {
                value |= (long)bytes[at + i] << (i * 8);
            }
            at += 8;
            return value;
        }

        private static void WriteSingle(byte[] bytes, ref int at, float value)
        {
            byte[] raw = BitConverter.GetBytes(value);
            bytes[at++] = raw[0];
            bytes[at++] = raw[1];
            bytes[at++] = raw[2];
            bytes[at++] = raw[3];
        }

        private static float ReadSingle(byte[] bytes, ref int at)
        {
            float value = BitConverter.ToSingle(bytes, at);
            at += 4;
            return value;
        }
    }
}
