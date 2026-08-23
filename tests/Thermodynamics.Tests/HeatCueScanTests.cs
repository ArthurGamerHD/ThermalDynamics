using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The scan that turns a grid's temperatures into cues, and the two things it must not do:
    /// cost anything on a cold hull, and repeat itself on a hot one.
    /// </summary>
    public class HeatCueScanTests
    {
        /// <summary>A hull with nothing hot on it produces nothing, and remembers nothing.</summary>
        [Fact]
        public void AColdHullProducesNoCuesAndRemembersNothing()
        {
            Rig rig = Rig.Build(300f);
            List<HeatCue> cues = new List<HeatCue>();

            rig.Simulation.Solver.CollectHeatCues(rig.State, 0.25f, cues);

            Assert.Empty(cues);
            Assert.Equal(0, rig.State.Count);
        }

        /// <summary>
        /// The floor a cold hull is compared against is the lower of where the coolest block starts
        /// glowing and where it starts being watched for a warning.
        ///
        /// <para>
        /// **For every block the game ships the watch point is the one that binds**, and the
        /// arithmetic says where that stops: the two cross at a rating of 400 K, and the
        /// lowest-rated type in the installed game is 583 K. The glow's start is the floor only for
        /// a definition rated under that, which is why both branches are exercised here with one
        /// rating the game has and one it does not.
        /// </para>
        /// </summary>
        [Fact]
        public void TheFloorIsTheLowerOfTheGlowStartAndTheWatchPoint()
        {
            Rig shipped = Rig.Build(300f, 900f);
            Assert.Equal(900f * HeatCueState.WatchFraction,
                shipped.Simulation.Solver.CueFloorTemperature(), 1);
            Assert.True(900f * HeatCueState.WatchFraction < Incandescence.GlowStartKelvin(900f));

            Rig fragile = Rig.Build(200f, 300f);
            Assert.Equal(Incandescence.GlowStartKelvin(300f),
                fragile.Simulation.Solver.CueFloorTemperature(), 1);

            Assert.True(fragile.Simulation.Solver.CueFloorTemperature()
                < shipped.Simulation.Solver.CueFloorTemperature(),
                "a grid of low-rated blocks should start looking sooner");
        }

        /// <summary>
        /// The lowest critical temperature is the coolest block's, and it is what the floor is
        /// built from.
        /// </summary>
        [Fact]
        public void TheLowestCriticalTemperatureIsTheCoolestBlocks()
        {
            Rig rig = Rig.Build(300f, 1400f, 700f, 1);
            Assert.Equal(700f, rig.Simulation.Solver.LowestCriticalTemperature, 1);
        }

        /// <summary>A block in the watched band is reported, with its glow and its stage.</summary>
        [Fact]
        public void AGlowingBlockIsReportedWithItsColourTemperature()
        {
            Rig rig = Rig.Build(1000f);
            List<HeatCue> cues = new List<HeatCue>();

            rig.Simulation.Solver.CollectHeatCues(rig.State, 0.25f, cues);

            Assert.Single(cues);
            Assert.Equal(1000f, cues[0].Kelvin, 0);
            Assert.Equal(Incandescence.Glow(1000f, 900f), cues[0].Glow, 4);
            Assert.Equal(HeatCueStage.Critical, cues[0].Stage);
            Assert.True(cues[0].Announce, "the first scan of a block already over should announce");
        }

        /// <summary>
        /// **The glow is a state and the sound is an event.** A block sitting over its rating keeps
        /// glowing and stops announcing, which is the difference the <c>Announce</c> flag carries.
        /// </summary>
        [Fact]
        public void AStageAnnouncesOnceRatherThanEveryScan()
        {
            Rig rig = Rig.Build(1000f);
            List<HeatCue> cues = new List<HeatCue>();

            rig.Simulation.Solver.CollectHeatCues(rig.State, 0.25f, cues);
            Assert.True(cues[0].Announce);

            for (int i = 0; i < 5; i++)
            {
                cues.Clear();
                rig.Simulation.Solver.CollectHeatCues(rig.State, 0.25f, cues);

                Assert.Single(cues);
                Assert.Equal(HeatCueStage.Critical, cues[0].Stage);
                Assert.False(cues[0].Announce, "scan " + i + " announced again");
                Assert.True(cues[0].Glow > 0f, "and it is still glowing");
            }
        }

        /// <summary>
        /// A block falling back out of trouble does not announce it. Coming down is not news, and a
        /// hull hovering on a threshold would otherwise chirp on every scan.
        /// </summary>
        [Fact]
        public void FallingBackDoesNotAnnounce()
        {
            Rig rig = Rig.Build(1000f);
            List<HeatCue> cues = new List<HeatCue>();

            rig.Simulation.Solver.CollectHeatCues(rig.State, 0.25f, cues);
            Assert.Equal(HeatCueStage.Critical, cues[0].Stage);

            rig.Hot.Temperature = 800f;
            rig.Step();
            cues.Clear();
            rig.Simulation.Solver.CollectHeatCues(rig.State, 0.25f, cues);

            Assert.Single(cues);
            Assert.NotEqual(HeatCueStage.Critical, cues[0].Stage);
            Assert.False(cues[0].Announce);
        }

        /// <summary>
        /// A block that cools out of the watched band is forgotten, so the state a grid carries is
        /// bounded by what is hot rather than by how much has ever been hot.
        /// </summary>
        [Fact]
        public void ABlockThatCoolsIsForgotten()
        {
            Rig rig = Rig.Build(1000f);
            List<HeatCue> cues = new List<HeatCue>();

            rig.Simulation.Solver.CollectHeatCues(rig.State, 0.25f, cues);
            Assert.Equal(1, rig.State.Count);

            rig.Hot.Temperature = 300f;
            rig.Step();
            cues.Clear();
            rig.Simulation.Solver.CollectHeatCues(rig.State, 0.25f, cues);

            Assert.Empty(cues);
            Assert.Equal(0, rig.State.Count);
        }

        /// <summary>
        /// **A block rated below the Draper point still glows.** This is the quarter of the game a
        /// physical brightness would never reach: at 560 K against a 600 K rating it is forty
        /// kelvin from failing, where nothing real emits any light, and it is bright.
        /// </summary>
        [Fact]
        public void ABlockRatedBelowTheDraperPointStillGlows()
        {
            Rig rig = Rig.Build(560f, 600f);
            List<HeatCue> cues = new List<HeatCue>();

            rig.Simulation.Solver.CollectHeatCues(rig.State, 0.25f, cues);

            Assert.Single(cues);
            Assert.True(cues[0].Kelvin < Incandescence.DraperKelvin);
            Assert.Equal(0.6f, cues[0].Glow, 3);
        }

        /// <summary>
        /// An ordinary hull at an ordinary temperature glows nothing, however many blocks it has —
        /// and the grid-level floor is what says so, in one comparison.
        /// </summary>
        [Fact]
        public void AHullAtRoomTemperatureGlowsNothing()
        {
            Rig rig = Rig.Build(295f, 900f, 900f, 4);
            List<HeatCue> cues = new List<HeatCue>();

            rig.Simulation.Solver.CollectHeatCues(rig.State, 0.25f, cues);

            Assert.Empty(cues);
            Assert.True(rig.Simulation.Solver.HottestNode().Temperature
                < rig.Simulation.Solver.CueFloorTemperature(),
                "the grid-level floor did not cover an ordinary hull");
        }

        /// <summary>
        /// One block on a grid of five hundred is one cue, which is the shape the cost argument
        /// rests on: the work after the floor test is bounded by what is hot, not by the hull.
        /// </summary>
        [Fact]
        public void OnlyTheHotBlockIsWalkedTwice()
        {
            Rig rig = Rig.Build(1000f, 900f, 900f, 8);
            List<HeatCue> cues = new List<HeatCue>();

            rig.Simulation.Solver.CollectHeatCues(rig.State, 0.25f, cues);

            Assert.True(rig.Simulation.Solver.Nodes.Count > 100,
                "the rig is meant to be a hull rather than a handful of blocks");
            Assert.Single(cues);
            Assert.Equal(1, rig.State.Count);
        }

        /// <summary>One hot block in a cold hull, with the cue state that goes with it.</summary>
        private class Rig
        {
            public ThermalSimulation Simulation;
            public ThermalNode Hot;
            public HeatCueState State = new HeatCueState();

            public static Rig Build(float kelvin)
            {
                return Build(kelvin, 900f);
            }

            public static Rig Build(float kelvin, float critical)
            {
                return Build(kelvin, critical, critical, 1);
            }

            /// <summary>
            /// One block held at <paramref name="kelvin"/> with a rating of
            /// <paramref name="critical"/>, surrounded by a cube of cold armour rated
            /// <paramref name="hullCritical"/> and <paramref name="radius"/> cells across.
            /// </summary>
            public static Rig Build(float kelvin, float critical, float hullCritical, int radius)
            {
                // **Every transport switched off**, because this file is about the scan rather
                // than about the physics: with nothing moving heat, a block sits exactly where it
                // is put and every figure below is the one the test chose. HeatWarningTests is
                // where the same code is asked about a grid that is actually running.
                ThermalSettings settings = new ThermalSettings();
                settings.EnableDamage = false;
                settings.EnableConduction = false;
                settings.EnableRadiation = false;
                settings.EnableConvection = false;
                settings.EnableEnvironment = false;
                settings.Derive();

                BlockThermalProperties source = Catalog.DefaultThermal();
                source.CriticalTemperature = critical;

                BlockThermalProperties hull = Catalog.DefaultThermal();
                hull.CriticalTemperature = hullCritical;

                GridBuilder builder = GridBuilder.Large();
                builder.Place(BlockModel.Solid("source", Vector3I.One, 1000f, source), Vector3I.Zero);

                BlockModel armour = BlockModel.Solid("hull", Vector3I.One, 500f, hull);
                for (int x = -radius; x <= radius; x++)
                {
                    for (int y = -radius; y <= radius; y++)
                    {
                        for (int z = -radius; z <= radius; z++)
                        {
                            if (x == 0 && y == 0 && z == 0) continue;
                            builder.Place(armour, new Vector3I(x, y, z));
                        }
                    }
                }

                Rig rig = new Rig();
                rig.Simulation = builder.BuildSimulation(settings, 60f);
                rig.Hot = rig.Simulation.Solver.GetNode(builder.Placed[0]);

                // Every node starts at the world's own temperature; only the one under test is
                // moved, and it is moved rather than driven so the scan is measured against a
                // temperature this file chose.
                for (int i = 0; i < rig.Simulation.Solver.Nodes.Count; i++)
                {
                    rig.Simulation.Solver.Nodes[i].Temperature = 300f;
                }

                rig.Hot.Temperature = kelvin;
                rig.Step();
                return rig;
            }

            /// <summary>
            /// Runs one solver step, which is what copies the node temperatures into the flat
            /// arrays the scan reads.
            ///
            /// **The scan reads the step's own arrays and so has to run after a step**, which is
            /// exactly where the game pass runs it — see ThermalGridCues. Reading the node objects
            /// instead would cost a dereference per block on a hull where nothing is hot, which is
            /// the one cost this feature is not allowed to have.
            /// </summary>
            public void Step()
            {
                Simulation.Update(Simulation.Settings.StepSeconds, Worlds.Shadow());
            }
        }
    }
}
