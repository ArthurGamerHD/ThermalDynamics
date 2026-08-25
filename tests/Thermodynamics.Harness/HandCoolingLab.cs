using System;
using System.Collections.Generic;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// **What a hand tool would have to be worth to save a block that is cooking.**
    ///
    /// <para>
    /// backlog.md `B32` is a void rather than a defect: the mod ships an
    /// extinguisher that scans and does not extinguish, and nothing anywhere states whether a
    /// player should be able to act on heat with their hands at all. The intent page cannot state
    /// a position on that without a number, because *hand-scale action is out of scope* and *hand
    /// -scale action was never costed* read identically from outside. This is the number.
    /// </para>
    ///
    /// <para>
    /// **The question is asked in watts and joules, which is the one framing the clock cannot
    /// move.** `HeatTimeScale` divides every heat capacity, so a kelvin is cheaper by that factor —
    /// but it is cheaper for the block's own waste heat and for a tool's cooling alike, and the
    /// ratio between them is what decides whether a tool is worth carrying. So every figure here is
    /// physical: watts of waste against watts of cooling, and joules of stored heat against joules
    /// a bottle can absorb. Nothing below scales with the dial.
    /// </para>
    ///
    /// <para>
    /// **Every term is the block's own best case**, taken from <see cref="BlockHeatIndex"/>: its
    /// skin radiating to deep space from every face and every face bolted to armour held at ambient
    /// for ever. A real block in a real hull sheds less than this, so the surplus a tool has to
    /// carry is a **lower bound**. That direction is deliberate — a case that hand-scale cooling
    /// cannot work is only worth making against the most favourable arithmetic available (`P2`).
    /// </para>
    /// </summary>
    public static class HandCoolingLab
    {
        /// <summary>
        /// The reference tool: a hand-carried CO2 bottle, at the size a person can hold.
        ///
        /// **Five kilograms**, which is the largest portable extinguisher in ordinary use. The
        /// mod's own `Bottle` ammo has no mass, no capacity and no stated contents, so the
        /// comparison is made against a real object rather than against an authored one — an
        /// authored number would make the answer a property of a value nobody has defended.
        /// </summary>
        public const float BottleKilograms = 5f;

        /// <summary>
        /// Heat one kilogram of CO2 absorbs going from a pressurised liquid to gas at room
        /// temperature: 571 kJ/kg of sublimation plus about 89 kJ/kg warming the gas from 195 K to
        /// 300 K at 0.85 kJ/(kg·K).
        ///
        /// **Real physics, quoted so it can be checked**, and generous: it assumes every gram
        /// reaches the block and none of it is lost to the room, which is not how an extinguisher
        /// works.
        /// </summary>
        public const float BottleJoulesPerKilogram = 660000f;

        /// <summary>
        /// Seconds a 5 kg extinguisher discharges for, wide open. Real bottles of that size run
        /// 10 to 20 seconds; the longer figure is used, because it makes the tool's *watts* smaller
        /// and that is the number the case against it rests on being small.
        /// </summary>
        public const float BottleSeconds = 20f;

        /// <summary>Joules the whole bottle absorbs.</summary>
        public static float BottleJoules
        {
            get { return BottleKilograms * BottleJoulesPerKilogram; }
        }

        /// <summary>Watts the bottle removes while it is discharging.</summary>
        public static float BottleWatts
        {
            get { return BottleJoules / BottleSeconds; }
        }

        /// <summary>What one block type would cost to save by hand.</summary>
        public class Price
        {
            public string Subtype;

            /// <summary>Waste watts at full rating.</summary>
            public float WasteWatts;

            /// <summary>
            /// Watts a tool must remove to stop the block getting hotter once it is at its own
            /// critical temperature — its waste less everything its own best case sheds there.
            ///
            /// **This is the floor and not the job.** Removing exactly this holds a block at the
            /// temperature it fails at; a player wants it lower.
            /// </summary>
            public float HoldWatts;

            /// <summary>
            /// Physical joules stored in the block at critical, above ambient: the real heat
            /// capacity — the simulated one times <c>HeatTimeScale</c> — over the whole rise.
            ///
            /// A tool that carries this much and delivers it instantly returns the block to
            /// ambient. It is the other half of the price, and the one a bottle is measured in.
            /// </summary>
            public float ReturnJoules;

            /// <summary>
            /// Simulated seconds from the crossing to the block being destroyed, alone in the dark.
            /// Infinite for a block that never finishes dying, which is most of them.
            /// </summary>
            public float WindowSeconds;

            /// <summary>Whole bottles it takes to hold this block for its own window.</summary>
            public float BottlesToHoldTheWindow
            {
                get
                {
                    if (HoldWatts <= 0f) return 0f;
                    if (float.IsInfinity(WindowSeconds)) return float.PositiveInfinity;

                    return HoldWatts * WindowSeconds / BottleJoules;
                }
            }

            /// <summary>Whole bottles it takes to return this block to ambient, ignoring its waste.</summary>
            public float BottlesToReturn
            {
                get { return ReturnJoules / BottleJoules; }
            }

            /// <summary>Whether one bottle, wide open, out-cools the block's surplus at all.</summary>
            public bool OneBottleHolds
            {
                get { return HoldWatts <= BottleWatts; }
            }

            /// <summary>
            /// Watts a tool must deliver to take the block from its failure temperature back to
            /// ambient inside the window it has left — the stored heat over the window, plus the
            /// waste still arriving.
            ///
            /// **This is the honest statement of the job**, because a player is not trying to hold
            /// a block at the temperature it fails at; they are trying to undo a crossing before
            /// the block is gone. Infinite window means the block dies too slowly to be worth
            /// saving, so the figure is the surplus alone.
            /// </summary>
            public float ReturnWattsInWindow
            {
                get
                {
                    if (float.IsInfinity(WindowSeconds) || WindowSeconds <= 0f) return HoldWatts;
                    return HoldWatts + (ReturnJoules / WindowSeconds);
                }
            }

            /// <summary>How many bottles discharging at once it would take to do that.</summary>
            public float BottlesAtOnce
            {
                get { return ReturnWattsInWindow / BottleWatts; }
            }
        }

        /// <summary>
        /// Every block that reaches its own critical temperature under its own waste heat, priced.
        ///
        /// **Blocks that never cross are left out rather than priced at zero**, because a tool that
        /// is not needed is not evidence about a tool that is: including them would put a majority
        /// of costless rows into every percentile and make the answer a statement about how many
        /// block types the game has (`E9`).
        /// </summary>
        public static List<Price> All()
        {
            List<Price> prices = new List<Price>();

            foreach (BlockHeatIndex.Reading reading in BlockHeatIndex.All())
            {
                if (float.IsInfinity(reading.SecondsToCritical)) continue;
                if (reading.Watts <= 0f) continue;

                float shed = reading.RadiatedWatts + reading.ConductedWatts;
                float hold = reading.Watts - shed;
                if (hold < 0f) hold = 0f;

                float physical = reading.HeatCapacity * BlockHeatIndex.PaceHeatTimeScale;

                prices.Add(new Price
                {
                    Subtype = reading.Subtype,
                    WasteWatts = reading.Watts,
                    HoldWatts = hold,
                    ReturnJoules = physical
                        * (reading.CriticalKelvin - BlockHeatIndex.AmbientKelvin),
                    WindowSeconds = reading.SecondsCriticalToLoss
                });
            }

            prices.Sort(delegate (Price a, Price b) { return b.HoldWatts.CompareTo(a.HoldWatts); });
            return prices;
        }

        /// <summary>
        /// The value at a quantile of a sorted-descending list, by nearest rank from the cheap end.
        ///
        /// Written here rather than borrowed so that the direction is explicit: `Quantile(0.5)` is
        /// the median block and `Quantile(0.99)` is the one a tool would struggle with.
        /// </summary>
        public static float Quantile(List<float> ascending, double quantile)
        {
            if (ascending.Count == 0) return 0f;

            int index = (int)Math.Ceiling(quantile * ascending.Count) - 1;
            if (index < 0) index = 0;
            if (index >= ascending.Count) index = ascending.Count - 1;

            return ascending[index];
        }
    }
}
