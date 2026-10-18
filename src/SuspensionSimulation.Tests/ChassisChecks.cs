using SuspensionSimulation.Design;
using SuspensionSimulation.Solvers.Design;

namespace SuspensionSimulation.Tests;

internal static class ChassisChecks
{
    internal static void Verify(bool solve=true)
    {
        var repository=AppContext.BaseDirectory;while(!Directory.Exists(Path.Combine(repository,"data"))) repository=Directory.GetParent(repository)?.FullName??throw new Exception("Repository missing.");
        var folder=Path.Combine(Path.GetTempPath(),"chassis-check-"+Guid.NewGuid().ToString("N"));
        try
        {
            var library=new DesignAssetLibrary(folder);var catalog=new PhysicalGarageCatalog(library,repository);var setup=catalog.Create("Chassis proof");var resolver=new GarageSetupResolver(library);var graph=resolver.Resolve(setup);
            var install=graph.Root.Vehicle!.Chassis??throw new Exception("Stock car has no complete chassis.");var chassis=library.LoadComponent(graph.Bodies.Single(b=>b.Path==install.ComponentPath).Component.Identity.AssetId)!;var s=chassis.Chassis!;
            if(chassis.Kind!=ComponentKind.Chassis||!chassis.VisualEdges.Any(e=>e.Id=="rack-crossmember")||s.RackSetbackM<=0||setup.Equipment.Length!=0||setup.Adjustments.Length!=0) throw new Exception("Chassis rails/crossmember or scoped car configuration is missing.");
            var beam=new ChassisBeam(graph);beam.Set(new(.002,-.003,.004));var expected=.5/s.LengthM*(s.TorsionalRigidityNm2*.002*.002+s.VerticalBendingRigidityNm2*.003*.003+s.LateralBendingRigidityNm2*.004*.004);
            if(Math.Abs(beam.EnergyJ-expected)>1e-10) throw new Exception("Beam energy does not use authored EI/GJ and length.");
            var previous=beam.MaterialPoint(new(s.FrontAxleXM-s.LengthM,0,s.RailHeightM));var arc=0d;const int pieces=2000;
            for(var i=1;i<=pieces;i++) {var next=beam.MaterialPoint(new(s.FrontAxleXM-s.LengthM+s.LengthM*i/pieces,0,s.RailHeightM));arc+=(next-previous).Length;previous=next;}
            if(Math.Abs(arc-s.LengthM)>1e-9) throw new Exception("Chassis bending introduced axial extension/compression.");
            var guide=graph.Bodies.Single(b=>b.Path=="front/guide");var guideOrigin=guide.Transform.Point(guide.Component.Points.Single(p=>p.Id=="origin").PositionM);
            if((beam.SectionPoint(ChassisSection.Rack,guideOrigin)-beam.MaterialPoint(new(s.FrontAxleXM-s.RackSetbackM,0,s.RackHeightM))).Length>1e-10) throw new Exception("Rack guide is not on the deformed crossmember.");
            var portable=AssemblyGraphResolver.Export(graph);var rates=DesignJson.Serialize(setup.ParameterValues);
            var longer=ChassisAuthoring.Create(chassis.Identity,s with {LengthM=s.LengthM+.2},chassis.Mass);var revision=library.Publish(longer,chassis.Identity.RevisionId);
            var longGraph=resolver.Resolve(setup);var neutral=new TwinsKinematics(longGraph).Solve(FourCornerTravel.Zero,0);
            if(Math.Abs(neutral.Wheels.Take(2).Average(w=>w.CenterM.X)-neutral.Wheels.Skip(2).Average(w=>w.CenterM.X)-s.LengthM-.2)>1e-8||DesignJson.Serialize(setup.ParameterValues)!=rates) throw new Exception("Axle spacing did not follow the source chassis length or car values changed.");
            var frozen=AssemblyGraphResolver.OpenBundle(portable);if(frozen.Bodies.Single(b=>b.Path==install.ComponentPath).Component.Chassis!.LengthM!=s.LengthM) throw new Exception("Frozen chassis source followed a new live revision.");
            try {DesignValidation.Validate(chassis with {Parameters=[new("EI","Overridden stiffness","Pa",1,10)]});throw new Exception("Chassis property accepted an importer parameter override.");}catch(ArgumentException) { }
            var changedRack=library.Publish(ChassisAuthoring.Create(chassis.Identity,s with {RackSetbackM=s.RackSetbackM+.01,RackHeightM=s.RackHeightM+.005}),revision.Identity.RevisionId);
            var changedRackGraph=resolver.Resolve(setup);var rackKinematics=new TwinsKinematics(changedRackGraph);var rackMotion=rackKinematics.Solve(FourCornerTravel.Zero,0);
            if(!rackMotion.Pose.Converged||Math.Abs(rackMotion.Wheels[0].HeadingRad-neutral.Wheels[0].HeadingRad)<1e-6) throw new Exception("Source rack crossmember geometry did not solve the actual rigid tie rods.");
            var actualRack=rackKinematics.System.Point("front/rack","origin",rackMotion.Pose.Coordinates);if(Math.Abs(actualRack.X-(s.FrontAxleXM-s.RackSetbackM-.01))>1e-8||Math.Abs(actualRack.Z-(s.RackHeightM+.005))>1e-8) throw new Exception("Rack did not follow the authored crossmember.");
            library.Publish(chassis,changedRack.Identity.RevisionId);
            Console.WriteLine("Chassis contracts: visible rails/rack mount, arc-length constraint, EI/GJ energy, length-driven axle spacing, source ownership and frozen history passed.");
            if(!solve) return;
            var solver=new InstalledEquilibrium(resolver.Resolve(setup));var input=new VehicleStaticInput(setup.Mass);var gravity=solver.Solve(input);
            var loaded=solver.Solve(input with {LateralG=.2,LongitudinalG=.05},gravity);
            if(gravity.Motion.ChassisFlex is null||gravity.ChassisStrainEnergyJ<=0||loaded.Motion.ChassisFlex!.Values.Max(Math.Abs)<1e-9||loaded.ForceResidualN.Length>.05||loaded.MomentResidualNm.Length>.05) throw new Exception("Chassis beam did not participate in accepted equilibrium.");
            var soft=library.Publish(ChassisAuthoring.Create(chassis.Identity,s with {VerticalBendingRigidityNm2=s.VerticalBendingRigidityNm2*.4,LateralBendingRigidityNm2=s.LateralBendingRigidityNm2*.4,TorsionalRigidityNm2=s.TorsionalRigidityNm2*.4}),library.LoadComponent(chassis.Identity.AssetId)!.Identity.RevisionId);
            var softened=new InstalledEquilibrium(resolver.Resolve(setup)).Solve(input with {LateralG=.2,LongitudinalG=.05},loaded);
            if(softened.Motion.ChassisFlex!.Values.Zip(loaded.Motion.ChassisFlex.Values,(a,b)=>Math.Abs(a-b)).Max()<1e-6||softened.Wheels.Zip(loaded.Wheels,(a,b)=>Math.Abs(a.CamberRad-b.CamberRad)+Math.Abs(a.HeadingRad-b.HeadingRad)).Max()<1e-7) throw new Exception("Changing source stiffness did not change actual solved chassis/alignment.");
            var output=Path.Combine(repository,"artifacts","chassis");Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"equilibrium.json"),DesignJson.Serialize(new {gravity,loaded,softened,source=chassis,soft=soft.Identity}));
            Console.WriteLine($"Chassis equilibrium passed: gravity energy {gravity.ChassisStrainEnergyJ:G6}J; loaded flex {DesignJson.Serialize(loaded.Motion.ChassisFlex)}; soft flex {DesignJson.Serialize(softened.Motion.ChassisFlex)}.");
        }
        finally {if(Directory.Exists(folder)) Directory.Delete(folder,true);}
    }
}
