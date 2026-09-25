using Thermodynamics.Presentation;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Thermodynamics.Harness;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Sim
{
    public static class ThermalCorpusExport
    {

        public static int Run(string output)
        {
            Directory.CreateDirectory(output);
            GameBlocks.Warm();
            foreach (string id in new[] { "2771284480", "1650715158", "605516903" })
            {
                string path = Path.Combine(Blueprints.CorpusPath(), "steamapps", "workshop", "content", "244850", id, "bp.sbc");
                var ship = Blueprints.Read(path).Single();
                if (!ship.IsVanilla || ship.Grids.Count != 1) throw new InvalidOperationException("Panel requires vanilla single-grid ships: " + id);
                string scenarioName = id == "1650715158" ? "recovery" : "full-electrical";
                var scenario = Battery.All().Single(s => s.Name == scenarioName);
                var assembly = ship.Build();
                ShipLoad.Apply(assembly, scenario.Load);

                var runner = new AssemblyRunner(assembly) { Environment = scenario.Environment, Integrity = GameBlocks.IntegrityOf };
                Console.WriteLine(ship.Name + " / " + scenarioName + " / " + ship.Blocks + " blocks");
                runner.Run(60);
                Save(output, id, ship, assembly, scenarioName, 60, "loaded", path);
                if (scenarioName == "recovery")
                {
                    ShipLoad.Apply(assembly, ShipLoad.State.Idle);
                    runner.Run(60);
                    Save(output, id, ship, assembly, scenarioName, 120, "idle-recovery", path);
                }
            }
            return 0;
        }

        private static void Save(string output, string id, Blueprints.Ship ship, ShipAssembly assembly,
            string scenario, int seconds, string phase, string path)
        {
            var blockRegions = assembly.Nodes.Select(n => new ThermalVisionRegionPartition.Region(
                (Vector3D)n.Block.Min - new Vector3D(.5),
                (Vector3D)n.Block.MaxExclusive - new Vector3D(.5), n.Temperature)).ToList();
            var timer = System.Diagnostics.Stopwatch.StartNew();
            ThermalVisionRegionOrder blockOrder;
            bool ordered = ThermalVisionRegionOrder.TryBuild(blockRegions, 16384, out blockOrder);
            timer.Stop();
            if (ordered)
            {
                var fragments = new System.Collections.Generic.List<ThermalVisionRegionPartition.Region>();
                blockOrder.WriteNearToFar(new Vector3D(-1000), fragments);
                foreach (var block in blockRegions)
                {
                    Vector3D point = (block.Min + block.Max) * .5;
                    var owners = fragments.Where(r => point.X >= r.Min.X && point.X < r.Max.X
                        && point.Y >= r.Min.Y && point.Y < r.Max.Y && point.Z >= r.Min.Z && point.Z < r.Max.Z).ToList();
                    if (owners.Count != 1 || owners[0].Kelvin != block.Kelvin)
                        throw new InvalidOperationException("Per-block temperature/ownership changed during ordering");
                }
            }
            var nativeBlockAudit = new {
                success = ordered, fragments = ordered ? blockOrder.LeafCount : -1,
                preparationMs = timer.Elapsed.TotalMilliseconds,
                triangleFaces = ordered ? blockOrder.LeafCount * 12 : -1,
                quadFaces = ordered ? blockOrder.LeafCount * 6 : -1,
                note = "Full-grid face submissions before frustum culling; near-plane caps and shared engine usage additional. Not a GPU benchmark."
            };
            Console.WriteLine("  exact block order " + JsonSerializer.Serialize(nativeBlockAudit));
            var mixedDetail = new System.Collections.Generic.List<object>();
            Vector3D max = blockRegions.Select(r => r.Max).Aggregate(Vector3D.Max);
            Vector3D origin = blockRegions.Select(r => r.Min).Aggregate(Vector3D.Min);
            foreach (int budget in new[] {128, 512, 1400})
            {

                var detail = new ThermalVisionBlockDetail(budget, max + new Vector3D(2));
                foreach (var region in blockRegions) detail.Observe(region, true);
                using (var scan = new ThermalVisionRegionScan(1, detail.CoarseCapacity, 1, 10485760, origin: origin))
                {
                    scan.Start(new[] {blockRegions.AsEnumerable()});
                    while (scan.Running) scan.Advance(1024);
                    if (scan.Failure != null) throw new InvalidOperationException(scan.Failure);
                    using var mean = new ThermalVisionRegionScan(scan.CellSize, detail.CoarseCapacity, 1, average: true, origin: origin);
                    mean.Start(new[] {blockRegions.AsEnumerable()});
                    while (mean.Running) mean.Advance(1024);
                    if (mean.Failure != null) throw new InvalidOperationException(mean.Failure);
                    var field = detail.Build(mean, true);
                    ThermalVisionRegionOrder order;
                    if (!ThermalVisionRegionOrder.TryBuild(field, budget, out order))
                        throw new InvalidOperationException("Mixed detail order exceeded budget");
                    var visible = new System.Collections.Generic.List<ThermalVisionRegionPartition.Region>();
                    order.WriteNearToFar(max + new Vector3D(2), visible);

                    var facePlan = new ThermalVisionFacePlan(); facePlan.Build(visible, max + new Vector3D(2));
                    var predicted = blockRegions.Select(block => {
                        Vector3D p = (block.Min + block.Max) * .5;
                        var owners = field.Where(r => p.X >= r.Min.X && p.X < r.Max.X && p.Y >= r.Min.Y
                            && p.Y < r.Max.Y && p.Z >= r.Min.Z && p.Z < r.Max.Z).ToList();
                        if (owners.Count != 1) throw new InvalidOperationException("Mixed detail lost block coverage");
                        return owners[0].Kelvin;
                    }).ToArray();
                    mixedDetail.Add(new {budget, detail.AllExact, detail.RefinedBlocks, fragments = order.LeafCount,
                        originalFaceBillboards = order.LeafCount * 6, retainedFaceBillboards = order.LeafCount * 6 - facePlan.Removed,
                        meanErrorK = predicted.Select((v,i) => Math.Abs(v-blockRegions[i].Kelvin)).Average(), kelvin = predicted});
                    Console.WriteLine("  mixed budget=" + budget + " exact=" + detail.RefinedBlocks + " fragments=" + order.LeafCount
                        + " faces=" + order.LeafCount*6 + " -> " + (order.LeafCount*6-facePlan.Removed));
                }
            }
            var data = new {
                mixedDetail,
                nativeBlockAudit,
                ship = ship.Name, workshopId = id, source = path,
                sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                scenario, seconds, phase, blocks = ship.Blocks, large = ship.Large,
                cellMetres = ship.Grids[0].Builder.Grid.GridSize,
                note = "Existing Battery load/environment; fixed 60 s phases, not equilibrium. Geometry is block bounds, not game meshes.",
                nodes = assembly.Nodes.Select(n => new {
                    type = n.Block.Model.Name,
                    position = new[] { n.Block.Position.X, n.Block.Position.Y, n.Block.Position.Z },
                    min = new[] { n.Block.Min.X, n.Block.Min.Y, n.Block.Min.Z },
                    max = new[] { n.Block.MaxExclusive.X, n.Block.MaxExclusive.Y, n.Block.MaxExclusive.Z },
                    kelvin = n.Temperature
                }).ToArray()
            };
            File.WriteAllText(Path.Combine(output, id + "-" + seconds + ".json"), JsonSerializer.Serialize(data));
        }
    }
}
