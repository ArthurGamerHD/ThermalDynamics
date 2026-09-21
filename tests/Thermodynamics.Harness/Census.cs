using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    public static class Census
    {
        public class Tier
        {
            public string Name;

            public float Share;

            public float Mass;

            public float Conductivity;

            public float CriticalTemperature;

            public int MountFaces = 6;

            public string Example;
        }

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

        public const float ProducerShare = 0.109f;

        public const float ProducerWatts = 111000f;

        public const float ProducerCriticalTemperature = 1050f;

        public const float ProducerMass = 2430f;

        private static BlockModel producer;

/// <summary>Producer operation.</summary>
        public static BlockModel Producer()
        {
            if (producer != null) return producer;

            BlockThermalProperties thermal = Catalog.DefaultThermal();
            thermal.Conductivity = 56.667f;
            thermal.CriticalTemperature = ProducerCriticalTemperature;

            producer = BlockModel.Solid(ProducerName, Vector3I.One, ProducerMass, thermal);
            return producer;
        }

        public static class Field
        {
            public const float LeastDemand = 21.35f;   // STR Hound, 1,293 blocks, 19 Aug, Frequency 4
            public const float MostDemand = 31.25f;    // UNSC Infinity, 42,051 blocks, 18 Aug, Frequency 4

            public const float RaisedAtCap8 = 0.0116f;
            public const float RaisedAtCap4 = 0.0601f;
            public const float RaisedAtCap2 = 0.2368f;
            public const float RaisedAtCap1 = 0.3910f;

            public const float HottestObserved = 938.9f;
            public const float HottestRating = 1050f;
        }

        public static class Corpus
        {
            public const int Ships = 8098;

            public const float AirP10 = 6.22f;
            public const float AirP50 = 7.90f;
            public const float AirP90 = 18.42f;
            public const float AirMax = 22.41f;

            public const float VacuumP50 = 7.40f;
            public const float VacuumP90 = 10.03f;
            public const float VacuumMax = 13.35f;

            public const float LitP50 = 16.83f;
            public const float StructuralP50 = 7.25f;
            public const float LitShare = 0.4474f;

            public const float BetweenTheModes = 0.491f;

            public const float AirRatioP10 = 1.00f;
            public const float AirRatioP50 = 1.07f;
            public const float AirRatioP90 = 2.52f;

            public const float CensusAirRatio = 1.00f;

            public const float StiffestFacesMean = 3.46f;


            public const float ProducerSharePercentile = 86.8f;
            public const float ProducerWattsPercentile = 88.0f;
            public const float WastePerBlockPercentile = 96.4f;

            public const float WastePerBlockP50 = 335f;
            public const float WastePerBlockP90 = 6056f;

            public const float CensusWastePerBlock = 12099f;

            public const float FlooredAtCap8 = 0.0087f;
            public const float FlooredAtCap4 = 0.2318f;
            public const float FlooredAtCap2 = 0.4022f;
            public const float FlooredAtCap1 = 0.7529f;
        }

        private static BlockModel[] models;

/// <summary>Models operation.</summary>
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

/// <summary>Mounted operation.</summary>
                built[i] = Mounted(tier.Name, tier.Mass, thermal, tier.MountFaces);
            }

            models = built;
            return models;
        }

/// <summary>Mounted operation.</summary>
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

/// <summary>TierAt operation.</summary>
        public static int TierAt(int index)
        {
            float position = ((index * 2654435761u) % 1000000u) / 1000000f;

            float running = 0f;
            for (int i = 0; i < Tiers.Length; i++)
            {
                running += Tiers[i].Share;
                if (position < running) return i;
            }

            return Tiers.Length - 1;
        }

/// <summary>ProducesHeatAt operation.</summary>
        public static bool ProducesHeatAt(int index)
        {
            int period = (int)Math.Round(1f / ProducerShare);
            return period > 0 && (index % period) == 0;
        }

/// <summary>IsProducer operation.</summary>
        public static bool IsProducer(ThermalNode node)
        {
            if (node == null || node.Block == null || node.Block.Model == null) return false;

            return node.Block.Model.Name == ProducerName;
        }

        public const string ProducerName = "producer";

/// <summary>PlaceCensus operation.</summary>
        public static GridBuilder PlaceCensus(this GridBuilder builder, IEnumerable<Vector3I> cells)
        {
/// <summary>Models operation.</summary>
            BlockModel[] tiers = Models();
/// <summary>Producer operation.</summary>
            BlockModel source = Producer();

/// <summary>List operation.</summary>
            List<Vector3I> layout = new List<Vector3I>(cells);
/// <summary>TiersFor operation.</summary>
            int[] tierOf = TiersFor(layout);

/// <summary>Bolt operation.</summary>
            BlockOrientation[] orientations = Bolt(layout, tierOf, tiers);

            for (int i = 0; i < layout.Count; i++)
            {
                if (ProducesHeatAt(i))
                {
                    builder.Place(source, layout[i]);
                    continue;
                }

                BlockModel model = tiers[tierOf[i]];
                builder.Place(model, layout[i], orientations[i]);
            }

            return builder;
        }

/// <summary>TiersFor operation.</summary>
        public static int[] TiersFor(List<Vector3I> layout)
        {
            int[] tierOf = new int[layout.Count];
/// <summary>HashSet operation.</summary>
            HashSet<Vector3I> filled = new HashSet<Vector3I>(layout);

            for (int i = 0; i < layout.Count; i++) tierOf[i] = TierAt(i);

            SurfaceTheLightestTier(layout, tierOf, filled);
            return tierOf;
        }

/// <summary>SurfaceTheLightestTier operation.</summary>
        private static void SurfaceTheLightestTier(
            List<Vector3I> layout, int[] tierOf, HashSet<Vector3I> filled)
        {
            const int Lightest = 0;

/// <summary>List operation.</summary>
            List<int> buried = new List<int>();
/// <summary>List operation.</summary>
            List<int> exposedElsewhere = new List<int>();
            int[] faces = new int[layout.Count];

            for (int i = 0; i < layout.Count; i++)
            {
/// <summary>ExposedFaces operation.</summary>
                faces[i] = ExposedFaces(layout[i], filled);
                if (ProducesHeatAt(i)) continue;

                if (tierOf[i] == Lightest && faces[i] == 0) buried.Add(i);
                else if (tierOf[i] != Lightest && faces[i] > 0) exposedElsewhere.Add(i);
            }

            exposedElsewhere.Sort(delegate(int a, int b)
            {
                int byFaces = faces[b].CompareTo(faces[a]);
                return byFaces != 0 ? byFaces : a.CompareTo(b);
            });

            int swaps = Math.Min(buried.Count, exposedElsewhere.Count);
            for (int s = 0; s < swaps; s++)
            {
                int inside = buried[s];
                int outside = exposedElsewhere[s];

                tierOf[inside] = tierOf[outside];
                tierOf[outside] = Lightest;
            }
        }

        private static BlockOrientation[] boltOrientations;

        private static int[] boltRotatedFace;

        private static int boltIdentity;

/// <summary>EnsureBoltTable operation.</summary>
        private static void EnsureBoltTable()
        {
            if (boltOrientations != null) return;

/// <summary>List operation.</summary>
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

/// <summary>Bolt operation.</summary>
        public static BlockOrientation[] Bolt(IList<Vector3I> layout, int[] tierOf, BlockModel[] tiers)
        {
            EnsureBoltTable();

            int count = layout.Count;
            BlockOrientation[] chosen = new BlockOrientation[count];
            int[] chosenIndex = new int[count];
            bool[] placed = new bool[count];

            Dictionary<Vector3I, int> at = new Dictionary<Vector3I, int>(count, Vector3I.Comparer);
            for (int i = 0; i < count; i++) at[layout[i]] = i;

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
                if (ProducesHeatAt(i))
                {
                    chosen[i] = BlockOrientation.Identity;
                    chosenIndex[i] = boltIdentity;
                    placed[i] = true;
                    continue;
                }

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

                        if (!placed[neighbour]
/// <summary>MountsToward operation.</summary>
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

/// <summary>MountsToward operation.</summary>
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

/// <summary>ExposedFaces operation.</summary>
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

/// <summary>DriveCensus operation.</summary>
        public static int DriveCensus(ThermalSimulation simulation)
        {
/// <summary>DriveCensus operation.</summary>
            return DriveCensus(simulation, ProducerWatts);
        }

/// <summary>DriveCensus operation.</summary>
        public static int DriveCensus(ThermalSimulation simulation, float watts)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            int producers = 0;

            for (int i = 0; i < nodes.Count; i++)
            {
                if (!IsProducer(nodes[i])) continue;

                float waste = nodes[i].Thermal.ProducerWasteEnergy;
                if (waste <= 0f) continue;

                nodes[i].Block.PowerProducedWatts = watts / waste;
                nodes[i].RefreshHeatGeneration();
                producers++;
            }

            return producers;
        }

/// <summary>DriveThrust operation.</summary>
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
