using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using SuspensionSimulation.Components;
using SuspensionSimulation.Design;

namespace SuspensionSimulation.Solvers.Design;

/// <summary>Add the authored chassis without changing existing car parameter values or historical source revisions.</summary>
internal sealed class ChassisMigration(DesignAssetLibrary library)
{
    public GarageSetup Convert(GarageSetup setup)
    {
        var source=library.LoadAssembly(setup.RootAssembly.AssetId,setup.RootAssembly.RevisionId)??throw new ArgumentException("Missing vehicle source for chassis migration.");
        if(source.Vehicle is not {} vehicle||vehicle.Chassis is not null) return setup;
        var required=new[]{"front","rear-left","rear-right","front-bar","rear-bar"};if(!required.All(id=>source.Instances.Any(i=>i.Id==id))) return setup; // Historical non-twins mechanisms retain their original solver.
        var graph=new AssemblyGraphResolver(library).Compile(setup.RootAssembly,[],setup.ParameterValues??[]);
        DesignVector Center(string corner) {var c=vehicle.Contacts.Single(c=>c.Corner==corner);var b=graph.Bodies.Single(b=>b.Path==c.InstancePath);return b.Transform.Point(b.Component.Points.Single(p=>p.Id==c.CenterPointId).PositionM);}
        var front=(Center("FL").X+Center("FR").X)/2;var rear=(Center("RL").X+Center("RR").X)/2;var s=ChassisAuthoring.Estimated(front-rear,front);
        var identity=new RevisionIdentity("estimated-twins-chassis-v1","draft","GR86 / BRZ estimated complete chassis",new(DataProvenance.Estimated,ChassisAuthoring.EstimateNote,ChassisAuthoring.Sources));
        var chassis=library.LoadComponent(identity.AssetId)??library.Publish(ChassisAuthoring.Create(identity,s),null);
        var rack=graph.Bodies.Single(b=>b.Component.Kind==ComponentKind.Rack);var rackMount=rack.Transform.Point(rack.Component.Points.Single(p=>p.Id=="origin").PositionM);
        var installation=new ChassisInstallation("chassis",front,front-rear,rackMount,
            [new("front",ChassisSection.Front,new(front,0,0)),new("front-bar",ChassisSection.Front,new(front,0,0)),new("rear-left",ChassisSection.Rear,new(rear,0,0)),new("rear-right",ChassisSection.Rear,new(rear,0,0)),new("rear-bar",ChassisSection.Rear,new(rear,0,0))],
            [..graph.GroundedBodies.Select(path=>new ChassisGroundAttachment(path,path=="front/guide"?ChassisSection.Rack:path.StartsWith("front",StringComparison.Ordinal)?ChassisSection.Front:ChassisSection.Rear))]);
        var next=source with {Identity=source.Identity with {AssetId="chassis-"+System.Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source.Identity.AssetId+"\n"+source.Identity.RevisionId)))[..24],RevisionId="draft",Name=source.Identity.Name+" · chassis",Provenance=source.Identity.Provenance with {Note=source.Identity.Provenance.Note+" Added estimated inextensible beam chassis; original source retained."}},Instances=source.Instances.Add(new ComponentInstance("chassis",new(chassis.Identity.AssetId),AssemblyAuthoring.IdentityPlacement)),GroundedInstances=source.GroundedInstances.Add("chassis"),Vehicle=vehicle with {Chassis=installation}};
        var published=library.LoadAssembly(next.Identity.AssetId)??library.Publish(next,null,setup.ParameterValues??[]);
        return setup with {RootAssembly=new(published.Identity.AssetId,setup.RootAssembly.RevisionId is null?null:published.Identity.RevisionId)};
    }
}
