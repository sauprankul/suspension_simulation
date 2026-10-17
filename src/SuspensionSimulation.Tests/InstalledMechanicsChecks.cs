using SuspensionSimulation.Design;
using SuspensionSimulation.Solvers.Design;

namespace SuspensionSimulation.Tests;

internal static class InstalledMechanicsChecks
{
    internal static void Verify()
    {
        var repository=AppContext.BaseDirectory;while(!Directory.Exists(Path.Combine(repository,"data"))) repository=Directory.GetParent(repository)?.FullName??throw new Exception("Repository missing.");
        var folder=Path.Combine(Path.GetTempPath(),"installed-twins-"+Guid.NewGuid().ToString("N"));
        try
        {
            var library=new DesignAssetLibrary(folder);var models=TwinsInstalledCatalog.Seed(library,repository);var resolver=new AssemblyGraphResolver(library);
            foreach(var model in new[]{models.Gr86,models.Brz})
            {
                var source=resolver.Compile(new(model.Identity.AssetId));var graph=GraphAdjustments.Freeze(source,[]);var report=MechanismMobility.Inspect(graph,5);
                if(!report.IsClosed||report.DegreesOfFreedom!=5||report.RedundantRows!=0||graph.ElasticElements.Length!=10||graph.SpringSeats.Length!=4) throw new Exception("Installed mechanism topology failed: "+System.Text.Json.JsonSerializer.Serialize(report));
                var kinematics=new TwinsKinematics(graph);var neutral=kinematics.Solve(FourCornerTravel.Zero,0);var mechanics=new InstalledMechanics(kinematics.System);var baseline=mechanics.Evaluate(neutral.Pose.Coordinates);
                if(baseline.Elements.Count(e=>e.Kind=="spring")!=4||baseline.Elements.Any(e=>e.Kind=="spring"&&e.ForceOrTorque<=0)||baseline.Elements.Where(e=>e.Kind=="torsion-bar").Any(e=>Math.Abs(e.EnergyJ)>1e-10)) throw new Exception("Installed neutral spring/bar forces failed.");
                var heave=kinematics.Solve(new(.01,.01,.01,.01),0,neutral);var heaveState=mechanics.Evaluate(heave.Pose.Coordinates);
                if(heaveState.Elements.Where(e=>e.Kind=="torsion-bar").Any(e=>Math.Abs(e.EnergyJ)>1e-8)) throw new Exception("Torsion-only bar resisted symmetric heave.");
                var roll=kinematics.Solve(new(.01,-.01,.01,-.01),0,neutral);var rollState=mechanics.Evaluate(roll.Pose.Coordinates);
                if(rollState.Elements.Where(e=>e.Kind=="torsion-bar").Any(e=>e.EnergyJ<=0)) throw new Exception("Differential travel did not twist both bars.");
                var steered=kinematics.Solve(new(.015,-.01,.02,-.015),225,neutral);mechanics.Evaluate(steered.Pose.Coordinates);
                var bundle=AssemblyGraphResolver.Export(source);var restored=AssemblyGraphResolver.OpenBundle(DesignJson.Deserialize<PortableDesignBundle>(DesignJson.Serialize(bundle)));
                if(restored.ElasticElements.Length!=10||restored.SpringSeats.Length!=4) throw new Exception("Frozen graph lost its mechanical behavior or spring seating.");
            }
            // A physical perch changes compression at fixed eye length; no separate additive preload is present.
            var spring=new CompressionSpringBehavior("a","b",3,.3,.1);var damper=new AxialDamperBehavior("a","b",DamperFamily.Shock,.3,.5,0,0,0,new(.02,.08));
            var initial=ElasticLaws.Spring(spring,ElasticLaws.SeatSeparation(damper,.35,0));var raised=ElasticLaws.Spring(spring,ElasticLaws.SeatSeparation(damper,.35,.01));
            if(Math.Abs(raised.ForceN-initial.ForceN-294.1995)>1e-8) throw new Exception("Perch compression was counted incorrectly.");
            var mount=library.LoadComponent("installed-top-mount")!;var bearing=mount.Frames.Single(f=>f.Id=="bearing").Frame;
            mount=library.Publish(mount with {Adjustments=[new("camber-slot","Camber slot",new StraightAdjustmentPath(bearing with {OriginM=bearing.OriginM-new DesignVector(0,.02,0)},bearing with {OriginM=bearing.OriginM+new DesignVector(0,.02,0)},PathOrientation.HoldStart),new(.5),["bearing"],["bearing"],"Synthetic adjustable plate test")]},mount.Identity.RevisionId);
            var unadjusted=resolver.Compile(new(models.Gr86.Identity.AssetId));var sourceBundle=DesignJson.Serialize(AssemblyGraphResolver.Export(unadjusted));
            var shifted=GraphAdjustments.Freeze(unadjusted,[new ContinuousAdjustmentSelection("front/left/top-mount","camber-slot",1)]);
            var adjustedMotion=new TwinsKinematics(shifted).Solve(FourCornerTravel.Zero,0);
            if(Math.Abs(adjustedMotion.Wheels[0].CamberRad)<.01||Math.Abs(adjustedMotion.Wheels[1].CamberRad)>1e-7) throw new Exception("A locked camber plate did not change only its own solved wheel alignment.");
            if(DesignJson.Serialize(AssemblyGraphResolver.Export(unadjusted))!=sourceBundle||shifted.Components.Single(c=>c.Identity.AssetId==mount.Identity.AssetId).Frames.Single(f=>f.Id=="bearing").Frame.OriginM.Y!=0) throw new Exception("Runtime settings changed immutable source geometry.");
        }
        finally {Directory.Delete(folder,true);}
    }
}
