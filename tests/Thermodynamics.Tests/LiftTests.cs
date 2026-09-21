using System;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class LiftTests
    {
        private const float ThickAir = 1f;
        private const float Speed = 200f;

/// <summary>Sets the tings.</summary>
        private static ThermalSettings Settings(bool lift, bool shape = true)
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.EnableDamage = false;
            settings.EnableShapeDrag = shape;
            settings.EnableLift = lift;
            settings.Derive();
            return settings;
        }

/// <summary>Built operation.</summary>
        private static ThermalSimulation Built(GridBuilder builder, bool lift, bool shape = true)
        {
            ThermalSimulation simulation = builder.BuildSimulation(Settings(lift, shape), 293.15f);
            simulation.Planet = PlanetThermalProperties.Default();
            simulation.StepExact(1, Worlds.Flight(ThickAir, Speed));
            return simulation;
        }

/// <summary>Cube operation.</summary>
        private static ThermalSimulation Cube(bool lift)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.HeavyArmor(), Vector3I.Zero, new Vector3I(6, 6, 6));
/// <summary>Built operation.</summary>
            return Built(builder, lift);
        }

/// <summary>Ramp operation.</summary>
        private static ThermalSimulation Ramp(bool lift)
        {
            GridBuilder builder = GridBuilder.Large();
            for (int y = 0; y < 6; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    for (int z = 0; z < 6 - y; z++)
                    {
                        builder.Place(Catalog.HeavyArmor(), new Vector3I(x, y, z));
                    }
                }
            }

/// <summary>Built operation.</summary>
            return Built(builder, lift);
        }

/// <summary>Wind operation.</summary>
        private static Vector3 Wind()
        {
            return Vector3.Backward * Speed;
        }

/// <summary>Lift operation.</summary>
        private static Vector3 Lift(ThermalSimulation simulation)
        {
            return LiftForce.Vector(simulation.Solver.LastPressureWatts, Wind(),
                simulation.Solver.Settings);
        }

        [Fact]
/// <summary>ASymmetricHullMakesNoLift operation.</summary>
        public void ASymmetricHullMakesNoLift()
        {
/// <summary>Lift operation.</summary>
            Vector3 lift = Lift(Cube(true));

            Assert.True(lift.Length() < 1f,
                "a cube is symmetric about the flow and produced " + lift.Length()
                    + " N of lift, so the transverse sum is reading an artefact");
        }

        [Fact]
/// <summary>ARampIsPushedAwayFromItsSlopedFace operation.</summary>
        public void ARampIsPushedAwayFromItsSlopedFace()
        {
/// <summary>Lift operation.</summary>
            Vector3 lift = Lift(Ramp(true));

            Assert.True(lift.Length() > 1f,
                "the ramp produced no lift at all, so this checks nothing");
            Assert.True(lift.Y < 0f,
                "the ramp's slope faces +Y so the pressure on it must push -Y, and this pushed "
                    + lift.Y);
        }

        [Fact]
/// <summary>LiftCarriesNoComponentAlongTheFlow operation.</summary>
        public void LiftCarriesNoComponentAlongTheFlow()
        {
/// <summary>Lift operation.</summary>
            Vector3 lift = Lift(Ramp(true));
            Vector3 flow = Vector3.Normalize(Wind());

            Assert.True(Math.Abs(Vector3.Dot(lift, flow)) < 1e-2f * lift.Length() + 1e-3f,
                "lift has a component along the flow, so switching it on changes drag");
        }

        [Fact]
/// <summary>LiftDoesNotChangeDrag operation.</summary>
        public void LiftDoesNotChangeDrag()
        {
/// <summary>Ramp operation.</summary>
            float without = Ramp(false).Solver.LastFrictionWatts;
/// <summary>Ramp operation.</summary>
            float with = Ramp(true).Solver.LastFrictionWatts;

            Assert.Equal(without, with, 4);
        }

        [Fact]
/// <summary>LiftIsRefusedWithoutTheShapeTerm operation.</summary>
        public void LiftIsRefusedWithoutTheShapeTerm()
        {
/// <summary>Built operation.</summary>
            ThermalSimulation ramp = Built(RampBuilder(), true, false);

            Assert.Equal(Vector3.Zero, ramp.Solver.LastPressureWatts);
            Assert.Equal(Vector3.Zero, Lift(ramp));
        }

        [Fact]
/// <summary>TheCoefficientScalesTheForce operation.</summary>
        public void TheCoefficientScalesTheForce()
        {
/// <summary>Ramp operation.</summary>
            ThermalSimulation ramp = Ramp(true);

            ThermalSettings half = ramp.Solver.Settings;
/// <summary>Lift operation.</summary>
            float full = Lift(ramp).Length();

            half.LiftCoefficient = 0.5f;
            float halved = LiftForce.Vector(ramp.Solver.LastPressureWatts, Wind(), half).Length();

            Assert.Equal(full * 0.5f, halved, 2);

            half.LiftCoefficient = 0f;
            Assert.Equal(Vector3.Zero, LiftForce.Vector(ramp.Solver.LastPressureWatts, Wind(), half));
        }

/// <summary>RampBuilder operation.</summary>
        private static GridBuilder RampBuilder()
        {
            GridBuilder builder = GridBuilder.Large();
            for (int y = 0; y < 6; y++)
                for (int x = 0; x < 8; x++)
                    for (int z = 0; z < 6 - y; z++)
                        builder.Place(Catalog.HeavyArmor(), new Vector3I(x, y, z));

            return builder;
        }
    }
}
