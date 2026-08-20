using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// What a substep costs, split into what it spends per node, per conduction link and per
    /// exposed face.
    ///
    /// The step budget bounds a step by <c>MaxLinkVisitsPerStep</c> — substeps times links — while
    /// a substep also runs the environment pass, which is per node and scales with how much of
    /// that node is exposed. The budget cannot see any of that, so two grids with the same link
    /// count and different shapes are granted the same budget for very different work. This lab
    /// measures the coefficients so the budget can charge for what a substep actually does.
    ///
    /// <para>
    /// **Two fits, not one, and the reason is worth stating.** The first attempt fitted
    /// <c>cost = a*nodes + b*links</c> with the environment on and produced nonsense — 0.37 ns a
    /// link, a negative r², and a solid box measuring *cheaper* than a stick with a third of the
    /// links. On a cube lattice every cell face is either bonded or exposed, so
    /// <c>faces ≈ 6*nodes − 2*links</c>: exposure and link count are very nearly collinear across
    /// any family of shapes, and no fit can separate them. The box was not cheaper because links
    /// are free; it was cheaper because its interior blocks have no exposed faces to integrate.
    /// </para>
    ///
    /// <para>
    /// So the environment is separated by *differencing* instead. Each shape is measured twice,
    /// once with radiation, convection and solar off and once with them on. The conduction-only
    /// figure fits nodes and links, which are independent. The difference between the two fits
    /// exposed faces alone, through the origin. Each fit then has one job and no collinearity.
    /// </para>
    ///
    /// <para>
    /// **This machine is not the game.** The harness is .NET 9 and the game is .NET 4.8, and a
    /// field report has measured the same solver about three times slower in game. What transfers
    /// is the *ratios*, which are arithmetic per element rather than throughput, and the ratios
    /// are what a budget needs. Absolute nanoseconds here are a floor, not a prediction.
    /// </para>
    /// </summary>
    public static class ElementCostLab
    {
        public class Row
        {
            public string Shape;
            public int Nodes;
            public int Links;

            /// <summary>Exposed cell faces summed over every node: what the environment pass walks.</summary>
            public int Faces;

            public double LinksPerNode;
            public double FacesPerNode;

            /// <summary>Substeps each step was cut into, which differs per shape by stiffness.</summary>
            public double SubstepsPerStep;

            /// <summary>Substep passes the timed run made. A short run measures noise.</summary>
            public long Substeps;

            /// <summary>Nanoseconds per substep with the environment switched off.</summary>
            public double ConductionNanoseconds;

            /// <summary>Nanoseconds per substep with radiation, convection and solar on.</summary>
            public double EnvironmentNanoseconds;

            /// <summary>What the environment pass added.</summary>
            public double EnvironmentCost
            {
                get { return EnvironmentNanoseconds - ConductionNanoseconds; }
            }
        }

        public class Fit
        {
            /// <summary>Nanoseconds a substep spends per node, before any exposure.</summary>
            public double PerNode;

            /// <summary>Nanoseconds a substep spends per conduction link.</summary>
            public double PerLink;

            /// <summary>Nanoseconds the environment pass spends per node, whatever its exposure.</summary>
            public double PerEnvironmentNode;

            /// <summary>Nanoseconds the environment pass spends per exposed cell face.</summary>
            public double PerFace;

            /// <summary>How many links one node is worth, before any exposure.</summary>
            public double NodeWeight
            {
                get { return PerLink <= 0d ? 0d : PerNode / PerLink; }
            }

            /// <summary>How many links one exposed face is worth.</summary>
            public double FaceWeight
            {
                get { return PerLink <= 0d ? 0d : PerFace / PerLink; }
            }

            /// <summary>
            /// How many links one node is worth once the environment is on: its share of the
            /// conduction pass plus its share of the environment pass, before exposure.
            /// </summary>
            public double NodeWeightWithEnvironment
            {
                get { return PerLink <= 0d ? 0d : (PerNode + PerEnvironmentNode) / PerLink; }
            }

            public double ConductionRSquared;
            public double EnvironmentRSquared;

            public List<Row> Rows = new List<Row>();
        }

        /// <summary>
        /// Shapes chosen for their link-to-node ratio, spanning as much of the range as a lattice
        /// allows: isolated blocks touch nothing, a stick is a chain, a plate is two-dimensional,
        /// and a solid box approaches three links per node. The hollow box is here to break the
        /// collinearity as far as it can be broken — it has a box's surface with a shell's link
        /// count. The ship is held out of both fits and predicted from them as a check.
        /// </summary>
        private static IEnumerable<KeyValuePair<string, HashSet<Vector3I>>> Shapes(int target)
        {
            int side = Math.Max(2, (int)Math.Round(Math.Pow(target, 1d / 3d)));
            int plateSide = Math.Max(2, (int)Math.Round(Math.Sqrt(target)));
            int hollowSide = Math.Max(3, (int)Math.Round(Math.Sqrt(target / 6d)) + 2);

            yield return new KeyValuePair<string, HashSet<Vector3I>>("dust", Dust(target));
            yield return new KeyValuePair<string, HashSet<Vector3I>>("stick", GridShapes.Stick(target));
            yield return new KeyValuePair<string, HashSet<Vector3I>>("comb", Comb(target));
            yield return new KeyValuePair<string, HashSet<Vector3I>>(
                "plate", GridShapes.Plate(plateSide, plateSide));
            yield return new KeyValuePair<string, HashSet<Vector3I>>(
                "hollow", GridShapes.HollowBox(Vector3I.Zero, new Vector3I(hollowSide, hollowSide, hollowSide)));
            yield return new KeyValuePair<string, HashSet<Vector3I>>(
                "box", GridShapes.SolidBox(Vector3I.Zero, new Vector3I(side, side, side)));

            // The shipped ship shape at its own proportions. Its size need not match the others:
            // the fit predicts from nodes, links and faces, not from grid size.
            yield return new KeyValuePair<string, HashSet<Vector3I>>("ship", GridShapes.Ship());
        }

        /// <summary>Blocks spaced so none touches another: nodes with no links at all.</summary>
        private static HashSet<Vector3I> Dust(int count)
        {
            HashSet<Vector3I> cells = new HashSet<Vector3I>(Vector3I.Comparer);
            int side = Math.Max(1, (int)Math.Ceiling(Math.Pow(count, 1d / 3d)));

            for (int z = 0; z < side && cells.Count < count; z++)
                for (int y = 0; y < side && cells.Count < count; y++)
                    for (int x = 0; x < side && cells.Count < count; x++)
                        cells.Add(new Vector3I(x * 2, y * 2, z * 2));

            return cells;
        }

        /// <summary>
        /// A spine with teeth: every second block on a stick carries a one-block branch. Fills the
        /// gap between dust and a stick, where a fit is otherwise extrapolating.
        /// </summary>
        private static HashSet<Vector3I> Comb(int count)
        {
            HashSet<Vector3I> cells = new HashSet<Vector3I>(Vector3I.Comparer);
            int z = 0;

            while (cells.Count < count)
            {
                cells.Add(new Vector3I(0, 0, z));
                if (cells.Count < count && (z % 2) == 0) cells.Add(new Vector3I(0, 1, z));
                z++;
            }
            return cells;
        }

        private static ThermalSettings Settings(bool environment)
        {
            ThermalSettings settings = new ThermalSettings
            {
                Frequency = 4,
                SimulationSpeed = 1f,

                // Deliberately stiff. At the shipped 225 these grids demand a single substep a
                // step, so a per-substep figure would be measuring the per-step overhead the
                // substep loop sits inside — array syncing, environment precompute, write-back —
                // rather than the per-element work a budget charges for. Running thermal time
                // fast forces tens of substeps a step, which is the regime a large grid is in and
                // the one the budget exists to bound. HeatTimeScale divides heat capacity, so a
                // large value is a stiff grid.
                HeatTimeScale = 20000f,

                // The measurement is cost per substep, so nothing may refuse or shorten one.
                MaxSubsteps = 4096,
                MaxSubstepsPerBlock = 0,
                MaxLinkVisitsPerStep = 0,

                EnableEnvironment = environment,
                EnableRadiation = environment,
                EnableConvection = environment,
                EnableSolarHeat = environment,

                EnableConduction = true,
            };
            settings.Derive();
            return settings;
        }

        private static double Measure(
            HashSet<Vector3I> cells, bool environment, float seconds,
            out int nodeCount, out int links, out int faces, out long substepCount, out double perStep)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.PlaceAll(Catalog.LightArmor(), cells);

            ThermalSimulation simulation = builder.BuildSimulation(Settings(environment));

            // A gradient across the grid, so no link can be skipped for having equal ends — a
            // settled grid measures the branch that decides not to work rather than the work.
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i].Temperature = 250f + ((i * 37) % 400);
            }

            faces = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                for (int f = 0; f < Face.Count; f++) faces += nodes[i].ExposedFaces[f];
            }

            EnvironmentSample sample = environment
                ? Worlds.Space(Vector3.Normalize(new Vector3(1f, 1f, 1f)))
                : Worlds.Shadow();

            int steps = Math.Max(1, (int)(seconds * simulation.Settings.StepsPerSecond));

            // Warm up: the first pass through the solver is the JIT, and the first touch of every
            // flat array is a page fault. Neither is what a budget needs to charge for.
            simulation.StepExact(Math.Min(steps, 8), sample);

            long substepsBefore = simulation.Work.SolverSubsteps;

            Stopwatch clock = Stopwatch.StartNew();
            simulation.StepExact(steps, sample);
            clock.Stop();

            long substeps = simulation.Work.SolverSubsteps - substepsBefore;
            if (substeps <= 0) substeps = 1;

            nodeCount = nodes.Count;
            links = simulation.Solver.LinkCount;
            substepCount = substeps;
            perStep = substeps / (double)steps;
            return (clock.Elapsed.TotalMilliseconds * 1e6) / substeps;
        }

        /// <summary>
        /// Best of three, for the reason <see cref="FrequencyLab"/> documents: a mean measures the
        /// machine's other work, and the fastest run is the one where the solver had the core to
        /// itself. Both conditions are measured the same way, since the environment cost is their
        /// difference and a noisy minuend would swamp it.
        /// </summary>
        private static Row Best(string shape, HashSet<Vector3I> cells, float seconds)
        {
            int nodes = 0, links = 0, faces = 0;
            double conduction = double.MaxValue;
            double environment = double.MaxValue;

            long substeps = 0;
            double perStep = 0;

            for (int attempt = 0; attempt < 3; attempt++)
            {
                int n, l, f;
                long sc;
                double ps;

                double cold = Measure(cells, false, seconds, out n, out l, out f, out sc, out ps);
                if (cold < conduction) conduction = cold;

                double hot = Measure(cells, true, seconds, out n, out l, out f, out sc, out ps);
                if (hot < environment) environment = hot;

                nodes = n;
                links = l;
                faces = f;
                substeps = sc;
                perStep = ps;
            }

            return new Row
            {
                Shape = shape,
                Nodes = nodes,
                Links = links,
                Faces = faces,
                LinksPerNode = nodes <= 0 ? 0d : links / (double)nodes,
                FacesPerNode = nodes <= 0 ? 0d : faces / (double)nodes,
                ConductionNanoseconds = conduction,
                EnvironmentNanoseconds = environment,
                SubstepsPerStep = perStep,
                Substeps = substeps,
            };
        }

        /// <summary>
        /// Least squares through the origin on two predictors. No intercept, because a substep over
        /// an empty grid does no work — an intercept would be fitting the per-step overhead the
        /// substep loop sits inside, which a budget charges once per step rather than per element.
        /// </summary>
        public static void Solve2(
            IList<Row> rows, Func<Row, double> x1, Func<Row, double> x2, Func<Row, double> y,
            out double a, out double b, out double r2)
        {
            double s11 = 0, s22 = 0, s12 = 0, s1y = 0, s2y = 0;

            for (int i = 0; i < rows.Count; i++)
            {
                double p = x1(rows[i]), q = x2(rows[i]), v = y(rows[i]);
                s11 += p * p;
                s22 += q * q;
                s12 += p * q;
                s1y += p * v;
                s2y += q * v;
            }

            double determinant = (s11 * s22) - (s12 * s12);
            if (Math.Abs(determinant) < 1e-9)
            {
                a = 0; b = 0; r2 = 0;
                return;
            }

            a = ((s1y * s22) - (s2y * s12)) / determinant;
            b = ((s2y * s11) - (s1y * s12)) / determinant;

            double mean = 0;
            for (int i = 0; i < rows.Count; i++) mean += y(rows[i]);
            mean /= Math.Max(1, rows.Count);

            double residual = 0, total = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                double predicted = (a * x1(rows[i])) + (b * x2(rows[i]));
                double actual = y(rows[i]);
                residual += (actual - predicted) * (actual - predicted);
                total += (actual - mean) * (actual - mean);
            }
            r2 = total <= 0d ? 1d : 1d - (residual / total);
        }

        /// <summary>Least squares through the origin on one predictor.</summary>
        public static void Solve1(
            IList<Row> rows, Func<Row, double> x, Func<Row, double> y, out double a, out double r2)
        {
            double sxx = 0, sxy = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                double p = x(rows[i]);
                sxx += p * p;
                sxy += p * y(rows[i]);
            }

            a = sxx <= 0d ? 0d : sxy / sxx;

            double mean = 0;
            for (int i = 0; i < rows.Count; i++) mean += y(rows[i]);
            mean /= Math.Max(1, rows.Count);

            double residual = 0, total = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                double predicted = a * x(rows[i]);
                double actual = y(rows[i]);
                residual += (actual - predicted) * (actual - predicted);
                total += (actual - mean) * (actual - mean);
            }
            r2 = total <= 0d ? 1d : 1d - (residual / total);
        }

        /// <summary>
        /// Shapes to measure, or null for all of them. A large run is dominated by building the
        /// grids, so a sweep at half a million blocks is worth restricting to the shapes that
        /// span the ratio — stick, plate and box do that on their own.
        /// </summary>
        public static string[] OnlyShapes;

        public static Fit Run(int target = 8000, float seconds = 20f)
        {
            Fit fit = new Fit();

            foreach (KeyValuePair<string, HashSet<Vector3I>> shape in Shapes(target))
            {
                if (OnlyShapes != null && Array.IndexOf(OnlyShapes, shape.Key) < 0) continue;
                fit.Rows.Add(Best(shape.Key, shape.Value, seconds));
            }

            // The ship is held out and predicted: a shape the coefficients have never seen is the
            // only honest test of whether they predict anything.
            //
            // Dust is held out for a different reason. With no links it has no conduction
            // stiffness, so it demands one substep however stiff the settings are, and its
            // per-substep figure is really the per-step overhead — array syncing, environment
            // sampling, write-back — which is an order of magnitude larger than the per-element
            // work and is charged once a step rather than per element. Leaving it in drove the
            // per-link coefficient negative. It stays in the table because that overhead is worth
            // seeing; it does not belong in a per-element fit.
            List<Row> fitted = new List<Row>();
            for (int i = 0; i < fit.Rows.Count; i++)
            {
                string shape = fit.Rows[i].Shape;
                if (shape != "ship" && shape != "dust") fitted.Add(fit.Rows[i]);
            }

            double perNode, perLink, conductionR2;
            Solve2(fitted, r => r.Nodes, r => r.Links, r => r.ConductionNanoseconds,
                out perNode, out perLink, out conductionR2);

            // The environment pass is fitted on nodes *and* faces. Per face alone does not
            // describe it: the measured cost per face rises sevenfold as exposure falls, because
            // most of the pass is per node — sampling the environment, the four radiation terms'
            // setup, the write-back — and only the face loop inside it scales with exposure.
            double perEnvironmentNode, perFace, environmentR2;
            Solve2(fitted, r => r.Nodes, r => r.Faces, r => r.EnvironmentCost,
                out perEnvironmentNode, out perFace, out environmentR2);

            fit.PerNode = perNode;
            fit.PerLink = perLink;
            fit.PerEnvironmentNode = perEnvironmentNode;
            fit.PerFace = perFace;
            fit.ConductionRSquared = conductionR2;
            fit.EnvironmentRSquared = environmentR2;
            return fit;
        }

        /// <summary>What the fitted model says a substep on this grid should cost, in nanoseconds.</summary>
        public static double Predict(Fit fit, Row row)
        {
            return (fit.PerNode * row.Nodes) + (fit.PerLink * row.Links)
                + (fit.PerEnvironmentNode * row.Nodes) + (fit.PerFace * row.Faces);
        }

        private static string N(double value, int decimals = 2)
        {
            return value.ToString("n" + decimals, CultureInfo.InvariantCulture);
        }

        public static string Report(int target = 8000, float seconds = 20f)
        {
            Fit fit = Run(target, seconds);

            StringBuilder sb = new StringBuilder();
            sb.Append("Element cost: what a substep spends per node, link and exposed face\n");
            sb.Append("==================================================================\n\n");
            sb.Append("  Shapes chosen for their link-to-node ratio. Each is measured twice, with\n");
            sb.Append("  the environment off and on; conduction fits nodes and links, and the\n");
            sb.Append("  difference between the two fits exposed faces. The ship row is held out of\n");
            sb.Append("  both fits and predicted from them.\n\n");
            sb.Append("  .NET 9 on this machine. The ratios transfer to the game; the absolute\n");
            sb.Append("  nanoseconds do not.\n\n");

            sb.Append("  shape      nodes    links   faces  link/n  face/n   sub/step  passes"
                + "     cond ns      env ns   predicted   error\n");

            foreach (Row row in fit.Rows)
            {
                double predicted = Predict(fit, row);
                double error = row.EnvironmentNanoseconds <= 0d
                    ? 0d
                    : 100d * (predicted - row.EnvironmentNanoseconds) / row.EnvironmentNanoseconds;

                sb.Append("  ").Append(row.Shape.PadRight(8))
                  .Append(row.Nodes.ToString("n0").PadLeft(8))
                  .Append(row.Links.ToString("n0").PadLeft(9))
                  .Append(row.Faces.ToString("n0").PadLeft(8))
                  .Append(N(row.LinksPerNode).PadLeft(8))
                  .Append(N(row.FacesPerNode).PadLeft(8))
                  .Append(N(row.SubstepsPerStep, 1).PadLeft(11))
                  .Append(row.Substeps.ToString("n0").PadLeft(8))
                  .Append(N(row.ConductionNanoseconds, 0).PadLeft(12))
                  .Append(N(row.EnvironmentNanoseconds, 0).PadLeft(12))
                  .Append(N(predicted, 0).PadLeft(12))
                  .Append((N(error, 1) + " %").PadLeft(9))
                  .Append(row.Shape == "ship" ? "   (held out)\n" : "\n");
            }

            sb.Append("\n  conduction pass\n");
            sb.Append("    ns per node                 ").Append(N(fit.PerNode, 2)).Append('\n');
            sb.Append("    ns per link                 ").Append(N(fit.PerLink, 2)).Append('\n');
            sb.Append("    r2                          ").Append(N(fit.ConductionRSquared, 4)).Append('\n');
            sb.Append("\n  environment pass, as what it adds\n");
            sb.Append("    ns per node                 ").Append(N(fit.PerEnvironmentNode, 2)).Append('\n');
            sb.Append("    ns per exposed face         ").Append(N(fit.PerFace, 2)).Append('\n');
            sb.Append("    r2                          ").Append(N(fit.EnvironmentRSquared, 4)).Append('\n');
            sb.Append("\n  in link-equivalents, which is what a budget would charge:\n");
            sb.Append("    a node, conduction only     ").Append(N(fit.NodeWeight)).Append(" links\n");
            sb.Append("    a node, environment on      ").Append(N(fit.NodeWeightWithEnvironment))
              .Append(" links\n");
            sb.Append("    an exposed face             ").Append(N(fit.FaceWeight)).Append(" links\n");

            return sb.ToString();
        }

        public static string Csv(int target = 8000, float seconds = 20f)
        {
            Fit fit = Run(target, seconds);

            StringBuilder sb = new StringBuilder();
            sb.Append("shape,nodes,links,faces,links_per_node,faces_per_node,conduction_ns,"
                + "environment_ns,predicted_ns,ns_per_node,ns_per_link,ns_per_environment_node,"
                + "ns_per_face,node_weight,face_weight,r2_conduction,r2_environment\n");

            foreach (Row row in fit.Rows)
            {
                sb.Append(row.Shape).Append(',')
                  .Append(row.Nodes).Append(',')
                  .Append(row.Links).Append(',')
                  .Append(row.Faces).Append(',')
                  .Append(R(row.LinksPerNode)).Append(',')
                  .Append(R(row.FacesPerNode)).Append(',')
                  .Append(R(row.ConductionNanoseconds)).Append(',')
                  .Append(R(row.EnvironmentNanoseconds)).Append(',')
                  .Append(R(Predict(fit, row))).Append(',')
                  .Append(R(fit.PerNode)).Append(',')
                  .Append(R(fit.PerLink)).Append(',')
                  .Append(R(fit.PerEnvironmentNode)).Append(',')
                  .Append(R(fit.PerFace)).Append(',')
                  .Append(R(fit.NodeWeight)).Append(',')
                  .Append(R(fit.FaceWeight)).Append(',')
                  .Append(R(fit.ConductionRSquared)).Append(',')
                  .Append(R(fit.EnvironmentRSquared)).Append('\n');
            }

            return sb.ToString();
        }

        private static string R(double value)
        {
            return value.ToString("r", CultureInfo.InvariantCulture);
        }
    }
}
