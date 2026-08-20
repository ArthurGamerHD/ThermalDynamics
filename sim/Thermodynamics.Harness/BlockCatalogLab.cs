using System;
using System.Collections.Generic;
using System.Text;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// The definition pass: what <see cref="BlockThermalDerivation"/> makes of every block in the
    /// game, and which individual blocks differ enough from their type to deserve an entry.
    ///
    /// Run it with `dotnet run --project Thermodynamics.Sim -- blocks`, and
    /// `-- blocks --xml` to emit the `Data/Cubes.xml` body it implies.
    /// </summary>
    public static class BlockCatalogLab
    {
        /// <summary>
        /// How far a subtype has to sit from its type's fallback before it is worth naming.
        ///
        /// The units differ per property, so each is a *relative* difference against the type
        /// figure, except the temperatures, which are compared in kelvin because a ratio near
        /// absolute zero is meaningless. Set so that a window against an armour block is far past
        /// the line and two armour blocks of different thickness are nowhere near it.
        /// </summary>
        public const float RelativeThreshold = 0.25f;

        /// <summary>Kelvin of critical-temperature difference worth naming a subtype for.</summary>
        public const float KelvinThreshold = 120f;

        public class TypeRow
        {
            public string TypeId;
            public int Subtypes;
            public BlockThermalProperties Properties;

            /// <summary>Subtypes that deviate far enough to earn an entry of their own.</summary>
            public List<OverrideRow> Overrides = new List<OverrideRow>();

            /// <summary>The heaviest components of the type, as a readable share string.</summary>
            public string Composition;
        }

        public class OverrideRow
        {
            public string SubtypeId;
            public BlockThermalProperties Properties;
            public string Reason;
        }

        /// <summary>
        /// Every type in the installed game with its derived fallback, and the subtypes under it
        /// that deviate. Empty when the game is not installed.
        /// </summary>
        public static List<TypeRow> Catalog()
        {
            List<TypeRow> rows = new List<TypeRow>();

            foreach (KeyValuePair<string, List<GameBlocks.Definition>> type in GameBlocks.ByType())
            {
                List<BlockComponent> components = GameBlocks.TypeComponents(type.Value);

                TypeRow row = new TypeRow
                {
                    TypeId = type.Key,
                    Subtypes = type.Value.Count,
                    Properties = BlockThermalDerivation.Derive(components, type.Key),
                    Composition = Composition(components),
                };

                foreach (GameBlocks.Definition block in type.Value)
                {
                    if (block.Components.Count == 0) continue;

                    BlockThermalProperties own = BlockThermalDerivation.Derive(block.Components, type.Key);
                    string reason = Deviation(row.Properties, own);
                    if (reason == null) continue;

                    row.Overrides.Add(new OverrideRow
                    {
                        SubtypeId = block.SubtypeId,
                        Properties = own,
                        Reason = reason,
                    });
                }

                rows.Add(row);
            }

            rows.Sort(delegate (TypeRow a, TypeRow b) { return b.Subtypes.CompareTo(a.Subtypes); });
            return rows;
        }

        /// <summary>
        /// Why a subtype differs from its type, or null when it does not differ enough to matter.
        ///
        /// Only the *material* half is compared. The functional half comes from the type table and
        /// is identical by construction for every subtype of a type, so comparing it would report
        /// nothing and hide the comparison that counts.
        /// </summary>
        private static string Deviation(BlockThermalProperties type, BlockThermalProperties own)
        {
            List<string> reasons = new List<string>();

            if (Differs(type.Conductivity, own.Conductivity)) reasons.Add("conductivity");
            if (Differs(type.SpecificHeat, own.SpecificHeat)) reasons.Add("specific heat");
            if (Differs(type.Emissivity, own.Emissivity)) reasons.Add("emissivity");

            if (Math.Abs(type.CriticalTemperature - own.CriticalTemperature) >= KelvinThreshold)
                reasons.Add("critical temperature");

            return reasons.Count == 0 ? null : string.Join(", ", reasons.ToArray());
        }

        private static bool Differs(float type, float own)
        {
            float reference = Math.Max(Math.Abs(type), 1e-6f);
            return Math.Abs(own - type) / reference >= RelativeThreshold;
        }

        private static string Composition(IList<BlockComponent> components)
        {
            float total = 0f;
            for (int i = 0; i < components.Count; i++) total += components[i].Mass;
            if (total <= 0f) return "(no priced components)";

            List<BlockComponent> sorted = new List<BlockComponent>(components);
            sorted.Sort(delegate (BlockComponent a, BlockComponent b) { return b.Mass.CompareTo(a.Mass); });

            StringBuilder sb = new StringBuilder();
            int take = sorted.Count < 3 ? sorted.Count : 3;
            for (int i = 0; i < take; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(sorted[i].Component).Append(' ')
                    .Append((sorted[i].Mass / total * 100f).ToString("n0")).Append('%');
            }
            return sb.ToString();
        }

        public static string Report()
        {
            StringBuilder sb = new StringBuilder();

            if (!GameBlocks.IsInstalled)
            {
                sb.AppendLine("Space Engineers is not installed here, so there is nothing to derive from.");
                sb.AppendLine("Set SE_BIN to the game's Bin64 directory.");
                return sb.ToString();
            }

            List<TypeRow> rows = Catalog();
            int overrides = 0;
            foreach (TypeRow row in rows) overrides += row.Overrides.Count;

            sb.AppendLine("BLOCK CATALOG  (derived from build components)");
            sb.AppendLine();
            sb.Append(GameBlocks.All().Count.ToString("n0")).Append(" definitions, ")
                .Append(rows.Count).Append(" types, ")
                .Append(overrides).AppendLine(" subtypes deviating far enough to name");
            sb.AppendLine();
            sb.AppendLine("type                          n     k     c/kg    e   crit K  prod  cons  area  composition");

            foreach (TypeRow row in rows)
            {
                BlockThermalProperties p = row.Properties;
                sb.Append(row.TypeId.PadRight(28));
                sb.Append(row.Subtypes.ToString().PadLeft(5));
                sb.Append(p.Conductivity.ToString("n0").PadLeft(6));
                sb.Append(p.SpecificHeat.ToString("n0").PadLeft(8));
                sb.Append(p.Emissivity.ToString("n2").PadLeft(6));
                sb.Append(p.CriticalTemperature.ToString("n0").PadLeft(8));
                sb.Append(p.ProducerWasteEnergy.ToString("n2").PadLeft(6));
                sb.Append(p.ConsumerWasteEnergy.ToString("n2").PadLeft(6));
                sb.Append(p.ExposedSurfaceMultiplier.ToString("n1").PadLeft(6));
                sb.Append("  ").Append(row.Composition);
                sb.AppendLine();

                foreach (OverrideRow over in row.Overrides)
                {
                    BlockThermalProperties o = over.Properties;
                    sb.Append("  + ").Append(over.SubtypeId.PadRight(24));
                    sb.Append("     ");
                    sb.Append(o.Conductivity.ToString("n0").PadLeft(6));
                    sb.Append(o.SpecificHeat.ToString("n0").PadLeft(8));
                    sb.Append(o.Emissivity.ToString("n2").PadLeft(6));
                    sb.Append(o.CriticalTemperature.ToString("n0").PadLeft(8));
                    sb.Append("                    ").Append(over.Reason);
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }
    }
}
