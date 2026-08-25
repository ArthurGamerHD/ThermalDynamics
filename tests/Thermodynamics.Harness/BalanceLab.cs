using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// Measures every block this mod ships against the vanilla blocks a player would otherwise
    /// build, so the definitions can be tuned against numbers instead of against intuition.
    ///
    /// The scenarios answer "does this mechanism work". This answers "is this block worth
    /// building", which is a different question and needs a different shape of measurement: not a
    /// settling curve, but a per-block figure that can be divided by what the block costs.
    ///
    /// Four things are measured, and each one exists because it can bind:
    ///
    /// 1. **Capacity** — J/K. How much heat the block swallows before it warms. Free of the solver
    ///    entirely; it is mass times specific heat over the thermal clock.
    /// 2. **Shedding** — watts radiated at a reference temperature. What the block gets rid of.
    /// 3. **Conduction** — W/K through the block's mount faces. What can *reach* it. A radiator
    ///    that sheds 20 kW and is bolted on by a joint that carries 4 kW sheds 4 kW.
    /// 4. **Delivered** — an end-to-end steady state with a real load, which is the only one of the
    ///    four that accounts for all three at once, and the one a player experiences.
    ///
    /// Every figure is then divided by mass, by PCU and by cell count, because a block that cools
    /// twice as well for three times the mass is a worse block and no absolute figure says so.
    ///
    /// Deterministic: fixed step counts, no clock, no randomness. The numbers are reproducible and
    /// diffable, which is what lets <c>BalanceTests</c> pin conclusions drawn from them.
    /// </summary>
    public static class BalanceLab
    {
        /// <summary>
        /// The temperature a shedding figure is quoted at. 600 K is about 327 C: hot enough that a
        /// ship is in trouble, cool enough that it is a state a player actually sits at rather than
        /// a moment on the way to damage. Radiation goes as T^4, so a shedding figure means nothing
        /// without the temperature beside it.
        /// </summary>
        public const float ReferenceTemperature = 600f;

        /// <summary>Deep space, so a shedding figure is not confounded by convection or sun.</summary>
        public const float ReferenceAmbient = 2.7f;

        // ---- rows ---------------------------------------------------------------------------

        /// <summary>One block, costed and measured.</summary>
        public class BlockRow
        {
            public string Subtype;
            public string Kind;
            public bool Large;
            public int Cells;
            public float Mass;
            public int Pcu;
            public float BuildSeconds;

            public float SpecificHeat;
            public float Emissivity;
            public float Conductivity;
            public float ExposedSurfaceMultiplier;

            /// <summary>J/K at the shipped thermal clock.</summary>
            public float CapacityJoulesPerKelvin;

            /// <summary>Square metres of radiating surface with every face open.</summary>
            public float ExposedAreaAlone;

            /// <summary>Watts shed at <see cref="ReferenceTemperature"/> with every face open.</summary>
            public float ShedAloneWatts;

            /// <summary>Watts shed once bolted onto a hull, which covers the mounted faces.</summary>
            public float ShedMountedWatts;

            /// <summary>W/K conducted through the block's mount joint to one neighbour.</summary>
            public float ConductanceWattsPerKelvin;

            /// <summary>Which face the joint was measured on — the first the block mounts by.</summary>
            public string JointFace = "none";

            /// <summary>How many of the six faces carry a mount point at all.</summary>
            public int MountFaceCount;

            /// <summary>
            /// The binding limit as a ratio: shedding over what conduction can deliver across a
            /// 100 K gradient. Below 1 the block sheds less than reaches it and its surface binds;
            /// above 1 the joint binds and a better surface buys nothing.
            /// </summary>
            public float ShedOverReach;
        }

        /// <summary>A vanilla heat source, and the watts it actually puts into the ship.</summary>
        public class LoadRow
        {
            public string Subtype;
            public string TypeId;
            public bool Large;
            public float RatedMegawatts;

            /// <summary>The fraction Cubes.xml applies to this block's power.</summary>
            public float WasteFraction;

            /// <summary>Watts of heat at full rating, as the shipped definitions compute it.</summary>
            public float WasteWatts;

            /// <summary>Whether the power flows through the fraction that the definition sets.</summary>
            public string Path;
        }

        /// <summary>A head-to-head: what one block of each kind delivers on the same hull.</summary>
        public class DeliveredRow
        {
            public string Label;
            public int Count;

            /// <summary>Steady reactor temperature, kelvin, after the run.</summary>
            public float SettledKelvin;

            /// <summary>Kelvin below the bare hull's settling point.</summary>
            public float KelvinSaved;

            public float AddedMass;
            public int AddedPcu;

            /// <summary>Kelvin saved per tonne added.</summary>
            public float KelvinPerTonne;
        }

        /// <summary>The heat pump at one temperature gap.</summary>
        public class PumpRow
        {
            public float ColdKelvin;
            public float GapKelvin;
            public float Coefficient;
            public float LiftedWatts;
            public float DrawnWatts;

            /// <summary>Which of the three limits decided the outcome.</summary>
            public string Binding;
        }

        /// <summary>A coolant ring of a given size, and what it moves.</summary>
        public class LoopRow
        {
            public int Pipes;
            public int Sinks;
            public float FluidThermalMass;
            public float CouplingWattsPerKelvin;
            public float SettledKelvin;
            public float KelvinSaved;
        }

        /// <summary>
        /// A one-cell block that turns every watt it draws into heat.
        ///
        /// <see cref="Catalog.Reactor"/> wastes what the shipped reactor wastes — a hundredth of
        /// what it makes — so driving it with 200 kW would put 2 kW into the ship. That is correct
        /// for a scenario asking what a reactor does, and useless for a table whose rows are
        /// labelled in watts: every figure would be a hundredth of its heading. This block has a
        /// fraction of one, so a load of 200 kW is 200 kW of heat and the labels need no asterisk.
        /// </summary>
        public static BlockModel Heater()
        {
            BlockThermalProperties thermal = Catalog.ReactorThermal();
            thermal.ProducerWasteEnergy = 1f;
            thermal.ConsumerWasteEnergy = 1f;
            return BlockModel.Solid("Heater", Vector3I.One, 3000f, thermal);
        }

        // ---- block measurements ---------------------------------------------------------------

        /// <summary>Every shipped block, plus the vanilla blocks it is competing with.</summary>
        public static List<BlockRow> Blocks()
        {
            List<BlockRow> rows = new List<BlockRow>();
            ThermalSettings settings = new ThermalSettings();

            foreach (string subtype in ShippedBlocks.Subtypes())
            {
                ShippedBlocks.Definition definition = ShippedBlocks.Get(subtype);
                rows.Add(Measure(subtype, Kind(subtype), definition.Large, definition.Size,
                    definition.Mass, definition.Pcu, definition.BuildSeconds, definition.Thermal,
                    ShippedBlocks.Model(subtype), settings));
            }

            foreach (Vanilla.Block block in Vanilla.Reference)
            {
                if (block.TypeId != "CubeBlock") continue;      // armour only: the structural alternative

                BlockThermalProperties thermal = Catalog.DefaultThermal();
                BlockModel model = BlockModel.Solid(block.Subtype, block.Size, block.Mass, thermal);
                rows.Add(Measure(block.Subtype, "vanilla armour", block.Large, block.Size,
                    block.Mass, block.Pcu, block.BuildSeconds, thermal, model, settings));
            }

            return rows;
        }

        private static BlockRow Measure(string subtype, string kind, bool large, Vector3I size,
            float mass, int pcu, float buildSeconds, BlockThermalProperties thermal,
            BlockModel model, ThermalSettings settings)
        {
            float gridSize = large ? Catalog.LargeGridSize : Catalog.SmallGridSize;

            BlockRow row = new BlockRow
            {
                Subtype = subtype,
                Kind = kind,
                Large = large,
                Cells = size.X * size.Y * size.Z,
                Mass = mass,
                Pcu = pcu,
                BuildSeconds = buildSeconds,
                SpecificHeat = thermal.SpecificHeat,
                Emissivity = thermal.Emissivity,
                Conductivity = thermal.Conductivity,
                ExposedSurfaceMultiplier = thermal.ExposedSurfaceMultiplier,
                CapacityJoulesPerKelvin = mass * thermal.SpecificHeat / settings.HeatTimeScale,
            };

            // Alone in space: every face radiates. The upper bound on what the block can shed.
            GridBuilder alone = new GridBuilder(gridSize);
            alone.Place(model, Vector3I.Zero);
            ThermalSimulation simulation = alone.BuildSimulation(settings, ReferenceTemperature);
            ThermalNode node = simulation.Solver.GetNodeAt(Vector3I.Zero);

            row.ExposedAreaAlone = node.ExposedArea;
            row.ShedAloneWatts = Radiated(node, ReferenceTemperature);

            // Bolted onto a hull. Only the faces still open radiate, and the joint is what heat
            // arrives through, so this is the pair of figures that decides which limit binds.
            int joint = FirstMountFace(model);
            row.JointFace = joint < 0 ? "none" : Face.Name(joint);
            row.MountFaceCount = MountFaceCount(model);
            row.ShedMountedWatts = MountedShedding(model, gridSize, settings, joint,
                out row.ConductanceWattsPerKelvin);

            float reach = row.ConductanceWattsPerKelvin * 100f;
            row.ShedOverReach = reach > 0f ? row.ShedMountedWatts / reach : 0f;
            return row;
        }

        private static float Radiated(ThermalNode node, float temperature)
        {
            double hot = (double)temperature * temperature * temperature * temperature;
            double cold = (double)ReferenceAmbient * ReferenceAmbient * ReferenceAmbient * ReferenceAmbient;
            return (float)(node.RadiationCoefficient * (hot - cold));
        }

        /// <summary>Faces of this model that carry a mount surface on at least one cell.</summary>
        private static int MountFaceCount(BlockModel model)
        {
            int count = 0;
            for (int face = 0; face < Face.Count; face++)
            {
                if (model.LocalFaceMountFraction(face) > 0f) count++;
            }
            return count;
        }

        /// <summary>
        /// The first face the block can be bolted on by, or -1 if it mounts nowhere.
        ///
        /// This has to come from the model rather than being assumed. A coolant pipe mounts on its
        /// two ends and nothing else; measuring its joint by pressing armour against its side
        /// would report zero conductance and call the block a thermal island, which is a statement
        /// about the measurement and not about the block.
        /// </summary>
        private static int FirstMountFace(BlockModel model)
        {
            for (int face = 0; face < Face.Count; face++)
            {
                if (model.LocalFaceMountFraction(face) > 0f) return face;
            }
            return -1;
        }

        /// <summary>
        /// The block bolted to a slab of light armour across one of its own mount faces. Returns
        /// what it can still radiate, and reports the conductance of the joint it hangs from.
        /// </summary>
        private static float MountedShedding(BlockModel model, float gridSize, ThermalSettings settings,
            int joint, out float conductance)
        {
            GridBuilder builder = new GridBuilder(gridSize);
            BlockModel armour = BlockModel.Solid("hull", Vector3I.One, 500f, Catalog.DefaultThermal());

            builder.Place(model, Vector3I.Zero);
            BlockInstance placed = builder.Last;

            if (joint >= 0)
            {
                // A sheet of armour covering the whole of that face, one cell thick.
                Vector3I extents = model.Extents;
                Vector3I step = Face.Offsets[joint];
                int axis = Face.Axis(joint);

                foreach (Vector3I cell in model.LocalCells())
                {
                    // Only the cells on the face itself, so the sheet is a skin and not a block.
                    int along = axis == 0 ? cell.X : axis == 1 ? cell.Y : cell.Z;
                    int span = axis == 0 ? extents.X : axis == 1 ? extents.Y : extents.Z;
                    bool onFace = IsPositive(step) ? along == span - 1 : along == 0;
                    if (onFace) builder.Place(armour, cell + step);
                }
            }

            ThermalSimulation simulation = builder.BuildSimulation(settings, ReferenceTemperature);
            ThermalNode node = simulation.Solver.GetNodeAt(placed.Min);

            // Every link that touches this node, summed: the whole joint the block hangs from.
            // Links are held by node index rather than by reference, so the index comes first.
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            int self = -1;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (ReferenceEquals(nodes[i], node)) self = i;
            }

            conductance = 0f;
            foreach (ThermalLink link in simulation.Solver.Links)
            {
                if (link.NodeA == self || link.NodeB == self) conductance += link.Conductance;
            }

            return Radiated(node, ReferenceTemperature);
        }

        private static bool IsPositive(Vector3I step)
        {
            return step.X > 0 || step.Y > 0 || step.Z > 0;
        }

        private static string Kind(string subtype)
        {
            if (subtype.Contains("Radiator")) return "radiator";
            if (subtype.Contains("HeatPump")) return "heat pump";
            if (subtype.Contains("CoolantPump")) return "coolant pump";
            if (subtype.Contains("CoolantPipe")) return "coolant pipe";
            return "other";
        }

        // ---- the load side ----------------------------------------------------------------------

        /// <summary>
        /// What the vanilla heat sources actually put into a ship, using the fractions the mod's
        /// own Cubes.xml declares for their types.
        ///
        /// This is the denominator of every cooling question — "how many radiators per reactor"
        /// has no answer until the reactor's number is known — and it is the one figure that is
        /// decided entirely by definitions rather than by the solver.
        /// </summary>
        public static List<LoadRow> Loads()
        {
            List<LoadRow> rows = new List<LoadRow>();

            foreach (Vanilla.Block block in Vanilla.Reference)
            {
                if (block.TypeId == "CubeBlock") continue;

                BlockThermalProperties thermal = block.Thermal;
                bool produces = block.PowerOutputMegawatts > 0f && block.TypeId != "Thrust";
                float rated = produces ? block.PowerOutputMegawatts : block.PowerDrawMegawatts;

                // A reactor and a battery deliver power, so their watts run through the producer
                // fraction; a thruster consumes, so it runs through the consumer one. Which side a
                // block lands on is set by the game adapter, not by the definition, and it is the
                // thing a definition can get wrong without any other symptom.
                float fraction = produces ? thermal.ProducerWasteEnergy : thermal.ConsumerWasteEnergy;

                rows.Add(new LoadRow
                {
                    Subtype = block.Subtype,
                    TypeId = block.TypeId,
                    Large = block.Large,
                    RatedMegawatts = rated,
                    WasteFraction = fraction,
                    WasteWatts = rated * 1000000f * fraction,
                    Path = produces ? "produced" : "consumed",
                });
            }

            return rows;
        }

        // ---- delivered cooling ------------------------------------------------------------------

        /// <summary>
        /// The end-to-end question: a 2 MW block with a column of panels stacked on it, measured
        /// once with the shipped radiator and once with a slab of ordinary light armour of the
        /// same shape in the same place.
        ///
        /// A column rather than a hull, deliberately. The shipped radiator mounts on its top and
        /// bottom faces only, so a panel bolted to a hull's *side* conducts nothing at all and the
        /// measurement would be of the boom holding it rather than of the block. Stacking is the
        /// one arrangement in which every panel is genuinely in the heat path, which makes the
        /// marginal return of the Nth panel a real number instead of an artefact of the layout.
        ///
        /// Same shape, same position, different definition: the difference between the two rows is
        /// the radiator's emissivity, area scaler and specific heat, and nothing else. That is the
        /// comparison a tuning decision needs. What bolting a panel flat against a hull costs is a
        /// different question, and the <c>radiator</c> scenario already answers it.
        /// </summary>
        public static List<DeliveredRow> Delivered()
        {
            List<DeliveredRow> rows = new List<DeliveredRow>();

            // Two loads, because one is not enough to see the shape. 2 MW into a single cell is a
            // block already far past its rating, where everything saturates and every fit looks
            // equally useless; 200 kW is a load a player builds around. If a cooling block only
            // helps at one of the two, that is the most important thing the table can say.
            foreach (float watts in new float[] { 200000f, 2000000f })
            {
                rows.AddRange(DeliveredAt(watts));
            }
            return rows;
        }

        private static List<DeliveredRow> DeliveredAt(float watts)
        {
            List<DeliveredRow> rows = new List<DeliveredRow>();
            ShippedBlocks.Definition radiator = ShippedBlocks.Get("Gauge_LG_Radiator");
            string load = N(watts / 1000f, 0) + " kW ";

            float bare = Column(0, false, watts);
            rows.Add(new DeliveredRow { Label = load + "bare source", Count = 0, SettledKelvin = bare });

            foreach (int count in new int[] { 1, 2, 4, 8 })
            {
                float settled = Column(count, false, watts);
                float mass = radiator.Mass * count;
                rows.Add(new DeliveredRow
                {
                    Label = load + "radiators",
                    Count = count,
                    SettledKelvin = settled,
                    KelvinSaved = bare - settled,
                    AddedMass = mass,
                    AddedPcu = radiator.Pcu * count,
                    KelvinPerTonne = mass > 0f ? (bare - settled) / (mass / 1000f) : 0f,
                });
            }

            // The same slab as plain armour. If armour is close, the radiator's properties are not
            // earning the block its place in the mod.
            foreach (int count in new int[] { 1, 2, 4, 8 })
            {
                float settled = Column(count, true, watts);
                float mass = ArmourSlabMass * count;
                rows.Add(new DeliveredRow
                {
                    Label = load + "armour slab",
                    Count = count,
                    SettledKelvin = settled,
                    KelvinSaved = bare - settled,
                    AddedMass = mass,
                    AddedPcu = count,
                    KelvinPerTonne = mass > 0f ? (bare - settled) / (mass / 1000f) : 0f,
                });
            }

            return rows;
        }

        /// <summary>
        /// A 1x5x2 slab of light armour weighs what ten armour cubes weigh, because that is the
        /// volume it fills. Costing it any other way would flatter whichever block is being
        /// argued for.
        /// </summary>
        private const float ArmourSlabMass = 5000f;

        /// <summary>
        /// A 2 MW source with <paramref name="panels"/> panels stacked on top of it, run to steady
        /// state in shadow. Returns the source's temperature in kelvin.
        /// </summary>
        private static float Column(int panels, bool armourInstead, float watts)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Heater(), Vector3I.Zero);
            builder.Last.PowerConsumedWatts = watts;
            BlockInstance source = builder.Last;

            BlockModel panel = armourInstead
                ? BlockModel.Solid("ArmourSlab", new Vector3I(1, 5, 2), ArmourSlabMass, Catalog.DefaultThermal())
                : ShippedBlocks.Model("Gauge_LG_Radiator");

            for (int i = 0; i < panels; i++)
            {
                builder.Place(panel, new Vector3I(0, 1 + (i * 5), 0));
            }

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);
            ScenarioRunner runner = new ScenarioRunner(simulation);
            runner.Environment = t => Worlds.Shadow();
            runner.Track("source", source);
            runner.Run(14400f, 1200f);

            float value;
            return runner.Final.Tracked.TryGetValue("source", out value) ? value : 0f;
        }

        // ---- sensitivity: which dial actually moves the number ---------------------------------

        /// <summary>One variation on the shipped radiator, and what it delivered.</summary>
        public class SensitivityRow
        {
            public string Dial;
            public string Change;
            public float SettledKelvin;

            /// <summary>Kelvin below the shipped radiator's result. Negative is worse.</summary>
            public float KelvinVersusShipped;

            /// <summary>Where the panel itself settled, kelvin.</summary>
            public float PanelKelvin;

            /// <summary>The gradient the joint is carrying at equilibrium.</summary>
            public float JointDropKelvin;

            /// <summary>W/K of the source-to-panel joint, measured in this rig.</summary>
            public float JointWattsPerKelvin;

            /// <summary>Watts crossing that joint at equilibrium — what the panel is actually removing.</summary>
            public float ThroughJointWatts;
        }

        /// <summary>
        /// The shipped radiator against a set of one-at-a-time changes, each run on the same
        /// 200 kW column. This is the table a tuning decision is actually made from: it says which
        /// property is binding, by changing one at a time and seeing which one the answer moves
        /// with.
        ///
        /// Some of these are dials the definitions already have and some are dials that do not
        /// exist yet. That is deliberate — the point is to find out whether a dial would be worth
        /// adding *before* adding it, rather than shipping a knob that turns out to be attached to
        /// nothing.
        /// </summary>
        public static List<SensitivityRow> Sensitivity()
        {
            List<SensitivityRow> rows = new List<SensitivityRow>();
            ShippedBlocks.Definition shipped = ShippedBlocks.Get("Gauge_LG_Radiator");

            float baseline = PanelColumn(ShippedBlocks.Model("Gauge_LG_Radiator"));
            rows.Add(Row("(shipped)", "as built", ShippedBlocks.Model("Gauge_LG_Radiator"), baseline));

            // The dial that prompted the question: a bigger fake surface.
            foreach (float scaler in new float[] { 2.5f, 5f, 10f })
            {
                rows.Add(Row("ExposedSurfaceMultiplier", "x" + N(scaler / shipped.Thermal.ExposedSurfaceMultiplier, 1)
                    + "  (" + N(scaler, 2) + ")", Variant(shipped, t => t.ExposedSurfaceMultiplier = scaler), baseline));
            }

            // The other surface dial the definitions already carry.
            rows.Add(Row("Emissivity", "0.35 -> 0.80", Variant(shipped, t => t.Emissivity = 0.80f), baseline));

            // Conduction, three ways. These are the ones the definitions cannot currently express.
            rows.Add(Row("mount faces", "2/6 -> 6/6", MountEverywhere(shipped), baseline));
            rows.Add(Row("panel depth", "1x5x2 -> 1x1x2, same mass", ShortPath(shipped), baseline));
            rows.Add(Row("both of the above", "6/6 and 1x1x2", ShortPathEverywhere(shipped), baseline));

            // The other way to feed a panel, and the reason this table matters. A bolt joint and a
            // coolant sink face are two different couplings to the same panel, and the definitions
            // already carry a dial for one of them.
            rows.Add(CoolantFed(baseline));

            return rows;
        }

        /// <summary>
        /// The same panel on the same load, reached through a coolant sink face instead of a bolt
        /// joint. Everything else is held: same source, same watts, same panel.
        /// </summary>
        private static SensitivityRow CoolantFed(float baseline)
        {
            GridBuilder builder = GridBuilder.Large();

            // Source and panel sit under the same ring, three cells apart, so they do not touch:
            // the loop has to be the only path between them or the comparison measures conduction.
            // Both sink cells are on a straight run, because a corner carries no sink port.
            Vector3I sourceCell = new Vector3I(1, 0, 0);
            Vector3I panelTop = new Vector3I(4, 0, 0);

            builder.Place(Heater(), sourceCell);
            builder.Last.PowerConsumedWatts = 200000f;
            BlockInstance source = builder.Last;

            // The panel hangs below the ring with its top face against the sink.
            builder.Place(ShippedBlocks.Model("Gauge_LG_Radiator"), panelTop - new Vector3I(0, 4, 0));
            BlockInstance panel = builder.Last;

            List<Vector3I> ring = PipeFitter.RectangleXZ(new Vector3I(0, 1, 0), 6, 3);
            Dictionary<int, Vector3I> sinks = new Dictionary<int, Vector3I>();
            sinks[ring.IndexOf(sourceCell + Vector3I.Up)] = Vector3I.Down;
            sinks[ring.IndexOf(panelTop + Vector3I.Up)] = Vector3I.Down;
            PipeFitter.BuildRing(builder, ring, -1, sinks);

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);
            ScenarioRunner runner = new ScenarioRunner(simulation);
            runner.Environment = t => Worlds.Shadow();
            runner.Track("source", source);
            runner.Track("panel", panel);
            runner.Run(14400f, 1200f);

            float sourceKelvin;
            float panelKelvin;
            runner.Final.Tracked.TryGetValue("source", out sourceKelvin);
            runner.Final.Tracked.TryGetValue("panel", out panelKelvin);

            // A sink face's coupling, for comparison with the bolt joint above it.
            float coupling = 0f;
            IList<CoolantLoop> loops = simulation.Solver.Loops;
            if (loops != null && loops.Count > 0)
            {
                foreach (LoopLink link in loops[0].Links) coupling = Math.Max(coupling, link.Conductance);
            }

            return new SensitivityRow
            {
                Dial = "coolant sink",
                Change = "fed by a loop, not bolted",
                SettledKelvin = sourceKelvin,
                KelvinVersusShipped = baseline - sourceKelvin,
                PanelKelvin = panelKelvin,
                JointDropKelvin = sourceKelvin - panelKelvin,
                JointWattsPerKelvin = coupling,
                ThroughJointWatts = 0f,
            };
        }

        private static SensitivityRow Row(string dial, string change, BlockModel model, float baseline)
        {
            ColumnResult result = PanelColumnDetail(model);

            return new SensitivityRow
            {
                Dial = dial,
                Change = change,
                SettledKelvin = result.SourceKelvin,
                KelvinVersusShipped = baseline - result.SourceKelvin,
                PanelKelvin = result.PanelKelvin,
                JointDropKelvin = result.SourceKelvin - result.PanelKelvin,
                JointWattsPerKelvin = result.JointWattsPerKelvin,
                ThroughJointWatts = result.JointWattsPerKelvin * (result.SourceKelvin - result.PanelKelvin),
            };
        }

        /// <summary>The shipped radiator with one thermal property changed and nothing else.</summary>
        private static BlockModel Variant(ShippedBlocks.Definition shipped, Action<BlockThermalProperties> change)
        {
            BlockModel model = ShippedBlocks.Model("Gauge_LG_Radiator");
            BlockThermalProperties thermal = Clone(shipped.Thermal);
            change(thermal);
            model.Thermal = thermal;
            return model;
        }

        private static BlockThermalProperties Clone(BlockThermalProperties source)
        {
            return new BlockThermalProperties
            {
                Conductivity = source.Conductivity,
                SpecificHeat = source.SpecificHeat,
                Emissivity = source.Emissivity,
                ExposedSurfaceMultiplier = source.ExposedSurfaceMultiplier,
                ProducerWasteEnergy = source.ProducerWasteEnergy,
                ConsumerWasteEnergy = source.ConsumerWasteEnergy,
                CriticalTemperature = source.CriticalTemperature,
                OverheatDamagePerKelvin = source.OverheatDamagePerKelvin,
            };
        }

        /// <summary>The same panel, but bolted on by every face rather than only top and bottom.</summary>
        private static BlockModel MountEverywhere(ShippedBlocks.Definition shipped)
        {
            return BlockModel.Solid("RadiatorAllMounts", shipped.Size, shipped.Mass, shipped.Thermal);
        }

        /// <summary>
        /// The same panel's mass and properties in a block one cell deep along its mount axis.
        ///
        /// Conduction runs centre-to-interface, so a block's half-depth along the contact axis is
        /// in the denominator: the shipped panel is five cells tall, which puts 6.25 m of metal
        /// between its middle and the joint it hangs from. This variant asks how much of the
        /// radiator's problem is that shape rather than its surface.
        /// </summary>
        private static BlockModel ShortPath(ShippedBlocks.Definition shipped)
        {
            BlockModel model = BlockModel.Solid("RadiatorShallow", new Vector3I(1, 1, 2),
                shipped.Mass, shipped.Thermal);

            foreach (Vector3I cell in model.LocalCells())
            {
                int state = CellSurface.SelfAirtightMask;
                state = CellSurface.WithSelfMount(state, Face.Up, true);
                state = CellSurface.WithSelfMount(state, Face.Down, true);
                model.SetLocalSurface(cell, state);
            }
            return model;
        }

        private static BlockModel ShortPathEverywhere(ShippedBlocks.Definition shipped)
        {
            return BlockModel.Solid("RadiatorShallowAllMounts", new Vector3I(1, 1, 2),
                shipped.Mass, shipped.Thermal);
        }

        /// <summary>What the column rig settled at, in enough detail to say which limit bound.</summary>
        private class ColumnResult
        {
            public float SourceKelvin;
            public float PanelKelvin;
            public float JointWattsPerKelvin;
        }

        private static float PanelColumn(BlockModel panel)
        {
            return PanelColumnDetail(panel).SourceKelvin;
        }

        /// <summary>
        /// A 200 kW source with one panel on it — the load where a panel still matters — reporting
        /// both temperatures and the conductance between them.
        ///
        /// Both ends matter. A shed figure quoted at a reference temperature and a conductance
        /// quoted per kelvin cannot be compared directly, because neither says what temperature the
        /// panel actually reaches; the drop across the joint at equilibrium is what settles which
        /// of the two is binding, and it is a measurement rather than an inference.
        /// </summary>
        private static ColumnResult PanelColumnDetail(BlockModel panel)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Heater(), Vector3I.Zero);
            builder.Last.PowerConsumedWatts = 200000f;
            BlockInstance source = builder.Last;
            builder.Place(panel, new Vector3I(0, 1, 0));
            BlockInstance placed = builder.Last;

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);
            ScenarioRunner runner = new ScenarioRunner(simulation);
            runner.Environment = t => Worlds.Shadow();
            runner.Track("source", source);
            runner.Track("panel", placed);
            runner.Run(14400f, 1200f);

            ColumnResult result = new ColumnResult();
            runner.Final.Tracked.TryGetValue("source", out result.SourceKelvin);
            runner.Final.Tracked.TryGetValue("panel", out result.PanelKelvin);

            // The one link between the two, read out of the solver rather than recomputed.
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            int a = -1;
            int b = -1;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (ReferenceEquals(nodes[i].Block, source)) a = i;
                if (ReferenceEquals(nodes[i].Block, placed)) b = i;
            }
            foreach (ThermalLink link in simulation.Solver.Links)
            {
                bool joins = (link.NodeA == a && link.NodeB == b) || (link.NodeA == b && link.NodeB == a);
                if (joins) result.JointWattsPerKelvin += link.Conductance;
            }
            return result;
        }

        // ---- heat pump ---------------------------------------------------------------------------

        /// <summary>
        /// The large heat pump across a sweep of temperature gaps, reporting which of its three
        /// limits binds at each. The block's whole character is which one that is, and it changes
        /// twice across an ordinary range.
        /// </summary>
        public static List<PumpRow> Pump()
        {
            List<PumpRow> rows = new List<PumpRow>();
            ThermalSettings settings = new ThermalSettings();
            HeatPumpShape shape = ThermalHeatPumpShapes.Get("Gauge_LG_HeatPump", Vector3I.One);

            float cold = 300f;
            foreach (float gap in new float[] { 5f, 10f, 20f, 40f, 60f, 100f, 200f, 400f })
            {
                float raw = settings.HeatPumpCarnotFraction * cold / gap;
                float coefficient = Math.Min(raw, settings.HeatPumpMaxCoefficient);
                float lifted = Math.Min(coefficient * shape.MaxPowerWatts, shape.RatedWatts);
                float drawn = coefficient > 0f ? lifted / coefficient : 0f;

                string binding;
                if (raw > settings.HeatPumpMaxCoefficient) binding = "coefficient cap";
                else if (coefficient * shape.MaxPowerWatts >= shape.RatedWatts) binding = "rating";
                else binding = "carnot";

                rows.Add(new PumpRow
                {
                    ColdKelvin = cold,
                    GapKelvin = gap,
                    Coefficient = coefficient,
                    LiftedWatts = lifted,
                    DrawnWatts = drawn,
                    Binding = binding,
                });
            }

            return rows;
        }

        // ---- coolant loops --------------------------------------------------------------------------

        /// <summary>
        /// Rings of rising size around one hot block, so the shape of the return on plumbing is
        /// visible rather than asserted. Each pipe couples to the fluid at its own full strength,
        /// so the interesting question is whether the *delivered* cooling keeps up with that or
        /// flattens against something else.
        /// </summary>
        public static List<LoopRow> Loops()
        {
            List<LoopRow> rows = new List<LoopRow>();

            foreach (int side in new int[] { 3, 4, 6, 8 })
            {
                rows.Add(Ring(side));
            }
            return rows;
        }

        private static LoopRow Ring(int side)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(3, 1, 3));

            // The source sits under the ring's near edge, so a sink face can actually reach it.
            // Under the centre it could not: a rectangle's perimeter never crosses its middle, and
            // a ring whose sink faces armour is the failure this measurement exists to avoid.
            Vector3I under = new Vector3I(1, 0, 0);
            BlockInstance occupant = builder.Grid.GetAtCell(under);
            builder.Grid.Remove(occupant);
            builder.Placed.Remove(occupant);
            builder.Place(Heater(), under);
            builder.Last.PowerConsumedWatts = 500000f;
            BlockInstance source = builder.Last;

            List<Vector3I> ring = PipeFitter.RectangleXZ(new Vector3I(0, 1, 0), side, side);

            int above = ring.IndexOf(under + Vector3I.Up);
            if (above < 0) throw new InvalidOperationException("No ring cell sits above the source");

            Dictionary<int, Vector3I> sinks = new Dictionary<int, Vector3I>();
            sinks[above] = Vector3I.Down;

            List<BlockInstance> pipes = PipeFitter.BuildRing(builder, ring, -1, sinks);

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);
            ScenarioRunner runner = new ScenarioRunner(simulation);
            runner.Environment = t => Worlds.Shadow();
            runner.Track("source", source);
            runner.Run(14400f, 1200f);

            float settled;
            runner.Final.Tracked.TryGetValue("source", out settled);

            float fluid = 0f;
            float coupling = 0f;
            IList<CoolantLoop> loops = simulation.Solver.Loops;
            if (loops != null)
            {
                foreach (CoolantLoop loop in loops)
                {
                    fluid += loop.ThermalMass;
                    foreach (LoopLink link in loop.Links) coupling += link.Conductance;
                }
            }

            return new LoopRow
            {
                Pipes = pipes.Count,
                Sinks = sinks.Count,
                FluidThermalMass = fluid,
                CouplingWattsPerKelvin = coupling,
                SettledKelvin = settled,
            };
        }

        // ---- reporting -------------------------------------------------------------------------------

        private static string N(float value, int decimals = 1)
        {
            return value.ToString("n" + decimals, CultureInfo.InvariantCulture);
        }

        /// <summary>The whole pass as text, for reading.</summary>
        public static string Report()
        {
            StringBuilder sb = new StringBuilder();
            ThermalSettings settings = new ThermalSettings();

            sb.AppendLine("BLOCKS  (shed at " + N(ReferenceTemperature, 0) + " K into a "
                + N(ReferenceAmbient, 1) + " K sky; reach = conduction across 100 K)");
            sb.AppendLine();
            sb.AppendLine(string.Format("{0,-42} {1,-13} {2,5} {3,8} {4,5} {5,9} {6,8} {7,10} {8,6} {9,9} {10,10}",
                "block", "kind", "cells", "mass kg", "pcu", "cap J/K", "area m2", "shed W",
                "mounts", "joint W/K", "shed/reach"));

            foreach (BlockRow row in Blocks())
            {
                sb.AppendLine(string.Format("{0,-42} {1,-13} {2,5} {3,8} {4,5} {5,9} {6,8} {7,10} {8,6} {9,9} {10,10}",
                    row.Subtype, row.Kind, row.Cells, N(row.Mass, 0), row.Pcu,
                    N(row.CapacityJoulesPerKelvin, 0), N(row.ExposedAreaAlone, 1),
                    N(row.ShedMountedWatts, 0), row.MountFaceCount + "/6",
                    N(row.ConductanceWattsPerKelvin, 0), N(row.ShedOverReach, 2)));
            }

            sb.AppendLine();
            sb.AppendLine("LOAD  (waste heat at full rating, as the shipped definitions compute it)");
            sb.AppendLine();
            sb.AppendLine(string.Format("{0,-30} {1,-13} {2,10} {3,9} {4,10} {5,12}",
                "block", "type", "rated MW", "path", "fraction", "waste W"));

            foreach (LoadRow row in Loads())
            {
                sb.AppendLine(string.Format("{0,-30} {1,-13} {2,10} {3,9} {4,10} {5,12}",
                    row.Subtype, row.TypeId, N(row.RatedMegawatts, 2), row.Path,
                    N(row.WasteFraction, 3), N(row.WasteWatts, 0)));
            }

            sb.AppendLine();
            sb.AppendLine("DELIVERED  (one source, panels stacked on it, shadow, 4 h to steady state)");
            sb.AppendLine();
            sb.AppendLine(string.Format("{0,-28} {1,6} {2,12} {3,12} {4,10} {5,12}",
                "fit", "count", "settled K", "saved K", "mass kg", "K per tonne"));

            foreach (DeliveredRow row in Delivered())
            {
                sb.AppendLine(string.Format("{0,-28} {1,6} {2,12} {3,12} {4,10} {5,12}",
                    row.Label, row.Count, N(row.SettledKelvin, 1), N(row.KelvinSaved, 1),
                    N(row.AddedMass, 0), N(row.KelvinPerTonne, 2)));
            }

            sb.AppendLine();
            sb.AppendLine("SENSITIVITY  (200 kW source, one panel; which dial moves the answer)");
            sb.AppendLine();
            sb.AppendLine(string.Format("{0,-20} {1,-28} {2,10} {3,8} {4,9} {5,8} {6,10} {7,11}",
                "dial", "change", "source K", "gain K", "panel K", "drop K", "joint W/K", "through W"));

            foreach (SensitivityRow row in Sensitivity())
            {
                sb.AppendLine(string.Format("{0,-20} {1,-28} {2,10} {3,8} {4,9} {5,8} {6,10} {7,11}",
                    row.Dial, row.Change, N(row.SettledKelvin, 1), N(row.KelvinVersusShipped, 1),
                    N(row.PanelKelvin, 1), N(row.JointDropKelvin, 1),
                    N(row.JointWattsPerKelvin, 0), N(row.ThroughJointWatts, 0)));
            }

            sb.AppendLine();
            sb.AppendLine("HEAT PUMP  (large grid, cold side 300 K)");
            sb.AppendLine();
            sb.AppendLine(string.Format("{0,8} {1,12} {2,10} {3,10} {4,16}",
                "gap K", "coefficient", "lifted W", "drawn W", "binding limit"));

            foreach (PumpRow row in Pump())
            {
                sb.AppendLine(string.Format("{0,8} {1,12} {2,10} {3,10} {4,16}",
                    N(row.GapKelvin, 0), N(row.Coefficient, 2), N(row.LiftedWatts, 0),
                    N(row.DrawnWatts, 0), row.Binding));
            }

            sb.AppendLine();
            sb.AppendLine("COOLANT RINGS  (500 kW block, one sink face)");
            sb.AppendLine();
            sb.AppendLine(string.Format("{0,7} {1,7} {2,12} {3,14} {4,12}",
                "pipes", "sinks", "fluid J/K", "coupling W/K", "settled K"));

            foreach (LoopRow row in Loops())
            {
                sb.AppendLine(string.Format("{0,7} {1,7} {2,12} {3,14} {4,12}",
                    row.Pipes, row.Sinks, N(row.FluidThermalMass, 0),
                    N(row.CouplingWattsPerKelvin, 0), N(row.SettledKelvin, 1)));
            }

            sb.AppendLine();
            sb.AppendLine("thermal clock: HeatTimeScale = " + N(settings.HeatTimeScale, 0));
            return sb.ToString();
        }

        /// <summary>The block table as CSV, for diffing a tuning pass against the one before it.</summary>
        public static string BlocksCsv()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("subtype,kind,large,cells,mass_kg,pcu,build_s,specific_heat,emissivity,"
                + "conductivity,exposed_surface_multiplier,capacity_j_per_k,area_m2,shed_alone_w,shed_mounted_w,"
                + "mount_faces,joint_face,joint_w_per_k,shed_over_reach");

            foreach (BlockRow row in Blocks())
            {
                sb.AppendLine(string.Join(",", new string[]
                {
                    row.Subtype, row.Kind, row.Large ? "large" : "small",
                    row.Cells.ToString(CultureInfo.InvariantCulture),
                    F(row.Mass), row.Pcu.ToString(CultureInfo.InvariantCulture), F(row.BuildSeconds),
                    F(row.SpecificHeat), F(row.Emissivity), F(row.Conductivity), F(row.ExposedSurfaceMultiplier),
                    F(row.CapacityJoulesPerKelvin), F(row.ExposedAreaAlone), F(row.ShedAloneWatts),
                    F(row.ShedMountedWatts), row.MountFaceCount.ToString(CultureInfo.InvariantCulture),
                    row.JointFace, F(row.ConductanceWattsPerKelvin), F(row.ShedOverReach),
                }));
            }
            return sb.ToString();
        }

        private static string F(float value)
        {
            return value.ToString("r", CultureInfo.InvariantCulture);
        }
    }
}
