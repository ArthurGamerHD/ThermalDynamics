using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// The block mix of a real ship, measured, so a synthetic hull behaves like one: the block-type
    /// table of one 1,381-block ship's telemetry dump, bucketed by heat capacity. A hull's cost is set
    /// by its *lightest* block, so both the substep demand and the *shape* of the distribution matter.
    ///
    /// <para>
    /// **One ship is one ship.** Refresh the tiers and the field observations beside them together as
    /// dumps arrive (`M11`); <see cref="Corpus"/> is the same measurement over 8,102 workshop hulls.
    /// See stiffness.md, and benchmarks.md, Keeping it honest.
    /// </para>
    /// </summary>
    public static class Census
    {
        /// <summary>One measured band of the population.</summary>
        public class Tier
        {
            /// <summary>What the band is, in the terms a reader would recognise.</summary>
            public string Name;

            /// <summary>Share of the blocks on the ship, 0..1.</summary>
            public float Share;

            /// <summary>Mean mass of the band, kg. Heat capacity follows from specific heat.</summary>
            public float Mass;

            /// <summary>Mean conduction quality of the band.</summary>
            public float Conductivity;

            /// <summary>Mean temperature the band takes damage above, K.</summary>
            public float CriticalTemperature;

            /// <summary>
            /// How many of the band's six faces carry a mount point, which is how many neighbours a
            /// block of it can conduct to.
            ///
            /// <para>
            /// **A light hangs off a hull by one face and this table used to bolt it on by six.**
            /// Every tier was built with <c>BlockModel.Solid</c>, which mounts everywhere, so the
            /// lightest band — 20 kg against neighbours of 440 — carried six joints where its own
            /// definition declares one. That is the whole of the gap between the census hull and
            /// the population it stands in for (backlog.md `C26`), and it
            /// is `M11`: the tier describes the block it was measured from.
            /// </para>
            ///
            /// <para>
            /// Taken from the installed definition of <see cref="Example"/>, with **six** where the
            /// definition declares none — a definition that lists no mount points is a block whose
            /// mounts the game derives from its model geometry, not a block that mounts nowhere,
            /// and the corpus walk makes the same fallback. `TheTiersCarryTheMountsTheirBlocksDeclare`
            /// holds each figure to its definition.
            /// </para>
            ///
            /// <para>
            /// **The machinery band is the one exception, and it is six rather than its example's
            /// one.** `Example` is *the most populous subtype in the band, for the record* — it
            /// documents the band rather than defining it — and a band of every heavy device on a
            /// ship is not all gyroscopes. A gyro declares a single mount point on its bottom;
            /// assemblers, refineries and containers mount all round. Taking the gyro's one for the
            /// whole band puts an eleven-tonne block on a single joint, which is a hull with a
            /// thermal dead end in it rather than a hull with machinery in it. The bands where the
            /// example *is* the band — lights, and the three shaped-armour bands — carry what their
            /// blocks declare.
            /// </para>
            /// </summary>
            public int MountFaces = 6;

            /// <summary>The most populous subtype in the band, for the record.</summary>
            public string Example;
        }

        /// <summary>
        /// The measured bands, heaviest share first. Shares sum to one.
        ///
        /// Read as: a third of a ship is ordinary large armour at about 440 kg, and the tail that
        /// decides the substep count is the one per cent below 64 J/K — the light fittings.
        /// </summary>
        public static readonly Tier[] Tiers =
        {
            new Tier { Name = "light fitting",   Share = 0.012f, Mass =    20f, Conductivity = 50f, CriticalTemperature =  900f, MountFaces = 1, Example = "SmallLight" },
            new Tier { Name = "armour tip",      Share = 0.068f, Mass =    59f, Conductivity = 50f, CriticalTemperature =  900f, MountFaces = 3, Example = "LargeBlockArmorCorner2Tip" },
            new Tier { Name = "armour slope",    Share = 0.147f, Mass =   108f, Conductivity = 50f, CriticalTemperature =  900f, MountFaces = 4, Example = "LargeBlockArmorSlope2Tip" },
            new Tier { Name = "half armour",     Share = 0.104f, Mass =   194f, Conductivity = 50f, CriticalTemperature =  900f, MountFaces = 5, Example = "LargeHalfArmorBlock" },
            new Tier { Name = "light armour",    Share = 0.347f, Mass =   440f, Conductivity = 50f, CriticalTemperature =  900f, MountFaces = 6, Example = "LargeBlockArmorBlock" },
            new Tier { Name = "conveyor",        Share = 0.157f, Mass =   750f, Conductivity = 50f, CriticalTemperature =  900f, MountFaces = 6, Example = "ConveyorTubeDuctT" },
            new Tier { Name = "heavy armour",    Share = 0.135f, Mass =  2430f, Conductivity = 56.667f, CriticalTemperature =  931f, MountFaces = 6, Example = "LargeHeavyBlockArmorBlock" },
            new Tier { Name = "machinery",       Share = 0.030f, Mass = 11281f, Conductivity = 55f, CriticalTemperature =  929f, MountFaces = 6, Example = "LargeBlockGyro" },
        };

        /// <summary>
        /// Share of blocks that generate waste heat, and what one of them makes.
        ///
        /// The field ship carried 150 heat producers in 1,381 blocks — eleven per cent — making
        /// 16.7 MW between them. The distribution is very skewed (four large thrusters were a
        /// tenth of it), but for a benchmark what matters is the total and the count, because that
        /// is what sets the equilibrium the hull settles at.
        /// </summary>
        public const float ProducerShare = 0.109f;

        /// <summary>Waste heat one producer makes, W. Measured mean across the field ship.</summary>
        public const float ProducerWatts = 111000f;

        /// <summary>
        /// Temperature above which a producer is damaged, K. Thrusters run hotter than structure
        /// and are rated for it — the field ship's thrusters were rated 1,050 K against 900 K for
        /// everything bolted around them, and they were the blocks that got hot.
        /// </summary>
        public const float ProducerCriticalTemperature = 1050f;

        /// <summary>Mass of a heat producer, kg. The field ship's thrusters and reactors.</summary>
        public const float ProducerMass = 2430f;

        private static BlockModel producer;

        /// <summary>
        /// The block a heat producer is, as distinct from the structure around it.
        ///
        /// Modelling producers as "whichever tier the cell landed on" was wrong in a way that
        /// only showed up once damage was being tested: it made a 900 K-rated armour block the
        /// thing generating 111 kW, so the hull burned at powers the field ship runs at happily.
        /// On a real ship the things making heat are the things rated to take it.
        /// </summary>
        public static BlockModel Producer()
        {
            if (producer != null) return producer;

            BlockThermalProperties thermal = Catalog.DefaultThermal();
            thermal.Conductivity = 56.667f;
            thermal.CriticalTemperature = ProducerCriticalTemperature;

            producer = BlockModel.Solid(ProducerName, Vector3I.One, ProducerMass, thermal);
            return producer;
        }

        /// <summary>
        /// What the field reports actually said, kept beside the tiers they were derived from.
        ///
        /// These are the numbers the synthetic ship is supposed to reproduce, and
        /// <c>CensusFidelityTests</c> holds it to them. Updating the census when a new dump
        /// arrives means editing both — the tiers to match the new population, and these to match
        /// what that population was observed to do — and the tests then say whether the two
        /// still agree.
        /// </summary>
        public static class Field
        {
            /// <summary>
            /// Substeps a full step was measured to need, across the ships seen so far.
            ///
            /// <para>
            /// <b>Both dumps ran <c>Frequency 4</c>, a quarter-second step, and these figures are
            /// meaningless without that.</b> A step of <c>dt</c> needs <c>dt * r_max / safety</c>
            /// substeps, so a demand is proportional to the step length: the same two ships at the
            /// shipped <c>Frequency 8</c> would ask for half of this. <c>CensusFidelityTests</c>
            /// pins the rate for exactly this reason, and the cap tables in stiffness.md were once
            /// quoted against the wrong one.
            /// </para>
            /// </summary>
            public const float LeastDemand = 21.35f;   // STR Hound, 1,293 blocks, 19 Aug, Frequency 4
            public const float MostDemand = 31.25f;    // UNSC Infinity, 42,051 blocks, 18 Aug, Frequency 4

            /// <summary>
            /// Share of blocks a cap would raise, from the projection in the 19 Aug single-ship
            /// dump. The shape of this curve is what decides which cap is safe to ship, so it is
            /// the property a synthetic hull most needs to get right.
            /// </summary>
            public const float RaisedAtCap8 = 0.0116f;
            public const float RaisedAtCap4 = 0.0601f;
            public const float RaisedAtCap2 = 0.2368f;
            public const float RaisedAtCap1 = 0.3910f;

            /// <summary>
            /// Hottest block a ship reached under its own power, K, and what it was rated for.
            /// Two sessions failed to reach a critical temperature; this is how close they came.
            ///
            /// <para>
            /// Placed in the population since: under full electrical load 938.9 K is the **64th
            /// percentile** of 8,142 workshop hulls' peaks, and **43.2 %** of them put a block over
            /// critical. So two sessions ending short of critical is what most ships do rather than
            /// a lucky pair — the claim survives, and it was never as strong as it read.
            /// </para>
            /// </summary>
            public const float HottestObserved = 938.9f;
            public const float HottestRating = 1050f;
        }

        /// <summary>
        /// The same quantities over **8,098 real workshop blueprints**, taken by <c>StiffnessLab</c>
        /// where <see cref="Field"/> is two ships from two vanished sessions. Quoted for a
        /// quarter-second step — <c>Frequency 4</c>, the basis <see cref="Field"/> used — in still
        /// sea-level air at noon, which is a *lower* bound.
        /// See stiffness.md, The same question asked of eight thousand real ships.
        ///
        /// <para>
        /// **Re-measured 2026-08-24 at the pace `C24` ships**, and the figures beside each constant
        /// are what the same walk read at `ConductionScale` 2.4 and `HeatTimeScale` 225. A substep
        /// demand is a conductance over a capacity, so both defaults move every number here and
        /// neither moves them by the same factor: a block whose demand is conduction rises with the
        /// pace and one whose demand is convection falls with the clock. Comparing a hull measured
        /// at one pair against a population measured at the other is the comparison `P6` forbids,
        /// and it is why this walk was re-run rather than the constants adjusted.
        /// </para>
        ///
        /// <para>
        /// **The population changed shape, not only scale**, and two claims this repository rested
        /// on went with it. The two modes have closed — a light-limited ship demands 16.83 against
        /// a structure-limited ship's 7.23, where it was 28.51 against 4.81 — and 49 % of ships now
        /// sit between 8 and 28 substeps where six per cent did. And air has stopped making much
        /// difference to *stiffness*: the median hull's stiffest block is 1.07 times stiffer in air
        /// than out of it, where it was 2.34. Neither says anything about how well air *cools* a
        /// hull, which is the environment term and is untouched; both say that at four times the
        /// conduction pace what sets a block's substep demand is its neighbours.
        /// </para>
        /// <para>
        /// **Re-walked 2026-08-25 after `A13`, and the interesting number is the one that did not
        /// move.** The blueprint reader had been building eleven kinds of vanilla block as an
        /// armour cube; corrected, the population's full-load *heat* rose **15.76 %** and its
        /// stiffness rose by nothing at all — `AirP50`, `AirP90`, `AirMax` and every vacuum
        /// percentile came back identical to the digit, and the constants below move only in their
        /// last place. **Heat is a sum over blocks and stiffness is a maximum over them**, and the
        /// corrected blocks are heavy: a gravity generator is eight tonnes of capacity, so it makes
        /// megawatts and demands almost no substeps. What sets a hull's demand is still a
        /// `SmallLight` or an armour cube, on 45 % and 31 % of ships. So a block-identity error can
        /// be worth a sixth of a population's heat and nothing whatever to its cost.
        /// </para>
        /// </summary>
        public static class Corpus
        {
            /// <summary>
            /// Ships measured, of 8,144 blueprints; 39 are over the 64 MB reader cap and **7 are
            /// modded**, where none were before `A13`. Those seven hold an empty-subtype `Door` or
            /// `GravityGenerator` on a small grid, which the game has only on large; they used to
            /// resolve to a small armour cube and be walked as vanilla.
            /// </summary>
            public const int Ships = 8098;

            // In air. Was 4.26 / 6.61 / 33.09 / 34.45 before C24; the top of the distribution came
            // down with the clock while the bottom of it went up with the conduction pace, which is
            // the two modes closing.
            public const float AirP10 = 6.22f;
            public const float AirP50 = 7.90f;
            public const float AirP90 = 18.42f;
            public const float AirMax = 22.41f;

            /// <summary>
            /// In vacuum, where the same ships used to be three to five times softer and are now
            /// barely softer at all. Was 4.79 / 6.34 / 8.64.
            /// </summary>
            public const float VacuumP50 = 7.40f;
            public const float VacuumP90 = 10.03f;
            public const float VacuumMax = 13.35f;

            /// <summary>
            /// **The population had two modes and nothing much between them, and `C24` closed the
            /// gap.**
            ///
            /// A ship's substep count is still set by a light or a camera on 45 % of hulls and by
            /// armour or structure on the rest — that share did not move at all, 0.4496 against
            /// 0.450 — but the two groups now differ by 2.3× where they differed by 5.9×: 16.83
            /// against 7.23, where it was 28.51 against 4.81. Between 8 and 28 substeps there are
            /// now **49 %** of ships against about six per cent. So a single figure describing "a
            /// typical ship" describes rather more of them than it used to, and the statements in
            /// this repository that refuse to quote one are the ones to re-read.
            /// </summary>
            public const float LitP50 = 16.83f;
            public const float StructuralP50 = 7.25f;
            public const float LitShare = 0.4474f;

            /// <summary>Share of ships between the two modes, 8 to 28 substeps. Was about 0.06.</summary>
            public const float BetweenTheModes = 0.491f;

            /// <summary>
            /// How much stiffer air makes the block that sets a hull's air peak: **the same block's**
            /// air demand over its own vacuum demand, since two peaks are not a ratio (`E6`).
            ///
            /// Was 1.04 / 2.34 / 6.89. At four times the conduction pace a block's neighbours set
            /// its demand and the air barely adds to it, so the median hull is 1.07 and a tenth of
            /// them are exactly 1.00 — which is what makes the census hull's own 1.00 ordinary
            /// rather than the fidelity gap it used to be.
            /// </summary>
            public const float AirRatioP10 = 1.00f;
            public const float AirRatioP50 = 1.07f;
            public const float AirRatioP90 = 2.52f;

            /// <summary>
            /// The census hull's own, on the same basis. Was 1.50 against a population median of
            /// 2.34; it is now 1.00 against 1.07, so the hull has stopped feeling the air and so
            /// has a tenth of the population.
            /// </summary>
            public const float CensusAirRatio = 1.00f;

            /// <summary>
            /// Exposed faces on the block that sets a real ship's air peak, mean. Was 5.26: the
            /// block that sets the peak has moved inboard along with everything else the pace
            /// changed.
            /// </summary>
            public const float StiffestFacesMean = 3.46f;

            // ---- what a real ship makes, against what the census hull makes -------------------

            /// <summary>
            /// **The census hull is a 96th-percentile ship for heat.** It reaches no stiffness figure —
            /// no watt appears in substep demand — and every temperature figure. Recorded rather than
            /// corrected, since a hot hull is a fair worst case and a poor "what a ship does".
            /// See stiffness.md, The census hull is a 96th-percentile ship for heat.
            /// </summary>
            public const float ProducerSharePercentile = 86.8f;
            public const float ProducerWattsPercentile = 88.0f;
            public const float WastePerBlockPercentile = 96.4f;

            /// <summary>Waste heat per block under full electrical load, W, over real ships.</summary>
            public const float WastePerBlockP50 = 335f;
            public const float WastePerBlockP90 = 6056f;

            /// <summary>The census hull's own: its share times its watts a producer.</summary>
            public const float CensusWastePerBlock = 12099f;

            /// <summary>
            /// The cap curve over 2.4 million blocks, against <see cref="Field.RaisedAtCap8"/> and its
            /// siblings from one dump. **They agree where the choice is made and diverge where it is
            /// not**, which is what confirms the shipped cap's reach.
            /// See stiffness.md, The cap curve holds where the cap is actually set.
            ///
            /// <para>
            /// Re-measured 2026-08-24 at the pace `C24` ships; it read 0.0159 / 0.0720 / 0.3554 /
            /// 0.5253 before. **The shipped cap of 8 still reaches under a per cent of real blocks**
            /// — 0.92 %, where it was 1.59 % — and every cap below it reaches more than it did,
            /// because the population's soft mode has come up to meet the stiff one. The dump it is
            /// read against was taken before the retune and cannot be re-measured, which is why the
            /// tests that compare the two now say so rather than pretending to a like-for-like.
            /// </para>
            /// </summary>
            public const float FlooredAtCap8 = 0.0087f;
            public const float FlooredAtCap4 = 0.2318f;
            public const float FlooredAtCap2 = 0.4022f;
            public const float FlooredAtCap1 = 0.7529f;
        }

        private static BlockModel[] models;

        /// <summary>One block model per tier, built once.</summary>
        public static BlockModel[] Models()
        {
            if (models != null) return models;

            BlockModel[] built = new BlockModel[Tiers.Length];
            for (int i = 0; i < Tiers.Length; i++)
            {
                Tier tier = Tiers[i];

                BlockThermalProperties thermal = Catalog.DefaultThermal();
                thermal.Conductivity = tier.Conductivity;
                thermal.CriticalTemperature = tier.CriticalTemperature;

                built[i] = Mounted(tier.Name, tier.Mass, thermal, tier.MountFaces);
            }

            models = built;
            return models;
        }

        /// <summary>
        /// A one-cell block that mounts on <paramref name="mountFaces"/> of its six faces and seals
        /// all of them.
        ///
        /// The faces are taken in a fixed order, so a band with one mount always bolts to the same
        /// side and two hulls built the same way are the same ship. Which side it is does not
        /// matter to the population figure — what matters is how many joints a block of the band
        /// has, since a joint is what makes it stiff. Sealing is untouched: a light is airtight and
        /// its exposure comes from whether a neighbour is there at all.
        /// </summary>
        private static BlockModel Mounted(
            string name, float mass, BlockThermalProperties thermal, int mountFaces)
        {
            BlockModel model = BlockModel.Solid(name, Vector3I.One, mass, thermal);
            if (mountFaces >= Face.Count) return model;

            int state = CellSurface.SelfAirtightMask;
            for (int face = 0; face < Face.Count; face++)
            {
                state = CellSurface.WithSelfMount(state, face, face < mountFaces);
            }

            for (int i = 0; i < model.LocalSurfaces.Length; i++) model.LocalSurfaces[i] = state;
            return model;
        }

        /// <summary>
        /// The tier the block at <paramref name="index"/> belongs to, walking the population in
        /// the measured proportions.
        ///
        /// Deterministic and stateless: a low-discrepancy walk rather than a random draw, so the
        /// mix is exactly right at every prefix rather than only in expectation, and two runs of
        /// the same benchmark build the same ship. That matters more than it sounds — the tail
        /// tier is one block in eighty, so a sampler that is merely unbiased can leave a small
        /// hull with no light fittings at all and quietly measure the wrong thing.
        /// </summary>
        public static int TierAt(int index)
        {
            // Walk the cumulative distribution at the position this block occupies in it.
            float position = ((index * 2654435761u) % 1000000u) / 1000000f;

            float running = 0f;
            for (int i = 0; i < Tiers.Length; i++)
            {
                running += Tiers[i].Share;
                if (position < running) return i;
            }

            return Tiers.Length - 1;
        }

        /// <summary>
        /// True when the block at <paramref name="index"/> is a heat producer.
        ///
        /// **This is the placement rule and not the identity.** It says which *cell* of a hull
        /// being laid out gets a producer; what a node in a built hull is, is
        /// <see cref="IsProducer(ThermalNode)"/>. The two agree on a hull built in the order it was
        /// laid out, which is every hull here bar one — and `CensusTests` pins that they do, so the
        /// pair cannot drift.
        /// </summary>
        public static bool ProducesHeatAt(int index)
        {
            int period = (int)Math.Round(1f / ProducerShare);
            return period > 0 && (index % period) == 0;
        }

        /// <summary>
        /// True when a node's block *is* a producer, whatever order the hull was built in.
        ///
        /// <para>
        /// **Node index is not block identity, and taking it for one is what `F22` is about.** A
        /// node's index comes from the order blocks were added, so a hull built cell by cell in a
        /// different order is the same ship with a permuted index space — and a rule that drives
        /// "every twentieth node" then drives twenty different blocks. Reading the model the cell
        /// actually holds is the same answer on any build order.
        /// </para>
        /// </summary>
        public static bool IsProducer(ThermalNode node)
        {
            if (node == null || node.Block == null || node.Block.Model == null) return false;

            // By name rather than by reference. The model cache is a plain lazy field, so two
            // threads racing to build it can each hand out their own instance — and a reference
            // test would then quietly answer *no* for a perfectly good producer, which is a hull
            // driving nothing and reporting that it did (`E8`).
            return node.Block.Model.Name == ProducerName;
        }

        /// <summary>The model name a producer carries, which is its identity on any build order.</summary>
        public const string ProducerName = "producer";

        /// <summary>
        /// Places one block per cell in the measured proportions, and returns the builder.
        ///
        /// Heat generation is left to the caller: a hull that is cooling and a hull that is being
        /// driven are different measurements, and only one of them wants sources.
        /// </summary>
        public static GridBuilder PlaceCensus(this GridBuilder builder, IEnumerable<Vector3I> cells)
        {
            BlockModel[] tiers = Models();
            BlockModel source = Producer();

            List<Vector3I> layout = new List<Vector3I>(cells);
            int[] tierOf = TiersFor(layout);

            BlockOrientation[] orientations = Bolt(layout, tierOf, tiers);

            for (int i = 0; i < layout.Count; i++)
            {
                if (ProducesHeatAt(i))
                {
                    // A producer stands in place of whatever tier the cell would have held, so the
                    // remaining tiers keep their proportions to each other and the hull keeps its
                    // block count.
                    builder.Place(source, layout[i]);
                    continue;
                }

                BlockModel model = tiers[tierOf[i]];
                builder.Place(model, layout[i], orientations[i]);
            }

            return builder;
        }

        /// <summary>
        /// Which tier each cell of a layout holds, after the lightest tier has been moved onto the
        /// surface. Public so a test can plan a hull the way <see cref="PlaceCensus"/> does and
        /// hold <see cref="Bolt"/> against a reference over the same plan.
        /// </summary>
        public static int[] TiersFor(List<Vector3I> layout)
        {
            int[] tierOf = new int[layout.Count];
            HashSet<Vector3I> filled = new HashSet<Vector3I>(layout);

            for (int i = 0; i < layout.Count; i++) tierOf[i] = TierAt(i);

            SurfaceTheLightestTier(layout, tierOf, filled);
            return tierOf;
        }

        /// <summary>
        /// Moves the lightest tier onto the hull's surface, swapping it with whatever was there.
        ///
        /// <para>
        /// **A real ship's stiffest block is one somebody could see.** Measured over 8,098 workshop
        /// hulls, the block that sets a ship's substep demand in air has **3.46 exposed faces** on
        /// average and a light sets it on 45 % of them — a light hangs off a hull. The census hull
        /// deals its tiers out by a hash of a block's position in the layout, so its light fittings
        /// landed wherever that put them, and the ones that landed inside had six armour neighbours
        /// and no sky: a 20 kg body with six joints into 440 kg blocks and nothing to radiate from
        /// is stiffer than anything a builder can make.
        /// </para>
        ///
        /// <para>
        /// **It went unnoticed while conduction ran at 2.4 and `C24` made it visible**, because a
        /// buried block's demand is all conduction and quadrupled with the pace while a real ship's
        /// exposed one is mostly convection and fell with the clock: the hull left the population it
        /// stands in for at 36.75 substeps against a corpus maximum of 22.41. That is
        /// backlog.md `C26`, and this is `M11` — the synthetic ship is
        /// refreshed against the field.
        /// </para>
        ///
        /// <para>
        /// **Counts are preserved exactly**: this is a permutation of tier onto cell, so every tier
        /// keeps the share <see cref="TierAt"/> gave it and the hull keeps its block count. Producer
        /// cells are left where they are, since which cell makes heat is its own placement rule and
        /// moving it would change what the hull generates as well as where.
        /// </para>
        /// </summary>
        private static void SurfaceTheLightestTier(
            List<Vector3I> layout, int[] tierOf, HashSet<Vector3I> filled)
        {
            const int Lightest = 0;

            List<int> buried = new List<int>();
            List<int> exposedElsewhere = new List<int>();
            int[] faces = new int[layout.Count];

            for (int i = 0; i < layout.Count; i++)
            {
                faces[i] = ExposedFaces(layout[i], filled);
                if (ProducesHeatAt(i)) continue;

                if (tierOf[i] == Lightest && faces[i] == 0) buried.Add(i);
                else if (tierOf[i] != Lightest && faces[i] > 0) exposedElsewhere.Add(i);
            }

            // **The most exposed cells first, not merely exposed ones.** A hull is mostly flat, so
            // taking the first surface cell in layout order puts every light on a face with one
            // open side, where the block that sets a real ship's demand has 3.46. Sorted by open
            // faces, the lights take the corners and edges a ship hangs them off. Ties keep layout
            // order, so the result is the same on every run and every build order (`E5`).
            exposedElsewhere.Sort(delegate(int a, int b)
            {
                int byFaces = faces[b].CompareTo(faces[a]);
                return byFaces != 0 ? byFaces : a.CompareTo(b);
            });

            // Whatever was on the surface takes the buried cell in exchange, so the swap moves two
            // blocks and changes no count.
            int swaps = Math.Min(buried.Count, exposedElsewhere.Count);
            for (int s = 0; s < swaps; s++)
            {
                int inside = buried[s];
                int outside = exposedElsewhere[s];

                tierOf[inside] = tierOf[outside];
                tierOf[outside] = Lightest;
            }
        }

        /// <summary>
        /// The 24 legal orientations, in <see cref="PipeFitter.AllOrientations"/>'s order, with
        /// each one's six rotated face directions resolved once. <see cref="Bolt"/> used to rotate
        /// a face through a freshly built matrix for every candidate of every block — 144 matrices a
        /// block, and a dictionary probe behind each — which put the hull generator at ten times
        /// the cost of the simulation it was built to feed. See performance.md, Iteration 2.
        /// </summary>
        private static BlockOrientation[] boltOrientations;

        /// <summary>Grid-space face index of each local face, per orientation: <c>[orientation * 6 + face]</c>.</summary>
        private static int[] boltRotatedFace;

        /// <summary>Where the identity sits in the table; not first, since the enumeration starts at Forward/Left.</summary>
        private static int boltIdentity;

        private static void EnsureBoltTable()
        {
            if (boltOrientations != null) return;

            List<BlockOrientation> all = new List<BlockOrientation>(PipeFitter.AllOrientations());
            int[] rotated = new int[all.Count * Face.Count];

            for (int o = 0; o < all.Count; o++)
            {
                for (int face = 0; face < Face.Count; face++)
                {
                    rotated[(o * Face.Count) + face] = Face.IndexOf(all[o].Rotate(Face.Offsets[face]));
                }
            }

            boltRotatedFace = rotated;
            boltIdentity = all.IndexOf(BlockOrientation.Identity);
            if (boltIdentity < 0) throw new InvalidOperationException("the orientation table holds no identity");
            boltOrientations = all.ToArray();
        }

        /// <summary>
        /// Turns every block so the faces it mounts on are faces that have something to mount to.
        ///
        /// <para>
        /// **A block that mounts on one face is bolted to something with it.** A light hangs off a
        /// wall; it does not hang off the empty side of one. Placing every partial-mount tier at
        /// the identity points its one mount face the same way whatever is around it, and a light
        /// whose mount face happens to look at open space is a block with *no joints at all* —
        /// twenty kilograms with nothing to conduct to, cooling toward the sky on an asymptote that
        /// never arrives. That is the shape `E8` is about: it does not fail, it quietly measures a
        /// hull that is not the one being described.
        /// </para>
        ///
        /// <para>
        /// So the orientation is chosen per cell, in layout order: the candidate whose mount faces
        /// cover the most neighbours that either mount back or have not yet chosen, ties going to
        /// the first in a fixed order so two hulls built alike are the same ship. Producers are
        /// solid and mount everywhere, so they stay at the identity. `CensusBoltTests` holds this
        /// against the search it replaced, orientation for orientation.
        /// </para>
        /// </summary>
        public static BlockOrientation[] Bolt(IList<Vector3I> layout, int[] tierOf, BlockModel[] tiers)
        {
            EnsureBoltTable();

            int count = layout.Count;
            BlockOrientation[] chosen = new BlockOrientation[count];
            int[] chosenIndex = new int[count];
            bool[] placed = new bool[count];

            Dictionary<Vector3I, int> at = new Dictionary<Vector3I, int>(count, Vector3I.Comparer);
            for (int i = 0; i < count; i++) at[layout[i]] = i;

            // The mount faces of each tier, as a bit per local face, read once rather than per candidate.
            int[] mounts = new int[tiers.Length];
            for (int t = 0; t < tiers.Length; t++)
            {
                int state = tiers[t].LocalSurfaces[0];
                for (int face = 0; face < Face.Count; face++)
                {
                    if (CellSurface.SelfMount(state, face)) mounts[t] |= 1 << face;
                }
            }

            int[] neighbourAt = new int[Face.Count];
            int orientations = boltOrientations.Length;
            int[] rotatedFace = boltRotatedFace;

            for (int i = 0; i < count; i++)
            {
                // A producer is a solid block and mounts everywhere; leave it at the identity.
                if (ProducesHeatAt(i))
                {
                    chosen[i] = BlockOrientation.Identity;
                    chosenIndex[i] = boltIdentity;
                    placed[i] = true;
                    continue;
                }

                // Six probes per block, not six per candidate.
                Vector3I cell = layout[i];
                for (int d = 0; d < Face.Count; d++)
                {
                    int neighbour;
                    neighbourAt[d] = at.TryGetValue(cell + Face.Offsets[d], out neighbour) ? neighbour : -1;
                }

                int mount = mounts[tierOf[i]];
                int best = -1;

                for (int o = 0; o < orientations; o++)
                {
                    int joined = 0;
                    int b = o * Face.Count;

                    for (int face = 0; face < Face.Count; face++)
                    {
                        if ((mount & (1 << face)) == 0) continue;

                        int toward = rotatedFace[b + face];
                        int neighbour = neighbourAt[toward];
                        if (neighbour < 0) continue;

                        // An unplaced neighbour still counts: it has not chosen a side yet and can
                        // take one that mounts back. A placed one counts only if it did.
                        if (!placed[neighbour]
                            || MountsToward(mounts[tierOf[neighbour]], chosenIndex[neighbour], Face.Opposite(toward)))
                        {
                            joined++;
                        }
                    }

                    if (joined <= best) continue;

                    best = joined;
                    chosenIndex[i] = o;
                }

                chosen[i] = boltOrientations[chosenIndex[i]];
                placed[i] = true;
            }

            return chosen;
        }

        /// <summary>Whether a placed block, at its chosen orientation, carries a mount point on a grid-space face.</summary>
        private static bool MountsToward(int mount, int orientation, int towardFace)
        {
            int b = orientation * Face.Count;
            for (int face = 0; face < Face.Count; face++)
            {
                if ((mount & (1 << face)) == 0) continue;
                if (boltRotatedFace[b + face] == towardFace) return true;
            }

            return false;
        }

        /// <summary>How many of a cell's six faces have nothing bolted to them.</summary>
        private static int ExposedFaces(Vector3I cell, HashSet<Vector3I> filled)
        {
            int open = 0;

            if (!filled.Contains(cell + Vector3I.Up)) open++;
            if (!filled.Contains(cell + Vector3I.Down)) open++;
            if (!filled.Contains(cell + Vector3I.Left)) open++;
            if (!filled.Contains(cell + Vector3I.Right)) open++;
            if (!filled.Contains(cell + Vector3I.Forward)) open++;
            if (!filled.Contains(cell + Vector3I.Backward)) open++;

            return open;
        }

        /// <summary>
        /// Switches on the measured share of heat producers, each making
        /// <see cref="ProducerWatts"/> of waste heat.
        /// </summary>
        /// <returns>How many blocks were made producers.</returns>
        public static int DriveCensus(ThermalSimulation simulation)
        {
            return DriveCensus(simulation, ProducerWatts);
        }

        /// <summary>
        /// The same, at a chosen wattage. The census figure settles a hull below its rating, which
        /// is what the field measured; multiplying it is how a benchmark reaches the other side of
        /// a critical temperature without changing anything else about the ship.
        /// </summary>
        public static int DriveCensus(ThermalSimulation simulation, float watts)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            int producers = 0;

            for (int i = 0; i < nodes.Count; i++)
            {
                if (!IsProducer(nodes[i])) continue;

                // The waste fraction is what turns power into heat, so the power a producer draws
                // is the heat it wants divided by that fraction.
                float waste = nodes[i].Thermal.ProducerWasteEnergy;
                if (waste <= 0f) continue;

                nodes[i].Block.PowerProducedWatts = watts / waste;
                nodes[i].RefreshHeatGeneration();
                producers++;
            }

            return producers;
        }

        /// <summary>
        /// Puts <paramref name="watts"/> of *thrust* heat on every heat producer of the hull, as a
        /// watt-equivalent of force rather than as power drawn.
        ///
        /// <para>
        /// **A thruster is charged against its thrust and not against its draw**, which is what
        /// makes a hydrogen thruster heat at all — it draws no electricity. The solver reads
        /// `ThrustWatts` through `ConsumerWasteEnergy`, so this is a separate term from
        /// <see cref="DriveCensus"/> and stacks with it: a ship under way is making both.
        /// </para>
        ///
        /// <para>
        /// It exists for the degraded-input sweep (backlog.md `F17`),
        /// where thrust is the one input the engine *predicts* on a client rather than replicating,
        /// so a client's is its own guess about a ship whose physics it is not running.
        /// </para>
        /// </summary>
        public static int DriveThrust(ThermalSimulation simulation, float watts)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            int thrusters = 0;

            for (int i = 0; i < nodes.Count; i++)
            {
                if (!IsProducer(nodes[i])) continue;

                float waste = nodes[i].Thermal.ConsumerWasteEnergy;
                if (waste <= 0f) continue;

                nodes[i].Block.ThrustWatts = watts / waste;
                nodes[i].RefreshHeatGeneration();
                thrusters++;
            }

            return thrusters;
        }
    }
}
