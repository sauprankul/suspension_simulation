using SuspensionSimulation.Design;
using SuspensionSimulation.Solvers.Design;

namespace SuspensionSimulation.Tests;

internal static class TwinsTopologyChecks
{
    internal static void Verify()
    {
        var repository=AppContext.BaseDirectory;while(!Directory.Exists(Path.Combine(repository,"data"))) repository=Directory.GetParent(repository)?.FullName??throw new Exception("Repository missing.");
        var folder=Path.Combine(Path.GetTempPath(),"twins-explicit-"+Guid.NewGuid().ToString("N"));
        try
        {
            var library=new DesignAssetLibrary(folder);var seed=TwinsTopologyCatalog.Seed(library,Path.Combine(repository,"data/synthetic/gr86/design-topology.json"));
            var graph=new AssemblyGraphResolver(library).Compile(new(seed.Vehicle.Identity.AssetId));var report=MechanismMobility.Inspect(graph,5);var system=new ConstraintMechanism(graph);
            if(report.DegreesOfFreedom!=5||!report.IsClosed||report.RedundantRows!=0||system.GaugeCount!=6) throw new Exception("Explicit topology rank failed: "+System.Text.Json.JsonSerializer.Serialize(report));
            foreach(var member in new[]{"rear-left/rear-traction","rear-left/rear-toe"})
            {
                var removed=graph with {Bodies=[..graph.Bodies.Where(b=>b.Path!=member)],Joints=[..graph.Joints.Where(j=>j.First.BodyPath!=member&&j.Second.BodyPath!=member)]};
                if(MechanismMobility.Inspect(removed).DegreesOfFreedom!=6) throw new Exception("Rear member did not constrain an independent operating freedom: "+member);
            }
            string[] paths=["front/left/upright","front/right/upright","rear-left/upright","rear-right/upright"];
            var baseline=new double[system.CoordinateCount];var heights=paths.Select(p=>system.Point(p,"wheel",baseline).Z).ToArray();
            MechanismDriver[] Drivers(double bump,double steering)
            {
                var drivers=paths.Select((p,i)=>new MechanismDriver(p,"wheel",new(0,0,1),heights[i]+bump)).ToList();
                drivers.Add(new("front/rack","origin",new(0,1,0),steering/450*seed.RackTravelAt450DegreesM));return drivers.ToArray();
            }
            var neutral=system.Solve(Drivers(0,0));if(!neutral.Converged) throw new Exception("Neutral pose is singular.");
            MechanismPose pose=neutral;
            foreach(var bumpMm in Enumerable.Range(0,21).Select(i=>i*2.5).Concat(Enumerable.Range(0,41).Select(i=>50-i*2.5)).Concat(Enumerable.Range(0,21).Select(i=>-50+i*2.5)))
            {
                pose=system.Solve(Drivers(bumpMm/1000,0),pose);if(!pose.Converged||pose.MaximumScaledResidual>1e-9) throw new Exception($"Explicit travel failed at {bumpMm} mm, residual {pose.MaximumScaledResidual}.");
                foreach(var path in paths) if(Math.Abs(system.Point(path,"wheel",pose.Coordinates).Z-(heights[Array.IndexOf(paths,path)]+bumpMm/1000))>1e-8) throw new Exception("Driven wheel height differs from solved pose.");
            }
            foreach(var steer in Enumerable.Range(0,19).Select(i=>i*25).Concat(Enumerable.Range(0,37).Select(i=>450-i*25)))
            {
                pose=system.Solve(Drivers(0,steer),pose);if(!pose.Converged) throw new Exception($"Rack steering failed at {steer} degrees (residual {pose.MaximumScaledResidual}, iterations {pose.Iterations}).");
                var a=system.Point("front/rack","left",pose.Coordinates);var b=system.Point("front/rack","right",pose.Coordinates);
                if(Math.Abs((a.Y-b.Y)-.68)>1e-8) throw new Exception("Rack inner points did not translate together.");
            }
            var shock=system.Point("rear-left/lower","shock-lower",pose.Coordinates);var lower=system.Point("rear-left/upright","lower",pose.Coordinates);
            if((shock-lower).Length<.05) throw new Exception("Rear shock mount collapsed to the legacy ball-joint alias.");
            var missing=graph.Root with {Identity=graph.Root.Identity with {AssetId="bad-explicit-root"},GroundedInstances=["missing-chassis"]};
            try {new AssemblyGraphResolver(library).CompileDraft(missing);throw new Exception("Missing required topology was accepted.");}catch(InvalidOperationException) { }
            // A matching initial residual is insufficient when operating drivers are missing.
            if(system.Solve(Drivers(0,0).Take(4).ToArray()).Converged) throw new Exception("An underdriven graph was reported converged.");
            var kinematics=new TwinsKinematics(graph,seed.RackTravelAt450DegreesM);var steered=kinematics.Solve(new(.015,-.01,.02,-.015),225);
            if(steered.Wheels.Any(w=>!double.IsFinite(w.CamberRad)||!double.IsFinite(w.HeadingRad))||steered.Front.ThrustAngleRad<=0) throw new Exception("Steering/alignment outputs have inconsistent signs.");
            var symmetric=kinematics.Solve(new(.025,.025,.025,.025),0);
            if(Math.Abs(symmetric.Wheels[0].CamberRad-symmetric.Wheels[1].CamberRad)>1e-7||Math.Abs(symmetric.Front.ThrustAngleRad)>1e-7||Math.Abs(symmetric.Rear.ThrustAngleRad)>1e-7) throw new Exception("Mirrored alignment is asymmetric.");
        }
        finally {Directory.Delete(folder,true);}
    }
}
