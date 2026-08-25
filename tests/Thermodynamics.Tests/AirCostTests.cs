using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **Why the same retune costs more in vacuum and less in air**, which is the arithmetic under
    /// the finding that decided `C12`.
    ///
    /// <para>
    /// Substep demand is a conductance over a heat capacity. `HeatTimeScale` divides every
    /// capacity and touches nothing else, so lowering it divides *every* stiffness term at once;
    /// multiplying a block's conductivity restores the conduction term and only that one. A block
    /// whose stiffness comes from its neighbours therefore gets dearer by
    /// <c>conductivity × clock / 225</c>, and a block whose stiffness comes from the air around it
    /// gets cheaper by <c>clock / 225</c>. Two rigs, one of each, are what these tests are.
    /// </para>
    ///
    /// <para>
    /// **The population figures are not pinned here and cannot be.** That air is where the budget
    /// goes — p99 73.4 substeps at 200 m/s against the 64 the caps grant, against 7.8 in vacuum —
    /// is a measurement over 49 workshop hulls that live outside the repository, and it is scored
    /// by `tools/corpus/air.py`. What is checkable without them is the mechanism that produces it,
    /// and a rig that isolates one term is a better test of that than a hull where the two are
    /// mixed in proportions nobody chose (`E7`). See
    /// balance.md.
    /// </para>
    /// </summary>
    public class AirCostTests
    {
        /// <summary>The pair `C12`'s retune would ship: conductivity ×4 with the clock at 100.</summary>
        private const float CandidateClock = 100f;

        private const float CandidateConductivity = 4f;

        private const float ShippedClock = 225f;

        /// <summary>Thick air, as the battery's atmospheric scenarios use it.</summary>
        private const float ThickAir = 1f;

        /// <summary>A conductivity in the middle of the authored range, so ×4 is a real change.</summary>
        private const float BaseConductivity = 120f;

        private static ThermalSettings Settings(float clock, int frequency = 0)
        {
            ThermalSettings settings = new ThermalSettings();
            settings.HeatTimeScale = clock;
            if (frequency > 0) settings.Frequency = frequency;

            // Uncapped, because what is being read is what a step *demanded*. A cap would hand back
            // the cap and the ratio under test would come out at one whatever the model did.
            settings.MaxSubsteps = Hulls.Unbounded;
            settings.MaxElementVisitsPerStep = 0;
            return settings.Derive();
        }

        private static BlockModel Block(float conductivity, float mass, bool radiates = true)
        {
            BlockThermalProperties thermal = Catalog.DefaultThermal();
            thermal.Conductivity = BaseConductivity * conductivity;

            // The conduction rig turns radiation off, because a block in shadow still sheds to the
            // sky and that term is not multiplied by a conductivity either. Left on, the ratio
            // comes out at 1.74 against a projected 1.78 and the test would be reading the mixture
            // rather than the term it names.
            if (!radiates) thermal.Emissivity = 0f;

            return BlockModel.Solid("rig", Vector3I.One, mass, thermal);
        }

        /// <summary>
        /// What a step demanded on this rig after it has been driven far enough to have a gradient.
        ///
        /// Read after stepping rather than on a fresh grid: the estimate is taken from the state,
        /// and a grid nobody has stepped has never demanded anything.
        /// </summary>
        private static float Demand(ThermalSimulation simulation, EnvironmentSample world)
        {
            for (int i = 0; i < 40; i++) simulation.StepExact(1, world);
            return simulation.Solver.LastRequiredSubsteps;
        }

        /// <summary>
        /// **The conduction rig**: two blocks bolted together, one heavy and one light, in shadow.
        ///
        /// The light block's stiffness is the conductance to its neighbour over its own capacity,
        /// and there is nothing else in play — vacuum, so no air, and shadow, so no sun.
        /// </summary>
        private static ThermalSimulation Conduction(float conductivity, float clock)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Block(conductivity, 4000f, false), Vector3I.Zero);
            builder.Place(Block(conductivity, 40f, false), new Vector3I(1, 0, 0));

            ThermalSimulation simulation = builder.BuildSimulation(Settings(clock), 293.15f);
            simulation.RebuildAll();
            return simulation;
        }

        /// <summary>
        /// **The convection rig**: one light block alone, with nothing to conduct to.
        ///
        /// Every face is exposed, so in moving air its whole stiffness is the convective
        /// conductance over its capacity — the term a conductivity multiplier cannot reach.
        /// </summary>
        private static ThermalSimulation Convection(float conductivity, float clock, int frequency = 0)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Block(conductivity, 40f), Vector3I.Zero);

            ThermalSimulation simulation = builder.BuildSimulation(Settings(clock, frequency), 293.15f);
            simulation.RebuildAll();
            return simulation;
        }

        [Fact]
        public void AConductionLimitedBlockCostsConductivityTimesTheClock()
        {
            // The half of the mechanism that is exactly predictable, and the projection
            // `PairLab.Cell.ProjectedDemandRatio` prints. The corpus sweep found the paired
            // per-hull ratio sitting on this figure to three decimals in vacuum.
            float shipped = Demand(Conduction(1f, ShippedClock), Worlds.Shadow());
            float candidate = Demand(Conduction(CandidateConductivity, CandidateClock),
                Worlds.Shadow());

            Assert.True(shipped > 0f);
            Assert.Equal(CandidateConductivity * CandidateClock / ShippedClock,
                candidate / shipped, 2);
        }

        [Fact]
        public void AConvectionLimitedBlockCostsTheClockAloneAndTheRetuneMakesItCheaper()
        {
            // **The finding.** Conductivity ×4 cannot reach the convective term, so all that is
            // left of the retune here is the clock — and the clock going down makes the block
            // cheaper rather than dearer. This is why the cost column taken in vacuum said the
            // retune costs up to twice as much while the environment the budget is actually spent
            // in says it costs less than half.
            EnvironmentSample air = Worlds.Flight(ThickAir, 200f);

            float shipped = Demand(Convection(1f, ShippedClock), air);
            float candidate = Demand(Convection(CandidateConductivity, CandidateClock), air);

            Assert.True(shipped > 0f);
            Assert.True(candidate < shipped,
                "a block whose stiffness is the air around it went " + shipped + " to " + candidate
                + " substeps; the G6 argument for shipping the retune rests on that falling");

            Assert.Equal(CandidateClock / ShippedClock, candidate / shipped, 2);
        }

        [Fact]
        public void ConductivityDoesNotReachTheConvectiveTermAtAll()
        {
            // Stated on its own, because it is the step of the argument a reader is most likely to
            // doubt and the one the two ratios above are the consequence of.
            EnvironmentSample air = Worlds.Flight(ThickAir, 200f);

            float plain = Demand(Convection(1f, ShippedClock), air);
            float conductive = Demand(Convection(CandidateConductivity, ShippedClock), air);

            Assert.Equal(plain, conductive, 3);
        }

        [Fact]
        public void AirIsWhereTheSubstepBudgetGoes()
        {
            // The premise the whole pass rests on: the same block costs far more to integrate in
            // moving air than in vacuum, so a cost criterion scored in vacuum is scored where the
            // money is not being spent.
            float vacuum = Demand(Convection(1f, ShippedClock), Worlds.Shadow());
            float air = Demand(Convection(1f, ShippedClock), Worlds.Flight(ThickAir, 200f));

            Assert.True(air > vacuum * 4f,
                "the same block demanded " + air + " substeps in air against " + vacuum
                + " in vacuum; if convection has stopped being the term that sets the cost then"
                + " every atmospheric figure in balance.md needs re-reading");
        }

        [Fact]
        public void TheShippedClockSpendsMostOfTheCapOnAirAndTheRetunePullsItBack()
        {
            // The defect's shape, on a rig. Exceeding `MaxSubsteps` is not a slow step: the solver
            // refuses to divide finely enough and floors the block's capacity instead, so a block
            // over the cap is being simulated wrong rather than slowly.
            //
            // **Whether a hull actually crosses it is a population property and is not asserted
            // here.** One cube is not the stiffest block on a workshop ship, and tuning this rig
            // until it crossed would be fitting the fixture to the claim. What the rig can say is
            // that the shipped pair spends a large share of the cap on air alone and the retune
            // hands most of it back. The crossing itself is measured over 49 hulls — p99 73.4
            // against 64, fourteen of them over — and scored by `tools/corpus/air.py`.
            ThermalSettings shipped = new ThermalSettings().Derive();
            EnvironmentSample air = Worlds.Flight(ThickAir, 200f);

            float before = Demand(Convection(1f, ShippedClock), air);
            float after = Demand(Convection(CandidateConductivity, CandidateClock), air);

            Assert.True(before > shipped.MaxSubsteps / 4f,
                "a light exposed block at 200 m/s in thick air demanded " + before
                + " substeps against a cap of " + shipped.MaxSubsteps + ", which is a small enough"
                + " share that air is no longer where the budget goes");

            Assert.True(after < before / 2f,
                "the retune took the air demand from " + before + " to " + after
                + ", and the G6 argument for shipping it is that this roughly halves");
        }

        [Fact]
        public void DemandIsExactlyProportionalToTheStep()
        {
            // **Why every atmospheric figure balance.md carried was exactly half.** Substep demand
            // is a conductance times the step over a capacity, so halving the step halves it — and
            // `Frequency` moved from 8 to 4 after that table was taken, which doubled the step and
            // was never carried through to it. Measured on the same 49-ship panel, all eight of its
            // figures come out at 2.000x what it published, in four environments at once; this is
            // the only global factor of two that could do that.
            //
            // Pinned because the correction rests on it: if demand ever stopped scaling with the
            // step, the re-derived table would be wrong in a way nothing else would catch.
            EnvironmentSample air = Worlds.Flight(ThickAir, 200f);

            float quarterSecond = Demand(Convection(1f, ShippedClock, 4), air);
            float eighthSecond = Demand(Convection(1f, ShippedClock, 8), air);

            Assert.True(eighthSecond > 0f);
            Assert.Equal(2.0, quarterSecond / eighthSecond, 2);
        }

        [Fact]
        public void TheShippedPairIsNotOverTheCapInVacuum()
        {
            // The control that makes the row above a finding rather than an artefact of the rig:
            // the same block in vacuum is nowhere near the cap, so what is over it is the air.
            ThermalSettings shipped = new ThermalSettings().Derive();

            float vacuum = Demand(Convection(1f, ShippedClock), Worlds.Shadow());

            Assert.True(vacuum < shipped.MaxSubsteps / 4f,
                "the rig block demanded " + vacuum + " substeps in vacuum, which is close enough to"
                + " the cap that the atmospheric reading is not isolating the air");
        }
    }
}
