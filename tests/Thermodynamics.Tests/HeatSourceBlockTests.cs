using Thermodynamics;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The debug heat source block's dial, which is the half of that block a session cannot check.
    ///
    /// <para>
    /// The block itself is two sliders and a call into <c>ThermalHeatSources</c>, and whether it
    /// radiates is answered by looking at it. What cannot be answered by looking is whether the
    /// number under the slider is the number the player set: a <b>clamp</b> that saturates without
    /// saying so and a <b>codec</b> that reloads a world at a different output both look exactly
    /// like a correct block, because the readout echoes whatever the dial ended up holding.
    /// </para>
    ///
    /// <para>
    /// The third thing pinned here is that the block and <c>/thermal heat</c> agree about what a
    /// heat source is. They are two ways of driving one registry and each has its own defaults; a
    /// pair like that is the drift <c>wired-to-nothing</c> keeps finding, so the agreement is
    /// asserted rather than assumed.
    /// </para>
    /// </summary>
    public class HeatSourceBlockTests
    {
        [Fact]
        public void TheDefaultDialSitsInsideItsOwnLimits()
        {
            HeatSourceBlockSetting setting = HeatSourceBlockSetting.Default();

            Assert.InRange(setting.Watts, HeatSourceBlockSetting.MinWatts, HeatSourceBlockSetting.MaxWatts);
            Assert.InRange(setting.Range, HeatSourceBlockSetting.MinRange, HeatSourceBlockSetting.MaxRange);

            // Clamping the default must not move it, or a freshly placed block is already not what
            // the constant says it is.
            Assert.Equal(setting.Watts, setting.Clamped().Watts);
            Assert.Equal(setting.Range, setting.Clamped().Range);
        }

        /// <summary>
        /// **The block places what the chat command places.** Two entry points onto one registry,
        /// and a player who reads `/thermal heat list` after building one should not find a
        /// different reach than the command would have given them.
        /// </summary>
        [Fact]
        public void TheBlockAndTheChatCommandAgreeOnWhatAHeatSourceReaches()
        {
            Assert.Equal(HeatSourceCommand.DefaultRange, HeatSourceBlockSetting.DefaultRange);
        }

        [Fact]
        public void BothDialsAreHeldInsideTheirLimits()
        {
            HeatSourceBlockSetting low = new HeatSourceBlockSetting(-5f, -5f).Clamped();
            Assert.Equal(HeatSourceBlockSetting.MinWatts, low.Watts);
            Assert.Equal(HeatSourceBlockSetting.MinRange, low.Range);
            Assert.Equal(0f, low.Watts);

            HeatSourceBlockSetting high = new HeatSourceBlockSetting(1e12f, 1e12f).Clamped();
            Assert.Equal(HeatSourceBlockSetting.MaxWatts, high.Watts);
            Assert.Equal(HeatSourceBlockSetting.MaxRange, high.Range);

            HeatSourceBlockSetting inside = new HeatSourceBlockSetting(2.5e6f, 350f).Clamped();
            Assert.Equal(2.5e6f, inside.Watts);
            Assert.Equal(350f, inside.Range);
        }

        /// <summary>
        /// **A NaN arrives as the bottom of the dial, not as a NaN.**
        ///
        /// This is the reason the clamp is written with its comparisons the way round they are.
        /// <c>Math.Max(NaN, low)</c> returns the NaN, and a NaN reaching the registry passes
        /// <c>watts &lt;= 0f</c> — so it registers as a live source and then puts a NaN irradiance
        /// into the solver, where it spreads to every node the step touches and none of it looks
        /// like a bad slider.
        /// </summary>
        [Fact]
        public void ANotANumberIsRejectedRatherThanPassedThrough()
        {
            HeatSourceBlockSetting setting =
                new HeatSourceBlockSetting(float.NaN, float.NaN).Clamped();

            Assert.False(float.IsNaN(setting.Watts));
            Assert.False(float.IsNaN(setting.Range));
            Assert.Equal(HeatSourceBlockSetting.MinWatts, setting.Watts);
            Assert.Equal(HeatSourceBlockSetting.MinRange, setting.Range);
        }


        /// <summary>
        /// **Zero watts is a setting, and it is the one that costs nothing.**
        ///
        /// <para>
        /// A registered source is charged one pass over the exposed blocks of every grid in its
        /// range, every sampled step, before anything looks at how many watts it carries — so
        /// leaving a zero-watt source in the registry would be a block that had been turned down to
        /// nothing and still cost the same. <c>HasOutput</c> is what <c>Sync</c> reads to take the
        /// entry out instead, and this is the assertion under that.
        /// </para>
        /// </summary>
        [Fact]
        public void ADialAtZeroAsksForNoSourceAtAll()
        {
            Assert.False(new HeatSourceBlockSetting(0f, 200f).HasOutput);
            Assert.False(new HeatSourceBlockSetting(-1f, 200f).Clamped().HasOutput);
            Assert.False(new HeatSourceBlockSetting(float.NaN, 200f).Clamped().HasOutput);

            Assert.True(new HeatSourceBlockSetting(1f, 200f).HasOutput);
            Assert.True(HeatSourceBlockSetting.Default().HasOutput);
        }

        /// <summary>
        /// The output dial reaches both ends exactly. The bottom is the interesting one: the curve
        /// is a logarithm, and a logarithm that merely gets *close* to zero would leave the block
        /// permanently registered — costing a pass per grid per step to deliver microwatts, with
        /// the slider hard against the stop and the player believing it was off.
        /// </summary>
        [Fact]
        public void TheOutputDialReachesZeroAndItsMaximumExactly()
        {
            Assert.Equal(0f, HeatSourceBlockSetting.WattsAtPosition(0f));
            Assert.Equal(HeatSourceBlockSetting.MaxWatts, HeatSourceBlockSetting.WattsAtPosition(1f));

            // And the far ends of the travel are not somewhere in the middle of the curve.
            Assert.Equal(0f, HeatSourceBlockSetting.PositionOfWatts(0f));
            Assert.Equal(1f, HeatSourceBlockSetting.PositionOfWatts(HeatSourceBlockSetting.MaxWatts));

            // A position outside the slider's own range is still one of the two ends.
            Assert.Equal(0f, HeatSourceBlockSetting.WattsAtPosition(-1f));
            Assert.Equal(HeatSourceBlockSetting.MaxWatts, HeatSourceBlockSetting.WattsAtPosition(2f));
            Assert.Equal(0f, HeatSourceBlockSetting.WattsAtPosition(float.NaN));
        }

        /// <summary>
        /// Position and watts are inverses of each other, which is what makes the slider stay where
        /// it was put: the setter converts one way and the getter the other, every frame the panel
        /// draws. A curve that did not round-trip would show a dial creeping while nobody touched it.
        /// </summary>
        [Theory]
        [InlineData(0f)]
        [InlineData(0.1f)]
        [InlineData(0.25f)]
        [InlineData(0.5f)]
        [InlineData(0.75f)]
        [InlineData(0.9f)]
        [InlineData(1f)]
        public void APositionSurvivesBeingTurnedIntoWattsAndBack(float position)
        {
            float watts = HeatSourceBlockSetting.WattsAtPosition(position);

            Assert.Equal(position, HeatSourceBlockSetting.PositionOfWatts(watts), 3);
        }

        /// <summary>
        /// The dial rises the whole way and never doubles back, and it spends its travel where a
        /// person setting a debug load actually works. The mid-point figure is the claim worth
        /// pinning: **half the travel is a megawatt**, not five hundred, which is the difference
        /// between this curve and the linear one it replaced.
        /// </summary>
        [Fact]
        public void TheDialSpendsItsTravelWhereTheUsefulOutputsAre()
        {
            float previous = -1f;
            for (int i = 0; i <= 100; i++)
            {
                float watts = HeatSourceBlockSetting.WattsAtPosition(i / 100f);
                Assert.True(watts > previous, "the dial doubled back at position " + (i / 100f));
                previous = watts;
            }

            // Six decades of travel: a tenth of the way up is kilowatts, half way is a megawatt.
            Assert.InRange(HeatSourceBlockSetting.WattsAtPosition(0.1f), 2.9e3f, 3.1e3f);
            Assert.InRange(HeatSourceBlockSetting.WattsAtPosition(0.5f), 0.99e6f, 1.01e6f);

            // The default is reachable and sits near the middle, where a slider is easy to place.
            float defaultPosition = HeatSourceBlockSetting.PositionOfWatts(
                HeatSourceBlockSetting.DefaultWatts);
            Assert.InRange(defaultPosition, 0.5f, 0.75f);
        }

        /// <summary>
        /// A saved world reopens at the output it was saved at. Round-tripped at both ends of the
        /// dial and at an awkward value in between, because the codec writes text and a rounded
        /// magnitude would survive the default and lose 5.37 MW.
        /// </summary>
        [Theory]
        [InlineData(0f, 10f)]
        [InlineData(1000f, 10f)]
        [InlineData(5000000f, 200f)]
        [InlineData(5371234.5f, 337f)]
        [InlineData(1000000000f, 1000f)]
        public void TheDialSurvivesASaveAndReload(float watts, float range)
        {
            HeatSourceBlockSetting saved = new HeatSourceBlockSetting(watts, range);

            HeatSourceBlockSetting loaded;
            Assert.True(HeatSourceBlockSetting.TryLoad(saved.Save(), out loaded));

            Assert.Equal(watts, loaded.Watts);
            Assert.Equal(range, loaded.Range);
        }

        /// <summary>
        /// Anything the codec does not recognise leaves the block at its default rather than at
        /// zero. A block that loaded as zero watts would be a block that had quietly switched
        /// itself off, and the storage dictionary is shared with every other mod on the entity.
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("garbage")]
        [InlineData("2|5000000|200")]
        [InlineData("1|5000000")]
        [InlineData("1|not-a-number|200")]
        public void AnUnreadableSaveLeavesTheBlockAtItsDefault(string stored)
        {
            HeatSourceBlockSetting loaded;
            Assert.False(HeatSourceBlockSetting.TryLoad(stored, out loaded));

            Assert.Equal(HeatSourceBlockSetting.DefaultWatts, loaded.Watts);
            Assert.Equal(HeatSourceBlockSetting.DefaultRange, loaded.Range);
        }

        /// <summary>
        /// A save carrying a figure outside the dial — a hand-edited world, or a build whose limits
        /// were wider — loads clamped rather than loading as written. The slider could not have
        /// produced it and the registry should not receive it.
        /// </summary>
        [Fact]
        public void ASaveFromOutsideTheDialLoadsClamped()
        {
            HeatSourceBlockSetting loaded;
            Assert.True(HeatSourceBlockSetting.TryLoad("1|1E+15|999999", out loaded));

            Assert.Equal(HeatSourceBlockSetting.MaxWatts, loaded.Watts);
            Assert.Equal(HeatSourceBlockSetting.MaxRange, loaded.Range);
        }

        /// <summary>
        /// The terminal's irradiance line is the solver's own answer and not a second inverse square
        /// written beside it (<c>D3</c>) — including the range cutoff, which is the half a
        /// reimplementation would forget and which turns the readout into a lie exactly where the
        /// player is trying to find the edge of the effect.
        /// </summary>
        [Fact]
        public void TheReadoutsIrradianceIsTheSolversOwn()
        {
            HeatSourceBlockSetting setting = new HeatSourceBlockSetting(5e6f, 200f);

            Assert.Equal(
                HeatSourceMath.Irradiance(VRageMath.Vector3D.Zero, 5e6f, 200f, new VRageMath.Vector3D(50.0, 0.0, 0.0)),
                setting.IrradianceAt(50f));

            // Beyond the reach, nothing — a cliff, which is what the range is.
            Assert.Equal(0f, setting.IrradianceAt(201f));
        }

        /// <summary>
        /// The figure the slider's tooltip quotes. Pinned because a tooltip is prose and drifts
        /// silently, and this one is the only place a player is told what the dial converts to.
        /// </summary>
        [Fact]
        public void AMegawattIsTwoWattsPerSquareMetreAtTwoHundredMetres()
        {
            HeatSourceBlockSetting setting = new HeatSourceBlockSetting(1e6f, 1000f);

            Assert.Equal(2f, setting.IrradianceAt(200f), 1);
            Assert.Equal(78f, setting.IrradianceAt(32f), 0);
        }
    }
}
