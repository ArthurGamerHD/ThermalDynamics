using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using VRage.Scripting;
using VRage.Game;
using VRage.Game.Components;
using VRageRender.Messages;
using VRage.Utils;
if (args.Length != 1) throw new ArgumentException("Expected game Bin64 directory");
var compiler = MyScriptCompiler.Static;
var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
    .Concat(new[] {typeof(MyVoxelMaterialDefinition).Assembly.Location, typeof(MyTextureChange).Assembly.Location,
        typeof(VRageMath.Vector3).Assembly.Location, typeof(MyStringId).Assembly.Location,
        typeof(VRage.MyVRage).Assembly.Location}).Distinct().ToArray();
compiler.AddReferencedAssemblies(paths);
compiler.AddReferencedAssemblies(Directory.GetFiles(args[0], "*.dll").Where(x => !Path.GetFileName(x).StartsWith("System.") && !Path.GetFileName(x).StartsWith("Microsoft.") && !Path.GetFileName(x).Contains("XmlSerializers")).Where(x => { try { using var f = File.OpenRead(x); using var pe = new System.Reflection.PortableExecutable.PEReader(f); return pe.HasMetadata; } catch { return false; } }).ToArray());
new SpaceEngineers.Game.GameWhitelistBootstrap().InitIlChecker();
/// <summary>typeof operation.</summary>
var type = typeof(MyScriptCompiler).Assembly.GetType("VRage.Scripting.Analyzers.WhitelistDiagnosticAnalyzer", true);
var analyzer = (DiagnosticAnalyzer)Activator.CreateInstance(type, BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,
    null, new object[]{compiler.Whitelist, MyWhitelistTarget.ModApi}, null);
var cases = new Dictionary<string,string> {
/// <summary>Read operation.</summary>
 ["thermal-celestial"] = "public class Probe { public double Read(Sandbox.Game.Entities.MyPlanet p) { var sun=Sandbox.Game.MyVisualScriptLogicProvider.GetSunDirection(); var position=p.PositionComp.WorldMatrixRef.Translation; var up=p.WorldMatrix.Up; var radius=p.AverageRadius; return radius+sun.X+position.X+up.X; } }",
/// <summary>Check operation.</summary>
 ["thermal-local-frustum"] = "public class Probe { public bool Check(VRageMath.MatrixD grid, VRageMath.BoundingBoxD box) { var camera=Sandbox.ModAPI.MyAPIGateway.Session.Camera; var frustum=new VRageMath.BoundingFrustumD(grid*VRageMath.MatrixD.Invert(camera.WorldMatrix)*camera.ProjectionMatrix); return frustum.Contains(box)!=VRageMath.ContainmentType.Disjoint; } }",
/// <summary>Read operation.</summary>
 ["occlusion-interface"] = "public class Probe { public bool Read(Sandbox.Game.Entities.MyCubeGrid source, VRageMath.Vector3I position) { VRage.Game.ModAPI.IMyCubeGrid grid=source; var block=grid.GetCubeBlock(position); return block!=null && block.IsFullIntegrity && block.BuildLevelRatio>=1 && !block.HasDeformation && block.BlockDefinition.Context!=null && block.BlockDefinition.Context.IsBaseGame && block.BlockDefinition.Id.SubtypeName==\"LargeBlockArmorBlock\"; } }",
/// <summary>Read operation.</summary>
 ["occlusion-concrete-rejected"] = "public class Probe { public bool Read(Sandbox.Game.Entities.MyCubeGrid grid, VRageMath.Vector3I position) { var block=grid.GetCubeBlock(position); return block!=null && block.IsFullIntegrity && block.BuildLevelRatio>=1 && !block.HasDeformation; } }",

/// <summary>Make operation.</summary>
 ["voxel-base-builder"] = "public class Probe { public VRage.Game.MyVoxelMaterialDefinition Make(VRage.Game.MyVoxelMaterialDefinition source, VRage.Game.MyObjectBuilder_DefinitionBase builder) { var copy = new VRage.Game.MyVoxelMaterialDefinition(); copy.Index = source.Index; copy.Init(builder, source.Context); return copy; } }",
/// <summary>Make operation.</summary>
 ["voxel-xml-base"] = "public class Probe { public VRage.Game.MyVoxelMaterialDefinition Make(VRage.Game.MyVoxelMaterialDefinition source, string xml) { var data = Sandbox.ModAPI.MyAPIGateway.Utilities.SerializeFromXML<VRage.Game.MyObjectBuilder_Definitions>(xml); VRage.Game.MyObjectBuilder_DefinitionBase builder = data.VoxelMaterials[0]; var copy = new VRage.Game.MyVoxelMaterialDefinition(); copy.Index = source.Index; copy.Init(builder, source.Context); return copy; } }",

/// <summary>Read operation.</summary>
 ["model-binary-reader"] = "public class Probe { public void Read() { var reader = Sandbox.ModAPI.MyAPIGateway.Utilities.ReadBinaryFileInGameContent(\"Models/test.mwm\"); reader.ReadString(); reader.BaseStream.Position = 0; reader.Dispose(); } }",
/// <summary>Read operation.</summary>
 ["grid-render-data"] = "public class Probe { public void Read(Sandbox.Game.Entities.MyCubeGrid grid) { var data = ((Sandbox.Game.Components.MyRenderComponentCubeGrid)grid.Render).RenderData; } }",
/// <summary>Read operation.</summary>
 ["model-materials"] = "public class Probe { public void Read(VRage.Game.Models.MyModel model) { var meshes = model.GetMeshList(); } }",
/// <summary>Read operation.</summary>
 ["voxel-enumeration"] = "public class Probe { public void Read() { var materials = Sandbox.Definitions.MyDefinitionManager.Static.GetVoxelMaterialDefinitions(); } }",
/// <summary>Save operation.</summary>
 ["voxel-copy"] = "public class Probe { public void Save(VRage.Game.MyVoxelMaterialDefinition live, VRage.Game.MyVoxelMaterialDefinition saved) { saved.RenderParams = live.RenderParams; } public void Restore(VRage.Game.MyVoxelMaterialDefinition live, VRage.Game.MyVoxelMaterialDefinition saved) { live.RenderParams = saved.RenderParams; live.UpdateVoxelMaterial(); } }",
/// <summary>Edit operation.</summary>
 ["voxel-direct"] = "public class Probe { public void Edit(VRage.Game.MyVoxelMaterialDefinition live) { live.RenderParams.TextureSets[0].ColorMetalY = \"test\"; } }",
/// <summary>Applies the .</summary>
 ["entity-override"] = "public class Probe { public void Apply(VRage.Game.Components.MyRenderComponentBase r, System.Collections.Generic.Dictionary<VRage.Utils.MyStringId, VRageRender.Messages.MyTextureChange> changes) { r.UpdateRenderTextureChanges(changes); } }",
/// <summary>Make operation.</summary>
 ["voxel-builder"] = "public class Probe { public VRage.Game.MyVoxelMaterialDefinition Make(VRage.Game.MyVoxelMaterialDefinition source, Medieval.ObjectBuilders.Definitions.MyObjectBuilder_Dx11VoxelMaterialDefinition builder) { var copy = new VRage.Game.MyVoxelMaterialDefinition(); copy.Index = source.Index; copy.Init(builder, source.Context); return copy; } }"
};
bool failed = false;
foreach(var pair in cases) {
 var compilation = CSharpCompilation.Create("Probe", new[]{ CSharpSyntaxTree.ParseText(pair.Value, new CSharpParseOptions(LanguageVersion.CSharp6))},
    ((System.Collections.Generic.List<MetadataReference>)typeof(MyScriptCompiler).GetField("m_metadataReferences", BindingFlags.Instance|BindingFlags.NonPublic).GetValue(compiler)), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
 var errors = compilation.GetDiagnostics().Where(x=>x.Severity==DiagnosticSeverity.Error).ToArray();
 var diagnostics = await compilation.WithAnalyzers(ImmutableArray.Create(analyzer)).GetAnalyzerDiagnosticsAsync();
 Console.WriteLine(pair.Key + ": compiler="+errors.Length+", whitelist="+diagnostics.Length);
 foreach(var d in errors.Concat(diagnostics)) Console.WriteLine(d);
 bool rejected = pair.Key == "occlusion-concrete-rejected" || pair.Key == "voxel-direct" || pair.Key == "voxel-builder" || pair.Key == "model-materials" || pair.Key == "grid-render-data";
 if (errors.Length != 0 || diagnostics.Any(x => x.Id != "ProhibitedMemberRule")
     || (rejected ? diagnostics.Length == 0 : diagnostics.Length != 0)) failed = true;
}

return failed ? 1 : 0;
