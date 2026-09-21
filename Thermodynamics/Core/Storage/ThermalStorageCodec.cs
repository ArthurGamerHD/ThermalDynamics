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
    /// How full one ring's coolant is, keyed by the same signature <see cref="StoredLoop"/> uses.
    ///
    /// **A section of its own rather than a field on `StoredLoop`**, because `W1` says the format
    /// grows by adding a section: a build that predates coolant being a consumable reads the four
    /// sections it knows and skips this one, where a widened record would have made every older
    /// build reject the payload. A ring saved by such a build simply loads full, which is what it
    /// was.
    /// </summary>
    public struct StoredLoopFill
    {
        public long Signature;
        public float Fill;

        public StoredLoopFill(long signature, float fill)
        {
            Signature = signature;
            Fill = fill;
        }
    }

    /// <summary>
    /// One pipe's held coolant, keyed by its cell: the real J/K it absorbed when its ring was
    /// broken, before <see cref="Thermodynamics.Core.ThermalSettings.HeatTimeScale"/>.
    ///
    /// <para>
    /// It is saved because it is heat capacity that no other saved value implies. The block's
    /// temperature is written whatever happens, so a reload that dropped the capacity would put the
    /// mixed temperature onto the bare pipe and destroy the fraction the mix had just conserved,
    /// on a slower trigger.
    /// </para>
    /// </summary>
    public struct StoredHeldCoolant
    {
        public Vector3I Position;
        public float Capacity;

        public StoredHeldCoolant(Vector3I position, float capacity)
        {
            Position = position;
            Capacity = capacity;
        }
    }

    /// <summary>One saved room air temperature, keyed by the room's anchor cell.</summary>
    public struct StoredRoom
    {
        public Vector3I Anchor;
        public float Temperature;

        public StoredRoom(Vector3I anchor, float temperature)
        {
            Anchor = anchor;
            Temperature = temperature;
        }
    }

    /// <summary>
    /// Serialises grid temperatures to and from a base64 blob. Version 1 is read so old saves load;
    /// version 2 is **extended by adding a section rather than by changing the marker**, so a reader
    /// skips what it does not recognise and a newer save still loads on an older build.
    /// See architecture.md, Persistence.
    /// </summary>
    public static class ThermalStorageCodec
    {
        private const byte Version2Marker = 0xFD;
        private const byte SectionBlocks = 1;
        private const byte SectionLoops = 2;
        private const byte SectionRooms = 3;
        private const byte SectionHeldCoolant = 4;
        private const byte SectionLoopFill = 5;

        private const int LegacyRecordSize = 6;
        private const int LegacyLoopRecordSize = 3;

        private const int Int32Size = 4;
        private const int RecordSize = 12;   // 8 byte key + 4 byte temperature

        // ---- version 2 ---------------------------------------------------------------------

        /// <summary>Encodes block and loop temperatures in the current format.</summary>
        public static string Encode(IList<StoredTemperature> blocks, IList<StoredLoop> loops)
        {
            return Encode(blocks, loops, null);
        }

        /// <summary>Encodes block, loop and room air temperatures in the current format.</summary>
        public static string Encode(IList<StoredTemperature> blocks, IList<StoredLoop> loops, IList<StoredRoom> rooms)
        {
            return Encode(blocks, loops, rooms, null);
        }

        /// <summary>
        /// Encodes block, loop, room air and held coolant in the current format. Held coolant is a
        /// fourth section rather than a new marker, so a build that predates it reads the three it
        /// knows and skips this one instead of rejecting the payload.
        /// </summary>
        public static string Encode(IList<StoredTemperature> blocks, IList<StoredLoop> loops,
            IList<StoredRoom> rooms, IList<StoredHeldCoolant> held)
        {
            return Encode(blocks, loops, rooms, held, null);
        }

        /// <summary>
        /// The same, with how full each ring's coolant is — a fifth section, on the rule `W1`
        /// states: a build that predates it skips what it does not know, and a ring it saved loads
        /// full, which is what it was.
        /// </summary>
        public static string Encode(IList<StoredTemperature> blocks, IList<StoredLoop> loops,
            IList<StoredRoom> rooms, IList<StoredHeldCoolant> held, IList<StoredLoopFill> fills)
        {
            int blockCount = blocks == null ? 0 : blocks.Count;
            int loopCount = loops == null ? 0 : loops.Count;
            int roomCount = rooms == null ? 0 : rooms.Count;
            int heldCount = held == null ? 0 : held.Count;
            int fillCount = fills == null ? 0 : fills.Count;

            int size = 1
                + (1 + Int32Size + (blockCount * RecordSize))
                + (1 + Int32Size + (loopCount * RecordSize));

            // A world with room air disabled writes no section rather than an empty one, so the
            // format costs nothing when the feature is unused.
            if (roomCount > 0) size += 1 + Int32Size + (roomCount * RecordSize);

            // The same rule, and it earns more here: a pipe holds coolant only between a ring
            // breaking and being rebuilt, so on almost every grid ever saved this section is absent.
            if (heldCount > 0) size += 1 + Int32Size + (heldCount * RecordSize);

            // And once more: a ring that is full is the overwhelming case, and a full ring needs no
            // record — the fill only has to survive a save while it is short of full.
            if (fillCount > 0) size += 1 + Int32Size + (fillCount * RecordSize);

            byte[] bytes = new byte[size];
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

            if (roomCount > 0)
            {
                bytes[at++] = SectionRooms;
                WriteInt32(bytes, ref at, roomCount);

                for (int i = 0; i < roomCount; i++)
                {
                    StoredRoom entry = rooms[i];
                    WriteInt64(bytes, ref at, GridMath.Key(entry.Anchor));
                    WriteSingle(bytes, ref at, entry.Temperature);
                }
            }

            if (heldCount > 0)
            {
                bytes[at++] = SectionHeldCoolant;
                WriteInt32(bytes, ref at, heldCount);

                for (int i = 0; i < heldCount; i++)
                {
                    StoredHeldCoolant entry = held[i];
                    WriteInt64(bytes, ref at, GridMath.Key(entry.Position));
                    WriteSingle(bytes, ref at, entry.Capacity);
                }
            }

            if (fillCount > 0)
            {
                bytes[at++] = SectionLoopFill;
                WriteInt32(bytes, ref at, fillCount);

                for (int i = 0; i < fillCount; i++)
                {
                    StoredLoopFill entry = fills[i];
                    WriteInt64(bytes, ref at, entry.Signature);
                    WriteSingle(bytes, ref at, entry.Fill);
                }
            }

            return Convert.ToBase64String(bytes);
        }

        /// <summary>
        /// Decodes either format. Returns false when the payload is unreadable, leaving the
        /// output lists empty rather than throwing into the host's load path.
        /// </summary>
        public static bool TryDecode(string data, List<StoredTemperature> blocks, List<StoredLoop> loops)
        {
            return TryDecode(data, blocks, loops, null);
        }

        /// <summary>
        /// Decodes either format, including the room air section. A payload written before rooms were
        /// saved leaves <paramref name="rooms"/> empty.
        /// </summary>
        public static bool TryDecode(string data, List<StoredTemperature> blocks, List<StoredLoop> loops, List<StoredRoom> rooms)
        {
            return TryDecode(data, blocks, loops, rooms, null);
        }

        /// <summary>
        /// Decodes either format, including held coolant. A payload written before pipes could hold
        /// any — which is every payload a released build has written — leaves
        /// <paramref name="held"/> empty.
        /// </summary>
        public static bool TryDecode(string data, List<StoredTemperature> blocks, List<StoredLoop> loops,
            List<StoredRoom> rooms, List<StoredHeldCoolant> held)
        {
            return TryDecode(data, blocks, loops, rooms, held, null);
        }

        /// <summary>
        /// The same, including how full each ring is. A payload written before coolant was a
        /// consumable leaves <paramref name="fills"/> empty, and a ring with no record loads full —
        /// which is what it was in the world that saved it.
        /// </summary>
        public static bool TryDecode(string data, List<StoredTemperature> blocks, List<StoredLoop> loops,
            List<StoredRoom> rooms, List<StoredHeldCoolant> held, List<StoredLoopFill> fills)
        {
            if (blocks != null) blocks.Clear();
            if (loops != null) loops.Clear();
            if (rooms != null) rooms.Clear();
            if (held != null) held.Clear();
            if (fills != null) fills.Clear();

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
                return TryDecodeVersion2(bytes, blocks, loops, rooms, held, fills);
            }

            return TryDecodeLegacyBlocks(bytes, blocks);
        }

        private static bool TryDecodeVersion2(byte[] bytes, List<StoredTemperature> blocks,
            List<StoredLoop> loops, List<StoredRoom> rooms, List<StoredHeldCoolant> held,
            List<StoredLoopFill> fills)
        {
            // Every read is bounds checked up front rather than caught afterwards: the in-game
            // script compiler's whitelist prohibits IndexOutOfRangeException, so a truncated payload
            // must be rejected before it is read.
            int at = 1;
            while (at < bytes.Length)
            {
                byte section = bytes[at++];

                if (at + Int32Size > bytes.Length) return false;
                int count = ReadInt32(bytes, ref at);
                if (count < 0) return false;

                // Guards against a corrupt count claiming more records than the payload holds, and
                // against the multiplication overflowing on an extreme one.
                if (count > (bytes.Length - at) / RecordSize) return false;

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
                    else if (section == SectionRooms)
                    {
                        if (rooms != null) rooms.Add(new StoredRoom(GridMath.FromKey(key), temperature));
                    }
                    else if (section == SectionHeldCoolant)
                    {
                        if (held != null) held.Add(new StoredHeldCoolant(GridMath.FromKey(key), temperature));
                    }
                    else if (section == SectionLoopFill)
                    {
                        if (fills != null) fills.Add(new StoredLoopFill(key, temperature));
                    }
                }
            }
            return true;
        }

        // ---- version 1 (read only) ----------------------------------------------------------

        /// <summary>
        /// Encodes in the version 1 format. Retained so tests can verify the reader against data in
        /// the legacy layout.
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

        /// <summary>Decodes the version 1 loop blob, which is keyed by list index.</summary>
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
