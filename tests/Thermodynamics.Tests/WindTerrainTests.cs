using System;
using Thermodynamics.Core;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// What the ground does to the wind over it: speeds it up over a rise, shelters behind a ridge,
    /// and steers it along a valley.
    ///
    /// Every case here is a piece of terrain built by hand as a ring of relative heights — a cone, a
    /// wall, a trench, an even slope — so the shapes are unambiguous and the expected answer is the
    /// one anybody who has stood on that shape would give.
    /// </summary>
    public class WindTerrainTests
    {
        private static readonly Vector3 North = new Vector3(0f, 0f, -1f);
        private static readonly Vector3 East = new Vector3(1f, 0f, 0f);

        private const float Inner = 150f;
        private const float Outer = 300f;

        /// <summary>Flat ground: every sample level with the site.</summary>
        private static float[] Flat()
        {
            return new float[WindTerrain.SampleCount];
        }

        /// <summary>A hill, with the site on top: the land falls away by <paramref name="fall"/>.</summary>
        private static float[] Hill(float fall)
        {
            float[] heights = new float[WindTerrain.SampleCount];
            for (int i = 0; i < WindTerrain.Bearings; i++)
            {
                heights[WindTerrain.Index(0, i)] = -fall * 0.5f;
                heights[WindTerrain.Index(1, i)] = -fall;
            }
            return heights;
        }

        /// <summary>A bowl, with the site at the bottom.</summary>
        private static float[] Hollow(float rise)
        {
            float[] heights = Hill(rise);
            for (int i = 0; i < heights.Length; i++) heights[i] = -heights[i];
            return heights;
        }

        /// <summary>
        /// An even slope through the site, rising toward the given bearing index. The site is neither
        /// exposed nor sheltered — it is halfway up a hillside.
        /// </summary>
        private static float[] Slope(int towards, float rise)
        {
            float[] heights = new float[WindTerrain.SampleCount];
            double axis = towards * (2d * Math.PI / WindTerrain.Bearings);

            for (int i = 0; i < WindTerrain.Bearings; i++)
            {
                double angle = i * (2d * Math.PI / WindTerrain.Bearings);
                float along = (float)Math.Cos(angle - axis);

                heights[WindTerrain.Index(0, i)] = rise * 0.5f * along;
                heights[WindTerrain.Index(1, i)] = rise * along;
            }
            return heights;
        }

        /// <summary>
        /// A valley running along the axis through bearings <c>axis</c> and its opposite:
        /// the two sides across from it stand high, the ends are open.
        /// </summary>
        private static float[] Valley(int axis, float wallHeight)
        {
            float[] heights = new float[WindTerrain.SampleCount];

            for (int i = 0; i < WindTerrain.Bearings; i++)
            {
                double angle = (i - axis) * (2d * Math.PI / WindTerrain.Bearings);

                // High across the axis, level along it: the second harmonic a valley makes.
                float across = -(float)Math.Cos(2d * angle);
                float height = wallHeight * 0.5f * (1f + across);

                heights[WindTerrain.Index(0, i)] = height * 0.5f;
                heights[WindTerrain.Index(1, i)] = height;
            }
            return heights;
        }

        /// <summary>A wall on one bearing only, level everywhere else.</summary>
        private static float[] Wall(int bearing, float height)
        {
            float[] heights = new float[WindTerrain.SampleCount];
            heights[WindTerrain.Index(0, bearing)] = height;
            heights[WindTerrain.Index(1, bearing)] = height;
            return heights;
        }

        // ---- exposure ------------------------------------------------------------------------

        [Fact]
        public void FlatGroundChangesNothing()
        {
            Assert.Equal(0f, WindTerrain.Relief(Flat(), Outer), 5);
            Assert.Equal(1f, WindTerrain.SpeedUp(WindTerrain.Relief(Flat(), Outer)), 5);
        }

        [Fact]
        public void ASummitIsWindierThanThePlainBelowIt()
        {
            // The measured effect this exists to reproduce: a hill top commonly runs two to three
            // times the valley wind. The cap keeps this model on the modest end of that.
            float relief = WindTerrain.Relief(Hill(100f), Outer);
            Assert.True(relief > 0f);

            float factor = WindTerrain.SpeedUp(relief);
            Assert.True(factor > 1.4f, "a hundred metres of relief in three hundred should be a real gain");
        }

        [Fact]
        public void AHollowIsSheltered()
        {
            Assert.True(WindTerrain.SpeedUp(WindTerrain.Relief(Hollow(100f), Outer)) < 0.8f);
        }

        [Fact]
        public void AHillsideIsNeither()
        {
            // Exposure is about standing proud of the land, not about being on a slope. An even
            // slope rises as much on one side as it falls on the other, and this has to say so —
            // otherwise every mountainside in the world reads as either a summit or a pit.
            Assert.Equal(0f, WindTerrain.Relief(Slope(0, 200f), Outer), 4);
        }

        [Fact]
        public void NeitherSpeedUpNorSlowDownRunsAway()
        {
            // Steep ground is exactly where the linear theory behind the 2H/L figure stops being
            // true, so the number it produces there is capped rather than trusted.
            Assert.Equal(1f + WindTerrain.MaximumSpeedUp, WindTerrain.SpeedUp(10f), 4);
            Assert.Equal(1f - WindTerrain.MaximumSlowDown, WindTerrain.SpeedUp(-10f), 4);
            Assert.True(WindTerrain.SpeedUp(-10f) > 0f, "sheltering must never reverse the wind");
        }

        // ---- shelter -------------------------------------------------------------------------

        [Fact]
        public void AWallUpwindTakesTheWindAway()
        {
            // North is bearing 0. A wall to the north shelters a wind blowing *from* the north,
            // which is a wind blowing toward the south.
            float[] terrain = Wall(0, 120f);
            Vector3 fromTheNorth = -North;

            float sheltered = WindTerrain.Shelter(terrain, Inner, Outer, fromTheNorth, North, East);
            Assert.True(sheltered < 0.6f, "a wall in the way should cost most of the wind");
        }

        [Fact]
        public void TheSameWallDoesNothingToAWindComingTheOtherWay()
        {
            // The half that makes it shelter rather than a general slowdown: a wind coming from the
            // clear side does not care what is behind the site.
            float[] terrain = Wall(0, 120f);

            Assert.Equal(1f, WindTerrain.Shelter(terrain, Inner, Outer, North, North, East), 4);
        }

        [Fact]
        public void ShelterTurnsWithTheWind()
        {
            // Walking the wind round the compass past a wall on one bearing has to give one
            // minimum, at the bearing the wall is on, and no others.
            float[] terrain = Wall(2, 120f);

            float worst = 2f;
            int worstBearing = -1;

            for (int i = 0; i < WindTerrain.Bearings; i++)
            {
                Vector3 from = WindTerrain.BearingDirection(i, North, East);
                float factor = WindTerrain.Shelter(terrain, Inner, Outer, -from, North, East);

                if (factor < worst)
                {
                    worst = factor;
                    worstBearing = i;
                }
            }

            Assert.Equal(2, worstBearing);
        }

        [Fact]
        public void ANearObstructionSheltersMoreThanAFarOneOfTheSameHeight()
        {
            // Because it is the angle that matters, not the height. This is why the sample carries
            // two radii instead of one.
            float[] near = new float[WindTerrain.SampleCount];
            near[WindTerrain.Index(0, 0)] = 60f;

            float[] far = new float[WindTerrain.SampleCount];
            far[WindTerrain.Index(1, 0)] = 60f;

            Vector3 fromTheNorth = -North;

            Assert.True(
                WindTerrain.Shelter(near, Inner, Outer, fromTheNorth, North, East)
                < WindTerrain.Shelter(far, Inner, Outer, fromTheNorth, North, East));
        }

        /// <summary>
        /// The saturation keeps both ends of the law it replaced: the literature's gradient at zero
        /// and the same bound at infinity.
        ///
        /// **A clip has those too — what it does not have is the middle.** Every terrain factor here
        /// was a linear law followed by a clip, and SE's ground is steep enough that every site ran
        /// past the clip, so terrain read the same number everywhere and stopped telling one place
        /// from another. [backlog](../../docs/backlog.md) `B19`.
        /// </summary>
        [Fact]
        public void SaturationKeepsTheGradientAndTheBound()
        {
            const float Bound = 0.6f;

            // Gentle ground is the linear law to within a per cent, which is the whole point: the
            // figure it comes from is a real one and applies exactly where it was measured.
            foreach (float value in new[] { 0.001f, 0.01f, 0.05f })
            {
                float saturated = WindTerrain.Saturate(value, Bound);
                Assert.True(Math.Abs(saturated - value) < value * 0.02f,
                    "gentle ground moved by more than a per cent: " + value + " to " + saturated);
            }

            // The bound is the same bound: approached, and never crossed. It is *reached* far out,
            // where `tanh` saturates to one in float — which is the arithmetic rather than the
            // model, and lands well past any ground.
            foreach (float value in new[] { 1f, 10f, 1000f })
            {
                float saturated = WindTerrain.Saturate(value, Bound);

                Assert.True(saturated <= Bound + 1e-6f, "the bound was crossed at " + value);
                Assert.True(saturated > Bound * 0.9f, "the bound was not approached at " + value);
            }

            Assert.True(WindTerrain.Saturate(1f, Bound) < Bound,
                "a relief of one should still be short of the bound");

            Assert.Equal(0f, WindTerrain.Saturate(0f, Bound), 6);
            Assert.Equal(0f, WindTerrain.Saturate(1f, 0f), 6);
        }

        /// <summary>
        /// **The property the clip made impossible.** Two slopes that both ran past the old clip
        /// gave the same answer; a steeper ridge is now a windier one all the way up.
        /// </summary>
        [Fact]
        public void ASteeperRidgeIsStillWindierThanASteepOne()
        {
            // Over the range SE ground actually produces. Far beyond it `tanh` saturates to one in
            // float and the curve does go flat — that is the arithmetic, and it is past any hill.
            float previous = 0f;

            for (float relief = 0.1f; relief <= 2f; relief += 0.1f)
            {
                float speedUp = WindTerrain.SpeedUp(relief);

                Assert.True(speedUp > previous,
                    "speed-up stopped rising at a relief of " + relief.ToString("n1"));
                Assert.True(speedUp <= 1f + WindTerrain.MaximumSpeedUp,
                    "speed-up passed its bound at " + relief.ToString("n1") + ": " + speedUp);

                previous = speedUp;
            }

            // And the same downhill, where the bound is a different one.
            previous = float.MaxValue;
            for (float relief = -0.1f; relief >= -2f; relief -= 0.1f)
            {
                float speedUp = WindTerrain.SpeedUp(relief);

                Assert.True(speedUp < previous,
                    "a deeper hollow stopped being calmer at " + relief.ToString("n1"));
                Assert.True(speedUp >= 1f - WindTerrain.MaximumSlowDown,
                    "a hollow passed its bound at " + relief.ToString("n1") + ": " + speedUp);

                previous = speedUp;
            }
        }

        /// <summary>
        /// Open ground shelters nothing, and the floor is approached rather than sat on.
        ///
        /// The shelter share saturates onto its bound instead of clipping to it, so a wall a
        /// hundred kilometres high gets arbitrarily close to the floor without a wall of a hundred
        /// metres reading the same number. See `WindTerrain.Saturate` and
        /// [backlog](../../docs/backlog.md) `B19`.
        /// </summary>
        [Fact]
        public void OpenGroundShelltersNothingAndTheFloorHolds()
        {
            Assert.Equal(1f, WindTerrain.Shelter(Flat(), Inner, Outer, North, North, East), 4);

            float[] cliff = Wall(0, 100000f);
            float least = WindTerrain.Shelter(cliff, Inner, Outer, -North, North, East);

            // **The floor is approached and never reached, and the reason is geometry.** An upwind
            // horizon cannot stand at more than ninety degrees, so the shelter share cannot exceed
            // `tanh(90 / FullShelterDegrees)` however tall the wall is. Computed rather than
            // written down, so moving either constant moves the expectation with it.
            float steepest = (float)System.Math.Tanh(90d / WindTerrain.FullShelterDegrees);
            float reachable = 1f - (WindTerrain.MaximumShelter * steepest);

            Assert.Equal(reachable, least, 0.001f);
            Assert.True(least > 1f - WindTerrain.MaximumShelter,
                "the floor is a bound, not a value the terrain can sit on: " + least);
            Assert.True(least > 0f, "even a wind shadow leaves something behind");
        }

        [Fact]
        public void GroundThatFallsAwayUpwindDoesNotShelter()
        {
            // Only terrain standing *above* the site can be in the way. A drop upwind is a view.
            Assert.Equal(1f, WindTerrain.Shelter(Hill(100f), Inner, Outer, -North, North, East), 4);
        }

        // ---- channelling ---------------------------------------------------------------------

        [Fact]
        public void AValleySteersTheWindAlongItself()
        {
            // A valley running north-south, and a wind trying to blow east across it. It should come
            // out running along the valley instead, which is what makes wind over terrain look like
            // weather rather than like a texture.
            float[] terrain = Valley(0, 150f);

            Vector3 channelled = WindTerrain.Channel(terrain, Outer, East, North, East, 1f);

            float alongValley = Math.Abs(Vector3.Dot(channelled, North));
            Assert.True(alongValley > 0.7f,
                "wind across a walled valley should be turned along it, got " + alongValley);
        }

        [Fact]
        public void ItPicksTheEndTheWindWasAlreadyHeadingFor()
        {
            // A valley has no direction of its own. Two winds crossing it from opposite sides must
            // leave down opposite ends, or the terrain would be inventing a flow.
            float[] terrain = Valley(0, 150f);

            Vector3 leaning = Vector3.Normalize(East + (North * 0.3f));
            Vector3 opposite = Vector3.Normalize(East - (North * 0.3f));

            float a = Vector3.Dot(WindTerrain.Channel(terrain, Outer, leaning, North, East, 1f), North);
            float b = Vector3.Dot(WindTerrain.Channel(terrain, Outer, opposite, North, East, 1f), North);

            Assert.True(a > 0f && b < 0f, "the two should leave by opposite ends: " + a + ", " + b);
        }

        [Fact]
        public void FlatGroundSteersNothing()
        {
            Vector3 channelled = WindTerrain.Channel(Flat(), Outer, East, North, East, 1f);
            Assert.Equal(East.X, channelled.X, 4);
            Assert.Equal(East.Z, channelled.Z, 4);
        }

        [Fact]
        public void AShallowValleySteersLessThanADeepOne()
        {
            Vector3 shallow = WindTerrain.Channel(Valley(0, 20f), Outer, East, North, East, 1f);
            Vector3 deep = WindTerrain.Channel(Valley(0, 150f), Outer, East, North, East, 1f);

            Assert.True(Math.Abs(Vector3.Dot(deep, North)) > Math.Abs(Vector3.Dot(shallow, North)));
        }

        [Fact]
        public void TurningItDownTurnsItDown()
        {
            float[] terrain = Valley(0, 150f);

            Vector3 full = WindTerrain.Channel(terrain, Outer, East, North, East, 1f);
            Vector3 half = WindTerrain.Channel(terrain, Outer, East, North, East, 0.4f);
            Vector3 none = WindTerrain.Channel(terrain, Outer, East, North, East, 0f);

            Assert.True(Math.Abs(Vector3.Dot(full, North)) > Math.Abs(Vector3.Dot(half, North)));
            Assert.Equal(East.X, none.X, 4);
        }

        [Fact]
        public void AChannelledWindIsStillAUnitVector()
        {
            // It steers the wind; the speed is decided elsewhere. A direction that quietly changed
            // length here would multiply into the speed twice.
            for (int axis = 0; axis < WindTerrain.Bearings; axis++)
            {
                Vector3 channelled = WindTerrain.Channel(
                    Valley(axis, 150f), Outer, East, North, East, 1f);

                Assert.Equal(1f, channelled.Length(), 4);
            }
        }

        [Fact]
        public void AWindWithNoDirectionStaysThatWay()
        {
            Assert.Equal(Vector3.Zero, WindTerrain.Channel(Valley(0, 150f), Outer, Vector3.Zero, North, East, 1f));
        }

        [Fact]
        public void AWindOnARidgeCrestIsSteeredOverItRatherThanAlongIt()
        {
            // A ridge is *not* a valley turned upside down as far as steering goes, and this is
            // where that shows. The rule is one thing — the wind is turned toward the lowest ground
            // in the ring — and it gives two different answers because the two landforms are
            // different:
            //
            //   in a valley, the lowest ground is along the valley floor, so the wind runs along it;
            //   on a ridge crest, the lowest ground is down either side, so the wind crosses it.
            //
            // Both are what air does. Flow does not run along a ridge line, it goes over the top,
            // which is across the crest. Written down because the first version of this test assumed
            // the two cases were mirror images and they are not.
            float[] valley = Valley(0, 150f);

            float[] ridge = new float[WindTerrain.SampleCount];
            for (int i = 0; i < ridge.Length; i++) ridge[i] = -valley[i];

            // The valley and the ridge both run north-south. A wind blowing east meets both.
            Vector3 inValley = WindTerrain.Channel(valley, Outer, East, North, East, 1f);
            Vector3 onCrest = WindTerrain.Channel(ridge, Outer, East, North, East, 1f);

            Assert.True(Math.Abs(Vector3.Dot(inValley, North)) > 0.7f,
                "the valley should turn the wind along itself");
            Assert.True(Math.Abs(Vector3.Dot(onCrest, North)) < 0.2f,
                "the crest should leave it crossing, not turn it along the ridge");
        }

        [Fact]
        public void TheRuleIsThatWindTurnsTowardTheLowestGround()
        {
            // The principle the two cases above share, tested directly: whatever the shape, the
            // channelled wind leans toward the side of the ring that sits lowest.
            for (int axis = 0; axis < WindTerrain.Bearings; axis++)
            {
                float[] terrain = Valley(axis, 150f);

                // The lower of the two ends of this valley's axis, as a direction.
                Vector3 low = WindTerrain.BearingDirection(axis, North, East);

                // Come at it from across, leaning slightly toward the low end so the sign is decided.
                Vector3 across = WindTerrain.BearingDirection(axis + 2, North, East);
                Vector3 wind = Vector3.Normalize(across + (low * 0.2f));

                Vector3 channelled = WindTerrain.Channel(terrain, Outer, wind, North, East, 1f);

                Assert.True(Vector3.Dot(channelled, low) > Vector3.Dot(wind, low),
                    "axis " + axis + " should have leaned further toward its open end");
            }
        }

        // ---- the sample layout ---------------------------------------------------------------

        [Fact]
        public void BearingsRunClockwiseFromNorth()
        {
            // Everything above depends on index 0 being north and 2 being east. If that convention
            // ever moves, the shelter tests would still pass while pointing the wrong way.
            Assert.Equal(North.Z, WindTerrain.BearingDirection(0, North, East).Z, 4);
            Assert.Equal(East.X, WindTerrain.BearingDirection(2, North, East).X, 4);
            Assert.Equal(-North.Z, WindTerrain.BearingDirection(4, North, East).Z, 4);
            Assert.Equal(-East.X, WindTerrain.BearingDirection(6, North, East).X, 4);
        }

        [Fact]
        public void EverySampleHasItsOwnSlot()
        {
            bool[] seen = new bool[WindTerrain.SampleCount];

            for (int radius = 0; radius < WindTerrain.Radii; radius++)
            {
                for (int bearing = 0; bearing < WindTerrain.Bearings; bearing++)
                {
                    int index = WindTerrain.Index(radius, bearing);
                    Assert.False(seen[index], "slot " + index + " was claimed twice");
                    seen[index] = true;
                }
            }
        }

        [Fact]
        public void AShortSampleIsRefusedRatherThanRead()
        {
            float[] stunted = new float[WindTerrain.SampleCount - 1];

            Assert.Equal(0f, WindTerrain.Relief(stunted, Outer), 5);
            Assert.Equal(1f, WindTerrain.Shelter(stunted, Inner, Outer, North, North, East), 5);
            Assert.Equal(1f, WindTerrain.Channel(stunted, Outer, East, North, East, 1f).Length(), 4);

            Assert.Equal(0f, WindTerrain.Relief(null, Outer), 5);
            Assert.Equal(1f, WindTerrain.Shelter(null, Inner, Outer, North, North, East), 5);
        }
    }
}
