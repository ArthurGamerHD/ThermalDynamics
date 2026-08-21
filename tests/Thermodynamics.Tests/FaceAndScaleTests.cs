using System;
using Thermodynamics.Core;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class FaceTests
    {
        [Fact]
        public void OppositeIsAnInvolutionAndReversesTheOffset()
        {
            for (int face = 0; face < Face.Count; face++)
            {
                int opposite = Face.Opposite(face);

                Assert.Equal(face, Face.Opposite(opposite));
                Assert.Equal(-Face.Offsets[face], Face.Offsets[opposite]);
            }
        }

        [Fact]
        public void IndexOfInvertsOffsets()
        {
            for (int face = 0; face < Face.Count; face++)
            {
                Assert.Equal(face, Face.IndexOf(Face.Offsets[face]));
            }

            Assert.Equal(-1, Face.IndexOf(new Vector3I(1, 1, 0)));
            Assert.Equal(-1, Face.IndexOf(Vector3I.Zero));
        }

        [Fact]
        public void NormalsMatchOffsets()
        {
            for (int face = 0; face < Face.Count; face++)
            {
                Vector3I offset = Face.Offsets[face];
                Assert.Equal(new Vector3(offset.X, offset.Y, offset.Z), Face.Normals[face]);
            }
        }

        [Fact]
        public void OppositeFacesShareAnAxis()
        {
            for (int face = 0; face < Face.Count; face++)
            {
                Assert.Equal(Face.Axis(face), Face.Axis(Face.Opposite(face)));
            }

            Assert.Equal(0, Face.Axis(Face.Left));
            Assert.Equal(1, Face.Axis(Face.Up));
            Assert.Equal(2, Face.Axis(Face.Forward));
        }
    }

    public class TemperatureScaleTests
    {
        [Fact]
        public void ConversionsRoundTrip()
        {
            Assert.Equal(0f, ThermalConstants.KelvinToCelsius(273.15f), 3);
            Assert.Equal(273.15f, ThermalConstants.CelsiusToKelvin(0f), 3);
            Assert.Equal(32f, ThermalConstants.KelvinToFahrenheit(273.15f), 2);
            Assert.Equal(212f, ThermalConstants.KelvinToFahrenheit(373.15f), 2);
        }

        [Fact]
        public void RampIsBlackAtZeroAndBlueAtTheLowAnchor()
        {
            Vector3 cold = TemperatureScale.ToHsv(0f);
            Assert.Equal(-1f, cold.Z, 3);

            Vector3 low = TemperatureScale.ToHsv(TemperatureScale.DefaultLow);
            Assert.Equal(240f / 360f, low.X, 3);
        }

        [Fact]
        public void HueSweepsDownFromBlueToRedBetweenTheAnchors()
        {
            float previousHue = float.MaxValue;
            for (float t = TemperatureScale.DefaultLow; t <= TemperatureScale.DefaultHigh; t += 10f)
            {
                float hue = TemperatureScale.ToHsv(t).X;
                Assert.True(hue <= previousHue + 1e-5f, "hue must not increase at " + t);
                previousHue = hue;
            }

            Assert.Equal(0f, TemperatureScale.ToHsv(TemperatureScale.DefaultHigh).X, 3);
        }

        [Fact]
        public void AboveTheHighAnchorSaturationFallsTowardWhite()
        {
            float atHigh = TemperatureScale.ToHsv(TemperatureScale.DefaultHigh).Y;
            float atMax = TemperatureScale.ToHsv(TemperatureScale.DefaultMax).Y;

            Assert.Equal(1f, atHigh, 3);
            Assert.True(atMax < atHigh);
        }

        [Fact]
        public void ValuesAreClampedRatherThanExtrapolated()
        {
            Vector3 beyond = TemperatureScale.ToHsv(999999f);
            Vector3 atMax = TemperatureScale.ToHsv(TemperatureScale.DefaultMax);
            Assert.Equal(atMax, beyond);

            Vector3 belowZero = TemperatureScale.ToHsv(-500f);
            Assert.Equal(TemperatureScale.ToHsv(0f), belowZero);
        }

        [Fact]
        public void DegenerateAnchorsDoNotProduceInfinities()
        {
            Vector3 colour = TemperatureScale.ToHsv(100f, 0f, 0f, 0f);
            Assert.False(float.IsNaN(colour.X) || float.IsInfinity(colour.X));
            Assert.False(float.IsNaN(colour.Y) || float.IsInfinity(colour.Y));
            Assert.False(float.IsNaN(colour.Z) || float.IsInfinity(colour.Z));
        }
    }

    public class OcclusionMathTests
    {
        [Fact]
        public void VisualSizeShrinksWithDistance()
        {
            double near = OcclusionMath.VisualSize(1000d, 60000d);
            double far = OcclusionMath.VisualSize(10000000d, 60000d);
            Assert.True(near > far);
        }

        [Fact]
        public void SunBehindThePlanetIsOccluded()
        {
            Vector3D planet = Vector3D.Zero;
            double radius = 60000d;
            Vector3D observer = new Vector3D(0d, 0d, radius + 1000d);

            // sun on the far side of the planet from the observer
            Vector3 sunBehind = new Vector3(0f, 0f, -1f);
            Assert.True(OcclusionMath.IsOccludedBySphere(observer, planet, radius, sunBehind));
        }

        [Fact]
        public void SunOverheadIsNotOccluded()
        {
            Vector3D planet = Vector3D.Zero;
            double radius = 60000d;
            Vector3D observer = new Vector3D(0d, 0d, radius + 1000d);

            Vector3 sunAbove = new Vector3(0f, 0f, 1f);
            Assert.False(OcclusionMath.IsOccludedBySphere(observer, planet, radius, sunAbove));
        }

        [Fact]
        public void FarFromThePlanetTheTerminatorIsSharp()
        {
            Vector3D planet = Vector3D.Zero;
            double radius = 60000d;
            Vector3D observer = new Vector3D(0d, 0d, 100000000d);

            // A tiny distant planet occludes only when almost exactly in line.
            Vector3 sideways = Vector3.Normalize(new Vector3(1f, 0f, -0.01f));
            Assert.False(OcclusionMath.IsOccludedBySphere(observer, planet, radius, sideways));
        }
    }
}
