using System;
using System.Globalization;
using Thermodynamics.Core;

namespace Thermodynamics
{
    /// <summary>
    /// One debug heat-source block's dial: the watts it radiates and how far it reaches, the limits
    /// both are held to, and the string a block saves them as.
    ///
    /// <para>
    /// No <c>Sandbox.*</c> reference, so the harness links it directly — for the same reason
    /// <see cref="HeatSourceCommand"/> has none. The two halves that can be wrong in silence are
    /// here: a <b>clamp</b> that saturates a slider without saying so, and a <b>codec</b> that
    /// reloads a world at an output it was not saved at. Neither is visible from inside a session,
    /// because both ends look like a number the player typed.
    /// </para>
    ///
    /// See blocks.md, <i>Debug heat source</i>, and api.md, <i>Heat sources</i>.
    /// </summary>
    public struct HeatSourceBlockSetting
    {
        /// <summary>
        /// Least output the dial offers, W.
        ///
        /// <para>
        /// **Zero, and zero means the source is not registered at all** — not registered at zero
        /// watts. <c>ThermalHeatSources</c> costs one pass over the exposed blocks of every
        /// grid in range per sampled step, per source, and that is charged for a source making
        /// nothing exactly as for one making a gigawatt. So the bottom of this dial has to take the
        /// entry out of the registry rather than empty it, which is what
        /// <c>ThermalHeatSourceBlock.Sync</c> does with it (`P8`: off means off).
        /// </para>
        /// </summary>
        public const float MinWatts = 0f;

        /// <summary>
        /// Most output the dial offers, W. A gigawatt reaches 1,361 W/m² — one solar constant — at
        /// 242 m, so the top of the slider is *a second sun at arm's length* and the range slider
        /// is what decides whether anything is standing in it.
        /// </summary>
        public const float MaxWatts = 1000000000f;

        /// <summary>
        /// Decades of output the dial's travel spans above its floor. Six: a kilowatt is where the
        /// inverse square puts a source under a milliwatt per square metre at its own hull, and a
        /// gigawatt is a second sun.
        /// </summary>
        private const double Decades = 6.0;

        /// <summary>
        /// Watts at the bottom decade, which is what makes the curve below finite at zero.
        /// <c>Max / (10^Decades − 1)</c>, so position 1 lands exactly on <see cref="MaxWatts"/>.
        /// </summary>
        private const double Floor = MaxWatts / 999999.0;

        /// <summary>Output a freshly placed block carries, W. The figure api.md's own example uses.</summary>
        public const float DefaultWatts = 5000000f;

        /// <summary>Least reach the dial offers, m. Shorter than a large-grid ship is long.</summary>
        public const float MinRange = 10f;

        /// <summary>
        /// Most reach the dial offers, m. **A range is a cost**: every grid inside it pays one pass
        /// over its exposed blocks per sampled step, so the slider stops well short of a sync
        /// distance rather than letting one block reach a whole server.
        /// </summary>
        public const float MaxRange = 1000f;

        /// <summary>
        /// Reach a freshly placed block carries, m. The same figure <c>/thermal heat</c> places at,
        /// so the block and the command agree about what "a heat source" means.
        /// </summary>
        public const float DefaultRange = HeatSourceCommand.DefaultRange;

        /// <summary>Radiated power, W.</summary>
        public float Watts;

        /// <summary>Beyond this many metres nothing samples it.</summary>
        public float Range;

        public HeatSourceBlockSetting(float watts, float range)
        {
            Watts = watts;
            Range = range;
        }

        /// <summary>What a block carries before anybody touches its sliders.</summary>
        public static HeatSourceBlockSetting Default()
        {
            return new HeatSourceBlockSetting(DefaultWatts, DefaultRange);
        }

        /// <summary>
        /// The same setting held inside the dial's limits.
        ///
        /// Applied on the way in from every direction — a slider, the network, and a saved world —
        /// because only one of those three is bounded by the control that produced it.
        /// </summary>
        public HeatSourceBlockSetting Clamped()
        {
            return new HeatSourceBlockSetting(
                Clamp(Watts, MinWatts, MaxWatts),
                Clamp(Range, MinRange, MaxRange));
        }

        /// <summary>
        /// Clamp, with a NaN arriving as the low end rather than passing through.
        ///
        /// <c>Math.Min</c>/<c>Math.Max</c> on a NaN return the NaN, and a NaN reaching
        /// <c>ThermalHeatSources</c> makes a source that is neither above zero nor below it — it
        /// fails <c>watts &lt;= 0f</c>, registers, and then delivers a NaN irradiance into the
        /// solver. So the comparison is written the way round that rejects it.
        /// </summary>
        private static float Clamp(float value, float low, float high)
        {
            if (!(value > low)) return low;
            return value > high ? high : value;
        }

        /// <summary>
        /// The dial as a string for the block's mod storage.
        ///
        /// Versioned and invariant. The leading number is what lets a later build add a third dial
        /// without a world saved by this one becoming unreadable — <c>W1</c>, the same rule the
        /// grid codec follows.
        ///
        /// <para>
        /// <b><c>G9</c> and not <c>R</c>.</b> The round-trip specifier is not round-trippable for
        /// <c>float</c> on .NET Framework, which is what the game runs; the suite runs on a modern
        /// runtime where it is, so the one place the difference would show is the one place no test
        /// here can look. Nine significant digits is exact for every <c>float</c> on both.
        /// </para>
        /// </summary>
        public string Save()
        {
            return "1|" + Watts.ToString("G9", CultureInfo.InvariantCulture)
                + "|" + Range.ToString("G9", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Reads back what <see cref="Save"/> wrote, clamped. False on anything it does not
        /// recognise, which is what a block placed by an older build looks like.
        /// </summary>
        public static bool TryLoad(string text, out HeatSourceBlockSetting setting)
        {
            setting = Default();
            if (string.IsNullOrEmpty(text)) return false;

            string[] parts = text.Split('|');
            if (parts.Length < 3 || parts[0] != "1") return false;

            float watts;
            float range;
            if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out watts)
                || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out range))
            {
                return false;
            }

            setting = new HeatSourceBlockSetting(watts, range).Clamped();
            return true;
        }

        /// <summary>True when this setting asks for any output at all.</summary>
        public bool HasOutput
        {
            get { return Watts > 0f; }
        }

        /// <summary>
        /// Watts for a slider position, 0..1. **This is the shape of the output dial**, and it is
        /// written here rather than handed to the terminal because the terminal cannot express it:
        /// the game's own <c>SetLogLimits</c> needs a floor above zero, and a floor above zero is
        /// the one position this dial has to have.
        ///
        /// <para>
        /// <c>Floor × (10^(6p) − 1)</c> — a logarithm of one-plus, which is a log curve everywhere
        /// it matters and is *finite and exactly zero* at the bottom. So the travel is worth six
        /// decades, the way a plain log slider would be, and the far left is off rather than
        /// a kilowatt. A linear dial over the same span cannot be put on 5 MW at all: that is half
        /// a per cent of the travel.
        /// </para>
        ///
        /// <para>
        /// Rounded to the watt on the way out. A slider is a mouse position and its last few digits
        /// are noise, and an un-rounded one makes <see cref="Save"/> write nine significant figures
        /// of it into every world.
        /// </para>
        /// </summary>
        public static float WattsAtPosition(float position)
        {
            if (!(position > 0f)) return 0f;              // rejects NaN as well as the low end
            if (position >= 1f) return MaxWatts;

            double watts = Floor * (Math.Pow(10.0, position * Decades) - 1.0);
            return (float)Math.Round(watts);
        }

        /// <summary>
        /// The slider position that <see cref="WattsAtPosition"/> would turn back into these watts.
        /// The inverse of the curve above, and the getter's half of the dial.
        /// </summary>
        public static float PositionOfWatts(float watts)
        {
            if (!(watts > 0f)) return 0f;
            if (watts >= MaxWatts) return 1f;

            return (float)(Math.Log10((watts / Floor) + 1.0) / Decades);
        }

        /// <summary>
        /// The dial in the words a player reads on the terminal, e.g. <c>5.00 MW out to 200 m</c>.
        /// Invariant for the reason <see cref="HeatSourceCommand.Describe"/> is: this is a figure
        /// somebody pastes back into the chat command.
        /// </summary>
        public string Describe()
        {
            return Units.Watts(Watts, 2, CultureInfo.InvariantCulture) + " out to "
                + Range.ToString("n0", CultureInfo.InvariantCulture) + " m";
        }

        /// <summary>
        /// Watts per square metre this setting puts at <paramref name="metres"/>, for the terminal
        /// readout. The solver's own answer, through <see cref="HeatSourceMath"/>, rather than a
        /// second inverse square written beside it (<c>D3</c>).
        /// </summary>
        public float IrradianceAt(float metres)
        {
            return HeatSourceMath.Irradiance(
                new VRageMath.Vector3D(0.0, 0.0, 0.0),
                Watts,
                Range,
                new VRageMath.Vector3D(metres, 0.0, 0.0));
        }
    }
}
