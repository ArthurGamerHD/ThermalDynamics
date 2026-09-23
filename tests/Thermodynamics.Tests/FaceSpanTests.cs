using Thermodynamics.Core;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class FaceSpanTests
    {
        [Theory]
        [InlineData(Face.Right, 0)]
        [InlineData(Face.Up, 1)]
        [InlineData(Face.Backward, 2)]

        public void APositiveFaceIsTheLastRowInsideTheBox(int face, int axis)
        {

            Vector3I min = new Vector3I(2, 3, 5);

            Vector3I maxExclusive = new Vector3I(6, 9, 11);

            BoxGeometry.FaceSpan span = BoxGeometry.Span(min, maxExclusive, face);

            Assert.Equal(axis, span.Axis);
            Assert.True(span.Positive);
            Assert.Equal(BoxGeometry.Component(maxExclusive, axis) - 1, span.Slab);
        }

        [Theory]
        [InlineData(Face.Left, 0)]
        [InlineData(Face.Down, 1)]
        [InlineData(Face.Forward, 2)]

        public void ANegativeFaceIsTheFirstRowOfTheBox(int face, int axis)
        {

            Vector3I min = new Vector3I(2, 3, 5);

            Vector3I maxExclusive = new Vector3I(6, 9, 11);

            BoxGeometry.FaceSpan span = BoxGeometry.Span(min, maxExclusive, face);

            Assert.Equal(axis, span.Axis);
            Assert.False(span.Positive);
            Assert.Equal(BoxGeometry.Component(min, axis), span.Slab);
        }

        [Fact]

        public void EverySlabIsInsideTheBox()
        {
            for (int sx = 1; sx <= 3; sx++)
            {
                for (int sy = 1; sy <= 3; sy++)
                {
                    for (int sz = 1; sz <= 3; sz++)
                    {

                        Vector3I min = new Vector3I(-2, 4, 7);

                        Vector3I maxExclusive = min + new Vector3I(sx, sy, sz);

                        for (int face = 0; face < Face.Count; face++)
                        {
                            BoxGeometry.FaceSpan span = BoxGeometry.Span(min, maxExclusive, face);

                            Assert.InRange(span.Slab,
                                BoxGeometry.Component(min, span.Axis),
                                BoxGeometry.Component(maxExclusive, span.Axis) - 1);
                        }
                    }
                }
            }
        }

        [Fact]

        public void TheSpannedAxesAreTheOtherTwoAxesExactlyOnce()
        {
            Vector3I min = Vector3I.Zero;

            Vector3I maxExclusive = new Vector3I(3, 4, 5);

            for (int face = 0; face < Face.Count; face++)
            {
                BoxGeometry.FaceSpan span = BoxGeometry.Span(min, maxExclusive, face);

                Assert.NotEqual(span.Axis, span.U);
                Assert.NotEqual(span.Axis, span.V);
                Assert.NotEqual(span.U, span.V);
            }
        }

        [Fact]

        public void TheSpanCoversTheFacesArea()
        {

            Vector3I min = new Vector3I(1, 1, 1);

            Vector3I size = new Vector3I(2, 3, 4);
            Vector3I maxExclusive = min + size;

            for (int face = 0; face < Face.Count; face++)
            {
                BoxGeometry.FaceSpan span = BoxGeometry.Span(min, maxExclusive, face);

                int cells = (span.MaxExclusiveU - span.MinU) * (span.MaxExclusiveV - span.MinV);
                int expected = BoxGeometry.Component(size, span.U) * BoxGeometry.Component(size, span.V);

                Assert.Equal(expected, cells);
            }
        }
    }
}
