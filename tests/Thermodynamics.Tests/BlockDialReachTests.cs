using System;
using System.Collections.Generic;
using System.Reflection;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    public class BlockDialReachTests
    {
        private readonly ITestOutputHelper output;

/// <summary>BlockDialReachTests operation.</summary>
        public BlockDialReachTests(ITestOutputHelper output)
        {
            this.output = output;
        }

/// <summary>Sets the tings.</summary>
        private static ThermalSettings Settings()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.MaxSubsteps = 4096;
            settings.MaxElementVisitsPerStep = 0;
            return settings.Derive();
        }

/// <summary>Read operation.</summary>
        private static void Read(ThermalSimulation simulation, BlockInstance subject,
            List<float> into)
        {
            float hottest = 0f;
            float total = 0f;
            int over = 0;

            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                float t = nodes[i].Temperature;
                if (t > hottest) hottest = t;
                total += t;
                if (t > nodes[i].Thermal.CriticalTemperature) over++;
            }

            ThermalNode node = simulation.Solver.GetNode(subject);

            into.Add(hottest);
            into.Add(total);
            into.Add(over);
            into.Add(node == null ? -1f : node.Temperature);
            into.Add(nodes.Count);
            into.Add(simulation.Solver.RequiredSubsteps(0.25f));

            IList<OverheatEvent> overheats = simulation.Overheats;
            float damage = 0f;
            for (int i = 0; overheats != null && i < overheats.Count; i++)
            {
                damage += overheats[i].Damage;
            }
            into.Add(damage);
        }

/// <summary>Rig operation.</summary>
        private static void Rig(BlockThermalProperties properties, EnvironmentSample world,
            float watts, bool exposed, List<float> into)
        {
            Rig(properties, world, watts, exposed, false, into);
        }

/// <summary>Rig operation.</summary>
        private static void Rig(BlockThermalProperties properties, EnvironmentSample world,
            float watts, bool exposed, bool consuming, List<float> into)
        {
            BlockModel subject = BlockModel.Solid("Subject", Vector3I.One, 900f, properties);

            GridBuilder builder = GridBuilder.Large();

            if (exposed)
            {
                builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(1, 1, 4));
                builder.Place(subject, new Vector3I(0, 0, 4));
            }
            else
            {
                builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(3, 3, 3));
                builder.Remove(new Vector3I(1, 1, 1));
                builder.Place(subject, new Vector3I(1, 1, 1));
            }

            BlockInstance instance = builder.Last;

            if (watts > 0f)
            {
                if (consuming) builder.Consuming(watts); else builder.Producing(watts);
            }

            ThermalSimulation simulation = builder.BuildSimulation(Settings(), 293.15f);
            simulation.StepExact(LabClock.Steps(300), world);

            Read(simulation, instance, into);
        }

/// <summary>Fingerprint operation.</summary>
        private static List<float> Fingerprint(BlockThermalProperties properties)
        {
/// <summary>List operation.</summary>
            List<float> readings = new List<float>();

            Rig(properties, Worlds.Shadow(), 0f, false, readings);

            Rig(properties, Worlds.Shadow(), 60000000f, false, readings);

            Rig(properties, Worlds.Shadow(), 200000f, true, readings);

            Rig(properties, Worlds.Space(new Vector3(0f, 0f, 1f)), 0f, true, readings);

            Rig(properties, Worlds.PlanetSurface(1f, 0.5f, 20f), 200000f, true, readings);

            Rig(properties, Worlds.Shadow(), 60000000f, false, true, readings);

            return readings;
        }

/// <summary>Levels operation.</summary>
        private static object[] Levels(FieldInfo field, BlockThermalProperties shipped)
        {
            if (field.FieldType == typeof(bool))
            {
                return new object[] { !(bool)field.GetValue(shipped) };
            }

            if (field.Name == "SolarAbsorptivity") return new object[] { 0.05f, 0.95f };

            float value = (float)field.GetValue(shipped);
            if (value == 0f) return new object[] { 1f, 200000f };
            return new object[] { value * 0.25f, value * 4f };
        }

/// <summary>Same operation.</summary>
        private static bool Same(List<float> a, List<float> b)
        {
            if (a.Count != b.Count) return false;

            for (int i = 0; i < a.Count; i++)
            {
                float scale = Math.Max(Math.Abs(a[i]), Math.Abs(b[i]));
                float tolerance = scale < 1f ? 1e-4f : scale * 1e-5f;
                if (Math.Abs(a[i] - b[i]) > tolerance) return false;
            }

            return true;
        }

        [Fact]
/// <summary>EveryBlockDialReachesTheSimulation operation.</summary>
        public void EveryBlockDialReachesTheSimulation()
        {
/// <summary>List operation.</summary>
            List<FieldInfo> fields = new List<FieldInfo>();

            foreach (FieldInfo field in typeof(BlockThermalProperties)
                .GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                Type type = field.FieldType;
                if (type == typeof(float) || type == typeof(bool)) fields.Add(field);
            }

            Assert.True(fields.Count >= 10,
                "only " + fields.Count + " block properties were found, so this test would pass on"
                + " a definition that had lost most of them");

/// <summary>Fingerprint operation.</summary>
            List<float> shipped = Fingerprint(Catalog.DefaultThermal());

/// <summary>List operation.</summary>
            List<string> inert = new List<string>();

            foreach (FieldInfo field in fields)
            {
                bool moved = false;

                foreach (object level in Levels(field, Catalog.DefaultThermal()))
                {
                    BlockThermalProperties properties = Catalog.DefaultThermal();
                    field.SetValue(properties, level);

                    if (Same(shipped, Fingerprint(properties))) continue;
                    moved = true;
                    break;
                }

                if (!moved) inert.Add(field.Name);

                output.WriteLine("{0,-32} {1}", field.Name,
                    moved ? "reaches" : "REACHES NOTHING");
            }

            Assert.True(inert.Count == 0,
                "block properties that changed nothing any rig here reads:\n  "
                + string.Join("\n  ", inert)
                + "\nEither the property is wired to nothing — which is the defect this exists for —"
                + " or no rig here can see it, and a rig is cheap to add.");
        }
    }
}
