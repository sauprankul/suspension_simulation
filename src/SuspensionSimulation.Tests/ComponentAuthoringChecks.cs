using SuspensionSimulation.Design;
using SuspensionSimulation.Components;

namespace SuspensionSimulation.Tests;

internal static class ComponentAuthoringChecks
{
    internal static void Verify()
    {
        var folder=Path.Combine(Path.GetTempPath(),"suspension-design-"+Guid.NewGuid().ToString("N"));
        var identity=new RevisionIdentity("traction-arm","draft","Traction arm",new(DataProvenance.Synthetic,"Synthetic authoring fixture",null));
        try
        {
            var library=new DesignAssetLibrary(folder);
            foreach(var kind in ComponentAuthoring.RigidTemplates) ComponentAuthoring.ValidateForPublish(ComponentAuthoring.Create(identity,kind,new(.3,.2,.1)));
            var link=ComponentAuthoring.Create(identity,ComponentKind.RigidLink,new(.4,0,0));
            var length=ComponentAuthoring.Measure(link.Points[0].PositionM,link.Points[1].PositionM);
            if(length.DistanceM!=.4) throw new Exception("Driving link length and drawn endpoints disagree.");
            var first=library.Publish(link,null);var second=library.Publish(ComponentAuthoring.Create(first.Identity with {Name="Longer arm"},first.Kind,new(.45,0,0)),first.Identity.RevisionId);
            if(first.Identity.RevisionId==second.Identity.RevisionId || library.LoadComponent(identity.AssetId)!.Identity.Name!="Longer arm" || library.LoadComponent(identity.AssetId,first.Identity.RevisionId)!.Points[1].PositionM.X!=.4)
                throw new Exception("Immutable revision history or latest resolution failed.");
            Reject(()=>library.Publish(link,first.Identity.RevisionId));
            Reject(()=>library.Publish(second with {Points=second.Points.SetItem(1,second.Points[1] with {PositionM=new(.9,0,0)})},second.Identity.RevisionId));
            var custom=ComponentAuthoring.MakeCustom(second);custom=custom with {Points=custom.Points.SetItem(1,custom.Points[1] with {PositionM=new(.5,0,0)})};
            ComponentAuthoring.ValidateForPublish(custom);
            var measure=ComponentAuthoring.Measure(new(0,0,0),new(3,4,0),new(new(10,10,10),new(0,1,0),new(1,0,0)));
            if(measure.DistanceM!=5 || measure.DeltaM!=new DesignVector(3,0,4)) throw new Exception("Arbitrary-point frame measurement is incorrect.");
            library.SaveDraft("component-workspace","{\"pendingLength\":\"invalid text\"}");
            if(!library.ReadDraft("component-workspace")!.Contains("invalid text")) throw new Exception("Recovery draft was lost.");
            var file=Path.Combine(folder,"components",identity.AssetId,"revisions",second.Identity.RevisionId+".json");
            File.WriteAllText(file,File.ReadAllText(file).Replace("Longer arm","Corrupt arm",StringComparison.Ordinal));
            try {library.LoadComponent(identity.AssetId);throw new Exception("Corrupted asset was accepted.");} catch(InvalidDataException) { }
        }
        finally {Directory.Delete(folder,true);}
    }
    private static void Reject(Action action) {try {action();}catch(ArgumentException) {return;}catch(InvalidOperationException) {return;}throw new Exception("Invalid publication accepted.");}
}
