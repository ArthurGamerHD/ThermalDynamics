using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The simulation must not know the game exists.
    ///
    /// That isolation is the reason this suite can run at all, and it is also what makes a second
    /// host possible: Space Engineers 2 keeps blocks in a different shape — integer AABBs on a
    /// 0.25 m lattice, cells grouped as boxes rather than listed — and an adapter can only be
    /// written against a core that takes block layout, an environment sample and a frame length,
    /// and hands back temperatures. Every game type that leaks into the core is a line that
    /// adapter would have to fake.
    ///
    /// It has always been asserted in prose — <c>sim/README.md</c> says the core references one
    /// assembly and names it — and never by anything that fails. A stray <c>using Sandbox.Game</c>
    /// would be caught by the build here, because the reference is not present to satisfy it; a
    /// type that arrives indirectly, through a shared struct or an interface parameter, would not
    /// be. These tests read the built assembly rather than the source, so they see what actually
    /// got compiled.
    /// </summary>
    public class CoreIsolationTests
    {
        private static readonly Assembly Core = typeof(ThermalSolver).Assembly;

        /// <summary>
        /// Assemblies the simulation is allowed to depend on.
        ///
        /// <c>VRage.Math</c> is the single exception, and a deliberate one: it is pure managed
        /// maths — <c>Vector3I</c>, <c>Vector3</c>, <c>Matrix</c>, <c>Base6Directions</c> — with no
        /// session, no entity and no engine behind it, and it loads on .NET on Linux. Everything
        /// else in that namespace does not.
        /// </summary>
        private static bool IsAllowed(string assemblyName)
        {
            if (assemblyName == "VRage.Math") return true;

            return assemblyName == "mscorlib"
                || assemblyName == "netstandard"
                || assemblyName == "System"
                || assemblyName.StartsWith("System.", StringComparison.Ordinal);
        }

        [Fact]
        public void TheCoreReferencesNothingButMathsAndTheFramework()
        {
            List<string> offenders = new List<string>();

            AssemblyName[] referenced = Core.GetReferencedAssemblies();
            for (int i = 0; i < referenced.Length; i++)
            {
                if (!IsAllowed(referenced[i].Name)) offenders.Add(referenced[i].Name);
            }

            Assert.True(offenders.Count == 0,
                "the simulation core references " + string.Join(", ", offenders.ToArray())
                + ". Only VRage.Math and the framework are allowed — anything else is a game "
                + "dependency an SE2 adapter would have to reimplement.");
        }

        /// <summary>
        /// And nothing from the game may reach the core through its own public surface either.
        ///
        /// The reference list catches a direct dependency. This catches the subtler one: a method
        /// that takes or returns a type belonging to an assembly the core is not allowed to know,
        /// which can arrive through a generic argument or an interface without the reference list
        /// ever changing shape.
        /// </summary>
        [Fact]
        public void NoPublicApiInTheCoreSpeaksAGameType()
        {
            StringBuilder offenders = new StringBuilder();

            Type[] types = Core.GetTypes();
            for (int t = 0; t < types.Length; t++)
            {
                Type type = types[t];
                if (!type.IsPublic && !type.IsNestedPublic) continue;

                MemberInfo[] members = type.GetMembers(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static
                    | BindingFlags.DeclaredOnly);

                for (int m = 0; m < members.Length; m++)
                {
                    CheckMember(type, members[m], offenders);
                }
            }

            Assert.True(offenders.Length == 0,
                "game types reach the simulation's public surface:\n" + offenders);
        }

        private static void CheckMember(Type owner, MemberInfo member, StringBuilder offenders)
        {
            MethodBase method = member as MethodBase;
            if (method != null)
            {
                ParameterInfo[] parameters = method.GetParameters();
                for (int p = 0; p < parameters.Length; p++)
                {
                    Check(owner, member, parameters[p].ParameterType, offenders);
                }

                MethodInfo asMethod = member as MethodInfo;
                if (asMethod != null) Check(owner, member, asMethod.ReturnType, offenders);
                return;
            }

            PropertyInfo property = member as PropertyInfo;
            if (property != null)
            {
                Check(owner, member, property.PropertyType, offenders);
                return;
            }

            FieldInfo field = member as FieldInfo;
            if (field != null) Check(owner, member, field.FieldType, offenders);
        }

        private static void Check(Type owner, MemberInfo member, Type type, StringBuilder offenders)
        {
            if (type == null) return;

            if (type.IsByRef || type.IsArray || type.IsPointer)
            {
                Check(owner, member, type.GetElementType(), offenders);
                return;
            }

            if (type.IsGenericType)
            {
                Type[] arguments = type.GetGenericArguments();
                for (int i = 0; i < arguments.Length; i++)
                {
                    Check(owner, member, arguments[i], offenders);
                }
            }

            string assembly = type.Assembly.GetName().Name;
            if (assembly == Core.GetName().Name) return;
            if (IsAllowed(assembly)) return;

            offenders.Append("  ").Append(owner.Name).Append('.').Append(member.Name)
                     .Append(" speaks ").Append(type.FullName)
                     .Append(" from ").Append(assembly).Append('\n');
        }

        /// <summary>
        /// The whole contract with a host, stated as a test: block layout in, an environment
        /// sample and a frame length in, temperatures and events out.
        ///
        /// If this compiles and runs against nothing but the core, a second host has somewhere to
        /// plug into. It is deliberately written the way an adapter would write it rather than the
        /// way the rest of the suite does, because that is the thing being checked.
        /// </summary>
        [Fact]
        public void AHostCanDriveTheSimulationThroughTheCoreAlone()
        {
            GridModel grid = new GridModel(0.25f);

            BlockThermalProperties thermal = new BlockThermalProperties
            {
                Conductivity = 50f,
                SpecificHeat = 450f,
                Emissivity = 0.2f,
                ExposedSurfaceMultiplier = 1f,
                CriticalTemperature = 1200f,
                OverheatDamagePerKelvin = 1f,
            };

            BlockModel model = BlockModel.Solid("hull", new Vector3I(2, 2, 2), 120f, thermal);

            ThermalSimulation simulation = new ThermalSimulation(new ThermalSettings(), grid);
            simulation.AddBlock(new BlockInstance(model, Vector3I.Zero, BlockOrientation.Identity), 400f);
            simulation.AddBlock(new BlockInstance(model, new Vector3I(2, 0, 0), BlockOrientation.Identity), 300f);
            simulation.RebuildAll();

            EnvironmentSample sample = EnvironmentSample.DarkVacuum();
            simulation.StepExact(20, sample);

            ThermalNode hot = simulation.Solver.GetNodeAt(Vector3I.Zero);
            ThermalNode cold = simulation.Solver.GetNodeAt(new Vector3I(2, 0, 0));

            Assert.NotNull(hot);
            Assert.NotNull(cold);
            Assert.True(hot.Temperature < 400f, "the hot block should have shed heat");
            Assert.True(cold.Temperature > 300f, "the cold block should have taken some of it");
        }
    }
}
