using SuspensionSimulation.Design;
using SuspensionSimulation.Solvers.Design;

namespace SuspensionSimulation.Tests;

internal static class InstalledEquilibriumChecks
{
    internal static void Verify()
    {
        var repository=AppContext.BaseDirectory;while(!Directory.Exists(Path.Combine(repository,"data"))) repository=Directory.GetParent(repository)?.FullName??throw new Exception("Repository missing.");
        var folder=Path.Combine(Path.GetTempPath(),"equilibrium-twins-"+Guid.NewGuid().ToString("N"));
        try
        {
            var library=new DesignAssetLibrary(folder);var models=TwinsInstalledCatalog.Seed(library,repository);var resolver=new AssemblyGraphResolver(library);var graph=resolver.Compile(new(models.Gr86.Identity.AssetId));
            var input=new VehicleStaticInput(new(MassAccountingPolicy.AggregateBase,1360.778,new(.0515,0,.46),[]));var solver=new InstalledEquilibrium(graph);var gravity=solver.Solve(input);
            if(gravity.ForceResidualN.Length>.05||gravity.MomentResidualNm.Length>.05||gravity.Wheels.Any(w=>Math.Abs(w.CenterM.Z-.31)>1e-8)||gravity.NormalLoadsN.Any(n=>n<=0)) throw new Exception("Gravity contact/force/moment closure failed.");
            if(gravity.Chassis.HeaveM>=0||gravity.CenterOfMassWorldM.Z>=.46||gravity.RideHeights.Any(h=>h.HeightM>=.15)) throw new Exception("Reference pose was used instead of installed-length static sag.");
            if(Math.Abs(gravity.Wheels[0].CamberRad-gravity.Wheels[1].CamberRad)>1e-7||Math.Abs(gravity.Wheels[2].CamberRad-gravity.Wheels[3].CamberRad)>1e-7) throw new Exception("Static mirrored alignment differs.");
            var loaded=solver.Solve(input with {LateralG=.3},gravity);
            if(loaded.Chassis.RollRad<=0||loaded.NormalLoadsN[1]<=loaded.NormalLoadsN[0]||loaded.ForceResidualN.Length>.05||loaded.MomentResidualNm.Length>.05) throw new Exception("Loaded equilibrium has incorrect roll/load-transfer signs.");
            var lifted=solver.Solve(input with {LateralG=1.3},loaded);
            if(lifted.NormalLoadsN.Any(n=>n<0)||lifted.NormalLoadsN.All(n=>n>1)||!lifted.Wheels.Where((w,i)=>lifted.NormalLoadsN[i]==0).Any(w=>w.CenterM.Z>.310001)) throw new Exception("Unilateral contact did not release a lifted tire.");
            var frontDamper=library.LoadComponent("installed-front-damper")!;var originalDamper=(AxialDamperBehavior)frontDamper.Behavior!;
            var datum=AssemblyAuthoring.IdentityPlacement.Frame;
            var adjustable=library.Publish(frontDamper with {Behavior=originalDamper with {SpringSeats=originalDamper.SpringSeats! with {PerchAdjustmentId="perch"}},Adjustments=[new("perch","Physical perch",new ThreadedAdjustmentPath(datum,0,.02,.04),new(.5),[],[],"Synthetic perch test")]},frontDamper.Identity.RevisionId);
            var raisedGraph=GraphAdjustments.Freeze(resolver.Compile(new(models.Gr86.Identity.AssetId)),[new ContinuousAdjustmentSelection("FL-damper","perch",1),new ContinuousAdjustmentSelection("FR-damper","perch",1)]);
            var raised=new InstalledEquilibrium(raisedGraph).Solve(input,gravity);
            if(raised.RideHeights.Where(h=>h.Id.StartsWith("front",StringComparison.Ordinal)).Average(h=>h.HeightM)<gravity.RideHeights.Where(h=>h.Id.StartsWith("front",StringComparison.Ordinal)).Average(h=>h.HeightM)+.005) throw new Exception("Perch adjustment did not raise the rebalanced front body.");
            var staticForceChange=raised.Mechanics.Elements.Single(e=>e.Path=="FL-spring").ForceOrTorque-gravity.Mechanics.Elements.Single(e=>e.Path=="FL-spring").ForceOrTorque;
            if(Math.Abs(staticForceChange)>250) throw new Exception("Fixed-length preload increment was incorrectly added to static spring force.");
            var stoppedDamper=library.Publish(frontDamper with {Behavior=originalDamper with {MinimumEyeLengthM=.52}},adjustable.Identity.RevisionId);
            var stopSolver=new InstalledEquilibrium(resolver.Compile(new(models.Gr86.Identity.AssetId)));var stopped=stopSolver.Solve(input,gravity);
            if(!stopped.RigidReactions.Any(r=>r.Id.Contains("FL-damper:bottom",StringComparison.Ordinal)&&r.ForceN>0&&Math.Abs(r.GapM)<1e-8)) throw new Exception("Damper bottom-out did not solve a rigid reaction.");
            var heavier=stopSolver.Solve(input with {Mass=input.Mass with {AggregateBaseMassKg=1460.778}},stopped);
            if(Math.Abs(heavier.Motion.Travel.FrontLeftM-stopped.Motion.Travel.FrontLeftM)>1e-7||heavier.RigidReactions.Where(r=>r.Id.EndsWith(":bottom",StringComparison.Ordinal)).Sum(r=>r.ForceN)<=stopped.RigidReactions.Where(r=>r.Id.EndsWith(":bottom",StringComparison.Ordinal)).Sum(r=>r.ForceN)) throw new Exception("Increasing load penetrated a rigid stop instead of increasing its reaction.");
            library.Publish(frontDamper,stoppedDamper.Identity.RevisionId);
            var frontSpring=library.LoadComponent("installed-gr86-front-spring")!;var originalSpring=(CompressionSpringBehavior)frontSpring.Behavior!;
            var bindLength=gravity.Mechanics.Elements.Single(e=>e.Path=="FL-spring").SpringSeatLengthM!.Value+.005;
            var boundSpring=library.Publish(frontSpring with {Behavior=originalSpring with {BlockLengthM=bindLength}},frontSpring.Identity.RevisionId);
            var bound=new InstalledEquilibrium(resolver.Compile(new(models.Gr86.Identity.AssetId))).Solve(input,gravity);
            if(!bound.RigidReactions.Any(r=>r.Id.EndsWith(":coil-bind",StringComparison.Ordinal)&&r.ForceN>0&&Math.Abs(r.GapM)<1e-8)) throw new Exception("Coil bind was not a rigid active constraint.");
            library.Publish(frontSpring with {Behavior=originalSpring with {FreeLengthM=.65}},boundSpring.Identity.RevisionId);
            var topped=new InstalledEquilibrium(resolver.Compile(new(models.Gr86.Identity.AssetId))).Solve(input,gravity);
            if(!topped.RigidReactions.Any(r=>r.Id.EndsWith(":top",StringComparison.Ordinal)&&r.ForceN>0&&Math.Abs(r.GapM)<1e-8)) throw new Exception("Top-out did not arrest travel with a unilateral reaction.");
            using var cancellation=new CancellationTokenSource();cancellation.Cancel();try {solver.Solve(input,cancellation:cancellation.Token);throw new Exception("Canceled solve continued.");}catch(OperationCanceledException) { }
            Directory.CreateDirectory(Path.Combine(repository,"artifacts","installed-equilibrium"));
            File.WriteAllText(Path.Combine(repository,"artifacts","installed-equilibrium","gravity.json"),System.Text.Json.JsonSerializer.Serialize(gravity,new System.Text.Json.JsonSerializerOptions {WriteIndented=true}));
            File.WriteAllText(Path.Combine(repository,"artifacts","installed-equilibrium","loaded.json"),System.Text.Json.JsonSerializer.Serialize(loaded,new System.Text.Json.JsonSerializerOptions {WriteIndented=true}));
            File.WriteAllText(Path.Combine(repository,"artifacts","installed-equilibrium","lifted.json"),System.Text.Json.JsonSerializer.Serialize(lifted,new System.Text.Json.JsonSerializerOptions {WriteIndented=true}));
            File.WriteAllText(Path.Combine(repository,"artifacts","installed-equilibrium","limits.json"),System.Text.Json.JsonSerializer.Serialize(new {stopped,heavier,bound,topped},new System.Text.Json.JsonSerializerOptions {WriteIndented=true}));
        }
        finally {Directory.Delete(folder,true);}
    }
}
