using System;
using System.Collections.Generic;

namespace Thermodynamics.Tests
{
    public static class ReferenceEfficiencies
    {
        public struct Reference
        {
            public float Low;

            public float High;

            public string Basis;


            public bool Admits(float value)
            {
                return value >= Low && value <= High;
            }
        }


        public static readonly Reference ElectricMotor = Band(0.05f, 0.15f,
            "IE3/NEMA Premium three-phase motors, 0.85-0.95 efficient at rated load, 1-100 kW");


        public static readonly Reference LithiumIonStore = Band(0.02f, 0.06f,

            "lithium-ion round trip 0.88-0.96, halved into one direction as 1 - sqrt(round trip)");


        public static readonly Reference CombustionEngine = Band(0.55f, 0.70f,
            "spark-ignition brake thermal efficiency 0.30-0.45");


        public static readonly Reference RadioTransmitter = Band(0.60f, 0.85f,
            "solid-state RF power amplifier chains, 0.15-0.40 DC-to-RF");


        public static readonly Reference SolidStateLaser = Band(0.50f, 0.90f,
            "wall-plug efficiency 0.10-0.50 across lamp-pumped, diode-pumped and fibre lasers");


        public static readonly Reference WaterElectrolysis = Band(0.2f, 0.4f,
            "alkaline and PEM electrolysers, 0.60-0.80 efficient against hydrogen's higher heating value");


        public static readonly Reference AllOfIt = Band(1f, 1f,
            "first law: a device doing no external work dissipates everything it draws");


        private static readonly Dictionary<string, Reference> Table = Build();


        private static Reference Band(float low, float high, string basis)
        {

            Reference r = new Reference();
            r.Low = low;
            r.High = high;
            r.Basis = basis;
            return r;
        }


        private static Dictionary<string, Reference> Build()
        {
            Dictionary<string, Reference> t =
                new Dictionary<string, Reference>(StringComparer.OrdinalIgnoreCase);

            t["electric motor"] = ElectricMotor;
            t["lithium-ion store"] = LithiumIonStore;
            t["combustion engine"] = CombustionEngine;
            t["radio transmitter"] = RadioTransmitter;
            t["solid-state laser"] = SolidStateLaser;
            t["water electrolysis"] = WaterElectrolysis;
            t["all of it"] = AllOfIt;

            return t;
        }


        public static bool IsKnown(string name)
        {
            return name != null && Table.ContainsKey(name.Trim());
        }


        public static Reference Get(string name)
        {
            Reference found;
            if (name != null && Table.TryGetValue(name.Trim(), out found)) return found;
            throw new ArgumentException("no reference conversion named '" + name + "'");
        }

        public static ICollection<string> Names
        {
            get { return Table.Keys; }
        }
    }
}
