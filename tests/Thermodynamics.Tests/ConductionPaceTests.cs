using Thermodynamics.Core;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **The game has two conduction paces, and they have to move together.**
    ///
    /// <para>
    /// <see cref="ThermalConstants.ConductionScale"/> sets the pace for solid conduction —
    /// block to block, and block to a bolted panel. <see cref="ThermalConstants.ReferenceConductivity"/>
    /// sets it for the coolant loop's fluid coupling, which is still a 0…1 quality value because
    /// fluid-to-wall transfer is convective and has no real conductivity to quote. Nothing in the
    /// code makes them agree, and the ratio between them is what decides whether plumbing beats
    /// bolting.
    /// </para>
    ///
    /// <para>
    /// **Measured, on the retune `C12` asks for.** Raising the solid pace ×4 and leaving the loop's
    /// alone took a coolant sink from buying **195.3 K** against the best surface dial's 41.5 K to
    /// buying **73.3 K** against 135.3 K — the mod's own headline finding, *the radiator is a block
    /// you plumb*, inverted, and nothing said so except four balance tests failing for what read
    /// like unrelated reasons. Moving the loop's pace with it recovered 73.3 K to 108.2 K and did
    /// not restore the ordering, which is a separate finding and is
    /// [balance.md](../../docs/balance.md#what-the-retune-was-measured-to-cost)'s.
    /// </para>
    ///
    /// <para>
    /// So this is not a test of a number. It is a test that a future pass which moves one pace
    /// cannot silently leave the other behind: the failure it prevents is a balance change nobody
    /// chose, in a mechanism nobody was editing.
    /// </para>
    /// </summary>
    public class ConductionPaceTests
    {
        /// <summary>
        /// The ratio the balance measurements in [balance.md](../../docs/balance.md) were taken at:
        /// a solid pace of 2.4 against a fluid reference of 200.
        /// </summary>
        private const float MeasuredRatio = 200f / 2.4f;

        [Fact]
        public void TheSolidPaceAndTheFluidPaceHaveNotDrifted()
        {
            float ratio = ThermalConstants.ReferenceConductivity / ThermalConstants.ConductionScale;

            Assert.True(
                System.Math.Abs(ratio - MeasuredRatio) < 0.001f * MeasuredRatio,
                "the fluid coupling is now " + ratio + " times the solid pace against the "
                + MeasuredRatio + " every figure in balance.md was measured at. If that is"
                + " deliberate, every cooling figure on that page wants re-deriving and this"
                + " constant wants moving with it; if it is not, a coolant loop has just become "
                + (MeasuredRatio / ratio) + " times weaker relative to the structure it competes"
                + " with, and nothing else will say so.");
        }

        /// <summary>
        /// The shipped pace itself, so a change to it is a deliberate edit here rather than a
        /// number that moved.
        ///
        /// 2.4 is what puts mild steel exactly where the old 0…1 quality value put it, which is the
        /// calibration the real-unit conversion was built around — see
        /// [definitions.md](../../docs/definitions.md).
        /// </summary>
        [Fact]
        public void TheShippedSolidPaceIsWhatTheConversionCalibratedTo()
        {
            Assert.Equal(2.4f, ThermalConstants.ConductionScale, 4);
            Assert.Equal(200f, ThermalConstants.ReferenceConductivity, 4);
        }
    }
}
