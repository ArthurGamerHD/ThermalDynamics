using System;
using System.Collections.Generic;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// **What the conversion to real conductances did to the ships people fly.**
    ///
    /// <para>
    /// `Conductivity` used to be a 0…1 quality multiplied by a 200 W/(m·K) reference. It is now the
    /// figure a materials table gives, multiplied by
    /// <see cref="ThermalConstants.ConductionScale"/>, and the calibration that made the conversion
    /// possible was that at a scale of 2.4 mild steel landed exactly where it had been: 0.6 × 200 =
    /// 120, and 50 × 2.4 = 120. Everything else moved, and what the moves did to a player's ship was
    /// argued rather than measured — backlog `C2`.
    /// </para>
    ///
    /// <para>
    /// **The pace has moved since, and the counterfactual moves with it** (`C24` took the scale to
    /// 9.6). This retest is about *flat against differentiated*, not about how fast either of them
    /// conducts, so both arms run at whatever pace ships and the flat world is pinned to mild
    /// steel's own conductance rather than to the literal 120 the old file held. Holding the
    /// counterfactual at 120 while the shipped world ran at four times that would make every arm
    /// report the retune (`P6`).
    /// </para>
    ///
    /// <para>
    /// **This is the counterfactual, and it is read off the pre-conversion file rather than
    /// guessed.** `Data/Cubes.xml` at <c>4f6b44a^</c> held twenty-two definitions and nothing else,
    /// so before the conversion every block in the game took one of exactly two conductances:
    /// </para>
    ///
    /// <list type="bullet">
    /// <item><description>
    /// <b>120 W/(m·K)</b> — the <c>EnvironmentDefinition/DefaultThermodynamics</c> fall-through at
    /// 0.6, which is every vanilla block. Derivation from build components arrived after the
    /// conversion (<c>9717e8d</c>), so before it a reactor, a thruster casing, a solar panel and an
    /// armour cube all conducted alike.
    /// </description></item>
    /// <item><description>
    /// <b>200 W/(m·K)</b> — quality 1, "the best there is", carried by <c>Thrust</c>,
    /// <c>Reactor</c>, and every one of the mod's own blocks: the twelve coolant pipes, the two
    /// coolant pumps, the two heat pumps and the two radiators.
    /// </description></item>
    /// </list>
    ///
    /// <para>
    /// So the counterfactual world is not a scale factor over the shipped one — it is a *flattening*
    /// of it, and each arm below restores one part of the flat world so the composite can be
    /// attributed. Every arm states which blocks it can reach, because an arm that reaches nothing
    /// reports "no change" in exactly the same shape as an arm that reached everything and changed
    /// nothing (`E8`).
    /// See balance.md, What the real-unit conversion moved.
    /// </para>
    /// </summary>
    public static class ConductanceRetest
    {
        /// <summary>The pre-conversion reference: quality 1 meant this many W/(m·K).</summary>
        public const float OldReference = 200f;

        /// <summary>The pre-conversion default quality, which every vanilla block fell through to.</summary>
        public const float OldDefaultQuality = 0.6f;

        /// <summary>
        /// Effective conductance every vanilla block had before the conversion, W/(m·K), at the
        /// pace that ships now: mild steel's own, which is what the flat world was calibrated to.
        /// At the 2.4 the conversion was made at this is the 120 the old file held literally.
        /// </summary>
        public static float PreDefault
        {
            get { return ReferenceMaterials.MildSteel.Conductivity * ThermalConstants.ConductionScale; }
        }

        /// <summary>
        /// Effective conductance the four quality-1 families had, W/(m·K), at the pace that ships
        /// now — the same 200/120 above the fall-through that quality 1 was.
        /// </summary>
        public static float PreBest
        {
            get { return PreDefault * (OldReference / (OldReference * OldDefaultQuality)); }
        }

        /// <summary>
        /// Authored conductivity that yields <paramref name="effective"/> once the solver applies
        /// the pace. Written once so no arm can quietly use a different pace than the mod does.
        /// </summary>
        public static float Authored(float effective)
        {
            return effective / ThermalConstants.ConductionScale;
        }

        /// <summary>The type ids that carried quality 1 before the conversion.</summary>
        private static readonly string[] BestTypes = { "Thrust", "Reactor" };

        /// <summary>
        /// Whether a subtype is one of the mod's own blocks. They all carry the author prefix, and
        /// all nineteen of them were authored at quality 1.
        /// </summary>
        public static bool IsModBlock(string subtype)
        {
            return subtype != null && subtype.StartsWith("Gauge_", StringComparison.Ordinal);
        }

        /// <summary>Whether a block was one of the quality-1 families before the conversion.</summary>
        public static bool WasBest(string typeId, string subtype)
        {
            if (IsModBlock(subtype)) return true;

            for (int i = 0; i < BestTypes.Length; i++)
            {
                if (string.Equals(typeId, BestTypes[i], StringComparison.Ordinal)) return true;
            }

            return false;
        }

        /// <summary>What every block conducted at before the conversion, W/(m·K) effective.</summary>
        public static float PreConversion(string typeId, string subtype)
        {
            return WasBest(typeId, subtype) ? PreBest : PreDefault;
        }

        /// <summary>One world to run the retest set in.</summary>
        public class World
        {
            public string Name;

            /// <summary>What this arm restores, printed with the results.</summary>
            public string Restores;

            /// <summary>
            /// True for the control, which is the world as published and the one every arm is read
            /// against. It is not a flag anyone sets — the control is exactly the world that
            /// rewrites nothing.
            /// </summary>
            public bool IsShipped
            {
                get { return Reaches == null; }
            }

            /// <summary>
            /// Which blocks this arm rewrites, or null for the control. Separate from the rewrite
            /// itself so the reach can be counted without running anything.
            /// </summary>
            public Func<string, string, bool> Reaches;

            /// <summary>The override to install, or null for the control.</summary>
            public Func<string, string, BlockThermalProperties, BlockThermalProperties> Material()
            {
                if (Reaches == null) return null;

                Func<string, string, bool> reaches = Reaches;
                return (typeId, subtype, source) =>
                {
                    if (!reaches(typeId, subtype)) return source;

                    BlockThermalProperties copy = source.Clone();
                    copy.Conductivity = Authored(PreConversion(typeId, subtype));
                    return copy;
                };
            }
        }

        /// <summary>
        /// The control, the composite, and one arm per family that moved.
        ///
        /// <para>
        /// The composite is not the sum of the arms and is not meant to be — conduction is a network
        /// and two stiffened families in series do not add. The arms are here to attribute a
        /// composite that moves, and to say plainly which of them could not act at all on this set.
        /// </para>
        /// </summary>
        public static List<World> All()
        {
            List<World> worlds = new List<World>();

            worlds.Add(new World
            {
                Name = "shipped",
                Restores = "nothing: real materials everywhere, the world as published",
            });

            worlds.Add(new World
            {
                Name = "pre-units",
                Restores = "the whole pre-conversion table: 120 everywhere, 200 for thrust, "
                    + "reactors and the mod's own blocks",
                Reaches = (typeId, subtype) => true,
            });

            worlds.Add(new World
            {
                Name = "vanilla-flat",
                Restores = "the 0.6 fall-through for every vanilla block, leaving thrust, "
                    + "reactors and the mod's blocks as shipped",
                Reaches = (typeId, subtype) => !WasBest(typeId, subtype),
            });

            worlds.Add(new World
            {
                Name = "thrust-and-reactors",
                Restores = "quality 1 for thrusters and reactors, which the conversion took to "
                    + "0.23× on an ion thruster, 0.60× on a hydrogen one and 0.52× on a reactor",
                Reaches = (typeId, subtype) =>
                    !IsModBlock(subtype) && WasBest(typeId, subtype),
            });

            worlds.Add(new World
            {
                Name = "mod-blocks",
                Restores = "quality 1 for the coolant pipes, pumps, heat pumps and radiators, "
                    + "which the conversion took to 4.8× and 2.84×",
                Reaches = (typeId, subtype) => IsModBlock(subtype),
            });

            return worlds;
        }


        // ---- what the conversion did to one block ------------------------------------------------

        /// <summary>What the conversion did to one block type.</summary>
        public class Move
        {
            public string Subtype;
            public string TypeId;

            /// <summary>Effective conductance before the conversion, W/(m·K).</summary>
            public float Before;

            /// <summary>Effective conductance now, W/(m·K).</summary>
            public float After;

            public float Ratio
            {
                get { return Before <= 0f ? 0f : After / Before; }
            }
        }

        /// <summary>
        /// The blocks worth naming in the conversion's own table, chosen to span it: the
        /// calibration point, both ends of the thruster family, the two power sources, the block
        /// that carries most of the population's waste heat, and a few ordinary hull blocks.
        ///
        /// <para>
        /// **The list is by hand and the figures are not.** A subtype the installed game does not
        /// carry comes back absent rather than faked, because these subtype ids are written here and
        /// the game renames blocks between versions — a candidate that vanished quietly would turn
        /// the table into a shorter table that still reads as complete.
        /// </para>
        /// </summary>
        private static readonly string[] Notable =
        {
            "LargeBlockArmorBlock",
            "LargeHeavyBlockArmorBlock",
            "LargeBlockLargeThrust",
            "LargeBlockLargeHydrogenThrust",
            "LargeBlockLargeAtmosphericThrust",
            "LargeBlockLargeGenerator",
            "LargeBlockBatteryBlock",
            "LargeJumpDrive",
            "LargeBlockSolarPanel",
            "LargeBlockGyro",
            "LargeBlockCockpit",
            "LargeBlockConveyor",
            "LargeBlockLargeContainer",
        };

        /// <summary>
        /// What the conversion did to each notable block, measured off the shipped definitions
        /// rather than stated. Empty when the game is not installed.
        /// </summary>
        public static List<Move> Moves()
        {
            List<Move> moves = new List<Move>();
            if (!GameBlocks.IsInstalled) return moves;

            Dictionary<string, GameBlocks.Definition> definitions = GameBlocks.BySubtype();

            for (int i = 0; i < Notable.Length; i++)
            {
                GameBlocks.Definition definition;
                if (!definitions.TryGetValue(Notable[i], out definition)) continue;

                BlockThermalProperties thermal = Blueprints.Model(definition).Thermal;

                moves.Add(new Move
                {
                    Subtype = Notable[i],
                    TypeId = definition.TypeId,
                    Before = PreConversion(definition.TypeId, Notable[i]),
                    After = thermal.Conductivity * ThermalConstants.ConductionScale,
                });
            }

            return moves;
        }

        /// <summary>Subtypes named above that the installed game does not carry.</summary>
        public static List<string> Missing()
        {
            List<string> missing = new List<string>();
            if (!GameBlocks.IsInstalled) return missing;

            Dictionary<string, GameBlocks.Definition> definitions = GameBlocks.BySubtype();
            for (int i = 0; i < Notable.Length; i++)
            {
                if (!definitions.ContainsKey(Notable[i])) missing.Add(Notable[i]);
            }

            return missing;
        }
    }
}
