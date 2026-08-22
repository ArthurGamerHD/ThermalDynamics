using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// A node's exposure on face <i>f</i> is weighted against the sun's and the wind's incidence
    /// on <b>that same face</b>, and nothing outside this suite would notice if it were not.
    ///
    /// <para>
    /// Both weightings are six-term sums, <c>Σ faceWeight[f] × incidence[f]</c>, written out term
    /// by term so the six face weights can be read once and shared between them. Written out, they
    /// are transcription: a sum that paired face 2's exposure with face 3's incidence would still
    /// compile, still be symmetric, still conserve energy, and still produce plausible numbers on
    /// every ship anyone benchmarks.
    /// </para>
    ///
    /// <para>
    /// <see cref="SolarSymmetryTests"/> cannot see it, and that is not a criticism of it — it
    /// tests a lone cube, whose six faces are identical, so <em>any</em> permutation of them is
    /// invisible by construction. Catching a permutation needs a node whose faces differ, which
    /// is what the fixture below builds: a block buried on five sides, leaving exactly one face
    /// open.
    /// </para>
    ///
    /// <para>
    /// The assertion does not assume which world direction lights which face. It asserts the
    /// weaker and more useful thing: that <b>one</b> direction lights each node, and that the
    /// direction is the same function of the open face for all six of them. A permutation breaks
    /// that — a node open on face <i>f</i> would answer to the direction belonging to face
    /// <i>π(f)</i> — while a sign or handedness convention this suite has no business pinning
    /// does not.
    /// </para>
    /// </summary>
    public class FaceWeightPairingTests
    {
        /// <summary>
        /// A block open on exactly one face: the centre of a plus with one arm left off.
        ///
        /// The centre is placed first so it is the first node, but it is found by its exposure
        /// rather than by its index, so a change to build order cannot quietly retarget the test
        /// onto one of the arms.
        /// </summary>
        private static ThermalSimulation OpenOn(int face, ThermalSettings settings)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);

            for (int f = 0; f < Face.Count; f++)
            {
                if (f == face) continue;
                builder.Place(Catalog.LightArmor(), Face.Offsets[f]);
            }

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.Solver.CollectDiagnostics = true;
            return simulation;
        }

        /// <summary>The node with one open face, and a check that it is the one asked for.</summary>
        private static ThermalNode Centre(ThermalSimulation simulation, int face)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;

            for (int i = 0; i < nodes.Count; i++)
            {
                ThermalNode node = nodes[i];
                if (node.TotalExposedFaces != 1) continue;
                if (node.ExposedFaces[face] <= 0) continue;
                return node;
            }

            Assert.Fail("no node was left open on " + Face.Name(face)
                + " alone — the fixture did not bury the other five faces, so the test would be"
                + " asserting about a block with nothing to distinguish its faces");
            return null;
        }

        /// <summary>
        /// The sun. This half is derivable rather than conventional: <c>ResolveDirection</c>
        /// weights a face by <c>max(0, dot(normal, direction))</c>, so a sun along a face's own
        /// normal weights that face at one and every other at zero. A node open only on face
        /// <i>f</i> must therefore take sunlight from <c>Normals[f]</c> and from nothing else.
        /// </summary>
        [Fact]
        public void EachFaceTakesSunFromItsOwnNormalAndNoOther()
        {
            if (!GameBlocks.IsInstalled) return;

            for (int open = 0; open < Face.Count; open++)
            {
                for (int from = 0; from < Face.Count; from++)
                {
                    ThermalSimulation simulation = OpenOn(open, new ThermalSettings());
                    simulation.StepExact(1, Worlds.Space(Face.Normals[from]));

                    float watts = Centre(simulation, open).LastSolarWatts;
                    string what = "open on " + Face.Name(open) + ", sun from " + Face.Name(from);

                    if (open == from)
                    {
                        Assert.True(watts > 0f, what
                            + ": took no sunlight, so the open face was weighted against some"
                            + " other face's incidence");
                    }
                    else
                    {
                        Assert.True(watts == 0f, what + ": took " + watts
                            + " W with its only open face edge-on or facing away, so the sum"
                            + " paired that face with the wrong incidence");
                    }
                }
            }
        }

        /// <summary>
        /// The wind, which shares the six hoisted face weights with the sun and is a separate
        /// transcription of the same sum.
        ///
        /// Unlike the sun, which direction of travel presents which face is a convention owned by
        /// <c>ComposeRelativeWind</c> and the environment solver, not by the weighting — so this
        /// does not assert what that convention is. It reads the convention off the first face and
        /// then requires the other five to obey the same one, which is what a permutation cannot
        /// do and a sign flip elsewhere in the model would not disturb.
        /// </summary>
        [Fact]
        public void EveryFaceMeetsTheWindOnTheSameConventionAsTheFirst()
        {
            if (!GameBlocks.IsInstalled) return;

            // Fast enough for friction to engage, in air dense enough for it to matter.
            const float Speed = 300f;

            int[] answeredTo = new int[Face.Count];

            for (int open = 0; open < Face.Count; open++)
            {
                int found = -1;

                for (int blowing = 0; blowing < Face.Count; blowing++)
                {
                    ThermalSimulation simulation = OpenOn(open, new ThermalSettings());
                    simulation.StepExact(1, Worlds.WindAndMotion(
                        1f, Speed, Face.Normals[blowing], Vector3.Zero));

                    if (Centre(simulation, open).LastFrictionWatts <= 0f) continue;

                    Assert.True(found < 0, "open on " + Face.Name(open)
                        + ": both " + Face.Name(found) + " and " + Face.Name(blowing)
                        + " drove friction through a single open face, so the sum is reading"
                        + " more than one face's incidence");
                    found = blowing;
                }

                Assert.True(found >= 0, "open on " + Face.Name(open)
                    + ": no wind direction reached it, so its exposure is being weighted against"
                    + " an incidence that is always zero");

                answeredTo[open] = found;
            }

            // The convention itself is whatever it is; what matters is that all six agree on it.
            // Identity means the wind that reaches a face runs along that face's own normal, and
            // Opposite means it runs against it. Anything else is a permutation.
            bool identity = answeredTo[0] == 0;

            for (int open = 0; open < Face.Count; open++)
            {
                int expected = identity ? open : Face.Opposite(open);

                Assert.True(answeredTo[open] == expected, "the face open on "
                    + Face.Name(open) + " answered to wind along " + Face.Name(answeredTo[open])
                    + ", where " + Face.Name(0) + " answering to " + Face.Name(answeredTo[0])
                    + " makes " + Face.Name(expected) + " the only consistent answer — the six"
                    + " terms of the wind sum are not paired face for face");
            }
        }
    }
}
