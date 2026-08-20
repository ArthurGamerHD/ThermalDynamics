using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace Thermodynamics.Core
{
    /// <summary>
    /// One property changed by an overlay, as a name and a value.
    ///
    /// A name and a number rather than a typed field per property, because an overlay states only
    /// what it changes: a file listing every property would silently impose its own value for the
    /// ones its author never thought about, which is the failure mode of a settings file with
    /// defaults baked into it rather than onto its fields.
    /// </summary>
    public class ThermalOverride
    {
        [XmlAttribute] public string Name;
        [XmlAttribute] public float Value;

        public ThermalOverride() { }

        public ThermalOverride(string name, float value)
        {
            Name = name;
            Value = value;
        }
    }

    /// <summary>Overrides for one block subtype, or for the fallback entry.</summary>
    public class BlockOverride
    {
        /// <summary>
        /// The block subtype this applies to. The empty string means the default entry, which is
        /// what every block without an authored entry of its own runs on.
        /// </summary>
        [XmlAttribute] public string Subtype = "";

        [XmlElement("Set")] public List<ThermalOverride> Values = new List<ThermalOverride>();
    }

    /// <summary>
    /// A profile's definition overlay: what that profile changes about blocks, planets and coolant
    /// loops, on top of what the definition files loaded.
    ///
    /// <para>
    /// The reason this exists is stiffness. A block's demand on the integrator is its conductance
    /// over its heat capacity, so a light fitting with a small mass and a metal's conductivity asks
    /// for tens of substeps while the armour around it asks for one — and a profile that grants
    /// three is not refusing an accuracy nicety, it is integrating that block outside the range its
    /// own physics is stable in. `MaxSubstepsPerBlock` already floors exactly those blocks and is
    /// measured (see field-tuning.md); this is the other half, for the properties that a capacity
    /// floor cannot reach: a planet's convection coefficient, a loop's flow rate, an emissivity.
    /// </para>
    ///
    /// <para>
    /// **The reference profile has no overlay, deliberately.** `simulation` runs the shipped
    /// definitions exactly, so it stays the thing every other configuration is measured against —
    /// and so a benchmark cannot quietly become a comparison between two sets of definitions
    /// rather than between two settings.
    /// </para>
    /// </summary>
    [XmlRoot("ThermalOverlay")]
    public class ThermalOverlay
    {
        /// <summary>Free text, shown nowhere; for whoever opens the file next.</summary>
        [XmlElement] public string Note = "";

        [XmlArray("Blocks")] [XmlArrayItem("Block")]
        public List<BlockOverride> Blocks = new List<BlockOverride>();

        [XmlArray("Planets")] [XmlArrayItem("Planet")]
        public List<BlockOverride> Planets = new List<BlockOverride>();

        [XmlElement("Loops")] public BlockOverride Loops;

        /// <summary>True when the overlay would change nothing, which is the reference profile.</summary>
        public bool IsEmpty
        {
            get
            {
                return (Blocks == null || Blocks.Count == 0)
                    && (Planets == null || Planets.Count == 0)
                    && (Loops == null || Loops.Values == null || Loops.Values.Count == 0);
            }
        }

        /// <summary>
        /// Applies this overlay's block values to a properties object, returning whether anything
        /// changed.
        ///
        /// The overlay names the fallback entry with an empty subtype, so a file can state one
        /// change that reaches every block the definitions never mention — which on an ordinary
        /// world is most of them, and is where a stiffness problem usually lives.
        /// </summary>
        public bool ApplyTo(BlockThermalProperties properties, string subtype)
        {
            if (properties == null || Blocks == null) return false;

            bool changed = false;

            for (int i = 0; i < Blocks.Count; i++)
            {
                BlockOverride entry = Blocks[i];
                if (entry == null || !Matches(entry.Subtype, subtype)) continue;

                for (int v = 0; v < entry.Values.Count; v++)
                {
                    changed |= Set(properties, entry.Values[v]);
                }
            }

            return changed;
        }

        /// <summary>Applies the loop values, returning whether anything changed.</summary>
        public bool ApplyTo(LoopThermalProperties properties)
        {
            if (properties == null || Loops == null || Loops.Values == null) return false;

            bool changed = false;
            for (int i = 0; i < Loops.Values.Count; i++)
            {
                changed |= Set(properties, Loops.Values[i]);
            }

            return changed;
        }

        /// <summary>Applies the values for one planet subtype, returning whether anything changed.</summary>
        public bool ApplyTo(PlanetThermalProperties properties, string subtype)
        {
            if (properties == null || Planets == null) return false;

            bool changed = false;

            for (int i = 0; i < Planets.Count; i++)
            {
                BlockOverride entry = Planets[i];
                if (entry == null || !Matches(entry.Subtype, subtype)) continue;

                for (int v = 0; v < entry.Values.Count; v++)
                {
                    changed |= Set(properties, entry.Values[v]);
                }
            }

            return changed;
        }

        /// <summary>
        /// An entry with no subtype applies to everything, which is how a profile says "every
        /// block" without listing the game's whole catalogue.
        /// </summary>
        private static bool Matches(string entrySubtype, string subtype)
        {
            if (string.IsNullOrEmpty(entrySubtype)) return true;
            if (subtype == null) return false;

            return string.Equals(entrySubtype, subtype, StringComparison.OrdinalIgnoreCase);
        }

        private static bool Set(BlockThermalProperties p, ThermalOverride value)
        {
            if (value == null || string.IsNullOrEmpty(value.Name)) return false;

            switch (value.Name)
            {
                case "Conductivity": p.Conductivity = value.Value; return true;
                case "SpecificHeat": p.SpecificHeat = value.Value; return true;
                case "Emissivity": p.Emissivity = value.Value; return true;
                case "ExposedSurfaceMultiplier": p.ExposedSurfaceMultiplier = value.Value; return true;
                case "ProducerWasteEnergy": p.ProducerWasteEnergy = value.Value; return true;
                case "ConsumerWasteEnergy": p.ConsumerWasteEnergy = value.Value; return true;
                case "CriticalTemperature": p.CriticalTemperature = value.Value; return true;
                case "OverheatDamagePerKelvin": p.OverheatDamagePerKelvin = value.Value; return true;
                case "ExcludeFromSimulation": p.ExcludeFromSimulation = value.Value > 0.5f; return true;
                default: return false;
            }
        }

        private static bool Set(LoopThermalProperties p, ThermalOverride value)
        {
            if (value == null || string.IsNullOrEmpty(value.Name)) return false;

            switch (value.Name)
            {
                case "CoolantMassPerPipe": p.CoolantMassPerPipe = value.Value; return true;
                case "Conductivity": p.Conductivity = value.Value; return true;
                case "SpecificHeat": p.SpecificHeat = value.Value; return true;
                case "PipeContactMultiplier": p.PipeContactMultiplier = value.Value; return true;
                case "SinkContactMultiplier": p.SinkContactMultiplier = value.Value; return true;
                case "LargeGridFlowRate": p.LargeGridFlowRate = value.Value; return true;
                case "SmallGridFlowRate": p.SmallGridFlowRate = value.Value; return true;
                case "StagnantTransferFraction": p.StagnantTransferFraction = value.Value; return true;
                default: return false;
            }
        }

        private static bool Set(PlanetThermalProperties p, ThermalOverride value)
        {
            if (value == null || string.IsNullOrEmpty(value.Name)) return false;

            switch (value.Name)
            {
                case "DayTemperature": p.DayTemperature = value.Value; return true;
                case "NightTemperature": p.NightTemperature = value.Value; return true;
                case "ConvectionCoefficient": p.ConvectionCoefficient = value.Value; return true;
                case "SolarDecay": p.SolarDecay = value.Value; return true;
                case "AmbientLagSeconds": p.AmbientLagSeconds = value.Value; return true;
                case "AmbientLapseRate": p.AmbientLapseRate = value.Value; return true;
                case "PoleTemperatureDrop": p.PoleTemperatureDrop = value.Value; return true;
                case "UndergroundTemperature": p.UndergroundTemperature = value.Value; return true;
                case "UndergroundDampingDepth": p.UndergroundDampingDepth = value.Value; return true;
                case "CoreTemperature": p.CoreTemperature = value.Value; return true;
                case "SealevelDeadzone": p.SealevelDeadzone = value.Value; return true;
                default: return false;
            }
        }

        /// <summary>
        /// Names this overlay sets that no property has, which is how a typo in a hand-written file
        /// is found before it becomes a mystery about why a profile does nothing.
        /// </summary>
        public List<string> Unknown()
        {
            List<string> bad = new List<string>();

            if (Blocks != null)
            {
                for (int i = 0; i < Blocks.Count; i++)
                {
                    Check(Blocks[i], new BlockThermalProperties(), bad, "block");
                }
            }

            if (Planets != null)
            {
                for (int i = 0; i < Planets.Count; i++)
                {
                    Check(Planets[i], PlanetThermalProperties.Default(), bad, "planet");
                }
            }

            if (Loops != null && Loops.Values != null)
            {
                for (int i = 0; i < Loops.Values.Count; i++)
                {
                    ThermalOverride value = Loops.Values[i];
                    if (!Set(LoopThermalProperties.Default(), value))
                    {
                        bad.Add("loop." + (value == null ? "(null)" : value.Name));
                    }
                }
            }

            return bad;
        }

        private static void Check(BlockOverride entry, BlockThermalProperties probe,
            List<string> bad, string kind)
        {
            if (entry == null || entry.Values == null) return;

            for (int i = 0; i < entry.Values.Count; i++)
            {
                ThermalOverride value = entry.Values[i];
                if (!Set(probe, value)) bad.Add(kind + "." + (value == null ? "(null)" : value.Name));
            }
        }

        private static void Check(BlockOverride entry, PlanetThermalProperties probe,
            List<string> bad, string kind)
        {
            if (entry == null || entry.Values == null) return;

            for (int i = 0; i < entry.Values.Count; i++)
            {
                ThermalOverride value = entry.Values[i];
                if (!Set(probe, value)) bad.Add(kind + "." + (value == null ? "(null)" : value.Name));
            }
        }
    }
}
