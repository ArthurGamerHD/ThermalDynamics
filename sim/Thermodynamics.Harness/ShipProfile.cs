using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// Everything about one ship that can be known without running it forward.
    ///
    /// <para>
    /// This is the cheap pass of the balance lab, and it does two jobs at once. It answers the
    /// questions that need no solver — what the population is built from, how much heat it makes
    /// against how much surface it has to lose it through, and how stiff it is to integrate — and
    /// it produces the **feature vector** that everything downstream samples and selects on.
    /// </para>
    ///
    /// <para>
    /// The features are chosen so that two ships close together here are expected to behave the
    /// same way in the scenario battery. That expectation is the whole basis for reducing a corpus
    /// of ten thousand to a panel of a few hundred, and it is a claim that has to be checked rather
    /// than assumed — see <see cref="Specimens"/>.
    /// </para>
    /// </summary>
    public class ShipProfile
    {
        public string Name;
        public long WorkshopId;
        public bool Large;
        public int Blocks;

        /// <summary>Kilograms, summed from the definitions.</summary>
        public float Mass;

        /// <summary>Joules per kelvin, after <c>HeatTimeScale</c>. What the ship's heat lands in.</summary>
        public float HeatCapacity;

        /// <summary>Square metres of face that see the environment.</summary>
        public float ExposedArea;

        /// <summary>Share of blocks with any exposed face. The rest are interior.</summary>
        public float ExposedFraction;

        /// <summary>Watts of heat at full rating: every producer, consumer and thruster at once.</summary>
        public float WasteWatts;

        /// <summary>Installed electrical output, watts.</summary>
        public float PowerOutputWatts;

        /// <summary>
        /// Thrust in the strongest single direction, newtons.
        ///
        /// **Not the sum of every thruster**, which is the mistake the first version of this pass
        /// made and which inflated the load on thruster-heavy ships several-fold. A ship
        /// accelerates one way at a time: the thrusters facing that way burn and the ones facing
        /// the other five do not. The heaviest direction is the sustained case.
        /// </summary>
        public float ThrustNewtons;

        /// <summary>Thrust summed over every direction. The ceiling that never happens.</summary>
        public float ThrustNewtonsAllDirections;

        /// <summary>Vanilla heat vents fitted. The only cooling a stock ship can carry.</summary>
        public int HeatVents;

        /// <summary>
        /// Watts of heat per square metre of exposed surface, at full rating.
        ///
        /// **The single number this whole pass exists to produce.** A grid at equilibrium sheds
        /// what it makes, and what it sheds goes as the fourth power of temperature over its
        /// exposed area — so this is the driver of where a ship settles, and everything else is a
        /// second-order correction to it. If the scenario battery finds anything else predicting
        /// outcome better, that is a finding worth having.
        /// </summary>
        public float ThermalStress
        {
            get { return ExposedArea <= 0f ? 0f : WasteWatts / ExposedArea; }
        }

        /// <summary>
        /// The equilibrium temperature <see cref="ThermalStress"/> implies for a grey body in deep
        /// space, before conduction, geometry or the sun move it.
        ///
        /// A screening estimate rather than a prediction: it assumes every watt reaches the skin
        /// and every face sees a 2.7 K sky, both of which flatter the ship. A hull this says is hot
        /// is certainly hot; one it says is cool may still not be.
        /// </summary>
        public float EquilibriumKelvin(float emissivity = 0.15f)
        {
            if (ThermalStress <= 0f) return 0f;
            return (float)Math.Pow(ThermalStress / (emissivity * ThermalConstants.StefanBoltzmann), 0.25d);
        }

        /// <summary>
        /// Substeps the stiffest block on the ship demands of a one-second step.
        ///
        /// A step is divided into as many substeps as the stiffest block needs, so this is what a
        /// grid costs — and it is set by the *lightest* block on the ship rather than the average
        /// one, which is why a population is needed to know it. See stiffness.md.
        /// </summary>
        public float PeakSubstepDemand;

        /// <summary>The demand at the 95th percentile of blocks, which is the number a cap reaches.</summary>
        public float SubstepDemandP95;

        public float SubstepDemandMedian;

        /// <summary>The subtype that set <see cref="PeakSubstepDemand"/>. Names what to tune.</summary>
        public string StiffestBlock;

        /// <summary>Sealed compartments the room mapper found.</summary>
        public int Rooms;

        /// <summary>
        /// The feature vector used for stratifying, clustering and selecting specimens.
        ///
        /// Logarithmic where the quantity spans orders of magnitude, which is most of them: a ship
        /// of 30 blocks and one of 9,000 differ by a factor no linear distance can weigh sensibly
        /// against an exposure fraction between 0 and 1.
        /// </summary>
        public double[] Features
        {
            get
            {
                return new double[]
                {
                    Log(Blocks),
                    Log(Mass),
                    Log(ExposedArea),
                    ExposedFraction,
                    Log(WasteWatts),
                    Log(ThermalStress),
                    Log(PeakSubstepDemand),
                    Large ? 1d : 0d,
                };
            }
        }

        public static readonly string[] FeatureNames =
        {
            "log blocks", "log mass", "log area", "exposed fraction",
            "log waste W", "log stress", "log stiffness", "large grid",
        };

        private static double Log(double value)
        {
            return Math.Log10(value > 0d ? value + 1d : 1d);
        }

        /// <summary>
        /// Measures one ship. Builds its simulation once, reads it, and lets it go.
        ///
        /// Nothing is stepped: every figure here is a property of the grid as built. That is what
        /// makes this affordable over ten thousand ships, and it is why the expensive passes are
        /// sampled from what this produces rather than run over everything.
        /// </summary>
        public static ShipProfile Measure(Blueprints.Ship ship, ThermalSettings settings = null)
        {
            ThermalSimulation simulation = ship.Build(settings ?? new ThermalSettings());
            ThermalSolver solver = simulation.Solver;

            ShipProfile profile = new ShipProfile
            {
                Name = ship.Name,
                WorkshopId = ship.WorkshopId,
                Large = ship.Large,
                Blocks = ship.Blocks,
                Rooms = simulation.Rooms.Map.RoomCount,
            };

            List<float> demands = new List<float>(solver.Nodes.Count);
            float[] thrustByDirection = new float[Face.Count];
            float draw = 0f;
            float installed = 0f;
            int exposed = 0;
            float peak = 0f;

            for (int i = 0; i < solver.Nodes.Count; i++)
            {
                ThermalNode node = solver.Nodes[i];

                profile.Mass += node.Block.Mass;
                profile.HeatCapacity += node.ThermalMass;
                profile.ExposedArea += node.ExposedArea;
                if (node.TotalExposedFaces > 0) exposed++;

                float demand = solver.NodeSubstepDemand(i);
                demands.Add(demand);

                if (demand > peak)
                {
                    peak = demand;
                    profile.StiffestBlock = node.Block.Name;
                }

                Rate(profile, node.Block, thrustByDirection, ref draw, ref installed);
            }

            // A ship's reactors deliver what its consumers ask for and no more, so the load is
            // bounded by the draw rather than by the plate rating. Summing every reactor's maximum
            // output describes a ship with nothing switched on to use it.
            float thrust = 0f;
            for (int i = 0; i < thrustByDirection.Length; i++)
            {
                profile.ThrustNewtonsAllDirections += thrustByDirection[i];
                if (thrustByDirection[i] > thrust) thrust = thrustByDirection[i];
            }

            profile.ThrustNewtons = thrust;
            profile.PowerOutputWatts = installed;

            float produced = draw < installed ? draw : installed;
            profile.WasteWatts =
                (produced * BlockThermalDerivation.FunctionOf("Reactor").ProducerWasteEnergy)
                + profile.ConsumerWasteWatts
                + (thrust * BlockThermalDerivation.FunctionOf("Thrust").ConsumerWasteEnergy);

            profile.ExposedFraction = solver.Nodes.Count == 0
                ? 0f
                : exposed / (float)solver.Nodes.Count;

            profile.PeakSubstepDemand = peak;
            demands.Sort();
            profile.SubstepDemandMedian = Percentile(demands, 0.50f);
            profile.SubstepDemandP95 = Percentile(demands, 0.95f);

            return profile;
        }

        /// <summary>Heat from everything that draws power but does not thrust, watts.</summary>
        public float ConsumerWasteWatts;

        /// <summary>
        /// Adds one block's contribution to the ship's rated load.
        ///
        /// Rated rather than actual, because a blueprint has no session and so no idea what it
        /// would really draw. Full rating is the ceiling a ship could reach and the case the
        /// balance criteria are written against; the battery's `derate` scenario is what finds the
        /// fraction that is actually sustainable.
        ///
        /// Thrust is bucketed by which way the thruster points, so the load can be taken as the
        /// strongest single direction rather than as the sum of all six.
        /// </summary>
        private static void Rate(ShipProfile profile, BlockInstance block, float[] thrustByDirection,
            ref float draw, ref float installed)
        {
            GameBlocks.Definition definition;
            if (!GameBlocks.BySubtype().TryGetValue(block.Name, out definition)) return;

            BlockThermalDerivation.BlockFunction function =
                BlockThermalDerivation.FunctionOf(definition.TypeId);

            installed += definition.PowerOutputWatts;
            draw += definition.PowerDrawWatts;

            if (definition.TypeId == "HeatVentBlock") profile.HeatVents++;

            if (definition.ThrustNewtons > 0f)
            {
                thrustByDirection[Direction(block)] += definition.ThrustNewtons;
            }
            else
            {
                profile.ConsumerWasteWatts += definition.PowerDrawWatts * function.ConsumerWasteEnergy;
            }
        }

        /// <summary>Which of the six grid directions a block faces, after its orientation.</summary>
        private static int Direction(BlockInstance block)
        {
            Vector3I facing = block.Orientation.Rotate(Vector3I.Forward);

            for (int face = 0; face < Face.Count; face++)
            {
                if (Face.Offsets[face] == facing) return face;
            }
            return 0;
        }

        private static float Percentile(List<float> sorted, float fraction)
        {
            if (sorted.Count == 0) return 0f;

            int index = (int)(fraction * (sorted.Count - 1));
            return sorted[index];
        }

        public override string ToString()
        {
            return Name + " (" + Blocks + " blocks, " + ThermalStress.ToString("n0") + " W/m²)";
        }
    }
}
