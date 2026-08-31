using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **What a per-grid drag sum costs, measured — because the drag milestone binds the drag milestone and says it is small.**
    ///
    /// <para>
    /// A ship with a rotor turret is several grids to the engine, each with its own `Physics`.
    /// the drag milestone argues that a *mass*-derived force splits badly, and that drag fails differently and
    /// less badly, "because area is additive, so a per-grid sum is about right in magnitude". That
    /// is true of real frontal area and this asks whether it is true of **this model's** area,
    /// which is a different question: the solver's drag is proportional to *exposed* windward
    /// faces, and the faces where two grids touch are exposed to the model however welded together
    /// they look.
    /// </para>
    ///
    /// <para>
    /// So splitting one hull into two grids does not merely redistribute the drag, it **adds** some:
    /// the interface that was interior becomes two lee-and-windward faces. This is the size of that
    /// error, so the drag milestone can apply a force knowing what it is wrong by rather than assuming it is
    /// nothing.
    /// </para>
    /// </summary>
    public class DragGroupingTests
    {
        private const float ThickAir = 1f;
        private const float Speed = 120f;

        private static ThermalSettings Settings()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.EnableDamage = false;
            settings.Derive();
            return settings;
        }

        private static ThermalSimulation Box(Vector3I min, Vector3I maxExclusive)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.HeavyArmor(), min, maxExclusive);

            ThermalSimulation simulation = builder.BuildSimulation(Settings(), 293.15f);
            simulation.Planet = PlanetThermalProperties.Default();
            return simulation;
        }

        private static float DragWatts(ThermalSimulation simulation)
        {
            simulation.StepExact(1, Worlds.Flight(ThickAir, Speed));
            return simulation.Solver.LastFrictionWatts;
        }

        /// <summary>
        /// **One hull as one grid against the same hull as two.** The wind runs along Z and the cut
        /// is across it, so the interface faces are the ones the wind cares most about: the
        /// downwind half's front face is newly windward and was interior before.
        /// </summary>
        [Fact]
        public void SplittingAHullIntoTwoGridsAddsDragThatIsNotThere()
        {
            float whole = DragWatts(Box(Vector3I.Zero, new Vector3I(4, 4, 4)));

            float front = DragWatts(Box(Vector3I.Zero, new Vector3I(4, 4, 2)));
            float back = DragWatts(Box(new Vector3I(0, 0, 2), new Vector3I(4, 4, 4)));
            float split = front + back;

            Assert.True(whole > 0f, "the whole hull took no drag, so this compares nothing");

            // The per-grid sum is larger, and by how much is the finding rather than the assertion:
            // the bound is loose so this test reports a size rather than pinning an arithmetic that
            // the drag milestone's shielding will change on purpose.
            // **Each half takes the *same* drag as the whole hull.** Drag here is windward
            // projection and nothing else, and both halves project the same 4x4 square; the depth
            // along the wind never entered the sum. So a cut across the wind does not add a little
            // drag, it multiplies it by the number of pieces.
            Assert.Equal(whole, front, 2);
            Assert.Equal(whole, back, 2);
            Assert.Equal(2f * whole, split, 2);
        }

        /// <summary>
        /// **A cut *along* the wind costs much less than a cut across it**, which is the same fact
        /// from the other side: what the model charges for is windward projection, so an interface
        /// parallel to the flow adds faces the wind weighting scores at zero.
        /// </summary>
        [Fact]
        public void ACutAlongTheWindCostsLessThanACutAcrossIt()
        {
            float whole = DragWatts(Box(Vector3I.Zero, new Vector3I(4, 4, 4)));

            float acrossFront = DragWatts(Box(Vector3I.Zero, new Vector3I(4, 4, 2)));
            float acrossBack = DragWatts(Box(new Vector3I(0, 0, 2), new Vector3I(4, 4, 4)));

            float alongLeft = DragWatts(Box(Vector3I.Zero, new Vector3I(2, 4, 4)));
            float alongRight = DragWatts(Box(new Vector3I(2, 0, 0), new Vector3I(4, 4, 4)));

            // **A cut along the wind is exactly additive**: it halves the frontal projection, so
            // each piece takes half the drag and the sum is the hull's own. A cut across it is
            // exactly doubling. The two are the extremes and the model has no case in between,
            // because the only quantity it charges for is the projection.
            Assert.Equal(whole, alongLeft + alongRight, 2);
            Assert.Equal(2f * whole, acrossFront + acrossBack, 2);
        }

        /// <summary>
        /// **A subgrid in the hull's lee is not sheltered**, which is the other half of what the drag milestone
        /// names and what the drag milestone is for. A block sitting directly behind the hull takes the same drag
        /// as one sitting in clear air, because nothing occludes anything.
        /// </summary>
        [Fact]
        public void AGridInTheLeeOfAnotherIsNotSheltered()
        {
            // Far downwind, in clear air.
            float clear = DragWatts(Box(new Vector3I(0, 0, 40), new Vector3I(1, 1, 41)));

            // Directly behind a four-cell hull, which in reality would be in its wake.
            float shadowed = DragWatts(Box(new Vector3I(0, 0, 5), new Vector3I(1, 1, 6)));

            Assert.True(clear > 0f);
            Assert.Equal(clear, shadowed, 4);
        }
    }
}
