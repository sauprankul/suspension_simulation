using System.Collections.Immutable;

namespace SuspensionSimulation.Design;

/// <summary>Explicit setup interfaces for the estimated installed examples. Source geometry is unchanged.</summary>
public static class TwinsGarageHardware
{
    public static TwinsInstalledModels Configure(DesignAssetLibrary library,TwinsInstalledModels models)
    {
        ComponentDefinition Save(ComponentDefinition c)=>library.LoadComponent(c.Identity.AssetId)??library.Publish(c,null);
        AssemblyDefinition Update(AssemblyDefinition a)=>library.Publish(a,a.Identity.RevisionId);
        foreach(var axle in new[]{"front","rear"})
        {
            var stock=library.LoadComponent("installed-"+axle+"-damper")!;var behavior=(AxialDamperBehavior)stock.Behavior!;
            Save(stock with {Identity=stock.Identity with {AssetId="example-adjustable-"+axle+"-damper",RevisionId="draft",Name="Example adjustable "+axle+" damper (estimated)",Provenance=stock.Identity.Provenance with {Note="Synthetic adjustable example using estimated stock stroke/seats; not a manufacturer part."}},
                Behavior=behavior with {SpringSeats=behavior.SpringSeats! with {PerchAdjustmentId="perch"}},
                Adjustments=[new("perch","Spring perch",new ThreadedAdjustmentPath(AssemblyAuthoring.IdentityPlacement.Frame,0,.02,.04),new(.5),[],[],"Moves the lower spring seat toward the upper seat. Positive offset increases compression at fixed eye length; equilibrium determines the final ride height. Estimated ±20 mm range.")]});
        }
        var mount=library.LoadComponent("installed-top-mount")!;var bearing=mount.Frames.Single(f=>f.Id=="bearing").Frame;
        Save(mount with {Identity=mount.Identity with {AssetId="example-camber-plate",RevisionId="draft",Name="Example camber plate (synthetic)",Provenance=mount.Identity.Provenance with {Note="Synthetic ±20 mm bearing slot, not measured hardware."}},
            Adjustments=[new("camber-slot","Bearing position",new StraightAdjustmentPath(bearing with {OriginM=bearing.OriginM-new DesignVector(0,.02,0)},bearing with {OriginM=bearing.OriginM+new DesignVector(0,.02,0)},PathOrientation.HoldStart),new(.5),["bearing"],["bearing"],"Moves the bearing along the component's local Y axis through an estimated ±20 mm slot. The right-hand instance is mirrored; resulting camber is solved from geometry.")]});
        EquipmentSlot Slot(string id,string name,string target,IEnumerable<string> choices)=>new(id,name,target,[..choices],"Only equipment explicitly approved by this assembly is listed. Dimensions and rates belong to the selected part; estimated source data remain estimates.");
        var corner=library.LoadAssembly("installed-front-corner-v2")!;
        if(corner.EquipmentSlots is null) Update(corner with {EquipmentSlots=[Slot("top-mount","Top mount","top-mount",[mount.Identity.AssetId,"example-camber-plate"])],SetupAdjustments=[new("top-mount","Top mount")]});
        ComponentDefinition AdjustableEnd(string sourceId,string exampleId,string point,string name)
        {
            var source=library.LoadComponent(sourceId)!;var end=source.Points.Single(p=>p.Id==point).PositionM;
            var inner=source.Points.Single(p=>p.Id=="inner").PositionM;var delta=end-inner;var axis=delta/delta.Length;
            var candidate=Math.Abs(axis.Z)<.9?new DesignVector(0,0,1):new DesignVector(1,0,0);var reference=candidate-axis*axis.Dot(candidate);reference/=reference.Length;
            return Save(source with {Identity=source.Identity with {AssetId=exampleId,RevisionId="draft",Name=name+" (synthetic)",Provenance=source.Identity.Provenance with {Note="Synthetic ±5 mm threaded outboard adjustment example; not measured stock or aftermarket hardware."}},
                Adjustments=[new("length","Outboard length",new ThreadedAdjustmentPath(new(end,axis,reference),delta.Length-.005,delta.Length,delta.Length+.005),new(.5),[point],[point],"Moves only the outboard attachment along the component's declared axis. Offset in mm, positive lengthens the end; the rigid mechanism determines toe/camber and motion ratio. Estimated ±5 mm range.")]});
        }
        var frontAxle=library.LoadAssembly("installed-front-axle-v2")!;
        if(frontAxle.EquipmentSlots is null)
        {
            var slots=new List<EquipmentSlot>();var adjusters=new List<SetupAdjustmentExposure>();
            foreach(var side in new[]{"left","right"})
            {
                var target=side+"-tie";var stockId="twins-"+target;var example=AdjustableEnd(stockId,"example-"+target,"outer",side+" adjustable tie rod");
                slots.Add(Slot(target,side+" tie rod",target,[stockId,example.Identity.AssetId]));adjusters.Add(new(target,side+" tie rod"));
            }
            Update(frontAxle with {EquipmentSlots=[..slots],SetupAdjustments=[..adjusters]});
        }
        var rear=library.LoadAssembly("installed-rear-corner-v2")!;
        if(rear.EquipmentSlots is null)
        {
            var lower=AdjustableEnd("twins-rear-lower","example-rear-lower","ball","Adjustable rear lower arm");
            var toe=AdjustableEnd("twins-rear-toe","example-rear-toe","outer","Adjustable rear toe link");
            Update(rear with {EquipmentSlots=[Slot("lower","Rear lower arm","lower",["twins-rear-lower",lower.Identity.AssetId]),Slot("toe","Rear toe link","rear-toe",["twins-rear-toe",toe.Identity.AssetId])],SetupAdjustments=[new("lower","Rear lower arm"),new("rear-toe","Rear toe link")]});
        }
        AssemblyDefinition Root(AssemblyDefinition root)
        {
            if(root.EquipmentSlots is not null) return root;
            var slots=new List<EquipmentSlot>();var exposed=new List<SetupAdjustmentExposure>();
            foreach(var cornerId in new[]{"FL","FR","RL","RR"})
            {
                var axle=cornerId[0]=='F'?"front":"rear";
                slots.Add(Slot(cornerId+"-spring",cornerId+" spring",cornerId+"-spring",new[]{"gr86","brz"}.Select(v=>"installed-"+v+"-"+axle+"-spring")));
                slots.Add(Slot(cornerId+"-damper",cornerId+" damper",cornerId+"-damper",["installed-"+axle+"-damper","example-adjustable-"+axle+"-damper"]));
                exposed.Add(new(cornerId+"-damper",cornerId+" damper"));
            }
            foreach(var axle in new[]{"front","rear"}) slots.Add(Slot(axle+"-bar",axle+" bar",axle+"-bar",new[]{"gr86","brz"}.Select(v=>"installed-"+v+"-"+axle+"-bar-assembly")));
            return Update(root with {EquipmentSlots=[..slots],SetupAdjustments=[..exposed]});
        }
        return new(Root(models.Gr86),Root(models.Brz));
    }
}
