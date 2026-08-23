using System;
using System.Collections.Generic;

namespace Thermodynamics.Core
{
    /// <summary>
    /// The engineering reference figures for the materials a definition is allowed to name.
    ///
    /// <para>
    /// **A comment in `Cubes.xml` saying `real units: aluminium` is a claim, and until this existed
    /// nothing checked it.** Every authored `Conductivity` and `SpecificHeat` in that file names the
    /// material it is meant to be; this is the table that makes the name mean something, so a value
    /// that drifts away from the material it claims to be fails a test instead of sitting there
    /// looking authoritative. See definitions.md, Conductivity is in real W/(m·K).
    /// </para>
    ///
    /// <para>
    /// Two fields only. Conductivity and specific heat are bulk properties of the substance and a
    /// table can hold them; **emissivity is a property of the surface**, not of the material — bare
    /// plate, paint and oxide differ by a factor of six on the same steel — so it stays per
    /// component in <see cref="BlockMaterials"/> and is deliberately absent here.
    /// </para>
    /// </summary>
    public static class ReferenceMaterials
    {
        /// <summary>Bulk thermal properties of one substance, in the units a table gives.</summary>
        public struct Reference
        {
            /// <summary>Thermal conductivity, W/(m K).</summary>
            public float Conductivity;

            /// <summary>Specific heat capacity, J/(kg K).</summary>
            public float SpecificHeat;
        }

        public static readonly Reference MildSteel =
            new Reference { Conductivity = 50f, SpecificHeat = 466f };

        public static readonly Reference Aluminium =
            new Reference { Conductivity = 237f, SpecificHeat = 900f };

        public static readonly Reference Copper =
            new Reference { Conductivity = 400f, SpecificHeat = 385f };

        public static readonly Reference SodaLimeGlass =
            new Reference { Conductivity = 1.0f, SpecificHeat = 840f };

        private static readonly Dictionary<string, Reference> Table = Build();

        private static Dictionary<string, Reference> Build()
        {
            Dictionary<string, Reference> t =
                new Dictionary<string, Reference>(StringComparer.OrdinalIgnoreCase);

            // The names a definition's own comment is allowed to use, including the ones it
            // actually uses today. "Steel casing" is mild steel with a job, not a second material.
            t["mild steel"] = MildSteel;
            t["steel"] = MildSteel;
            t["steel casing"] = MildSteel;
            t["aluminium"] = Aluminium;
            t["aluminum"] = Aluminium;
            t["copper"] = Copper;
            t["glass"] = SodaLimeGlass;
            t["soda-lime glass"] = SodaLimeGlass;

            return t;
        }

        /// <summary>Every material name a definition may claim.</summary>
        public static ICollection<string> Names
        {
            get { return Table.Keys; }
        }

        public static bool IsKnown(string material)
        {
            return material != null && Table.ContainsKey(material);
        }

        /// <summary>
        /// The reference for a named material. Throws on an unknown name rather than falling back,
        /// because a caller asking about a material this table does not hold has a typo or a claim
        /// nobody has priced, and both should be loud.
        /// </summary>
        public static Reference Get(string material)
        {
            Reference reference;
            if (material != null && Table.TryGetValue(material, out reference)) return reference;

            throw new ArgumentException("no reference material called '" + material + "'");
        }
    }
}
