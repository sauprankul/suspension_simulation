using SuspensionSimulation.Design;
using SuspensionSimulation.Solvers.Design;

namespace SuspensionSimulation.Tests;

internal static class AssemblyGraphChecks
{
    internal static void Verify()
    {
        var folder=Path.Combine(Path.GetTempPath(),"suspension-graph-"+Guid.NewGuid().ToString("N"));
        try
        {
            var library=new DesignAssetLibrary(folder);var corner=SyntheticAssemblyCatalog.SeedCorner(library);var resolver=new AssemblyGraphResolver(library);
            var graph=resolver.Compile(new(corner.Identity.AssetId));var report=MechanismMobility.Inspect(graph,1);
            if(report.DegreesOfFreedom!=1||report.Rank!=17||!report.IsClosed||report.RedundantRows!=0) throw new Exception("Analytic corner mobility failed: "+System.Text.Json.JsonSerializer.Serialize(report));
            var axle=new AssemblyDefinition(corner.Identity with {AssetId="analytic-axle",Name="Synthetic mirrored axle"},
                [new AssemblyInstance("left",new(corner.Identity.AssetId),new(AssemblyAuthoring.IdentityPlacement.Frame with {OriginM=new(0,.6,0)}))],[],[]) {GroundedInstances=["left/chassis","right/chassis"]};
            axle=AssemblyAuthoring.MirrorInstance(axle,"left","right",new(new(0,0,0),new(0,1,0)));axle=library.Publish(axle,null);
            var mirrored=resolver.Compile(new(axle.Identity.AssetId));var axleReport=MechanismMobility.Inspect(mirrored,2);
            if(axleReport.DegreesOfFreedom!=2||!axleReport.IsClosed) throw new Exception("Mirrored nested mobility changed.");
            foreach(var port in mirrored.Interfaces.Values) DesignValidation.Frame(port.Frame);
            var l=mirrored.Interfaces["left/upright:toe"].Frame.OriginM;var r=mirrored.Interfaces["right/upright:toe"].Frame.OriginM;
            if((l-new DesignVector(r.X,-r.Y,r.Z)).Length>1e-12) throw new Exception("Mirror geometry lost parity.");
            var arm=library.LoadComponent("analytic-arm")!;var updated=library.Publish(arm with {Identity=arm.Identity with {Name="Updated compatible arm"}},arm.Identity.RevisionId);
            var latest=resolver.Resolve(new(axle.Identity.AssetId),mirrored);
            if(latest.IsStale||latest.Graph!.Bodies.Where(x=>x.Component.Identity.AssetId==arm.Identity.AssetId).Any(x=>x.Component.Identity.RevisionId!=updated.Identity.RevisionId)) throw new Exception("Compatible revisions did not propagate consistently.");
            library.Publish(updated with {Ports=updated.Ports.RemoveAt(1)},updated.Identity.RevisionId);
            var conflict=resolver.Resolve(new(axle.Identity.AssetId),latest.Graph);
            if(!conflict.IsStale||!ReferenceEquals(conflict.Graph,latest.Graph)) throw new Exception("Conflict replaced part of the last-good graph.");
            var pinned=corner with {Identity=corner.Identity with {AssetId="mixed-revisions"},Instances=corner.Instances.SetItem(1,corner.Instances[1] with {Definition=new("analytic-arm",arm.Identity.RevisionId)})};
            Reject(()=>resolver.CompileDraft(pinned));
            // A group preserves all constraints, exported root interfaces, placements and ground paths.
            library.Publish(updated,library.LoadComponent("analytic-arm")!.Identity.RevisionId);
            var group=AssemblyAuthoring.Group(corner,["upper","upright"],corner.Identity with {AssetId="upper-group",Name="Upper group"},"group");library.Publish(group.Child,null);
            var grouped=resolver.CompileDraft(group.Parent);var groupedReport=MechanismMobility.Inspect(grouped,1);
            if(groupedReport.DegreesOfFreedom!=1||!groupedReport.IsClosed||grouped.Joints.Length!=graph.Joints.Length) throw new Exception("Grouping changed the mechanism.");
            var bundle=AssemblyGraphResolver.Export(latest.Graph!);var restored=DesignJson.Deserialize<PortableDesignBundle>(DesignJson.Serialize(bundle));
            if(restored.Components.Length!=3||restored.Assemblies.Length!=2||restored.Manifest.Dependencies.Any(x=>x.Sha256.Length!=64)) throw new Exception("Portable graph lost dependencies.");
            var frozen=AssemblyGraphResolver.OpenBundle(restored);
            if(frozen.Bodies.Where(x=>x.Component.Identity.AssetId==arm.Identity.AssetId).Any(x=>x.Component.Identity.RevisionId!=updated.Identity.RevisionId)) throw new Exception("Frozen graph followed a later library head.");
            var imported=library.ImportClone(restored);var importedGraph=resolver.Compile(new(imported.Identity.AssetId));
            if(MechanismMobility.Inspect(importedGraph).DegreesOfFreedom!=2||importedGraph.Components.Any(x=>restored.Components.Any(y=>x.Identity.AssetId==y.Identity.AssetId))) throw new Exception("Independent import lost mobility or reused mutable identities.");
            var unclosed=resolver.CompileDraft(corner with {Instances=corner.Instances.SetItem(3,corner.Instances[3] with {Placement=new(AssemblyAuthoring.IdentityPlacement.Frame with {OriginM=new(.4,0,.2)})})});
            if(MechanismMobility.Inspect(unclosed).IsClosed) throw new Exception("Unclosed joint pose was accepted.");
            var duplicate=resolver.CompileDraft(corner with {Joints=corner.Joints.Add(corner.Joints[0] with {Id="redundant-hinge"})});
            if(MechanismMobility.Inspect(duplicate).RedundantRows!=5) throw new Exception("Redundant constraints were not diagnosed.");
            foreach(var kind in Enum.GetValues<JointKind>()) CheckJoint(library,kind);
            // An indirect asset cycle must be rejected even if each stored revision is structurally valid.
            var child=library.Publish(new AssemblyDefinition(corner.Identity with {AssetId="cycle-child"},[],[],[]),null);
            var parent=library.Publish(new AssemblyDefinition(corner.Identity with {AssetId="cycle-parent"},[new AssemblyInstance("child",new(child.Identity.AssetId),AssemblyAuthoring.IdentityPlacement)],[],[]),null);
            Reject(()=>library.Publish(child with {Instances=[new AssemblyInstance("parent",new(parent.Identity.AssetId),AssemblyAuthoring.IdentityPlacement)]},child.Identity.RevisionId));
        }
        finally {Directory.Delete(folder,true);}
    }
    private static void CheckJoint(DesignAssetLibrary library,JointKind kind)
    {
        var axis=kind==JointKind.Universal?new DesignVector(0,1,0):new DesignVector(0,0,1);
        var first=library.LoadComponent("analytic-chassis")!;
        var frame=AssemblyAuthoring.IdentityPlacement.Frame;
        var p=first with {Identity=first.Identity with {AssetId="joint-"+kind},Frames=first.Frames.SetItem(0,first.Frames[0] with {Frame=frame}),Ports=first.Ports.SetItem(0,first.Ports[0] with {FrameId=first.Frames[0].Id})};
        p=library.Publish(p,null);
        var placement=new InstancePlacement(frame with {Axis=axis,OriginM=kind==JointKind.RigidDistance?new(0,0,1):new(0,0,0)});
        var a=new AssemblyDefinition(first.Identity with {AssetId="joint-check-"+kind},[new ComponentInstance("a",new(p.Identity.AssetId),AssemblyAuthoring.IdentityPlacement),new ComponentInstance("b",new(p.Identity.AssetId),placement)],
            [new("joint",kind,new("a","lower"),new("b","lower"))],[]) {GroundedInstances=["a"]};
        var report=MechanismMobility.Inspect(new AssemblyGraphResolver(library).CompileDraft(a));
        var expected=kind switch {JointKind.Fixed=>0,JointKind.Revolute or JointKind.Prismatic=>1,JointKind.Cylindrical or JointKind.Universal=>2,JointKind.Spherical=>3,JointKind.RigidDistance=>5,_=>-1};
        if(report.DegreesOfFreedom!=expected||!report.IsClosed) throw new Exception(kind+" primitive mobility failed.");
    }
    private static void Reject(Action action) {try {action();}catch(ArgumentException) {return;}catch(InvalidOperationException) {return;}throw new Exception("Invalid graph accepted.");}
}
