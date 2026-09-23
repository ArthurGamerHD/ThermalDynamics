using System;
using System.Collections.Generic;

namespace Thermodynamics.Core
{
    public static class ReferenceMaterials
    {
        public struct Reference
        {
            public float Conductivity;

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

        public static ICollection<string> Names
        {
            get { return Table.Keys; }
        }


        public static bool IsKnown(string material)
        {
            return material != null && Table.ContainsKey(material);
        }


        public static Reference Get(string material)
        {
            Reference reference;
            if (material != null && Table.TryGetValue(material, out reference)) return reference;

            throw new ArgumentException("no reference material called '" + material + "'");
        }
    }
}
