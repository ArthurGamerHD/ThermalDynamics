using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class StorageCodecTests
    {
        [Fact]
/// <summary>BlocksAndLoopsRoundTrip operation.</summary>
        public void BlocksAndLoopsRoundTrip()
        {
            List<StoredTemperature> blocks = new List<StoredTemperature>
            {
/// <summary>StoredTemperature operation.</summary>
                new StoredTemperature(new Vector3I(0, 0, 0), 293.15f),
/// <summary>StoredTemperature operation.</summary>
                new StoredTemperature(new Vector3I(-7, 12, 300), 812.5f),
/// <summary>StoredTemperature operation.</summary>
                new StoredTemperature(new Vector3I(100000, -100000, 5), 4.25f),
            };
            List<StoredLoop> loops = new List<StoredLoop>
            {
/// <summary>StoredLoop operation.</summary>
                new StoredLoop(123456789L, 350.5f),
/// <summary>StoredLoop operation.</summary>
                new StoredLoop(-987654321L, 1.5f),
            };

            string encoded = ThermalStorageCodec.Encode(blocks, loops);

/// <summary>List operation.</summary>
            List<StoredTemperature> decodedBlocks = new List<StoredTemperature>();
/// <summary>List operation.</summary>
            List<StoredLoop> decodedLoops = new List<StoredLoop>();
            Assert.True(ThermalStorageCodec.TryDecode(encoded, decodedBlocks, decodedLoops));

            Assert.Equal(blocks.Count, decodedBlocks.Count);
            for (int i = 0; i < blocks.Count; i++)
            {
                Assert.Equal(blocks[i].Position, decodedBlocks[i].Position);
                Assert.Equal(blocks[i].Temperature, decodedBlocks[i].Temperature, 4);
            }

            Assert.Equal(loops.Count, decodedLoops.Count);
            for (int i = 0; i < loops.Count; i++)
            {
                Assert.Equal(loops[i].Signature, decodedLoops[i].Signature);
                Assert.Equal(loops[i].Temperature, decodedLoops[i].Temperature, 4);
            }
        }

        [Fact]
/// <summary>FractionalTemperaturesSurvive operation.</summary>
        public void FractionalTemperaturesSurvive()
        {
            List<StoredTemperature> blocks = new List<StoredTemperature>
            {
/// <summary>StoredTemperature operation.</summary>
                new StoredTemperature(Vector3I.Zero, 293.456789f),
            };

/// <summary>List operation.</summary>
            List<StoredTemperature> decoded = new List<StoredTemperature>();
            ThermalStorageCodec.TryDecode(ThermalStorageCodec.Encode(blocks, null), decoded, null);

            Assert.Equal(293.456789f, decoded[0].Temperature, 4);
        }

        [Fact]
/// <summary>TheLegacyFormatLosesTheFraction operation.</summary>
        public void TheLegacyFormatLosesTheFraction()
        {
            List<StoredTemperature> blocks = new List<StoredTemperature>
            {
/// <summary>StoredTemperature operation.</summary>
                new StoredTemperature(Vector3I.Zero, 293.99f),
            };

/// <summary>List operation.</summary>
            List<StoredTemperature> decoded = new List<StoredTemperature>();
            ThermalStorageCodec.TryDecode(ThermalStorageCodec.EncodeLegacyBlocks(blocks), decoded, null);

            Assert.Equal(293f, decoded[0].Temperature, 3);
        }

        [Fact]
/// <summary>LegacyPayloadsStillLoad operation.</summary>
        public void LegacyPayloadsStillLoad()
        {
            List<StoredTemperature> blocks = new List<StoredTemperature>
            {
/// <summary>StoredTemperature operation.</summary>
                new StoredTemperature(new Vector3I(1, 2, 3), 400f),
/// <summary>StoredTemperature operation.</summary>
                new StoredTemperature(new Vector3I(-4, -5, -6), 250f),
            };

            string legacy = ThermalStorageCodec.EncodeLegacyBlocks(blocks);

/// <summary>List operation.</summary>
            List<StoredTemperature> decoded = new List<StoredTemperature>();
            Assert.True(ThermalStorageCodec.TryDecode(legacy, decoded, null));

            Assert.Equal(2, decoded.Count);
            Assert.Equal(new Vector3I(1, 2, 3), decoded[0].Position);
            Assert.Equal(400f, decoded[0].Temperature, 3);
            Assert.Equal(new Vector3I(-4, -5, -6), decoded[1].Position);
            Assert.Equal(250f, decoded[1].Temperature, 3);
        }

        [Fact]
/// <summary>LegacyLoopsAreIndexedByPosition operation.</summary>
        public void LegacyLoopsAreIndexedByPosition()
        {
            List<float> temperatures = new List<float> { 300f, 450f, 600f };
            string encoded = ThermalStorageCodec.EncodeLegacyLoops(temperatures);

/// <summary>List operation.</summary>
            List<float> decoded = new List<float>();
            Assert.True(ThermalStorageCodec.TryDecodeLegacyLoops(encoded, decoded));

            Assert.Equal(3, decoded.Count);
            Assert.Equal(450f, decoded[1], 3);
        }

        [Fact]
/// <summary>CorruptPayloadsAreRejectedNotThrown operation.</summary>
        public void CorruptPayloadsAreRejectedNotThrown()
        {
/// <summary>List operation.</summary>
            List<StoredTemperature> blocks = new List<StoredTemperature>();
/// <summary>List operation.</summary>
            List<StoredLoop> loops = new List<StoredLoop>();

            Assert.False(ThermalStorageCodec.TryDecode(null, blocks, loops));
            Assert.False(ThermalStorageCodec.TryDecode("", blocks, loops));
            Assert.False(ThermalStorageCodec.TryDecode("not base64 at all !!!", blocks, loops));
            Assert.False(ThermalStorageCodec.TryDecode(Convert.ToBase64String(new byte[] { 1, 2, 3 }), blocks, loops));

            Assert.Empty(blocks);
            Assert.Empty(loops);
        }

        [Fact]
/// <summary>EveryTruncationOfAValidPayloadIsRejected operation.</summary>
        public void EveryTruncationOfAValidPayloadIsRejected()
        {
            List<StoredTemperature> source = new List<StoredTemperature>
            {
/// <summary>StoredTemperature operation.</summary>
                new StoredTemperature(Vector3I.Zero, 300f),
/// <summary>StoredTemperature operation.</summary>
                new StoredTemperature(new Vector3I(4, -7, 9), 812.25f),
            };
/// <summary>StoredLoop operation.</summary>
            List<StoredLoop> sourceLoops = new List<StoredLoop> { new StoredLoop(99887766L, 275.5f) };
            List<StoredRoom> sourceRooms = new List<StoredRoom>
            {
/// <summary>StoredRoom operation.</summary>
                new StoredRoom(new Vector3I(1, 1, 1), 295.5f),
/// <summary>StoredRoom operation.</summary>
                new StoredRoom(new Vector3I(-3, 8, 2), 331.25f),
            };

            byte[] full = Convert.FromBase64String(ThermalStorageCodec.Encode(source, sourceLoops, sourceRooms));

            for (int length = 1; length < full.Length; length++)
            {
                byte[] cut = new byte[length];
                Array.Copy(full, cut, length);

/// <summary>List operation.</summary>
                List<StoredTemperature> blocks = new List<StoredTemperature>();
/// <summary>List operation.</summary>
                List<StoredLoop> loops = new List<StoredLoop>();
/// <summary>List operation.</summary>
                List<StoredRoom> rooms = new List<StoredRoom>();

                ThermalStorageCodec.TryDecode(Convert.ToBase64String(cut), blocks, loops, rooms);
                Assert.True(blocks.Count <= source.Count);
                Assert.True(loops.Count <= sourceLoops.Count);
                Assert.True(rooms.Count <= sourceRooms.Count);
            }
        }

        [Fact]
/// <summary>RoomAirRoundTrips operation.</summary>
        public void RoomAirRoundTrips()
        {
            List<StoredRoom> rooms = new List<StoredRoom>
            {
/// <summary>StoredRoom operation.</summary>
                new StoredRoom(new Vector3I(2, 2, 2), 293.15f),
/// <summary>StoredRoom operation.</summary>
                new StoredRoom(new Vector3I(-40, 17, 2000), 341.75f),
            };

            string encoded = ThermalStorageCodec.Encode(null, null, rooms);

/// <summary>List operation.</summary>
            List<StoredRoom> decoded = new List<StoredRoom>();
            Assert.True(ThermalStorageCodec.TryDecode(encoded, null, null, decoded));

            Assert.Equal(rooms.Count, decoded.Count);
            for (int i = 0; i < rooms.Count; i++)
            {
                Assert.Equal(rooms[i].Anchor, decoded[i].Anchor);
                Assert.Equal(rooms[i].Temperature, decoded[i].Temperature, 4);
            }
        }

        [Fact]
/// <summary>AReaderThatDoesNotKnowAboutRoomsStillReadsBlocksAndLoops operation.</summary>
        public void AReaderThatDoesNotKnowAboutRoomsStillReadsBlocksAndLoops()
        {
            List<StoredTemperature> blocks = new List<StoredTemperature>
            {
/// <summary>StoredTemperature operation.</summary>
                new StoredTemperature(new Vector3I(1, 2, 3), 455.5f),
            };
/// <summary>StoredLoop operation.</summary>
            List<StoredLoop> loops = new List<StoredLoop> { new StoredLoop(4242L, 310.25f) };
/// <summary>StoredRoom operation.</summary>
            List<StoredRoom> rooms = new List<StoredRoom> { new StoredRoom(Vector3I.Zero, 290f) };

            string encoded = ThermalStorageCodec.Encode(blocks, loops, rooms);

/// <summary>List operation.</summary>
            List<StoredTemperature> decodedBlocks = new List<StoredTemperature>();
/// <summary>List operation.</summary>
            List<StoredLoop> decodedLoops = new List<StoredLoop>();
            Assert.True(ThermalStorageCodec.TryDecode(encoded, decodedBlocks, decodedLoops));

            Assert.Equal(455.5f, decodedBlocks[0].Temperature, 4);
            Assert.Equal(310.25f, decodedLoops[0].Temperature, 4);
        }

        [Fact]
/// <summary>APayloadWithNoRoomSectionDecodesToNoRooms operation.</summary>
        public void APayloadWithNoRoomSectionDecodesToNoRooms()
        {
            List<StoredTemperature> blocks = new List<StoredTemperature>
            {
/// <summary>StoredTemperature operation.</summary>
                new StoredTemperature(Vector3I.Zero, 300f),
            };

/// <summary>List operation.</summary>
            List<StoredRoom> rooms = new List<StoredRoom>();
            Assert.True(ThermalStorageCodec.TryDecode(
                ThermalStorageCodec.Encode(blocks, null), null, null, rooms));

            Assert.Empty(rooms);
        }

        [Fact]
/// <summary>AReaderThatDoesNotKnowAboutHeldCoolantStillReadsEverythingElse operation.</summary>
        public void AReaderThatDoesNotKnowAboutHeldCoolantStillReadsEverythingElse()
        {
            List<StoredTemperature> blocks = new List<StoredTemperature>
            {
/// <summary>StoredTemperature operation.</summary>
                new StoredTemperature(new Vector3I(1, 2, 3), 455.5f),
            };
/// <summary>StoredLoop operation.</summary>
            List<StoredLoop> loops = new List<StoredLoop> { new StoredLoop(4242L, 310.25f) };
/// <summary>StoredRoom operation.</summary>
            List<StoredRoom> rooms = new List<StoredRoom> { new StoredRoom(Vector3I.Zero, 290f) };
            List<StoredHeldCoolant> held = new List<StoredHeldCoolant>
            {
/// <summary>StoredHeldCoolant operation.</summary>
                new StoredHeldCoolant(new Vector3I(4, 5, 6), 6422000f),
            };

            string encoded = ThermalStorageCodec.Encode(blocks, loops, rooms, held);

/// <summary>List operation.</summary>
            List<StoredTemperature> decodedBlocks = new List<StoredTemperature>();
/// <summary>List operation.</summary>
            List<StoredLoop> decodedLoops = new List<StoredLoop>();
/// <summary>List operation.</summary>
            List<StoredRoom> decodedRooms = new List<StoredRoom>();
            Assert.True(ThermalStorageCodec.TryDecode(encoded, decodedBlocks, decodedLoops, decodedRooms));

            Assert.Equal(455.5f, decodedBlocks[0].Temperature, 4);
            Assert.Equal(310.25f, decodedLoops[0].Temperature, 4);
            Assert.Equal(290f, decodedRooms[0].Temperature, 4);

/// <summary>List operation.</summary>
            List<StoredHeldCoolant> decodedHeld = new List<StoredHeldCoolant>();
            Assert.True(ThermalStorageCodec.TryDecode(encoded, null, null, null, decodedHeld));
            Assert.Single(decodedHeld);
            Assert.Equal(new Vector3I(4, 5, 6), decodedHeld[0].Position);
            Assert.Equal(6422000f, decodedHeld[0].Capacity, 0);
        }

        [Fact]
/// <summary>APayloadWithNoHeldCoolantSectionDecodesToNone operation.</summary>
        public void APayloadWithNoHeldCoolantSectionDecodesToNone()
        {
            List<StoredTemperature> blocks = new List<StoredTemperature>
            {
/// <summary>StoredTemperature operation.</summary>
                new StoredTemperature(Vector3I.Zero, 300f),
            };

/// <summary>List operation.</summary>
            List<StoredHeldCoolant> held = new List<StoredHeldCoolant>();
            Assert.True(ThermalStorageCodec.TryDecode(
                ThermalStorageCodec.Encode(blocks, null), null, null, null, held));

            Assert.Empty(held);
        }

        [Fact]
/// <summary>AnOversizedRecordCountIsRejected operation.</summary>
        public void AnOversizedRecordCountIsRejected()
        {
            byte[] bytes = new byte[] { 0xFD, 1, 0xFF, 0xFF, 0xFF, 0x7F };   // count = int.MaxValue

/// <summary>List operation.</summary>
            List<StoredTemperature> blocks = new List<StoredTemperature>();
/// <summary>List operation.</summary>
            List<StoredLoop> loops = new List<StoredLoop>();

            Assert.False(ThermalStorageCodec.TryDecode(Convert.ToBase64String(bytes), blocks, loops));
            Assert.Empty(blocks);
            Assert.Empty(loops);
        }

        [Fact]
/// <summary>EmptyPayloadsRoundTrip operation.</summary>
        public void EmptyPayloadsRoundTrip()
        {
            string encoded = ThermalStorageCodec.Encode(new List<StoredTemperature>(), new List<StoredLoop>());

/// <summary>List operation.</summary>
            List<StoredTemperature> blocks = new List<StoredTemperature>();
/// <summary>List operation.</summary>
            List<StoredLoop> loops = new List<StoredLoop>();

            Assert.True(ThermalStorageCodec.TryDecode(encoded, blocks, loops));
            Assert.Empty(blocks);
            Assert.Empty(loops);
        }

        [Fact]
/// <summary>DistantBlocksSurviveTheCurrentFormatButNotTheLegacyOne operation.</summary>
        public void DistantBlocksSurviveTheCurrentFormatButNotTheLegacyOne()
        {
/// <summary>Vector3I operation.</summary>
            Vector3I distant = new Vector3I(2000, 0, 0);
            List<StoredTemperature> blocks = new List<StoredTemperature>
            {
/// <summary>StoredTemperature operation.</summary>
                new StoredTemperature(Vector3I.Zero, 300f),
/// <summary>StoredTemperature operation.</summary>
                new StoredTemperature(distant, 900f),
            };

/// <summary>List operation.</summary>
            List<StoredTemperature> modern = new List<StoredTemperature>();
            ThermalStorageCodec.TryDecode(ThermalStorageCodec.Encode(blocks, null), modern, null);
            Assert.Equal(distant, modern[1].Position);

/// <summary>List operation.</summary>
            List<StoredTemperature> legacy = new List<StoredTemperature>();
            ThermalStorageCodec.TryDecode(ThermalStorageCodec.EncodeLegacyBlocks(blocks), legacy, null);
            Assert.NotEqual(distant, legacy[1].Position);
        }
    }

    public class SettingsTests
    {
        [Fact]
/// <summary>DerivedValuesFollowFrequency operation.</summary>
        public void DerivedValuesFollowFrequency()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.Frequency = 10;
            settings.SimulationSpeed = 2f;
            settings.Derive();

            Assert.Equal(0.1f, settings.StepSeconds, 5);
            Assert.Equal(20f, settings.StepsPerSecond, 5);
        }

        [Fact]
/// <summary>OutOfRangeValuesAreClamped operation.</summary>
        public void OutOfRangeValuesAreClamped()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.Frequency = 0;
            settings.SimulationSpeed = -3f;
            settings.VacuumTemperature = -100f;
            settings.Derive();

            Assert.Equal(1, settings.Frequency);
            Assert.Equal(1f, settings.SimulationSpeed, 5);
            Assert.Equal(0f, settings.VacuumTemperature, 5);
        }

        [Fact]
/// <summary>ValidateReportsSuspiciousValues operation.</summary>
        public void ValidateReportsSuspiciousValues()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            Assert.Empty(settings.Validate());

            settings.Frequency = 240;
            Assert.NotEmpty(settings.Validate());
        }

        [Fact]
/// <summary>CloningDoesNotShareState operation.</summary>
        public void CloningDoesNotShareState()
        {
            ThermalSettings settings = new ThermalSettings { Frequency = 4 };
            ThermalSettings copy = settings.Clone();
            copy.Frequency = 32;

            Assert.Equal(4, settings.Frequency);
        }
    }

    public class DefinitionTests
    {
        [Fact]
/// <summary>BlockPropertiesAreClampedToTheirRanges operation.</summary>
        public void BlockPropertiesAreClampedToTheirRanges()
        {
            BlockThermalProperties properties = new BlockThermalProperties
            {
                Conductivity = -5f,
                Emissivity = -2f,
                SpecificHeat = 0f,
                ExposedSurfaceMultiplier = -1f,
                CriticalTemperature = -50f,
            };

            properties.Clamp();

            Assert.Equal(0f, properties.Conductivity, 5);
            Assert.Equal(0f, properties.Emissivity, 5);
            Assert.True(properties.SpecificHeat > 0f);
            Assert.Equal(0f, properties.ExposedSurfaceMultiplier, 5);
            Assert.Equal(0f, properties.CriticalTemperature, 5);
        }

        [Fact]
/// <summary>ZeroSpecificHeatCannotProduceAnInfiniteTemperature operation.</summary>
        public void ZeroSpecificHeatCannotProduceAnInfiniteTemperature()
        {
            BlockThermalProperties properties = Catalog.DefaultThermal();
            properties.SpecificHeat = 0f;
            properties.Clamp();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(BlockModel.Solid("Broken", Vector3I.One, 100f, properties), Vector3I.Zero)
                   .Producing(1e6f);

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 300f);
            simulation.StepExact(4, Worlds.Shadow());

            float temperature = simulation.Solver.Nodes[0].Temperature;
            Assert.False(float.IsNaN(temperature));
            Assert.False(float.IsInfinity(temperature));
        }

        [Fact]
/// <summary>ValidateFlagsUnphysicalValuesWithoutChangingThem operation.</summary>
        public void ValidateFlagsUnphysicalValuesWithoutChangingThem()
        {
            BlockThermalProperties properties = new BlockThermalProperties
            {
                Emissivity = 2f,
                ProducerWasteEnergy = 3f,
            };

            List<string> problems = properties.Validate();

            Assert.NotEmpty(problems);
            Assert.Equal(2f, properties.Emissivity, 5);
        }

        [Fact]
/// <summary>PlanetAndLoopPropertiesClampToo operation.</summary>
        public void PlanetAndLoopPropertiesClampToo()
        {
            PlanetThermalProperties planet = new PlanetThermalProperties { SolarDecay = 4f, ConvectionCoefficient = -1f };
            planet.Clamp();
            Assert.Equal(1f, planet.SolarDecay, 5);
            Assert.Equal(0f, planet.ConvectionCoefficient, 5);

            LoopThermalProperties loop = new LoopThermalProperties
            {
                CoolantMassPerPipe = -4f,
                CoolantKilogramsPerCubicMetre = -33f,
                HeatTransferCoefficient = -9f,
            };
            loop.Clamp();
            Assert.Equal(0f, loop.HeatTransferCoefficient, 5);

            Assert.Equal(0f, loop.CoolantMassPerPipe, 5);
            Assert.Equal(0f, loop.CoolantKilogramsPerCubicMetre, 5);

            Assert.True(loop.MassPerPipe(2.5f) > 0f);
        }
    }

    public class SchedulerTests
    {

        [Fact]
/// <summary>RoomMappingBudgetStaysWithinBounds operation.</summary>
        public void RoomMappingBudgetStaysWithinBounds()
        {
            Assert.Equal(64, SimulationScheduler.RoomMappingBudget(0));
            Assert.Equal(64, SimulationScheduler.RoomMappingBudget(100));
            Assert.Equal(4096, SimulationScheduler.RoomMappingBudget(100000000));

            int middle = SimulationScheduler.RoomMappingBudget(60000);
            Assert.InRange(middle, 64, 4096);
        }
    }
}
