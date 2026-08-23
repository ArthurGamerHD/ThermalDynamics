using System;
using System.Collections.Generic;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The engineering figures a waste fraction in `Cubes.xml` is allowed to name, and the ranges
    /// they are held to.
    ///
    /// <para>
    /// This is <see cref="Thermodynamics.Core.ReferenceMaterials"/>'s shape applied to the other
    /// half of a block's definition. A conductivity claiming aluminium is checked; a
    /// `ConsumerWasteEnergy` claimed nothing at all until this existed, which is how the jump drive
    /// sat at 0.15 by assertion while its own definition stated an efficiency. See
    /// [backlog.md](../../docs/backlog.md) `C21`.
    /// </para>
    ///
    /// <para>
    /// **Ranges rather than points, because that is what the literature gives.** A motor's
    /// efficiency is a band across frame sizes and loads, not a constant, so an entry carries the
    /// band and the check asks whether the authored value is inside it. A point value with a
    /// tolerance around it would be inventing a precision the source does not have.
    /// </para>
    ///
    /// <para>
    /// **It lives in the test project rather than in `Core`.** Nothing the game runs reads it: the
    /// fractions are authored, and this is the oracle that judges them, which puts it beside
    /// <see cref="LegacyFormulas"/> and <see cref="Reference"/> rather than beside the code that
    /// ships.
    /// </para>
    /// </summary>
    public static class ReferenceEfficiencies
    {
        /// <summary>One class of energy conversion, as a band of waste fractions.</summary>
        public struct Reference
        {
            /// <summary>Smallest waste fraction the source admits.</summary>
            public float Low;

            /// <summary>Largest waste fraction the source admits.</summary>
            public float High;

            /// <summary>Where the band comes from. Prose, and the reason an entry is auditable.</summary>
            public string Basis;

            public bool Admits(float value)
            {
                return value >= Low && value <= High;
            }
        }

        /// <summary>
        /// A rotating electrical machine driving a mechanical load: rotors, pistons, suspensions.
        /// IE3 and NEMA Premium three-phase machines run 0.85 to 0.95 efficient at rated load over
        /// the 1 to 100 kW frames a ship block stands for, so a twentieth to a seventh is lost as
        /// winding, iron and bearing heat.
        /// </summary>
        public static readonly Reference ElectricMotor = Band(0.05f, 0.15f,
            "IE3/NEMA Premium three-phase motors, 0.85-0.95 efficient at rated load, 1-100 kW");

        /// <summary>
        /// An electrochemical store, charging or discharging. Lithium-ion round-trip efficiency is
        /// 0.88 to 0.96, and a single direction loses `1 - sqrt(round trip)`, which is 0.02 to 0.06.
        /// </summary>
        public static readonly Reference LithiumIonStore = Band(0.02f, 0.06f,
            "lithium-ion round trip 0.88-0.96, halved into one direction as 1 - sqrt(round trip)");

        /// <summary>
        /// A piston engine burning fuel to turn a generator. Brake thermal efficiency is 0.30 to
        /// 0.45 for spark ignition, so 0.55 to 0.70 of the fuel leaves as heat in the exhaust, the
        /// coolant and the block.
        /// </summary>
        public static readonly Reference CombustionEngine = Band(0.55f, 0.70f,
            "spark-ignition brake thermal efficiency 0.30-0.45");

        /// <summary>
        /// A radio transmitter, where the radiated power genuinely leaves the ship. Solid-state
        /// power amplifiers run 0.15 to 0.40 DC-to-RF once the driver and the modulator are counted,
        /// so 0.60 to 0.85 of what an antenna draws stays behind as heat.
        /// </summary>
        public static readonly Reference RadioTransmitter = Band(0.60f, 0.85f,
            "solid-state RF power amplifier chains, 0.15-0.40 DC-to-RF");

        /// <summary>
        /// A laser, where the beam leaves the ship. Wall-plug efficiency spans 0.10 for a
        /// lamp-pumped solid-state laser to 0.50 for the best industrial fibre lasers, so the waste
        /// band is 0.50 to 0.90 and the low end of it is the best machine that exists.
        /// </summary>
        public static readonly Reference SolidStateLaser = Band(0.50f, 0.90f,
            "wall-plug efficiency 0.10-0.50 across lamp-pumped, diode-pumped and fibre lasers");

        /// <summary>
        /// Everything, by the first law: a device that does no work outside itself and radiates
        /// nothing away turns every watt it draws into heat where it stands. It is a bound rather
        /// than a measurement, which is why the band has no width.
        /// </summary>
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

        /// <summary>Every name a definition may cite, for a test that wants to report coverage.</summary>
        public static ICollection<string> Names
        {
            get { return Table.Keys; }
        }
    }
}
