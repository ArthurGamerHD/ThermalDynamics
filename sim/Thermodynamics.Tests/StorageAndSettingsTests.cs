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
        public void BlocksAndLoopsRoundTrip()
        {
            List<StoredTemperature> blocks = new List<StoredTemperature>
            {
                new StoredTemperature(new Vector3I(0, 0, 0), 293.15f),
                new StoredTemperature(new Vector3I(-7, 12, 300), 812.5f),
                new StoredTemperature(new Vector3I(100000, -100000, 5), 4.25f),
            };
            List<StoredLoop> loops = new List<StoredLoop>
            {
                new StoredLoop(123456789L, 350.5f),
                new StoredLoop(-987654321L, 1.5f),
            };

            string encoded = ThermalStorageCodec.Encode(blocks, loops);

            List<StoredTemperature> decodedBlocks = new List<StoredTemperature>();
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
        public void FractionalTemperaturesSurvive()
        {
            List<StoredTemperature> blocks = new List<StoredTemperature>
            {
                new StoredTemperature(Vector3I.Zero, 293.456789f),
            };

            List<StoredTemperature> decoded = new List<StoredTemperature>();
            ThermalStorageCodec.TryDecode(ThermalStorageCodec.Encode(blocks, null), decoded, null);

            Assert.Equal(293.456789f, decoded[0].Temperature, 4);
        }

        /// <summary>
        /// The original format stored temperature as a short, so every save quantised the whole
        /// grid to whole kelvin — and wrote the quantised value back onto the live simulation.
        /// </summary>
        [Fact]
        public void TheLegacyFormatLosesTheFraction()
        {
            List<StoredTemperature> blocks = new List<StoredTemperature>
            {
                new StoredTemperature(Vector3I.Zero, 293.99f),
            };

            List<StoredTemperature> decoded = new List<StoredTemperature>();
            ThermalStorageCodec.TryDecode(ThermalStorageCodec.EncodeLegacyBlocks(blocks), decoded, null);

            Assert.Equal(293f, decoded[0].Temperature, 3);
        }

        [Fact]
        public void LegacyPayloadsStillLoad()
        {
            List<StoredTemperature> blocks = new List<StoredTemperature>
            {
                new StoredTemperature(new Vector3I(1, 2, 3), 400f),
                new StoredTemperature(new Vector3I(-4, -5, -6), 250f),
            };

            string legacy = ThermalStorageCodec.EncodeLegacyBlocks(blocks);

            List<StoredTemperature> decoded = new List<StoredTemperature>();
            Assert.True(ThermalStorageCodec.TryDecode(legacy, decoded, null));

            Assert.Equal(2, decoded.Count);
            Assert.Equal(new Vector3I(1, 2, 3), decoded[0].Position);
            Assert.Equal(400f, decoded[0].Temperature, 3);
            Assert.Equal(new Vector3I(-4, -5, -6), decoded[1].Position);
            Assert.Equal(250f, decoded[1].Temperature, 3);
        }

        [Fact]
        public void LegacyLoopsAreIndexedByPosition()
        {
            List<float> temperatures = new List<float> { 300f, 450f, 600f };
            string encoded = ThermalStorageCodec.EncodeLegacyLoops(temperatures);

            List<float> decoded = new List<float>();
            Assert.True(ThermalStorageCodec.TryDecodeLegacyLoops(encoded, decoded));

            Assert.Equal(3, decoded.Count);
            Assert.Equal(450f, decoded[1], 3);
        }

        [Fact]
        public void CorruptPayloadsAreRejectedNotThrown()
        {
            List<StoredTemperature> blocks = new List<StoredTemperature>();
            List<StoredLoop> loops = new List<StoredLoop>();

            Assert.False(ThermalStorageCodec.TryDecode(null, blocks, loops));
            Assert.False(ThermalStorageCodec.TryDecode("", blocks, loops));
            Assert.False(ThermalStorageCodec.TryDecode("not base64 at all !!!", blocks, loops));
            Assert.False(ThermalStorageCodec.TryDecode(Convert.ToBase64String(new byte[] { 1, 2, 3 }), blocks, loops));

            Assert.Empty(blocks);
            Assert.Empty(loops);
        }

        /// <summary>
        /// The decoder used to lean on catching IndexOutOfRangeException, which the in-game
        /// script compiler's whitelist prohibits, so the bounds are now checked before each read.
        /// Every truncation of a valid payload has to be rejected without throwing.
        /// </summary>
        [Fact]
        public void EveryTruncationOfAValidPayloadIsRejected()
        {
            List<StoredTemperature> source = new List<StoredTemperature>
            {
                new StoredTemperature(Vector3I.Zero, 300f),
                new StoredTemperature(new Vector3I(4, -7, 9), 812.25f),
            };
            List<StoredLoop> sourceLoops = new List<StoredLoop> { new StoredLoop(99887766L, 275.5f) };
            List<StoredRoom> sourceRooms = new List<StoredRoom>
            {
                new StoredRoom(new Vector3I(1, 1, 1), 295.5f),
                new StoredRoom(new Vector3I(-3, 8, 2), 331.25f),
            };

            byte[] full = Convert.FromBase64String(ThermalStorageCodec.Encode(source, sourceLoops, sourceRooms));

            for (int length = 1; length < full.Length; length++)
            {
                byte[] cut = new byte[length];
                Array.Copy(full, cut, length);

                List<StoredTemperature> blocks = new List<StoredTemperature>();
                List<StoredLoop> loops = new List<StoredLoop>();
                List<StoredRoom> rooms = new List<StoredRoom>();

                // may decode the sections it did receive in full, but must never throw
                ThermalStorageCodec.TryDecode(Convert.ToBase64String(cut), blocks, loops, rooms);
                Assert.True(blocks.Count <= source.Count);
                Assert.True(loops.Count <= sourceLoops.Count);
                Assert.True(rooms.Count <= sourceRooms.Count);
            }
        }

        [Fact]
        public void RoomAirRoundTrips()
        {
            List<StoredRoom> rooms = new List<StoredRoom>
            {
                new StoredRoom(new Vector3I(2, 2, 2), 293.15f),
                new StoredRoom(new Vector3I(-40, 17, 2000), 341.75f),
            };

            string encoded = ThermalStorageCodec.Encode(null, null, rooms);

            List<StoredRoom> decoded = new List<StoredRoom>();
            Assert.True(ThermalStorageCodec.TryDecode(encoded, null, null, decoded));

            Assert.Equal(rooms.Count, decoded.Count);
            for (int i = 0; i < rooms.Count; i++)
            {
                Assert.Equal(rooms[i].Anchor, decoded[i].Anchor);
                Assert.Equal(rooms[i].Temperature, decoded[i].Temperature, 4);
            }
        }

        /// <summary>
        /// Rooms were added to version 2 as a new section rather than a new marker, so a reader
        /// that predates them has to skip past it and still read everything it does know. That is
        /// what lets a world be opened on an older build after being saved on this one.
        /// </summary>
        [Fact]
        public void AReaderThatDoesNotKnowAboutRoomsStillReadsBlocksAndLoops()
        {
            List<StoredTemperature> blocks = new List<StoredTemperature>
            {
                new StoredTemperature(new Vector3I(1, 2, 3), 455.5f),
            };
            List<StoredLoop> loops = new List<StoredLoop> { new StoredLoop(4242L, 310.25f) };
            List<StoredRoom> rooms = new List<StoredRoom> { new StoredRoom(Vector3I.Zero, 290f) };

            string encoded = ThermalStorageCodec.Encode(blocks, loops, rooms);

            // the three argument overload is exactly what the older reader was
            List<StoredTemperature> decodedBlocks = new List<StoredTemperature>();
            List<StoredLoop> decodedLoops = new List<StoredLoop>();
            Assert.True(ThermalStorageCodec.TryDecode(encoded, decodedBlocks, decodedLoops));

            Assert.Equal(455.5f, decodedBlocks[0].Temperature, 4);
            Assert.Equal(310.25f, decodedLoops[0].Temperature, 4);
        }

        /// <summary>A save from before rooms existed has to load, leaving the room list empty.</summary>
        [Fact]
        public void APayloadWithNoRoomSectionDecodesToNoRooms()
        {
            List<StoredTemperature> blocks = new List<StoredTemperature>
            {
                new StoredTemperature(Vector3I.Zero, 300f),
            };

            List<StoredRoom> rooms = new List<StoredRoom>();
            Assert.True(ThermalStorageCodec.TryDecode(
                ThermalStorageCodec.Encode(blocks, null), null, null, rooms));

            Assert.Empty(rooms);
        }

        /// <summary>A record count far larger than the payload must not be trusted.</summary>
        [Fact]
        public void AnOversizedRecordCountIsRejected()
        {
            byte[] bytes = new byte[] { 0xFD, 1, 0xFF, 0xFF, 0xFF, 0x7F };   // count = int.MaxValue

            List<StoredTemperature> blocks = new List<StoredTemperature>();
            List<StoredLoop> loops = new List<StoredLoop>();

            Assert.False(ThermalStorageCodec.TryDecode(Convert.ToBase64String(bytes), blocks, loops));
            Assert.Empty(blocks);
            Assert.Empty(loops);
        }

        [Fact]
        public void EmptyPayloadsRoundTrip()
        {
            string encoded = ThermalStorageCodec.Encode(new List<StoredTemperature>(), new List<StoredLoop>());

            List<StoredTemperature> blocks = new List<StoredTemperature>();
            List<StoredLoop> loops = new List<StoredLoop>();

            Assert.True(ThermalStorageCodec.TryDecode(encoded, blocks, loops));
            Assert.Empty(blocks);
            Assert.Empty(loops);
        }

        [Fact]
        public void DistantBlocksSurviveTheCurrentFormatButNotTheLegacyOne()
        {
            Vector3I distant = new Vector3I(2000, 0, 0);
            List<StoredTemperature> blocks = new List<StoredTemperature>
            {
                new StoredTemperature(Vector3I.Zero, 300f),
                new StoredTemperature(distant, 900f),
            };

            List<StoredTemperature> modern = new List<StoredTemperature>();
            ThermalStorageCodec.TryDecode(ThermalStorageCodec.Encode(blocks, null), modern, null);
            Assert.Equal(distant, modern[1].Position);

            List<StoredTemperature> legacy = new List<StoredTemperature>();
            ThermalStorageCodec.TryDecode(ThermalStorageCodec.EncodeLegacyBlocks(blocks), legacy, null);
            Assert.NotEqual(distant, legacy[1].Position);
        }
    }

    public class SettingsTests
    {
        [Fact]
        public void DerivedValuesFollowFrequency()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.Frequency = 10;
            settings.SimulationSpeed = 2f;
            settings.Derive();

            Assert.Equal(0.1f, settings.StepSeconds, 5);
            Assert.Equal(20f, settings.StepsPerSecond, 5);
        }

        [Fact]
        public void OutOfRangeValuesAreClamped()
        {
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
        public void ValidateReportsSuspiciousValues()
        {
            ThermalSettings settings = new ThermalSettings();
            Assert.Empty(settings.Validate());

            settings.Frequency = 240;
            Assert.NotEmpty(settings.Validate());
        }

        [Fact]
        public void CloningDoesNotShareState()
        {
            ThermalSettings settings = new ThermalSettings();
            ThermalSettings copy = settings.Clone();
            copy.Frequency = 32;

            Assert.Equal(4, settings.Frequency);
        }
    }

    public class DefinitionTests
    {
        [Fact]
        public void BlockPropertiesAreClampedToTheirRanges()
        {
            BlockThermalProperties properties = new BlockThermalProperties
            {
                Conductivity = 5f,
                Emissivity = -2f,
                SpecificHeat = 0f,
                SurfaceAreaScaler = -1f,
                CriticalTemperature = -50f,
            };

            properties.Clamp();

            Assert.Equal(1f, properties.Conductivity, 5);
            Assert.Equal(0f, properties.Emissivity, 5);
            Assert.True(properties.SpecificHeat > 0f);
            Assert.Equal(0f, properties.SurfaceAreaScaler, 5);
            Assert.Equal(0f, properties.CriticalTemperature, 5);
        }

        [Fact]
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
        public void PlanetAndLoopPropertiesClampToo()
        {
            PlanetThermalProperties planet = new PlanetThermalProperties { SolarDecay = 4f, ConvectionCoefficient = -1f };
            planet.Clamp();
            Assert.Equal(1f, planet.SolarDecay, 5);
            Assert.Equal(0f, planet.ConvectionCoefficient, 5);

            LoopThermalProperties loop = new LoopThermalProperties { MassPerPipe = 0f, Conductivity = 9f };
            loop.Clamp();
            Assert.Equal(1f, loop.MassPerPipe, 5);
            Assert.Equal(1f, loop.Conductivity, 5);
        }
    }

    public class SchedulerTests
    {
        [Fact]
        public void CreditAccumulatesAcrossShortFrames()
        {
            ThermalSettings settings = new ThermalSettings();   // 4 steps per second
            SimulationScheduler scheduler = new SimulationScheduler(settings);

            // a sixtieth of a second earns 4/60 of a step
            int total = 0;
            for (int i = 0; i < 60; i++)
            {
                total += scheduler.StepsDue(1f / 60f);
            }

            Assert.Equal(4, total);
        }

        [Fact]
        public void SimulationSpeedMultipliesTheStepRate()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.SimulationSpeed = 3f;
            settings.Derive();

            SimulationScheduler scheduler = new SimulationScheduler(settings);

            int total = 0;
            for (int i = 0; i < 60; i++) total += scheduler.StepsDue(1f / 60f, 64);

            Assert.Equal(12, total);
        }

        [Fact]
        public void ALongStallDoesNotProduceABurstOfSteps()
        {
            ThermalSettings settings = new ThermalSettings();
            SimulationScheduler scheduler = new SimulationScheduler(settings);

            int due = scheduler.StepsDue(60f, 4);

            Assert.Equal(4, due);
            Assert.Equal(0f, scheduler.Pending, 5);
        }

        [Fact]
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
