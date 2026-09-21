using System;
using System.Collections.Generic;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class OverlayBudgetTests
    {
/// <summary>Vector3D operation.</summary>
        private static readonly Vector3D Forward = new Vector3D(0, 0, -1);

/// <summary>Cone operation.</summary>
        private static void Cone(out double sin, out double cos)
        {
            double half = OverlayBudget.ConeHalfAngle(70.0 * Math.PI / 180.0, 16.0 / 9.0);
            sin = Math.Sin(half);
            cos = Math.Cos(half);
        }

/// <summary>InView operation.</summary>
        private static bool InView(Vector3D delta, double radius)
        {
            double sin, cos;
            Cone(out sin, out cos);

            Vector3D forward = Forward;
            return OverlayBudget.InView(ref delta, radius, ref forward, sin, cos);
        }

        [Fact]
/// <summary>ABoxStraightAheadIsDrawn operation.</summary>
        public void ABoxStraightAheadIsDrawn()
        {
            Assert.True(InView(new Vector3D(0, 0, -50), 1.25));
        }

        [Fact]
/// <summary>ABoxBehindTheCameraIsNot operation.</summary>
        public void ABoxBehindTheCameraIsNot()
        {
            Assert.False(InView(new Vector3D(0, 0, 50), 1.25));
            Assert.False(InView(new Vector3D(10, 4, 30), 1.25));
        }

        [Fact]
/// <summary>ABoxAroundTheCameraIsDrawn operation.</summary>
        public void ABoxAroundTheCameraIsDrawn()
        {
            Assert.True(InView(new Vector3D(0, 0, 0.5), 2.5));
        }

        [Fact]
/// <summary>ABoxOffToTheSideIsNot operation.</summary>
        public void ABoxOffToTheSideIsNot()
        {
            Assert.False(InView(new Vector3D(200, 0, -20), 1.25));
            Assert.False(InView(new Vector3D(0, 200, -20), 1.25));
        }

        [Fact]
/// <summary>EveryPointOfTheScreenSurvivesTheCull operation.</summary>
        public void EveryPointOfTheScreenSurvivesTheCull()
        {
            double fov = 70.0 * Math.PI / 180.0;
            double aspect = 16.0 / 9.0;
            double tangent = Math.Tan(fov * 0.5);

            for (double x = -1; x <= 1.0001; x += 0.1)
            {
                for (double y = -1; y <= 1.0001; y += 0.1)
                {
/// <summary>Vector3D operation.</summary>
                    Vector3D delta = new Vector3D(x * tangent * aspect * 100, y * tangent * 100, -100);

                    Assert.True(InView(delta, 0.0), "screen point " + x + "," + y + " was culled");
                }
            }
        }

        [Fact]
/// <summary>ABoxOutsideTheConeSurvivesByItsOwnRadius operation.</summary>
        public void ABoxOutsideTheConeSurvivesByItsOwnRadius()
        {
/// <summary>Vector3D operation.</summary>
            Vector3D delta = new Vector3D(0, 200, -100);

            Assert.False(InView(delta, 0.0));
            Assert.True(InView(delta, 40.0));
        }


/// <summary>Frame operation.</summary>
        private static int Frame(OverlayBudget budget, int side, double spacing)
        {
            budget.BeginFrame();

            double sin, cos;
            Cone(out sin, out cos);
            Vector3D forward = Forward;

            for (int x = 0; x < side; x++)
            {
                for (int y = 0; y < side; y++)
                {
                    for (int z = 0; z < side; z++)
                    {
/// <summary>Vector3D operation.</summary>
                        Vector3D delta = new Vector3D(
                            (x - (side * 0.5)) * spacing,
                            (y - (side * 0.5)) * spacing,
                            -((z + 1) * spacing));

                        if (!OverlayBudget.InView(ref delta, spacing * 0.5, ref forward, sin, cos))
                        {
                            budget.Cull();
                            continue;
                        }

                        budget.Accept(delta.Length());
                    }
                }
            }

            budget.EndFrame();
            return budget.Drawn;
        }

        [Fact]
/// <summary>AGridInsideTheBudgetIsDrawnWholeAndNeverLimited operation.</summary>
        public void AGridInsideTheBudgetIsDrawnWholeAndNeverLimited()
        {
/// <summary>OverlayBudget operation.</summary>
            OverlayBudget budget = new OverlayBudget();
            budget.MaxBoxes = 12000;

/// <summary>Frame operation.</summary>
            int drawn = Frame(budget, 10, 2.5);

            Assert.Equal(budget.Considered - budget.OffScreen, drawn);
            Assert.Equal(0, budget.OverBudget);
            Assert.False(budget.IsLimiting);
        }

        [Fact]
/// <summary>TheFirstFrameOverTheBudgetIsStillCapped operation.</summary>
        public void TheFirstFrameOverTheBudgetIsStillCapped()
        {
/// <summary>OverlayBudget operation.</summary>
            OverlayBudget budget = new OverlayBudget();
            budget.MaxBoxes = 500;

            Assert.Equal(500, Frame(budget, 30, 2.5));
            Assert.True(budget.OverBudget > 0);
        }

        [Fact]
/// <summary>TheRadiusSettlesNearTheBudgetWithinAFewFrames operation.</summary>
        public void TheRadiusSettlesNearTheBudgetWithinAFewFrames()
        {
/// <summary>OverlayBudget operation.</summary>
            OverlayBudget budget = new OverlayBudget();
            budget.MaxBoxes = 2000;

/// <summary>List operation.</summary>
            List<int> drawn = new List<int>();
            for (int frame = 0; frame < 8; frame++)
            {
                drawn.Add(Frame(budget, 40, 1.0));
            }

            for (int frame = 4; frame < drawn.Count; frame++)
            {
                Assert.InRange(drawn[frame], 1500, 2000);
            }
        }

        [Fact]
/// <summary>ASettledFrameDropsNothingToTheHardStop operation.</summary>
        public void ASettledFrameDropsNothingToTheHardStop()
        {
/// <summary>OverlayBudget operation.</summary>
            OverlayBudget budget = new OverlayBudget();
            budget.MaxBoxes = 2000;

            for (int frame = 0; frame < 8; frame++) Frame(budget, 40, 1.0);

            Assert.Equal(0, budget.Capped);
            Assert.True(budget.Beyond > 0);
        }

        [Fact]
/// <summary>TheRadiusLiftsAgainWhenTheGridNoLongerNeedsIt operation.</summary>
        public void TheRadiusLiftsAgainWhenTheGridNoLongerNeedsIt()
        {
/// <summary>OverlayBudget operation.</summary>
            OverlayBudget budget = new OverlayBudget();
            budget.MaxBoxes = 2000;

            for (int frame = 0; frame < 8; frame++) Frame(budget, 40, 1.0);
            Assert.True(budget.IsLimiting);

            for (int frame = 0; frame < 10; frame++) Frame(budget, 8, 1.0);

            Assert.False(budget.IsLimiting);
            Assert.Equal(0, budget.OverBudget);
        }

        [Fact]
/// <summary>TheRadiusNeverFallsBelowSomethingWorthDrawing operation.</summary>
        public void TheRadiusNeverFallsBelowSomethingWorthDrawing()
        {
/// <summary>OverlayBudget operation.</summary>
            OverlayBudget budget = new OverlayBudget();
            budget.MaxBoxes = 1;

            for (int frame = 0; frame < 40; frame++) Frame(budget, 30, 1.0);

            Assert.True(budget.Radius >= OverlayBudget.MinimumRadius);
            Assert.True(budget.Drawn >= 1);
        }

        [Fact]
/// <summary>ABudgetOfZeroDrawsNothingAndDoesNotDivideByIt operation.</summary>
        public void ABudgetOfZeroDrawsNothingAndDoesNotDivideByIt()
        {
/// <summary>OverlayBudget operation.</summary>
            OverlayBudget budget = new OverlayBudget();
            budget.MaxBoxes = 0;

            Assert.Equal(0, Frame(budget, 10, 1.0));
            Assert.True(budget.OverBudget > 0);
        }

        [Fact]
/// <summary>TheConeContainsTheFrustumAtEveryAspect operation.</summary>
        public void TheConeContainsTheFrustumAtEveryAspect()
        {
            double fov = 60.0 * Math.PI / 180.0;

            foreach (double aspect in new[] { 0.5, 1.0, 16.0 / 9.0, 32.0 / 9.0 })
            {
                double half = OverlayBudget.ConeHalfAngle(fov, aspect);
                double tangent = Math.Tan(fov * 0.5);

/// <summary>Vector3D operation.</summary>
                Vector3D corner = new Vector3D(tangent * aspect, tangent, -1);
                double angle = Math.Acos(Vector3D.Dot(Vector3D.Normalize(corner), Forward));

                Assert.True(half >= angle - 1e-9, "aspect " + aspect + " corner outside the cone");
            }
        }
    }
}
