using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **The shape term, and whether it separates the pair a projected area cannot.**
    ///
    /// <para>
    /// `DragShapeTests` is the counter-example this exists to answer: a brick and a stair-stepped
    /// wedge sharing a frontal cross-section compute identical drag, because the sum is over six
    /// axis normals and the steps project onto the square the brick presents. `ShapeNormal` gives
    /// each node an effective normal read from the cells around it, and the Newtonian `sin²θ` that
    /// the projected area is missing. These are the readings that say whether it worked.
    /// See thermal-model.md, The shape term, and backlog.md `K22`.
    /// </para>
    /// </summary>
    public class ShapeDragTests
    {
        private const float ThickAir = 1f;
        private const float Speed = 120f;

        private static ThermalSettings Settings(bool shape)
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.EnableDamage = false;
            settings.EnableShapeDrag = shape;
            settings.Derive();
            return settings;
        }

        /// <summary>A solid block, four cells on a side.</summary>
        private static ThermalSimulation Brick(bool shape)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.HeavyArmor(), Vector3I.Zero, new Vector3I(4, 4, 4));
            return Built(builder, shape);
        }

        /// <summary>
        /// The same hull as `DragShapeTests` builds: four cells of frontal cross-section, tapering
        /// away behind it as a staircase. `Worlds.Flight` moves the ship backward, so the stepped
        /// surface is the one facing the wind.
        /// </summary>
        private static ThermalSimulation Wedge(bool shape)
        {
            GridBuilder builder = GridBuilder.Large();

            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    for (int z = 0; z < 4 - y; z++)
                    {
                        builder.Place(Catalog.HeavyArmor(), new Vector3I(x, y, z));
                    }
                }
            }

            return Built(builder, shape);
        }

        private static ThermalSimulation Built(GridBuilder builder, bool shape)
        {
            ThermalSimulation simulation = builder.BuildSimulation(Settings(shape), 293.15f);
            simulation.Planet = PlanetThermalProperties.Default();
            return simulation;
        }

        private static float DragWatts(ThermalSimulation simulation)
        {
            simulation.StepExact(1, Worlds.Flight(ThickAir, Speed));
            return simulation.Solver.LastFrictionWatts;
        }

        /// <summary>
        /// **The finding the term exists for: with it on, the wedge and the brick come apart.**
        /// Off they are **172,800 W each**, equal to three decimal places, which is
        /// `DragShapeTests`' claim re-asserted here so the pair is read the same way in both
        /// places. On, the brick reads **100,800 W** and the wedge **82,215 W** — a ratio of
        /// **0.816**, where there was no ratio at all.
        /// </summary>
        [Fact]
        public void TheShapeTermSeparatesTheWedgeFromTheBrick()
        {
            float flatBrick = DragWatts(Brick(false));
            float flatWedge = DragWatts(Wedge(false));
            Assert.Equal(flatBrick, flatWedge, 3);

            float brick = DragWatts(Brick(true));
            float wedge = DragWatts(Wedge(true));

            Assert.True(brick > 0f, "the brick took no drag, so this compares nothing");
            Assert.True(wedge < brick,
                "the wedge must take less drag than the brick it projects onto: "
                    + wedge + " against " + brick);

            // Pinned, because the size of the separation is the result rather than its sign: a
            // term that discriminated by a thousandth would pass the assertion above and be worth
            // nothing.
            Assert.Equal(0.816f, wedge / brick, 3);
        }

        /// <summary>
        /// **A stair-stepped 45° surface reads as 45°, which is the whole mechanism in one
        /// assertion.** The cells down the wedge's windward staircase reconstruct to
        /// `(0, 0.707, 0.707)` exactly — the diagonal — and so carry `sin²45° = 0.5`. Nothing about
        /// a single cell's own six faces could produce that: its tread is square to the flow and
        /// its riser is parallel to it.
        /// </summary>
        [Fact]
        public void AStairSteppedSlopeReconstructsToItsMeanSurface()
        {
            ThermalSimulation wedge = Wedge(true);
            CellBitset occupancy = wedge.Grid.Occupancy();

            foreach (Vector3I cell in new[] { new Vector3I(1, 1, 2), new Vector3I(1, 2, 1) })
            {
                Vector3 normal = ShapeNormal.Of(occupancy, wedge.Grid.GetAtCell(cell));

                Assert.Equal(0f, normal.X, 3);
                Assert.Equal(0.707f, normal.Y, 3);
                Assert.Equal(0.707f, normal.Z, 3);
                Assert.Equal(0.5f, ShapeNormal.Factor(normal, Vector3.Backward), 3);
            }
        }

        /// <summary>
        /// **A flat plate square to the flow is what the term must not move**, and the brick's own
        /// interior front cells are that plate: reconstructed, they point straight into the wind
        /// and carry a factor of one. The brick as a whole still drops to **0.583** of its
        /// unshaped drag, and that is edges — a four-cell cube is nearly all edge, and the fraction
        /// falls with hull size. It is also why `DragCoefficient` has to be re-scored on the
        /// population before this could ship on (backlog.md `K22`).
        /// </summary>
        [Fact]
        public void AFlatFaceSquareToTheFlowIsUnchangedAndTheEdgesAreNot()
        {
            ThermalSimulation brick = Brick(true);
            CellBitset occupancy = brick.Grid.Occupancy();

            Vector3 middle = ShapeNormal.Of(occupancy, brick.Grid.GetAtCell(new Vector3I(1, 1, 3)));
            Assert.Equal(1f, ShapeNormal.Factor(middle, Vector3.Backward), 3);

            Assert.Equal(0.583f, DragWatts(Brick(true)) / DragWatts(Brick(false)), 3);
        }

        /// <summary>
        /// **The shape term moves temperatures, and this is how far — the reading that keeps the
        /// switch off.**
        ///
        /// <para>
        /// The friction watts it scales are what warm a hull, so this is a change to the heat model
        /// and not only to the force one. At reentry conditions — 300 m/s in 0.8 density air, the
        /// scenario the corpus uses — a settled four-cell brick peaks **344.80 K** with the term off
        /// and **334.83 K** with it on, **9.96 K cooler**; the stair-stepped wedge reads 343.63 K
        /// against 332.27 K, **11.36 K**. Both larger than the 7.4 K windward shielding was worth,
        /// which is the change that already keeps *that* switch off.
        /// </para>
        ///
        /// <para>
        /// **And the sign is the opposite of shielding's, which is a consequence of where the factor
        /// is applied rather than a coincidence.** Shielding multiplies the six-face sum that the
        /// convection factor reads too, so a sheltered hull loses less to moving air and runs
        /// *hotter*. The shape factor is applied to the friction row alone, so a shaped hull is
        /// heated less and runs *cooler*. If somebody later folds it into `windFactor`, this test is
        /// what notices.
        /// </para>
        /// </summary>
        [Fact]
        public void TheShapeTermMovesTemperaturesAndNotOnlyDrag()
        {
            ThermalSimulation off = Heated(false);
            ThermalSimulation on = Heated(true);

            off.StepExact(600, Worlds.Flight(ReentryDensity, ReentrySpeed));
            on.StepExact(600, Worlds.Flight(ReentryDensity, ReentrySpeed));

            float hot = Peak(off);
            float shaped = Peak(on);

            Assert.True(hot > 0f && shaped > 0f);
            Assert.True(shaped < hot,
                "the shaped hull is not cooler, so the term is not reaching the friction that heats"
                    + " it — off " + hot + " K, on " + shaped + " K");

            // Pinned, because the *size* is what decides whether this can ever be a default: a term
            // worth a tenth of a kelvin would need no corpus walk at all.
            Assert.Equal(9.96f, hot - shaped, 1);
        }

        /// <summary>The brick with the environment live, so convection and friction both run — the
        /// drag rigs above switch it off to isolate the friction term.</summary>
        private static ThermalSimulation Heated(bool shape)
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableSolarHeat = false;
            settings.EnableDamage = false;
            settings.EnableShapeDrag = shape;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.HeavyArmor(), Vector3I.Zero, new Vector3I(4, 4, 4));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.Planet = PlanetThermalProperties.Default();
            return simulation;
        }

        private static float Peak(ThermalSimulation simulation)
        {
            float peak = 0f;
            for (int i = 0; i < simulation.Solver.Nodes.Count; i++)
            {
                float t = simulation.Solver.Nodes[i].Temperature;
                if (t > peak) peak = t;
            }

            return peak;
        }

        /// <summary>The `reentry` scenario's air: 300 m/s in 0.8 density.</summary>
        private const float ReentryDensity = 0.8f;
        private const float ReentrySpeed = 300f;

        /// <summary>
        /// **What a wider neighbourhood buys, which is why the radius is one.**
        ///
        /// <para>
        /// A 45° slope reads **0.500 — `sin²45°` exactly — at radius one, two and three alike**, and
        /// a 26.6° slope reads **0.134 against an ideal 0.200 at all three**. So widening does not
        /// improve the accuracy it was wanted for. What it buys is separating slopes *below* about
        /// 27°, which radius one conflates — an 18.4° ramp reads 0.134 at radius one and 0.056 at
        /// radius two against an ideal 0.100, so it stops conflating and starts overshooting — and
        /// it costs 2.07× the pass (backlog.md `K22`).
        /// </para>
        ///
        /// <para>
        /// **The rig has to be wider than the neighbourhood or it measures its own edges.** The
        /// first version of this was four cells across, so a radius-two read reached past the hull
        /// in X and returned a lateral normal on a ramp that is uniform in X. Sixteen wide, the X
        /// component is nought at every radius, which is the check that the rest of the reading is
        /// about the slope.
        /// </para>
        /// </summary>
        [Theory]
        [InlineData(1, 0.500f)]
        [InlineData(2, 0.500f)]
        [InlineData(3, 0.500f)]
        public void AFortyFiveDegreeSlopeReadsTheSameAtEveryRadius(int radius, float expected)
        {
            ThermalSimulation ramp = Ramp(1);
            CellBitset occupancy = ramp.Grid.Occupancy();

            Vector3 normal = ShapeNormal.Of(occupancy, SlopeCell(ramp, 1), radius);

            Assert.Equal(0f, normal.X, 3);
            Assert.Equal(expected, ShapeNormal.Factor(normal, Vector3.Backward), 3);
        }

        /// <summary>
        /// **Radius one conflates every slope shallower than 45°**, which is the limit it is chosen
        /// with rather than a defect discovered in it: a 26.6° ramp and an 18.4° one both read
        /// 0.134.
        /// </summary>
        [Fact]
        public void RadiusOneCannotSeparateTheShallowSlopes()
        {
            float shallow = FactorOf(Ramp(2), 2, 1);
            float shallower = FactorOf(Ramp(3), 3, 1);

            Assert.Equal(0.134f, shallow, 3);
            Assert.Equal(0.134f, shallower, 3);
        }

        private static float FactorOf(ThermalSimulation ramp, int run, int radius)
        {
            return ShapeNormal.Factor(
                ShapeNormal.Of(ramp.Grid.Occupancy(), SlopeCell(ramp, run), radius),
                Vector3.Backward);
        }

        /// <summary>Sixteen cells across, so a radius-three read never leaves the hull sideways.</summary>
        private const int RampWidth = 16;
        private const int RampHeight = 8;

        /// <summary>A stair-stepped ramp at `1:run`, so run 1 is 45° and run 2 is 26.6°.</summary>
        private static ThermalSimulation Ramp(int run)
        {
            GridBuilder builder = GridBuilder.Large();
            for (int y = 0; y < RampHeight; y++)
            {
                for (int x = 0; x < RampWidth; x++)
                {
                    for (int z = 0; z < (RampHeight - y) * run; z++)
                    {
                        builder.Place(Catalog.HeavyArmor(), new Vector3I(x, y, z));
                    }
                }
            }

            ThermalSimulation simulation = builder.BuildSimulation(Settings(true), 293.15f);
            simulation.Planet = PlanetThermalProperties.Default();
            return simulation;
        }

        /// <summary>Mid-span, mid-height, on the sloping face — away from every edge of the rig.</summary>
        private static BlockInstance SlopeCell(ThermalSimulation ramp, int run)
        {
            int y = RampHeight / 2;
            return ramp.Grid.GetAtCell(
                new Vector3I(RampWidth / 2, y, (RampHeight - y) * run - 1));
        }

        /// <summary>
        /// **A node the pass has not reached yet reads as no correction, and that is what makes the
        /// pass safe to slice.** An unvisited node holds a zero normal; zero returns a factor of
        /// one, which is the full projected area — the model without the term. So a half-finished
        /// pass degrades toward the old answer and never past it, and there is no interruption that
        /// charges a hull less than it was charged before.
        /// </summary>
        [Fact]
        public void AnUnbuiltNormalReadsAsNoCorrection()
        {
            Assert.Equal(1f, ShapeNormal.Factor(Vector3.Zero, Vector3.Backward), 4);
            Assert.Equal(1f, ShapeNormal.Factor(Vector3.Zero, Vector3.Up), 4);
        }

        /// <summary>
        /// **A surface facing away from the wind takes no pressure**, which is Newtonian and is the
        /// other end of the same bound: the factor is never negative and never above one.
        /// </summary>
        [Fact]
        public void ASurfaceInTheLeeTakesNothingAndNothingExceedsOne()
        {
            Assert.Equal(0f, ShapeNormal.Factor(Vector3.Forward, Vector3.Backward), 4);
            Assert.Equal(1f, ShapeNormal.Factor(Vector3.Backward, Vector3.Backward), 4);
        }

        /// <summary>
        /// **The pass gets a seventh of the exposure budget because a node costs seven times as
        /// much**, which is measured rather than chosen: 195.5 ns a node against 28.1 on a
        /// 126,731-block hull. A budget sized like exposure's would put a 24.8 ms pass on one frame.
        /// </summary>
        [Fact]
        public void TheShapeNormalBudgetIsSizedAgainstWhatANodeCosts()
        {
            Assert.Equal(452, SimulationScheduler.ShapeNormalBudget(126731));
            Assert.Equal(3168, SimulationScheduler.ExposureBudget(126731));

            // Floored and capped, so a tiny grid still makes progress and a vast one cannot spend a
            // frame on it.
            Assert.Equal(32, SimulationScheduler.ShapeNormalBudget(1));
            Assert.Equal(512, SimulationScheduler.ShapeNormalBudget(10000000));
        }

        /// <summary>
        /// **The bound, which is what makes the term safe to switch on**: it carries a factor of
        /// `sin²θ`, at most one, so it may only reduce — the same bound
        /// <see cref="DragProfile"/> has. A world that turns it on cannot find a hull that heats
        /// or drags harder than it did.
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TheShapeTermOnlyEverReduces(bool wedge)
        {
            float without = DragWatts(wedge ? Wedge(false) : Brick(false));
            float with = DragWatts(wedge ? Wedge(true) : Brick(true));

            Assert.True(with <= without + 1e-3f,
                "the shape term raised drag, which it must never do: "
                    + with + " against " + without);
        }
    }
}
